using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The outcome of importing one batch.
/// </summary>
/// <param name="Batch">The batch number.</param>
/// <param name="Status">Imported, AlreadyImported, Missing or Invalid.</param>
/// <param name="Detail">Why the batch was not imported, or counts when it was.</param>
/// <param name="Notes">Warnings, dropped rows and excluded lemmas, one line each.</param>
internal sealed record BatchResult(int Batch, string Status, string Detail, IReadOnlyList<string>? Notes = null);

/// <summary>
///     Imports completed sentence batches into the corpus after validating them.
/// </summary>
/// <remarks>
///     A batch is a pair of files, <c>anchors/batchN.tsv</c> and <c>holdout/batchN.tsv</c>, plus an
///     optional specification <c>batches/batchN.tsv</c> (<c>lemma</c> and comma-separated <c>labels</c>).
///     The import also writes the batch's sense inventory <c>senses/speech-batchN.tsv</c>, in which the part of
///     speech of every sense is derived from its label; a label that is not a part of speech makes the batch invalid.
///     Lemma-level absence (a specified lemma without sentences) is a warning: the lemma is left out.
///     Row-level errors (bad format, unknown lemma, disallowed label, duplicate sentence, anchor/holdout
///     overlap) drop only the offending rows, provided they are few (at most <see cref="MaxDroppedShare"/>
///     of the batch); otherwise the batch is rejected. A lemma left with fewer than
///     <see cref="MinSenses"/> senses, or a sense with fewer than <see cref="MinAnchorsPerSense"/> anchors,
///     is excluded entirely. Sentences are copied unchanged and never created.
/// </remarks>
internal static class BatchImporter
{
    /// <summary>The largest share of a batch's rows that may be dropped for row-level errors.</summary>
    public const double MaxDroppedShare = 0.05;

    /// <summary>The minimum number of senses a lemma must have in the anchors.</summary>
    public const int MinSenses = 2;

    /// <summary>The minimum number of anchors for every represented sense.</summary>
    public const int MinAnchorsPerSense = 4;

    /// <summary>
    ///     Imports every complete batch found in a source directory.
    /// </summary>
    /// <param name="sourceDirectory">The directory with <c>anchors</c>, <c>holdout</c> and <c>batches</c> folders.</param>
    /// <param name="corpusDirectory">The corpus directory (<c>corpus/</c>).</param>
    /// <param name="maxBatch">The highest batch number to look for.</param>
    /// <returns>One result per batch number.</returns>
    public static List<BatchResult> Import(string sourceDirectory, string corpusDirectory, int maxBatch)
    {
        var results = new List<BatchResult>();
        var knownLemmas = Corpus.ReadForms(Path.Combine(corpusDirectory, "forms", "speech.tsv")).Select(f => f.Lemma).ToHashSet(StringComparer.Ordinal);
        for (var batch = 1; batch <= maxBatch; batch++)
        {
            results.Add(ImportOne(batch, sourceDirectory, corpusDirectory, knownLemmas));
        }

        return results;
    }

