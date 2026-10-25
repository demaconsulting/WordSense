using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Data;

/// <summary>
///     The result of <see cref="ImageCompiler.Compile"/>.
/// </summary>
/// <param name="Spellings">The number of distinct spellings.</param>
/// <param name="Analyses">The number of analysis rows.</param>
/// <param name="Entries">The number of entries the image keeps.</param>
/// <param name="Senses">The number of senses of the catalog.</param>
/// <param name="Groups">The number of groups of the catalog.</param>
/// <param name="Bytes">The size of the image in bytes.</param>
internal sealed record ImageResult(int Spellings, int Analyses, int Entries, int Senses, int Groups, int Bytes);

/// <summary>
///     Compiles the data tables into the flat word image (<c>wordsense.img</c>) that the core package embeds.
/// </summary>
/// <remarks>
///     <para>
///     The image holds the <c>form</c> table (every analysis of every written form), the entries those forms refer to
///     and the sense catalog: the sense labels, the groups and the sense keys; the senses of a lemma and
///     part of speech are in the order of Open English WordNet (the WordNet 3.0 tag counts do not order them); see <see cref="ImageFormat"/> for the layout. Spellings and entry lemmas
///     are sorted by their UTF-8 bytes. Each section is Brotli-compressed at the highest quality, after a byte-plane
///     shuffle for fixed-width columns. The same data always gives the same sections; the compressed bytes can differ
///     between platforms when the Brotli library differs, so compare decompressed sections and the data hash, not the
///     file.
///     </para>
///     <para>
///     A spelling without a letter (a numeral of Open English WordNet such as <c>0</c>, <c>24/7</c> or <c>.22</c>) is left
///     out, with its analyses, its entries and the senses of its lemma: the library never looks such a token up (the
///     runtime applies the same rule, <see cref="CueModel.HasLetter"/>), so the image holds none and the count, the senses
///     and the lookup of the lexicon agree. The filter is applied here and not to the data tables, which stay a faithful
///     copy of the source, so a new import of the source needs no cleanup.
///     </para>
///     <para>
///     The compilation fails, with one message that lists every violation, when a sense of a single-token entry or a
///     project sense has no label, two senses have the same id, a sense is in two groups, a group has fewer than two
///     senses, holds all the senses of its lemma and part of speech or mixes lemmas or parts of speech. The data hash
///     of the header covers the committed data tables the image is compiled from, with carriage returns removed so
///     that it is the same on every platform.
///     </para>
/// </remarks>
internal static class ImageCompiler
{
    /// <summary>
    ///     The location of the image in the core package, relative to the repository root.
    /// </summary>
    public const string DefaultOutput = "src/DemaConsulting.WordSense/Resources/wordsense.img";

    private const int MaxText = 255;
    private const string SurfacePrefix = "\u0001surface\t";

    // The tables the image is compiled from; sense_frequency is not one of them (it feeds the prior of the encoder, not the order)
    private static readonly string[] HashedTables =
        ["entry", "form", "sense", "sense_label", "sense_group", "group_sense"];

    private static readonly Comparer<string> Utf8 = Comparer<string>.Create(DataLayout.CompareUtf8);

    // The key of the entry an analysis uses: its entry id, or a synthesized key of lemma and part of speech
    private static string EntryKey(string[] formRow) =>
        formRow[2].Length > 0 ? formRow[2] : $"{SurfacePrefix}{formRow[6]}\t{formRow[7]}";

    // The image entry of a surface-only analysis: its synthesized key stands for the id
    private static string[] SurfaceEntry(string key)
    {
        var parts = key[SurfacePrefix.Length..].Split('\t');
        return [key, parts[0], parts[1], "0", "1", "1", string.Empty];
    }

    /// <summary>
    ///     Compiles the image and writes it to a file.
    /// </summary>
    /// <param name="dataDirectory">The data directory.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data cannot be compiled (run <c>validate-data</c> for details).</exception>
    public static ImageResult Compile(string dataDirectory, string output)
    {
        var (image, result) = Build(dataDirectory);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(output, image);
        return result;
    }

