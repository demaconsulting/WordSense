using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One point of a learning curve: the accuracy of a lemma on one evaluation set when trained on a number of anchors.
/// </summary>
/// <param name="Lemma">The lemma, or <c>(macro)</c> for the aggregate over all lemmas.</param>
/// <param name="Set">The evaluation set.</param>
/// <param name="K">The anchors per sense (<c>2</c>, <c>4</c>, ...) or <c>all</c>.</param>
/// <param name="Anchors">The mean number of anchors per sense actually used.</param>
/// <param name="Subsets">The number of random subsets averaged.</param>
/// <param name="N">The number of evaluation rows.</param>
/// <param name="MeanAccuracy">The best-sense accuracy averaged over the subsets.</param>
/// <param name="MinAccuracy">The lowest subset accuracy.</param>
/// <param name="MaxAccuracy">The highest subset accuracy.</param>
/// <param name="Correct">The correct rows summed over the subsets.</param>
/// <param name="Answered">The answered rows summed over the subsets.</param>
/// <param name="AnsweredCorrect">The correct answered rows summed over the subsets.</param>
internal sealed record CurvePoint(
    string Lemma,
    string Set,
    string K,
    double Anchors,
    int Subsets,
    int N,
    double MeanAccuracy,
    double MinAccuracy,
    double MaxAccuracy,
    long Correct,
    long Answered,
    long AnsweredCorrect)
{
    /// <summary>
    ///     Gets the number of row evaluations (rows times subsets).
    /// </summary>
    public long Trials => (long)N * Subsets;
}

/// <summary>
///     The plateau estimate of one learning curve.
/// </summary>
/// <param name="Lemma">The lemma, or <c>(macro)</c>.</param>
/// <param name="Set">The evaluation set.</param>
/// <param name="AnchorsLast">The anchors per sense at the last point.</param>
/// <param name="AnchorsPrevious">The anchors per sense at the earlier point.</param>
/// <param name="AccuracyLast">The accuracy at the last point.</param>
/// <param name="AccuracyPrevious">The accuracy at the earlier point.</param>
/// <param name="Slope">The accuracy gain per added anchor per sense.</param>
/// <param name="Gain2x">The estimated gain from doubling the anchors, limited to the remaining error.</param>
/// <param name="Plateaued"><c>yes</c>, <c>no</c> or <c>n/a</c> when no earlier point is far enough away.</param>
internal sealed record PlateauRow(
    string Lemma,
    string Set,
    double AnchorsLast,
    double AnchorsPrevious,
    double AccuracyLast,
    double AccuracyPrevious,
    double Slope,
    double Gain2x,
    string Plateaued);

/// <summary>
///     The <c>learning-curve</c> command: how accuracy grows with the number of anchors per sense.
/// </summary>
/// <remarks>
///     For every lemma and every anchor count in <see cref="Counts"/> (and all anchors) a number of random anchor
///     subsets is drawn with a seed derived from a stable hash of the lemma, the count and the subset index; a
///     classifier is trained on each subset with the settings of the table build and evaluated on every evaluation
///     set. Abstaining uses the lemma's margin chosen for the full anchor set, so the answered rate is comparable
///     across counts.
/// </remarks>
internal static class LearningCurve
{
    /// <summary>
    ///     The anchors per sense that are evaluated before the full anchor set.
    /// </summary>
    public static readonly int[] Counts = [2, 4, 7, 10, 14];

    /// <summary>
    ///     The label of the point that uses every anchor.
    /// </summary>
    public const string All = "all";

    /// <summary>
    ///     The default number of random subsets per anchor count.
    /// </summary>
    public const int DefaultSubsets = 5;

    /// <summary>
    ///     The default seed mixed into the subset seeds.
    /// </summary>
    public const int DefaultSeed = 20260101;

    /// <summary>
    ///     The accuracy gain per added anchor per sense below which a curve counts as plateaued.
    /// </summary>
    public const double PlateauSlope = 0.002;

