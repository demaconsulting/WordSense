using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     What a model answered on one row of the suite.
/// </summary>
/// <param name="Grade">The grade of the answer.</param>
/// <param name="Level">The level of the answer (<c>Sense</c>, <c>Group</c>, <c>PartOfSpeech</c> or <c>None</c>).</param>
/// <param name="Analysis">The selected analysis as <c>lemma/Part[/Features]</c>, or empty when the model gave none.</param>
/// <param name="Target">The sense id or group id of the answer, or empty.</param>
/// <param name="Confidence">The confidence of the answer.</param>
internal sealed record SuiteAnswer(SuiteGrade Grade, string Level, string Analysis, string Target, double Confidence)
{
    /// <summary>
    ///     Gets the answer as the diff shows it: level, analysis and sense or group.
    /// </summary>
    public string Text => Level + " " + (Analysis.Length == 0 ? "-" : Analysis) + (Target.Length == 0 ? string.Empty : " " + Target);
}

/// <summary>
///     One row of the suite whose answer changed between two runs.
/// </summary>
/// <param name="Row">The row.</param>
/// <param name="Model">The model.</param>
/// <param name="Old">The old answer.</param>
/// <param name="New">The new answer.</param>
/// <param name="Expected">The grade the expectation file asks of the model for the row.</param>
internal sealed record SuiteChange(SuiteRow Row, string Model, SuiteAnswer Old, SuiteAnswer New, SuiteGrade Expected)
{
    /// <summary>
    ///     Gets the verdict on the change.
    /// </summary>
    public string Verdict
    {
        get
        {
            if (New.Grade > Old.Grade)
            {
                return "better";
            }

            return New.Grade < Old.Grade ? "WORSE" : "same grade, other answer";
        }
    }
}

/// <summary>
///     Records the answers of the models on the suite and lists, row by row, the answers that changed between two runs.
/// </summary>
/// <remarks>
///     <para>
///     <c>evaluate-suite --outcomes FILE</c> writes one line per model and row: the grade, the level, the selected
///     analysis, the sense or group and the confidence. <c>evaluate-suite --diff FILE</c> reads the outcomes of an earlier
///     run (the baseline) and lists every row on which the answer or the grade changed, in either direction, with the
///     sentence, the old and the new answer, the gold of the row (<c>suite.tsv</c>), the expectation of the model and a
///     verdict, so that a reviewer can read every change.
///     </para>
///     <para>
///     The file has no dependency on the model projects. The grading rule is <see cref="SuiteFile.Grade"/>.
///     </para>
/// </remarks>
internal static class SuiteDiff
{
    private const string Header = "model\tid\tgrade\tlevel\tanalysis\ttarget\tconfidence";

    /// <summary>
    ///     Classifies the sentence of every row with a model and records the answer for its target word.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answers parallel to the rows.</returns>
    public static async Task<IReadOnlyList<SuiteAnswer>> AnswerAsync(IWordSenseModel model, IReadOnlyList<SuiteRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        var answers = new List<SuiteAnswer>(rows.Count);
        foreach (var row in rows)
        {
            var results = await model.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            answers.Add(AnswerOf(row, results[row.TargetIndex]));
        }

        return answers;
    }

    /// <summary>
    ///     Describes the result of the target word of a row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="result">The result.</param>
    /// <returns>The answer.</returns>
    public static SuiteAnswer AnswerOf(SuiteRow row, ClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(row);
        var analysis = string.Empty;
        if (result.Details is { } d)
        {
            analysis = d.Lemma + "/" + d.PartOfSpeech;
            if (d.Features.Length > 0)
            {
                analysis += "/" + d.Features;
            }
        }
        var target = result.Level switch
        {
            SenseLevel.Sense => result.SenseId ?? string.Empty,
            SenseLevel.Group => result.GroupId ?? string.Empty,
            _ => string.Empty,
        };
        return new SuiteAnswer(SuiteFile.Grade(row, result), result.Level.ToString(), analysis, target, result.Confidence);
    }

    /// <summary>
    ///     Renders the answers of the models as the text of an outcome file.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="answers">The answers parallel to the rows, by model.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderOutcomes(IReadOnlyList<SuiteRow> rows, IReadOnlyDictionary<string, IReadOnlyList<SuiteAnswer>> answers)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(answers);
        var text = new StringBuilder(Header).Append('\n');
        foreach (var model in SuiteFile.Models)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var a = answers[model][i];
                text.Append(model).Append('\t').Append(rows[i].Id).Append('\t').Append(SuiteFile.Name(a.Grade)).Append('\t').Append(a.Level).Append('\t')
                    .Append(a.Analysis).Append('\t').Append(a.Target).Append('\t').Append(a.Confidence.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads an outcome file of the suite.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The answers by model and row id.</returns>
    /// <exception cref="InvalidDataException">Thrown when the header or a line is malformed.</exception>
    public static Dictionary<(string Model, string Id), SuiteAnswer> ReadOutcomes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || !string.Equals(lines[0].TrimEnd('\r'), Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}: the header of a suite outcome file is '{Header.Replace('\t', ',')}'.");
        }

