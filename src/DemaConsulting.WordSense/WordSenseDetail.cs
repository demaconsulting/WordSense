namespace DemaConsulting.WordSense;

/// <summary>
///     The best sense of a word together with the confidence figures behind the decision.
/// </summary>
/// <remarks>
///     Returned by <see cref="IWordSenseClassifier.ClassifyDetailAsync"/>, which reports a detail even when
///     the classifier abstained, so a caller can apply its own threshold to <see cref="Margin"/>. The classifier
///     does not expose the threshold it applied; the effective threshold of a lemma is
///     <see cref="LemmaModel.AbstainMargin"/> multiplied by
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>, and the models are public through
///     <see cref="WordSenseTable.LoadEmbedded"/>. Otherwise use <see cref="Abstained"/>.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(["They", "will", "record", "it"]);
///
///     if (details[2] is { } detail)
///     {
///         Console.WriteLine($"{detail.Sense} margin={detail.Margin:F2} runner-up={detail.RunnerUp}");
///         if (detail.Abstained)
///         {
///             Console.WriteLine("The classifier is not confident enough to answer.");
///         }
///     }
///     </code>
/// </example>
/// <param name="Sense">
///     The best sense; always set for a word that has a classifier. It is populated even when
///     <paramref name="Abstained"/> is true, whereas <see cref="IWordSenseClassifier.ClassifyAsync(IReadOnlyList{string}, CancellationToken)"/>
///     returns null for such a word.
/// </param>
/// <param name="Margin">
///     The score of the best sense minus the score of the runner-up. A score is the linear activation of a
///     sense (feature vector times the sense's weights plus bias; for a two-sense lemma the scores are
///     <c>-a</c> and <c>+a</c>), so higher is better but the scale is not a probability. The margin is the
///     value compared with the lemma's abstain threshold (<see cref="LemmaModel.AbstainMargin"/> times
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>); it is comparable only within one lemma,
///     because each lemma has its own threshold. The classifier does not expose the threshold itself; use
///     <see cref="Abstained"/> unless you compute the threshold from the embedded table.
/// </param>
/// <param name="RunnerUp">The second-best sense.</param>
/// <param name="RunnerUpMargin">
///     The score of the runner-up minus the score of the third-best sense; 0 when the lemma has only two senses.
/// </param>
/// <param name="Abstained">True when <paramref name="Margin"/> is below the lemma's (scaled) abstain threshold.</param>
public readonly record struct WordSenseDetail(
    WordSense Sense,
    double Margin,
    WordSense RunnerUp,
    double RunnerUpMargin,
    bool Abstained);
