using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     One analysis of a word with the probability the model gives it in its context, and the probabilities of its fine
///     senses.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="Probability"/> is the probability of the analysis (lemma, part of speech and features) among the
///     analyses of the word; over all analyses of a result they sum to one. The senses belong to a lemma and a part of
///     speech, not to the features, so the analyses that have the same lemma and part of speech and differ only in
///     features (a <em>slot</em>: <c>cut</c> as an infinitive and as a past tense, <c>deer</c> as a singular and a
///     plural) share the probability of their slot, split among them by the evidence the model has (a form chooser, the
///     context cues of the Basic model) or, without evidence, equally, and every one of them lists the same senses with
///     the same probabilities. The probability of a slot is the sum of the probabilities of its analyses.
///     <see cref="Senses"/> lists, when the model
///     ranked the fine senses of the analysis, the probability of each sense given the analysis (given its slot): they
///     sum to one over the
///     listed senses, are sorted by probability (equal probabilities in the inventory order of the lemma, see
///     <see cref="WordLexicon.SensesOf"/>) and are never truncated. The ranking is either scored (an encoder model, from the
///     context of the word) or the usage prior of the Basic model (the WordNet 3.0 tag counts of the senses, the same in
///     every sentence, for every polysemous lemma and part of speech no model scored); the level and the confidence of the
///     result say which senses are answered, and a ranking is never an answer by itself. <see cref="SensesRankedByUsage"/>
///     tells the two rankings apart. The
///     list is empty when the model did not rank the senses of this analysis. That is the case for a lemma and part of
///     speech that have no sense, and it can also be the case for a polysemous slot when a model does not rank it (for
///     example a model that you write); the shipped models rank every polysemous slot, an encoder model by its scores for
///     the lemmas it was trained for and the Basic model by usage for every other one. A consumer that maps senses to its own
///     categories can sum <c>Probability * sense.Probability</c> per category over all analyses (which gives the
///     probability of the slot times the probability of the sense, whatever the number of feature variants).
///     </para>
///     <para>
///     The probabilities are comparable only within one result; do not compare them across models. A
///     <see langword="default"/> candidate is valid: its <see cref="Details"/> is a placeholder without a lemma and its
///     <see cref="Senses"/> is empty.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static void Show(ClassifyResult result)
///     {
///         foreach (AnalysisCandidate analysis in result.Analyses)
///         {
///             // The categories of a consumer: the probability of each sense label, summed over the analysis
///             foreach (SenseCandidate sense in analysis.Senses)
///             {
///                 Console.WriteLine($"{analysis.Details.Lemma} {sense.SenseId}: {analysis.Probability * sense.Probability:P0}");
///             }
///         }
///     }
///     </code>
/// </example>
public readonly record struct AnalysisCandidate
{
    private static readonly WordDetails Empty = new(string.Empty, PartOfSpeech.Noun, string.Empty);

    private readonly WordDetails? _details;
    private readonly ImmutableArray<SenseCandidate> _senses;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalysisCandidate"/> struct whose senses, when there are any, were
    ///     scored from the context of the word (<see cref="SensesRankedByUsage"/> is <see langword="false"/>).
    /// </summary>
    /// <param name="details">The analysis: lemma, part of speech and features.</param>
    /// <param name="probability">The probability of the analysis among the analyses of the word.</param>
    /// <param name="senses">The probability of each fine sense given the analysis, the most probable first; a default array counts as empty.</param>
    public AnalysisCandidate(WordDetails details, double probability, ImmutableArray<SenseCandidate> senses)
        : this(details, probability, senses, false)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalysisCandidate"/> struct and says whether the senses are only
    ///     ranked by their usage.
    /// </summary>
    /// <param name="details">The analysis: lemma, part of speech and features.</param>
    /// <param name="probability">The probability of the analysis among the analyses of the word.</param>
    /// <param name="senses">The probability of each fine sense given the analysis, the most probable first; a default array counts as empty.</param>
    /// <param name="sensesRankedByUsage"><see langword="true"/> when the senses are the context-free usage prior of the lemma and not scored from the context of the word.</param>
    public AnalysisCandidate(WordDetails details, double probability, ImmutableArray<SenseCandidate> senses, bool sensesRankedByUsage)
    {
        _details = details;
        Probability = probability;
        _senses = senses;
        SensesRankedByUsage = sensesRankedByUsage;
    }

    /// <summary>
    ///     Gets the analysis: lemma, part of speech and features.
    /// </summary>
    public WordDetails Details => _details ?? Empty;

    /// <summary>
    ///     Gets the probability of the analysis among the analyses of the word, from 0 to 1.
    /// </summary>
    public double Probability { get; }

    /// <summary>
    ///     Gets the probability of each fine sense given the analysis, the most probable first, summing to one; empty
    ///     when the senses were not ranked.
    /// </summary>
    public ImmutableArray<SenseCandidate> Senses => _senses.IsDefault ? ImmutableArray<SenseCandidate>.Empty : _senses;

    /// <summary>
    ///     Gets a value that says whether the listed senses are only ranked by their usage (the Basic model: the WordNet 3.0
    ///     tag counts of the lemma, the same in every sentence) and not scored from the context of the word.
    /// </summary>
    /// <remarks>
    ///     It is <see langword="false"/> when the senses are empty, when they were scored from the context (an encoder model),
    ///     when the analysis has one sense (nothing was ranked), and for every candidate built without the argument, so a
    ///     model that you write and lists senses is taken as scoring them in context unless it says otherwise. The
    ///     <see cref="FallbackWordSenseModel"/> relies on it: a result whose selected analysis ranks its senses by usage is
    ///     not a sense score of its model, so the composite does not use it as one.
    /// </remarks>
    public bool SensesRankedByUsage { get; }

    /// <summary>
    ///     Determines whether another candidate has the same values, the senses compared item by item.
    /// </summary>
    /// <param name="other">The other candidate.</param>
    /// <returns><see langword="true"/> when every value is equal.</returns>
    public bool Equals(AnalysisCandidate other) =>
        Details.Equals(other.Details)
        && Probability.CompareTo(other.Probability) == 0
        && SensesRankedByUsage == other.SensesRankedByUsage
        && Senses.AsSpan().SequenceEqual(other.Senses.AsSpan());

    /// <summary>
    ///     Gets a candidate with the same analysis, senses and ranking kind and another probability.
    /// </summary>
    /// <param name="probability">The new probability.</param>
    /// <returns>The new candidate.</returns>
    internal AnalysisCandidate WithProbability(double probability) => new(Details, probability, Senses, SensesRankedByUsage);

    /// <summary>
    ///     Gets a hash code that agrees with <see cref="Equals(AnalysisCandidate)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Details);
        hash.Add(Probability);
        hash.Add(SensesRankedByUsage);
        foreach (var sense in Senses)
        {
            hash.Add(sense);
        }

        return hash.ToHashCode();
    }
}