        var answers = new Dictionary<(string, string), SuiteAnswer>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != 7 || !double.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || !TryGrade(c[2], out var grade) || !answers.TryAdd((c[0], c[1]), new SuiteAnswer(grade, c[3], c[4], c[5], confidence)))
            {
                throw new InvalidDataException($"{name}:{(i + 1).ToString(CultureInfo.InvariantCulture)}: expected seven columns, a grade of fine, answer or miss, a number as confidence, and a model and row that occur once.");
            }
        }

        return answers;
    }

    /// <summary>
    ///     Lists the rows whose answer or grade differs between a baseline and the current answers.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="expectations">The expectations by model and row id.</param>
    /// <param name="baseline">The answers of the baseline by model and row id.</param>
    /// <param name="current">The current answers parallel to the rows, by model.</param>
    /// <returns>The changes in file order of the rows, model by model.</returns>
    /// <exception cref="InvalidDataException">Thrown when the baseline has no answer for a row of a model.</exception>
    public static List<SuiteChange> Diff(IReadOnlyList<SuiteRow> rows, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SuiteGrade>> expectations, IReadOnlyDictionary<(string Model, string Id), SuiteAnswer> baseline, IReadOnlyDictionary<string, IReadOnlyList<SuiteAnswer>> current)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(expectations);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var changes = new List<SuiteChange>();
        foreach (var model in SuiteFile.Models)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var old = baseline.TryGetValue((model, rows[i].Id), out var found)
                    ? found
                    : throw new InvalidDataException($"The baseline has no answer of {model} for the row '{rows[i].Id}'.");
                var now = current[model][i];
                if (old.Grade != now.Grade || !string.Equals(old.Text, now.Text, StringComparison.Ordinal))
                {
                    changes.Add(new SuiteChange(rows[i], model, old, now, expectations[model][rows[i].Id]));
                }
            }
        }

        return changes;
    }

    /// <summary>
    ///     Renders the changes as Markdown, one block per row and model.
    /// </summary>
    /// <param name="changes">The changes.</param>
    /// <param name="rowCount">The number of rows of the suite.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderDiff(IReadOnlyList<SuiteChange> changes, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("### Suite changes against the baseline\n\n");
        var models = SuiteFile.Models.Count;
        text.Append(changes.Count.ToString(inv)).Append(" of ").Append((rowCount * models).ToString("N0", inv)).Append(" answers changed: ")
            .Append(changes.Count(c => c.Verdict == "better").ToString(inv)).Append(" better, ")
            .Append(changes.Count(c => c.Verdict == "WORSE").ToString(inv)).Append(" worse, ")
            .Append(changes.Count(c => c.Verdict == "same grade, other answer").ToString(inv)).Append(" with the same grade and another answer.\n\n");
        foreach (var change in changes)
        {
            var row = change.Row;
            var sentence = string.Join(' ', row.Words.Select((w, i) => i == row.TargetIndex ? "{" + w + "}" : w));
            var gold = row.AnalysisLemma + "/" + row.PartOfSpeech + (row.Features.Count == 0 ? string.Empty : "/" + string.Join('+', row.Features)) + (row.SenseIds.Count == 0 ? string.Empty : " " + string.Join(" | ", row.SenseIds));
            text.Append("- **").Append(row.Id).Append("** (").Append(change.Model).Append("): ").Append(change.Verdict).Append('\n')
                .Append("  - sentence: ").Append(sentence).Append('\n')
                .Append("  - old: ").Append(change.Old.Text).Append(" (").Append(SuiteFile.Name(change.Old.Grade)).Append(", ").Append(change.Old.Confidence.ToString("0.000", inv)).Append(")\n")
                .Append("  - new: ").Append(change.New.Text).Append(" (").Append(SuiteFile.Name(change.New.Grade)).Append(", ").Append(change.New.Confidence.ToString("0.000", inv)).Append(")\n")
                .Append("  - gold: ").Append(gold).Append('\n')
                .Append("  - expectation: ").Append(SuiteFile.Name(change.Expected)).Append(change.New.Grade < change.Expected ? " (the new grade is BELOW it)" : string.Empty).Append('\n');
        }

        return text.ToString();
    }

    private static bool TryGrade(string text, out SuiteGrade grade)
    {
        grade = text switch
        {
            "fine" => SuiteGrade.Fine,
            "answer" => SuiteGrade.Answer,
            _ => SuiteGrade.Miss,
        };
        return text is "fine" or "answer" or "miss";
    }
}
