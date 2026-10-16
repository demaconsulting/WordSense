namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     Options for <see cref="MiniLMWordSense.CreateClassifierAsync"/>.
/// </summary>
/// <remarks>
///     Every property is optional. Set <see cref="ModelDirectory"/> for offline use and <see cref="Downloader"/> to
///     reach the model through a mirror or a custom store. A null <see cref="Downloader"/> does not disable the mirror
///     environment variables; see <see cref="DownloadMirror.UrlEnvironmentVariable"/>.
/// </remarks>
/// <example>
///     <code>
///     var options = new MiniLMWordSenseOptions
///     {
///         MaxSequenceLength = 128,  // 8 to 512 tokens
///         IntraOpThreads = 2,       // 0 lets ONNX Runtime decide
///     };
///
///     using var classifier = await MiniLMWordSense.CreateClassifierAsync(options);
///     </code>
/// </example>
public sealed class MiniLMWordSenseOptions
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMWordSenseOptions"/> class with every option at its default.
    /// </summary>
    public MiniLMWordSenseOptions()
    {
    }

    /// <summary>
    ///     Gets the directory that holds the model files, which are verified against their pinned digests when the
    ///     classifier is created; when null (the default) the model is obtained through
    ///     <see cref="WordSenseModelDownloader"/> using <see cref="Downloader"/>.
    /// </summary>
    /// <remarks>
    ///     When this is set, <see cref="Downloader"/> is ignored: the files are verified in place, nothing is
    ///     downloaded and no mirror environment variable is read. The directory must contain the two files
    ///     <c>model_int8.onnx</c> and <c>vocab.txt</c> directly (not in a subfolder); the name of the folder itself
    ///     does not matter. Each file must match its pinned SHA-256, otherwise
    ///     <see cref="MiniLMWordSense.CreateClassifierAsync"/> throws.
    /// </remarks>
    public string? ModelDirectory { get; init; }

    /// <summary>
    ///     Gets the options used when the model has to be downloaded; the default is null.
    /// </summary>
    /// <remarks>
    ///     Ignored when <see cref="ModelDirectory"/> is set. When null, default downloader options are used, and a
    ///     mirror is still configured from the environment variables named on
    ///     <see cref="DownloadMirror.UrlEnvironmentVariable"/>; an invalid environment configuration makes
    ///     <see cref="WordSenseModelDownloader.EnsureModelAsync"/> throw <see cref="ArgumentException"/>.
    /// </remarks>
    public WordSenseModelDownloaderOptions? Downloader { get; init; }

    /// <summary>
    ///     Gets the maximum encoder sequence length including special tokens; at least 8, at most 512; the default is 256.
    ///     Longer texts are classified from a window centered on each target word.
    /// </summary>
    public int MaxSequenceLength { get; init; } = 256;

    /// <summary>
    ///     Gets the number of threads ONNX Runtime may use within one inference; the default is 0, which lets it decide.
    ///     A negative value is rejected by <see cref="MiniLMWordSense.CreateClassifierAsync"/>.
    /// </summary>
    public int IntraOpThreads { get; init; }
}
