using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The comparison of two versions of a model on one subset of one set.
/// </summary>
/// <param name="Set">The name of the set.</param>
/// <param name="Subset">The subset: <c>all tokens</c> or <c>ambiguous tokens</c>.</param>
/// <param name="Tokens">The number of paired tokens.</param>
/// <param name="Clusters">The number of clusters the tokens fall in.</param>
/// <param name="BaseCorrect">The tokens the base version gets right.</param>
/// <param name="NewCorrect">The tokens the new version gets right.</param>
/// <param name="Repairs">The tokens the base gets wrong and the new gets right.</param>
/// <param name="Regressions">The tokens the base gets right and the new gets wrong.</param>
/// <param name="Gain">The gain of the new version in percentage points of the tokens.</param>
/// <param name="Lower">The lower end of the bootstrap interval of the gain, in percentage points.</param>
/// <param name="Upper">The upper end of the bootstrap interval of the gain, in percentage points.</param>
/// <param name="McNemarP">The exact two-sided McNemar p-value of the repairs against the regressions.</param>
internal sealed record OutcomeComparison(string Set, string Subset, int Tokens, int Clusters, int BaseCorrect, int NewCorrect, int Repairs, int Regressions, double Gain, double Lower, double Upper, double McNemarP)
{
    /// <summary>
    ///     Gets the accuracy of the base version in percent.
    /// </summary>
    public double BaseAccuracy => Tokens == 0 ? 0 : 100.0 * BaseCorrect / Tokens;

    /// <summary>
    ///     Gets the accuracy of the new version in percent.
    /// </summary>
    public double NewAccuracy => Tokens == 0 ? 0 : 100.0 * NewCorrect / Tokens;
}

/// <summary>
///     One cell of the confusion of a set: the gold part of speech and the part of speech a version answered.
/// </summary>
/// <param name="Set">The name of the set.</param>
/// <param name="Gold">The gold part of speech.</param>
/// <param name="Answer">The answered part of speech.</param>
/// <param name="BaseCount">The tokens of the cell for the base version.</param>
/// <param name="NewCount">The tokens of the cell for the new version.</param>
/// <param name="Tokens">The number of paired tokens of the set.</param>
internal sealed record ConfusionChange(string Set, string Gold, string Answer, int BaseCount, int NewCount, int Tokens)
{
    /// <summary>
    ///     Gets the change of the cell in percentage points of the tokens of the set; positive is more errors.
    /// </summary>
    public double Points => Tokens == 0 ? 0 : 100.0 * (NewCount - BaseCount) / Tokens;
}

/// <summary>
///     The result of comparing two outcome files.
/// </summary>
/// <param name="Comparisons">One entry per set and subset.</param>
/// <param name="Confusions">The changes of the cells of the confusion, one entry for every wrong cell that has tokens in either version.</param>
/// <param name="Unpaired">The tokens that only one of the files has, by set.</param>
/// <param name="Cluster">The cluster unit: <c>sentence</c> or <c>document</c>.</param>
/// <param name="Replicates">The number of bootstrap replicates.</param>
/// <param name="Seed">The seed of the bootstrap.</param>
internal sealed record OutcomeReport(IReadOnlyList<OutcomeComparison> Comparisons, IReadOnlyList<ConfusionChange> Confusions, IReadOnlyDictionary<string, int> Unpaired, string Cluster, int Replicates, int Seed);

/// <summary>
///     Compares two versions of a model on the same tokens: the gain in accuracy with a paired cluster bootstrap interval
///     and an exact McNemar test.
/// </summary>
/// <remarks>
///     <para>
///     The tokens of the two files are paired on the set, the sentence and the token. The statistic is the difference of the
///     accuracies of the new and the base version over the paired tokens. The bootstrap resamples the clusters (the
///     sentences or the documents) with replacement, which keeps the dependence between tokens of one cluster, and
///     recomputes the difference on each replicate; the interval is the 2.5th to the 97.5th percentile of the
///     replicates. The random numbers come from a fixed generator, so the same files and seed give the same interval on
///     every platform. McNemar's test uses the tokens on which exactly one version is right and is exact (binomial), so
///     it ignores the clustering and is reported next to the interval, not instead of it.
///     </para>
/// </remarks>
internal static class OutcomeComparer
{
    /// <summary>
    ///     The default number of bootstrap replicates.
    /// </summary>
    public const int DefaultReplicates = 10_000;

