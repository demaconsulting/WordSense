using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     Fuses the part-of-speech posterior of the tagger with the context cue scores into one probability per candidate
///     analysis.
/// </summary>
/// <remarks>
///     The candidates are analyses (lemma, part of speech and features), never senses, and several may share a part of
///     speech: they then share the same posterior share and only the cue scores separate them. The fused score of a
///     candidate is <c>w * ln(max(p, 1e-6)) + cue / temperature</c> where <c>p</c> is the posterior
///     of the candidate's part of speech renormalized over the candidates and <c>cue</c> is its cue score (the mean of
///     the available scores when the candidate has none, zero when no candidate has one). The result is the softmax of
///     the scores, so the confidence of a decision is the probability of the winner.
/// </remarks>
internal static class SenseFusion
{
    private const double PosteriorFloor = 1e-6;

    /// <summary>
    ///     Computes the fused probability of each candidate.
    /// </summary>
    /// <param name="posteriors">The tagger posterior of the word, <see cref="PosTag.Count"/> values.</param>
    /// <param name="analyses">The candidate analyses.</param>
    /// <param name="cues">The cue score of each candidate, <see cref="double.NaN"/> when missing; modified in place.</param>
    /// <param name="tagWeight">The weight of the logarithm of the part-of-speech posterior.</param>
    /// <param name="invTemperature">The factor on the cue scores.</param>
    /// <param name="probabilities">Receives the probability of each candidate; the values sum to 1.</param>
    public static void Fuse(
        ReadOnlySpan<double> posteriors,
        ImmutableArray<WordDetails> analyses,
        Span<double> cues,
        double tagWeight,
        double invTemperature,
        Span<double> probabilities)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(analyses.IsDefault, true);
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
            probabilities[j] = Math.Exp(probabilities[j] - best);
            sum += probabilities[j];
        }

        for (var j = 0; j < count; j++)
        {
            probabilities[j] /= sum;
        }
    }

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
