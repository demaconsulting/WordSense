using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One row of a provenance sidecar file.
/// </summary>
/// <param name="Id">The row id (SHA-256 prefix over <c>lemma|sentence</c>).</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="Sense">The sense.</param>
/// <param name="Split">The split: <c>anchors</c>, <c>holdout</c> or <c>eval</c>.</param>
/// <param name="Source">The source name.</param>
/// <param name="Licence">The licence.</param>
/// <param name="SourceKey">The source key.</param>
/// <param name="Url">The URL.</param>
/// <param name="Contributor">The contributor or author and work.</param>
/// <param name="Origin">One of <c>real</c>, <c>authored</c> and <c>llm-corpus</c>.</param>
internal sealed record ProvenanceRow(string Id, string Lemma, string Sense, string Split, string Source, string Licence, string SourceKey, string Url, string Contributor, string Origin);

/// <summary>
///     Imports judged real sentences into the corpus, restores the previously dropped project-authored rows and keeps
///     the provenance sidecar.
/// </summary>
internal static class RealImport
{
    /// <summary>
    ///     The accepted licences of provenance rows.
    /// </summary>
    public static readonly string[] Licences =
    [
        "CC0 1.0", "CC BY 2.0 FR", "CC BY 4.0", "CC BY-SA 3.0", "CC BY-SA 4.0", "Public domain", "MIT (project-authored)", "web-excerpt"
    ];

    /// <summary>
    ///     The accepted origins.
    /// </summary>
    public static readonly string[] Origins = ["real", "authored", "llm-corpus"];

    /// <summary>
    ///     The maximum number of words of a sentence.
    /// </summary>
    public const int MaxWords = 35;

    /// <summary>
    ///     The number of anchors that real sentences are collected up to (above the minimum, so that real text dominates).
    /// </summary>
    public const int TargetAnchors = 20;

    /// <summary>
    ///     The number of holdout rows that real sentences are collected up to.
    /// </summary>
    public const int TargetHoldout = 10;

    private const string ProvenanceHeader = "id\tlemma\tsense\tsplit\tsource\tlicence\tsource_key\turl\tcontributor\torigin";

    /// <summary>
    ///     Gets the licence rank: open and public-domain first.
    /// </summary>
    /// <param name="licence">The licence.</param>
    /// <returns>0 for CC0 and public domain, 1 for attributed licences, 2 for web excerpts.</returns>
    public static int LicenceRank(string licence) => licence switch
    {
        "CC0 1.0" or "Public domain" => 0,
        "web-excerpt" => 2,
        _ => 1
    };

    /// <summary>
    ///     Imports the judged candidates.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="candidates">The harvested candidates (new real rows).</param>
    /// <param name="judgements">The merged sense judgements by row id.</param>
    /// <returns>A report of the import.</returns>
    public static string Import(string corpus, IReadOnlyList<Candidate> candidates, IReadOnlyDictionary<string, SenseJudgement> judgements)
    {
        var plan = RealPlan.ReadPlan(corpus);
        var lemmas = plan.Select(p => p.Lemma).ToHashSet(StringComparer.Ordinal);
        var existingA = RealPlan.ReadExisting(corpus, "anchors", lemmas);
        var existingH = RealPlan.ReadExisting(corpus, "holdout", lemmas);
        var (mapped, undecided, _) = RealPlan.ReadPool(corpus, plan);

        // Project-authored rows that come back: the mapped ones and the undecided ones that the sense judges settled
        var authored = mapped.ToList();
        foreach (var row in undecided)
        {
            if (judgements.TryGetValue(PosAudit.RowId(row.Lemma, row.Sentence), out var j) && plan.Any(p => p.Lemma == row.Lemma && p.Sense == j.Final))
            {
                authored.Add(row with { Sense = j.Final });
            }
        }

        var report = new StringBuilder();
        var rowsA = new List<(string Lemma, string Sense, string Sentence, Candidate? Real)>();
        var rowsH = new List<(string Lemma, string Sense, string Sentence, Candidate? Real)>();
        var provenance = new List<ProvenanceRow>();

        report.AppendLine("lemma\tsense\tauthored a/h\treal a/h\ttotal a/h\tnote");
        var added = new HashSet<(string, string)>();
        foreach (var p in plan)
        {
            var baseA = existingA.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense);
            var baseH = existingH.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense);
            var pool = candidates.Where(c => c.Lemma == p.Lemma && judgements.TryGetValue(c.Id, out var j) && j.Final == p.Sense)
                .GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();

