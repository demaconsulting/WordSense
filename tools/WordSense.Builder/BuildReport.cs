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
/// <param name="MeaningCorrect">Holdout sentences whose best sense has the meaning of the gold sense (the same sense for a lemma that is not a heteronym).</param>
/// <param name="Senses">The holdout counts per gold sense, or null when they were not collected.</param>
/// <param name="Pos">The part-of-speech counts of the lemma, or null when they were not collected.</param>
/// <param name="Pronunciation">The pronunciation (meaning) counts of the lemma, or null when not collected or not a heteronym.</param>
internal sealed record LemmaReport(
    string Lemma,
    int AnchorCount,
    int HoldoutCount,
    int Correct,
    int Answered,
    int AnsweredCorrect,
    float Margin,
    bool DefaultMargin,
    int MinAnchorsPerSense,
    int MeaningCorrect,
    IReadOnlyList<SenseStats>? Senses = null,
    LevelStats? Pos = null,
    LevelStats? Pronunciation = null)
{
    /// <summary>
    ///     Gets the accuracy of the best sense over every holdout sentence.
    /// </summary>
    public double Accuracy => HoldoutCount == 0 ? double.NaN : (double)Correct / HoldoutCount;
}

/// <summary>
///     Holdout counts of the sentences whose gold sense is one sense of a lemma.
/// </summary>
/// <param name="Sense">The gold sense.</param>
/// <param name="Total">Sentences.</param>
/// <param name="Correct">Sentences whose best sense was the gold sense.</param>
/// <param name="Answered">Answered sentences.</param>
/// <param name="AnsweredCorrect">Answered sentences that were right.</param>
internal sealed record SenseStats(string Sense, int Total, int Correct, int Answered, int AnsweredCorrect);

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
/// <param name="Accuracy">The part-of-speech and pronunciation evaluation.</param>
internal sealed record BuildReport(
    IReadOnlyList<LemmaReport> Lemmas,
    IReadOnlyDictionary<string, GroupStats> BySource,
    IReadOnlyList<string> Warnings,
    double TargetAccuracy,
    AccuracyReport Accuracy)
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
        text.AppendLine(inv, $"Meaning level: {Pct(Lemmas.Sum(l => l.MeaningCorrect), overall.Total)} best-meaning accuracy over {overall.Total} sentences (a prediction is right when the predicted sense has the meaning of the gold sense; for a lemma that is not a heteronym the sense itself)");
        text.AppendLine(inv, $"With abstention: {Pct(overall.AnsweredCorrect, overall.Answered)} accuracy at {Pct(overall.Answered, overall.Total)} coverage");
        text.AppendLine();
        text.AppendLine("Per holdout file:");
        foreach (var (source, stats) in BySource.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.AppendLine(inv, $"  {source}: {Pct(stats.Correct, stats.Total)} of {stats.Total}; abstain {Pct(stats.AnsweredCorrect, stats.Answered)} at {Pct(stats.Answered, stats.Total)} coverage");
        }

        text.AppendLine();
        RenderAccuracy(text, inv);
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
    ///     Renders the part-of-speech and pronunciation sections.
    /// </summary>
    /// <param name="text">The report text.</param>
    /// <param name="inv">The invariant culture.</param>
    private void RenderAccuracy(StringBuilder text, CultureInfo inv)
    {
        text.AppendLine("POS accuracy (part of speech of the best sense against the gold sense, every holdout sentence):");
        text.AppendLine(inv, $"  Overall: {Line(Accuracy.Pos)}");
        foreach (var (pos, stats) in Accuracy.PosByGold.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.AppendLine(inv, $"  Gold {pos}: {Line(stats)}");
        }

        foreach (var (source, stats) in Accuracy.PosBySource.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.AppendLine(inv, $"  {source}: {Line(stats)}");
        }

        text.AppendLine();
        text.AppendLine("Pronunciation (meaning) accuracy (meaning of the best sense against the gold sense, sentences of heteronym lemmas only):");
        text.AppendLine(inv, $"  Overall: {Line(Accuracy.Pronunciation)}");
        foreach (var (source, stats) in Accuracy.PronunciationBySource.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.AppendLine(inv, $"  {source}: {Line(stats)}");
        }
    }

    /// <summary>
    ///     Formats the counts of one evaluation level.
    /// </summary>
    /// <param name="s">The counts.</param>
    /// <returns>The text.</returns>
    private static string Line(LevelStats s)
    {
        return $"{Pct(s.Correct, s.Evaluated)} best accuracy over {s.Evaluated}; abstain {Pct(s.AnsweredCorrect, s.Answered)} at {Pct(s.Answered, s.Evaluated)} coverage";
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
///     The built tables and the report.
/// </summary>
/// <param name="Table">The lexicon table.</param>
/// <param name="Weights">The weights table trained for the model and the lexicon table.</param>
/// <param name="Report">The report.</param>
/// <param name="DictionaryWords">The number of dictionary words in the table.</param>
/// <param name="ShadowedWords">The number of dictionary words dropped because a trained lemma or a form spells them.</param>
internal sealed record BuildResult(
    WordSenseTable Table,
    WordSenseWeightsTable Weights,
    BuildReport Report,
    int DictionaryWords = 0,
    int ShadowedWords = 0);
