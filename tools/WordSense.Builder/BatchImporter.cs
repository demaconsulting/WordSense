using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The outcome of importing one batch.
/// </summary>
/// <param name="Batch">The batch number.</param>
/// <param name="Status">Imported, AlreadyImported, Missing or Invalid.</param>
/// <param name="Detail">Why the batch was not imported, or counts when it was.</param>
internal sealed record BatchResult(int Batch, string Status, string Detail);

/// <summary>
///     Imports completed sentence batches into the corpus after validating them.
/// </summary>
/// <remarks>
///     A batch is a pair of files, <c>anchors/batchN.tsv</c> and <c>holdout/batchN.tsv</c>, plus an
///     optional specification <c>batches/batchN.tsv</c> (<c>lemma</c> and comma-separated <c>labels</c>).
///     A batch is imported only when it is complete and valid; sentences are copied unchanged.
/// </remarks>
internal static class BatchImporter
{
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
        try
        {
            var anchors = Corpus.ReadSentences(anchorSource);
            var holdout = Corpus.ReadSentences(holdoutSource);
            if (File.Exists(anchorTarget) && File.Exists(holdoutTarget))
            {
                return new BatchResult(batch, "AlreadyImported", $"{target} exists");
            }

            // Validate against the specification and the rest of the corpus before writing anything
            ValidateAgainstSpec(source, name, anchors, holdout, knownLemmas);
            var otherAnchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
            var otherHoldout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
            Corpus.EnsureDisjoint(otherAnchors.Concat(anchors), otherHoldout.Concat(holdout));

            WriteRows(anchorTarget, anchors);
            WriteRows(holdoutTarget, holdout);
            return new BatchResult(batch, "Imported", $"{anchors.Count} anchors, {holdout.Count} holdout");
        }
        catch (InvalidDataException ex)
        {
            return new BatchResult(batch, "Invalid", ex.Message);
        }
    }

    /// <summary>
    ///     Checks that a batch covers its specified lemmas and uses only specified senses.
    /// </summary>
    /// <param name="source">The source directory.</param>
    /// <param name="name">The batch file name.</param>
    /// <param name="anchors">The batch anchors.</param>
    /// <param name="holdout">The batch holdout.</param>
    /// <param name="knownLemmas">Lemmas present in the forms file.</param>
    /// <exception cref="InvalidDataException">Thrown when the batch is incomplete or inconsistent.</exception>
    private static void ValidateAgainstSpec(string source, string name, List<CorpusRow> anchors, List<CorpusRow> holdout, HashSet<string> knownLemmas)
    {
        var unknown = anchors.Concat(holdout).Select(r => r.Lemma).Where(l => !knownLemmas.Contains(l)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidDataException($"Lemmas missing from the forms file: {string.Join(", ", unknown)}");
        }

        var specPath = Path.Combine(source, "batches", name);
        if (!File.Exists(specPath))
        {
            return;
        }

        var spec = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(specPath, new UTF8Encoding(false)).Skip(1).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
        {
            var columns = line.Split('\t');
            spec[columns[0].Trim()] = columns.Length > 1
                ? columns[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
                : [];
        }

        foreach (var (set, rows) in new[] { ("anchors", anchors), ("holdout", holdout) })
        {
            var missing = spec.Keys.Where(l => rows.All(r => r.Lemma != l)).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidDataException($"{set} lack the specified lemmas: {string.Join(", ", missing)}");
            }

            var stray = rows.Where(r => !spec.TryGetValue(r.Lemma, out var labels) || (labels.Count > 0 && !labels.Contains(r.Sense))).Select(r => $"{r.Lemma}/{r.Sense}").Distinct(StringComparer.Ordinal).ToList();
            if (stray.Count > 0)
            {
                throw new InvalidDataException($"{set} use lemmas or senses outside the specification: {string.Join(", ", stray)}");
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
