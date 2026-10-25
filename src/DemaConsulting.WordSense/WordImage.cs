using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Reads the flat binary word image (see <see cref="ImageFormat"/>) and answers lookups from it in place.
/// </summary>
/// <remarks>
///     <para>
///     The image is decompressed once, section by section, into flat arrays (no dictionary and no per-word object is
///     built): a lookup is a binary search over the UTF-8 spellings and a small amount of arithmetic, so opening the
///     image is fast and the memory it holds is the size of its sections. The data hash of the header names the data
///     the image was compiled from. Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
internal sealed partial class WordImage
{
    private const int MaxSpellingBytes = 255;
    private const int MaxSections = 64;

    private readonly Dictionary<int, SectionEntry> _directory = [];
    private readonly byte[] _data;
    private readonly byte[] _spellingBytes;
    private readonly uint[] _spellingOffset;
    private readonly uint[] _analysisStart;
    private readonly uint[] _rows;
    private readonly string[] _features;
    private readonly byte[] _entryLemmaBytes;
    private readonly uint[] _entryLemmaOffset;
    private readonly byte[] _entryPos;
    private readonly string?[] _entryLemmas;
    private readonly int _foldCount;
    private readonly uint[] _meta;
    private uint[]? _fold;
    private uint[]? _entryFormStart;
    private uint[]? _entryForms;
    private Catalog? _catalog;

    private WordImage(byte[] data)
    {
        _data = data;
        ParseDirectory();
        var meta = Uints(ImageFormat.SectionMeta);
        if (meta.Length != ImageFormat.MetaLength)
        {
            throw new InvalidDataException("The word image has an invalid meta section.");
        }

        _meta = meta;
        SpellingCount = Count(meta[ImageFormat.MetaSpellingCount]);
        EntryCount = Count(meta[ImageFormat.MetaEntryCount]);
        var rowCount = Count(meta[ImageFormat.MetaRowCount]);
        var featureCount = Count(meta[ImageFormat.MetaFeatureCount]);
        _foldCount = Count(meta[ImageFormat.MetaFoldCount]);
        SenseCount = Count(meta[ImageFormat.MetaSenseCount]);
        GroupCount = Count(meta[ImageFormat.MetaGroupCount]);

        var spellingLength = Section(ImageFormat.SectionSpellingLength);
        _spellingBytes = Section(ImageFormat.SectionSpellingBytes);
        _spellingOffset = Offsets(spellingLength, SpellingCount, _spellingBytes.Length, "spelling");
        var analysisCount = Section(ImageFormat.SectionAnalysisCount);
        if (analysisCount.Length != SpellingCount)
        {
            throw new InvalidDataException("The word image has an invalid analysis count section.");
        }

        _analysisStart = new uint[SpellingCount + 1];
        for (var i = 0; i < SpellingCount; i++)
        {
            _analysisStart[i + 1] = _analysisStart[i] + analysisCount[i];
        }

        _rows = Uints(ImageFormat.SectionAnalysisRows);
        if (_analysisStart[SpellingCount] != rowCount || _rows.Length != rowCount * 2)
        {
            throw new InvalidDataException("The word image has an invalid analysis row section.");
        }

        _features = Strings(ImageFormat.SectionFeatureLength, ImageFormat.SectionFeatureBytes, featureCount);
        _entryLemmaBytes = Section(ImageFormat.SectionEntryLemmaBytes);
        _entryLemmaOffset = Offsets(Section(ImageFormat.SectionEntryLemmaLength), EntryCount, _entryLemmaBytes.Length, "entry lemma");
        _entryPos = Section(ImageFormat.SectionEntryPos);
        if (_entryPos.Length != EntryCount)
        {
            throw new InvalidDataException("The word image has an invalid entry section.");
        }

        _entryLemmas = new string?[EntryCount];
        ValidateRows(rowCount, featureCount);
    }

    /// <summary>
    ///     Gets the number of spellings.
    /// </summary>
    public int SpellingCount { get; }

    /// <summary>
    ///     Gets the number of entries.
    /// </summary>
    public int EntryCount { get; }

    /// <summary>
    ///     Gets the number of senses of the catalog.
    /// </summary>
    public int SenseCount { get; }

