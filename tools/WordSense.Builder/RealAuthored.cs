using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Imports independently authored anchor and holdout sentences of the new-lemma flow and records their provenance.
/// </summary>
/// <remarks>
///     A batch is named <c>new-authored-N</c> and writes <c>anchors/NAME.tsv</c>, <c>holdout/NAME.tsv</c>,
///     <c>provenance/NAME.tsv</c> and, only for planned senses that no other file declares, <c>senses/NAME.tsv</c>.
///     Anchors and holdout come from two separate input files written by two different authors; the provenance records
///     the author role as contributor. All checks run before anything is written. The real-sentence import ignores
///     <c>new-authored-*.tsv</c> files when it counts the existing rows, so it can be repeated after an authored batch.
/// </remarks>
internal static class RealAuthored
{
    /// <summary>
    ///     The prefix of an authored batch name.
    /// </summary>
    public const string Prefix = "new-authored-";

    /// <summary>
    ///     The contributor recorded for the anchor author.
    /// </summary>
    public const string AnchorContributor = "authored-anchor";

    /// <summary>
    ///     The contributor recorded for the holdout author.
    /// </summary>
    public const string HoldoutContributor = "authored-holdout";

    private const string Licence = "MIT (project-authored)";

    /// <summary>
    ///     Checks whether a file name belongs to an authored batch.
    /// </summary>
    /// <param name="fileName">The file name, with extension.</param>
    /// <returns>True for <c>new-authored-*.tsv</c>.</returns>
    public static bool IsAuthoredFile(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.Ordinal) && fileName.EndsWith(".tsv", StringComparison.Ordinal);

