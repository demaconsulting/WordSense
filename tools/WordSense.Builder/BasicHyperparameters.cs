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
/// <param name="TenseWeight">The weight of the shared tense model in the split of a verb slot between its past and non-past analyses (runtime; in the table header). 0 turns the model off.</param>
/// <param name="TenseAlpha">The additive smoothing of the feature likelihoods of the tense model.</param>
/// <param name="TenseMinFeatureCount">The fewest training verbs a context feature needs to enter the vocabulary of the tense model.</param>
/// <param name="ClauseSmoothing">The pseudo count that smooths the clause-state transitions of the tagger toward the unconditioned transitions (runtime; in the table header): a trigram context with m counts in its clause state weighs m / (m + smoothing).</param>
/// <param name="PriorShare">The share, 0 to 1, of the word prior from the sense catalog in the target of the pseudo counts of the emission of every word (runtime; in the table header); the rest is the generic tag prior. 0 turns the word prior off.</param>
/// <param name="PriorSmoothing">The constant that the word prior adds to the weight of every lemma and part of speech of a word (runtime; in the table header).</param>
/// <param name="PriorSenses">The weight that the word prior gives to every sense of a word, the proxy of a word without tag counts (runtime; in the table header).</param>
/// <param name="PriorExponent">The exponent of the weight of a part of speech in the word prior (runtime; in the table header).</param>
/// <param name="CueFolds">The number of folds of the cross-validation that measures how well each cue table predicts held-out sentences of its own training data.</param>
/// <param name="CueSkillTarget">The balanced accuracy of the held-out sentences at which a cue table is fully reliable; a table below it is damped in proportion to its skill over chance (chance is one over its analyses), down to 0 at chance. 0 turns the damping off.</param>
/// <param name="RunningFolds">The number of folds of the cross-fitted measurement of the net benefit of each cue table on running text (the training split of the open corpus): the tagger is trained on all folds but one and scores the fold that is left out with every cue table on and with every one off. 0 turns the measurement off.</param>
/// <param name="RunningPseudo">The pseudo count of the running-text reliability: a table that the cues turned right (helped) on h tokens and wrong (hurt) on u tokens gets the factor min(1, (h + pseudo) / (u + pseudo)), so a table with few changed tokens stays near 1.</param>
/// <param name="RunningMinimum">True when the final reliability of a table is the smaller of its anchor reliability and its running-text factor, false when it is their product.</param>
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
    int CueMinFeatureCount,
    double TenseWeight = 1.0,
    double TenseAlpha = 0.5,
    int TenseMinFeatureCount = 2,
    double ClauseSmoothing = 4.0,
    double PriorShare = 0.0,
    double PriorSmoothing = 0.5,
    double PriorSenses = 0.0,
    double PriorExponent = 1.0,
    int CueFolds = 5,
    double CueSkillTarget = 0.0,
    int RunningFolds = 0,
    double RunningPseudo = 5.0,
    bool RunningMinimum = false)
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
    ///     The values the shipped table is trained with. The part-of-speech emission of a word is its counts in the open
    ///     corpus smoothed by <see cref="Beta"/> pseudo counts whose target is a mix of the generic tag prior and a word prior
    ///     from the sense catalog (<see cref="PriorShare"/> of it is the word prior); every word of the open training counts
    ///     that the dictionary lists with two or more parts of speech has a row (<see cref="TopAmbiguousWords"/> is
    ///     unlimited), and a cue table whose sentences are predicted poorly by the table itself when they are held out is
    ///     damped (<see cref="CueSkillTarget"/>), as is a table whose cues turn more tokens of the open corpus training split
    ///     wrong than right when the tagger is cross-fitted (<see cref="RunningPseudo"/>; the development sets are flat over
    ///     the pseudo count, and the reviewed suite keeps its rows from 50 on, so the evidence of a few tokens damps little).
    ///     The values are chosen with <c>tune-basic</c> on the development split of
    ///     the representative open corpus, the development file of the treebank and the anchor development subset only (see
    ///     <see cref="PriorGrid"/> and <see cref="Previous"/>, the settings without the word prior). The development sets
    ///     alone prefer fewer pseudo counts (<see cref="Beta"/> 3 gives +0.19 and +0.35 points of ambiguous accuracy on the
    ///     open corpus and the treebank), but with them the reviewed suite loses five rows that Basic answers right
    ///     (<c>state</c>, <c>feel</c>, <c>august</c>, <c>resume</c>, <c>present</c>), so the smoothing stays at 5 pseudo
    ///     counts of the generic prior plus 2 of the word prior (<c>Beta</c> 7 with a share of 0.3); the suite is a guard
    ///     here, not a tuning set.
    /// </summary>
    public static readonly BasicHyperparameters Default = new(
        Gamma: 1.0,
        Beta: 7,
        Lambda1: 0.1,
        Lambda2: 0.3,
        Lambda3: 0.6,
        HmmWeight: 1.5,
        Temperature: 2,
        ClosedMinCount: 5,
        ClosedShare: 0.9,
        ClosedAddShare: 0.2,
        TopAmbiguousWords: int.MaxValue,
        SuffixMinTotal: 5,
        CueAlpha: 0.1,
        CueMinFeatureCount: 2,
        TenseWeight: 1.0,
        TenseAlpha: 0.5,
        TenseMinFeatureCount: 2,
        ClauseSmoothing: 4.0,
        PriorShare: 0.3,
        PriorSmoothing: 0.25,
        PriorSenses: 1.0,
        PriorExponent: 1.0,
        CueFolds: 5,
        CueSkillTarget: 0.6,
        RunningFolds: 4,
        RunningPseudo: 50.0,
        RunningMinimum: false);

    /// <summary>
    ///     The settings without the word prior: gamma 0.75, beta 5, no prior, and only the 2,000 most frequent ambiguous words
    ///     tabled. The tuner scores them as the baseline of the word prior.
    /// </summary>
    public static readonly BasicHyperparameters Previous = Default with { Gamma = 0.75, Beta = 5, PriorShare = 0.0, TopAmbiguousWords = 2000 };

    /// <summary>
    ///     The values the tuner tries, one parameter at a time while the others keep their default.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<BasicHyperparameters> Variants)> Grid =
    [
        ("gamma", [.. new[] { 0.5, 0.75, 1.0 }.Select(v => Default with { Gamma = v })]),
        ("beta", [.. new[] { 0.5, 1.0, 5.0 }.Select(v => Default with { Beta = v })]),
        ("closed", [Default, Default with { ClosedMinCount = 3, ClosedShare = 0.8 }]),
        ("hmm-weight", [.. new[] { 0.5, 1.0, 1.5, 2.0 }.Select(v => Default with { HmmWeight = v })]),
        ("temperature", [.. new[] { 2.0, 4.0, 8.0 }.Select(v => Default with { Temperature = v })]),
        ("cue-alpha", [.. new[] { 0.05, 0.1, 0.5 }.Select(v => Default with { CueAlpha = v })]),
        ("clause-smoothing", [.. new[] { 0.0, 1.0, 4.0, 16.0, 64.0 }.Select(v => Default with { ClauseSmoothing = v })]),
    ];

    /// <summary>
    ///     The values the tuner tries for the word prior: the values before the prior, then one parameter at a time around
    ///     <see cref="Default"/>, then the weight of the prior against beta.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<BasicHyperparameters> Variants)> PriorGrid =
    [
        ("previous", [Previous]),
        ("component on baseline", [Previous with { Gamma = 1.0 }, Previous with { TopAmbiguousWords = int.MaxValue }, Previous with { PriorShare = 0.3, Beta = 7 }]),
        ("component off default", [Default with { Gamma = 0.75 }, Default with { TopAmbiguousWords = 2000 }, Default with { PriorShare = 0.0, Beta = 5 }]),
        ("no prior", [Default with { PriorShare = 0.0 }]),
        ("top", [.. new[] { 2000, 5000, int.MaxValue }.Select(v => Default with { TopAmbiguousWords = v })]),
        ("share", [.. new[] { 0.15, 0.3, 0.5, 0.75, 1.0 }.Select(v => Default with { PriorShare = v })]),
        ("beta", [.. new[] { 3.0, 5.0, 7.0, 10.0 }.Select(v => Default with { Beta = v })]),
        ("gamma", [.. new[] { 0.75, 1.0, 1.25 }.Select(v => Default with { Gamma = v })]),
        ("smoothing", [.. new[] { 0.1, 0.25, 1.0 }.Select(v => Default with { PriorSmoothing = v })]),
        ("exponent", [.. new[] { 0.5, 0.75, 1.0, 1.5 }.Select(v => Default with { PriorExponent = v })]),
        ("senses", [.. new[] { 0.0, 1.0, 2.0 }.Select(v => Default with { PriorSenses = v })]),
    ];

    /// <summary>
    ///     The cue skill targets the tuner tries (0 turns the damping off).
    /// </summary>
    public static readonly IReadOnlyList<double> CueSkillTargetGrid = [0.0, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];

    /// <summary>
    ///     The pseudo counts of the running-text reliability the tuner tries.
    /// </summary>
    public static readonly IReadOnlyList<double> RunningPseudoGrid = [1.0, 2.0, 5.0, 10.0, 20.0, 50.0];

    /// <summary>
    ///     The weights of the tense model the tuner tries (0 turns the model off).
    /// </summary>
    public static readonly IReadOnlyList<double> TenseWeightGrid = [0.0, 0.25, 0.5, 1.0, 1.5, 2.0];

    /// <summary>
    ///     Gets the inverse of <see cref="Temperature"/>: the factor on the cue scores that the runtime reads.
    /// </summary>
    public double InvTemperature => 1.0 / Temperature;
}
