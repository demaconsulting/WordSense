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
///     equals the abstain margin. It is a ranking score, not a calibrated probability; choose a threshold for your
///     use (the evaluation in the repository's build notes uses 0.5) and treat lower values as uncertain.
///     </para>
/// </remarks>
/// <example>
///     <code>
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
    ///     When a model directory was supplied, no network access happens: every file must exist there and match its
    ///     pinned SHA-256. Otherwise valid files in the store are reused and only missing or invalid files are
    ///     downloaded (mirror first, then the public source); a file that is already valid never contacts the mirror.
    ///     Calling the method again after it completed is cheap and harmless. Cancelling stops only this caller from
    ///     waiting; the shared work continues, and a cancelled or failed download leaves no partial file under a final
    ///     file name, so the call can simply be repeated.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a supplied model directory lacks a model file (or does not exist).</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained, verified, from any source.</exception>
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