    /// <summary>
    ///     Imports one batch.
    /// </summary>
    /// <param name="batch">The batch number.</param>
    /// <param name="source">The source directory.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="knownLemmas">Lemmas present in the forms file.</param>
    /// <returns>The result.</returns>
    private static BatchResult ImportOne(int batch, string source, string corpus, HashSet<string> knownLemmas)
    {
        var name = $"batch{batch}.tsv";
        var anchorSource = Path.Combine(source, "anchors", name);
        var holdoutSource = Path.Combine(source, "holdout", name);
        if (!File.Exists(anchorSource) || !File.Exists(holdoutSource))
        {
            return new BatchResult(batch, "Missing", $"anchors present: {File.Exists(anchorSource)}, holdout present: {File.Exists(holdoutSource)}");
        }

        var target = $"speech-batch{batch}.tsv";
        var anchorTarget = Path.Combine(corpus, "anchors", target);
        var holdoutTarget = Path.Combine(corpus, "holdout", target);
        if (File.Exists(anchorTarget) && File.Exists(holdoutTarget))
        {
            return new BatchResult(batch, "AlreadyImported", $"{target} exists");
        }

        try
        {
            var notes = new List<string>();
            var (anchors, anchorErrors) = Corpus.ReadSentencesLenient(anchorSource);
            var (holdout, holdoutErrors) = Corpus.ReadSentencesLenient(holdoutSource);
            var total = anchors.Count + holdout.Count + anchorErrors.Count + holdoutErrors.Count;
            var dropped = anchorErrors.Count + holdoutErrors.Count;
            notes.AddRange(anchorErrors.Concat(holdoutErrors).Select(e => "dropped row: " + e));

            var spec = ReadSpec(Path.Combine(source, "batches", name));
            var existing = Corpus.ReadDirectory(Path.Combine(corpus, "anchors")).Concat(Corpus.ReadDirectory(Path.Combine(corpus, "holdout")))
                .Select(r => r.Sentence).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(existing, StringComparer.Ordinal);
            anchors = FilterRows("anchors", anchors, knownLemmas, spec, seen, notes, ref dropped);
            holdout = FilterRows("holdout", holdout, knownLemmas, spec, seen, notes, ref dropped);
            if (dropped > MaxDroppedShare * Math.Max(1, total))
            {
                return new BatchResult(batch, "Invalid", $"{dropped} of {total} rows have errors (more than {MaxDroppedShare:P0}); nothing imported", notes);
            }

            ApplyLemmaRules(anchors, holdout, spec, notes, out var excluded);
            anchors = anchors.Where(r => !excluded.Contains(r.Lemma)).ToList();
            holdout = holdout.Where(r => !excluded.Contains(r.Lemma)).ToList();
            Corpus.EnsureDisjoint(anchors, holdout);

            // Derive the inventory first so an unmapped label rejects the batch before anything is written
            var inventory = DeriveInventory(anchors);
            WriteRows(anchorTarget, anchors);
            WriteRows(holdoutTarget, holdout);
            SenseInventory.WriteFile(Path.Combine(corpus, "senses", target), inventory.Values.SelectMany(v => v));
            var lemmaCount = anchors.Select(r => r.Lemma).Distinct(StringComparer.Ordinal).Count();
            return new BatchResult(batch, "Imported", $"{anchors.Count} anchors, {holdout.Count} holdout, {lemmaCount} lemmas, {dropped} rows dropped, {excluded.Count} lemmas excluded", notes);
        }
        catch (InvalidDataException ex)
        {
            return new BatchResult(batch, "Invalid", ex.Message);
        }
    }

    /// <summary>
    ///     Derives the inventory rows of a batch from its anchors. Only part-of-speech labels
    ///     (noun, verb, adj, adv) can be imported; any other sense needs an authored inventory entry.
    /// </summary>
    /// <param name="anchors">The anchor rows of the batch.</param>
    /// <returns>The inventory rows by source file name.</returns>
    /// <exception cref="InvalidDataException">Thrown for a label that is not a part of speech.</exception>
    private static SortedDictionary<string, List<InventoryRow>> DeriveInventory(IEnumerable<CorpusRow> anchors)
    {
        var files = new SortedDictionary<string, List<InventoryRow>>(StringComparer.Ordinal);
        var emitted = new HashSet<(string, string)>();
        foreach (var row in anchors.Where(r => emitted.Add((r.Lemma, r.Sense))))
        {
            if (InventoryRow.PosOfTag(row.Sense) is not { } pos)
            {
                throw new InvalidDataException(
                    $"Sense label '{row.Sense}' (lemma '{row.Lemma}') is not a part of speech; add the sense to the sense inventory by hand with a part of speech and meaning.");
            }

            if (!files.TryGetValue(row.Source, out var list))
            {
                files[row.Source] = list = [];
            }

            // Imported lemmas are not heteronyms: no meaning, as in NounVerbImport
            list.Add(new InventoryRow(row.Lemma, row.Sense, pos, null, null, row.Source));
        }

        foreach (var list in files.Values)
        {
            list.Sort((a, b) => a.Lemma == b.Lemma ? string.CompareOrdinal(a.Label, b.Label) : string.CompareOrdinal(a.Lemma, b.Lemma));
        }

        return files;
    }

