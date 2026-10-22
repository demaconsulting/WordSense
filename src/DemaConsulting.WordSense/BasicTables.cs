using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     One column of a cue table: the analysis that the weights of the column score. The table names the analysis by
///     lemma, part of speech and features and the loader checks it against the analysis inventory of the word image.
/// </summary>
/// <remarks>
///     A column with empty features stands for every analysis of its lemma and part of speech; a column with features
///     (for example <c>Tense=Past</c>) stands for the analyses that carry all of its items.
/// </remarks>
/// <param name="Lemma">The lemma of the analysis, as written in the word image (a training lemma, which may be a form such as <c>left</c>).</param>
/// <param name="PartOfSpeech">The part of speech of the analysis.</param>
/// <param name="Features">The required feature items (<c>Key=Value</c> joined by <c>+</c>); empty when the column stands for every analysis of the lemma and part of speech.</param>
/// <param name="Prior">The quantized log prior of the analysis, in steps of 1/8 nat (zero or negative).</param>
internal sealed record CueAnalysis(string Lemma, PartOfSpeech PartOfSpeech, string Features, sbyte Prior);

/// <summary>
///     The statistics of one tabled word: its closed-class tags and its per-tag training counts.
/// </summary>
/// <remarks>
///     Immutable: the counts are held as an <see cref="ImmutableArray{T}"/>, so a consumer cannot change them.
/// </remarks>
internal sealed class WordEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordEntry"/> class.
    /// </summary>
    /// <param name="closedMask">The tags the word may have regardless of the dictionary; 0 for an open-class word.</param>
    /// <param name="counts">The training count of the word for each of the <see cref="PosTag.Count"/> tags.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="counts"/> is the default array.</exception>
    /// <exception cref="InvalidDataException">Thrown when the counts add up to more than a 32-bit integer holds.</exception>
    public WordEntry(int closedMask, ImmutableArray<int> counts)
    {
        if (counts.IsDefault)
        {
            throw new ArgumentException("The counts must not be the default array.", nameof(counts));
        }

        ClosedMask = closedMask;
        Counts = counts;
        long total = 0;
        foreach (var count in counts)
        {
            total += count;
        }

        if (total > int.MaxValue)
        {
            throw new InvalidDataException("The basic table has a word count that is too large.");
        }

        Total = (int)total;
    }

    /// <summary>
    ///     Gets the bit mask (bit t is tag t) of the tags the word may have regardless of the dictionary.
    /// </summary>
    public int ClosedMask { get; }

    /// <summary>
    ///     Gets the training count of the word for each tag, as a read-only view.
    /// </summary>
    public ImmutableArray<int> Counts { get; }

    /// <summary>
    ///     Gets the sum of <see cref="Counts"/>.
    /// </summary>
    public int Total { get; }
}

/// <summary>
///     The cue table of one ambiguous training lemma: its analyses as columns, the hashed context features that carry
///     weights, and the quantized weights.
/// </summary>
/// <remarks>
///     The table is keyed by a training lemma (or by a form that spans lemmas, such as <c>left</c>), so its columns may
///     name analyses of several lemmas. The feature items of each column are split once at construction so that
///     <see cref="RowOf"/> does not allocate. Immutable: the columns, hashes and weights are held as
///     <see cref="ImmutableArray{T}"/>, so a consumer cannot change them.
/// </remarks>
internal sealed class LemmaCue
{
    private readonly string[][] _items;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaCue"/> class.
    /// </summary>
    /// <param name="lemma">The training lemma that keys the table.</param>
    /// <param name="analyses">The analyses, in the column order of the weights.</param>
    /// <param name="hashes">The ascending, distinct 16-bit feature hashes.</param>
    /// <param name="weights">The weights, feature-major: one value per analysis for each hash.</param>
    /// <exception cref="ArgumentException">Thrown when an array is the default array.</exception>
    public LemmaCue(string lemma, ImmutableArray<CueAnalysis> analyses, ImmutableArray<ushort> hashes, ImmutableArray<sbyte> weights)
    {
        if (analyses.IsDefault || hashes.IsDefault || weights.IsDefault)
        {
            throw new ArgumentException("The analyses, hashes and weights must not be the default array.");
        }

        Lemma = lemma;
        Analyses = analyses;
        Hashes = hashes;
        Weights = weights;
        _items = [.. analyses.Select(a => a.Features.Length == 0 ? [] : a.Features.Split('+'))];
    }

