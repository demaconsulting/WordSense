namespace DemaConsulting.WordSense;

/// <summary>
///     Options for <see cref="WordSenseClassifier.CreateAsync"/>.
/// </summary>
public sealed class WordSenseClassifierOptions
{
    /// <summary>
    ///     Gets the directory that already contains the verified model files; when null the model is
    ///     obtained through <see cref="WordSenseModelDownloader"/> using <see cref="Downloader"/>.
    /// </summary>
    public string? ModelDirectory { get; init; }

    /// <summary>
    ///     Gets the options used when the model has to be downloaded.
    /// </summary>
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
    public double AbstainMarginScale { get; init; } = 1.0;
}