            // Real sentences first, up to the target; project-authored rows only fill what the minimum still needs
            // A small pool is split by the deficits first, then by the proportion of the minimums
            var needA = Math.Max(0, TargetAnchors - baseA);
            var needH = Math.Max(0, TargetHoldout - baseH);
            if (pool.Count < needA + needH)
            {
                var deficitA = Math.Max(0, RealPlan.MinAnchors - baseA);
                var deficitH = Math.Max(0, RealPlan.MinHoldout - baseH);
                var extra = Math.Max(0, pool.Count - deficitA - deficitH);
                var holdout = deficitA + deficitH > pool.Count && deficitA + deficitH > 0 ? pool.Count * deficitH / (deficitA + deficitH) : deficitH;
                var anchors = deficitA + deficitH > pool.Count ? pool.Count - holdout : deficitA;
                var extraH = Math.Min(needH - deficitH, extra * RealPlan.MinHoldout / (RealPlan.MinAnchors + RealPlan.MinHoldout));
                var extraA = Math.Min(needA - deficitA, extra - extraH);
                needH = holdout + Math.Max(0, extraH);
                needA = anchors + Math.Max(0, extraA);
            }

            var (takeA, takeH, note) = Allocate(pool, needA, needH);
            foreach (var c in takeA)
            {
                rowsA.Add((c.Lemma, p.Sense, c.Sentence, c));
            }

            foreach (var c in takeH)
            {
                rowsH.Add((c.Lemma, p.Sense, c.Sentence, c));
            }

            var fillA = authored.Where(r => r.Lemma == p.Lemma && r.Sense == p.Sense && r.Split == "anchors")
                .OrderBy(r => PosAudit.RowId(r.Lemma, r.Sentence), StringComparer.Ordinal).Take(Math.Max(0, RealPlan.MinAnchors - baseA - takeA.Count)).ToList();
            var fillH = authored.Where(r => r.Lemma == p.Lemma && r.Sense == p.Sense && r.Split == "holdout")
                .OrderBy(r => PosAudit.RowId(r.Lemma, r.Sentence), StringComparer.Ordinal).Take(Math.Max(0, RealPlan.MinHoldout - baseH - takeH.Count)).ToList();
            rowsA.AddRange(fillA.Select(r => (r.Lemma, p.Sense, r.Sentence, (Candidate?)null)));
            rowsH.AddRange(fillH.Select(r => (r.Lemma, p.Sense, r.Sentence, (Candidate?)null)));

            var (ta, th) = (baseA + takeA.Count + fillA.Count, baseH + takeH.Count + fillH.Count);
            if (p.IsNew && ta >= 4)
            {
                added.Add((p.Lemma, p.Sense));
            }

