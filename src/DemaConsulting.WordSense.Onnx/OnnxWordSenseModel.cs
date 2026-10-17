namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The shared implementation of the ONNX encoder models: model file management, the ONNX session and the
///     classifier it creates.
/// </summary>
/// <remarks>
///     <para>
///     A concrete model (<c>MiniLMWordSenseModel</c>, <c>BertBaseCasedWordSenseModel</c>) supplies the model identity: the
///     files with their SHA-256 digests, the vocabulary style and the trained weights embedded in its own assembly. This
///     class cannot be derived from outside the WordSense packages.
///     </para>
///     <para>
///     Call <see cref="EnsureModelAsync"/> once, then <see cref="Create"/>. The model files come from the directory
///     given to the model's constructor (verified, with no network access) or, when none was given, from the per-user
///     store, which a mirror or the GitHub copy of the repository fills on first use. Concurrent
///     <see cref="EnsureModelAsync"/> calls share one download, and a failed call can be repeated.
///     The ONNX session and the weights are loaded on the first <see cref="Create"/> call and shared by every classifier
///     the model creates. Dispose the model after every classifier call has finished.
///     </para>
/// </remarks>
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

    /// <inheritdoc />
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ensure is null || _ensure.IsFaulted || _ensure.IsCanceled)
            {
                _ensure = EnsureCoreAsync(mirror);
            }

            task = _ensure;
        }

        // The shared work is not cancelled by one caller, but each caller may stop waiting
        return task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
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