    /// <summary>
    ///     Gets the training lemma that keys the table.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the analyses in weight-column order, as a read-only view.
    /// </summary>
    public ImmutableArray<CueAnalysis> Analyses { get; }

    /// <summary>
    ///     Gets the ascending feature hashes, as a read-only view.
    /// </summary>
    public ImmutableArray<ushort> Hashes { get; }

    /// <summary>
    ///     Gets the quantized log-likelihood weights, feature-major, as a read-only view.
    /// </summary>
    public ImmutableArray<sbyte> Weights { get; }

    /// <summary>
    ///     Finds the column of an analysis of the lexicon.
    /// </summary>
    /// <remarks>
    ///     Among the columns of the same lemma and part of speech whose feature items are all contained in the feature
    ///     items of the analysis, the one with the most items wins (the first on a tie); a column with empty features
    ///     always qualifies, so it is the fallback of every analysis of its lemma and part of speech. The column of the
    ///     most specific features is preferred because it was trained on exactly that use (the past of <c>read</c>).
    /// </remarks>
    /// <param name="details">The analysis (lemma, part of speech and features).</param>
    /// <returns>The index of the column, or -1 when the table has no column for the analysis.</returns>
    public int RowOf(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var best = -1;
        var bestItems = -1;
        for (var i = 0; i < Analyses.Length; i++)
        {
            var column = Analyses[i];
            if (column.PartOfSpeech != details.PartOfSpeech
                || _items[i].Length <= bestItems
                || !string.Equals(column.Lemma, details.Lemma, StringComparison.Ordinal)
                || !HasAll(details.Features, _items[i]))
            {
                continue;
            }

            best = i;
            bestItems = _items[i].Length;
        }

        return best;
    }