    /// <summary>
    ///     Gets the number of groups of the catalog.
    /// </summary>
    public int GroupCount { get; }

    /// <summary>
    ///     Gets the SHA-256 of the data the image was compiled from.
    /// </summary>
    public ReadOnlySpan<byte> DataHash => _data.AsSpan(ImageFormat.DataHashOffset, ImageFormat.HashSize);

    /// <summary>
    ///     Gets the inventory hash of the header: the SHA-256 over the sections that name analyses, senses and groups.
    ///     Models trained for this inventory carry the hash, so a model and an image that disagree on the inventory are
    ///     detected when the model is loaded.
    /// </summary>
    public ReadOnlySpan<byte> InventoryHash => _data.AsSpan(ImageFormat.InventoryHashOffset, ImageFormat.HashSize);

    /// <summary>
    ///     Opens an image.
    /// </summary>
    /// <param name="data">The image bytes; the reader keeps the array and does not change it.</param>
    /// <param name="verifyHash">Whether to verify the content hash of the header.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidDataException">Thrown when the image is corrupt, truncated or of another version.</exception>
    public static WordImage Open(byte[] data, bool verifyHash = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The word image is little-endian.");
        }

        if (data.Length < ImageFormat.HeaderSize || !data.AsSpan(0, 4).SequenceEqual(ImageFormat.Magic))
        {
            throw new InvalidDataException("The word image has a wrong magic number.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != ImageFormat.Version)
        {
            throw new InvalidDataException("The word image has an unsupported version.");
        }

        if (verifyHash && !SHA256.HashData(data.AsSpan(ImageFormat.HeaderSize)).AsSpan().SequenceEqual(data.AsSpan(ImageFormat.ContentHashOffset, ImageFormat.HashSize)))
        {
            throw new InvalidDataException("The word image is corrupt: the content hash does not match.");
        }

        return new WordImage(data);
    }

    /// <summary>
    ///     Gets the analyses of a spelling by index.
    /// </summary>
    /// <param name="spelling">The spelling index.</param>
    /// <returns>The analyses in answer order.</returns>
    public ImmutableArray<WordDetails> Analyses(int spelling) => Materialize([spelling]);

    /// <summary>
    ///     Gets the decompressed bytes of a section (for tests and tools).
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <returns>The raw section bytes.</returns>
    public byte[] RawSection(int id) => Section(id);

    /// <summary>
    ///     Gets the ids of the sections of the image.
    /// </summary>
    public IEnumerable<int> SectionIds => _directory.Keys.OrderBy(k => k);

    /// <summary>
    ///     Gets a spelling by index.
    /// </summary>
    /// <param name="index">The index, 0 up to <see cref="SpellingCount"/>.</param>
    /// <returns>The spelling.</returns>
    public string Spelling(int index) => Encoding.UTF8.GetString(SpellingSpan(index));

    /// <summary>
    ///     Finds a spelling exactly.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The spelling index, or -1.</returns>
    public int Find(ReadOnlySpan<char> word)
    {
        var byteCount = Encoding.UTF8.GetByteCount(word);
        if (byteCount is 0 or > MaxSpellingBytes)
        {
            return -1;
        }

        Span<byte> buffer = stackalloc byte[MaxSpellingBytes];
        var n = Encoding.UTF8.GetBytes(word, buffer);
        return FindUtf8(buffer[..n]);
    }

    /// <summary>
    ///     Looks a word up: the exact spelling first and, when there is none, the lower-case spelling and the spellings
    ///     whose lower-case text equals the lower-case word. A possessive form (<c>dog's</c>, <c>dogs'</c>) that is not
    ///     stored is answered with the noun and pronoun analyses of its stem and the feature <c>Poss=Yes</c> added.
    /// </summary>
    /// <param name="word">The word; the curly apostrophe U+2019 counts as <c>'</c>.</param>
    /// <param name="senses">The analyses, in answer order.</param>
    /// <returns>True when the word is known.</returns>
    public bool TryLookup(string word, out ImmutableArray<WordDetails> senses)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Contains('\u2019', StringComparison.Ordinal))
        {
            word = word.Replace('\u2019', '\'');
        }

        if (TryLookupStored(word, out senses))
        {
            return true;
        }

