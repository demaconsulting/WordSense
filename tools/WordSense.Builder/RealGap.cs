using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One sense of a lemma in a gap plan: the menu entry the judges see and what the import adds for it.
/// </summary>
/// <param name="Lemma">The lower-case lemma.</param>
/// <param name="Sense">The sense label (an existing sense of the inventory).</param>
/// <param name="Definition">The short definition shown to the judges.</param>
/// <param name="AddAnchors">The number of real anchors to add for the sense (0 for a sense that only appears in the menu).</param>
/// <param name="RealHoldout">The number of real holdout rows the sense should have in total, counting the real holdout rows that exist already.</param>
internal sealed record GapSense(string Lemma, string Sense, string Definition, int AddAnchors, int RealHoldout);

/// <summary>
///     Adds real anchors and a separate real holdout for senses with a coverage gap, without touching the existing rows.
/// </summary>
/// <remarks>
///     A gap batch has a plan (<c>corpus/audit/NAME-plan.tsv</c>) and writes <c>anchors/NAME.tsv</c>,
///     <c>holdout/NAME.tsv</c> and <c>provenance/NAME.tsv</c> (NAME is <c>real-gap-N</c>). The holdout rows are chosen from
///     contributors other than the new anchors where possible and are never moved into the anchors.
/// </remarks>
internal static class RealGap
{
    private const string PlanHeader = "lemma\tsense\tdefinition\tadd_anchors\treal_holdout";

    /// <summary>
    ///     The prefix of a gap batch name.
    /// </summary>
    public const string Prefix = "real-gap-";

    /// <summary>
    ///     Checks a batch name.
    /// </summary>
    /// <param name="name">The batch name, for example <c>real-gap-1</c>.</param>
    /// <exception cref="ArgumentException">Thrown when the name does not have the form <c>real-gap-N</c>.</exception>
    public static void ValidateName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length || !name[Prefix.Length..].All(char.IsAsciiDigit))
        {
            throw new ArgumentException($"The gap batch name must have the form {Prefix}N but was '{name}'.");
        }
    }

    /// <summary>
    ///     Reads a gap plan.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="name">The batch name.</param>
    /// <returns>The plan senses in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is malformed.</exception>
    public static List<GapSense> ReadPlan(string corpus, string name)
    {
        ValidateName(name);
        var file = $"{name}-plan.tsv";
        var lines = File.ReadAllLines(Path.Combine(corpus, "audit", file), new UTF8Encoding(false));
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != PlanHeader)
        {
            throw new InvalidDataException($"{file}:1: the header must be '{PlanHeader.Replace("\t", "<TAB>", StringComparison.Ordinal)}'.");
        }

        var plan = new List<GapSense>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != 5 || !int.TryParse(c[3], out var anchors) || !int.TryParse(c[4], out var holdout) || anchors < 0 || holdout < 0)
            {
                throw new InvalidDataException($"{file}:{i + 1}: expected 5 columns with non-negative counts in the last two.");
            }

            plan.Add(new GapSense(c[0], c[1], c[2], anchors, holdout));
        }

        return plan;
    }

    /// <summary>
    ///     Imports the judged candidates of a gap batch.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="name">The batch name, for example <c>real-gap-1</c>.</param>
    /// <param name="candidates">The harvested candidates.</param>
    /// <param name="judgements">The merged sense judgements by row id.</param>
    /// <returns>A report of the import.</returns>
    public static string Import(string corpus, string name, IReadOnlyList<Candidate> candidates, IReadOnlyDictionary<string, SenseJudgement> judgements)
    {
        var plan = ReadPlan(corpus, name);
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var invalid = plan.FirstOrDefault(p => !inventory.Contains(p.Lemma, p.Sense));
        if (invalid is not null)
        {
            throw new InvalidDataException($"{name}-plan.tsv: {invalid.Lemma}:{invalid.Sense} is not a sense of the inventory.");
        }

        var ownFile = name + ".tsv";
        var existingRealHoldout = ReadRealHoldoutCounts(corpus, ownFile);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var split in new[] { "anchors", "holdout" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(corpus, split), "*.tsv").Where(f => Path.GetFileName(f) != ownFile))
            {
                taken.UnionWith(Corpus.ReadSentences(file).Select(r => PosAudit.RowId(r.Lemma, r.Sentence)));
            }
        }

        var rowsA = new List<(string Lemma, string Sense, Candidate Real)>();
        var rowsH = new List<(string Lemma, string Sense, Candidate Real)>();
        var report = new StringBuilder("lemma\tsense\tpool\treal anchors added\treal holdout added\treal holdout total\tnote\n");
        foreach (var p in plan.Where(p => p.AddAnchors > 0 || p.RealHoldout > 0))
        {
            var pool = candidates.Where(c => c.Lemma == p.Lemma && !taken.Contains(c.Id) && judgements.TryGetValue(c.Id, out var j) && j.Final == p.Sense)
                .GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
            var existing = existingRealHoldout.GetValueOrDefault((p.Lemma, p.Sense));
            var needH = Math.Max(0, p.RealHoldout - existing);
            var (takeA, takeH, note) = RealImport.Allocate(pool, p.AddAnchors, needH);
            rowsA.AddRange(takeA.Select(c => (p.Lemma, p.Sense, c)));
            rowsH.AddRange(takeH.Select(c => (p.Lemma, p.Sense, c)));
            var shortfall = takeA.Count < p.AddAnchors || existing + takeH.Count < p.RealHoldout ? "SHORT" : string.Empty;
            report.AppendLine($"{p.Lemma}\t{p.Sense}\t{pool.Count}\t{takeA.Count}\t{takeH.Count}\t{existing + takeH.Count}\t{note}{(note.Length > 0 && shortfall.Length > 0 ? "; " : string.Empty)}{shortfall}");
        }

        WriteRows(Path.Combine(corpus, "anchors", ownFile), rowsA);
        WriteRows(Path.Combine(corpus, "holdout", ownFile), rowsH);
        var provenance = rowsA.Select(r => ToProvenance(r.Real, r.Sense, "anchors")).Concat(rowsH.Select(r => ToProvenance(r.Real, r.Sense, "holdout")));
        RealImport.WriteProvenance(Path.Combine(corpus, "provenance", ownFile), provenance);
        report.AppendLine($"anchors {rowsA.Count}, holdout {rowsH.Count}.");
        return report.ToString();
    }

    private static Dictionary<(string Lemma, string Sense), int> ReadRealHoldoutCounts(string corpus, string ownFile)
    {
        var counts = new Dictionary<(string, string), int>();
        var directory = Path.Combine(corpus, "provenance");
        if (!Directory.Exists(directory))
        {
            return counts;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv").Where(f => Path.GetFileName(f) != ownFile))
        {
            foreach (var row in RealImport.ReadProvenance(file).Where(r => r.Split == "holdout" && r.Origin == "real"))
            {
                counts[(row.Lemma, row.Sense)] = counts.GetValueOrDefault((row.Lemma, row.Sense)) + 1;
            }
        }

        return counts;
    }

    private static void WriteRows(string path, IEnumerable<(string Lemma, string Sense, Candidate Real)> rows)
    {
        var text = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal).ThenBy(r => r.Real.Sentence, StringComparer.Ordinal))
        {
            text.Append($"{r.Lemma}\t{r.Sense}\t{r.Real.Sentence}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static ProvenanceRow ToProvenance(Candidate c, string sense, string split) =>
        new(c.Id, c.Lemma, sense, split, c.Source, c.Licence, c.SourceKey, c.Url, c.Contributor, "real");
}
