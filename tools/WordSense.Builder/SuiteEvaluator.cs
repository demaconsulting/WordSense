using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     Runs the reviewed suite against the models and reports the pass rates.
/// </summary>
/// <remarks>
///     <para>
///     The same grading rule as the suite tests of the model projects is used (<see cref="SuiteFile.Grade"/>): a row is
///     answered when the answer is right at the level the model gave; it is fine when, besides, the most probable
///     ranked sense is an acceptable sense. The expectation file records what each model keeps; it is written once with
///     <c>--write-expectations</c> for a new suite or model and is changed afterwards only by a reviewed commit
///     (an improvement raises an expectation; a regression is never hidden by lowering one).
///     </para>
/// </remarks>
internal static class SuiteEvaluator
{
    /// <summary>
    ///     Grades the rows with every model of the expectation file.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grades parallel to the rows, by model name.</returns>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<SuiteGrade>>> RunAsync(string repository, IReadOnlyList<SuiteRow> rows, TextWriter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(progress);
        var grades = new Dictionary<string, IReadOnlyList<SuiteGrade>>(StringComparer.Ordinal);
        foreach (var name in SuiteFile.Models)
        {
            var info = ModelCatalog.Find(name);
            using var model = ModelCatalog.Open(info, repository);
            await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
            grades[name] = await SuiteFile.GradeAsync(model, rows, cancellationToken).ConfigureAwait(false);
            progress.WriteLine($"{name}: suite done.");
        }

        return grades;
    }

    /// <summary>
    ///     Renders the pass rates per list and model, and the rows that fall below the expectation of a model.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="grades">The grades by model.</param>
    /// <param name="expectations">The expectations by model, or null when there is no expectation file yet.</param>
    /// <returns>The Markdown text with line feeds.</returns>
    public static string Render(IReadOnlyList<SuiteRow> rows, IReadOnlyDictionary<string, IReadOnlyList<SuiteGrade>> grades, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SuiteGrade>>? expectations)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(grades);
        var text = new StringBuilder();
        text.Append("### Reviewed suite\n\n");
        text.Append("Answered = right at the level the model answered; fine = answered and the most probable ranked sense is acceptable.\n\n");
        text.Append("| List | Rows | ").Append(string.Join(" | ", SuiteFile.Models.Select(m => m + " answered / fine"))).Append(" |\n");
        text.Append("| --- | ---: |").Append(string.Concat(SuiteFile.Models.Select(_ => " ---: |"))).Append('\n');
        foreach (var list in rows.Select(r => r.List).Distinct(StringComparer.Ordinal).Append("all"))
        {
            var indexes = Enumerable.Range(0, rows.Count).Where(i => list == "all" || rows[i].List == list).ToList();
            text.Append("| ").Append(list).Append(" | ").Append(indexes.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var model in SuiteFile.Models)
            {
                var answered = indexes.Count(i => grades[model][i] >= SuiteGrade.Answer);
                var fine = indexes.Count(i => grades[model][i] == SuiteGrade.Fine);
                text.Append(" | ").Append(EvaluationResult.Percent(answered, indexes.Count)).Append(" / ").Append(EvaluationResult.Percent(fine, indexes.Count));
            }

            text.Append(" |\n");
        }

        if (expectations is not null)
        {
            text.Append('\n');
            foreach (var model in SuiteFile.Models)
            {
                var regressions = SuiteFile.Regressions(rows, expectations[model], grades[model]);
                text.Append("- ").Append(model).Append(": ").Append(regressions.Count == 0 ? "no regression against the expectations" : regressions.Count.ToString(CultureInfo.InvariantCulture) + " regression(s): " + string.Join("; ", regressions)).Append('\n');
                var improved = Enumerable.Range(0, rows.Count).Count(i => grades[model][i] > expectations[model][rows[i].Id]);
                text.Append("  (").Append(improved.ToString(CultureInfo.InvariantCulture)).Append(" row(s) are better than their expectation)\n");
            }
        }

        return text.ToString();
    }
}
