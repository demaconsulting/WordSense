using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One ranked weak lemma.
/// </summary>
/// <param name="Rank">The rank, starting at 1 for the weakest lemma.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="N">The holdout and real-evaluation rows.</param>
/// <param name="HoldoutAccuracy">The best-sense accuracy on the holdout.</param>
/// <param name="RealN">The real-text rows (real holdout rows and real-evaluation rows).</param>
/// <param name="RealAccuracy">The best-sense accuracy on the real-text rows; the holdout accuracy when there are none.</param>
/// <param name="AbstainRate">The share of rows the classifier did not answer.</param>
/// <param name="Errors">The misclassified or abstained rows.</param>
/// <param name="CoverageGap">The errors of class <c>coverage-gap</c>.</param>
/// <param name="Overlap">The errors of class <c>overlap</c>.</param>
/// <param name="Ambiguous">The errors of class <c>ambiguous</c>.</param>
/// <param name="Plateaued">Whether the holdout learning curve is plateaued: <c>yes</c>, <c>no</c> or <c>n/a</c>.</param>
/// <param name="Gain2x">The extrapolated gain from doubling the anchors.</param>
/// <param name="Score">The weakness score; higher is weaker.</param>
/// <param name="Recommendation">The recommended action.</param>
/// <param name="ExpectedGain">The estimated gain in accuracy if the recommendation succeeds.</param>
internal sealed record WeakLemma(
    int Rank,
    string Lemma,
    int N,
    double HoldoutAccuracy,
    int RealN,
    double RealAccuracy,
    double AbstainRate,
    int Errors,
    int CoverageGap,
    int Overlap,
    int Ambiguous,
    string Plateaued,
    double Gain2x,
    double Score,
    string Recommendation,
    double ExpectedGain);

/// <summary>
///     The <c>weak-lemmas</c> command: ranks the lemmas that need work, from the diagnosis and the learning curve.
/// </summary>
internal static class WeakLemmas
{
    /// <summary>
    ///     The holdout best-sense accuracy below which a lemma is weak.
    /// </summary>
    public const double WeakAccuracy = 0.95;

    /// <summary>
    ///     The real-text best-sense accuracy below which a lemma is weak.
    /// </summary>
    public const double WeakRealAccuracy = 0.90;

    /// <summary>
    ///     The smallest number of real-text rows from which the real-text accuracy counts.
    /// </summary>
    public const int MinRealRows = 3;

    /// <summary>
    ///     The recommendation for a lemma whose errors are mostly coverage gaps.
    /// </summary>
    public const string MoreSentences = "more-sentences";

    /// <summary>
    ///     The recommendation for a lemma whose errors are mostly overlaps and whose curve still rises.
    /// </summary>
    public const string ConsumerRule = "consumer-rule";

    /// <summary>
    ///     The recommendation for a lemma whose errors are mostly overlaps and whose curve is plateaued.
    /// </summary>
    public const string StrongerEncoder = "stronger-encoder";

    /// <summary>
    ///     The recommendation for a lemma whose errors are mostly ambiguous (label-noise suspects).
    /// </summary>
    public const string FixLabels = "fix-labels";