    [SuppressMessage("Major Code Smell", "S3267:Loops should be simplified using the \"All\" LINQ method", Justification = "Hot path of every candidate analysis; a lambda that captures the features would allocate per call.")]
    private static bool HasAll(string features, string[] items)
    {
        foreach (var item in items)
        {
            if (!HasItem(features, item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasItem(string features, string item)
    {
        // Walk the '+'-separated items without allocating
        var rest = features.AsSpan();
        while (rest.Length > 0)
        {
            var end = rest.IndexOf('+');
            var current = end < 0 ? rest : rest[..end];
            if (current.SequenceEqual(item))
            {
                return true;
            }

            rest = end < 0 ? default : rest[(end + 1)..];
        }

        return false;
    }
}

/// <summary>
///     The immutable statistics of the basic model, read once from the embedded table: the part-of-speech transition
///     probabilities, the word and suffix statistics of the tagger, and the context cue tables of the ambiguous lemmas.
/// </summary>
/// <remarks>
///     <para>
///     The table is a Brotli-compressed container that the repository's builder writes (<c>WSB3</c>); after
///     decompression every integer is a little-endian 7-bit varint unless noted, a double is eight little-endian
///     bytes and a string is a varint byte length followed by UTF-8. The layout is: the magic <c>WSB3</c>; the
///     32-byte analysis inventory hash (<c>WordImage.InventoryHash</c>, the hash of the analysis, sense and group sections
///     of the word image the table was trained for); seven doubles (gamma, beta, lambda1, lambda2, lambda3, the weight
///     of the tagger in the fusion, the inverse temperature of the cues); the tag count; the unigram, bigram and
///     trigram tag counts (the boundary is the last index); the tabled words (word, flags byte with bit 0 = closed
///     class, a 16-bit closed mask when closed, a 16-bit presence mask and one count per set bit); the suffix entries
///     (suffix and four counts for noun, verb, adjective and adverb); and the cue lemmas (the training lemma, the
///     analysis count, per analysis the lemma string, the part-of-speech byte, the features string and the signed
///     quantized prior byte, the delta-coded ascending 16-bit feature hashes and one signed byte weight per feature
///     and analysis). Words, suffixes and lemmas are in ascending ordinal order, so equal data always gives equal
///     bytes. The load fails when the hash differs from the word image, when an analysis does not exist in the word
///     image, or when a lemma table names an analysis twice.
///     </para>
///     <para>
///     An instance is immutable and safe for concurrent use: the statistics are exposed only as
///     <see cref="ImmutableArray{T}"/> views and frozen dictionaries of immutable entries, so no consumer can change
///     what another one sees. <see cref="Default"/> is the table embedded in the assembly. The compressed blob must be
///     exactly one Brotli stream; bytes after its end are rejected like any other trailing data.
///     </para>
/// </remarks>
internal sealed class BasicTables
{
    /// <summary>
    ///     The name of the embedded resource that holds the table.
    /// </summary>
    public const string ResourceName = "DemaConsulting.WordSense.basic.tables.bin";

    /// <summary>
    ///     The additive smoothing of the suffix statistics: a suffix probability is (count + 0.1) / (total + 0.4).
    /// </summary>
    public const double SuffixSmoothing = 0.1;

    private const double MinimumTagPrior = 1e-9;
    private const int MaximumAnalyses = 255;
    private const int MaximumLemmaLength = 1024;
    private const int HashSize = 32;
    private const int InitialBufferSize = 1 << 16;
    private static readonly byte[] Magic = "WSB3"u8.ToArray();

    private static readonly Lazy<BasicTables> DefaultTables = new(LoadEmbeddedResource, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly string[] _suffixKeys;
    private readonly ImmutableArray<double>[] _suffixProbabilities;

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor of an immutable table whose fields are all supplied by the single reader.")]
    private BasicTables(
        double[] parameters,
        int[] unigrams,
        int[] bigrams,
        int[] trigrams,
        FrozenDictionary<string, WordEntry> words,
        string[] suffixKeys,
        ImmutableArray<double>[] suffixProbabilities,
        FrozenDictionary<string, LemmaCue> cues)
    {
        Gamma = parameters[0];
        Beta = parameters[1];
        Lambda1 = parameters[2];
        Lambda2 = parameters[3];
        Lambda3 = parameters[4];
        HmmWeight = parameters[5];
        InvTemperature = parameters[6];

        // The reader hands over arrays that nothing else references, so they are wrapped without a copy and the table
        // exposes only read-only views of them
        Unigrams = ImmutableCollectionsMarshal.AsImmutableArray(unigrams);
        Bigrams = ImmutableCollectionsMarshal.AsImmutableArray(bigrams);
        Trigrams = ImmutableCollectionsMarshal.AsImmutableArray(trigrams);
        Words = words;
        _suffixKeys = suffixKeys;
        _suffixProbabilities = suffixProbabilities;
        Cues = cues;
        var derived = Derive(unigrams, bigrams, trigrams, Lambda1, Lambda2, Lambda3);
        Transition = ImmutableCollectionsMarshal.AsImmutableArray(derived.Transition);
        TagPrior = ImmutableCollectionsMarshal.AsImmutableArray(derived.TagPrior);
        SuffixFallback = ImmutableCollectionsMarshal.AsImmutableArray(derived.SuffixFallback);
    }

    /// <summary>
    ///     Gets the table embedded in the assembly, loaded on first use.
    /// </summary>
    /// <exception cref="ModelIntegrityException">Thrown on first access when the embedded table is missing or corrupt (the inner exception is the original <see cref="InvalidDataException"/>).</exception>
    public static BasicTables Default => DefaultTables.Value;

    /// <summary>
    ///     Gets the exponent that sharpens the emission probabilities of a tabled word.
    /// </summary>
    public double Gamma { get; }

    /// <summary>
    ///     Gets the weight, in pseudo counts, of the tag prior in the emission probabilities of a tabled word.
    /// </summary>
    public double Beta { get; }

    /// <summary>
    ///     Gets the weight of the unigram tag probability in the transition interpolation.
    /// </summary>
    public double Lambda1 { get; }

    /// <summary>
    ///     Gets the weight of the bigram tag probability in the transition interpolation.
    /// </summary>
    public double Lambda2 { get; }

    /// <summary>
    ///     Gets the weight of the trigram tag probability in the transition interpolation.
    /// </summary>
    public double Lambda3 { get; }

    /// <summary>
    ///     Gets the weight of the logarithm of the part-of-speech posterior in the fused score of an analysis.
    /// </summary>
    public double HmmWeight { get; }

    /// <summary>
    ///     Gets the factor (the inverse of the temperature) that scales the cue score in the fused score of an analysis.
    /// </summary>
    public double InvTemperature { get; }

    /// <summary>
    ///     Gets the unigram tag counts, indexed by tag with the boundary last, as a read-only view.
    /// </summary>
    public ImmutableArray<int> Unigrams { get; }

    /// <summary>
    ///     Gets the bigram tag counts, row-major by (previous, current), as a read-only view.
    /// </summary>
    public ImmutableArray<int> Bigrams { get; }

    /// <summary>
    ///     Gets the trigram tag counts, row-major by (before previous, previous, current), as a read-only view.
    /// </summary>
    public ImmutableArray<int> Trigrams { get; }

    /// <summary>
    ///     Gets the interpolated transition probabilities, row-major by (before previous, previous, current) with the
    ///     boundary as the last index of each dimension, as a read-only view.
    /// </summary>
    public ImmutableArray<double> Transition { get; }

    /// <summary>
    ///     Gets the relative frequency of each of the <see cref="PosTag.Count"/> tags in the training data, as a
    ///     read-only view.
    /// </summary>
    public ImmutableArray<double> TagPrior { get; }

    /// <summary>
    ///     Gets the open-class distribution used for an unknown word without a known suffix, as a read-only view.
    /// </summary>
    public ImmutableArray<double> SuffixFallback { get; }

    /// <summary>
    ///     Gets the tabled words by lower-case key.
    /// </summary>
    public FrozenDictionary<string, WordEntry> Words { get; }

    /// <summary>
    ///     Gets the cue tables by training lemma (or by a form that spans lemmas).
    /// </summary>
    public FrozenDictionary<string, LemmaCue> Cues { get; }

    /// <summary>
    ///     Gets the number of suffix entries.
    /// </summary>
    public int SuffixCount => _suffixKeys.Length;

    /// <summary>
    ///     Reads a table that belongs to the embedded word image.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt or not for the embedded word image.</exception>
    public static BasicTables Load(ReadOnlySpan<byte> blob) => Load(blob, WordLexicon.Current);

    /// <summary>
    ///     Reads a table.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <param name="image">The word image the table must belong to.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt or not for the word image.</exception>
    public static BasicTables Load(ReadOnlySpan<byte> blob, WordImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Load(blob, image.InventoryHash, image.AnalysisExists);
    }

    /// <summary>
    ///     Reads a table against an explicit analysis inventory. The overload that takes the word image delegates here;
    ///     the tests use it with a small inventory so that they do not need an image.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <param name="inventoryHash">The 32-byte analysis inventory hash that the table must carry.</param>
    /// <param name="analysisExists">Tells whether an analysis (lemma, part of speech, required features) is in the inventory.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt, carries another inventory hash, names an unknown analysis or names an analysis twice in one lemma table.</exception>
    internal static BasicTables Load(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> inventoryHash, Func<string, PartOfSpeech, string, bool> analysisExists)
    {
        ArgumentNullException.ThrowIfNull(analysisExists);
        var reader = new Reader(Decompress(blob));
        for (var i = 0; i < Magic.Length; i++)
        {
            if (reader.Remaining == 0 || reader.ReadByte() != Magic[i])
            {
                throw new InvalidDataException("The basic table has an unknown format.");
            }
        }

        var hash = new byte[HashSize];
        for (var i = 0; i < hash.Length; i++)
        {
            hash[i] = reader.ReadByte();
        }

        if (!hash.AsSpan().SequenceEqual(inventoryHash))
        {
            throw new InvalidDataException(
                $"The basic table was trained for another analysis inventory: its inventory hash is {Convert.ToHexString(hash)}, the word image has {Convert.ToHexString(inventoryHash)}. Rebuild the table.");
        }

        var parameters = new double[7];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i] = reader.ReadDouble();
        }

        ValidateParameters(parameters);
        if (reader.ReadVarint() != PosTag.Count)
        {
            throw new InvalidDataException("The basic table has a different number of tags.");
        }

        var unigrams = reader.ReadVarints(PosTag.Extended);
        var bigrams = reader.ReadVarints(PosTag.Extended * PosTag.Extended);
        var trigrams = reader.ReadVarints(PosTag.Extended * PosTag.Extended * PosTag.Extended);
        var words = ReadWords(reader);
        var (suffixKeys, suffixProbabilities) = ReadSuffixes(reader);
        var cues = ReadCues(reader, analysisExists);
        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("The basic table has trailing bytes.");
        }

        return new BasicTables(parameters, unigrams, bigrams, trigrams, words, suffixKeys, suffixProbabilities, cues);
    }

    /// <summary>
    ///     Finds the cue table that scores a word: the table keyed by the normalized word (a form that spans lemmas, such
    ///     as <c>left</c> or <c>bases</c>, has a table of its own) and otherwise the table keyed by the lemma of its first
    ///     analysis.
    /// </summary>
    /// <param name="word">The normalized (lower-case) word.</param>
    /// <param name="analyses">The analyses of the word in lexicon order; the first (most frequent) one names the lemma.</param>
    /// <returns>The cue table, or null when the word has none.</returns>
    public LemmaCue? FindCue(string word, ImmutableArray<WordDetails> analyses)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (Cues.TryGetValue(word, out var byWord))
        {
            return byWord;
        }

        return analyses.IsDefaultOrEmpty ? null : Cues.GetValueOrDefault(analyses[0].Lemma);
    }

    /// <summary>
    ///     Finds the open-class distribution of the longest known suffix of a word.
    /// </summary>
    /// <param name="key">The lower-case word.</param>
    /// <param name="probabilities">Receives the probabilities of noun, verb, adjective and adverb for the longest suffix of up to three characters that is tabled, as a read-only view; the default array when none is.</param>
    /// <returns>True when a suffix of the word is tabled.</returns>
    public bool TryFindSuffix(ReadOnlySpan<char> key, out ImmutableArray<double> probabilities)
    {
        for (var length = Math.Min(3, key.Length); length >= 1; length--)
        {
            var suffix = key[^length..];
            var low = 0;
            var high = _suffixKeys.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var order = suffix.SequenceCompareTo(_suffixKeys[middle].AsSpan());
                if (order == 0)
                {
                    probabilities = _suffixProbabilities[middle];
                    return true;
                }

                if (order < 0)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }
        }

        probabilities = default;
        return false;
    }

