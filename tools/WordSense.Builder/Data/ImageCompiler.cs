using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Data;

/// <summary>
///     The result of <see cref="ImageCompiler.Compile"/>.
/// </summary>
/// <param name="Spellings">The number of distinct spellings.</param>
/// <param name="Analyses">The number of analysis rows (before the scope lists expand them).</param>
/// <param name="Entries">The number of entries the image keeps.</param>
/// <param name="Bytes">The size of the image in bytes.</param>
internal sealed record ImageResult(int Spellings, int Analyses, int Entries, int Bytes);

/// <summary>
///     Compiles the data tables into the flat word image (<c>wordsense.img</c>) that the core package embeds.
/// </summary>
/// <remarks>
///     <para>
///     The image holds the <c>form</c> table (every analysis of every written form), the entries those forms refer to,
///     the scope lists of <c>form_sense_scope</c> and the class table of the shipped models; see
///     <see cref="ImageFormat"/> for the layout. Spellings and entry lemmas are sorted by their UTF-8 bytes. Each section
///     is Brotli-compressed at the highest quality, after a byte-plane shuffle for fixed-width columns. The same data
///     always gives the same sections; the compressed bytes can differ between platforms when the Brotli library
///     differs, so compare decompressed sections and the data hash, not the file.
///     </para>
///     <para>
///     The data hash of the header covers the committed data tables the image is compiled from, with carriage returns
///     removed so that it is the same on every platform.
///     </para>
/// </remarks>
internal static class ImageCompiler
{
    /// <summary>
    ///     The location of the image in the core package, relative to the repository root.
    /// </summary>
    public const string DefaultOutput = "src/DemaConsulting.WordSense/Resources/wordsense.img";

    private const int MaxText = 255;
    private const int MaxIndex = 65535;
    private const string SurfacePrefix = "\u0001surface\t";

    // The key of the entry an analysis uses: its entry id, or a synthesized key of lemma and part of speech
    private static string EntryKey(string[] formRow) =>
        formRow[2].Length > 0 ? formRow[2] : $"{SurfacePrefix}{formRow[6]}\t{formRow[7]}";

    // The image entry of a surface-only analysis: its synthesized key stands for the id
    private static string[] SurfaceEntry(string key)
    {
        var parts = key[SurfacePrefix.Length..].Split('\t');
        return [key, parts[0], parts[1], "0", "1", "1", string.Empty];
    }

    private static readonly string[] HashedTables = ["entry", "form", "form_sense_scope", "coarse_class"];

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
        var formRows = DataTables.Read(dataDirectory, schema.Table("form")).ToList();
        var scopeRows = DataTables.Read(dataDirectory, schema.Table("form_sense_scope")).ToList();
        var classRows = DataTables.Read(dataDirectory, schema.Table("coarse_class"))
            .Where(r => r[4] != "unmapped")
            .OrderBy(r => r[0], Comparer<string>.Create(DataLayout.CompareUtf8))
            .ToList();

        var utf8 = Comparer<string>.Create(DataLayout.CompareUtf8);