    /// <summary>
    ///     Checks a batch name.
    /// </summary>
    /// <param name="name">The batch name, for example <c>new-authored-1</c>.</param>
    /// <exception cref="ArgumentException">Thrown when the name does not have the form <c>new-authored-N</c>.</exception>
    public static void ValidateName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length || !name[Prefix.Length..].All(char.IsAsciiDigit))
        {
            throw new ArgumentException($"The authored batch name must have the form {Prefix}N but was '{name}'.");
        }
    }

    /// <summary>
    ///     Imports the authored sentences.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="name">The batch name, for example <c>new-authored-1</c>.</param>
    /// <param name="anchorsFile">The sentence file of the anchor author.</param>
    /// <param name="holdoutFile">The sentence file of the holdout author.</param>
    /// <returns>A report with the per-sense counts against the minima.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found; nothing is written then.</exception>
    public static string Import(string corpus, string name, string anchorsFile, string holdoutFile)
    {
        ValidateName(name);
        var errors = new List<string>();
        var plan = RealPlan.ReadPlan(corpus);
        var ownFile = name + ".tsv";
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"), ownFile);
        var forms = ReadForms(corpus);

        var (anchors, anchorErrors) = Corpus.ReadSentencesLenient(anchorsFile);
        var (holdout, holdoutErrors) = Corpus.ReadSentencesLenient(holdoutFile);
        errors.AddRange(anchorErrors);
        errors.AddRange(holdoutErrors);

        // Sentences that exist in other files; authored sentences must not repeat them, so the splits stay disjoint
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        var existing = new Dictionary<(string, string, string), int>();
        foreach (var split in new[] { "anchors", "holdout" })
        {
            var folder = Path.Combine(corpus, split);
            foreach (var file in Directory.GetFiles(folder, "*.tsv").Where(f => Path.GetFileName(f) != ownFile).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var row in Corpus.ReadSentences(file))
                {
                    taken.TryAdd(Key(row.Sentence), $"{split}/{Path.GetFileName(file)}");
                    var key = (row.Lemma, row.Sense, split);
                    existing[key] = existing.GetValueOrDefault(key) + 1;
                }
            }
        }

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var authoredAnchorSenses = anchors.Select(r => (r.Lemma, r.Sense)).ToHashSet();
        foreach (var (split, rows, file) in new[] { ("anchors", anchors, anchorsFile), ("holdout", holdout, holdoutFile) })
        {
            foreach (var row in rows)
            {
                var label = $"{Path.GetFileName(file)}: '{row.Sentence}'";
                if (!plan.Any(p => p.Lemma == row.Lemma && p.Sense == row.Sense))
                {
                    errors.Add($"{label}: {row.Lemma}/{row.Sense} is not a sense of the restore plan.");
                }
                else if (!inventory.Contains(row.Lemma, row.Sense) && !authoredAnchorSenses.Contains((row.Lemma, row.Sense)))
                {
                    errors.Add($"{label}: {row.Lemma}/{row.Sense} is not in the inventory and has no authored anchors to declare it.");
                }

                var target = row.Words[row.TargetWordIndex].ToLowerInvariant();
                if (!forms.Contains((target, row.Lemma)))
                {
                    errors.Add($"{label}: the target '{target}' is not a form of '{row.Lemma}' in the forms files.");
                }

                var plain = row.Sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
                if (plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > RealImport.MaxWords)
                {
                    errors.Add($"{label}: more than {RealImport.MaxWords} words.");
                }

                var key = Key(row.Sentence);
                if (taken.TryGetValue(key, out var where))
                {
                    errors.Add($"{label}: duplicate of a sentence in {where}.");
                }
                else if (!seen.TryAdd(key, $"{split}/{Path.GetFileName(file)}"))
                {
                    errors.Add($"{label}: duplicate of a sentence in {seen[key]}" + (seen[key].StartsWith(split, StringComparison.Ordinal) ? "." : " (anchors and holdout must stay disjoint)."));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var report = new StringBuilder("lemma\tsense\tauthored a/h\ttotal a/h\tnote\n");
        foreach (var p in plan.Where(p => anchors.Concat(holdout).Any(r => r.Lemma == p.Lemma && r.Sense == p.Sense)))
        {
            var (na, nh) = (anchors.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense), holdout.Count(r => r.Lemma == p.Lemma && r.Sense == p.Sense));
            var (ta, th) = (na + existing.GetValueOrDefault((p.Lemma, p.Sense, "anchors")), nh + existing.GetValueOrDefault((p.Lemma, p.Sense, "holdout")));
            var note = ta < RealPlan.MinAnchors || th < RealPlan.MinHoldout ? $"LACKING (anchors {ta}/{RealPlan.MinAnchors}, holdout {th}/{RealPlan.MinHoldout})" : string.Empty;
            report.AppendLine($"{p.Lemma}\t{p.Sense}\t{na}/{nh}\t{ta}/{th}\t{note}");
        }

        WriteRows(Path.Combine(corpus, "anchors", ownFile), anchors);
        WriteRows(Path.Combine(corpus, "holdout", ownFile), holdout);
        var provenance = anchors.Select(r => ToProvenance(r, "anchors", name, AnchorContributor))
            .Concat(holdout.Select(r => ToProvenance(r, "holdout", name, HoldoutContributor)));
        RealImport.WriteProvenance(Path.Combine(corpus, "provenance", ownFile), provenance);

        // A sense is declared once: only the planned senses that no other file declares are written here
        var declared = plan.Where(p => !inventory.Contains(p.Lemma, p.Sense) && authoredAnchorSenses.Contains((p.Lemma, p.Sense)))
            .Select(p => new InventoryRow(p.Lemma, p.Sense, p.Tags, ownFile, p.Group)).ToList();
        var sensesPath = Path.Combine(corpus, "senses", ownFile);
        if (declared.Count > 0)
        {
            SenseInventory.WriteFile(sensesPath, declared);
        }
        else if (File.Exists(sensesPath))
        {
            File.Delete(sensesPath);
        }

        report.AppendLine($"anchors {anchors.Count}, holdout {holdout.Count}, new senses {declared.Count}.");
        return report.ToString();
    }

    private static HashSet<(string Form, string Lemma)> ReadForms(string corpus)
    {
        var forms = new HashSet<(string, string)>();
        var directory = Path.Combine(corpus, "forms");
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                forms.UnionWith(Corpus.ReadForms(file).Select(f => (f.Form, f.Lemma)));
            }
        }

        return forms;
    }

    private static string Key(string sentence) => sentence.ToLowerInvariant();

    private static void WriteRows(string path, IEnumerable<CorpusRow> rows)
    {
        var text = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal).ThenBy(r => r.Sentence, StringComparer.Ordinal))
        {
            text.Append($"{r.Lemma}\t{r.Sense}\t{r.Sentence}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static ProvenanceRow ToProvenance(CorpusRow row, string split, string name, string contributor) =>
        new(PosAudit.RowId(row.Lemma, row.Sentence), row.Lemma, row.Sense, split, "authored", Licence, name, "-", contributor, "authored");
}
