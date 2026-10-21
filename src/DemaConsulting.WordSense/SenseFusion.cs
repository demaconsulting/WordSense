using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     Fuses the part-of-speech posterior of the tagger with the context cue scores into one probability per candidate
///     analysis.
/// </summary>
/// <remarks>
///     The candidates are analyses (lemma, part of speech and features), never senses. Several may share a lemma and a
///     part of speech (a slot, the unit that owns the senses): they then share the same posterior share and only the cue
///     scores separate them. The fused score of a
///     candidate is <c>w * ln(max(p, 1e-6)) + cue / temperature</c> where <c>p</c> is the posterior
///     of the candidate's part of speech renormalized over the candidates and <c>cue</c> is its cue score (the mean of
///     the available scores when the candidate has none, zero when no candidate has one). The result is the softmax of
///     the scores, with one correction: the candidates of one slot that have no cue score are not separated by the
///     context, so together they carry the weight of a single candidate (each one the fraction one over their number),
///     and a slot is never favored merely because its analyses differ in features. The probability of a slot is the sum
///     of the probabilities of its candidates.
/// </remarks>
internal static class SenseFusion
{
    private const double PosteriorFloor = 1e-6;

    // The smallest relative weight of a candidate before normalization, so that no probability underflows to zero
    private const double MinimumWeight = 1e-300;

    /// <summary>
    ///     Computes the fused probability of each candidate.
    /// </summary>
    /// <param name="posteriors">The tagger posterior of the word, <see cref="PosTag.Count"/> values.</param>
    /// <param name="analyses">The candidate analyses.</param>
    /// <param name="cues">The cue score of each candidate, <see cref="double.NaN"/> when missing; not modified.</param>
    /// <param name="tagWeight">The weight of the logarithm of the part-of-speech posterior.</param>
    /// <param name="invTemperature">The factor on the cue scores.</param>
    /// <param name="probabilities">Receives the probability of each candidate, all above zero; the values sum to 1.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="analyses"/> is the default array.</exception>
    public static void Fuse(
        ReadOnlySpan<double> posteriors,
        ImmutableArray<WordDetails> analyses,
        ReadOnlySpan<double> cues,
        double tagWeight,
        double invTemperature,
        Span<double> probabilities)
    {
        if (analyses.IsDefault)
        {
            throw new ArgumentNullException(nameof(analyses));
        }
        var count = analyses.Length;
        Span<double> mass = stackalloc double[PosTag.Count];
        mass.Clear();
        foreach (var analysis in analyses)
        {
            var pos = PosTag.Of(analysis.PartOfSpeech);
            mass[pos] = posteriors[pos];
        }

        var total = 0.0;
        foreach (var value in mass)
        {
            total += value;
        }

        var finiteSum = 0.0;
        var finite = 0;
        for (var j = 0; j < count; j++)
        {
            if (double.IsFinite(cues[j]))
            {
                finiteSum += cues[j];
                finite++;
            }
        }

        var fill = finite > 0 ? finiteSum / finite : 0.0;
        var best = double.NegativeInfinity;
        for (var j = 0; j < count; j++)
        {
            var p = total > 0 ? posteriors[PosTag.Of(analyses[j].PartOfSpeech)] / total : 1.0 / count;
            var cue = double.IsFinite(cues[j]) ? cues[j] : fill;
            probabilities[j] = (tagWeight * Math.Log(Math.Max(p, PosteriorFloor))) + (cue * invTemperature);
            best = Math.Max(best, probabilities[j]);
        }

        var sum = 0.0;
        for (var j = 0; j < count; j++)
        {
            // The weight never underflows to zero: every candidate keeps a probability above zero
            probabilities[j] = Math.Max(Math.Exp(probabilities[j] - best), MinimumWeight);
        }

        // The candidates of one slot without a cue score share the weight of one candidate
        for (var j = 0; j < count; j++)
        {
            if (!double.IsFinite(cues[j]))
            {
                probabilities[j] /= Unscored(analyses, cues, j);
            }

            sum += probabilities[j];
        }

        for (var j = 0; j < count; j++)
        {
            probabilities[j] /= sum;
        }
    }

    /// <summary>
    ///     Counts the candidates that have the lemma and the part of speech of a candidate and no cue score.
    /// </summary>
    /// <param name="analyses">The candidate analyses.</param>
    /// <param name="cues">The cue scores, <see cref="double.NaN"/> when missing.</param>
    /// <param name="index">The candidate.</param>
    /// <returns>The number of candidates of its slot without a cue score (at least 1: the candidate itself).</returns>
    private static int Unscored(ImmutableArray<WordDetails> analyses, ReadOnlySpan<double> cues, int index)
    {
        var count = 0;
        for (var k = 0; k < analyses.Length; k++)
        {
            if (!double.IsFinite(cues[k]) && SameSlot(analyses[k], analyses[index]))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    ///     Tells whether two analyses have the same lemma and part of speech, so that they share their senses.
    /// </summary>
    /// <param name="a">An analysis.</param>
    /// <param name="b">Another analysis.</param>
    /// <returns>True when lemma and part of speech are equal.</returns>
    internal static bool SameSlot(WordDetails a, WordDetails b) =>
        a.PartOfSpeech == b.PartOfSpeech && string.Equals(a.Lemma, b.Lemma, StringComparison.Ordinal);

    /// <summary>
    ///     Gets the index of the largest probability, preferring the lowest index on ties.
    /// </summary>
    /// <param name="probabilities">The probabilities.</param>
    /// <returns>The index of the winner.</returns>
    public static int Winner(ReadOnlySpan<double> probabilities)
    {
        var best = 0;
        for (var j = 1; j < probabilities.Length; j++)
        {
            if (probabilities[j] > probabilities[best])
            {
                best = j;
            }
        }

        return best;
    }
}