    /// <summary>
    ///     Reads the table from the stream of the embedded resource, reporting a missing or damaged table as <see cref="ModelIntegrityException"/>.
    /// </summary>
    /// <param name="stream">The resource stream, or null when the resource is missing.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ModelIntegrityException">Thrown when the stream is null or the table is corrupt or not for the embedded word image; the original <see cref="InvalidDataException"/> is the inner exception.</exception>
    internal static BasicTables LoadEmbedded(Stream? stream)
    {
        if (stream is null)
        {
            throw new ModelIntegrityException(
                "The embedded basic table is missing from the assembly.",
                new InvalidDataException("The embedded basic table is missing from the assembly."));
        }

        try
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), WordLexicon.Current);
        }
        catch (InvalidDataException ex)
        {
            throw new ModelIntegrityException($"The embedded basic table is damaged: {ex.Message}", ex);
        }
    }

    private static BasicTables LoadEmbeddedResource()
    {
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(ResourceName);
        return LoadEmbedded(stream);
    }

    /// <summary>
    ///     Decompresses a blob that must be exactly one complete Brotli stream.
    /// </summary>
    /// <param name="blob">The compressed bytes.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is not a complete Brotli stream, or when bytes follow the end of the stream.</exception>
    private static byte[] Decompress(ReadOnlySpan<byte> blob)
    {
        using var decoder = new BrotliDecoder();
        var buffer = new byte[Math.Max(InitialBufferSize, blob.Length * 4)];
        var consumed = 0;
        var written = 0;
        while (true)
        {
            var status = decoder.Decompress(blob[consumed..], buffer.AsSpan(written), out var read, out var wrote);
            consumed += read;
            written += wrote;
            switch (status)
            {
                case OperationStatus.Done when consumed == blob.Length:
                    return buffer.AsSpan(0, written).ToArray();
                case OperationStatus.Done:
                    throw new InvalidDataException("The basic table has trailing bytes after its compressed stream.");
                case OperationStatus.DestinationTooSmall:
                    // Output is larger than guessed: double the buffer and continue the same stream
                    Array.Resize(ref buffer, buffer.Length * 2);
                    break;
                default:
                    throw new InvalidDataException("The basic table is corrupt.");
            }
        }
    }

    private static void ValidateParameters(double[] p)
    {
        if (p.Any(v => !double.IsFinite(v) || v > 1e6) || p[0] <= 0 || p[1] <= 0 || p[2] < 0 || p[3] < 0 || p[4] < 0 || p[5] < 0 || p[6] < 0)
        {
            throw new InvalidDataException("The basic table has invalid hyper-parameters.");
        }
    }

    private static FrozenDictionary<string, WordEntry> ReadWords(Reader reader)
    {
        var count = reader.ReadCount();
        var words = new Dictionary<string, WordEntry>(count, StringComparer.Ordinal);
        string? previous = null;
        const int allTags = (1 << PosTag.Count) - 1;
        for (var i = 0; i < count; i++)
        {
            var word = reader.ReadString();
            if (word.Length == 0 || (previous is not null && string.CompareOrdinal(previous, word) >= 0))
            {
                throw new InvalidDataException("The basic table has unordered words.");
            }

            previous = word;
            var flags = reader.ReadByte();
            if (flags > 1)
            {
                throw new InvalidDataException("The basic table has invalid word flags.");
            }

            var closedMask = flags == 1 ? reader.ReadUInt16() : 0;
            var presence = reader.ReadUInt16();
            if ((closedMask & ~allTags) != 0 || (presence & ~allTags) != 0)
            {
                throw new InvalidDataException("The basic table has an invalid tag mask.");
            }

            var counts = ReadWordCounts(reader, presence);

            words.Add(word, new WordEntry(closedMask, ImmutableCollectionsMarshal.AsImmutableArray(counts)));
        }

        return words.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static int[] ReadWordCounts(Reader reader, int presence)
    {
        var counts = new int[PosTag.Count];
        for (var t = 0; t < PosTag.Count; t++)
        {
            if ((presence & (1 << t)) == 0)
            {
                continue;
            }

            counts[t] = reader.ReadVarint();
            if (counts[t] <= 0)
            {
                throw new InvalidDataException("The basic table has a non-positive word count.");
            }
        }

        return counts;
    }

    private static (string[] Keys, ImmutableArray<double>[] Probabilities) ReadSuffixes(Reader reader)
    {
        var count = reader.ReadCount();
        var keys = new string[count];
        var probabilities = new ImmutableArray<double>[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = reader.ReadString();
            if (keys[i].Length is < 1 or > 3 || (i > 0 && string.CompareOrdinal(keys[i - 1], keys[i]) >= 0))
            {
                throw new InvalidDataException("The basic table has unordered suffixes.");
            }

            var counts = reader.ReadVarints(PosTag.OpenClasses.Length);
            var total = counts.Sum(c => (double)c) + (SuffixSmoothing * PosTag.OpenClasses.Length);
            probabilities[i] = [.. counts.Select(c => (c + SuffixSmoothing) / total)];
        }

        return (keys, probabilities);
    }

    private static FrozenDictionary<string, LemmaCue> ReadCues(Reader reader, Func<string, PartOfSpeech, string, bool> analysisExists)
    {
        var count = reader.ReadCount();
        var cues = new Dictionary<string, LemmaCue>(count, StringComparer.Ordinal);
        string? previous = null;
        for (var i = 0; i < count; i++)
        {
            var lemma = reader.ReadString();
            if (lemma.Length is 0 or > MaximumLemmaLength || (previous is not null && string.CompareOrdinal(previous, lemma) >= 0))
            {
                throw new InvalidDataException("The basic table has unordered cue lemmas.");
            }

            previous = lemma;
            var analysisCount = reader.ReadVarint();
            if (analysisCount is < 2 or > MaximumAnalyses)
            {
                throw new InvalidDataException("The basic table has an invalid analysis count.");
            }

            var analyses = ReadCueAnalyses(reader, analysisExists, lemma, analysisCount);
            var hashes = ReadFeatureHashes(reader);

            // Size the weight block in 64-bit arithmetic and check it against the remaining bytes before
            // allocating, so a corrupt hash count reports truncation rather than overflowing the multiplication
            var weightCount = (long)hashes.Length * analysisCount;
            if (weightCount > reader.Remaining)
            {
                throw new InvalidDataException("The basic table is truncated: a cue's weights extend past the end of the data.");
            }

            var weights = new sbyte[weightCount];
            for (var w = 0; w < weights.Length; w++)
            {
                weights[w] = (sbyte)reader.ReadByte();
            }

            cues.Add(lemma, new LemmaCue(
                lemma,
                ImmutableCollectionsMarshal.AsImmutableArray(analyses),
                ImmutableCollectionsMarshal.AsImmutableArray(hashes),
                ImmutableCollectionsMarshal.AsImmutableArray(weights)));
        }

        return cues.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static CueAnalysis[] ReadCueAnalyses(Reader reader, Func<string, PartOfSpeech, string, bool> analysisExists, string table, int analysisCount)
    {
        var analyses = new CueAnalysis[analysisCount];
        var seen = new HashSet<(string, PartOfSpeech, string)>();
        for (var s = 0; s < analysisCount; s++)
        {
            var lemma = reader.ReadString();
            var posByte = reader.ReadByte();
            var features = reader.ReadString();
            var prior = (sbyte)reader.ReadByte();
            if (!Enum.IsDefined((PartOfSpeech)posByte))
            {
                throw new InvalidDataException("The basic table has an invalid part of speech.");
            }

            var pos = (PartOfSpeech)posByte;

            // Every column must name an analysis of the word image, and no analysis may be a column twice
            if (!analysisExists(lemma, pos, features))
            {
                throw new InvalidDataException($"The basic table names the analysis '{lemma}' ({pos}, '{features}') in the cue table '{table}', which is not in the word image.");
            }

            if (!seen.Add((lemma, pos, features)))
            {
                throw new InvalidDataException($"The basic table names the analysis '{lemma}' ({pos}, '{features}') twice in the cue table '{table}'.");
            }

            analyses[s] = new CueAnalysis(lemma, pos, features, prior);
        }

        return analyses;
    }

    private static ushort[] ReadFeatureHashes(Reader reader)
    {
        var featureCount = reader.ReadCount();
        var hashes = new ushort[featureCount];
        var last = -1;
        for (var f = 0; f < featureCount; f++)
        {
            var value = f == 0 ? reader.ReadVarint() : last + reader.ReadVarint();
            if (value > ushort.MaxValue || value <= last)
            {
                throw new InvalidDataException("The basic table has unordered feature hashes.");
            }

            hashes[f] = (ushort)value;
            last = value;
        }

        return hashes;
    }

    private static (double[] Transition, double[] TagPrior, double[] SuffixFallback) Derive(int[] c1, int[] c2, int[] c3, double l1, double l2, double l3)
    {
        const int k = PosTag.Extended;
        var total1 = c1.Sum(c => (double)c);
        var tagTotal = c1.Take(PosTag.Count).Sum(c => (double)c);
        if (total1 <= 0 || tagTotal <= 0)
        {
            throw new InvalidDataException("The basic table has no tag counts.");
        }

        var transition = new double[k * k * k];
        for (var a = 0; a < k; a++)
        {
            for (var b = 0; b < k; b++)
            {
                double row2 = 0;
                double row3 = 0;
                for (var c = 0; c < k; c++)
                {
                    row2 += c2[(b * k) + c];
                    row3 += c3[(((a * k) + b) * k) + c];
                }

                row2 = Math.Max(row2, 1);
                row3 = Math.Max(row3, 1);
                for (var c = 0; c < k; c++)
                {
                    var index = (((a * k) + b) * k) + c;
                    transition[index] = (l3 * c3[index] / row3) + (l2 * c2[(b * k) + c] / row2) + (l1 * c1[c] / total1);
                }
            }
        }

        var prior = new double[PosTag.Count];
        for (var t = 0; t < prior.Length; t++)
        {
            prior[t] = Math.Max(c1[t] / tagTotal, MinimumTagPrior);
        }

        var open = PosTag.OpenClasses.Select(t => (double)c1[t]).ToArray();
        var openTotal = open.Sum();
        var fallback = open.Select(v => openTotal > 0 ? v / openTotal : 1.0 / open.Length).ToArray();
        return (transition, prior, fallback);
    }

    /// <summary>
    ///     Reads primitive values from a byte array, throwing <see cref="InvalidDataException"/> on truncation.
    /// </summary>
    /// <param name="data">The bytes.</param>
    private sealed class Reader(byte[] data)
    {
        /// <summary>
        ///     UTF-8 decoding that throws on malformed bytes instead of substituting replacement characters.
        /// </summary>
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private int _position;

        /// <summary>
        ///     Gets the number of bytes not yet read.
        /// </summary>
        public int Remaining => data.Length - _position;

        /// <summary>
        ///     Reads one byte.
        /// </summary>
        /// <returns>The byte.</returns>
        public byte ReadByte()
        {
            if (_position >= data.Length)
            {
                throw Truncated();
            }

            return data[_position++];
        }

        /// <summary>
        ///     Reads an unsigned 7-bit varint that fits a non-negative 32-bit integer.
        /// </summary>
        /// <returns>The value.</returns>
        public int ReadVarint()
        {
            var result = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var b = ReadByte();
                if (shift == 28 && (b & 0xF8) != 0)
                {
                    throw new InvalidDataException("The basic table has an oversized number.");
                }

                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("The basic table has an oversized number.");
        }

        /// <summary>
        ///     Reads a varint that counts items of at least one byte each, so it cannot exceed the remaining bytes.
        /// </summary>
        /// <returns>The count.</returns>
        public int ReadCount()
        {
            var count = ReadVarint();
            return count <= Remaining ? count : throw Truncated();
        }

        /// <summary>
        ///     Reads several varints.
        /// </summary>
        /// <param name="count">The number of values.</param>
        /// <returns>The values.</returns>
        public int[] ReadVarints(int count)
        {
            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ReadVarint();
            }

            return values;
        }

        /// <summary>
        ///     Reads an unsigned 16-bit little-endian integer.
        /// </summary>
        /// <returns>The value.</returns>
        public int ReadUInt16()
        {
            var low = ReadByte();
            return low | (ReadByte() << 8);
        }

        /// <summary>
        ///     Reads a little-endian double.
        /// </summary>
        /// <returns>The value.</returns>
        public double ReadDouble()
        {
            if (Remaining < sizeof(double))
            {
                throw Truncated();
            }

            var value = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(_position, sizeof(double)));
            _position += sizeof(double);
            return value;
        }

        /// <summary>
        ///     Reads a length-prefixed UTF-8 string.
        /// </summary>
        /// <returns>The string.</returns>
        public string ReadString()
        {
            var length = ReadVarint();
            if (length > Remaining)
            {
                throw Truncated();
            }

            try
            {
                var value = StrictUtf8.GetString(data, _position, length);
                _position += length;
                return value;
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("The basic table has an invalid text.", ex);
            }
        }

        private static InvalidDataException Truncated() => new("The basic table is truncated.");
    }
}
