namespace DemaConsulting.WordSense;

/// <summary>
///     One fine sense of an analysis with the probability the model gives it, given the analysis.
/// </summary>
/// <remarks>
///     The senses of an <see cref="AnalysisCandidate"/> are listed ranked by probability, the most probable first (ties by
///     the ordinal order of the sense id), and the probabilities of the listed senses sum to one. A consumer can apply
///     its own grouping of the senses: it sums the probabilities per category of its own and answers the category with
///     the largest sum, even when the result itself is answered at the part-of-speech or the group level.
/// </remarks>
/// <example>
///     <code>
///     SenseCandidate candidate = new("bow#archery", 0.48);
///     Console.WriteLine($"{candidate.SenseId}: {candidate.Probability:P0}");
///     </code>
/// </example>
/// <param name="SenseId">The identifier of the sense (<c>lemma#label</c>).</param>
/// <param name="Probability">The probability of the sense given its analysis, from 0 to 1.</param>
public readonly record struct SenseCandidate(string SenseId, double Probability);
