using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The diagnosis of one misclassified or abstained evaluation sentence.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Set">The set the row was read for: <c>holdout</c> or <c>real-eval</c>.</param>
/// <param name="Origin">The provenance origin of the row.</param>
/// <param name="Gold">The labeled sense.</param>
/// <param name="Predicted">The sense the classifier chose.</param>
/// <param name="Answered">Whether the classifier answered (did not abstain).</param>
/// <param name="SimilaritySame">The cosine similarity to the nearest anchor of the labeled sense.</param>
/// <param name="SimilarityOther">The cosine similarity to the nearest anchor of any other sense of the lemma.</param>
/// <param name="Class">The error class.</param>
/// <param name="Sentence">The sentence.</param>
internal sealed record DiagnosedRow(
    string Lemma,
    string Set,
    string Origin,
    string Gold,
    string Predicted,
    bool Answered,
    double SimilaritySame,
    double SimilarityOther,
    string Class,
    string Sentence);

/// <summary>
///     The counts of one lemma on one set.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Set">The set.</param>
/// <param name="N">The number of evaluation rows.</param>
/// <param name="Correct">The rows whose best sense is the labeled sense.</param>
/// <param name="Abstained">The rows the classifier did not answer.</param>
/// <param name="Errors">The rows that are misclassified or abstained.</param>
/// <param name="CoverageGap">The errors of class <c>coverage-gap</c>.</param>
/// <param name="Overlap">The errors of class <c>overlap</c>.</param>
/// <param name="Ambiguous">The errors of class <c>ambiguous</c>.</param>
internal sealed record LemmaErrorCounts(string Lemma, string Set, int N, int Correct, int Abstained, int Errors, int CoverageGap, int Overlap, int Ambiguous);

/// <summary>
///     The similarity statistics of one class, used to calibrate the thresholds.
/// </summary>
/// <param name="Class">The class, or <c>correct</c> for rows that are right and answered.</param>
/// <param name="Count">The number of rows.</param>
/// <param name="P10Same">The 10th percentile of the similarity to the labeled sense.</param>
/// <param name="MedianSame">The median similarity to the labeled sense.</param>
/// <param name="MedianOther">The median similarity to the other senses.</param>
internal sealed record ClassStatistics(string Class, int Count, double P10Same, double MedianSame, double MedianOther);

/// <summary>
///     The result of the <c>error-diagnosis</c> command.
/// </summary>
/// <param name="Rows">The diagnosed rows.</param>
/// <param name="Lemmas">The counts per lemma and set.</param>
/// <param name="Statistics">The statistics per class.</param>
internal sealed record DiagnosisResult(IReadOnlyList<DiagnosedRow> Rows, IReadOnlyList<LemmaErrorCounts> Lemmas, IReadOnlyList<ClassStatistics> Statistics);

/// <summary>
///     The <c>error-diagnosis</c> command: why does the classifier miss or abstain on an evaluation sentence?
/// </summary>
/// <remarks>
///     The classifier trained on all anchors (as in the table build) is evaluated on every holdout and real-evaluation
///     sentence. For each sentence that is misclassified or abstained, the cosine similarity of its centered feature
///     (features are normalized, so this is the dot product) to the nearest anchor of the labeled sense
///     (<c>s_same</c>) and of the other senses (<c>s_other</c>) decides the class.
/// </remarks>
internal static class ErrorDiagnosis
{
    /// <summary>
    ///     The similarity below which an anchor counts as far away. It was calibrated on the committed corpus: it is
    ///     about the 10th percentile of the similarity between a correctly answered sentence and the nearest anchor of
    ///     its own sense, so a sentence that is farther than that from every anchor is less covered than 9 in 10 of
    ///     the sentences the classifier handles correctly.
    /// </summary>
    public const double FarSimilarity = 0.77;

    /// <summary>
    ///     The similarity margin within which an anchor of another sense counts as being as close as the labeled
    ///     sense's nearest anchor.
    /// </summary>
    public const double OverlapBand = 0.05;

    /// <summary>
    ///     The class of a sentence far from every anchor.
    /// </summary>
    public const string CoverageGap = "coverage-gap";

    /// <summary>
    ///     The class of a sentence whose nearest anchors are close but of another sense.
    /// </summary>
    public const string Overlap = "overlap";