    /// <summary>
    ///     The default seed of the bootstrap.
    /// </summary>
    public const int DefaultSeed = 20261029;

    /// <summary>
    ///     The change of a confusion cell, in percentage points, above which a cell counts as worsened.
    /// </summary>
    public const double WorsenedPoints = 0.3;

    /// <summary>
    ///     Compares two sets of outcomes.
    /// </summary>
    /// <param name="baseOutcomes">The outcomes of the base version.</param>
    /// <param name="newOutcomes">The outcomes of the new version.</param>
    /// <param name="cluster">The cluster unit: <c>sentence</c> or <c>document</c>.</param>
    /// <param name="replicates">The number of bootstrap replicates; at least 1.</param>
    /// <param name="seed">The seed of the bootstrap.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentException">Thrown when the cluster unit is unknown or the files have no token in common.</exception>
    public static OutcomeReport Compare(IReadOnlyList<TokenOutcome> baseOutcomes, IReadOnlyList<TokenOutcome> newOutcomes, string cluster, int replicates, int seed)
    {
        ArgumentNullException.ThrowIfNull(baseOutcomes);
        ArgumentNullException.ThrowIfNull(newOutcomes);
        if (cluster is not ("sentence" or "document"))
        {
            throw new ArgumentException($"The cluster unit must be 'sentence' or 'document', not '{cluster}'.", nameof(cluster));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(replicates, 1);
        var newByKey = new Dictionary<(string, int, int), TokenOutcome>();
        foreach (var o in newOutcomes)
        {
            newByKey[(o.Set, o.Sentence, o.Token)] = o;
        }

        var pairs = new List<(TokenOutcome Base, TokenOutcome New)>();
        var unpaired = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = new HashSet<(string, int, int)>();
        foreach (var o in baseOutcomes)
        {
            var key = (o.Set, o.Sentence, o.Token);
            if (newByKey.TryGetValue(key, out var other))
            {
                pairs.Add((o, other));
                matched.Add(key);
            }
            else
            {
                unpaired[o.Set] = unpaired.GetValueOrDefault(o.Set) + 1;
            }
        }

        foreach (var set in newOutcomes.Where(o => !matched.Contains((o.Set, o.Sentence, o.Token))).Select(o => o.Set))
        {
            unpaired[set] = unpaired.GetValueOrDefault(set) + 1;
        }

        if (pairs.Count == 0)
        {
            throw new ArgumentException("The two outcome files have no token in common (same set, sentence and token).");
        }

        var comparisons = new List<OutcomeComparison>();
        var confusions = new List<ConfusionChange>();
        foreach (var set in pairs.Select(p => p.Base.Set).Distinct(StringComparer.Ordinal))
        {
            var inSet = pairs.Where(p => p.Base.Set == set).ToList();
            comparisons.Add(CompareSubset(set, "all tokens", inSet, cluster, replicates, seed));
            comparisons.Add(CompareSubset(set, "ambiguous tokens", [.. inSet.Where(p => p.Base.Ambiguous)], cluster, replicates, seed));
            confusions.AddRange(ConfusionOf(set, inSet));
        }

        return new OutcomeReport(comparisons, confusions, unpaired, cluster, replicates, seed);
    }

    /// <summary>
    ///     Computes the exact two-sided McNemar p-value: twice the binomial tail of the smaller discordant count at one half.
    /// </summary>
    /// <param name="repairs">The tokens only the new version gets right.</param>
    /// <param name="regressions">The tokens only the base version gets right.</param>
    /// <returns>The p-value, at most 1; 1 when there is no discordant token.</returns>
    public static double McNemar(int repairs, int regressions)
    {
        var n = repairs + regressions;
        if (n == 0)
        {
            return 1;
        }

        var k = Math.Min(repairs, regressions);
        var logFactorial = new double[n + 1];
        for (var i = 2; i <= n; i++)
        {
            logFactorial[i] = logFactorial[i - 1] + Math.Log(i);
        }

        // P(X <= k) for X ~ Binomial(n, 1/2), summed from the largest term in log space
        var terms = Enumerable.Range(0, k + 1).Select(i => logFactorial[n] - logFactorial[i] - logFactorial[n - i] - (n * Math.Log(2))).ToList();
        var max = terms.Max();
        var tail = Math.Exp(max) * terms.Sum(t => Math.Exp(t - max));
        return Math.Min(1.0, 2 * tail);
    }

    /// <summary>
    ///     Renders the report as Markdown.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="baseName">The name of the base version.</param>
    /// <param name="newName">The name of the new version.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(OutcomeReport report, string baseName, string newName)
    {
        ArgumentNullException.ThrowIfNull(report);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("### Paired comparison of outcomes\n\n");
        text.Append("Base: ").Append(baseName).Append("; new: ").Append(newName).Append(". Interval: 95% paired cluster bootstrap by ").Append(report.Cluster)
            .Append(", ").Append(report.Replicates.ToString("N0", inv)).Append(" replicates, seed ").Append(report.Seed.ToString(inv)).Append(".\n\n");
        text.Append("| Set | Subset | Tokens | Clusters | Base | New | Repairs | Regressions | Gain (points) | 95% interval | McNemar p |\n| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |\n");
        foreach (var c in report.Comparisons)
        {
            text.Append("| ").Append(c.Set).Append(" | ").Append(c.Subset).Append(" | ").Append(c.Tokens.ToString("N0", inv)).Append(" | ").Append(c.Clusters.ToString("N0", inv))
                .Append(" | ").Append(c.BaseAccuracy.ToString("0.00", inv)).Append("% | ").Append(c.NewAccuracy.ToString("0.00", inv)).Append("% | ")
                .Append(c.Repairs.ToString("N0", inv)).Append(" | ").Append(c.Regressions.ToString("N0", inv)).Append(" | ")
                .Append(c.Gain.ToString("+0.00;-0.00;0.00", inv)).Append(" | ").Append(c.Lower.ToString("+0.00;-0.00;0.00", inv)).Append(" to ").Append(c.Upper.ToString("+0.00;-0.00;0.00", inv))
                .Append(" | ").Append(c.McNemarP.ToString("0.0e+00", inv)).Append(" |\n");
        }

        text.Append('\n');
        foreach (var (set, count) in report.Unpaired)
        {
            text.Append("Not paired in ").Append(set).Append(": ").Append(count.ToString("N0", inv)).Append(" tokens (left out).\n");
        }

        foreach (var set in report.Confusions.Select(c => c.Set).Distinct(StringComparer.Ordinal))
        {
            var cells = report.Confusions.Where(c => c.Set == set).OrderByDescending(c => Math.Abs(c.NewCount - c.BaseCount)).ThenBy(c => c.Gold, StringComparer.Ordinal).ThenBy(c => c.Answer, StringComparer.Ordinal).Take(8).ToList();
            text.Append("\nLargest changes of the wrong cells (gold to answer) in ").Append(set).Append(":\n\n| Gold | Answer | Base | New | Change (points) |\n| --- | --- | ---: | ---: | ---: |\n");
            foreach (var c in cells)
            {
                text.Append("| ").Append(c.Gold).Append(" | ").Append(c.Answer).Append(" | ").Append(c.BaseCount.ToString("N0", inv)).Append(" | ").Append(c.NewCount.ToString("N0", inv))
                    .Append(" | ").Append(c.Points.ToString("+0.00;-0.00;0.00", inv)).Append(" |\n");
            }

            var worse = report.Confusions.Where(c => c.Set == set && c.Points > WorsenedPoints).ToList();
            text.Append('\n').Append(worse.Count == 0
                ? "No wrong cell grows by more than " + WorsenedPoints.ToString("0.0", inv) + " points.\n"
                : "Wrong cells that grow by more than " + WorsenedPoints.ToString("0.0", inv) + " points: " + string.Join("; ", worse.Select(c => c.Gold + " to " + c.Answer + " (" + c.Points.ToString("+0.00", inv) + ")")) + ".\n");
        }

        return text.ToString();
    }

    private static OutcomeComparison CompareSubset(string set, string subset, List<(TokenOutcome Base, TokenOutcome New)> pairs, string cluster, int replicates, int seed)
    {
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var perCluster = new List<(int Tokens, int Diff)>();
        int baseCorrect = 0, newCorrect = 0, repairs = 0, regressions = 0;
        foreach (var (b, n) in pairs)
        {
            var key = cluster == "sentence" ? b.Sentence.ToString(CultureInfo.InvariantCulture) : b.Document;
            if (!keys.TryGetValue(key, out var index))
            {
                index = perCluster.Count;
                keys[key] = index;
                perCluster.Add((0, 0));
            }

            var diff = (n.Correct ? 1 : 0) - (b.Correct ? 1 : 0);
            perCluster[index] = (perCluster[index].Tokens + 1, perCluster[index].Diff + diff);
            baseCorrect += b.Correct ? 1 : 0;
            newCorrect += n.Correct ? 1 : 0;
            repairs += !b.Correct && n.Correct ? 1 : 0;
            regressions += b.Correct && !n.Correct ? 1 : 0;
        }

        var tokens = pairs.Count;
        var gain = tokens == 0 ? 0 : 100.0 * (newCorrect - baseCorrect) / tokens;
        var (lower, upper) = tokens == 0 ? (0.0, 0.0) : Interval(perCluster, replicates, seed);
        return new OutcomeComparison(set, subset, tokens, perCluster.Count, baseCorrect, newCorrect, repairs, regressions, gain, lower, upper, McNemar(repairs, regressions));
    }

    private static (double Lower, double Upper) Interval(List<(int Tokens, int Diff)> clusters, int replicates, int seed)
    {
        var random = new SplitMix64((ulong)(uint)seed);
        var count = clusters.Count;
        var gains = new double[replicates];
        for (var r = 0; r < replicates; r++)
        {
            long tokens = 0, diff = 0;
            for (var i = 0; i < count; i++)
            {
                var (t, d) = clusters[(int)random.NextBelow((ulong)count)];
                tokens += t;
                diff += d;
            }

            gains[r] = 100.0 * diff / tokens;
        }

        Array.Sort(gains);
        return (gains[(int)Math.Floor(0.025 * (replicates - 1))], gains[(int)Math.Ceiling(0.975 * (replicates - 1))]);
    }

    private static List<ConfusionChange> ConfusionOf(string set, List<(TokenOutcome Base, TokenOutcome New)> pairs)
    {
        var cells = new Dictionary<(string, string), (int Base, int New)>();
        foreach (var (b, n) in pairs)
        {
            if (!b.Correct)
            {
                var key = (b.Gold, b.Answer);
                cells[key] = (cells.GetValueOrDefault(key).Base + 1, cells.GetValueOrDefault(key).New);
            }

            if (!n.Correct)
            {
                var key = (n.Gold, n.Answer);
                cells[key] = (cells.GetValueOrDefault(key).Base, cells.GetValueOrDefault(key).New + 1);
            }
        }

        return [.. cells.OrderBy(c => c.Key.Item1, StringComparer.Ordinal).ThenBy(c => c.Key.Item2, StringComparer.Ordinal).Select(c => new ConfusionChange(set, c.Key.Item1, c.Key.Item2, c.Value.Base, c.Value.New, pairs.Count))];
    }

    /// <summary>
    ///     A small fixed random generator (SplitMix64), so that the bootstrap does not depend on the platform's generator.
    /// </summary>
    private sealed class SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong NextBelow(ulong bound)
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            z ^= z >> 31;
            return z % bound;
        }
    }
}
