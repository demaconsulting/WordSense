using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Renders the part-of-speech audit report (<c>docs/build_notes/pos-audit.md</c>).
/// </summary>
internal static class PosAuditReport
{
    /// <summary>
    ///     Renders the report.
    /// </summary>
    /// <param name="lines">The merged audit lines.</param>
    /// <param name="notes">Optional free text (pilot and probe results) inserted into the method section.</param>
    /// <returns>The markdown text, LF line endings.</returns>
    public static string Render(List<PosAudit.AuditLine> lines, string? notes)
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        var senses = lines.GroupBy(l => (l.Lemma, l.Sense))
            .OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.Sense, StringComparer.Ordinal).ToList();

        text.Append("# Part-of-Speech Audit\n\n");
        text.Append("Every anchor and holdout sentence of the corpus was judged for the real part of speech and form of the braced\n");
        text.Append("word. The audit is the evidence behind the relabel of the sense inventory (see `corpus/audit/`).\n\n");
        text.Append("## Method\n\n");
        text.Append("The corpus was cut into chunks. Two independent judges (A: corpus order, B: reversed order) saw only the\n");
        text.Append("lemma and the sentence, never the current label. Rows where the two judges agreed on a part of speech with\n");
        text.Append("at least medium confidence were accepted; every other row went to a third judge (C) who saw both answers.\n");
        text.Append("C is final only when it is confident and matches A or B; otherwise the row is `undecided` and is never\n");
        text.Append("used as evidence. A blind probe of about 5 % of the accepted rows re-checks the agreement. The judge\n");
        text.Append("prompt is frozen in `corpus/audit/judge-prompt.md`. The model was not used.\n\n");
        if (!string.IsNullOrWhiteSpace(notes))
        {
            text.Append(notes.Trim()).Append("\n\n");
        }

        text.Append("## Results\n\n");
        text.Append("| Resolution | Rows |\n| --- | ---: |\n");
        foreach (var g in lines.GroupBy(l => l.Resolution).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append(inv, $"| {g.Key} | {g.Count()} |\n");
        }

        text.Append(inv, $"| total | {lines.Count} |\n\n");
        text.Append("| Final part of speech | Rows |\n| --- | ---: |\n");
        foreach (var g in lines.GroupBy(l => l.FinalPos).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append(inv, $"| {g.Key} | {g.Count()} |\n");
        }

        text.Append("\n## Senses That Are Not Pure\n\n");
        text.Append("A sense is PURE when one part of speech covers all rows. NOISE means the minority is at most 2 rows or 5 %.\n");
        text.Append("MIXED means a larger minority. Senses with undecided rows are marked.\n\n");
        text.Append("| Lemma | Sense | Flag | Distribution |\n| --- | --- | --- | --- |\n");
        var counts = new Dictionary<string, int>();
        foreach (var g in senses)
        {
            var dist = g.GroupBy(l => l.FinalPos).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var total = g.Count();
            var minority = total - dist[0].Count();
            var flag = Flag(minority, total);
            counts[flag] = counts.GetValueOrDefault(flag) + 1;
            if (flag != "PURE")
            {
                text.Append(inv, $"| {g.Key.Lemma} | {g.Key.Sense} | {flag} | {string.Join(", ", dist.Select(x => $"{x.Key} {x.Count()}"))} |\n");
            }
        }

        text.Append("\n| Flag | Senses |\n| --- | ---: |\n");
        foreach (var (flag, n) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            text.Append(inv, $"| {flag} | {n} |\n");
        }

        text.Append("\n## Forms Worth Knowing\n\n");
        var comparative = lines.Where(l => l.FinalForm is "comparative" or "superlative").ToList();
        text.Append(inv, $"{comparative.Count} rows are comparative or superlative adjectives or adverbs; ");
        text.Append(inv, $"{lines.Count(l => l.FinalForm == "pastpart")} are past participles and {lines.Count(l => l.FinalForm == "presentpart")} are present participles.\n");
        return text.ToString();
    }

    private static string Flag(int minority, int total)
    {
        if (minority == 0)
        {
            return "PURE";
        }

        return minority <= Math.Max(2, total / 20) ? "NOISE" : "MIXED";
    }
}
