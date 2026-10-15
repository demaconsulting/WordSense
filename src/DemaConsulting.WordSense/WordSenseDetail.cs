namespace DemaConsulting.WordSense;

/// <summary>
///     The best sense of a word together with the confidence figures behind the decision.
/// </summary>
/// <param name="Sense">The best sense; always set for a word that has a classifier.</param>
/// <param name="Margin">
///     The score of the best sense minus the score of the runner-up. This is the value compared with
///     the lemma's abstain threshold.
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
