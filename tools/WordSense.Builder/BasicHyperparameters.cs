namespace WordSense.Builder;

/// <summary>
///     Every tunable number of the basic model, in one place: the training (selection of the tabled words and the cue
///     vocabulary), the tuning (the grids and the sample sizes) and the values that travel in the table header to the
///     runtime. The trainer, the tuner and the table writer read nothing else; to try a different value, change it
///     here (or in a <c>with</c> expression of the tuner) and run <c>train-basic</c>.
/// </summary>
/// <param name="Gamma">The exponent of the emission ratio of a tabled word (runtime; in the table header).</param>
/// <param name="Beta">The pseudo count of the tag prior in the emission of a tabled word (runtime; in the table header).</param>
/// <param name="Lambda1">The unigram weight of the transition interpolation (runtime; in the table header).</param>
/// <param name="Lambda2">The bigram weight of the transition interpolation (runtime; in the table header).</param>
/// <param name="Lambda3">The trigram weight of the transition interpolation (runtime; in the table header).</param>
/// <param name="HmmWeight">The weight of the log tag posterior in the fusion (runtime; in the table header).</param>
/// <param name="Temperature">The temperature of the cue scores in the fusion (runtime; in the table header as its inverse).</param>
/// <param name="ClosedMinCount">The fewest training occurrences of a word that can make it a closed-class word.</param>
/// <param name="ClosedShare">The share of the dominant tag of a word at which the word is closed-class with all its tags.</param>
/// <param name="ClosedAddShare">The share a tag needs for a word whose dominant share is lower, to be added to its closed tags.</param>
/// <param name="TopAmbiguousWords">The number of most frequent dictionary-ambiguous words whose counts are tabled.</param>
/// <param name="SuffixMinTotal">The fewest training words a suffix needs to be tabled.</param>
/// <param name="CueAlpha">The additive smoothing of the cue likelihoods.</param>
/// <param name="CueMinFeatureCount">The fewest training sentences a context feature needs to enter the cue vocabulary of a lemma.</param>
internal sealed record BasicHyperparameters(
    double Gamma,
    double Beta,
    double Lambda1,
    double Lambda2,
    double Lambda3,
    double HmmWeight,
    double Temperature,
    int ClosedMinCount,
    double ClosedShare,
    double ClosedAddShare,
    int TopAmbiguousWords,
    int SuffixMinTotal,
    double CueAlpha,
    int CueMinFeatureCount)
{
    /// <summary>
    ///     The number of anchor sentences the tuner samples for its anchor train and dev subsets.
    /// </summary>
    public const int AnchorSample = 5000;

    /// <summary>
    ///     The seed of the deterministic anchor sample.
    /// </summary>
    public const int AnchorSeed = 20260101;

    /// <summary>
    ///     The share of the sampled anchors (one in this many, by a hash of the sentence) that form the anchor dev subset.
    /// </summary>
    public const int AnchorDevModulus = 5;

    /// <summary>
    ///     The values the shipped table is trained with: the values of the original design. <c>tune-basic</c> (development
    ///     split of the representative open corpus and the anchor development subset only) finds at most +0.5 points on the
    ///     open development split for gamma 1.0 with beta 1.0, but that costs 1.8 points on the anchor development subset
    ///     (the hard, targeted ambiguous words), so the defaults stay (see the user guide chapter on accuracy).
    /// </summary>
    public static readonly BasicHyperparameters Default = new(
        Gamma: 0.75,
        Beta: 5,
        Lambda1: 0.1,
        Lambda2: 0.3,
        Lambda3: 0.6,
        HmmWeight: 1.5,
        Temperature: 2,
        ClosedMinCount: 5,
        ClosedShare: 0.9,
        ClosedAddShare: 0.2,
        TopAmbiguousWords: 2000,
        SuffixMinTotal: 5,
        CueAlpha: 0.1,
        CueMinFeatureCount: 2);

    /// <summary>
    ///     The values the tuner tries, one parameter at a time while the others keep their default.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<BasicHyperparameters> Variants)> Grid =
    [
        ("gamma", [.. new[] { 0.5, 0.75, 1.0 }.Select(v => Default with { Gamma = v })]),
        ("beta", [.. new[] { 1.0, 5.0 }.Select(v => Default with { Beta = v })]),
        ("closed", [Default, Default with { ClosedMinCount = 3, ClosedShare = 0.8 }]),
        ("hmm-weight", [.. new[] { 0.5, 1.0, 1.5, 2.0 }.Select(v => Default with { HmmWeight = v })]),
        ("temperature", [.. new[] { 2.0, 4.0, 8.0 }.Select(v => Default with { Temperature = v })]),
        ("cue-alpha", [.. new[] { 0.05, 0.1, 0.5 }.Select(v => Default with { CueAlpha = v })]),
        ("combined", [Default with { Gamma = 1.0, Beta = 1.0 }, Default with { Gamma = 1.0, Beta = 1.0, ClosedMinCount = 3, ClosedShare = 0.8 }]),
    ];

    /// <summary>
    ///     Gets the inverse of <see cref="Temperature"/>: the factor on the cue scores that the runtime reads.
    /// </summary>
    public double InvTemperature => 1.0 / Temperature;
}
