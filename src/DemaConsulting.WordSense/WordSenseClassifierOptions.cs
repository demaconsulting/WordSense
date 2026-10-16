namespace DemaConsulting.WordSense;

/// <summary>
///     Options for <see cref="WordSenseClassifier.CreateAsync"/>.
/// </summary>
/// <remarks>
///     Every property is optional. Set <see cref="ModelDirectory"/> for offline use, <see cref="Downloader"/>
///     to reach the model through a mirror or a custom store, and <see cref="AbstainMarginScale"/> to trade
///     coverage against precision: a larger scale makes the classifier abstain more often (<c>Details</c> is null and
///     <c>Abstained</c> is true).
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
    ///     Initializes a new instance of the <see cref="WordSenseClassifierOptions"/> class with every option at its default.
    /// </summary>
    public WordSenseClassifierOptions()
    {
    }

    /// <summary>
    ///     Gets the directory that holds the model files, which are verified against their pinned digests when the
    ///     classifier is created; when null (the default) the model is
    ///     obtained through <see cref="WordSenseModelDownloader"/> using <see cref="Downloader"/>.
    /// </summary>
    /// <remarks>
    ///     When this is set, <see cref="Downloader"/> is ignored: the files are verified in place, nothing is
    ///     downloaded and no mirror environment variable is read. The directory must contain the two files
    ///     <c>model_int8.onnx</c> and <c>vocab.txt</c> directly (not in a subfolder); the name of the folder itself
    ///     does not matter. Each file must match its pinned SHA-256, otherwise
    ///     <see cref="WordSenseClassifier.CreateAsync"/> throws.
    /// </remarks>
    public string? ModelDirectory { get; init; }

    /// <summary>
    ///     Gets the options used when the model has to be downloaded; the default is null.
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
    ///     Gets the maximum encoder sequence length including special tokens; at least 8, at most 512; the default is 256.
    ///     Longer texts are classified from a window centered on each target word.
    /// </summary>
    public int MaxSequenceLength { get; init; } = 256;

    /// <summary>
    ///     Gets the number of threads ONNX Runtime may use within one inference; the default is 0, which lets it decide.
    ///     A negative value is rejected by <see cref="WordSenseClassifier.CreateAsync"/>.
    /// </summary>
    public int IntraOpThreads { get; init; }

    /// <summary>
    ///     Gets the factor applied to every lemma's abstain margin; the default is 1.0. Values above 1 make the
    ///     classifier abstain more often; 0 never abstains.
    /// </summary>
    /// <remarks>
    ///     Must not be negative or NaN; <see cref="WordSenseClassifier.CreateAsync"/> rejects such values. The
    ///     effective threshold of a lemma is <c>SenseWeights.AbstainMargin * AbstainMarginScale</c>; a decision whose
    ///     margin (see <see cref="WordAnalysis.Margin"/>) is below it is withheld. For example a scale of 0.5 halves
    ///     every lemma's threshold, so more answers are given and more of them may be wrong.
    /// </remarks>
    public double AbstainMarginScale { get; init; } = 1.0;

    /// <summary>
    ///     Gets a value indicating whether words that only the dictionary knows are answered; the default is true.
    /// </summary>
    /// <remarks>
    ///     When true, a word without a trained classifier is answered from the dictionary (<see cref="AnswerBasis.Dictionary"/>):
    ///     with its one part of speech, or abstained with its candidates when it has several. When false only lemmas with a
    ///     trained classifier are answered and every other word is unknown with <see cref="AnswerBasis.None"/>; this
    ///     is the behavior before the dictionary existed. It also makes <see cref="IWordSenseClassifier.GetCandidates"/>
    ///     return nothing for a dictionary-only lemma. The setting never changes a context answer.
    /// </remarks>
    public bool UseLexicon { get; init; } = true;
}
