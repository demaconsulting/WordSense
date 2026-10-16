namespace DemaConsulting.WordSense;

/// <summary>
///     The single decision rule that turns a score margin into an answer or an abstention.
/// </summary>
internal static class AbstainRule
{
    /// <summary>
    ///     Decides whether a margin is too small to answer; the single predicate shared by every classification
    ///     path, so a NaN margin or threshold always abstains.
    /// </summary>
    /// <param name="margin">The score margin.</param>
    /// <param name="threshold">The abstain threshold.</param>
    /// <returns>True when no answer should be given.</returns>
    public static bool Abstains(double margin, double threshold) => !(margin >= threshold);
}