    /// <summary>
    ///     Ranks the weak lemmas.
    /// </summary>
    /// <param name="counts">The error counts per lemma and set.</param>
    /// <param name="plateaus">The plateau estimates.</param>
    /// <returns>The weak lemmas, weakest first; ties are broken by the error count and then the lemma.</returns>
    public static IReadOnlyList<WeakLemma> Rank(IReadOnlyList<LemmaErrorCounts> counts, IReadOnlyList<PlateauRow> plateaus)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(plateaus);
        var candidates = new List<WeakLemma>();
        foreach (var group in counts.GroupBy(c => c.Lemma, StringComparer.Ordinal))
        {
            var holdout = group.FirstOrDefault(c => c.Set == AnalysisFiles.SetHoldout);
            var real = group.FirstOrDefault(c => c.Set == AnalysisFiles.SetReal);
            var eval = group.FirstOrDefault(c => c.Set == AnalysisFiles.SetRealEval);
            var n = (holdout?.N ?? 0) + (eval?.N ?? 0);
            var holdoutAccuracy = holdout is { N: > 0 } ? (double)holdout.Correct / holdout.N : 1.0;
            var realN = (real?.N ?? 0) + (eval?.N ?? 0);
            var realAccuracy = realN > 0 ? (double)((real?.Correct ?? 0) + (eval?.Correct ?? 0)) / realN : holdoutAccuracy;
            var realCounts = realN >= MinRealRows;
            var weak = (holdout is { N: > 0 } && holdoutAccuracy < WeakAccuracy) || (realCounts && realAccuracy < WeakRealAccuracy);
            if (!weak)
            {
                continue;
            }

            var errors = (holdout?.Errors ?? 0) + (eval?.Errors ?? 0);
            var gap = (holdout?.CoverageGap ?? 0) + (eval?.CoverageGap ?? 0);
            var overlap = (holdout?.Overlap ?? 0) + (eval?.Overlap ?? 0);
            var ambiguous = (holdout?.Ambiguous ?? 0) + (eval?.Ambiguous ?? 0);
            var abstained = (holdout?.Abstained ?? 0) + (eval?.Abstained ?? 0);
            var plateau = plateaus.FirstOrDefault(p => p.Lemma == group.Key && p.Set == AnalysisFiles.SetHoldout);
            var plateaued = plateau?.Plateaued ?? "n/a";
            var gain2x = plateau?.Gain2x ?? 0;
            var score = realCounts ? (0.5 * (1 - holdoutAccuracy)) + (0.5 * (1 - realAccuracy)) : 1 - holdoutAccuracy;
            var (recommendation, expected) = Recommend(n, gap, overlap, ambiguous, plateaued, gain2x);
            candidates.Add(new WeakLemma(
                0, group.Key, n, holdoutAccuracy, realN, realAccuracy, n == 0 ? 0 : (double)abstained / n, errors, gap, overlap, ambiguous, plateaued, gain2x, score, recommendation, expected));
        }

