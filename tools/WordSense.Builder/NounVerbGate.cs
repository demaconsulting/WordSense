using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The quality gate of the noun/verb lemmas: the thresholds a lemma must reach on its holdout rows.
/// </summary>
/// <param name="MinBestSense">The lowest best-sense accuracy over the holdout rows of the lemma.</param>
/// <param name="MinSenseAnswered">The lowest answered accuracy (after abstaining) of every sense of the lemma.</param>
/// <param name="MinCoverage">The lowest share of holdout rows that the lemma answers.</param>
internal sealed record GatePolicy(double MinBestSense = 0.90, double MinSenseAnswered = 0.80, double MinCoverage = 0.70);

/// <summary>
///     The holdout results of one noun/verb lemma and the outcome of the gate.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Anchors">The anchor rows.</param>
/// <param name="Holdout">The holdout rows.</param>
/// <param name="BestSense">The best-sense accuracy over the holdout rows.</param>
/// <param name="Coverage">The share of holdout rows answered.</param>
/// <param name="AnsweredAccuracy">The accuracy of the answered rows.</param>
/// <param name="TagAccuracy">The best-tag accuracy of the noun-versus-verb question.</param>
/// <param name="Senses">The counts per gold sense.</param>
/// <param name="Failure">Why the lemma failed the gate, or an empty text when it passed.</param>
internal sealed record GateResult(
    string Lemma,
    int Anchors,
    int Holdout,
    double BestSense,
    double Coverage,
    double AnsweredAccuracy,
    double TagAccuracy,
    IReadOnlyList<SenseStats> Senses,
    string Failure)
{
    /// <summary>
    ///     Gets a value indicating whether the lemma reached every threshold.
    /// </summary>
    public bool Passed => Failure.Length == 0;

    /// <summary>
    ///     Gets the answered accuracy of a sense, or NaN when no row of it was answered.
    /// </summary>
    /// <param name="sense">The sense.</param>
    /// <returns>The answered accuracy.</returns>
    public double SenseAnswered(string sense)
    {
        var s = Senses.FirstOrDefault(x => x.Sense == sense);
        return s is null || s.Answered == 0 ? double.NaN : (double)s.AnsweredCorrect / s.Answered;
    }

    /// <summary>
    ///     Gets the best-sense accuracy of a sense, or NaN when it has no holdout row.
    /// </summary>
    /// <param name="sense">The sense.</param>
    /// <returns>The accuracy.</returns>
    public double SenseBest(string sense)
    {
        var s = Senses.FirstOrDefault(x => x.Sense == sense);
        return s is null || s.Total == 0 ? double.NaN : (double)s.Correct / s.Total;
    }
}

/// <summary>
///     Applies the quality gate to the noun/verb lemmas and moves the lemmas that fail it out of the corpus.
/// </summary>
/// <remarks>
///     A lemma passes when its holdout best-sense accuracy, the answered accuracy of each of its senses and its coverage
///     after abstaining reach the thresholds of the <see cref="GatePolicy" />. A failing lemma is removed from the
///     anchors, holdout, provenance and senses of the noun/verb batches and from <c>forms/speech.tsv</c>; its numbers
///     and the reason are recorded in <c>audit/nounverb-dropped.tsv</c>, which the import reads so that a re-import does not
///     bring the lemma back.
/// </remarks>
internal static class NounVerbGate
{
    /// <summary>
    ///     The file name of the report of dropped lemmas, in the <c>audit</c> folder.
    /// </summary>
    public const string DroppedFile = "nounverb-dropped.tsv";

    private const string Header = "lemma\treason\tanchors\tholdout\tbest_sense\tnoun_answered\tverb_answered\tcoverage";

    /// <summary>
    ///     Evaluates the gate for the given lemmas.
    /// </summary>
    /// <param name="lemmas">The evaluation of every lemma of the table.</param>
    /// <param name="scope">The lemmas the gate applies to.</param>
    /// <param name="policy">The thresholds.</param>
    /// <returns>One result per lemma in scope, in lemma order.</returns>
    public static List<GateResult> Evaluate(IEnumerable<LemmaReport> lemmas, ISet<string> scope, GatePolicy policy)
    {
        var results = new List<GateResult>();
        foreach (var l in lemmas.Where(l => scope.Contains(l.Lemma)).OrderBy(l => l.Lemma, StringComparer.Ordinal))
        {
            var senses = l.Senses ?? [];
            var failures = new List<string>();
            var best = l.HoldoutCount == 0 ? 0 : (double)l.Correct / l.HoldoutCount;
            var coverage = l.HoldoutCount == 0 ? 0 : (double)l.Answered / l.HoldoutCount;
            if (best < policy.MinBestSense)
            {
                failures.Add($"best-sense accuracy {Pct(best)} below {Pct(policy.MinBestSense)}");
            }

            foreach (var s in senses)
            {
                var answered = s.Answered == 0 ? 0 : (double)s.AnsweredCorrect / s.Answered;
                if (answered < policy.MinSenseAnswered)
                {
                    failures.Add($"{s.Sense} answered accuracy {Pct(answered)} below {Pct(policy.MinSenseAnswered)}");
                }
            }

            if (coverage < policy.MinCoverage)
            {
                failures.Add($"coverage {Pct(coverage)} below {Pct(policy.MinCoverage)}");
            }

            var tag = l.Tag is { Evaluated: > 0 } t ? (double)t.Correct / t.Evaluated : 0;
            results.Add(new GateResult(
                l.Lemma,
                l.AnchorCount,
                l.HoldoutCount,
                best,
                coverage,
                l.Answered == 0 ? 0 : (double)l.AnsweredCorrect / l.Answered,
                tag,
                senses,
                string.Join("; ", failures)));
        }

        return results;
    }

