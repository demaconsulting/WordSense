using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     One sense as the label generator sees it: the sense, its entry and its synset.
/// </summary>
/// <param name="SenseId">The sense id.</param>
/// <param name="SenseKey">The sense key, empty for a project sense.</param>
/// <param name="Lemma">The lemma of the entry of the sense.</param>
/// <param name="Pos">The part of speech letter of the entry (a letter of the <c>pos</c> vocabulary).</param>
/// <param name="TokenCount">The number of tokens of the lemma.</param>
/// <param name="SynsetId">The synset of the sense.</param>
internal sealed record LabelSense(string SenseId, string SenseKey, string Lemma, string Pos, int TokenCount, string SynsetId)
{
    /// <summary>
    ///     Gets a value indicating whether the sense belongs to the inventory: a sense of a lemma of one token, or a
    ///     project sense (a sense without a sense key).
    /// </summary>
    public bool InInventory => TokenCount == 1 || SenseKey.Length == 0;
}

/// <summary>
///     The tables the label generator reads, already loaded.
/// </summary>
/// <param name="Senses">Every sense of the data.</param>
/// <param name="Hypernyms">The hypernym synsets of a synset.</param>
/// <param name="Labels">The existing rows of <c>sense_label</c> (sense id, label, origin).</param>
/// <param name="RetiredIds">The old ids of <c>sense_label_map</c> (<c>lemma#label</c>).</param>
/// <param name="Counts">The WordNet 3.0 tag count by sense key; a sense without a count has the count 0.</param>
/// <param name="PosNames">The part of speech names by letter.</param>
internal sealed record LabelInputs(
    IReadOnlyList<LabelSense> Senses,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Hypernyms,
    IReadOnlyList<string[]> Labels,
    IReadOnlyCollection<string> RetiredIds,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, string> PosNames);

/// <summary>
///     The outcome of the label generation.
/// </summary>
/// <param name="Rows">The new <c>sense_label</c> rows (sense id, label, origin), in no particular order.</param>
/// <param name="Suffixed">The labels that needed a numeric suffix.</param>
/// <param name="Fallbacks">The labels that fell back to the part of speech name although the lemma and part of speech has several senses.</param>
internal sealed record LabelOutput(IReadOnlyList<string[]> Rows, int Suffixed, int Fallbacks);

/// <summary>
///     The counts of a run of the label generator.
/// </summary>
/// <param name="Kept">The label rows that existed and were kept unchanged.</param>
/// <param name="Generated">The label rows written for senses that had none.</param>
/// <param name="Suffixed">The generated labels that needed a numeric suffix.</param>
/// <param name="Fallbacks">The generated labels that fell back to the part of speech name.</param>
internal sealed record LabelRun(int Kept, int Generated, int Suffixed, int Fallbacks)
{
    /// <summary>
    ///     Renders the counts as one line.
    /// </summary>
    /// <returns>The summary line.</returns>
    public string Render() => string.Create(
        CultureInfo.InvariantCulture,
        $"sense_label: {Kept} kept, {Generated} generated ({Suffixed} with a numeric suffix, {Fallbacks} by part of speech name).");
}

