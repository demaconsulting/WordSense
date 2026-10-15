namespace DemaConsulting.WordSense;

/// <summary>
///     Reports the progress of a model download.
/// </summary>
/// <param name="FileName">The file being downloaded.</param>
/// <param name="Source">A short name of the source being tried: <c>mirror</c>, <c>huggingface</c> or <c>github-lfs</c>.</param>
/// <param name="BytesTransferred">Bytes received so far for the file.</param>
/// <param name="TotalBytes">The expected size when the server reported one, otherwise null.</param>
public sealed record WordSenseModelDownloadProgress(string FileName, string Source, long BytesTransferred, long? TotalBytes);

/// <summary>
///     Thrown when a model file could not be obtained from any download source.
/// </summary>
public sealed class WordSenseModelDownloadException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelDownloadException"/> class.
    /// </summary>
    public WordSenseModelDownloadException()
    {
        Failures = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelDownloadException"/> class with a message.
    /// </summary>
    /// <param name="message">Describes the failure.</param>
    public WordSenseModelDownloadException(string message)
        : base(message)
    {
        Failures = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelDownloadException"/> class with a message and cause.
    /// </summary>
    /// <param name="message">Describes the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public WordSenseModelDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelDownloadException"/> class with the per-source failures.
    /// </summary>
    /// <param name="message">Describes the failure.</param>
    /// <param name="failures">One entry per source that was tried, in the order tried.</param>
    public WordSenseModelDownloadException(string message, IReadOnlyList<string> failures)
        : base(message)
    {
        Failures = failures;
    }

    /// <summary>
    ///     Gets one description per source that was tried, in the order tried.
    /// </summary>
    public IReadOnlyList<string> Failures { get; }
}

/// <summary>
///     Options for <see cref="WordSenseModelDownloader"/>.
/// </summary>
/// <remarks>
///     Sources are tried in this order for every file: the mirror (when configured), Hugging Face (only
///     for files known to be identical upstream), then the GitHub Git LFS media endpoint of this
///     repository. Every download is verified against the pinned SHA-256; a request failure, timeout,
///     block or digest mismatch moves on to the next source.
/// </remarks>
public sealed class WordSenseModelDownloaderOptions
{
    /// <summary>
    ///     The default Hugging Face base location for the sentence-transformers model.
    /// </summary>
    public static readonly Uri DefaultHuggingFaceBaseUri =
        new("https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main");

    /// <summary>
    ///     The default GitHub Git LFS media base location of this repository (without reference or path).
    /// </summary>
    public static readonly Uri DefaultGitHubLfsBaseUri =
        new("https://media.githubusercontent.com/media/demaconsulting/WordSense");

    /// <summary>
    ///     Gets the mirror to try first, or null to fall back to <see cref="GetEnvironmentVariable"/>.
    /// </summary>
    /// <remarks>An explicitly supplied mirror always wins over the environment variables.</remarks>
    public DownloadMirror? Mirror { get; init; }

    /// <summary>
    ///     Gets the function used to read the mirror environment variables; null uses the process environment.
    /// </summary>
    public Func<string, string?>? GetEnvironmentVariable { get; init; }

    /// <summary>
    ///     Gets the Hugging Face base location, or null to skip Hugging Face. The file name is appended.
    /// </summary>
    public Uri? HuggingFaceBaseUri { get; init; } = DefaultHuggingFaceBaseUri;

    /// <summary>
    ///     Gets the GitHub Git LFS media base location, or null to skip it. <c>/{ref}/model/{file}</c> is appended.
    /// </summary>
    public Uri? GitHubLfsBaseUri { get; init; } = DefaultGitHubLfsBaseUri;

    /// <summary>
    ///     Gets the Git reference (branch, tag or commit) requested from the GitHub Git LFS endpoint.
    /// </summary>
    public string GitHubLfsRef { get; init; } = "main";

    /// <summary>
    ///     Gets the directory beneath which model folders are stored; null uses the per-user default.
    /// </summary>
    public string? StoreRoot { get; init; }

    /// <summary>
    ///     Gets the HTTP client to use; null creates and disposes a private one.
    /// </summary>
    /// <remarks>
    ///     The private client does not follow redirects automatically; the downloader follows them itself
    ///     (up to 10 hops) and attaches mirror credentials only to hops with the mirror's own scheme, host
    ///     and port. A caller-supplied client keeps its own redirect policy: if it follows redirects
    ///     automatically, the downloader cannot observe the hops, and credential handling on redirect is that
    ///     client's responsibility (the default .NET handler drops the Authorization header on redirect).
    ///     Configure such a client with automatic redirects disabled to get the per-hop guarantee.
    /// </remarks>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    ///     Gets the maximum time a single source attempt for a single file may take.
    /// </summary>
    /// <remarks>
    ///     Must be positive or <see cref="Timeout.InfiniteTimeSpan"/>; <see cref="WordSenseModelDownloader.EnsureModelAsync"/>
    ///     throws <see cref="ArgumentOutOfRangeException"/> for any other value.
    /// </remarks>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the receiver of download progress reports, or null.
    /// </summary>
    /// <remarks>
    ///     An exception thrown by the receiver is not treated as a source failure: it propagates out of
    ///     <see cref="WordSenseModelDownloader.EnsureModelAsync"/> and the remaining sources are not tried.
    /// </remarks>
    public IProgress<WordSenseModelDownloadProgress>? Progress { get; init; }
}
