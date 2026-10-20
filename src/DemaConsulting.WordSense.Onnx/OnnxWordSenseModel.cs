namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The shared implementation of the ONNX encoder models: model file management and the answers the encoder model
///     gives.
/// </summary>
/// <remarks>
///     <para>
///     A concrete model supplies the model identity: the files with their SHA-256 digests, the vocabulary style and the
///     trained weights embedded in its own assembly. The constructor is not accessible outside the WordSense assemblies,
///     so this class is extended only by the packages of this repository (<c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c>, which wrap it, composed with the Basic model as the fallback, in
///     their public models); use those packages, or the interface <see cref="IWordSenseModel"/>, rather than deriving
///     from it.
///     </para>
///     <para>
///     This is the encoder model and it is usable on its own: <see cref="ClassifyAsync"/> scores the fine senses of the
///     lemmas that have trained weights with the encoder and answers each word at the finest level it is confident in
///     (see <see cref="ClassifyResult"/> and <see cref="SenseLevel"/>). A word that is not in the lexicon is unknown, a
///     word with one analysis and one sense is answered at the <see cref="SenseLevel.Sense"/> level without any model
///     file, and every other known word whose senses are not scored (its lemma has no trained weights) is answered
///     from the lexicon at the <see cref="SenseLevel.PartOfSpeech"/> level with all its analyses equally likely and
///     without scored senses. Use the model composed with a fallback model that chooses the analysis of such words in
///     context (see <see cref="FallbackWordSenseModel"/>), as the MiniLM and BertBaseCased packages do with the Basic
///     model.
///     </para>
///     <para>
///     Call <see cref="EnsureModelAsync"/> to make the model files available and verified. <see cref="ClassifyAsync"/>
///     ensures them itself when a word needs the encoder, and answers every other text without them: a classification
///     of words that need no context never downloads a file, never opens an ONNX session and never fails because
///     a model file is missing. The model files come from the directory
///     given to the model's constructor (every file verified against its pinned SHA-256, with no network access) or,
///     when none was given, from the per-user store, which a mirror or the GitHub copy of the repository fills on first
///     use. Concurrent <see cref="EnsureModelAsync"/> calls share one in-flight ensure (the mirror of a later call is
///     ignored while one is running), and a failed call can be repeated. Cancelling a token only stops that caller from
///     waiting: its returned task is cancelled, but the shared ensure is not cancelled, it keeps running even when no
///     caller is waiting any more (only disposing the model cancels it), and the next call joins it while it runs
///     (its <see cref="MirrorOptions"/> are ignored), returns at once when it has succeeded, and starts a new ensure
///     with its own <see cref="MirrorOptions"/> only after a failed one. A download in progress writes to temporary
///     files that are never visible under a final file name, so a failed or abandoned download leaves no partial model file.
///     <see cref="ClassifyAsync"/> is thread-safe. The model owns the downloader state, so it is
///     <see cref="IDisposable"/> (through <see cref="IWordSenseModel"/>).
///     Recommendation: dispose it (for example with <c>using</c>) when you no longer need the model. Guarantee:
///     disposing, which is idempotent and thread-safe, is safe while calls are running. It cancels a download that
///     <see cref="EnsureModelAsync"/> has in flight, and the pending
///     task then throws <see cref="OperationCanceledException"/>; the hashing of files that are already in place (or
///     of a supplied directory) is not interrupted, so when no download is needed the pending ensure completes
///     normally even though the model is disposed (every later call throws). The model is marked disposed at once
///     (new calls throw <see cref="ObjectDisposedException"/>); a classification that already runs
///     completes normally, and the model's resources are released when the last of them finishes (at once when none
///     runs). <see cref="Dispose()"/> itself never waits for any of this.
///     Dispose never deletes files: the downloaded (or supplied) model files stay in the store or directory and are
///     reused, after hashing, by the next instance.
///     </para>
///     <para>
///     State of the ensure: the ensured state belongs to the instance, not to the process. After one successful
///     <see cref="EnsureModelAsync"/> the instance remembers the directory it verified (including the
///     <see cref="MirrorOptions.StoreRoot"/> of the <see cref="MirrorOptions"/> passed to that call), and later calls
///     reuse it without hashing again; a new instance starts not yet ensured and
///     hashes the files again. Only after a failed ensure does the next call start a new one (an ensure cancelled by
///     disposal is never restarted: every later call throws <see cref="ObjectDisposedException"/>). Each ensure is
///     shared by concurrent callers, and a <see cref="MirrorOptions"/> passed
///     while an ensure is already in flight is ignored. After the model is ensured, passing another mirror has no
///     effect.
///     </para>
///     <para>
///     Concurrency: <see cref="ClassifyAsync"/> calls are not serialized; they run in parallel. The ONNX models run on
///     the CPU only; there is no GPU option.
///     </para>
///     <para>
///     The classification returns one <see cref="ClassifyResult"/> per word, with its <see cref="ClassifyResult.Analyses"/>
///     (each with its probability and, for a trained lemma, its ranked senses), the selected
///     <see cref="ClassifyResult.Details"/>, a confidence between 0 and 1 and the <see cref="ClassifyResult.Level"/> at
///     which the answer was given; <see cref="ClassifyResult.Details"/> is null (level <see cref="SenseLevel.None"/>,
///     confidence 0) only when the word is not in the lexicon. For example, in
///     <c>["They", "will", "record", "the", "record", "."]</c> the word <c>"the"</c> has one analysis and one sense, the
///     period is unknown, and each <c>"record"</c> has its noun and verb analyses with the probabilities that the
///     context gives them (when the lemma <c>record</c> is not trained for this model, each has the probability 1/2 and
///     the first analysis of the lexicon is the selected one).
///     </para>
///     <para>
///     Input: split punctuation into elements of their own and keep contractions (<c>don't</c>) and hyphenated words
///     as one token; classify one sentence per call.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent): <see cref="EnsureModelAsync"/> reads and hashes each model
///     file (about 30 ms for the 22 MiB MiniLM files and about 110 ms for the 104 MiB BertBaseCased files, measured
///     with a warm file cache; a cold or slow disk takes longer); the first classification that needs the encoder opens
///     the ONNX session (about 0.4 s for MiniLM, about 0.5 s for BertBaseCased) and loads the weights; the word lexicon
///     (about 55 ms) loads on first use. After
///     <see cref="Dispose()"/>, <see cref="EnsureModelAsync"/> and <see cref="ClassifyAsync"/> throw
///     <see cref="ObjectDisposedException"/>: <see cref="EnsureModelAsync"/> throws it synchronously from the call,
///     while <see cref="ClassifyAsync"/> reports it through the returned task (an <c>await</c> inside a <c>try</c>
///     catches both). A disposed model reports <see cref="ObjectDisposedException"/> even when the token is already
///     cancelled, because the disposal check comes first.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     // Any ONNX encoder model works through the interface; here the MiniLM model.
///     static async Task&lt;ClassifyResult[]&gt; ClassifyAsync(IWordSenseModel model, string[] words)
///     {
///         await model.EnsureModelAsync();   // optional: fails early, or takes a MirrorOptions
///         return await model.ClassifyAsync(words);
///     }
///
///     using var model = new MiniLMWordSenseModel();
///     ClassifyResult[] results = await ClassifyAsync(model, ["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public abstract class OnnxWordSenseModel : IWordSenseModel
{
    private readonly ModelSpec _spec;
    private readonly string? _modelDirectory;
    private readonly int _maxSequenceLength;
    private readonly object _gate = new();
    private Task? _ensure;
    private string? _directory;
    private OnnxEncoder? _encoder;
    private OnnxClassifier? _classifier;
    private WeightedLemmaTable? _table;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private volatile bool _disposed;
    private readonly InFlightTracker _inFlight;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxWordSenseModel"/> class.
    /// </summary>
    /// <param name="spec">The model description.</param>
    /// <param name="modelDirectory">A directory that holds the model files, or null to use the per-user store.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="spec"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    private protected OnnxWordSenseModel(ModelSpec spec, string? modelDirectory, int maxSequenceLength)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSequenceLength, 512);
        _spec = spec;
        _modelDirectory = modelDirectory is null ? null : Path.GetFullPath(modelDirectory);
        _maxSequenceLength = maxSequenceLength;
        _disposeToken = _disposeCts.Token;
        _inFlight = new InFlightTracker(GetType().Name, ReleaseEncoder);
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
    /// <param name="cancellationToken">A token that cancels the wait of this call (the returned task is cancelled); the shared ensure itself is not cancelled by it.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     When a model directory was supplied, this call is optional but useful: it verifies the files up front, and no
    ///     network access happens (it throws <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/>
    ///     through the task when a file is missing or altered). Every file
    ///     must exist directly in that directory (a flat layout; there is no <c>{model-id}</c> subfolder) and match its
    ///     pinned SHA-256, and all files of the model are verified (for BertBaseCased that includes
    ///     <c>config.json</c> and <c>tokenizer.json</c>, although the runtime reads only the ONNX file and
    ///     <c>vocab.txt</c>). Otherwise valid files in the store (<c>{StoreRoot}/{ModelId}/</c>, by default under the
    ///     per-user local application data folder: <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows,
    ///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS, or beneath <c>$XDG_DATA_HOME</c>
    ///     when that variable is set) are reused and only missing or invalid files are
    ///     downloaded (mirror first, then the public source); a file that is already valid never contacts the mirror.
    ///     Every existing file is hashed once per model instance (the first ensure; later calls on that instance
    ///     return the remembered result), so the call costs time proportional to the file sizes.
    ///     Calling the method again after it completed is cheap and harmless. Cancelling a token stops only that caller
    ///     from waiting: the task returned to it is cancelled, but the shared ensure is not cancelled and keeps running
    ///     even when no caller is waiting any more (only disposing the model cancels it). The next call joins an ensure
    ///     that is still running (its <see cref="MirrorOptions"/> are ignored), returns at once when it has succeeded
    ///     in the meantime, and starts a new ensure with its own <see cref="MirrorOptions"/> only when the earlier one
    ///     failed; a failed download leaves no partial file under a final file name, so the call can simply be
    ///     repeated. A token that is already cancelled does not make the call throw: the call returns a cancelled task
    ///     (awaiting it throws <see cref="OperationCanceledException"/>), as the Basic model does, except that a
    ///     task that has already completed (the ensure succeeded earlier) is returned as it is; otherwise the shared ensure is still started (or joined) and
    ///     continues, although the task returned to this caller is cancelled. Only a disposed model throws from the call itself (even for an already cancelled token). The only environment variables are the four
    ///     <c>WORDSENSE_MIRROR_*</c> mirror variables; none of them sets the store location, which only
    ///     <see cref="MirrorOptions.StoreRoot"/> changes (and a <see cref="MirrorOptions"/> needs a mirror base URI;
    ///     a relative <see cref="MirrorOptions.StoreRoot"/> is resolved against the current directory of the process).
    ///     Offline route through the store: copy the model files into <c>{StoreRoot}/{ModelId}/</c> (or the default
    ///     store); files that match their pinned SHA-256 are kept and never requested, so nothing is downloaded when all
    ///     are valid.
    ///     Environment details: a blank variable counts as unset; user without password (or the reverse), or a user,
    ///     password or token without <c>WORDSENSE_MIRROR_URL</c>, throws <see cref="ArgumentException"/> through the
    ///     task, and so does a <c>WORDSENSE_MIRROR_URL</c> or <see cref="MirrorOptions.BaseUri"/> that is relative,
    ///     not http or https, or has a query, a fragment or user-info; the environment is not read at all when a model
    ///     directory was supplied (then an invalid setting is
    ///     ignored without an exception) or when a <see cref="MirrorOptions"/> is passed. A
    ///     <see cref="MirrorOptions.Timeout"/> that is zero, negative or longer than about 49 days throws
    ///     <see cref="ArgumentOutOfRangeException"/> (infinite is allowed); it is ignored when a model directory was
    ///     supplied (so no <see cref="ArgumentException"/> is thrown for an invalid <see cref="MirrorOptions"/> then). A download that exceeds the timeout is not
    ///     reported as an exception of its own: that source is discarded, the next is tried, and when none delivers the
    ///     file the task fails with <see cref="HttpRequestException"/>.
    ///     Network notes: downloads use <c>HttpClient</c> with the default proxy behavior of .NET (the system proxy
    ///     settings, and the <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c> and <c>NO_PROXY</c> variables on Linux and macOS),
    ///     with no proxy credentials of its own. Allow-list the host of your mirror, if any, and for the public source
    ///     <c>media.githubusercontent.com</c> (files at
    ///     https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/{model-id}/{file});
    ///     the library follows HTTP redirects itself (up to a fixed limit, http or https only), so a host that the public
    ///     source redirects to must be reachable too, and mirror credentials are sent only to the mirror's own origin.
    ///     Several processes (or instances) may share one store: each download goes to its own uniquely named temporary
    ///     file, is verified, and then moved into place, so a reader never sees a partial file and the processes at
    ///     worst download the same file twice. A file in the store that is missing or fails its SHA-256 check (corrupt,
    ///     truncated or tampered) is downloaded again and replaced; it is never used.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown (from the call itself) when the model has been disposed.</exception>
    /// <exception cref="ArgumentException">Reported through the returned task when the mirror configuration is invalid (including <see cref="ArgumentOutOfRangeException"/> for an invalid timeout).</exception>
    /// <exception cref="FileNotFoundException">Reported through the returned task when a supplied model directory lacks a model file (or does not exist).</exception>
    /// <exception cref="InvalidDataException">Reported through the returned task when a file of a supplied model directory does not match its pinned SHA-256.</exception>
    /// <exception cref="HttpRequestException">Reported through the returned task when a file could not be obtained and verified from any source.</exception>
    /// <exception cref="OperationCanceledException">Reported through the returned task (not thrown from the call, also not for an already cancelled token) when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed during a download.</exception>
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
    ///     Classifies every word of a tokenized sentence or text; the model files are ensured on first use.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>; empty for an empty list.</returns>
    /// <remarks>
    ///     An unknown word is answered with <see cref="ClassifyResult.Unknown"/>. A word with one analysis and one sense
    ///     is answered at the <see cref="SenseLevel.Sense"/> level with confidence 1. A word whose lemma has trained
    ///     weights and several scored senses is scored with the encoder: its analyses get their probabilities, each
    ///     lists its senses ranked by their probability, and the answer is at the finest level the lemma's thresholds
    ///     support (sense, group or part of speech). Every other known word (no trained weights) is answered from the
    ///     lexicon with all its analyses equally likely (probability 1/n for n analyses, the first analysis of the
    ///     lexicon is the selected one) at the <see cref="SenseLevel.PartOfSpeech"/> level and without scored senses,
    ///     which makes a composition use its fallback model for the word (see <see cref="FallbackWordSenseModel"/>).
    ///     A list in which no word needs the encoder is answered without any model file: nothing is ensured,
    ///     downloaded or verified, so it works before <see cref="EnsureModelAsync"/> and when the model directory does
    ///     not exist. A word that needs the encoder ensures the model lazily (with the default and environment mirror
    ///     settings; call <see cref="EnsureModelAsync"/> yourself first to pass a <see cref="MirrorOptions"/>), shared
    ///     with concurrent callers; a failed ensure can be retried by the next call. The method is
    ///     thread-safe. Every exception, including <see cref="ObjectDisposedException"/>, is reported through the
    ///     returned task, so an <c>await</c> in a <c>try</c> catches it; a disposed model wins over an already cancelled
    ///     token. Disposing while this call is running is safe: the call completes normally and the model's resources
    ///     are released after it (and every other running call) has finished. Threads and cancellation: the
    ///     call runs synchronously on the calling thread until the model files are needed (argument checks, the lexicon
    ///     load and the weights check), so offload the call yourself if the caller
    ///     must not block. The token is checked before the work starts, so an already canceled token cancels the call even for an empty list or
    ///     a list of single-sense words. An empty element is unknown and an element
    ///     containing a space is looked up as one key, which no lexicon entry has, so it is unknown too.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null, or by the lazy ensure when the mirror settings of the <c>WORDSENSE_MIRROR_*</c> environment variables are invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed during the lazy ensure.</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256 (lazy ensure), the embedded weights are corrupt or do not match the word image, or the ONNX model cannot be read.</exception>
    /// <exception cref="HttpRequestException">Thrown by the lazy ensure when a file could not be obtained and verified.</exception>
    /// <exception cref="FileNotFoundException">Thrown by the lazy ensure when a supplied model directory lacks a model file.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed, whatever the words.</exception>
    public async Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        // Honor an already canceled token before any work, whatever the words (a disposed model wins, above)
        cancellationToken.ThrowIfCancellationRequested();

        // A word needs the model only when its lemma has trained weights and several scored senses; the weights are
        // embedded in the assembly, so deciding this needs no model file, download or ONNX session
        if (!OnnxClassifier.NeedsModel(list, GetTable()))
        {
            return OnnxClassifier.ResolveWithoutModel(list, cancellationToken);
        }

        // Ambiguous words need the model: ensure it lazily (shared with concurrent callers), then open the resources
        await EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
        var classifier = AcquireClassifier();
        try
        {
            return await classifier.ClassifyAsync(list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseClassifier();
        }
    }

    /// <summary>
    ///     Gets whether the encoder (and with it the ONNX session) has been released or was never opened.
    /// </summary>
    internal bool IsEncoderReleased
    {
        get
        {
            lock (_gate)
            {
                return _encoder is null;
            }
        }
    }

    /// <summary>
    ///     Registers a running classification and returns the classifier; every call must be paired with
    ///     <see cref="ReleaseClassifier"/>. While registered, a disposal does not release the ONNX session.
    /// </summary>
    /// <returns>The classifier.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model files are not available.</exception>
    /// <exception cref="InvalidDataException">Thrown when the embedded weights are corrupt or the ONNX model cannot be read.</exception>
    internal OnnxClassifier AcquireClassifier()
    {
        _inFlight.Enter();
        try
        {
            return GetClassifier();
        }
        catch
        {
            _inFlight.Exit();
            throw;
        }
    }

    /// <summary>
    ///     Unregisters a classification registered by <see cref="AcquireClassifier"/>; after a disposal, the last one
    ///     to finish releases the ONNX session.
    /// </summary>
    internal void ReleaseClassifier() => _inFlight.Exit();

    /// <summary>
    ///     Releases the encoder; run once by the in-flight tracker, after the last running classification.
    /// </summary>
    private void ReleaseEncoder()
    {
        OnnxEncoder? encoder;
        lock (_gate)
        {
            encoder = _encoder;
            _encoder = null;
            _classifier = null;
        }

        encoder?.Dispose();
    }

    /// <summary>
    ///     Gets the embedded weights, loading and checking them on first use (no model file is needed).
    /// </summary>
    /// <returns>The table.</returns>
    private WeightedLemmaTable GetTable()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _table ??= WeightedLemmaTable.LoadEmbedded(_spec.WeightsAssembly, _spec.WeightsResourceName, _spec.ModelId, _spec.HiddenSize);
        }
    }

    /// <summary>
    ///     Gets the classifier, opening the encoder and loading the weights on first use.
    /// </summary>
    /// <returns>The classifier.</returns>
    private OnnxClassifier GetClassifier()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_classifier is null)
            {
                var directory = _directory ?? throw new InvalidOperationException("The model files are not available; call EnsureModelAsync first.");
                _table ??= WeightedLemmaTable.LoadEmbedded(_spec.WeightsAssembly, _spec.WeightsResourceName, _spec.ModelId, _spec.HiddenSize);
                _encoder ??= OpenEncoderCore(directory);
                _classifier = new OnnxClassifier(_table, _encoder.EncodeAsync);
            }

            return _classifier;
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
    ///     Cancels a download that <see cref="EnsureModelAsync"/> has in flight and releases the resources of the model.
    /// </summary>
    /// <remarks>
    ///     Calling it more than once, or from several threads at once, is harmless: the first call releases, the others
    ///     return. When a classification is running, the resources are released when the last running
    ///     classification finishes (they complete normally; new calls already throw); nothing here waits for them. It
    ///     does not wait for a cancelled download to unwind; the cancelled download leaves no partial model
    ///     file. Every pending <see cref="EnsureModelAsync"/> task that is downloading then ends with
    ///     <see cref="OperationCanceledException"/> (one that only has to hash files already in place completes
    ///     normally); calls made after disposal throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Releases the resources of the model and cancels a download in flight.
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
        }

        // Release now when no classification is running, otherwise when the last one finishes
        _inFlight.Close();

        // Cancel outside the gate; every callback has run when Cancel returns, so the source can then be disposed. A
        // download still holding the token (copied in the constructor) sees a cancelled token, never a disposed source
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    /// <summary>
    ///     Throws when the model has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
            try
            {
                directory = await ModelDownloader.EnsureAsync(_spec, mirror, _disposeToken, HttpClient, GitHubBaseUri, GetEnvironmentVariable).ConfigureAwait(false);
            }
            catch (Exception ex) when (_disposeToken.IsCancellationRequested && ex is not OperationCanceledException)
            {
                // Disposal cancelled the download; whatever the abort surfaced as, report one consistent outcome
                throw new OperationCanceledException("The model was disposed while its files were being ensured.", ex, _disposeToken);
            }
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
        Path.Join(directory, _spec.OnnxFileName),
        Path.Join(directory, _spec.VocabularyFileName),
        _spec.LowerCase,
        _maxSequenceLength,
        IntraOpThreads);
}