/// <summary>
///     Gives every sense of the inventory that has no label a readable, deterministic label.
/// </summary>
/// <remarks>
///     <para>
///     The public id of a sense is <c>lemma#label</c>. A label is the first usable slug of: the part of speech name
///     (the only sense of its lemma and part of speech), the first synonym of the synset, the first usable member of the
///     first hypernym synset that has one (a member whose slug is empty or the slug of the lemma itself is skipped), the
///     part of speech name; a numeric suffix makes it unique among the labels of the lemma. Labels that
///     exist, and the labels of the old ids of the id map, are never changed, removed or taken again. Every order that
///     could change the result is fixed by the data (sense ids and synset ids by their UTF-8 bytes, the tag count),
///     never by the order in which rows were read, so the same data always gives the same files.
///     </para>
///     <para>
///     The type is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class SenseLabelGenerator
{
    /// <summary>The origin written for the label of a sense of Open English WordNet.</summary>
    public const string OriginGenerated = "generated";

    /// <summary>The origin written for the label of a project sense.</summary>
    public const string OriginProject = "project";

    /// <summary>The origin of a curated group, the only origin a group has.</summary>
    public const string GroupCurated = "curated";

    // The order of the parts of speech in the inventory order of the senses of a lemma
    private const string PosOrder = "nvarupcidx";

    /// <summary>
    ///     Reads the tables of a data folder, writes the <c>sense_label</c> table with a label for every sense of the
    ///     inventory, and returns the counts. It never creates a group: a group exists only when it is curated.
    /// </summary>
    /// <param name="dataDirectory">The data folder with <c>schema.yaml</c> and the tables.</param>
    /// <param name="outputDirectory">The folder that receives the tables, or null to write into the data folder.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when an input table is missing or unreadable, or two labels of one lemma are equal; nothing is written.</exception>
    public static LabelRun Run(string dataDirectory, string? outputDirectory = null)
    {
        var schema = DataSchema.Load(Path.Join(dataDirectory, "schema.yaml"));
        var entryTable = schema.Table("entry");
        var senseTable = schema.Table("sense");
        RequireFolder(dataDirectory, entryTable);
        RequireFolder(dataDirectory, senseTable);
        RequireFolder(dataDirectory, schema.Table("synset_relation"));
        Dictionary<string, string[]> entries;
        List<string[]> senseRows;
        LabelInputs inputs;
        try
        {
            (entries, senseRows, inputs) = Load(dataDirectory, schema);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new InvalidDataException($"The tables in {dataDirectory} cannot be read: {ex.Message}", ex);
        }

        var output = Generate(inputs);

        // Everything is computed in memory first, so a failure leaves the files as they were
        var resolver = new PartitionResolver(schema.FileSplit);
        foreach (var row in entries.Values)
        {
            resolver.AddEntry(row[0], row[1]);
        }

        foreach (var row in senseRows)
        {
            resolver.AddSense(row[0], row[2], row[3], row[1]);
        }

        var target = outputDirectory ?? dataDirectory;
        DataTables.Write(target, schema.Table("sense_label"), [.. inputs.Labels, .. output.Rows], resolver);
        return new LabelRun(inputs.Labels.Count, output.Rows.Count, output.Suffixed, output.Fallbacks);
    }

    // Reads the tables the generator needs; a row of the wrong width or a number that cannot be read is reported with the
    // file and the line instead of surfacing as an index or format exception
    private static (Dictionary<string, string[]> Entries, List<string[]> SenseRows, LabelInputs Inputs) Load(string dataDirectory, DataSchema schema)
    {
        var entries = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, line, row) in ReadRows(dataDirectory, schema.Table("entry")))
        {
            entries[row[0]] = row;
            tokenCounts[row[0]] = ParseCount(row[4], path, line);
        }

        var senseRows = ReadRows(dataDirectory, schema.Table("sense")).Select(r => r.Row).ToList();
        var senses = new List<LabelSense>(senseRows.Count);
        foreach (var row in senseRows)
        {
            if (!entries.TryGetValue(row[2], out var entry))
            {
                throw new InvalidDataException($"The sense '{row[0]}' refers to the entry '{row[2]}', which does not exist.");
            }

            senses.Add(new LabelSense(row[0], row[1], entry[1], entry[2], tokenCounts[row[2]], row[3]));
        }

        var hypernyms = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, _, row) in ReadRows(dataDirectory, schema.Table("synset_relation")).Where(r => r.Row[1] == "hypernym"))
        {
            if (!hypernyms.TryGetValue(row[0], out var list))
            {
                hypernyms[row[0]] = list = [];
            }

            list.Add(row[2]);
        }

        var labels = ReadRows(dataDirectory, schema.Table("sense_label")).Select(r => r.Row).ToList();
        var retired = ReadRows(dataDirectory, schema.Table("sense_label_map")).Select(r => r.Row[0]).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, line, row) in ReadRows(dataDirectory, schema.Table("sense_frequency")))
        {
            counts[row[0]] = ParseCount(row[1], path, line);
        }

        var inputs = new LabelInputs(
            senses,
            hypernyms.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal),
            labels,
            retired,
            counts,
            schema.Vocabulary("pos"));
        return (entries, senseRows, inputs);
    }

    private static List<(string Path, int Line, string[] Row)> ReadRows(string dataDirectory, TableDef table)
    {
        var rows = DataTables.ReadLocated(dataDirectory, table).ToList();
        foreach (var (path, line, row) in rows)
        {
            if (row.Length != table.Columns.Count)
            {
                throw new InvalidDataException($"{path}:{line.ToString(CultureInfo.InvariantCulture)}: the row has {row.Length.ToString(CultureInfo.InvariantCulture)} cells but the table '{table.Name}' has {table.Columns.Count.ToString(CultureInfo.InvariantCulture)} columns.");
            }
        }

        return rows;
    }

    private static int ParseCount(string text, string path, int line) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{path}:{line.ToString(CultureInfo.InvariantCulture)}: '{text}' is not a non-negative integer.");

    /// <summary>
    ///     Computes the labels of the senses of the inventory that have none; the pure part of the generator.
    /// </summary>
    /// <param name="inputs">The loaded tables.</param>
    /// <returns>The new rows and the counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when two existing labels of one lemma are equal.</exception>
    public static LabelOutput Generate(LabelInputs inputs)
    {
        var lemmaOf = inputs.Senses.ToDictionary(s => s.SenseId, s => s.Lemma, StringComparer.Ordinal);
        var used = UsedLabels(inputs, lemmaOf);
        var labeled = inputs.Labels.Select(l => l[0]).ToHashSet(StringComparer.Ordinal);
        var synsetMembers = inputs.Senses.GroupBy(s => s.SynsetId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SenseId, Utf8Order).ToList(), StringComparer.Ordinal);
        var perLemmaPos = inputs.Senses.GroupBy(s => (s.Lemma, s.Pos)).ToDictionary(g => g.Key, g => g.Count());
        var rows = new List<string[]>();
        var suffixed = 0;
        var fallbacks = 0;
        var pending = inputs.Senses.Where(s => s.InInventory && !labeled.Contains(s.SenseId)).GroupBy(s => s.Lemma, StringComparer.Ordinal).OrderBy(g => g.Key, Utf8Order);
        foreach (var lemmaSenses in pending)
        {
            var taken = used.TryGetValue(lemmaSenses.Key, out var set) ? set : [];
            foreach (var sense in lemmaSenses.OrderBy(s => PosOrder.IndexOf(s.Pos, StringComparison.Ordinal)).ThenByDescending(s => Count(inputs, s)).ThenBy(s => s.SenseId, Utf8Order))
            {
                var (slug, fromName) = Choose(inputs, sense, synsetMembers, perLemmaPos[(sense.Lemma, sense.Pos)]);
                var label = slug;
                var number = 1;
                while (taken.Contains(label))
                {
                    number++;
                    label = slug + "-" + number.ToString(CultureInfo.InvariantCulture);
                }

                suffixed += number > 1 ? 1 : 0;
                fallbacks += fromName && perLemmaPos[(sense.Lemma, sense.Pos)] > 1 ? 1 : 0;
                taken.Add(label);
                rows.Add([sense.SenseId, label, sense.SenseKey.Length == 0 ? OriginProject : OriginGenerated]);
            }
        }

        return new LabelOutput(rows, suffixed, fallbacks);
    }

    /// <summary>
    ///     Computes the slug of a text: lower-case, accents removed, every run of other characters one hyphen.
    /// </summary>
    /// <param name="text">The text, for example a lemma.</param>
    /// <returns>The slug made of lower-case ASCII letters, digits and single hyphens, or an empty text when nothing usable remains.</returns>
    public static string Slug(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && result.Length > 0)
                {
                    result.Append('-');
                }

                pendingHyphen = false;
                result.Append(c);
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return result.ToString();
    }

    /// <summary>
    ///     Gets the candidates of a sense in the order of the rules: the slug of the first synonym, then of the first
    ///     hypernym head, then the part of speech name.
    /// </summary>
    /// <param name="inputs">The loaded tables.</param>
    /// <param name="sense">The sense.</param>
    /// <param name="synsetMembers">The senses of each synset in the order of their sense ids.</param>
    /// <returns>The candidate slugs, each non-empty and different from the slug of the lemma, except the last.</returns>
    public static IEnumerable<string> Candidates(LabelInputs inputs, LabelSense sense, IReadOnlyDictionary<string, List<LabelSense>> synsetMembers)
    {
        var own = Slug(sense.Lemma);
        var synonym = synsetMembers.GetValueOrDefault(sense.SynsetId, [])
            .Where(m => m.SenseId != sense.SenseId).Select(m => Slug(m.Lemma)).FirstOrDefault(s => s.Length > 0 && s != own);
        if (synonym is not null)
        {
            yield return synonym;
        }

        // The first hypernym synset (by id) that has a usable member gives the head: its first member (by sense id) whose
        // slug is not empty and not the slug of the lemma itself
        var head = inputs.Hypernyms.GetValueOrDefault(sense.SynsetId, [])
            .OrderBy(h => h, Utf8Order)
            .Select(h => synsetMembers.GetValueOrDefault(h, []).Select(m => Slug(m.Lemma)).FirstOrDefault(s => s.Length > 0 && s != own))
            .FirstOrDefault(s => s is not null);
        if (head is not null)
        {
            yield return head;
        }

        yield return inputs.PosNames[sense.Pos];
    }

    private static (string Slug, bool FromName) Choose(LabelInputs inputs, LabelSense sense, IReadOnlyDictionary<string, List<LabelSense>> synsetMembers, int sensesOfPos)
    {
        var name = inputs.PosNames[sense.Pos];
        if (sensesOfPos == 1)
        {
            return (name, true);
        }

        var first = Candidates(inputs, sense, synsetMembers).First();
        return (first, first == name);
    }

    private static int Count(LabelInputs inputs, LabelSense sense) =>
        sense.SenseKey.Length > 0 ? inputs.Counts.GetValueOrDefault(sense.SenseKey) : 0;

    private static Dictionary<string, HashSet<string>> UsedLabels(LabelInputs inputs, Dictionary<string, string> lemmaOf)
    {
        var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in inputs.Labels)
        {
            if (!lemmaOf.TryGetValue(row[0], out var lemma))
            {
                continue;
            }

            if (!used.TryGetValue(lemma, out var set))
            {
                used[lemma] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            if (!set.Add(row[1]))
            {
                throw new InvalidDataException($"The label '{row[1]}' of the lemma '{lemma}' is the label of two senses.");
            }
        }

        // A label is taken by a label of the lemma and by a retired id; the label of a group is not taken, because the id
        // of a group (lemma#group:label) lives in its own namespace
        foreach (var id in inputs.RetiredIds)
        {
            var cut = id.LastIndexOf('#');
            if (cut <= 0)
            {
                continue;
            }

            var lemma = id[..cut];
            if (!used.TryGetValue(lemma, out var set))
            {
                used[lemma] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(id[(cut + 1)..]);
        }

        return used;
    }

    private static void RequireFolder(string dataDirectory, TableDef table)
    {
        var path = table.Partition == PartitionKind.None ? Path.Join(dataDirectory, table.Name + DataLayout.Extension) : Path.Join(dataDirectory, table.Name);
        if (!(File.Exists(path) || Directory.Exists(path)))
        {
            throw new InvalidDataException($"The input table '{table.Name}' is missing: {path} does not exist.");
        }
    }

    private static readonly IComparer<string> Utf8Order = Comparer<string>.Create(DataLayout.CompareUtf8);
}
