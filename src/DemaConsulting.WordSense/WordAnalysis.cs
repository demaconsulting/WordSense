namespace DemaConsulting.WordSense;

/// <summary>
///     The result of classifying one word: the answer, if any, and the confidence figures behind it.
/// </summary>
/// <remarks>
///     <para>
///     Every classification returns a full analysis, so there is no separate "detail" mode. The cases are:
///     </para>
///     <list type="bullet">
///     <item><description>
///     Unknown word (no classifier for it, or no tokens to read): <see cref="Details"/> is null,
///     <see cref="Abstained"/> is false, <see cref="RunnerUp"/> is null and both margins are 0.
///     </description></item>
///     <item><description>
///     Answered: <see cref="Details"/> is the best answer and <see cref="Abstained"/> is false.
///     </description></item>
///     <item><description>
///     Abstained: the word is known but the evidence is weak, so <see cref="Details"/> is null and
///     <see cref="Abstained"/> is true; <see cref="Margin"/> and <see cref="RunnerUp"/> are still populated, so a
///     caller may apply its own threshold.
///     </description></item>
///     </list>
///     <para>
///     The margin is a score difference, not a probability, and each lemma has its own scale. An answer is given
///     when the margin is at least the lemma's abstain margin multiplied by
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>. The scale is not calibrated: margins of answered
///     words typically fall between about 1 and 10 and larger means more confident, but there is no fixed value that
///     means "certain". Rely on <see cref="Abstained"/>, and tune
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/> for a stricter or looser rule.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordAnalysis[] words = await classifier.ClassifyAsync(["They", "will", "record", "it"]);
///
///     WordAnalysis record = words[2];
///     if (record.Details is { } details)
///     {
///         Console.WriteLine($"{record.Word} is the {details.PartOfSpeech} {details.Lemma} (margin {record.Margin:F2})");
///     }
///     else if (record.Abstained)
///     {
///         Console.WriteLine($"Not sure; the runner-up is {record.RunnerUp?.PartOfSpeech}.");
///     }
///     </code>
/// </example>
/// <param name="Word">The input word exactly as given.</param>
/// <param name="Details">The best answer, or null when the word is unknown or the classifier abstained.</param>
/// <param name="Margin">
///     The score of the best candidate minus the score of the runner-up. It is populated whenever the word is known
///     (also when the classifier abstained) and 0 for an unknown word.
/// </param>
/// <param name="Abstained">
///     True when the word is known but <paramref name="Margin"/> is below the lemma's (scaled) abstain threshold; false
///     for an unknown word.
/// </param>
/// <param name="RunnerUp">The second-best candidate, populated whenever the word is known; null for an unknown word.</param>
/// <param name="RunnerUpMargin">
///     The score of the runner-up minus the score of the third-best candidate; 0 when there are only two candidates or
///     the word is unknown.
/// </param>
public readonly record struct WordAnalysis(
    string Word,
    WordDetails? Details,
    double Margin,
    bool Abstained,
    WordDetails? RunnerUp,
    double RunnerUpMargin);