            var lacking = ta < RealPlan.MinAnchors || th < RealPlan.MinHoldout ? $"LACKING (anchors {ta}/{RealPlan.MinAnchors}, holdout {th}/{RealPlan.MinHoldout}); pool {pool.Count}" : string.Empty;
            report.AppendLine($"{p.Lemma}\t{p.Sense}\t{fillA.Count}/{fillH.Count}\t{takeA.Count}/{takeH.Count}\t{ta}/{th}\t{note}{(note.Length > 0 && lacking.Length > 0 ? "; " : string.Empty)}{lacking}");
        }

        // A new sense with fewer than four anchors cannot be trained, so its rows are left out and the sense is reported
        bool Included((string Lemma, string Sense, string Sentence, Candidate? Real) r) =>
            added.Contains((r.Lemma, r.Sense)) || !plan.First(p => p.Lemma == r.Lemma && p.Sense == r.Sense).IsNew;
        rowsA = rowsA.Where(Included).ToList();
        rowsH = rowsH.Where(Included).ToList();

        WriteRows(Path.Combine(corpus, "anchors", RealPlan.RestoreFile), rowsA);
        WriteRows(Path.Combine(corpus, "holdout", RealPlan.RestoreFile), rowsH);
        foreach (var (rows, split) in new[] { (rowsA, "anchors"), (rowsH, "holdout") })
        {
            foreach (var (lemma, sense, sentence, real) in rows)
            {
                provenance.Add(real is null ? Authored(lemma, sense, sentence, split) : Real(real, sense, split));
            }
        }

        WriteProvenance(Path.Combine(corpus, "provenance", "real-restore.tsv"), provenance);
        var newSenses = plan.Where(p => p.IsNew && added.Contains((p.Lemma, p.Sense)))
            .Select(p => new InventoryRow(p.Lemma, p.Sense, p.Tags, "real-restore.tsv", p.Group));
        SenseInventory.WriteFile(Path.Combine(corpus, "senses", "real-restore.tsv"), newSenses);
        UpdateDecisions(corpus, lemmas);
        report.AppendLine($"anchors {rowsA.Count} (real {rowsA.Count(r => r.Real is not null)}), holdout {rowsH.Count} (real {rowsH.Count(r => r.Real is not null)}), new senses {added.Count}.");
        return report.ToString();
    }

    /// <summary>
    ///     Chooses the real rows of one sense: holdout first, from contributors other than the anchors where possible.
    /// </summary>
    /// <param name="pool">The accepted candidates of the sense.</param>
    /// <param name="needAnchors">The anchors needed.</param>
    /// <param name="needHoldout">The holdout rows needed.</param>
    /// <returns>The anchor rows, the holdout rows and a note.</returns>
    public static (List<Candidate> Anchors, List<Candidate> Holdout, string Note) Allocate(IReadOnlyList<Candidate> pool, int needAnchors, int needHoldout)
    {
        var units = pool.GroupBy(c => c.Contributor, StringComparer.Ordinal)
            .Select(g => (Unit: g.Key, Rows: g.OrderBy(c => LicenceRank(c.Licence)).ThenBy(c => Hash(c.Id), StringComparer.Ordinal).ToList()))
            .OrderBy(u => u.Rows.Min(c => LicenceRank(c.Licence))).ThenBy(u => Hash(u.Unit), StringComparer.Ordinal).ToList();
        var (anchors, holdout) = (new List<Candidate>(), new List<Candidate>());
        var holdoutUnits = new HashSet<string>(StringComparer.Ordinal);

        // Holdout from the units with the fewest rows first, so that big contributors remain available for the anchors
        foreach (var unit in units.OrderBy(u => u.Rows.Count).ThenBy(u => Hash(u.Unit), StringComparer.Ordinal))
        {
            if (holdout.Count >= needHoldout)
            {
                break;
            }

            var take = unit.Rows.Take(needHoldout - holdout.Count).ToList();
            holdout.AddRange(take);
            holdoutUnits.Add(unit.Unit);
        }

        foreach (var unit in units.Where(u => !holdoutUnits.Contains(u.Unit)))
        {
            anchors.AddRange(unit.Rows.Take(needAnchors - anchors.Count));
            if (anchors.Count >= needAnchors)
            {
                break;
            }
        }

        var note = string.Empty;
        if (anchors.Count < needAnchors)
        {
            var left = units.Where(u => holdoutUnits.Contains(u.Unit)).SelectMany(u => u.Rows).Where(c => !holdout.Contains(c)).ToList();
            var fill = left.Take(needAnchors - anchors.Count).ToList();
            anchors.AddRange(fill);
            note = fill.Count > 0 ? $"same-source split for {fill.Count} anchors" : string.Empty;
        }

        return (anchors, holdout, note);
    }

    /// <summary>
    ///     Reads a provenance file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The rows.</returns>
    public static List<ProvenanceRow> ReadProvenance(string path)
    {
        var rows = new List<ProvenanceRow>();
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != ProvenanceHeader)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}:1: unexpected provenance header.");
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var c = lines[i].TrimEnd('\r').Split('\t');
            if (lines[i].Length == 0)
            {
                continue;
            }

            if (c.Length != 10)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{i + 1}: expected 10 columns but found {c.Length}.");
            }

            rows.Add(new ProvenanceRow(c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8], c[9]));
        }

        return rows;
    }

    /// <summary>
    ///     Writes a provenance file ordered by lemma, sense, split and id.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="rows">The rows.</param>
    public static void WriteProvenance(string path, IEnumerable<ProvenanceRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder(ProvenanceHeader).Append('\n');
        foreach (var r in rows.OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal)
                     .ThenBy(r => r.Split, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            text.Append($"{r.Id}\t{r.Lemma}\t{r.Sense}\t{r.Split}\t{r.Source}\t{r.Licence}\t{r.SourceKey}\t{r.Url}\t{r.Contributor}\t{r.Origin}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Validates the provenance sidecar against the real-sentence files of the corpus.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>One message per problem; empty when the corpus is clean.</returns>
    public static List<string> Validate(string corpus)
    {
        var errors = new List<string>();
        var directory = Path.Combine(corpus, "provenance");
        var provenance = new Dictionary<string, ProvenanceRow>(StringComparer.Ordinal);
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var row in ReadProvenance(file))
                {
                    if (!provenance.TryAdd(row.Id, row))
                    {
                        errors.Add($"provenance: id {row.Id} is listed twice.");
                    }

                    ValidateRow(row, errors);
                }
            }
        }

        var covered = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<(string Path, string Split)>
        {
            (Path.Combine(corpus, "anchors", RealPlan.RestoreFile), "anchors"),
            (Path.Combine(corpus, "holdout", RealPlan.RestoreFile), "holdout")
        };
        var realtext = Path.Combine(corpus, "realtext");
        if (Directory.Exists(realtext))
        {
            files.AddRange(Directory.GetFiles(realtext, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).Select(f => (f, "eval")));
        }

        foreach (var (path, split) in files.Where(f => File.Exists(f.Path)))
        {
            foreach (var row in Corpus.ReadSentences(path))
            {
                var id = PosAudit.RowId(row.Lemma, row.Sentence);
                covered.Add(id);
                if (!provenance.TryGetValue(id, out var p))
                {
                    errors.Add($"{Path.GetFileName(path)}: '{row.Sentence}' has no provenance row.");
                }
                else if (p.Lemma != row.Lemma || p.Sense != row.Sense || p.Split != split)
                {
                    errors.Add($"{Path.GetFileName(path)}: provenance of {id} disagrees on lemma, sense or split.");
                }

                var plain = row.Sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
                if (plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWords)
                {
                    errors.Add($"{Path.GetFileName(path)}: '{row.Sentence}' has more than {MaxWords} words.");
                }

                if (plain.Contains("gutenberg", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{Path.GetFileName(path)}: '{row.Sentence}' mentions Gutenberg.");
                }
            }
        }

        errors.AddRange(provenance.Keys.Where(k => !covered.Contains(k)).Select(k => $"provenance: row {k} matches no sentence."));
        return errors;
    }

    private static void ValidateRow(ProvenanceRow row, List<string> errors)
    {
        if (!Licences.Contains(row.Licence))
        {
            errors.Add($"provenance {row.Id}: licence '{row.Licence}' is not allowed.");
        }

        if (!Origins.Contains(row.Origin))
        {
            errors.Add($"provenance {row.Id}: origin '{row.Origin}' is not real, authored or llm-corpus.");
        }

        if (row.Licence == "CC BY 2.0 FR" && (row.Contributor.Length == 0 || row.Contributor == "-" || row.Contributor == "unknown"))
        {
            errors.Add($"provenance {row.Id}: a CC BY row needs a contributor.");
        }

        if (row.Source == "gutenberg" && (!row.SourceKey.StartsWith("pg", StringComparison.Ordinal) || row.Licence != "Public domain"))
        {
            errors.Add($"provenance {row.Id}: a Gutenberg row needs a pgN key and the Public domain licence.");
        }

        if (row.Licence == "web-excerpt" && !row.Url.StartsWith("http", StringComparison.Ordinal))
        {
            errors.Add($"provenance {row.Id}: a web excerpt needs its URL.");
        }
    }

    private static void WriteRows(string path, IEnumerable<(string Lemma, string Sense, string Sentence, Candidate? Real)> rows)
    {
        var text = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal).ThenBy(r => r.Sentence, StringComparer.Ordinal))
        {
            text.Append($"{r.Lemma}\t{r.Sense}\t{r.Sentence}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static void UpdateDecisions(string corpus, ISet<string> lemmas)
    {
        var path = Path.Combine(corpus, "audit", "relabel-decisions.tsv");
        var lines = File.ReadAllLines(path, new UTF8Encoding(false)).Select(l => l.TrimEnd('\r')).ToList();
        for (var i = 1; i < lines.Count; i++)
        {
            var c = lines[i].Split('\t');
            if (c.Length == 9 && lemmas.Contains(c[0]) && (c[4] == "drop" || c[4] == "exclude"))
            {
                c[4] = "restore";
                c[8] = "restored by the real-text pass: " + c[8];
                lines[i] = string.Join('\t', c);
            }
        }

        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    private static ProvenanceRow Authored(string lemma, string sense, string sentence, string split) =>
        new(PosAudit.RowId(lemma, sentence), lemma, sense, split, "llm-corpus", "MIT (project-authored)", "dropped-rows.tsv", "-", "project (LLM-written)", "llm-corpus");

    private static ProvenanceRow Real(Candidate c, string sense, string split)
    {
        var origin = c.Source == "authored" ? "authored" : "real";
        return new ProvenanceRow(c.Id, c.Lemma, sense, split, c.Source, c.Licence, c.SourceKey, c.Url, c.Contributor, origin);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 6);
}
