using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The answer of a model for the target word of one row of the shape probe set.
/// </summary>
/// <param name="Part">The part of speech of the selected analysis, or null when the word is not answered (not in the lexicon).</param>
/// <param name="Confidence">The confidence of the answer.</param>
internal sealed record ShapeAnswer(PartOfSpeech? Part, double Confidence);

/// <summary>
///     Measures the noun/verb decision of the models on the shape probe set and reports it per shape and per model.
/// </summary>
/// <remarks>
///     <para>
///     A row is right when the part of speech of the selected analysis of the target word is the gold part of speech
///     (<see cref="ShapeProbeRow.Gold"/>); a word that is not answered is wrong. The sentence of a row is classified on its
///     own, so the answer does not depend on the other rows. The <i>harmful</i> answer is a gold noun that a model answers as a
///     verb with a confidence of at least <see cref="ShapeProbeFile.HarmfulConfidence"/>: a consumer that trusts the answer acts
///     on it, so this is the error that costs most. The set is evaluation data only: the figures are reported, never tuned on.
///     </para>
/// </remarks>
internal static class ShapeProbeEvaluator
{
    /// <summary>
    ///     Classifies the sentence of every row with a model.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answers parallel to the rows.</returns>
    public static async Task<IReadOnlyList<ShapeAnswer>> AnswerAsync(IWordSenseModel model, IReadOnlyList<ShapeProbeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        var answers = new List<ShapeAnswer>(rows.Count);
        foreach (var row in rows)
        {
            var results = await model.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            var result = results[row.TargetIndex];
            answers.Add(new ShapeAnswer(result.Details?.PartOfSpeech, result.Confidence));
        }

        return answers;
    }

    /// <summary>
    ///     Tells whether an answer is right.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>True when the answered part of speech is the gold part of speech.</returns>
    public static bool IsCorrect(ShapeProbeRow row, ShapeAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(answer);
        return answer.Part == row.Gold;
    }

    /// <summary>
    ///     Tells whether an answer is harmful: a gold noun answered as a verb with a confidence of at least
    ///     <see cref="ShapeProbeFile.HarmfulConfidence"/>.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>True for a harmful answer.</returns>
    public static bool IsHarmful(ShapeProbeRow row, ShapeAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(answer);
        return row.Gold == PartOfSpeech.Noun && answer.Part == PartOfSpeech.Verb && answer.Confidence >= ShapeProbeFile.HarmfulConfidence;
    }

    /// <summary>
    ///     Renders the accuracy per shape and model and the harmful rate per model.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="models">The answers of each model, parallel to the rows, with the display name of the model.</param>
    /// <returns>The Markdown text with line feeds.</returns>
    public static string Render(IReadOnlyList<ShapeProbeRow> rows, IReadOnlyList<(string Model, IReadOnlyList<ShapeAnswer> Answers)> models)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(models);
        var text = new StringBuilder();
        text.Append("### Shape probe\n\n");
        text.Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" sentences, one target word each. Accuracy = the selected analysis has the gold part of speech (noun or verb).\n\n");
        text.Append("| Shape | Gold | Rows |").Append(string.Concat(models.Select(m => " " + m.Model + " |"))).Append('\n');
        text.Append("| --- | --- | ---: |").Append(string.Concat(models.Select(_ => " ---: |"))).Append('\n');
        foreach (var (shape, gold) in ShapeProbeFile.Shapes)
        {
            AppendAccuracy(text, shape, gold.ToString(), rows, models, i => string.Equals(rows[i].Shape, shape, StringComparison.Ordinal));
        }

        AppendAccuracy(text, "all noun shapes", "Noun", rows, models, i => rows[i].Gold == PartOfSpeech.Noun);
        AppendAccuracy(text, "all verb shapes", "Verb", rows, models, i => rows[i].Gold == PartOfSpeech.Verb);
        AppendAccuracy(text, "all", string.Empty, rows, models, _ => true);

        text.Append("\nHarmful = gold noun answered as a verb with a confidence of at least ").Append(ShapeProbeFile.HarmfulConfidence.ToString("0.00", CultureInfo.InvariantCulture)).Append(".\n\n");
        text.Append("| Shape | Noun rows |").Append(string.Concat(models.Select(m => " " + m.Model + " harmful |"))).Append('\n');
        text.Append("| --- | ---: |").Append(string.Concat(models.Select(_ => " ---: |"))).Append('\n');
        foreach (var (shape, _) in ShapeProbeFile.Shapes.Where(s => s.Gold == PartOfSpeech.Noun))
        {
            AppendHarmful(text, shape, rows, models, i => string.Equals(rows[i].Shape, shape, StringComparison.Ordinal));
        }

        AppendHarmful(text, "all noun shapes", rows, models, i => rows[i].Gold == PartOfSpeech.Noun);
        return text.ToString();
    }

    /// <summary>
    ///     Renders the answer of every model for every row as a tab-separated table, to compare two runs row by row.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="models">The answers of each model, parallel to the rows, with the display name of the model.</param>
    /// <returns>The text with line feeds and a header line.</returns>
    public static string RenderOutcomes(IReadOnlyList<ShapeProbeRow> rows, IReadOnlyList<(string Model, IReadOnlyList<ShapeAnswer> Answers)> models)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(models);
        var text = new StringBuilder("id\tshape\tlemma\tgold\tmodel\tanswered\tconfidence\tcorrect\tharmful\n");
        foreach (var (model, answers) in models)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                text.Append(string.Join('\t', rows[i].Id, rows[i].Shape, rows[i].Lemma, rows[i].Gold, model, answers[i].Part?.ToString() ?? "none", answers[i].Confidence.ToString("0.000", CultureInfo.InvariantCulture), IsCorrect(rows[i], answers[i]) ? "1" : "0", IsHarmful(rows[i], answers[i]) ? "1" : "0")).Append('\n');
            }
        }

        return text.ToString();
    }

    private static void AppendAccuracy(StringBuilder text, string label, string gold, IReadOnlyList<ShapeProbeRow> rows, IReadOnlyList<(string Model, IReadOnlyList<ShapeAnswer> Answers)> models, Func<int, bool> select)
    {
        var indexes = Enumerable.Range(0, rows.Count).Where(select).ToList();
        text.Append("| ").Append(label).Append(" | ").Append(gold).Append(" | ").Append(indexes.Count.ToString(CultureInfo.InvariantCulture)).Append(" |");
        foreach (var (_, answers) in models)
        {
            var correct = indexes.Count(i => IsCorrect(rows[i], answers[i]));
            text.Append(' ').Append(EvaluationResult.Percent(correct, indexes.Count)).Append(" |");
        }

        text.Append('\n');
    }

    private static void AppendHarmful(StringBuilder text, string label, IReadOnlyList<ShapeProbeRow> rows, IReadOnlyList<(string Model, IReadOnlyList<ShapeAnswer> Answers)> models, Func<int, bool> select)
    {
        var indexes = Enumerable.Range(0, rows.Count).Where(select).ToList();
        text.Append("| ").Append(label).Append(" | ").Append(indexes.Count.ToString(CultureInfo.InvariantCulture)).Append(" |");
        foreach (var (_, answers) in models)
        {
            var harmful = indexes.Count(i => IsHarmful(rows[i], answers[i]));
            text.Append(' ').Append(harmful.ToString(CultureInfo.InvariantCulture)).Append(" (").Append(EvaluationResult.Percent(harmful, indexes.Count)).Append(") |");
        }

        text.Append('\n');
    }
}
