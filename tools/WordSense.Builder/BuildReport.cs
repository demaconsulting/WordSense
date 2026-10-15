using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Evaluation of one lemma on the holdout set.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="AnchorCount">Number of training sentences.</param>
/// <param name="HoldoutCount">Number of evaluation sentences.</param>
/// <param name="Correct">Holdout sentences whose best sense was right, ignoring abstention.</param>
/// <param name="Answered">Holdout sentences answered (margin at or above the threshold).</param>
/// <param name="AnsweredCorrect">Answered holdout sentences that were right.</param>
/// <param name="Margin">The abstain margin of the lemma.</param>
/// <param name="DefaultMargin">Whether the margin is the global default rather than cross-validated.</param>
/// <param name="MinAnchorsPerSense">The smallest number of training sentences of any sense.</param>
internal sealed record LemmaReport(
    string Lemma,
    int AnchorCount,
    int HoldoutCount,
    int Correct,
    int Answered,
    int AnsweredCorrect,
    float Margin,
    bool DefaultMargin,
    int MinAnchorsPerSense)
{
    /// <summary>
    ///     Gets the accuracy of the best sense over every holdout sentence.
    /// </summary>
    public double Accuracy => HoldoutCount == 0 ? double.NaN : (double)Correct / HoldoutCount;
}

/// <summary>
///     Counts for one group of holdout sentences.
/// </summary>
/// <param name="Total">Sentences.</param>
/// <param name="Correct">Correct best senses.</param>
/// <param name="Answered">Answered sentences.</param>
/// <param name="AnsweredCorrect">Correct answered sentences.</param>
internal sealed record GroupStats(int Total, int Correct, int Answered, int AnsweredCorrect);

/// <summary>
///     The outcome of building a table: statistics and warnings.
/// </summary>
/// <param name="Lemmas">Per-lemma evaluation, in lemma order.</param>
/// <param name="BySource">Overall counts per holdout file.</param>
/// <param name="Warnings">Quality warnings.</param>
/// <param name="TargetAccuracy">The accuracy a lemma is expected to reach.</param>
internal sealed record BuildReport(
    IReadOnlyList<LemmaReport> Lemmas,
    IReadOnlyDictionary<string, GroupStats> BySource,
    IReadOnlyList<string> Warnings,
    double TargetAccuracy)
{
    /// <summary>
    ///     Gets the overall counts over every holdout sentence.
    /// </summary>
    public GroupStats Overall => new(
        Lemmas.Sum(l => l.HoldoutCount),
        Lemmas.Sum(l => l.Correct),
        Lemmas.Sum(l => l.Answered),
        Lemmas.Sum(l => l.AnsweredCorrect));

    /// <summary>
    ///     Renders the report as text.
    /// </summary>
    /// <returns>The report text.</returns>
    public string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        var overall = Overall;
        text.AppendLine(inv, $"Overall holdout: {Pct(overall.Correct, overall.Total)} best-sense accuracy over {overall.Total} sentences");
        text.AppendLine(inv, $"With abstention: {Pct(overall.AnsweredCorrect, overall.Answered)} accuracy at {Pct(overall.Answered, overall.Total)} coverage");
        text.AppendLine();
        text.AppendLine("Per holdout file:");
        foreach (var (source, stats) in BySource.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.AppendLine(inv, $"  {source}: {Pct(stats.Correct, stats.Total)} of {stats.Total}; abstain {Pct(stats.AnsweredCorrect, stats.Answered)} at {Pct(stats.Answered, stats.Total)} coverage");
        }

        text.AppendLine();
        text.AppendLine("Per lemma (anchors, holdout, accuracy, abstain accuracy, coverage, margin):");
        foreach (var l in Lemmas)
        {
            text.AppendLine(inv, $"  {l.Lemma}: {l.AnchorCount}, {l.HoldoutCount}, {Pct(l.Correct, l.HoldoutCount)}, {Pct(l.AnsweredCorrect, l.Answered)}, {Pct(l.Answered, l.HoldoutCount)}, {l.Margin:0.###}{(l.DefaultMargin ? " (default)" : string.Empty)}");
        }

        text.AppendLine();
        text.AppendLine(inv, $"Lemmas below {TargetAccuracy:P0}: {string.Join(", ", Lemmas.Where(l => l.Accuracy < TargetAccuracy).Select(l => $"{l.Lemma} ({l.Accuracy:P1})"))}");
        text.AppendLine(inv, $"Warnings ({Warnings.Count}):");
        foreach (var warning in Warnings)
        {
            text.AppendLine("  " + warning);
        }

        return text.ToString();
    }

    /// <summary>
    ///     Formats a percentage, tolerating an empty denominator.
    /// </summary>
    /// <param name="part">The numerator.</param>
    /// <param name="whole">The denominator.</param>
    /// <returns>The percentage text.</returns>
    private static string Pct(int part, int whole)
    {
        return whole == 0 ? "n/a" : ((double)part / whole).ToString("P2", CultureInfo.InvariantCulture);
    }
}

/// <summary>
///     The built table and its report.
/// </summary>
/// <param name="Table">The table.</param>
/// <param name="Report">The report.</param>
internal sealed record BuildResult(WordSenseTable Table, BuildReport Report);
