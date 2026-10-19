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
internal sealed class WordImage
{
    private const int MaxSpellingBytes = 255;

    private readonly Dictionary<int, SectionEntry> _directory = [];
    private readonly byte[] _data;
    private readonly byte[] _spellingBytes;
    private readonly uint[] _spellingOffset;
    private readonly uint[] _analysisStart;
    private readonly uint[] _rows;
    private readonly uint[] _scopeStart;
    private readonly uint[] _scopeItems;
    private readonly string[] _features;
    private readonly byte[] _entryLemmaBytes;
    private readonly uint[] _entryLemmaOffset;
    private readonly byte[] _entryPos;
    private readonly string?[] _entryLemmas;
    private readonly string[] _classIds;
    private readonly byte[] _classPos;
    private readonly int _foldCount;
    private uint[]? _fold;
    private uint[]? _entryFormStart;
    private uint[]? _entryForms;
    private byte[]? _classHash;

    private WordImage(byte[] data)
    {
        _data = data;
        ParseDirectory();
        var meta = Uints(ImageFormat.SectionMeta);
        if (meta.Length != ImageFormat.MetaLength)
        {
            throw new InvalidDataException("The word image has an invalid meta section.");
        }

        SpellingCount = Count(meta[ImageFormat.MetaSpellingCount]);
        EntryCount = Count(meta[ImageFormat.MetaEntryCount]);
        ClassCount = Count(meta[ImageFormat.MetaClassCount]);
        var rowCount = Count(meta[ImageFormat.MetaRowCount]);
        var scopeCount = Count(meta[ImageFormat.MetaScopeCount]);
        var featureCount = Count(meta[ImageFormat.MetaFeatureCount]);
        _foldCount = Count(meta[ImageFormat.MetaFoldCount]);

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

        _scopeStart = Uints(ImageFormat.SectionScopeStart);
        _scopeItems = Uints(ImageFormat.SectionScopeItems);
        if (_scopeStart.Length != scopeCount + 1 || _scopeStart[^1] != _scopeItems.Length)
        {
            throw new InvalidDataException("The word image has an invalid scope section.");
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
        _classIds = Strings(ImageFormat.SectionClassIdLength, ImageFormat.SectionClassIdBytes, ClassCount);
        _classPos = Section(ImageFormat.SectionClassPos);
        if (_classPos.Length != ClassCount || _classPos.Any(p => p > (byte)PartOfSpeech.Auxiliary))
        {
            throw new InvalidDataException("The word image has an invalid class section.");
        }

        for (var i = 1; i < _classIds.Length; i++)
        {
            if (CompareUtf8(_classIds[i - 1], _classIds[i]) >= 0)
            {
                throw new InvalidDataException("The word image has unordered class ids.");
            }
        }

        ValidateRows(rowCount, scopeCount, featureCount);
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
    ///     Gets the number of classes.
    /// </summary>
    public int ClassCount { get; }

    /// <summary>
    ///     Gets the SHA-256 of the data the image was compiled from.
    /// </summary>
    public ReadOnlySpan<byte> DataHash => _data.AsSpan(ImageFormat.DataHashOffset, ImageFormat.HashSize);

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
    ///     Gets the SHA-256 of the class table: for every class in index order the UTF-8 bytes of its id, a zero byte and
    ///     its part of speech byte. Models trained for this inventory carry the hash, so a model and an image that
    ///     disagree on the classes are detected when the model is loaded.
    /// </summary>
    public ReadOnlySpan<byte> ClassHash
    {
        get
        {
            var hash = _classHash;
            if (hash is null)
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                Span<byte> tail = stackalloc byte[2];
                for (var i = 0; i < _classIds.Length; i++)
                {
                    sha.AppendData(Encoding.UTF8.GetBytes(_classIds[i]));
                    tail[0] = 0;
                    tail[1] = _classPos[i];
                    sha.AppendData(tail);
                }

                hash = sha.GetHashAndReset();
                _classHash = hash;
            }

            return hash;
        }
    }

    /// <summary>
    ///     Gets the id of a class by index.
    /// </summary>
    /// <param name="index">The class index, 0 up to <see cref="ClassCount"/>.</param>
    /// <returns>The class id.</returns>
    public string ClassId(int index) => _classIds[index];

    /// <summary>
    ///     Gets the part of speech of a class.
    /// </summary>
    /// <param name="classId">The class id.</param>
    /// <returns>The part of speech, or null for an unknown class.</returns>
    public PartOfSpeech? ClassPos(string classId)
    {
        var index = ClassIndex(classId);
        return index < 0 ? null : (PartOfSpeech)_classPos[index];
    }

    /// <summary>
    ///     Finds a class by id.
    /// </summary>
    /// <param name="classId">The class id.</param>
    /// <returns>The class index, or -1 for an unknown class.</returns>
    public int ClassIndex(string classId)
    {
        ArgumentNullException.ThrowIfNull(classId);
        int lo = 0, hi = _classIds.Length;
        while (lo < hi)
        {
            var mid = (int)((uint)(lo + hi) >> 1);
            var c = CompareUtf8(_classIds[mid], classId);
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

    private static int Count(uint value) => value > int.MaxValue / 8 ? throw new InvalidDataException("The word image has an invalid count.") : (int)value;

    private static int CompareUtf8(string a, string b) =>
        Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));

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
        var result = new List<WordDetails>();
        foreach (var spelling in spellings)
        {
            for (var r = (int)_analysisStart[spelling]; r < (int)_analysisStart[spelling + 1]; r++)
            {
                var entry = (int)_rows[r * 2];
                var packed = _rows[(r * 2) + 1];
                var features = _features[(int)(packed & 0xFFFF)];
                var scope = (int)(packed >> 16);
                var lemma = EntryLemma(entry);
                var pos = (PartOfSpeech)_entryPos[entry];
                if (scope == 0)
                {
                    result.Add(new WordDetails(null, lemma, pos, features));
                    continue;
                }

                for (var k = _scopeStart[scope - 1]; k < _scopeStart[scope]; k++)
                {
                    result.Add(new WordDetails(_classIds[(int)_scopeItems[k]], lemma, pos, features));
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

    private void ValidateRows(int rowCount, int scopeCount, int featureCount)
    {
        for (var r = 0; r < rowCount; r++)
        {
            var packed = _rows[(r * 2) + 1];
            if (_rows[r * 2] >= EntryCount || (packed & 0xFFFF) >= featureCount || (packed >> 16) > scopeCount)
            {
                throw new InvalidDataException("The word image has an analysis row that refers outside its tables.");
            }
        }

        for (var i = 1; i < _scopeStart.Length; i++)
        {
            if (_scopeStart[i] < _scopeStart[i - 1])
            {
                throw new InvalidDataException("The word image has an invalid scope list.");
            }
        }

        if (_scopeItems.Any(c => c >= ClassCount))
        {
            throw new InvalidDataException("The word image has a scope that refers to an unknown class.");
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

    private void ParseDirectory()
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(8));
        if (count > 64 || ImageFormat.HeaderSize + (count * ImageFormat.DirectoryEntrySize) > _data.Length)
        {
            throw new InvalidDataException("The word image has an invalid directory.");
        }

        for (var i = 0; i < count; i++)
        {
            var d = _data.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            var entry = new SectionEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(d[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(d[16..]));
            if ((ulong)entry.Offset + entry.Stored > (ulong)_data.Length || entry.Raw > int.MaxValue || !_directory.TryAdd((int)BinaryPrimitives.ReadUInt32LittleEndian(d), entry))
            {
                throw new InvalidDataException("The word image has an invalid directory entry.");
            }
        }
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
            return raw == source.Length ? source.ToArray() : throw new InvalidDataException("The word image has an invalid stored section.");
        }

        var stride = (int)(entry.Flags & 0xFF);
        if (stride < 1 || raw % stride != 0)
        {
            throw new InvalidDataException("The word image has an invalid section stride.");
        }

        var result = new byte[raw];
        var target = stride > 1 ? new byte[raw] : result;
        if (!BrotliDecoder.TryDecompress(source, target, out var written) || written != raw)
        {
            throw new InvalidDataException($"Section {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the word image cannot be decompressed.");
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
