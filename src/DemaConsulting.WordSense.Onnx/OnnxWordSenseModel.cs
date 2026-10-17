namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The shared implementation of the ONNX encoder models: model file management, the ONNX session and the
///     classifier it creates.
/// </summary>
/// <remarks>
///     <para>
///     A concrete model (<c>MiniLMWordSenseModel</c>, <c>BertBaseCasedWordSenseModel</c>) supplies the model identity: the
///     files with their SHA-256 digests, the vocabulary style and the trained weights embedded in its own assembly. The
///     constructor is not accessible outside the WordSense assemblies, so this class is extended only by the packages of
///     this repository (<c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c>); use
///     those packages, or the contracts <see cref="IWordSenseModel"/> and <see cref="IWordSenseClassifier"/>, rather
///     than deriving from it.
///     </para>
///     <para>
///     Call <see cref="EnsureModelAsync"/> once, then <see cref="Create"/>. The model files come from the directory
///     given to the model's constructor (every file verified against its pinned SHA-256, with no network access) or,
///     when none was given, from the per-user store, which a mirror or the GitHub copy of the repository fills on first
///     use. Concurrent <see cref="EnsureModelAsync"/> calls share one in-flight download (the mirror of a later call is
///     ignored while one is running), and a failed or cancelled call can be repeated. Cancelling the token only stops
///     that caller from waiting: the shared work continues, and a download in progress writes to temporary files that
///     are never visible under a final file name, so a cancelled or failed download leaves no partial model file.
///     <see cref="Create"/> may be called repeatedly after <see cref="EnsureModelAsync"/> has completed; every call
///     returns a classifier that shares one ONNX session and one set of weights, and the method is thread-safe.
///     The ONNX session and the weights are loaded on the first <see cref="Create"/> call. Dispose the model once,
///     after every classifier call has finished: disposing while a classification is running is not supported.
///     </para>
///     <para>
///     A classifier returns a confidence between 0 and 1 for each word it resolved. For a word with one sense it is 1.
///     For a word with several senses it is a monotonic mapping of the margin between the best and the runner-up
///     sense, relative to the abstain margin trained for that word's lemma, clamped to 0 to 1: 0.5 means the margin
///     equals the abstain margin. It is a ranking score, not a calibrated probability, and its scale belongs to the
///     model: never reuse a threshold from the Basic model (a probability) or from the other encoder; choose a
///     threshold for your use (the evaluation in the repository's build notes uses 0.5) and treat lower values as
///     uncertain.
///     </para>
///     <para>
///     <see cref="WordSense.Details"/> is null (with confidence 0) in exactly two cases: the word is not in the
///     lexicon, or it has several senses and the model cannot score it (its lemma has no intact trained weights, or
///     the encoder produced no vector for the word). The shipped MiniLM and BertBaseCased packages have intact
///     weights for every lemma (a test asserts that none is dropped), so in practice only unknown words are null.
///     A word with several senses and a low confidence still has its best sense in <see cref="WordSense.Details"/>.
///     For example, in <c>["They", "will", "record", "the", "record", "."]</c> the word <c>"the"</c> and the period
///     are unknown (null, 0), while <c>"record"</c> has several senses and is scored: its best sense is returned with
///     the confidence of the model, however low.
///     </para>
///     <para>
///     Input: split punctuation into elements of their own and keep contractions (<c>don't</c>) and hyphenated words
///     as one token, as the evaluation did; classify one sentence per call, because the words are joined by single
///     spaces and encoded as one text. The tokenizer lower-cases for an uncased model, splits each word into WordPieces
///     and handles punctuation inside a word itself. A text of at most <c>maxSequenceLength</c> word pieces (128 by
///     default, including the two special tokens; 8 to 512 allowed) is encoded once; a longer one is encoded per target
///     word from a window of that size around the word, so nothing is truncated away from the target but distant context
///     is lost.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent): <see cref="EnsureModelAsync"/> reads and hashes each model
///     file; the first <see cref="Create"/> opens the ONNX session (about 0.4 s for MiniLM, about 0.5 s for
///     BertBaseCased) and loads the weights; the word lexicon (about 120 ms, about 43 MiB) loads on first use. After
///     <see cref="Dispose()"/>, <see cref="EnsureModelAsync"/> and <see cref="Create"/> throw
///     <see cref="ObjectDisposedException"/>, as does a classification that has to run the encoder on a classifier
///     created earlier.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Onnx;
///
///     // Works with any ONNX encoder model, for example the MiniLM or BertBaseCased model.
///     static async Task&lt;WordSense[]&gt; ClassifyAsync(OnnxWordSenseModel model, string[] words)
///     {
///         await model.EnsureModelAsync();
///         IWordSenseClassifier classifier = model.Create();
///         return await classifier.ClassifyAsync(words);
///     }
///     </code>
/// </example>
public abstract class OnnxWordSenseModel : IWordSenseModel, IDisposable
{
    private readonly ModelSpec _spec;
    private readonly string? _modelDirectory;
    private readonly int _maxSequenceLength;
    private readonly object _gate = new();
    private Task? _ensure;
    private string? _directory;
    private OnnxEncoder? _encoder;
    private WeightedLemmaTable? _table;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxWordSenseModel"/> class.
    /// </summary>
    /// <param name="spec">The model description.</param>
    /// <param name="modelDirectory">A directory that holds the model files, or null to use the per-user store.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    private protected OnnxWordSenseModel(ModelSpec spec, string? modelDirectory, int maxSequenceLength)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSequenceLength, 512);
        _spec = spec;
        _modelDirectory = modelDirectory is null ? null : Path.GetFullPath(modelDirectory);
        _maxSequenceLength = maxSequenceLength;
    }

    /// <summary>
    ///     Gets or sets the number of threads ONNX Runtime may use within one inference; 0 lets it decide.
    /// </summary>
    internal int IntraOpThreads { get; set; }

    /// <summary>
    ///     Gets or sets the HTTP client used for downloads, or null to create one; set by tests.
    /// </summary>
    internal HttpClient? HttpClient { get; set; }

    /// <summary>
    ///     Gets or sets the GitHub LFS base location used for downloads, or null for the default; set by tests.
    /// </summary>
    internal Uri? GitHubBaseUri { get; set; }

    /// <summary>
    ///     Gets or sets the environment reader used to find a mirror, or null for the process environment; set by tests.
    /// </summary>
    internal Func<string, string?>? GetEnvironmentVariable { get; set; }

    /// <summary>
    ///     Makes sure every file of the model is available and verified: from the directory given to the constructor,
    ///     or from the per-user store, downloading what is missing.
    /// </summary>
    /// <param name="mirror">
    ///     A mirror to try before the public source; it takes precedence over the <c>WORDSENSE_MIRROR_*</c> environment
    ///     variables. Pass <see langword="null"/> to use the environment's mirror, if any. A mirror passed while an
    ///     earlier call is still running is ignored, and a mirror is never used when a model directory was supplied.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the wait of this call.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     When a model directory was supplied, this call is still required and no network access happens: every file
    ///     must exist directly in that directory (a flat layout; there is no <c>{model-id}</c> subfolder) and match its
    ///     pinned SHA-256, and all files of the model are verified (for BertBaseCased that includes
    ///     <c>config.json</c> and <c>tokenizer.json</c>, although the runtime reads only the ONNX file and
    ///     <c>vocab.txt</c>). Otherwise valid files in the store (<c>{StoreRoot}/{ModelId}/</c>, by default under the
    ///     per-user local application data folder: <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows,
    ///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS, or beneath <c>$XDG_DATA_HOME</c>
    ///     when that variable is set) are reused and only missing or invalid files are
    ///     downloaded (mirror first, then the public source); a file that is already valid never contacts the mirror.
    ///     Every existing file is hashed on each process start, so the call costs time proportional to the file sizes.
    ///     Calling the method again after it completed is cheap and harmless. Cancelling stops only this caller from
    ///     waiting; the shared work continues, and a cancelled or failed download leaves no partial file under a final
    ///     file name, so the call can simply be repeated. The only environment variables are the four
    ///     <c>WORDSENSE_MIRROR_*</c> mirror variables; none of them sets the store location, which only
    ///     <see cref="MirrorOptions.StoreRoot"/> changes (and a <see cref="MirrorOptions"/> needs a mirror base URI).
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid (including <see cref="ArgumentOutOfRangeException"/> for an invalid timeout).</exception>
    /// <exception cref="FileNotFoundException">Thrown when a supplied model directory lacks a model file (or does not exist).</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained and verified from any source.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ensure is null || _ensure.IsFaulted || _ensure.IsCanceled)
            {
                // Run on the thread pool so that hashing and file work never block the caller or the gate
                _ensure = Task.Run(() => EnsureCoreAsync(mirror), CancellationToken.None);
            }

            task = _ensure;
        }

        // The shared work is not cancelled by one caller, but each caller may stop waiting
        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a classifier on this model; call <see cref="EnsureModelAsync"/> first.
    /// </summary>
    /// <returns>
    ///     A classifier that shares this model's ONNX session and weights. Calling the method again is allowed, safe
    ///     from several threads, and returns another classifier on the same shared resources.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="EnsureModelAsync"/> has not completed.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the embedded weights are corrupt or the ONNX model cannot be read.</exception>
    public IWordSenseClassifier Create()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var directory = _directory ?? throw new InvalidOperationException("The model files are not available; call EnsureModelAsync first.");
            _table ??= WeightedLemmaTable.LoadEmbedded(_spec.WeightsAssembly, _spec.WeightsResourceName, _spec.ModelId, _spec.HiddenSize);
            _encoder ??= OpenEncoderCore(directory);
            return new OnnxClassifier(_encoder, _table);
        }
    }

    /// <summary>
    ///     Gets the hidden size (feature dimension) of the encoder; known before <see cref="EnsureModelAsync"/>.
    /// </summary>
    public int HiddenSize => _spec.HiddenSize;

    /// <summary>
    ///     Gets the identifier of the model, which names its folder in the repository, on a mirror and in the store.
    /// </summary>
    public string ModelId => _spec.ModelId;

    /// <summary>
    ///     Releases the ONNX session. Calling it more than once is harmless.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Releases the ONNX session.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _encoder?.Dispose();
            _encoder = null;
        }
    }

    /// <summary>
    ///     Opens a new encoder on the verified model files; the caller owns it.
    /// </summary>
    /// <returns>The encoder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the model files are not available.</exception>
    internal OnnxEncoder OpenEncoder()
    {
        string directory;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            directory = _directory ?? throw new InvalidOperationException("The model files are not available; call EnsureModelAsync first.");
        }

        return OpenEncoderCore(directory);
    }

    /// <summary>
    ///     Gets the description of this model.
    /// </summary>
    internal ModelSpec Spec => _spec;

    /// <summary>
    ///     Makes the model files available and verified.
    /// </summary>
    /// <param name="mirror">The mirror, or null.</param>
    /// <returns>A task that completes when the files are ready.</returns>
    private async Task EnsureCoreAsync(MirrorOptions? mirror)
    {
        string directory;
        if (_modelDirectory is not null)
        {
            // An explicit directory is used as is: no network access, every file verified
            ModelDownloader.Verify(_spec, _modelDirectory);
            directory = _modelDirectory;
        }
        else
        {
            directory = await ModelDownloader.EnsureAsync(_spec, mirror, CancellationToken.None, HttpClient, GitHubBaseUri, GetEnvironmentVariable).ConfigureAwait(false);
        }

        lock (_gate)
        {
            _directory = directory;
        }
    }

    /// <summary>
    ///     Opens an encoder on a verified directory.
    /// </summary>
    /// <param name="directory">The directory that holds the model files.</param>
    /// <returns>The encoder.</returns>
    private OnnxEncoder OpenEncoderCore(string directory) => new(
        Path.Combine(directory, _spec.OnnxFileName),
        Path.Combine(directory, _spec.VocabularyFileName),
        _spec.LowerCase,
        _maxSequenceLength,
        IntraOpThreads);
}