    /// <summary>
    ///     Reads a batch specification; a missing file gives <see langword="null"/>.
    /// </summary>
    /// <param name="path">The specification file.</param>
    /// <returns>The allowed labels by lemma (empty means any label), or <see langword="null"/>.</returns>
    private static Dictionary<string, HashSet<string>>? ReadSpec(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var spec = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
        {
            var columns = line.Split('\t');
            spec[columns[0].Trim()] = columns.Length > 1
                ? columns[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
                : [];
        }

        return spec;
    }

    /// <summary>
    ///     Drops rows with a row-level error: unknown lemma, disallowed label or duplicate sentence.
    /// </summary>
    /// <param name="set">The set name for messages.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="knownLemmas">Lemmas present in the forms file.</param>
    /// <param name="spec">The batch specification, if any.</param>
    /// <param name="seen">Sentences already seen (existing corpus and earlier rows); updated.</param>
    /// <param name="notes">Receives one line per dropped row.</param>
    /// <param name="dropped">The running count of dropped rows.</param>
    /// <returns>The remaining rows.</returns>
    private static List<CorpusRow> FilterRows(
        string set, List<CorpusRow> rows, HashSet<string> knownLemmas, Dictionary<string, HashSet<string>>? spec, HashSet<string> seen, List<string> notes, ref int dropped)
    {
        var kept = new List<CorpusRow>();
        foreach (var row in rows)
        {
            string? reason = null;
            if (!knownLemmas.Contains(row.Lemma) || (spec is not null && !spec.ContainsKey(row.Lemma)))
            {
                reason = $"unknown lemma '{row.Lemma}'";
            }
            else if (spec is not null && spec[row.Lemma].Count > 0 && !spec[row.Lemma].Contains(row.Sense))
            {
                reason = $"disallowed label '{row.Lemma}/{row.Sense}'";
            }
            else if (!seen.Add(row.Sentence))
            {
                reason = set == "holdout" ? "duplicate sentence (or overlap with anchors)" : "duplicate sentence";
            }

            if (reason is null)
            {
                kept.Add(row);
            }
            else
            {
                dropped++;
                notes.Add($"dropped {set} row ({reason}): {row.Lemma}\t{row.Sense}\t{row.Sentence}");
            }
        }

        return kept;
    }

    /// <summary>
    ///     Applies the lemma-level rules: warnings for absence, exclusion for thin anchors.
    /// </summary>
    /// <param name="anchors">The remaining anchors.</param>
    /// <param name="holdout">The remaining holdout.</param>
    /// <param name="spec">The batch specification, if any.</param>
    /// <param name="notes">Receives warnings.</param>
    /// <param name="excluded">The lemmas to leave out of the import.</param>
    private static void ApplyLemmaRules(
        List<CorpusRow> anchors, List<CorpusRow> holdout, Dictionary<string, HashSet<string>>? spec, List<string> notes, out HashSet<string> excluded)
    {
        excluded = new HashSet<string>(StringComparer.Ordinal);
        var anchorLemmas = anchors.Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        var holdoutLemmas = holdout.Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        if (spec is not null)
        {
            var noAnchors = spec.Keys.Where(l => !anchorLemmas.Contains(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();
            var noHoldout = spec.Keys.Where(l => anchorLemmas.Contains(l) && !holdoutLemmas.Contains(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();
            if (noAnchors.Count > 0)
            {
                notes.Add($"WARNING lemmas without anchor sentences (no classifier): {string.Join(", ", noAnchors)}");
            }

            if (noHoldout.Count > 0)
            {
                notes.Add($"WARNING lemmas without holdout sentences (trained, not evaluated): {string.Join(", ", noHoldout)}");
            }
        }

        foreach (var lemma in holdoutLemmas.Where(l => !anchorLemmas.Contains(l)).OrderBy(l => l, StringComparer.Ordinal))
        {
            excluded.Add(lemma);
            notes.Add($"EXCLUDED lemma '{lemma}': holdout sentences but no anchors.");
        }

        foreach (var group in anchors.GroupBy(r => r.Lemma, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var perSense = group.GroupBy(r => r.Sense, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (perSense.Count < MinSenses)
            {
                excluded.Add(group.Key);
                notes.Add($"EXCLUDED lemma '{group.Key}': anchors represent {perSense.Count} sense(s), need at least {MinSenses}.");
            }
            else if (perSense.Values.Min() < MinAnchorsPerSense)
            {
                excluded.Add(group.Key);
                var thin = string.Join(", ", perSense.Where(p => p.Value < MinAnchorsPerSense).Select(p => $"{p.Key}={p.Value}"));
                notes.Add($"EXCLUDED lemma '{group.Key}': fewer than {MinAnchorsPerSense} anchors for a sense ({thin}).");
            }
        }
    }

    /// <summary>
    ///     Writes rows as UTF-8 without a byte-order mark and with LF line endings.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="rows">The rows.</param>
    private static void WriteRows(string path, List<CorpusRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(row.Lemma).Append('\t').Append(row.Sense).Append('\t').Append(row.Sentence).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
