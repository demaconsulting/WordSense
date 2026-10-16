namespace DemaConsulting.WordSense;

/// <summary>
///     Options for <see cref="WordSenseClassifier.CreateAsync"/>.
/// </summary>
/// <remarks>
///     Every property is optional. Set <see cref="ModelDirectory"/> for offline use, <see cref="Downloader"/>
///     to reach the model through a mirror or a custom store, and <see cref="AbstainMarginScale"/> to trade
///     coverage against precision: a larger scale makes the classifier abstain (return null) more often.
///     A null <see cref="Downloader"/> does not disable the mirror environment variables; see
///     <see cref="DownloadMirror.UrlEnvironmentVariable"/>.
/// </remarks>
/// <example>
///     <code>
///     var options = new WordSenseClassifierOptions
///     {
///         AbstainMarginScale = 0.5, // answer more often; 0 never abstains, above 1 abstains more
///         MaxSequenceLength = 128,  // 8 to 512 tokens
///         IntraOpThreads = 2,       // 0 lets ONNX Runtime decide
///     };
///
///     using var classifier = await WordSenseClassifier.CreateAsync(options);
///     </code>
/// </example>
public sealed class WordSenseClassifierOptions
{
    /// <summary>
    ///     Gets the directory that already contains the verified model files; when null the model is
    ///     obtained through <see cref="WordSenseModelDownloader"/> using <see cref="Downloader"/>.
    /// </summary>
    /// <remarks>
    ///     When this is set, <see cref="Downloader"/> is ignored: the files are verified in place, nothing is
    ///     downloaded and no mirror environment variable is read.
    /// </remarks>
    public string? ModelDirectory { get; init; }

    /// <summary>
    ///     Gets the options used when the model has to be downloaded.
    /// </summary>
    /// <remarks>
    ///     Ignored when <see cref="ModelDirectory"/> is set. When null, default downloader options are used,
    ///     and a mirror is still configured from the environment variables named on
    ///     <see cref="DownloadMirror.UrlEnvironmentVariable"/> (<c>WORDSENSE_MIRROR_URL</c> and its credential
    ///     companions); an invalid environment configuration makes
    ///     <see cref="WordSenseModelDownloader.EnsureModelAsync"/> throw <see cref="ArgumentException"/>. See that
    ///     method for the other exceptions.
    /// </remarks>
    public WordSenseModelDownloaderOptions? Downloader { get; init; }

    /// <summary>
    ///     Gets the maximum encoder sequence length including special tokens; at least 8, at most 512.
    ///     Longer texts are classified from a window centered on each target word.
    /// </summary>
    public int MaxSequenceLength { get; init; } = 256;

    /// <summary>
    ///     Gets the number of threads ONNX Runtime may use within one inference; 0 lets it decide.
    /// </summary>
    public int IntraOpThreads { get; init; }

    /// <summary>
    ///     Gets the factor applied to every lemma's abstain margin. Values above 1 make the classifier
    ///     abstain more often; 0 never abstains.
    /// </summary>
    /// <remarks>Must not be negative or NaN; <see cref="WordSenseClassifier.CreateAsync"/> rejects such values.</remarks>
    public double AbstainMarginScale { get; init; } = 1.0;
}