    /// <summary>
    ///     The class of a sentence that neither gap nor overlap explains, a label-noise suspect.
    /// </summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>
    ///     The class label of correctly answered rows in the statistics.
    /// </summary>
    public const string Correct = "correct";

    /// <summary>
    ///     Classifies an error from the similarity to the nearest anchor of the labeled sense and of the other senses.
    /// </summary>
    /// <param name="similaritySame">The similarity to the nearest anchor of the labeled sense.</param>
    /// <param name="similarityOther">The similarity to the nearest anchor of another sense.</param>
    /// <returns>
    ///     <see cref="CoverageGap"/> when both are below <see cref="FarSimilarity"/>; <see cref="Overlap"/> when both
    ///     reach it and the other sense is within <see cref="OverlapBand"/> of the labeled one or closer; otherwise
    ///     <see cref="Ambiguous"/>.
    /// </returns>
    public static string Classify(double similaritySame, double similarityOther)
    {
        if (similaritySame < FarSimilarity && similarityOther < FarSimilarity)
        {
            return CoverageGap;
        }

        if (similaritySame >= FarSimilarity && similarityOther >= FarSimilarity && similarityOther >= similaritySame - OverlapBand)
        {
            return Overlap;
        }

        return Ambiguous;
    }

    /// <summary>
    ///     Finds the highest dot product between a feature and a set of anchors.
    /// </summary>
    /// <param name="feature">The normalized feature.</param>
    /// <param name="anchors">The normalized anchor features.</param>
    /// <param name="indexes">The anchors to compare with.</param>
    /// <returns>The highest similarity, or -1 when there are no anchors.</returns>
    public static double Nearest(float[] feature, IReadOnlyList<float[]> anchors, IEnumerable<int> indexes)
    {
        var best = -1.0;
        foreach (var index in indexes)
        {
            double dot = 0;
            var anchor = anchors[index];
            for (var d = 0; d < feature.Length; d++)
            {
                dot += (double)feature[d] * anchor[d];
            }

            best = Math.Max(best, dot);
        }

        return best;
    }

