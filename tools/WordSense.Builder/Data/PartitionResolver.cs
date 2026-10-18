namespace WordSense.Builder.Data;

/// <summary>
///     Computes the file key of a row of any table from the entry, sense, synset and class tables, so that the
///     importer places rows and the validator checks placement with the same rule.
/// </summary>
/// <remarks>
///     <para>
///     Child tables use the key of their parent so a change to one lemma stays in one file: an entry by its lemma, a
///     sense by its entry, a synset by the smallest lower-case lemma among its senses (ties by sense id, both by UTF-8
///     bytes), and a coarse class by its own lemma. Parents must be added before children (entries, then senses,
///     then classes). Not thread-safe while filling; read-only afterwards.
///     </para>
/// </remarks>
internal sealed class PartitionResolver
{
    private readonly IReadOnlyCollection<char> _split;
    private readonly Dictionary<string, string> _entryLemma = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _senseEntry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Lemma, string SenseId)> _synsetFirst = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _classLemma = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="PartitionResolver"/> class.
    /// </summary>
    /// <param name="splitLetters">The split plan.</param>
    public PartitionResolver(IReadOnlyCollection<char> splitLetters)
    {
        _split = splitLetters;
    }

    /// <summary>
    ///     Registers an entry.
    /// </summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="lemma">Its lemma.</param>
    public void AddEntry(string entryId, string lemma) => _entryLemma[entryId] = lemma;

    /// <summary>
    ///     Registers a sense; its entry must have been added.
    /// </summary>
    /// <param name="senseId">The sense id.</param>
    /// <param name="entryId">The entry it belongs to.</param>
    /// <param name="synsetId">The synset it belongs to.</param>
    public void AddSense(string senseId, string entryId, string synsetId)
    {
        _senseEntry[senseId] = entryId;
        if (!_entryLemma.TryGetValue(entryId, out var lemma))
        {
            return;
        }

        var folded = DataLayout.AsciiLower(lemma);
        if (!_synsetFirst.TryGetValue(synsetId, out var best) || IsBefore(folded, senseId, best))
        {
            _synsetFirst[synsetId] = (folded, senseId);
        }
    }

    /// <summary>
    ///     Registers a coarse class.
    /// </summary>
    /// <param name="classId">The class id.</param>
    /// <param name="lemma">The lemma of the class.</param>
    public void AddClass(string classId, string lemma) => _classLemma[classId] = lemma;

    /// <summary>
    ///     Gets the file key of a synset.
    /// </summary>
    /// <param name="synsetId">The synset id.</param>
    /// <returns>The key, or null when the synset has no sense.</returns>
    public string? SynsetKey(string synsetId) =>
        _synsetFirst.TryGetValue(synsetId, out var first) ? DataLayout.FileFor(first.Lemma, _split) : null;

    /// <summary>
    ///     Gets the file key of a row.
    /// </summary>
    /// <param name="table">The table of the row.</param>
    /// <param name="row">The cells of the row.</param>
    /// <returns>The key, or null when the row refers to a parent that does not exist (reported as a foreign key problem).</returns>
    public string? KeyFor(TableDef table, string[] row)
    {
        if (table.Partition == PartitionKind.None)
        {
            return string.Empty;
        }

        var value = row[table.IndexOf(table.PartitionColumn!)];
        var lemma = table.Partition switch
        {
            PartitionKind.Lemma => value,
            PartitionKind.Entry => Lookup(_entryLemma, value),
            PartitionKind.Sense => _senseEntry.TryGetValue(value, out var entry) ? Lookup(_entryLemma, entry) : null,
            PartitionKind.Class => Lookup(_classLemma, value),
            _ => null,
        };
        if (table.Partition == PartitionKind.Synset)
        {
            return SynsetKey(value);
        }

        return lemma is null ? null : DataLayout.FileFor(lemma, _split);
    }

    private static string? Lookup(Dictionary<string, string> map, string key) => map.GetValueOrDefault(key);

    private static bool IsBefore(string lemma, string senseId, (string Lemma, string SenseId) best)
    {
        var byLemma = DataLayout.CompareUtf8(lemma, best.Lemma);
        return byLemma != 0 ? byLemma < 0 : DataLayout.CompareUtf8(senseId, best.SenseId) < 0;
    }
}