        // The possessive of a known noun or pronoun: Xs' or X's with a stem that is known
        string? stem = null;
        if (word.Length > 2 && word.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            stem = word[..^2];
        }
        else if (word.Length > 2 && word.EndsWith("s'", StringComparison.OrdinalIgnoreCase))
        {
            stem = word[..^1];
        }

        if (stem is null || stem.Contains('\'', StringComparison.Ordinal) || !TryLookupStored(stem, out var stemSenses))
        {
            senses = default;
            return false;
        }

        var possessive = stemSenses
            .Where(s => s.PartOfSpeech is PartOfSpeech.Noun or PartOfSpeech.Pronoun)
            .Select(s => s with { Features = WithPossessive(s.Features) })
            .Distinct()
            .ToArray();
        if (possessive.Length == 0)
        {
            senses = default;
            return false;
        }

        senses = ImmutableCollectionsMarshal.AsImmutableArray(possessive);
        return true;
    }

    // Adds Poss=Yes to a feature string; the items stay in the alphabetical order of the stored strings
    private static string WithPossessive(string features)
    {
        var items = features.Length == 0 ? [] : features.Split('+').ToList();
        if (!items.Contains("Poss=Yes", StringComparer.Ordinal))
        {
            items.Add("Poss=Yes");
        }

        items.Sort(StringComparer.Ordinal);
        return string.Join('+', items);
    }

    private bool TryLookupStored(string word, out ImmutableArray<WordDetails> senses)
    {
        var exact = Find(word);
        if (exact >= 0)
        {
            senses = Materialize([exact]);
            return true;
        }

        var lower = word.ToLowerInvariant();
        var matches = new List<int>();
        if (!string.Equals(lower, word, StringComparison.Ordinal))
        {
            var index = Find(lower);
            if (index >= 0)
            {
                matches.Add(index);
            }
        }

        if (_foldCount > 0)
        {
            foreach (var index in FoldMatches(lower).Where(i => !matches.Contains(i)))
            {
                matches.Add(index);
            }
        }

        if (matches.Count == 0)
        {
            senses = default;
            return false;
        }

        senses = Materialize(matches);
        return true;
    }

    /// <summary>
    ///     Gets every spelling that is a form of a lemma (of any entry with that lemma), in spelling order.
    /// </summary>
    /// <param name="lemma">The lemma as written in the entry.</param>
    /// <returns>The spellings; empty for an unknown lemma.</returns>
    public IReadOnlyList<string> FormsOf(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        var byteCount = Encoding.UTF8.GetByteCount(lemma);
        if (byteCount is 0 or > MaxSpellingBytes)
        {
            return [];
        }

        Span<byte> query = stackalloc byte[MaxSpellingBytes];
        query = query[..Encoding.UTF8.GetBytes(lemma, query)];
        var first = FirstEntry(query);
        var spellings = new SortedSet<int>();
        if (first < EntryCount)
        {
            LoadForms();
            for (var e = first; e < EntryCount && EntryLemmaSpan(e).SequenceEqual(query); e++)
            {
                for (var k = _entryFormStart![e]; k < _entryFormStart[e + 1]; k++)
                {
                    spellings.Add((int)_entryForms![k]);
                }
            }
        }

        return spellings.Select(Spelling).ToArray();
    }

    /// <summary>
    ///     Determines whether the image has an analysis: an entry of the lemma and part of speech that some form lists with
    ///     the features (every <c>Key=Value</c> item of <paramref name="features"/> present; any features when empty).
    /// </summary>
    /// <param name="lemma">The lemma as written in the entry.</param>
    /// <param name="pos">The part of speech.</param>
    /// <param name="features">The required feature items joined by <c>+</c>, or an empty string.</param>
    /// <returns>True when an analysis with this lemma, part of speech and features exists.</returns>
    public bool AnalysisExists(string lemma, PartOfSpeech pos, string features)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(features);
        var byteCount = Encoding.UTF8.GetByteCount(lemma);
        if (byteCount is 0 or > MaxSpellingBytes)
        {
            return false;
        }

        Span<byte> query = stackalloc byte[MaxSpellingBytes];
        query = query[..Encoding.UTF8.GetBytes(lemma, query)];
        var required = features.Length == 0 ? [] : features.Split('+');
        LoadForms();
        for (var e = FirstEntry(query); e < EntryCount && EntryLemmaSpan(e).SequenceEqual(query); e++)
        {
            if (_entryPos[e] != (byte)pos)
            {
                continue;
            }

            if (EntryHasAnalysis(e, required))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether an entry has an analysis that carries every required feature item.
    /// </summary>
    /// <param name="e">The index of the entry; its forms must have been loaded.</param>
    /// <param name="required">
    ///     The required <c>Key=Value</c> feature items, or an empty array when any analysis of the entry matches.
    /// </param>
    /// <returns>
    ///     True when some form of the entry lists an analysis of this entry whose features contain every required
    ///     item; false when no analysis of the entry does, including when the entry lists no forms.
    /// </returns>
    private bool EntryHasAnalysis(int e, string[] required)
    {
        for (var k = _entryFormStart![e]; k < _entryFormStart[e + 1]; k++)
        {
            var spelling = (int)_entryForms![k];
            for (var r = (int)_analysisStart[spelling]; r < (int)_analysisStart[spelling + 1]; r++)
            {
                if (_rows[r * 2] == e && HasItems(_features[(int)_rows[(r * 2) + 1]], required))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasItems(string features, string[] required)
    {
        if (required.Length == 0)
        {
            return true;
        }

        var have = features.Split('+');
        return required.All(item => have.Contains(item, StringComparer.Ordinal));
    }

    private static int Count(uint value) => value > int.MaxValue / 8 ? throw new InvalidDataException("The word image has an invalid count.") : (int)value;

    private ReadOnlySpan<byte> SpellingSpan(int i) =>
        _spellingBytes.AsSpan((int)_spellingOffset[i], (int)(_spellingOffset[i + 1] - _spellingOffset[i]));

    private ReadOnlySpan<byte> EntryLemmaSpan(int i) =>
        _entryLemmaBytes.AsSpan((int)_entryLemmaOffset[i], (int)(_entryLemmaOffset[i + 1] - _entryLemmaOffset[i]));

    private int FindUtf8(ReadOnlySpan<byte> query)
    {
        int lo = 0, hi = SpellingCount;
        while (lo < hi)
        {
            var mid = (int)((uint)(lo + hi) >> 1);
            var c = SpellingSpan(mid).SequenceCompareTo(query);
            if (c == 0)
            {
                return mid;
            }

            if (c < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return -1;
    }

    private int FirstEntry(ReadOnlySpan<byte> lemma)
    {
        int lo = 0, hi = EntryCount;
        while (lo < hi)
        {
            var mid = (int)((uint)(lo + hi) >> 1);
            if (EntryLemmaSpan(mid).SequenceCompareTo(lemma) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private string EntryLemma(int entry) => _entryLemmas[entry] ??= Encoding.UTF8.GetString(EntryLemmaSpan(entry));

    private ImmutableArray<WordDetails> Materialize(List<int> spellings)
    {
        // One analysis for each lemma, part of speech and features: two entries of one lemma and part of speech (the
        // homographs of the source) give one analysis
        var result = new List<WordDetails>();
        foreach (var spelling in spellings)
        {
            for (var r = (int)_analysisStart[spelling]; r < (int)_analysisStart[spelling + 1]; r++)
            {
                var entry = (int)_rows[r * 2];
                var analysis = new WordDetails(EntryLemma(entry), (PartOfSpeech)_entryPos[entry], _features[(int)_rows[(r * 2) + 1]]);
                if (!result.Contains(analysis))
                {
                    result.Add(analysis);
                }
            }
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(result.ToArray());
    }

    private List<int> FoldMatches(string lower)
    {
        _fold ??= Uints(ImageFormat.SectionFold);
        var query = Encoding.UTF8.GetBytes(lower);
        int lo = 0, hi = _fold.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (FoldedBytes((int)_fold[mid]).AsSpan().SequenceCompareTo(query) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        var result = new List<int>();
        while (lo < _fold.Length && FoldedBytes((int)_fold[lo]).AsSpan().SequenceEqual(query))
        {
            result.Add((int)_fold[lo++]);
        }

        return result;
    }

    private byte[] FoldedBytes(int spelling) => Encoding.UTF8.GetBytes(Spelling(spelling).ToLowerInvariant());

    private void LoadForms()
    {
        if (_entryForms is not null)
        {
            return;
        }

        var start = Uints(ImageFormat.SectionEntryFormStart);
        var forms = Uints(ImageFormat.SectionEntryForms);
        if (start.Length != EntryCount + 1 || start[^1] != forms.Length || forms.Any(f => f >= SpellingCount))
        {
            throw new InvalidDataException("The word image has an invalid form section.");
        }

        _entryFormStart = start;
        _entryForms = forms;
    }

    private void ValidateRows(int rowCount, int featureCount)
    {
        for (var r = 0; r < rowCount; r++)
        {
            if (_rows[r * 2] >= EntryCount || _rows[(r * 2) + 1] >= featureCount)
            {
                throw new InvalidDataException("The word image has an analysis row that refers outside its tables.");
            }
        }

        if (_entryPos.Any(p => p > (byte)PartOfSpeech.Auxiliary))
        {
            throw new InvalidDataException("The word image has an invalid part of speech.");
        }
    }

    private static uint[] Offsets(byte[] lengths, int count, int total, string what)
    {
        if (lengths.Length != count)
        {
            throw new InvalidDataException($"The word image has an invalid {what} length section.");
        }

        var offsets = new uint[count + 1];
        for (var i = 0; i < count; i++)
        {
            offsets[i + 1] = offsets[i] + lengths[i];
        }

        return offsets[count] == total ? offsets : throw new InvalidDataException($"The word image has invalid {what} bytes.");
    }

    private string[] Strings(int lengthSection, int bytesSection, int count)
    {
        var bytes = Section(bytesSection);
        var offsets = Offsets(Section(lengthSection), count, bytes.Length, "string");
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Encoding.UTF8.GetString(bytes, (int)offsets[i], (int)(offsets[i + 1] - offsets[i]));
        }

        return result;
    }

    private uint[] Uints(int id)
    {
        var bytes = Section(id);
        if (bytes.Length % 4 != 0)
        {
            throw new InvalidDataException($"Section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the word image has an invalid length.");
        }

        return MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
    }

    private ushort[] Ushorts(int id)
    {
        var bytes = Section(id);
        if (bytes.Length % 2 != 0)
        {
            throw new InvalidDataException($"Section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the word image has an invalid length.");
        }

        return MemoryMarshal.Cast<byte, ushort>(bytes).ToArray();
    }

    /// <summary>
    ///     Reads and validates the section directory: every section lies after the header and the directory and inside
    ///     the image, no two sections overlap, the flags are known and the raw and stored lengths agree with them.
    /// </summary>
    /// <remarks>
    ///     The directory is checked completely before any section is decompressed, so a damaged directory is reported as
    ///     such and a reader never follows an offset into the header, the directory or another section.
    /// </remarks>
    /// <exception cref="InvalidDataException">Thrown when the directory is damaged.</exception>
    private void ParseDirectory()
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(8));
        if (count > MaxSections || ImageFormat.HeaderSize + (count * ImageFormat.DirectoryEntrySize) > _data.Length)
        {
            throw new InvalidDataException("The word image has an invalid directory.");
        }

        var firstSectionOffset = ImageFormat.HeaderSize + (count * ImageFormat.DirectoryEntrySize);
        var occupied = new List<(uint Start, uint End)>((int)count);
        for (var i = 0; i < count; i++)
        {
            var d = _data.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            var entry = new SectionEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(d[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[16..]));
            ValidateDirectoryEntry(entry, firstSectionOffset);
            if (!_directory.TryAdd((int)BinaryPrimitives.ReadUInt32LittleEndian(d), entry))
            {
                throw new InvalidDataException("The word image has a duplicate section in its directory.");
            }

            if (entry.Stored > 0)
            {
                occupied.Add((entry.Offset, entry.Offset + entry.Stored));
            }
        }

        // Sections are stored one after the other: after sorting by start, each must begin where or after the previous one ends
        occupied.Sort();
        for (var i = 1; i < occupied.Count; i++)
        {
            if (occupied[i].Start < occupied[i - 1].End)
            {
                throw new InvalidDataException("The word image has overlapping sections.");
            }
        }
    }

    /// <summary>
    ///     Checks one directory entry against the layout rules of <see cref="ImageFormat"/>.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="firstSectionOffset">The offset of the end of the directory, the lowest offset a section may have.</param>
    /// <exception cref="InvalidDataException">Thrown when the entry points outside the section area, has unknown flags or has inconsistent lengths.</exception>
    private void ValidateDirectoryEntry(SectionEntry entry, uint firstSectionOffset)
    {
        // The section lies in the area after the directory and ends inside the image
        if (entry.Offset < firstSectionOffset || (ulong)entry.Offset + entry.Stored > (ulong)_data.Length)
        {
            throw new InvalidDataException("The word image has a section outside its section area.");
        }

        if (entry.Raw > int.MaxValue)
        {
            throw new InvalidDataException("The word image has an invalid directory entry.");
        }

        // The flags are either "stored as is" alone or a byte-plane stride from 1 to 255; nothing else is defined
        var stored = entry.Flags == ImageFormat.FlagStored;
        if (!stored && (entry.Flags is 0 or > 0xFF))
        {
            throw new InvalidDataException("The word image has a section with unsupported flags.");
        }

        // An empty section stores nothing; a stored section is as long as its content; a compressed section is stored
        // only when it is shorter than its content and holds whole records of its stride
        if (!LengthsAreConsistent(entry, stored))
        {
            throw new InvalidDataException("The word image has a section whose stored and raw lengths disagree.");
        }
    }

    /// <summary>
    ///     Determines whether the raw and stored lengths of a directory entry agree with its storage mode.
    /// </summary>
    /// <param name="entry">The entry, whose flags are already known to be valid.</param>
    /// <param name="stored">Whether the section is stored as is (otherwise it is compressed, the flags being its stride).</param>
    /// <returns>True when the lengths are consistent.</returns>
    private static bool LengthsAreConsistent(SectionEntry entry, bool stored)
    {
        if (entry.Raw == 0)
        {
            return entry.Stored == 0;
        }

        if (stored)
        {
            return entry.Stored == entry.Raw;
        }

        return entry.Stored > 0 && entry.Stored < entry.Raw && entry.Raw % entry.Flags == 0;
    }

    private byte[] Section(int id)
    {
        if (!_directory.TryGetValue(id, out var entry))
        {
            throw new InvalidDataException($"The word image has no section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        var raw = (int)entry.Raw;
        if (raw == 0)
        {
            return [];
        }

        var source = _data.AsSpan((int)entry.Offset, (int)entry.Stored);
        if ((entry.Flags & ImageFormat.FlagStored) != 0)
        {
            return source.ToArray();
        }

        var stride = (int)(entry.Flags & 0xFF);
        var result = new byte[raw];
        var target = stride > 1 ? new byte[raw] : result;

        // The whole stored range must be one complete Brotli stream that fills the section exactly: a stream that ends
        // early or is followed by other bytes is not what the compiler wrote
        using var decoder = new BrotliDecoder();
        var status = decoder.Decompress(source, target, out var consumed, out var written);
        if (status != OperationStatus.Done || written != raw)
        {
            throw new InvalidDataException($"Section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the word image cannot be decompressed.");
        }

        if (consumed != source.Length)
        {
            throw new InvalidDataException($"Section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the word image has trailing bytes after its compressed stream.");
        }

        if (stride > 1)
        {
            MergePlanes(target, result, stride);
        }

        return result;
    }

    /// <summary>
    ///     Reverses the byte-plane split of a fixed-stride section: plane <c>p</c> holds byte <c>p</c> of every record.
    /// </summary>
    /// <param name="planes">The planar bytes.</param>
    /// <param name="result">Receives the interleaved records.</param>
    /// <param name="stride">The record size in bytes.</param>
    private static void MergePlanes(byte[] planes, byte[] result, int stride)
    {
        var n = result.Length / stride;
        for (var p = 0; p < stride; p++)
        {
            for (var i = 0; i < n; i++)
            {
                result[(i * stride) + p] = planes[(p * n) + i];
            }
        }
    }

    private readonly record struct SectionEntry(uint Raw, uint Stored, uint Offset, uint Flags);
}
