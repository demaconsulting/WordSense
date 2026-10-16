namespace DemaConsulting.WordSense;

/// <summary>
///     Evaluates tag-level queries from the per-sense scores of one lemma.
/// </summary>
/// <remarks>
///     The score of a tag is the best score among the senses that carry it. Senses that carry none of
///     the queried tags form an "other" group. The group with the highest score wins; the answer is
///     withheld when the winner's margin over the runner-up group is below the threshold, when the
///     winner is "other", or when no queried tag exists on the lemma. A sense that carries several
///     queried tags counts for each of them, so it ties them. Ties break in favor of the tag that is
///     first in ordinal order, and a tag beats "other".
/// </remarks>
internal static class TagScorer
{
    /// <summary>
    ///     Answers a multi-tag query.
    /// </summary>
    /// <param name="senses">The senses of the lemma.</param>
    /// <param name="scores">The score of each sense, parallel to <paramref name="senses"/>.</param>
    /// <param name="tags">The queried tags, sorted ordinally and distinct.</param>
    /// <param name="threshold">The margin below which no answer is given.</param>
    /// <returns>The winning queried tag, or null for no answer.</returns>
    public static string? Classify(IReadOnlyList<WordSense> senses, ReadOnlySpan<float> scores, IReadOnlyList<string> tags, double threshold)
    {
        // Gather the group scores: one per queried tag that exists, plus "other"
        var best = -1;
        var bestScore = double.NegativeInfinity;
        var second = double.NegativeInfinity;
        var other = double.NegativeInfinity;
        for (var i = 0; i < senses.Count; i++)
        {
            var hasQueried = false;
            for (var t = 0; t < tags.Count; t++)
            {
                hasQueried |= senses[i].HasTag(tags[t]);
            }

            if (!hasQueried)
            {
                other = Math.Max(other, scores[i]);
            }
        }

        var present = 0;
        var winnerIsOther = false;
        for (var t = 0; t < tags.Count; t++)
        {
            var score = GroupScore(senses, scores, tags[t]);
            if (double.IsNegativeInfinity(score))
            {
                continue;
            }

            present++;
            if (score > bestScore)
            {
                second = bestScore;
                bestScore = score;
                best = t;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        if (present == 0)
        {
            return null;
        }

        // Fold the other group in; a queried tag wins a tie against it
        if (!double.IsNegativeInfinity(other))
        {
            if (other > bestScore)
            {
                second = bestScore;
                bestScore = other;
                winnerIsOther = true;
            }
            else if (other > second)
            {
                second = other;
            }
        }

        if (winnerIsOther)
        {
            return null;
        }

        // With a single present group the margin is infinite
        var margin = double.IsNegativeInfinity(second) ? double.PositiveInfinity : bestScore - second;
        return Abstains(margin, threshold) ? null : tags[best];
    }

    /// <summary>
    ///     Decides whether a margin is too small to answer; the single predicate shared by every classification
    ///     path, so a NaN margin or threshold always abstains.
    /// </summary>
    /// <param name="margin">The score margin.</param>
    /// <param name="threshold">The abstain threshold.</param>
    /// <returns>True when no answer should be given.</returns>
    public static bool Abstains(double margin, double threshold) => !(margin >= threshold);

    /// <summary>
    ///     Answers whether the best sense of the lemma carries a tag.
    /// </summary>
    /// <param name="senses">The senses of the lemma.</param>
    /// <param name="scores">The score of each sense.</param>
    /// <param name="tag">The queried tag.</param>
    /// <param name="threshold">The margin below which no answer is given.</param>
    /// <returns>True when the tag wins, false when "other" wins, null when the tag is absent or the margin is too small.</returns>
    public static bool? IsTag(IReadOnlyList<WordSense> senses, ReadOnlySpan<float> scores, string tag, double threshold)
    {
        var with = GroupScore(senses, scores, tag);
        if (double.IsNegativeInfinity(with))
        {
            return null;
        }

        var other = double.NegativeInfinity;
        for (var i = 0; i < senses.Count; i++)
        {
            if (!senses[i].HasTag(tag))
            {
                other = Math.Max(other, scores[i]);
            }
        }

        var margin = double.IsNegativeInfinity(other) ? double.PositiveInfinity : Math.Abs(with - other);
        if (Abstains(margin, threshold))
        {
            return null;
        }

        return with >= other;
    }

    /// <summary>
    ///     Gets the best score among the senses that carry a tag.
    /// </summary>
    /// <param name="senses">The senses.</param>
    /// <param name="scores">The scores.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The best score, or negative infinity when no sense carries the tag.</returns>
    private static double GroupScore(IReadOnlyList<WordSense> senses, ReadOnlySpan<float> scores, string tag)
    {
        var result = double.NegativeInfinity;
        for (var i = 0; i < senses.Count; i++)
        {
            if (senses[i].HasTag(tag))
            {
                result = Math.Max(result, scores[i]);
            }
        }

        return result;
    }
}
