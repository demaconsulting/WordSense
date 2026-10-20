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
///     scored the fine senses of the analysis, the probability of each sense given the analysis (given its slot): they
///     sum to one over the
///     listed senses, are sorted by probability (ties by the ordinal order of the sense id) and are never truncated. The
///     list is empty when the model did not score the senses of this analysis. A consumer that maps senses to its own
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
    ///     Initializes a new instance of the <see cref="AnalysisCandidate"/> struct.
    /// </summary>
    /// <param name="details">The analysis: lemma, part of speech and features.</param>
    /// <param name="probability">The probability of the analysis among the analyses of the word.</param>
    /// <param name="senses">The probability of each fine sense given the analysis, the most probable first; a default array counts as empty.</param>
    public AnalysisCandidate(WordDetails details, double probability, ImmutableArray<SenseCandidate> senses)
    {
        _details = details;
        Probability = probability;
        _senses = senses;
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
    ///     when the senses were not scored.
    /// </summary>
    public ImmutableArray<SenseCandidate> Senses => _senses.IsDefault ? ImmutableArray<SenseCandidate>.Empty : _senses;

    /// <summary>
    ///     Determines whether another candidate has the same values, the senses compared item by item.
    /// </summary>
    /// <param name="other">The other candidate.</param>
    /// <returns><see langword="true"/> when every value is equal.</returns>
    public bool Equals(AnalysisCandidate other) =>
        Details.Equals(other.Details)
        && Probability.CompareTo(other.Probability) == 0
        && Senses.AsSpan().SequenceEqual(other.Senses.AsSpan());

    /// <summary>
    ///     Gets a hash code that agrees with <see cref="Equals(AnalysisCandidate)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Details);
        hash.Add(Probability);
        foreach (var sense in Senses)
        {
            hash.Add(sense);
        }

        return hash.ToHashCode();
    }
}