    /// <summary>
    ///     Diagnoses every misclassified or abstained evaluation sentence.
    /// </summary>
    /// <param name="data">The featurized corpus.</param>
    /// <returns>The diagnosed rows, the counts per lemma and set and the statistics per class.</returns>
    public static DiagnosisResult Run(AnalysisData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var perLemma = new (List<DiagnosedRow> Rows, List<LemmaErrorCounts> Counts, List<(string Class, double Same, double Other)> Similarities)[data.Lemmas.Count];
        Parallel.For(0, data.Lemmas.Count, i => perLemma[i] = DiagnoseLemma(data.Lemmas[i], data.C));
        var similarities = perLemma.SelectMany(p => p.Similarities).ToList();
        var statistics = new List<ClassStatistics>();
        foreach (var name in new[] { Correct, CoverageGap, Overlap, Ambiguous })
        {
            var group = similarities.Where(s => s.Class == name).ToList();
            if (group.Count > 0)
            {
                statistics.Add(new ClassStatistics(
                    name, group.Count, Percentile(group.Select(g => g.Same), 0.10), Percentile(group.Select(g => g.Same), 0.5), Percentile(group.Select(g => g.Other), 0.5)));
            }
        }

        return new DiagnosisResult(perLemma.SelectMany(p => p.Rows).ToList(), perLemma.SelectMany(p => p.Counts).ToList(), statistics);
    }

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Floor(fraction * (sorted.Count - 1))];
    }

    private static (List<DiagnosedRow> Rows, List<LemmaErrorCounts> Counts, List<(string Class, double Same, double Other)> Similarities) DiagnoseLemma(LemmaAnalysis lemma, double c)
    {
        var model = lemma.Train(Enumerable.Range(0, lemma.AnchorSense.Length).ToArray(), c);
        var rows = new List<DiagnosedRow>();
        var similarities = new List<(string Class, double Same, double Other)>();
        var tallies = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var row in lemma.Eval.OrderBy(r => Array.IndexOf(AnalysisFiles.Sets, r.Set)).ThenBy(r => r.Row.Sentence, StringComparer.Ordinal))
        {
            var (best, margin) = model.Score(row.Feature);
            var right = best == row.SenseIndex;
            var answered = margin >= lemma.Margin;
            var same = Nearest(row.Feature, lemma.AnchorFeatures, Enumerable.Range(0, lemma.AnchorSense.Length).Where(a => lemma.AnchorSense[a] == row.SenseIndex));
            var other = Nearest(row.Feature, lemma.AnchorFeatures, Enumerable.Range(0, lemma.AnchorSense.Length).Where(a => lemma.AnchorSense[a] != row.SenseIndex));
            var error = !right || !answered;
            var errorClass = error ? Classify(same, other) : Correct;
            similarities.Add((errorClass, same, other));
            if (error)
            {
                rows.Add(new DiagnosedRow(lemma.Lemma, row.Set, row.Origin, row.Row.Sense, lemma.Senses[best].Name, answered, same, other, errorClass, row.Row.Sentence));
            }

            foreach (var set in row.Memberships)
            {
                if (!tallies.TryGetValue(set, out var tally))
                {
                    tally = new int[7];
                    tallies[set] = tally;
                }

                tally[0]++;
                tally[1] += right ? 1 : 0;
                tally[2] += answered ? 0 : 1;
                tally[3] += error ? 1 : 0;
                tally[4] += errorClass == CoverageGap ? 1 : 0;
                tally[5] += errorClass == Overlap ? 1 : 0;
                tally[6] += errorClass == Ambiguous ? 1 : 0;
            }
        }

        var counts = AnalysisFiles.Sets.Where(tallies.ContainsKey)
            .Select(set => new LemmaErrorCounts(lemma.Lemma, set, tallies[set][0], tallies[set][1], tallies[set][2], tallies[set][3], tallies[set][4], tallies[set][5], tallies[set][6])).ToList();
        return (rows, counts, similarities);
    }

    /// <summary>
    ///     Renders the diagnosed rows as TSV.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The file content.</returns>
    public static string RenderRows(IReadOnlyList<DiagnosedRow> rows)
    {
        var text = new StringBuilder("lemma\tset\torigin\tgold\tpredicted\tanswered\ts_same\ts_other\tclass\tsentence\n");
        foreach (var r in rows)
        {
            text.Append($"{r.Lemma}\t{r.Set}\t{r.Origin}\t{r.Gold}\t{r.Predicted}\t{(r.Answered ? "yes" : "no")}\t{AnalysisFiles.Num(r.SimilaritySame)}\t{AnalysisFiles.Num(r.SimilarityOther)}\t{r.Class}\t{r.Sentence}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the per-lemma counts as TSV.
    /// </summary>
    /// <param name="counts">The counts.</param>
    /// <returns>The file content.</returns>
    public static string RenderLemmas(IReadOnlyList<LemmaErrorCounts> counts)
    {
        var text = new StringBuilder("lemma\tset\tn_eval\tcorrect\tabstained\terrors\tcoverage_gap\toverlap\tambiguous\n");
        foreach (var c in counts)
        {
            text.Append($"{c.Lemma}\t{c.Set}\t{c.N}\t{c.Correct}\t{c.Abstained}\t{c.Errors}\t{c.CoverageGap}\t{c.Overlap}\t{c.Ambiguous}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the class statistics as TSV.
    /// </summary>
    /// <param name="statistics">The statistics.</param>
    /// <returns>The file content.</returns>
    public static string RenderStatistics(IReadOnlyList<ClassStatistics> statistics)
    {
        var text = new StringBuilder("class\tn\tp10_s_same\tmedian_s_same\tmedian_s_other\n");
        foreach (var s in statistics)
        {
            text.Append($"{s.Class}\t{s.Count}\t{AnalysisFiles.Num(s.P10Same)}\t{AnalysisFiles.Num(s.MedianSame)}\t{AnalysisFiles.Num(s.MedianOther)}\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Runs the command and writes the three files into the output directory.
    /// </summary>
    /// <param name="data">The featurized corpus.</param>
    /// <param name="output">The output directory.</param>
    /// <returns>The diagnosis result.</returns>
    public static DiagnosisResult RunAndWrite(AnalysisData data, string output)
    {
        var result = Run(data);
        AnalysisFiles.Write(output, AnalysisFiles.Errors, RenderRows(result.Rows));
        AnalysisFiles.Write(output, AnalysisFiles.ErrorLemmas, RenderLemmas(result.Lemmas));
        AnalysisFiles.Write(output, AnalysisFiles.ErrorSummary, RenderStatistics(result.Statistics));
        return result;
    }
}
