using System.Globalization;
using System.Xml;

namespace WordSense.Builder.Data;

/// <summary>
///     The parts of the OEWN importer that read a release without importing it: the written forms of the lexical entries
///     and the identifier map of an earlier release.
/// </summary>
internal static partial class OewnImporter
{
    /// <summary>
    ///     The outcome of the identifier map import.
    /// </summary>
    /// <param name="SenseRows">Rows written for sense identifiers that changed.</param>
    /// <param name="EntryRows">Rows written for entry identifiers that changed.</param>
    /// <param name="Unchanged">Sense and entry identifiers of the earlier release that the data holds unchanged.</param>
    /// <param name="Removed">Sense and entry identifiers of the earlier release that the data no longer has and that map to nothing.</param>
    /// <param name="Ambiguous">Entry identifiers of the earlier release whose successor is not unique.</param>
    internal sealed record IdMapResult(int SenseRows, int EntryRows, int Unchanged, int Removed, int Ambiguous);

    /// <summary>
    ///     Reads the written forms (<c>Form</c> elements) of every lexical entry of an OEWN file.
    /// </summary>
    /// <param name="input">The LMF file, gzip-compressed or plain XML.</param>
    /// <param name="expectedSha256">The SHA-256 the file must have, or null to skip the check.</param>
    /// <returns>The written forms by entry identifier, in file order; entries without a form are absent.</returns>
    /// <exception cref="InvalidDataException">Thrown when the hash does not match or the XML is malformed.</exception>
    public static Dictionary<string, List<string>> ReadForms(string input, string? expectedSha256 = PinnedSha256)
    {
        CheckHash(input, expectedSha256);
        var cleaner = new Cleaner();
        var forms = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in ReadChecked(input).Entries.Where(e => e.Forms.Count > 0))
        {
            forms[entry.Id] = entry.Forms.Select(cleaner.Clean).ToList();
        }

        return forms;
    }

    /// <summary>
    ///     Writes the <c>id_map</c> table from the identifiers of an earlier release to the identifiers of the data.
    /// </summary>
    /// <param name="previous">The LMF file of the earlier release.</param>
    /// <param name="dataDirectory">The data directory with the current tables; its <c>id_map</c> is replaced.</param>
    /// <param name="release">The release name written to the table.</param>
    /// <param name="expectedSha256">The SHA-256 the earlier file must have, or null to skip the check.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the hash does not match or the XML is malformed.</exception>
    /// <remarks>
    ///     A sense identifier that the data still has is unchanged and gets no row. A changed sense identifier maps by its
    ///     sense key when the data has exactly one sense with that key. An entry identifier that the data still has gets no
    ///     row; otherwise it maps to the entry of the majority of its mapped senses, or to the only entry with its lemma and
    ///     part of speech. An identifier without a unique successor gets no row.
    /// </remarks>
    public static IdMapResult ImportPrevious(string previous, string dataDirectory, string release = PreviousRelease, string? expectedSha256 = PreviousPinnedSha256)
    {
        CheckHash(previous, expectedSha256);
        var schema = DataSchema.Load(Path.Combine(dataDirectory, "schema.yaml"));
        var currentSenses = DataTables.Read(dataDirectory, schema.Table("sense")).ToList();
        var currentEntries = DataTables.Read(dataDirectory, schema.Table("entry")).ToList();
        var senseIds = currentSenses.Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        var entryIds = currentEntries.Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in currentSenses.Where(r => r[1].Length > 0))
        {
            byKey[row[1]] = row[0];
        }

        var senseEntry = currentSenses.ToDictionary(r => r[0], r => r[2], StringComparer.Ordinal);
        var byLemma = currentEntries
            .Where(r => r[0].StartsWith("oewn-", StringComparison.Ordinal))
            .GroupBy(r => (r[1], r[2]))
            .ToDictionary(g => g.Key, g => g.Select(r => r[0]).ToList());

        var cleaner = new Cleaner();
        var rows = new List<string[]>();
        var unchanged = 0;
        var removed = 0;
        var ambiguous = 0;
        var senseRows = 0;
        var entryRows = 0;
        foreach (var entry in ReadChecked(previous).Entries)
        {
            var lemma = cleaner.Clean(entry.Lemma);
            var votes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var senseId in entry.Senses.Select(s => s.Id))
            {
                if (senseIds.Contains(senseId))
                {
                    unchanged++;
                    votes[senseEntry[senseId]] = votes.GetValueOrDefault(senseEntry[senseId]) + 1;
                    continue;
                }

                string? key;
                try
                {
                    key = SenseKey(senseId, lemma);
                }
                catch (InvalidDataException)
                {
                    key = null;
                }

                if (key is not null && byKey.TryGetValue(key, out var target))
                {
                    rows.Add([senseId, target, release]);
                    senseRows++;
                    votes[senseEntry[target]] = votes.GetValueOrDefault(senseEntry[target]) + 1;
                }
                else
                {
                    removed++;
                }
            }

            if (entryIds.Contains(entry.Id))
            {
                unchanged++;
                continue;
            }

            var winner = Majority(votes);
            if (winner is null && votes.Count == 0 && byLemma.TryGetValue((lemma, MapEntryPos(entry.Pos)), out var candidates) && candidates.Count == 1)
            {
                winner = candidates[0];
            }

            if (winner is not null)
            {
                rows.Add([entry.Id, winner, release]);
                entryRows++;
            }
            else if (votes.Count == 0)
            {
                removed++;
            }
            else
            {
                ambiguous++;
            }
        }

        DataTables.Write(dataDirectory, schema.Table("id_map"), rows, new PartitionResolver(schema.FileSplit));
        return new IdMapResult(senseRows, entryRows, unchanged, removed, ambiguous);
    }

    // The key with the most votes, or null when there is none or the best count is shared
    private static string? Majority(Dictionary<string, int> votes)
    {
        if (votes.Count == 0)
        {
            return null;
        }

        var best = votes.Values.Max();
        var winners = votes.Where(v => v.Value == best).Select(v => v.Key).ToList();
        return winners.Count == 1 ? winners[0] : null;
    }

    private static OewnDocument ReadChecked(string input)
    {
        try
        {
            return ReadDocument(input);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{input} is not well-formed XML: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Formats the counts of an identifier map import.
    /// </summary>
    /// <param name="result">The counts.</param>
    /// <returns>A summary line.</returns>
    public static string Describe(IdMapResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"id_map: {result.SenseRows} sense rows, {result.EntryRows} entry rows, {result.Unchanged} unchanged identifiers, {result.Removed} removed, {result.Ambiguous} ambiguous.");
}