    /// <summary>
    ///     The smallest difference in anchors per sense between the two points the plateau slope is taken from.
    /// </summary>
    public const double MinPlateauSpan = 3.0;

    /// <summary>
    ///     The name of the aggregate rows.
    /// </summary>
    public const string Macro = "(macro)";

    /// <summary>
    ///     Computes a stable 32-bit hash of a lemma, an anchor count and a subset index (FNV-1a over UTF-8 bytes);
    ///     unlike <see cref="string.GetHashCode()"/> it does not change between processes.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="k">The anchors per sense.</param>
    /// <param name="subset">The subset index.</param>
    /// <returns>The hash.</returns>
    public static uint StableHash(string lemma, int k, int subset)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes($"{lemma}\0{k}\0{subset}"))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }

    /// <summary>
    ///     Runs the learning curves of every lemma.
    /// </summary>
    /// <param name="data">The featurized corpus.</param>
    /// <param name="subsets">The number of random subsets per anchor count.</param>
    /// <param name="seed">The seed mixed into every subset seed.</param>
    /// <returns>The points per lemma, set and anchor count, in ordinal lemma order.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="subsets"/> is less than one.</exception>
    public static IReadOnlyList<CurvePoint> Run(AnalysisData data, int subsets, int seed)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (subsets < 1)
        {
            throw new ArgumentException("Option --subsets must be at least 1.");
        }

        var perLemma = new List<CurvePoint>[data.Lemmas.Count];
        Parallel.For(0, data.Lemmas.Count, i => perLemma[i] = CurveOf(data.Lemmas[i], data.C, subsets, seed));
        return perLemma.SelectMany(p => p).ToList();
    }

    /// <summary>
    ///     Draws the anchors of one subset: up to <paramref name="k"/> per sense, chosen at random.
    /// </summary>
    /// <param name="bySense">The anchor indexes of each sense, in ascending order.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="k">The anchors per sense; a sense with fewer anchors contributes all of them.</param>
    /// <param name="subset">The subset index.</param>
    /// <param name="seed">The seed mixed into the subset seed.</param>
    /// <returns>The chosen anchor indexes in ascending order within each sense.</returns>
    public static int[] Draw(IReadOnlyList<int[]> bySense, string lemma, int k, int subset, int seed)
    {
        var state = StableHash(lemma, k, subset) ^ unchecked((uint)seed);
        var chosen = new List<int>();
        foreach (var anchors in bySense)
        {
            if (anchors.Length <= k)
            {
                chosen.AddRange(anchors);
                continue;
            }

            var pool = anchors.ToArray();
            for (var j = 0; j < k; j++)
            {
                var other = j + NextBelow(ref state, pool.Length - j);
                (pool[j], pool[other]) = (pool[other], pool[j]);
            }

            chosen.AddRange(pool[..k].Order());
        }

        return [.. chosen];
    }

    /// <summary>
    ///     Advances a SplitMix32 generator and returns a value below a bound; the sequence is fixed by the seed, so
    ///     subsets do not depend on the platform or the runtime version.
    /// </summary>
    /// <param name="state">The generator state.</param>
    /// <param name="bound">The exclusive upper bound; positive.</param>
    /// <returns>A value in <c>[0, bound)</c>.</returns>
    private static int NextBelow(ref uint state, int bound)
    {
        unchecked
        {
            state += 0x9E3779B9u;
            var z = state;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            z ^= z >> 16;
            return (int)(((ulong)z * (ulong)bound) >> 32);
        }
    }

    private static List<CurvePoint> CurveOf(LemmaAnalysis lemma, double c, int subsets, int seed)
    {
        var bySense = Enumerable.Range(0, lemma.Senses.Length)
            .Select(s => Enumerable.Range(0, lemma.AnchorSense.Length).Where(a => lemma.AnchorSense[a] == s).ToArray()).ToList();
        var largest = bySense.Max(a => a.Length);
        var full = Evaluate(lemma, lemma.Train(Enumerable.Range(0, lemma.AnchorSense.Length).ToArray(), c));
        var steps = new List<(string Label, int K, List<Dictionary<string, Tally>> Runs)>();
        foreach (var k in Counts)
        {
            if (k >= largest)
            {
                // Every sense has at most k anchors, so the subset is the full anchor set
                steps.Add((AnalysisFiles.Int(k), k, [full]));
                continue;
            }

            var runs = new List<Dictionary<string, Tally>>();
            for (var i = 0; i < subsets; i++)
            {
                runs.Add(Evaluate(lemma, lemma.Train(Draw(bySense, lemma.Lemma, k, i, seed), c)));
            }

            steps.Add((AnalysisFiles.Int(k), k, runs));
        }

        steps.Add((All, int.MaxValue, [full]));
        var points = new List<CurvePoint>();
        foreach (var set in AnalysisFiles.Sets)
        {
            foreach (var (label, k, runs) in steps)
            {
                var tallies = runs.Select(r => r.GetValueOrDefault(set, new Tally(0, 0, 0, 0))).ToList();
                var n = tallies[0].N;
                if (n == 0)
                {
                    continue;
                }

                var accuracies = tallies.Select(t => (double)t.Correct / n).ToList();
                var anchors = bySense.Average(a => (double)Math.Min(a.Length, k));
                points.Add(new CurvePoint(
                    lemma.Lemma, set, label, anchors, runs.Count, n, accuracies.Average(), accuracies.Min(), accuracies.Max(),
                    tallies.Sum(t => (long)t.Correct), tallies.Sum(t => (long)t.Answered), tallies.Sum(t => (long)t.AnsweredCorrect)));
            }
        }

        return points;
    }

    private static Dictionary<string, Tally> Evaluate(LemmaAnalysis lemma, DemaConsulting.WordSense.LemmaModel model)
    {
        var result = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var row in lemma.Eval)
        {
            var (best, margin) = model.Score(row.Feature);
            var right = best == row.SenseIndex;
            var answered = margin >= lemma.Margin;
            foreach (var set in row.Memberships)
            {
                var old = result.GetValueOrDefault(set, new Tally(0, 0, 0, 0));
                result[set] = new Tally(old.N + 1, old.Correct + (right ? 1 : 0), old.Answered + (answered ? 1 : 0), old.AnsweredCorrect + (answered && right ? 1 : 0));
            }
        }

        return result;
    }

    /// <summary>
    ///     Aggregates the points of every lemma into one macro and micro point per set and anchor count.
    /// </summary>
    /// <param name="points">The lemma points.</param>
    /// <returns>Rows of set, count, lemma count, rows, macro and micro accuracy and answered rates, in report order.</returns>
    public static IReadOnlyList<SummaryRow> Summarize(IReadOnlyList<CurvePoint> points)
    {
        var rows = new List<SummaryRow>();
        foreach (var set in AnalysisFiles.Sets)
        {
            foreach (var k in KeyOrder())
            {
                var group = points.Where(p => p.Set == set && p.K == k).ToList();
                if (group.Count == 0)
                {
                    continue;
                }

                var trials = group.Sum(p => p.Trials);
                var answered = group.Sum(p => p.Answered);
                rows.Add(new SummaryRow(
                    set, k, group.Average(p => p.Anchors), group.Count, group.Sum(p => (long)p.N), group.Average(p => p.MeanAccuracy),
                    (double)group.Sum(p => p.Correct) / trials, (double)answered / trials,
                    answered == 0 ? double.NaN : (double)group.Sum(p => p.AnsweredCorrect) / answered));
            }
        }

        return rows;
    }

    /// <summary>
    ///     Estimates the plateau of a curve from its last two points that are far enough apart.
    /// </summary>
    /// <param name="lemma">The lemma or <see cref="Macro"/>.</param>
    /// <param name="set">The set.</param>
    /// <param name="curve">The (anchors, accuracy) points in ascending anchor order.</param>
    /// <returns>The estimate, with <c>n/a</c> when no earlier point is at least <see cref="MinPlateauSpan"/> anchors away.</returns>
    public static PlateauRow EstimatePlateau(string lemma, string set, IReadOnlyList<(double Anchors, double Accuracy)> curve)
    {
        var last = curve[^1];
        var previousIndex = -1;
        for (var i = curve.Count - 2; i >= 0; i--)
        {
            if (last.Anchors - curve[i].Anchors >= MinPlateauSpan)
            {
                previousIndex = i;
                break;
            }
        }

        if (previousIndex < 0)
        {
            return new PlateauRow(lemma, set, last.Anchors, last.Anchors, last.Accuracy, last.Accuracy, 0, 0, "n/a");
        }

        var previous = curve[previousIndex];
        var slope = (last.Accuracy - previous.Accuracy) / (last.Anchors - previous.Anchors);

        // Doubling the anchors adds as many anchors per sense as there are now
        var gain = Math.Clamp(slope * last.Anchors, 0, 1 - last.Accuracy);
        return new PlateauRow(lemma, set, last.Anchors, previous.Anchors, last.Accuracy, previous.Accuracy, slope, gain, slope < PlateauSlope ? "yes" : "no");
    }

    /// <summary>
    ///     Estimates the plateau of every lemma curve and of the macro curves.
    /// </summary>
    /// <param name="points">The lemma points.</param>
    /// <param name="summary">The aggregated points.</param>
    /// <returns>The lemma rows in ordinal order, then the macro rows.</returns>
    public static IReadOnlyList<PlateauRow> Plateaus(IReadOnlyList<CurvePoint> points, IReadOnlyList<SummaryRow> summary)
    {
        var rows = new List<PlateauRow>();
        foreach (var group in points.GroupBy(p => (p.Lemma, p.Set)).OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => Array.IndexOf(AnalysisFiles.Sets, g.Key.Set)))
        {
            rows.Add(EstimatePlateau(group.Key.Lemma, group.Key.Set, group.OrderBy(p => p.Anchors).Select(p => (p.Anchors, p.MeanAccuracy)).ToList()));
        }

        foreach (var set in AnalysisFiles.Sets)
        {
            var curve = summary.Where(s => s.Set == set).OrderBy(s => s.Anchors).Select(s => (s.Anchors, s.MacroAccuracy)).ToList();
            if (curve.Count > 0)
            {
                rows.Add(EstimatePlateau(Macro, set, curve));
            }
        }

        return rows;
    }

    private static IEnumerable<string> KeyOrder() => Counts.Select(k => AnalysisFiles.Int(k)).Append(All);

    /// <summary>
    ///     Renders the per-lemma points as TSV.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <returns>The file content.</returns>
    public static string RenderCurve(IReadOnlyList<CurvePoint> points)
    {
        var text = new StringBuilder("lemma\tset\tk\tanchors\tsubsets\tn_eval\tmean_acc\tmin_acc\tmax_acc\tanswered\tanswered_acc\n");
        foreach (var p in points)
        {
            var answeredRate = (double)p.Answered / p.Trials;
            var answeredAccuracy = p.Answered == 0 ? "n/a" : AnalysisFiles.Num((double)p.AnsweredCorrect / p.Answered);
            text.Append($"{p.Lemma}\t{p.Set}\t{p.K}\t{AnalysisFiles.Num(p.Anchors)}\t{p.Subsets}\t{p.N}\t{AnalysisFiles.Num(p.MeanAccuracy)}\t{AnalysisFiles.Num(p.MinAccuracy)}\t{AnalysisFiles.Num(p.MaxAccuracy)}\t{AnalysisFiles.Num(answeredRate)}\t{answeredAccuracy}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the aggregated points as TSV.
    /// </summary>
    /// <param name="summary">The aggregated points.</param>
    /// <returns>The file content.</returns>
    public static string RenderSummary(IReadOnlyList<SummaryRow> summary)
    {
        var text = new StringBuilder("set\tk\tanchors\tlemmas\tn_eval\tmacro_acc\tmicro_acc\tmicro_answered\tmicro_answered_acc\n");
        foreach (var s in summary)
        {
            var answeredAccuracy = double.IsNaN(s.MicroAnsweredAccuracy) ? "n/a" : AnalysisFiles.Num(s.MicroAnsweredAccuracy);
            text.Append($"{s.Set}\t{s.K}\t{AnalysisFiles.Num(s.Anchors)}\t{s.Lemmas}\t{s.N}\t{AnalysisFiles.Num(s.MacroAccuracy)}\t{AnalysisFiles.Num(s.MicroAccuracy)}\t{AnalysisFiles.Num(s.MicroAnswered)}\t{answeredAccuracy}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the plateau estimates as TSV.
    /// </summary>
    /// <param name="plateaus">The estimates.</param>
    /// <returns>The file content.</returns>
    public static string RenderPlateau(IReadOnlyList<PlateauRow> plateaus)
    {
        var text = new StringBuilder("lemma\tset\tanchors_last\tanchors_previous\tacc_last\tacc_previous\tslope_per_anchor\tgain_at_2x\tplateaued\n");
        foreach (var p in plateaus)
        {
            text.Append($"{p.Lemma}\t{p.Set}\t{AnalysisFiles.Num(p.AnchorsLast)}\t{AnalysisFiles.Num(p.AnchorsPrevious)}\t{AnalysisFiles.Num(p.AccuracyLast)}\t{AnalysisFiles.Num(p.AccuracyPrevious)}\t{AnalysisFiles.Num(p.Slope)}\t{AnalysisFiles.Num(p.Gain2x)}\t{p.Plateaued}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Runs the command and writes the three files into the output directory.
    /// </summary>
    /// <param name="data">The featurized corpus.</param>
    /// <param name="output">The output directory.</param>
    /// <param name="subsets">The number of random subsets per anchor count.</param>
    /// <param name="seed">The seed mixed into every subset seed.</param>
    /// <returns>The number of lemmas analyzed.</returns>
    public static int RunAndWrite(AnalysisData data, string output, int subsets, int seed)
    {
        var points = Run(data, subsets, seed);
        var summary = Summarize(points);
        AnalysisFiles.Write(output, AnalysisFiles.Curve, RenderCurve(points));
        AnalysisFiles.Write(output, AnalysisFiles.CurveSummary, RenderSummary(summary));
        AnalysisFiles.Write(output, AnalysisFiles.Plateau, RenderPlateau(Plateaus(points, summary)));
        return data.Lemmas.Count;
    }

    private readonly record struct Tally(int N, int Correct, int Answered, int AnsweredCorrect);
}

/// <summary>
///     One aggregated point of the learning curve.
/// </summary>
/// <param name="Set">The evaluation set.</param>
/// <param name="K">The anchors per sense or <c>all</c>.</param>
/// <param name="Anchors">The mean anchors per sense over the lemmas.</param>
/// <param name="Lemmas">The number of lemmas with rows in the set.</param>
/// <param name="N">The number of evaluation rows.</param>
/// <param name="MacroAccuracy">The mean of the lemma accuracies.</param>
/// <param name="MicroAccuracy">The accuracy over all rows.</param>
/// <param name="MicroAnswered">The share of answered rows.</param>
/// <param name="MicroAnsweredAccuracy">The accuracy of the answered rows, or NaN when none is answered.</param>
internal sealed record SummaryRow(
    string Set,
    string K,
    double Anchors,
    int Lemmas,
    long N,
    double MacroAccuracy,
    double MicroAccuracy,
    double MicroAnswered,
    double MicroAnsweredAccuracy);
