using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One sense of a lemma in the restore plan: a part of the sense menu that the judges see.
/// </summary>
/// <param name="Lemma">The lower-case lemma.</param>
/// <param name="Sense">The sense label.</param>
/// <param name="Tags">The tags, sorted ordinally.</param>
/// <param name="Group">The optional group.</param>
/// <param name="From">The <c>old_sense:pos</c> keys of dropped rows that map to this sense.</param>
/// <param name="Definition">The short definition shown to the judges.</param>
/// <param name="IsNew">True when the sense is not in the committed inventory before the restore.</param>
internal sealed record PlanSense(string Lemma, string Sense, string[] Tags, string? Group, string[] From, string Definition, bool IsNew);

/// <summary>
///     One dropped row that the restore either maps to a sense or hands to the sense judges.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Sense">The mapped sense, or null when the part-of-speech audit could not decide.</param>
/// <param name="Split">Either <c>anchors</c> or <c>holdout</c>.</param>
/// <param name="Sentence">The braced sentence.</param>
internal sealed record PoolRow(string Lemma, string? Sense, string Split, string Sentence);

/// <summary>
///     The restore plan and the pool of previously dropped, project-authored rows that can be reinstated.
/// </summary>
/// <remarks>
///     The plan (<c>corpus/audit/restore-plan.tsv</c>) lists, for every lemma that lost senses for count reasons,
///     the complete sense menu with definitions, and which audited dropped rows map to which sense.
/// </remarks>
internal static class RealPlan
{
    /// <summary>
    ///     The minimum number of anchors of a sense.
    /// </summary>
    public const int MinAnchors = 14;

    /// <summary>
    ///     The minimum number of holdout rows of a sense.
    /// </summary>
    public const int MinHoldout = 8;

    /// <summary>
    ///     The file name used for the restored rows in the anchors and holdout folders.
    /// </summary>
    public const string RestoreFile = "real-restore.tsv";

    private const string PlanHeader = "lemma\tsense\ttags\tgroup\tfrom\tdefinition\tstatus";

    /// <summary>
    ///     Reads the restore plan.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The plan senses in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is malformed.</exception>
    public static List<PlanSense> ReadPlan(string corpus)
    {
        var path = Path.Combine(corpus, "audit", "restore-plan.tsv");
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != PlanHeader)
        {
            throw new InvalidDataException($"restore-plan.tsv:1: the header must be '{PlanHeader.Replace("\t", "<TAB>", StringComparison.Ordinal)}'.");
        }

        var plan = new List<PlanSense>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != 7 || (c[6] != "new" && c[6] != "existing"))
            {
                throw new InvalidDataException($"restore-plan.tsv:{i + 1}: expected 7 columns and status new or existing.");
            }

            plan.Add(new PlanSense(
                c[0], c[1], c[2].Split('|'), c[3].Length == 0 ? null : c[3],
                c[4].Split(',', StringSplitOptions.RemoveEmptyEntries), c[5], c[6] == "new"));
        }

        return plan;
    }

    /// <summary>
    ///     Reads the corpus rows of the given lemmas, ignoring the restore file and the authored batches
    ///     (<c>new-authored-*.tsv</c>) so that the result is stable when the import or an authored import has already run.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split">Either <c>anchors</c> or <c>holdout</c>.</param>
    /// <param name="lemmas">The lemmas to keep.</param>
    /// <returns>The rows.</returns>
    public static List<CorpusRow> ReadExisting(string corpus, string split, ISet<string> lemmas)
    {
        var directory = Path.Combine(corpus, split);
        var rows = new List<CorpusRow>();
        foreach (var file in Directory.GetFiles(directory, "*.tsv").Where(f => Path.GetFileName(f) != RestoreFile && !RealAuthored.IsAuthoredFile(Path.GetFileName(f)) && !NounVerbImport.IsNounVerbFile(Path.GetFileName(f))).OrderBy(f => f, StringComparer.Ordinal))
        {
            rows.AddRange(Corpus.ReadSentences(file).Where(r => lemmas.Contains(r.Lemma)));
        }

        return rows;
    }

    /// <summary>
    ///     Builds the pool of dropped rows of the plan lemmas.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The mapped rows (project-authored, reinstated), the rows left to the judges and the unmapped rows.</returns>
    public static (List<PoolRow> Mapped, List<PoolRow> Undecided, List<PoolRow> Unmapped) ReadPool(string corpus, IReadOnlyList<PlanSense> plan)
    {
        var lemmas = plan.Select(p => p.Lemma).ToHashSet(StringComparer.Ordinal);
        var audit = PosAudit.ReadResults(Path.Combine(corpus, "audit", "pos-audit.tsv")).ToDictionary(a => a.Id, a => a.FinalPos, StringComparer.Ordinal);
        var (mapped, undecided, unmapped) = (new List<PoolRow>(), new List<PoolRow>(), new List<PoolRow>());
        var lines = File.ReadAllLines(Path.Combine(corpus, "audit", "dropped-rows.tsv"), new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var c = lines[i].TrimEnd('\r').Split('\t');
            if (c.Length < 6)
            {
                throw new InvalidDataException($"dropped-rows.tsv:{i + 1}: expected at least 6 columns but found {c.Length}.");
            }

            if (!lemmas.Contains(c[1]))
            {
                continue;
            }

            var pos = audit.GetValueOrDefault(c[0], PosAudit.Undecided);
            var target = plan.FirstOrDefault(p => p.Lemma == c[1] && p.From.Contains(c[2] + ":" + pos, StringComparer.Ordinal));
            if (pos == PosAudit.Undecided)
            {
                undecided.Add(new PoolRow(c[1], null, c[3], c[5]));
            }
            else if (target is null)
            {
                unmapped.Add(new PoolRow(c[1], null, c[3], c[5]));
            }
            else
            {
                mapped.Add(new PoolRow(c[1], target.Sense, c[3], c[5]));
            }
        }

        return (mapped, undecided, unmapped);
    }

    /// <summary>
    ///     Renders the gap report: per planned sense the rows that exist, the project-authored rows that can be reinstated and the
    ///     number of real rows still needed.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The report text.</returns>
    public static string Gap(string corpus)
    {
        var plan = ReadPlan(corpus);
        var lemmas = plan.Select(p => p.Lemma).ToHashSet(StringComparer.Ordinal);
        var anchors = ReadExisting(corpus, "anchors", lemmas);
        var holdout = ReadExisting(corpus, "holdout", lemmas);
        var (mapped, undecided, unmapped) = ReadPool(corpus, plan);
        var text = new StringBuilder();
        text.AppendLine("lemma\tsense\tstatus\texisting a/h\tauthored a/h\tneed real a/h");
        foreach (var p in plan)
        {
            var (ea, eh) = (anchors.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense), holdout.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense));
            var (la, lh) = (mapped.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense && r.Split == "anchors"), mapped.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense && r.Split == "holdout"));
            text.AppendLine($"{p.Lemma}\t{p.Sense}\t{(p.IsNew ? "new" : "existing")}\t{ea}/{eh}\t{la}/{lh}\t{Math.Max(0, MinAnchors - ea - la)}/{Math.Max(0, MinHoldout - eh - lh)}");
        }

        text.AppendLine($"undecided rows for the sense judges: {undecided.Count}; rows that map to no planned sense: {unmapped.Count}.");
        return text.ToString();
    }
}