    /// <summary>
    ///     Compiles the image in memory.
    /// </summary>
    /// <param name="dataDirectory">The data directory.</param>
    /// <returns>The image bytes and the counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data cannot be compiled.</exception>
    public static (byte[] Image, ImageResult Result) Build(string dataDirectory)
    {
        var schema = DataSchema.Load(Path.Join(dataDirectory, "schema.yaml"));
        var entryRows = DataTables.Read(dataDirectory, schema.Table("entry")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var formRows = DataTables.Read(dataDirectory, schema.Table("form")).Where(r => CueModel.HasLetter(r[0])).ToList();

        var sections = new SortedDictionary<int, (byte[] Raw, int Stride)>();
        var (spellingCount, entryCount) = AddAnalysisSections(sections, entryRows, formRows);
        var (senseCount, groupCount) = AddCatalogSections(sections, dataDirectory, schema, entryRows);

        var inventoryHash = ImageFormat.ComputeInventoryHash(id => sections.TryGetValue(id, out var section) ? section.Raw : []);
        var image = Assemble(sections, DataHash(dataDirectory), inventoryHash);
        return (image, new ImageResult(spellingCount, formRows.Count, entryCount, senseCount, groupCount, image.Length));
    }

    /// <summary>
    ///     Computes the hash of the data an image is compiled from.
    /// </summary>
    /// <param name="dataDirectory">The data directory.</param>
    /// <returns>The SHA-256 over the names and the content (without carriage returns) of the files, in name order.</returns>
    public static byte[] DataHash(string dataDirectory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = new List<(string Name, string Path)>();
        foreach (var table in HashedTables)
        {
            var directory = Path.Join(dataDirectory, table);
            if (Directory.Exists(directory))
            {
                files.AddRange(Directory.GetFiles(directory, "*.tsv").Select(f => ($"data/{table}/{Path.GetFileName(f)}", f)));
            }

            var single = Path.Join(dataDirectory, table + ".tsv");
            if (File.Exists(single))
            {
                files.Add(($"data/{table}.tsv", single));
            }
        }

        foreach (var (name, path) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var nameBytes = Encoding.UTF8.GetBytes(name + "\n");
            hash.AppendData(nameBytes);
            var content = File.ReadAllBytes(path).Where(b => b != (byte)'\r').ToArray();
            hash.AppendData(content);
            hash.AppendData("\n"u8);
        }

        return hash.GetHashAndReset();
    }

    // The spellings, entries, features and analysis rows of the form table
    private static (int Spellings, int Entries) AddAnalysisSections(
        SortedDictionary<int, (byte[] Raw, int Stride)> sections,
        Dictionary<string, string[]> entryRows,
        List<string[]> formRows)
    {
        // Forms by spelling, in rank order; the used entries sorted by lemma bytes, part of speech and id. A surface-only
        // analysis (no entry id) gets one synthesized image entry for its lemma and part of speech.
        var bySpelling = formRows
            .GroupBy(r => r[0], StringComparer.Ordinal)
            .OrderBy(g => g.Key, Utf8)
            .Select(g => (Spelling: g.Key, Rows: g.OrderBy(r => int.Parse(r[1], CultureInfo.InvariantCulture)).ToList()))
            .ToList();
        var usedEntries = formRows.Select(EntryKey).Distinct(StringComparer.Ordinal)
            .Select(key =>
            {
                if (key.StartsWith(SurfacePrefix, StringComparison.Ordinal))
                {
                    return SurfaceEntry(key);
                }

                return entryRows.TryGetValue(key, out var e) ? e : throw new InvalidDataException($"form: the entry '{key}' does not exist.");
            })
            .OrderBy(e => e[1], Utf8).ThenBy(e => (int)DataTables.PosOf(e[2])).ThenBy(e => e[0], Utf8)
            .ToList();
        var entryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < usedEntries.Count; i++)
        {
            entryIndex[usedEntries[i][0]] = i;
        }

        var featureList = formRows.Select(r => r[3]).Append(string.Empty).Distinct(StringComparer.Ordinal).OrderBy(f => f, Utf8).ToList();
        var featureIndex = featureList.Select((f, i) => (f, i)).ToDictionary(t => t.f, t => t.i, StringComparer.Ordinal);

        var analysisCounts = new byte[bySpelling.Count];
        var rows = new List<uint>(formRows.Count * 2);
        var entryForms = new SortedSet<int>[usedEntries.Count];
        for (var i = 0; i < entryForms.Length; i++)
        {
            entryForms[i] = [];
        }

        for (var s = 0; s < bySpelling.Count; s++)
        {
            var (spelling, spellingRows) = bySpelling[s];
            if (Encoding.UTF8.GetByteCount(spelling) > MaxText || spellingRows.Count > byte.MaxValue)
            {
                throw new InvalidDataException($"form: the spelling '{spelling}' is too long or has too many analyses.");
            }

            analysisCounts[s] = (byte)spellingRows.Count;
            foreach (var row in spellingRows)
            {
                var entry = entryIndex[EntryKey(row)];
                entryForms[entry].Add(s);
                rows.Add((uint)entry);
                rows.Add((uint)featureIndex[row[3]]);
            }
        }

        // Fold index: spellings with an upper-case letter by their lower-case text
        var fold = Enumerable.Range(0, bySpelling.Count)
            .Where(i => !string.Equals(bySpelling[i].Spelling.ToLowerInvariant(), bySpelling[i].Spelling, StringComparison.Ordinal))
            .OrderBy(i => bySpelling[i].Spelling.ToLowerInvariant(), Utf8).ThenBy(i => i)
            .ToList();

        sections[ImageFormat.SectionAnalysisCount] = (analysisCounts, 1);
        sections[ImageFormat.SectionAnalysisRows] = (Uints(rows), 8);
        AddStrings(sections, ImageFormat.SectionSpellingLength, ImageFormat.SectionSpellingBytes, bySpelling.Select(b => b.Spelling));
        AddStrings(sections, ImageFormat.SectionFeatureLength, ImageFormat.SectionFeatureBytes, featureList);
        AddStrings(sections, ImageFormat.SectionEntryLemmaLength, ImageFormat.SectionEntryLemmaBytes, usedEntries.Select(e => e[1]));
        sections[ImageFormat.SectionEntryPos] = (usedEntries.Select(e => (byte)DataTables.PosOf(e[2])).ToArray(), 1);
        var formStart = new List<uint> { 0 };
        var forms = new List<uint>();
        foreach (var set in entryForms)
        {
            forms.AddRange(set.Select(i => (uint)i));
            formStart.Add((uint)forms.Count);
        }

        sections[ImageFormat.SectionEntryFormStart] = (Uints(formStart), 4);
        sections[ImageFormat.SectionEntryForms] = (Uints(forms), 4);
        sections[ImageFormat.SectionFold] = (Uints(fold.Select(i => (uint)i).ToList()), 4);
        sections[ImageFormat.SectionMeta] = (
            Uints([(uint)bySpelling.Count, (uint)usedEntries.Count, (uint)(rows.Count / 2), (uint)featureList.Count, (uint)fold.Count, 0, 0, 0]),
            4);
        return (bySpelling.Count, usedEntries.Count);
    }