        // Classes
        var classIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < classRows.Count; i++)
        {
            classIndex[classRows[i][0]] = i;
        }

        // Scope rows by (spelling, rank), the classes in class order
        var scopeBy = new Dictionary<(string, int), List<int>>();
        foreach (var row in scopeRows)
        {
            var key = (row[0], int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture));
            if (!classIndex.TryGetValue(row[2], out var index))
            {
                throw new InvalidDataException($"form_sense_scope: the class '{row[2]}' of '{row[0]}' is not in the class table.");
            }

            if (!scopeBy.TryGetValue(key, out var list))
            {
                scopeBy[key] = list = [];
            }

            list.Add(index);
        }

        foreach (var list in scopeBy.Values)
        {
            list.Sort();
        }

        // Forms by spelling, in rank order; the used entries sorted by lemma bytes, part of speech and id. A surface-only
        // analysis (no entry id) gets one synthesized image entry for its lemma and part of speech.
        var bySpelling = formRows
            .GroupBy(r => r[0], StringComparer.Ordinal)
            .OrderBy(g => g.Key, utf8)
            .Select(g => (Spelling: g.Key, Rows: g.OrderBy(r => int.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture)).ToList()))
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
            .OrderBy(e => e[1], utf8).ThenBy(e => (int)DataTables.PosOf(e[2])).ThenBy(e => e[0], utf8)
            .ToList();
        var entryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < usedEntries.Count; i++)
        {
            entryIndex[usedEntries[i][0]] = i;
        }

        var featureList = formRows.Select(r => r[3]).Append(string.Empty).Distinct(StringComparer.Ordinal).OrderBy(f => f, utf8).ToList();
        var featureIndex = featureList.Select((f, i) => (f, i)).ToDictionary(t => t.f, t => t.i, StringComparer.Ordinal);

        // Analysis rows and scope lists
        var scopeLists = new List<List<int>>();
        var scopeIds = new Dictionary<string, int>(StringComparer.Ordinal);
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
                var scope = 0;
                if (scopeBy.TryGetValue((spelling, int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture)), out var classes))
                {
                    var key = string.Join(',', classes);
                    if (!scopeIds.TryGetValue(key, out var id))
                    {
                        id = scopeLists.Count;
                        scopeLists.Add(classes);
                        scopeIds[key] = id;
                    }

                    scope = id + 1;
                }

                if (scope > MaxIndex || featureIndex[row[3]] > MaxIndex)
                {
                    throw new InvalidDataException("form: too many distinct feature strings or scope lists for the image format.");
                }

                rows.Add((uint)entry);
                rows.Add((uint)featureIndex[row[3]] | ((uint)scope << 16));
            }
        }

        // Fold index: spellings with an upper-case letter by their lower-case text
        var fold = Enumerable.Range(0, bySpelling.Count)
            .Where(i => !string.Equals(bySpelling[i].Spelling.ToLowerInvariant(), bySpelling[i].Spelling, StringComparison.Ordinal))
            .OrderBy(i => bySpelling[i].Spelling.ToLowerInvariant(), utf8).ThenBy(i => i)
            .ToList();

        var sections = new SortedDictionary<int, (byte[] Raw, int Stride)>
        {
            [ImageFormat.SectionMeta] = (Uints([(uint)bySpelling.Count, (uint)usedEntries.Count, (uint)(rows.Count / 2), (uint)scopeLists.Count, (uint)classRows.Count, (uint)featureList.Count, (uint)fold.Count]), 4),
            [ImageFormat.SectionAnalysisCount] = (analysisCounts, 1),
            [ImageFormat.SectionAnalysisRows] = (Uints(rows), 8),
        };
        AddStrings(sections, ImageFormat.SectionSpellingLength, ImageFormat.SectionSpellingBytes, bySpelling.Select(b => b.Spelling));
        var scopeStart = new List<uint> { 0 };
        var scopeItems = new List<uint>();
        foreach (var list in scopeLists)
        {
            scopeItems.AddRange(list.Select(c => (uint)c));
            scopeStart.Add((uint)scopeItems.Count);
        }

        sections[ImageFormat.SectionScopeStart] = (Uints(scopeStart), 4);
        sections[ImageFormat.SectionScopeItems] = (Uints(scopeItems), 4);
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
        AddStrings(sections, ImageFormat.SectionClassIdLength, ImageFormat.SectionClassIdBytes, classRows.Select(r => r[0]));
        sections[ImageFormat.SectionClassPos] = (classRows.Select(r => (byte)DataTables.PosOf(r[3])).ToArray(), 1);

        var image = Assemble(sections, DataHash(dataDirectory));
        return (image, new ImageResult(bySpelling.Count, formRows.Count, usedEntries.Count, image.Length));
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

    private static byte[] Assemble(SortedDictionary<int, (byte[] Raw, int Stride)> sections, byte[] dataHash)
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