    /// <summary>
    ///     Reads the lemmas of the noun/verb batches.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The lemmas of every <c>senses/nounverb-*.tsv</c> file.</returns>
    public static HashSet<string> ReadScope(string corpus)
    {
        var lemmas = new HashSet<string>(StringComparer.Ordinal);
        var directory = Path.Combine(corpus, "senses");
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").Where(f => NounVerbImport.IsNounVerbFile(Path.GetFileName(f))))
            {
                lemmas.UnionWith(File.ReadAllLines(file, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')[0]));
            }
        }

        return lemmas;
    }

    /// <summary>
    ///     Reads the lemmas that an earlier gate run dropped.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The dropped lemmas; empty when there is no report.</returns>
    public static HashSet<string> ReadDropped(string corpus)
    {
        var path = Path.Combine(corpus, "audit", DroppedFile);
        return File.Exists(path)
            ? File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')[0]).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Removes the failing lemmas from the corpus and records them in the dropped report.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="failed">The results of the lemmas that failed the gate.</param>
    public static void Drop(string corpus, IReadOnlyList<GateResult> failed)
    {
        if (failed.Count == 0)
        {
            return;
        }

        var drop = failed.Select(f => f.Lemma).ToHashSet(StringComparer.Ordinal);
        foreach (var folder in new[] { "anchors", "holdout", "provenance", "senses" })
        {
            var directory = Path.Combine(corpus, folder);
            foreach (var file in Directory.Exists(directory) ? Directory.GetFiles(directory, "*.tsv").Where(f => NounVerbImport.IsNounVerbFile(Path.GetFileName(f))) : [])
            {
                FilterLines(file, drop, folder == "provenance" ? 1 : 0, folder is not ("anchors" or "holdout"));
            }
        }

        FilterLines(Path.Combine(corpus, "forms", "speech.tsv"), drop, 1, true);

        var path = Path.Combine(corpus, "audit", DroppedFile);
        var lines = File.Exists(path) ? File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0).ToList() : [];
        lines.AddRange(failed.Select(f => string.Join(
            '\t',
            f.Lemma,
            f.Failure,
            f.Anchors.ToString(CultureInfo.InvariantCulture),
            f.Holdout.ToString(CultureInfo.InvariantCulture),
            Pct(f.BestSense),
            Pct(f.SenseAnswered("noun")),
            Pct(f.SenseAnswered("verb")),
            Pct(f.Coverage))));
        var sorted = lines.OrderBy(l => l.Split('\t')[0], StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Header + "\n" + string.Join('\n', sorted) + "\n", new UTF8Encoding(false));
    }

    /// <summary>
    ///     Renders the results as a tab-separated table.
    /// </summary>
    /// <param name="results">The results.</param>
    /// <returns>The table text with a header.</returns>
    public static string RenderTsv(IReadOnlyList<GateResult> results)
    {
        var text = new StringBuilder("lemma\tanchors\tholdout\tbest_sense\tnoun_best\tverb_best\tnoun_answered\tverb_answered\tcoverage\tanswered_accuracy\ttag_accuracy\tstatus\n");
        foreach (var r in results)
        {
            text.Append(string.Join(
                '\t',
                r.Lemma,
                r.Anchors.ToString(CultureInfo.InvariantCulture),
                r.Holdout.ToString(CultureInfo.InvariantCulture),
                Pct(r.BestSense),
                Pct(r.SenseBest("noun")),
                Pct(r.SenseBest("verb")),
                Pct(r.SenseAnswered("noun")),
                Pct(r.SenseAnswered("verb")),
                Pct(r.Coverage),
                Pct(r.AnsweredAccuracy),
                Pct(r.TagAccuracy),
                r.Passed ? "pass" : "fail: " + r.Failure)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the results as a Markdown table.
    /// </summary>
    /// <param name="results">The results.</param>
    /// <returns>The table text.</returns>
    public static string RenderMarkdown(IReadOnlyList<GateResult> results)
    {
        var text = new StringBuilder();
        text.Append("| Lemma | Anchors | Holdout | Best | Noun | Verb | Noun ans. | Verb ans. | Cov. | Ans. | Tag | Gate |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");
        foreach (var r in results)
        {
            text.Append($"| {r.Lemma} | {r.Anchors} | {r.Holdout} | {Pct(r.BestSense)} | {Pct(r.SenseBest("noun"))} | {Pct(r.SenseBest("verb"))} | {Pct(r.SenseAnswered("noun"))} | {Pct(r.SenseAnswered("verb"))} | {Pct(r.Coverage)} | {Pct(r.AnsweredAccuracy)} | {Pct(r.TagAccuracy)} | {(r.Passed ? "pass" : "fail")} |\n");
        }

        return text.ToString();
    }

    // Only files that have a header line keep their first line; anchors and holdout have none, so their first
    // line is data and must be filtered like every other
    private static void FilterLines(string path, HashSet<string> drop, int column, bool hasHeader)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lines = File.ReadAllLines(path, new UTF8Encoding(false)).Where(l => l.Length > 0).ToList();
        var kept = lines.Where((l, i) => i == 0 && hasHeader && IsHeader(l) || !drop.Contains(l.Split('\t')[column])).ToList();
        File.WriteAllText(path, string.Join('\n', kept) + (kept.Count > 0 ? "\n" : string.Empty), new UTF8Encoding(false));
    }

    private static bool IsHeader(string line) => line.StartsWith("lemma\t", StringComparison.Ordinal) || line.StartsWith("form\t", StringComparison.Ordinal) || line.StartsWith("id\t", StringComparison.Ordinal);

    private static string Pct(double value) => double.IsNaN(value) ? "n/a" : (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