    // One sense of the catalog, before it is numbered; Position and Homograph give its place in the Open English WordNet order
    private sealed record SenseEntry(string Id, string DataId, string Lemma, PartOfSpeech Pos, string? KeySuffix, int Position, int Homograph);

    // The senses, groups, lemma blocks and keys
    private static (int Senses, int Groups) AddCatalogSections(
        SortedDictionary<int, (byte[] Raw, int Stride)> sections,
        string dataDirectory,
        DataSchema schema,
        Dictionary<string, string[]> entryRows)
    {
        var errors = new List<string>();
        var allSenseRows = DataTables.Read(dataDirectory, schema.Table("sense")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var labels = DataTables.Read(dataDirectory, schema.Table("sense_label")).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);

        // The senses of a lemma without a letter are left out first, so that every check below sees the retained set only
        var omitted = allSenseRows.Values
            .Where(r => entryRows.TryGetValue(r[2], out var owner) && !CueModel.HasLetter(owner[1]))
            .Select(r => r[0])
            .ToHashSet(StringComparer.Ordinal);
        var senseRows = allSenseRows.Where(p => !omitted.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        foreach (var row in senseRows.Values.OrderBy(r => r[0], Utf8))
        {
            if (entryRows.TryGetValue(row[2], out var owner) && (owner[4] == "1" || owner[6] == "dema") && !labels.ContainsKey(row[0]))
            {
                errors.Add($"sense_label: the sense '{row[0]}' has no label.");
            }
        }

        var senses = new List<SenseEntry>();
        foreach (var (dataId, label) in labels.Where(p => !omitted.Contains(p.Key)).OrderBy(p => p.Key, Utf8))
        {
            if (!senseRows.TryGetValue(dataId, out var row) || !entryRows.TryGetValue(row[2], out var entry))
            {
                errors.Add($"sense_label: the sense '{dataId}' does not exist.");
                continue;
            }

            var lemma = entry[1];
            var key = row[1];
            if (key.Length > 0 && !key.StartsWith(lemma.ToLowerInvariant() + "%", StringComparison.Ordinal))
            {
                errors.Add($"sense: the key '{key}' of the sense '{dataId}' does not start with its lemma '{lemma.ToLowerInvariant()}%'.");
                continue;
            }

            senses.Add(new SenseEntry(
                lemma + "#" + label,
                dataId,
                lemma,
                DataTables.PosOf(entry[2]),
                key.Length == 0 ? null : key[(lemma.Length + 1)..],
                int.Parse(row[4], CultureInfo.InvariantCulture),
                int.Parse(entry[3], CultureInfo.InvariantCulture)));
        }

        senses.Sort((a, b) => DataLayout.CompareUtf8(a.Id, b.Id));
        for (var i = 1; i < senses.Count; i++)
        {
            if (senses[i - 1].Id == senses[i].Id)
            {
                errors.Add($"sense_label: the id '{senses[i].Id}' is used by two senses.");
            }
        }

        var indexOfData = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < senses.Count; i++)
        {
            indexOfData[senses[i].DataId] = i;
        }

        // Lemma blocks: the senses of a lemma and part of speech in the order of Open English WordNet: the senses of the dictionary
        // first by their position in the entry and the homograph number of the entry, the project senses (no sense key) last,
        // then the id bytes. The order does not depend on the WordNet 3.0 tag counts, which only feed the prior of the encoder.
        var blocks = senses.Select((s, i) => (Sense: s, Index: i))
            .GroupBy(p => (p.Sense.Lemma, p.Sense.Pos))
            .OrderBy(g => g.Key.Lemma, Utf8).ThenBy(g => (int)g.Key.Pos)
            .Select(g => g.OrderBy(p => p.Sense.KeySuffix is null ? 1 : 0).ThenBy(p => p.Sense.Position).ThenBy(p => p.Sense.Homograph).ThenBy(p => p.Sense.Id, Utf8).Select(p => p.Index).ToList())
            .ToList();
        var blockOf = new int[senses.Count];
        var rankInBlock = new int[senses.Count];
        for (var b = 0; b < blocks.Count; b++)
        {
            for (var k = 0; k < blocks[b].Count; k++)
            {
                blockOf[blocks[b][k]] = b;
                rankInBlock[blocks[b][k]] = k;
            }
        }

        var groups = ReadGroups(dataDirectory, schema, senses, indexOfData, omitted, blockOf, rankInBlock, blocks, errors);
        if (errors.Count > 0)
        {
            throw new InvalidDataException("The sense catalog cannot be compiled:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        WriteCatalog(sections, senses, blocks, groups);
        return (senses.Count, groups.Count);
    }

    private static List<(string Id, PartOfSpeech Pos, List<int> Members)> ReadGroups(
        string dataDirectory,
        DataSchema schema,
        List<SenseEntry> senses,
        Dictionary<string, int> indexOfData,
        HashSet<string> omitted,
        int[] blockOf,
        int[] rankInBlock,
        List<List<int>> blocks,
        List<string> errors)
    {
        var members = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var groupOfSense = new Dictionary<int, string>();
        foreach (var row in DataTables.Read(dataDirectory, schema.Table("group_sense")))
        {
            // A member that was left out of the catalog (a lemma without a letter) is no member: the checks below see the retained ones
            if (omitted.Contains(row[1]))
            {
                continue;
            }

            if (!indexOfData.TryGetValue(row[1], out var sense))
            {
                errors.Add($"group_sense: the sense '{row[1]}' of the group '{row[0]}' has no label.");
                continue;
            }

            if (!groupOfSense.TryAdd(sense, row[0]))
            {
                errors.Add($"group_sense: the sense '{senses[sense].Id}' is in the groups '{groupOfSense[sense]}' and '{row[0]}'.");
                continue;
            }

            if (!members.TryGetValue(row[0], out var list))
            {
                members[row[0]] = list = [];
            }

            list.Add(sense);
        }

        var declared = DataTables.Read(dataDirectory, schema.Table("sense_group")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var senseIds = senses.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<(string, PartOfSpeech, List<int>)>();

        // A declared group without any member would be left out of the image silently, so it is an error
        foreach (var id in declared.Keys.Where(k => !members.ContainsKey(k)).OrderBy(k => k, Utf8))
        {
            errors.Add($"group '{id}': a group needs at least two senses, but it has none in group_sense.");
        }
        foreach (var (id, list) in members.OrderBy(p => p.Key, Utf8))
        {
            if (!declared.TryGetValue(id, out var row))
            {
                errors.Add($"group_sense: the group '{id}' is not in sense_group.");
                continue;
            }

            list.Sort((a, b) => rankInBlock[a].CompareTo(rankInBlock[b]));
            if (!id.StartsWith(row[1] + "#group:", StringComparison.Ordinal) || id.Length == row[1].Length + "#group:".Length || senseIds.Contains(id))
            {
                errors.Add($"group '{id}': the id of a group is its lemma, '#group:' and a slug, and never the id of a sense.");
            }
            var distinctBlocks = list.Select(s => blockOf[s]).Distinct().ToList();
            if (distinctBlocks.Count != 1 || senses[list[0]].Lemma != row[1] || senses[list[0]].Pos != DataTables.PosOf(row[2]))
            {
                errors.Add($"group '{id}': its senses are not all senses of the lemma '{row[1]}' and the part of speech '{row[2]}'.");
            }
            else if (list.Count < 2)
            {
                errors.Add($"group '{id}': a group needs at least two senses.");
            }
            else if (list.Count >= blocks[distinctBlocks[0]].Count)
            {
                errors.Add($"group '{id}': a group never holds all the senses of its lemma and part of speech.");
            }

            result.Add((id, senses[list[0]].Pos, list));
        }

        return result;
    }

    private static void WriteCatalog(
        SortedDictionary<int, (byte[] Raw, int Stride)> sections,
        List<SenseEntry> senses,
        List<List<int>> blocks,
        List<(string Id, PartOfSpeech Pos, List<int> Members)> groups)
    {
        var groupIndex = new Dictionary<int, int>();
        for (var g = 0; g < groups.Count; g++)
        {
            foreach (var member in groups[g].Members)
            {
                groupIndex[member] = g + 1;
            }
        }

        AddStrings(sections, ImageFormat.SectionSenseIdLength, ImageFormat.SectionSenseIdBytes, senses.Select(s => s.Id));
        AddStrings(sections, ImageFormat.SectionSenseKeyLength, ImageFormat.SectionSenseKeyBytes, senses.Select(s => s.KeySuffix ?? string.Empty));
        sections[ImageFormat.SectionSenseGroup] = (Uints(senses.Select((_, i) => (uint)groupIndex.GetValueOrDefault(i)).ToList()), 4);
        AddStrings(sections, ImageFormat.SectionGroupIdLength, ImageFormat.SectionGroupIdBytes, groups.Select(g => g.Id));
        sections[ImageFormat.SectionGroupPos] = (groups.Select(g => (byte)g.Pos).ToArray(), 1);
        var groupStart = new List<uint> { 0 };
        var groupMembers = new List<uint>();
        foreach (var group in groups)
        {
            groupMembers.AddRange(group.Members.Select(m => (uint)m));
            groupStart.Add((uint)groupMembers.Count);
        }

        sections[ImageFormat.SectionGroupMemberStart] = (Uints(groupStart), 4);
        sections[ImageFormat.SectionGroupMembers] = (Uints(groupMembers), 4);

        AddStrings(sections, ImageFormat.SectionBlockLemmaLength, ImageFormat.SectionBlockLemmaBytes, blocks.Select(b => senses[b[0]].Lemma));
        sections[ImageFormat.SectionBlockPos] = (blocks.Select(b => (byte)senses[b[0]].Pos).ToArray(), 1);
        var blockStart = new List<uint> { 0 };
        var blockSenses = new List<uint>();
        foreach (var block in blocks)
        {
            blockSenses.AddRange(block.Select(s => (uint)s));
            blockStart.Add((uint)blockSenses.Count);
        }

        sections[ImageFormat.SectionBlockSenseStart] = (Uints(blockStart), 4);
        sections[ImageFormat.SectionBlockSenses] = (Uints(blockSenses), 4);

        var keyed = Enumerable.Range(0, senses.Count)
            .Where(i => senses[i].KeySuffix is not null)
            .OrderBy(i => senses[i].Lemma.ToLowerInvariant() + "%" + senses[i].KeySuffix, Utf8)
            .ToList();
        sections[ImageFormat.SectionKeyIndex] = (Uints(keyed.Select(i => (uint)i).ToList()), 4);

        sections[ImageFormat.SectionMeta] = (
            Uints(ReadUints(sections[ImageFormat.SectionMeta].Raw, senses.Count, groups.Count, blocks.Count)),
            4);
    }

    // The meta values the analysis part wrote, completed with the counts of the catalog
    private static List<uint> ReadUints(byte[] raw, int senses, int groups, int blocks)
    {
        var values = new List<uint>();
        for (var i = 0; i < raw.Length; i += 4)
        {
            values.Add(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i)));
        }

        values[ImageFormat.MetaSenseCount] = (uint)senses;
        values[ImageFormat.MetaGroupCount] = (uint)groups;
        values[ImageFormat.MetaBlockCount] = (uint)blocks;
        return values;
    }

    private static byte[] Uints(List<uint> values)
    {
        var bytes = new byte[values.Count * 4];
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    private static void AddStrings(SortedDictionary<int, (byte[] Raw, int Stride)> sections, int lengthId, int bytesId, IEnumerable<string> values)
    {
        var lengths = new List<byte>();
        using var bytes = new MemoryStream();
        foreach (var value in values)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            if (encoded.Length > MaxText)
            {
                throw new InvalidDataException($"The text '{value}' is longer than {MaxText} bytes.");
            }

            lengths.Add((byte)encoded.Length);
            bytes.Write(encoded);
        }

        sections[lengthId] = (lengths.ToArray(), 1);
        sections[bytesId] = (bytes.ToArray(), 1);
    }

    /// <summary>
    ///     Assembles an image from raw sections (for the compiler and for tests that damage a section).
    /// </summary>
    /// <param name="sections">The raw sections with the stride of their byte-plane shuffle.</param>
    /// <param name="dataHash">The data hash of the header.</param>
    /// <param name="inventoryHash">The inventory hash of the header.</param>
    /// <returns>The image.</returns>
    internal static byte[] Assemble(SortedDictionary<int, (byte[] Raw, int Stride)> sections, byte[] dataHash, byte[] inventoryHash)
    {
        var count = sections.Count;
        var stored = new List<(int Id, int Raw, byte[] Bytes, int Flags)>();
        foreach (var (id, (raw, stride)) in sections)
        {
            if (raw.Length == 0)
            {
                stored.Add((id, 0, [], stride));
                continue;
            }

            var shuffled = Shuffle(raw, stride);
            var packed = Compress(shuffled);
            stored.Add(packed.Length < raw.Length ? (id, raw.Length, packed, stride) : (id, raw.Length, raw, ImageFormat.FlagStored));
        }

        var directorySize = count * ImageFormat.DirectoryEntrySize;
        var body = new byte[directorySize + stored.Sum(s => s.Bytes.Length)];
        var offset = ImageFormat.HeaderSize + directorySize;
        var position = directorySize;
        for (var i = 0; i < stored.Count; i++)
        {
            var (id, raw, bytes, flags) = stored[i];
            var entry = body.AsSpan(i * ImageFormat.DirectoryEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)id);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)raw);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], (uint)flags);
            bytes.CopyTo(body, position);
            position += bytes.Length;
            offset += bytes.Length;
        }

        var image = new byte[ImageFormat.HeaderSize + body.Length];
        ImageFormat.Magic.CopyTo(image);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), ImageFormat.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8), (uint)count);
        dataHash.CopyTo(image, ImageFormat.DataHashOffset);
        inventoryHash.CopyTo(image, ImageFormat.InventoryHashOffset);
        body.CopyTo(image, ImageFormat.HeaderSize);
        SHA256.HashData(image.AsSpan(ImageFormat.HeaderSize)).CopyTo(image, ImageFormat.ContentHashOffset);
        return image;
    }

    private static byte[] Shuffle(byte[] raw, int stride)
    {
        if (stride <= 1)
        {
            return raw;
        }

        var n = raw.Length / stride;
        var result = new byte[raw.Length];
        for (var p = 0; p < stride; p++)
        {
            for (var i = 0; i < n; i++)
            {
                result[(p * n) + i] = raw[(i * stride) + p];
            }
        }

        return result;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(data);
        }

        return output.ToArray();
    }
}