        return candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Errors).ThenBy(c => c.Lemma, StringComparer.Ordinal)
            .Select((c, i) => c with { Rank = i + 1 }).ToList();
    }

    /// <summary>
    ///     Chooses the recommendation from the dominant error class.
    /// </summary>
    /// <param name="n">The evaluation rows.</param>
    /// <param name="gap">The coverage-gap errors.</param>
    /// <param name="overlap">The overlap errors.</param>
    /// <param name="ambiguous">The ambiguous errors.</param>
    /// <param name="plateaued">Whether the learning curve is plateaued.</param>
    /// <param name="gain2x">The extrapolated gain from doubling the anchors.</param>
    /// <returns>
    ///     The recommendation and the expected gain: for coverage gaps the smaller of the gap share of the rows and the
    ///     learning-curve gain; for overlaps and ambiguous rows the share of rows, an upper bound. A tie between classes
    ///     goes to coverage gap, then overlap, then ambiguous.
    /// </returns>
    public static (string Recommendation, double ExpectedGain) Recommend(int n, int gap, int overlap, int ambiguous, string plateaued, double gain2x)
    {
        var share = n == 0 ? 0.0 : 1.0 / n;
        if (gap >= overlap && gap >= ambiguous)
        {
            return (MoreSentences, Math.Min(gap * share, gain2x));
        }

        if (overlap >= ambiguous)
        {
            return (plateaued == "yes" ? StrongerEncoder : ConsumerRule, overlap * share);
        }

        return (FixLabels, ambiguous * share);
    }

    /// <summary>
    ///     Renders the ranked list as TSV.
    /// </summary>
    /// <param name="ranked">The ranked weak lemmas.</param>
    /// <returns>The file content.</returns>
    public static string Render(IReadOnlyList<WeakLemma> ranked)
    {
        var text = new StringBuilder("rank\tlemma\tn_eval\tholdout_acc\treal_n\treal_acc\tabstain_rate\terrors\tcoverage_gap\toverlap\tambiguous\tplateaued\tgain_at_2x\tscore\trecommendation\texpected_gain\n");
        foreach (var w in ranked)
        {
            text.Append($"{w.Rank}\t{w.Lemma}\t{w.N}\t{AnalysisFiles.Num(w.HoldoutAccuracy)}\t{w.RealN}\t{AnalysisFiles.Num(w.RealAccuracy)}\t{AnalysisFiles.Num(w.AbstainRate)}\t{w.Errors}\t{w.CoverageGap}\t{w.Overlap}\t{w.Ambiguous}\t{w.Plateaued}\t{AnalysisFiles.Num(w.Gain2x)}\t{AnalysisFiles.Num(w.Score)}\t{w.Recommendation}\t{AnalysisFiles.Num(w.ExpectedGain)}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads the error counts written by <c>error-diagnosis</c>.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <returns>The counts.</returns>
    public static List<LemmaErrorCounts> ReadCounts(string directory)
    {
        const string name = AnalysisFiles.ErrorLemmas;
        return AnalysisFiles.Read(directory, name, 9).Select(c => new LemmaErrorCounts(
            c[0], c[1], AnalysisFiles.ParseInt(c[2], name), AnalysisFiles.ParseInt(c[3], name), AnalysisFiles.ParseInt(c[4], name),
            AnalysisFiles.ParseInt(c[5], name), AnalysisFiles.ParseInt(c[6], name), AnalysisFiles.ParseInt(c[7], name), AnalysisFiles.ParseInt(c[8], name))).ToList();
    }

    /// <summary>
    ///     Reads the plateau estimates written by <c>learning-curve</c>.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <returns>The estimates.</returns>
    public static List<PlateauRow> ReadPlateaus(string directory)
    {
        const string name = AnalysisFiles.Plateau;
        return AnalysisFiles.Read(directory, name, 9).Select(c => new PlateauRow(
            c[0], c[1], AnalysisFiles.ParseNum(c[2], name), AnalysisFiles.ParseNum(c[3], name), AnalysisFiles.ParseNum(c[4], name),
            AnalysisFiles.ParseNum(c[5], name), AnalysisFiles.ParseNum(c[6], name), AnalysisFiles.ParseNum(c[7], name), c[8])).ToList();
    }

    /// <summary>
    ///     Reads the aggregated learning curve written by <c>learning-curve</c>.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <returns>The aggregated points.</returns>
    public static List<SummaryRow> ReadSummary(string directory)
    {
        const string name = AnalysisFiles.CurveSummary;
        return AnalysisFiles.Read(directory, name, 9).Select(c => new SummaryRow(
            c[0], c[1], AnalysisFiles.ParseNum(c[2], name), AnalysisFiles.ParseInt(c[3], name), AnalysisFiles.ParseInt(c[4], name),
            AnalysisFiles.ParseNum(c[5], name), AnalysisFiles.ParseNum(c[6], name), AnalysisFiles.ParseNum(c[7], name),
            c[8] == "n/a" ? double.NaN : AnalysisFiles.ParseNum(c[8], name))).ToList();
    }

    /// <summary>
    ///     Reads the class statistics written by <c>error-diagnosis</c>.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <returns>The statistics.</returns>
    public static List<ClassStatistics> ReadStatistics(string directory)
    {
        const string name = AnalysisFiles.ErrorSummary;
        return AnalysisFiles.Read(directory, name, 5).Select(c => new ClassStatistics(
            c[0], AnalysisFiles.ParseInt(c[1], name), AnalysisFiles.ParseNum(c[2], name), AnalysisFiles.ParseNum(c[3], name), AnalysisFiles.ParseNum(c[4], name))).ToList();
    }

    /// <summary>
    ///     Ranks the weak lemmas from the files of the other two commands and writes <c>weak-lemmas.tsv</c>.
    /// </summary>
    /// <param name="directory">The analysis directory holding the files of <c>learning-curve</c> and <c>error-diagnosis</c>.</param>
    /// <returns>The ranked list.</returns>
    public static IReadOnlyList<WeakLemma> RunAndWrite(string directory)
    {
        var ranked = Rank(ReadCounts(directory), ReadPlateaus(directory));
        AnalysisFiles.Write(directory, AnalysisFiles.Weak, Render(ranked));
        return ranked;
    }

    /// <summary>
    ///     Renders the Markdown report from the files in the analysis directory.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <returns>The report; it contains no dates and no machine paths.</returns>
    public static string RenderReport(string directory)
    {
        var summary = ReadSummary(directory);
        var plateaus = ReadPlateaus(directory);
        var counts = ReadCounts(directory);
        var statistics = ReadStatistics(directory);
        var ranked = Rank(counts, plateaus);
        var text = new StringBuilder();
        text.AppendLine("# Learning Curve and Error Diagnosis").AppendLine();
        text.AppendLine("All figures on this page come from one run of the builder commands `learning-curve`, `error-diagnosis` and");
        text.AppendLine("`weak-lemmas` on the committed corpus; the page is generated by `weak-lemmas --report` and the underlying files");
        text.AppendLine("are in `corpus/audit/analysis/`:").AppendLine();
        text.AppendLine("```text");
        text.AppendLine("dotnet run --project tools/WordSense.Builder -c Release -- learning-curve --corpus corpus --model model --out corpus/audit/analysis");
        text.AppendLine("dotnet run --project tools/WordSense.Builder -c Release -- error-diagnosis --corpus corpus --model model --out corpus/audit/analysis");
        text.AppendLine("dotnet run --project tools/WordSense.Builder -c Release -- weak-lemmas --analysis corpus/audit/analysis --report docs/build_notes/learning-curve-and-diagnosis.md");
        text.AppendLine("```").AppendLine();
        AppendMethod(text);
        AppendCurves(text, summary, plateaus);
        AppendDiagnosis(text, counts, statistics);
        AppendWeak(text, ranked);
        AppendConclusions(text, summary, plateaus, counts, ranked);
        return Wrap(text.ToString());
    }

    private static void AppendMethod(StringBuilder text)
    {
        text.AppendLine("## Method").AppendLine();
        text.AppendLine("Three evaluation sets are used: `holdout` (every holdout row, project-written and real), `real` (the holdout rows");
        text.AppendLine("whose provenance origin is `real`; a subset of `holdout`) and `real-eval` (the separate real-text evaluation");
        text.AppendLine("sentences, never part of the holdout). Sentences are encoded once with the real model and centered with the mean of");
        text.AppendLine("all anchors, as in the table build.").AppendLine();
        text.AppendLine($"- **Learning curve**: for each lemma the classifier is trained on {string.Join(", ", LearningCurve.Counts)} randomly chosen anchors per sense");
        text.AppendLine("  (a sense with fewer anchors contributes all of them) and on all anchors, with the settings of the table build. Each");
        text.AppendLine("  count except `all` uses several random subsets whose seeds come from a stable FNV-1a hash of the lemma, the count and the");
        text.AppendLine("  subset index, so a run is repeatable. A row is answered when its score margin reaches the lemma's abstain margin chosen");
        text.AppendLine("  for the full anchor set. The plateau estimate takes the slope of accuracy per added anchor per sense between the last");
        text.AppendLine($"  point and the latest earlier point at least {F(LearningCurve.MinPlateauSpan, "0")} anchors per sense away, and extrapolates it linearly to twice the anchors,");
        text.AppendLine($"  limited to the remaining error; a curve is plateaued when the slope is below {F(LearningCurve.PlateauSlope, "0.000")} per anchor (an estimate, not a");
        text.AppendLine("  guarantee).");
        text.AppendLine("- **Error diagnosis**: for each holdout or real-evaluation row that is misclassified or abstained, `s_same` and `s_other` are the");
        text.AppendLine("  cosine similarities to the nearest anchor of the labeled sense and of any other sense of the lemma. The class is");
        text.AppendLine($"  `coverage-gap` when both are below {F(ErrorDiagnosis.FarSimilarity, "0.00")}; `overlap` when both reach {F(ErrorDiagnosis.FarSimilarity, "0.00")} and `s_other` is at least");
        text.AppendLine($"  `s_same` minus {F(ErrorDiagnosis.OverlapBand, "0.00")}; otherwise `ambiguous`, a label-noise suspect (the labeled sense's nearest anchor is clearly closer yet the");
        text.AppendLine("  classifier disagrees, or the sentence is far from its own label but near another).");
        text.AppendLine($"- **Weak lemmas**: a lemma is weak when its holdout accuracy is below {Percent0(WeakAccuracy)} or, with at least {MinRealRows} real-text rows, its");
        text.AppendLine($"  real-text accuracy is below {Percent0(WeakRealAccuracy)}. The score is the mean of the holdout and real-text error rates (the holdout error rate");
        text.AppendLine("  alone without real-text rows). The recommendation follows the dominant error class (ties: coverage gap, overlap,");
        text.AppendLine("  ambiguous): `more-sentences` for coverage gaps (expected gain: the smaller of the gap share of the rows and the");
        text.AppendLine("  learning-curve gain at twice the anchors), `stronger-encoder` for overlaps on a plateaued curve and `consumer-rule` for");
        text.AppendLine("  overlaps on a rising curve (expected gain: the overlap share of the rows, an upper bound), and `fix-labels` for ambiguous");
        text.AppendLine("  rows (expected gain: the ambiguous share of the rows, an upper bound).").AppendLine();
    }

    private static void AppendCurves(StringBuilder text, List<SummaryRow> summary, List<PlateauRow> plateaus)
    {
        text.AppendLine("## Learning Curves").AppendLine();
        text.AppendLine("Macro accuracy is the mean over lemmas of the best-sense accuracy; micro figures pool all rows. Anchors is the mean");
        text.AppendLine("anchors per sense that were used.").AppendLine();
        text.AppendLine("| Set | Anchors per sense | Lemmas | Rows | Macro best sense | Micro best sense | Answered | Answered accuracy |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var s in summary)
        {
            var answeredAccuracy = double.IsNaN(s.MicroAnsweredAccuracy) ? "n/a" : Percent(s.MicroAnsweredAccuracy);
            text.AppendLine($"| {s.Set} | {s.K} ({AnalysisFiles.Num(s.Anchors)}) | {s.Lemmas} | {s.N} | {Percent(s.MacroAccuracy)} | {Percent(s.MicroAccuracy)} | {Percent(s.MicroAnswered)} | {answeredAccuracy} |");
        }

        text.AppendLine().AppendLine("### Plateau Estimate").AppendLine();
        text.AppendLine("| Set | Lemma curves | Plateaued | Rising | Not estimable | Macro slope per anchor | Macro gain at twice the anchors |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var set in AnalysisFiles.Sets)
        {
            var lemmas = plateaus.Where(p => p.Set == set && p.Lemma != LearningCurve.Macro).ToList();
            var macro = plateaus.FirstOrDefault(p => p.Set == set && p.Lemma == LearningCurve.Macro);
            if (macro is null)
            {
                continue;
            }

            text.AppendLine($"| {set} | {lemmas.Count} | {lemmas.Count(p => p.Plateaued == "yes")} | {lemmas.Count(p => p.Plateaued == "no")} | {lemmas.Count(p => p.Plateaued == "n/a")} | {AnalysisFiles.Num(macro.Slope)} | {Percent(macro.Gain2x)} |");
        }

        text.AppendLine();
    }

    private static void AppendDiagnosis(StringBuilder text, List<LemmaErrorCounts> counts, List<ClassStatistics> statistics)
    {
        text.AppendLine("## Error Diagnosis").AppendLine();
        text.AppendLine("| Set | Rows | Correct | Abstained | Errors | Coverage gap | Overlap | Ambiguous |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var set in AnalysisFiles.Sets)
        {
            var rows = counts.Where(c => c.Set == set).ToList();
            if (rows.Count > 0)
            {
                text.AppendLine($"| {set} | {rows.Sum(c => c.N)} | {rows.Sum(c => c.Correct)} | {rows.Sum(c => c.Abstained)} | {rows.Sum(c => c.Errors)} | {rows.Sum(c => c.CoverageGap)} | {rows.Sum(c => c.Overlap)} | {rows.Sum(c => c.Ambiguous)} |");
            }
        }

        text.AppendLine().AppendLine("### Threshold Calibration").AppendLine();
        text.AppendLine("Similarity of the evaluation rows to the nearest anchor per class (`correct` is a row that is right and answered). The");
        text.AppendLine($"far threshold {F(ErrorDiagnosis.FarSimilarity, "0.00")} is about the 10th percentile of the `correct` row; the overlap band is {F(ErrorDiagnosis.OverlapBand, "0.00")}.").AppendLine();
        text.AppendLine("| Class | Rows | 10th percentile of s_same | Median s_same | Median s_other |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var s in statistics)
        {
            text.AppendLine($"| {s.Class} | {s.Count} | {AnalysisFiles.Num(s.P10Same)} | {AnalysisFiles.Num(s.MedianSame)} | {AnalysisFiles.Num(s.MedianOther)} |");
        }

        text.AppendLine();
    }

    private static void AppendWeak(StringBuilder text, IReadOnlyList<WeakLemma> ranked)
    {
        text.AppendLine("## Weak Lemmas").AppendLine();
        text.AppendLine($"{ranked.Count} lemmas are weak. The full ranking is `corpus/audit/analysis/weak-lemmas.tsv`; the 30 weakest are:").AppendLine();
        text.AppendLine("| Rank | Lemma | Holdout | Real rows | Real | Errors | Gap | Overlap | Ambiguous | Recommendation | Expected gain |");
        text.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |");
        foreach (var w in ranked.Take(30))
        {
            text.AppendLine($"| {w.Rank} | {w.Lemma} | {Percent(w.HoldoutAccuracy)} | {w.RealN} | {Percent(w.RealAccuracy)} | {w.Errors} | {w.CoverageGap} | {w.Overlap} | {w.Ambiguous} | {w.Recommendation} | {Percent(w.ExpectedGain)} |");
        }

        text.AppendLine().AppendLine("### Recommendations").AppendLine();
        foreach (var name in new[] { MoreSentences, ConsumerRule, StrongerEncoder, FixLabels })
        {
            var group = ranked.Where(w => w.Recommendation == name).ToList();
            text.AppendLine($"- `{name}` ({group.Count} lemmas): {(group.Count == 0 ? "none" : string.Join(", ", group.Select(w => w.Lemma)))}");
        }

        text.AppendLine();
    }

    private static void AppendConclusions(StringBuilder text, List<SummaryRow> summary, List<PlateauRow> plateaus, List<LemmaErrorCounts> counts, IReadOnlyList<WeakLemma> ranked)
    {
        text.AppendLine("## Conclusions").AppendLine();
        foreach (var set in new[] { AnalysisFiles.SetHoldout, AnalysisFiles.SetReal, AnalysisFiles.SetRealEval })
        {
            var curve = summary.Where(s => s.Set == set).OrderBy(s => s.Anchors).ToList();
            var macro = plateaus.FirstOrDefault(p => p.Set == set && p.Lemma == LearningCurve.Macro);
            if (curve.Count == 0 || macro is null)
            {
                continue;
            }

            var lemmas = plateaus.Where(p => p.Set == set && p.Lemma != LearningCurve.Macro).ToList();
            text.AppendLine($"- On `{set}` the macro accuracy rises from {Percent(curve[0].MacroAccuracy)} at {curve[0].K} anchors per sense to {Percent(curve[^1].MacroAccuracy)} with all anchors " +
                $"(mean {AnalysisFiles.Num(curve[^1].Anchors)} per sense); between {AnalysisFiles.Num(macro.AnchorsPrevious)} and {AnalysisFiles.Num(macro.AnchorsLast)} anchors per sense the slope is " +
                $"{AnalysisFiles.Num(macro.Slope)} per anchor, so doubling the anchors is expected to add about {Percent(macro.Gain2x)}. {lemmas.Count(p => p.Plateaued == "yes")} of {lemmas.Count} lemma curves are plateaued.");
        }

        var errors = counts.Where(c => c.Set != AnalysisFiles.SetReal).ToList();
        var gap = errors.Sum(c => c.CoverageGap);
        var overlap = errors.Sum(c => c.Overlap);
        var ambiguous = errors.Sum(c => c.Ambiguous);
        var total = Math.Max(1, gap + overlap + ambiguous);
        text.AppendLine($"- Of {gap + overlap + ambiguous} diagnosed errors (holdout and real-eval) {gap} are coverage gaps ({Percent((double)gap / total)}), " +
            $"{overlap} are overlaps ({Percent((double)overlap / total)}) and {ambiguous} are ambiguous ({Percent((double)ambiguous / total)}).");
        text.AppendLine($"- {ranked.Count} lemmas are weak: {ranked.Count(w => w.Recommendation == MoreSentences)} `more-sentences`, " +
            $"{ranked.Count(w => w.Recommendation == StrongerEncoder)} `stronger-encoder`, {ranked.Count(w => w.Recommendation == ConsumerRule)} `consumer-rule` and " +
            $"{ranked.Count(w => w.Recommendation == FixLabels)} `fix-labels`. The expected gains are estimates: gap gains come from the learning-curve extrapolation, " +
            "the others are upper bounds.");
    }

    /// <summary>
    ///     Normalizes line endings to LF and wraps prose lines longer than <see cref="MaxLine"/> characters at
    ///     spaces, so the generated page passes the Markdown line-length rule. Table rows and code are kept.
    /// </summary>
    internal static string Wrap(string markdown)
    {
        var output = new StringBuilder();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw;
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }

            var indent = line.StartsWith("- ", StringComparison.Ordinal) ? "  " : string.Empty;
            while (!inCode && !line.StartsWith('|') && line.Length > MaxLine)
            {
                var cut = line.LastIndexOf(' ', MaxLine);
                if (cut <= indent.Length)
                {
                    break;
                }

                output.Append(line, 0, cut).Append('\n');
                line = indent + line[(cut + 1)..];
            }

            output.Append(line).Append('\n');
        }

        return output.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>Longest prose line of the generated report, matching the Markdown line-length rule.</summary>
    internal const int MaxLine = 120;
    private static string F(double value, string format) => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private static string Percent0(double value) => F(100.0 * value, "0") + " %";

    private static string Percent(double value) => (100.0 * value).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " %";
}
