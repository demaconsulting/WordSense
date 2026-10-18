using System.Buffers.Binary;
using System.Collections.Frozen;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     One sense row of a cue table: the sense that the weights of one lemma column score.
/// </summary>
/// <param name="PartOfSpeech">The part of speech of the sense.</param>
/// <param name="Meaning">The pronunciation meaning of the sense, or null when the lemma is not a heteronym.</param>
/// <param name="Prior">The quantized log prior of the sense, in steps of 1/8 nat (zero or negative).</param>
internal sealed record CueSense(PartOfSpeech PartOfSpeech, string? Meaning, sbyte Prior);

/// <summary>
///     The statistics of one tabled word: its closed-class tags and its per-tag training counts.
/// </summary>
internal sealed class WordEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordEntry"/> class.
    /// </summary>
    /// <param name="closedMask">The tags the word may have regardless of the dictionary; 0 for an open-class word.</param>
    /// <param name="counts">The training count of the word for each of the <see cref="PosTag.Count"/> tags.</param>
    public WordEntry(int closedMask, int[] counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
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
    ///     Gets the training count of the word for each tag.
    /// </summary>
    public int[] Counts { get; }

    /// <summary>
    ///     Gets the sum of <see cref="Counts"/>.
    /// </summary>
    public int Total { get; }
}

/// <summary>
///     The cue table of one ambiguous lemma: its senses, the hashed context features that carry weights, and the
///     quantized weights.
/// </summary>
internal sealed class LemmaCue
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaCue"/> class.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senses">The senses, in the column order of the weights.</param>
    /// <param name="hashes">The ascending, distinct 16-bit feature hashes.</param>
    /// <param name="weights">The weights, feature-major: one value per sense for each hash.</param>
    public LemmaCue(string lemma, CueSense[] senses, ushort[] hashes, sbyte[] weights)
    {
        Lemma = lemma;
        Senses = senses;
        Hashes = hashes;
        Weights = weights;
    }

    /// <summary>
    ///     Gets the lemma.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the senses in weight-column order.
    /// </summary>
    public CueSense[] Senses { get; }

    /// <summary>
    ///     Gets the ascending feature hashes.
    /// </summary>
    public ushort[] Hashes { get; }

    /// <summary>
    ///     Gets the quantized log-likelihood weights, feature-major.
    /// </summary>
    public sbyte[] Weights { get; }

    /// <summary>
    ///     Finds the column of a lexicon sense.
    /// </summary>
    /// <param name="details">The sense, matched by part of speech and meaning.</param>
    /// <returns>The index of the sense, or -1 when the table has no such sense.</returns>
    public int RowOf(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        for (var i = 0; i < Senses.Length; i++)
        {
            if (Senses[i].PartOfSpeech == details.PartOfSpeech && string.Equals(Senses[i].Meaning, details.Meaning, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
///     The immutable statistics of the basic model, read once from the embedded table: the part-of-speech transition
///     probabilities, the word and suffix statistics of the tagger, and the context cue tables of the ambiguous lemmas.
/// </summary>
/// <remarks>
///     <para>
///     The table is a Brotli-compressed container that the repository's builder writes (<c>WSB1</c>); after
///     decompression every integer is a little-endian 7-bit varint unless noted, a double is eight little-endian
///     bytes and a string is a varint byte length followed by UTF-8. The layout is: the magic <c>WSB1</c>; seven
///     doubles (gamma, beta, lambda1, lambda2, lambda3, the weight of the tagger in the fusion, the inverse
///     temperature of the cues); the tag count; the unigram, bigram and trigram tag counts (the boundary is the last
///     index); the tabled words (word, flags byte with bit 0 = closed class, a 16-bit closed mask when closed, a 16-bit
///     presence mask and one count per set bit); the suffix entries (suffix and four counts for noun, verb, adjective
///     and adverb); and the cue lemmas (lemma, senses with part of speech, meaning and quantized prior, the delta-coded
///     ascending 16-bit feature hashes and one signed byte weight per feature and sense). Words, suffixes and lemmas
///     are in ascending ordinal order, so equal data always gives equal bytes.
///     </para>
///     <para>
///     An instance is immutable and safe for concurrent use. <see cref="Default"/> is the table embedded in the
///     assembly.
///     </para>
/// </remarks>
internal sealed class BasicTables
{
    /// <summary>
    ///     The name of the embedded resource that holds the table.
    /// </summary>
    public const string ResourceName = "DemaConsulting.WordSense.Basic.tables.bin";

    /// <summary>
    ///     The additive smoothing of the suffix statistics: a suffix probability is (count + 0.1) / (total + 0.4).
    /// </summary>
    public const double SuffixSmoothing = 0.1;

    private const double MinimumTagPrior = 1e-9;
    private const int MaximumSenses = 255;
    private const int MaximumLemmaLength = 1024;
    private static readonly byte[] Magic = "WSB1"u8.ToArray();

    private static readonly Lazy<BasicTables> DefaultTables = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly string[] _suffixKeys;
    private readonly double[][] _suffixProbabilities;

    private BasicTables(
        double[] parameters,
        int[] unigrams,
        int[] bigrams,
        int[] trigrams,
        FrozenDictionary<string, WordEntry> words,
        string[] suffixKeys,
        double[][] suffixProbabilities,
        FrozenDictionary<string, LemmaCue> cues)
    {
        Gamma = parameters[0];
        Beta = parameters[1];
        Lambda1 = parameters[2];
        Lambda2 = parameters[3];
        Lambda3 = parameters[4];
        HmmWeight = parameters[5];
        InvTemperature = parameters[6];
        Unigrams = unigrams;
        Bigrams = bigrams;
        Trigrams = trigrams;
        Words = words;
        _suffixKeys = suffixKeys;
        _suffixProbabilities = suffixProbabilities;
        Cues = cues;
        var derived = Derive(unigrams, bigrams, trigrams, Lambda1, Lambda2, Lambda3);
        Transition = derived.Transition;
        TagPrior = derived.TagPrior;
        SuffixFallback = derived.SuffixFallback;
    }

    /// <summary>
    ///     Gets the table embedded in the assembly, loaded on first use.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded table is missing or corrupt.</exception>
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
    ///     Gets the weight of the logarithm of the part-of-speech posterior in the fused score of a sense.
    /// </summary>
    public double HmmWeight { get; }

    /// <summary>
    ///     Gets the factor (the inverse of the temperature) that scales the cue score in the fused score of a sense.
    /// </summary>
    public double InvTemperature { get; }

    /// <summary>
    ///     Gets the unigram tag counts, indexed by tag with the boundary last.
    /// </summary>
    public int[] Unigrams { get; }

    /// <summary>
    ///     Gets the bigram tag counts, row-major by (previous, current).
    /// </summary>
    public int[] Bigrams { get; }

    /// <summary>
    ///     Gets the trigram tag counts, row-major by (before previous, previous, current).
    /// </summary>
    public int[] Trigrams { get; }

    /// <summary>
    ///     Gets the interpolated transition probabilities, row-major by (before previous, previous, current) with the
    ///     boundary as the last index of each dimension.
    /// </summary>
    public double[] Transition { get; }

    /// <summary>
    ///     Gets the relative frequency of each of the <see cref="PosTag.Count"/> tags in the training data.
    /// </summary>
    public double[] TagPrior { get; }

    /// <summary>
    ///     Gets the open-class distribution used for an unknown word without a known suffix.
    /// </summary>
    public double[] SuffixFallback { get; }

    /// <summary>
    ///     Gets the tabled words by lower-case key.
    /// </summary>
    public FrozenDictionary<string, WordEntry> Words { get; }

    /// <summary>
    ///     Gets the cue tables by lemma.
    /// </summary>
    public FrozenDictionary<string, LemmaCue> Cues { get; }

    /// <summary>
    ///     Gets the number of suffix entries.
    /// </summary>
    public int SuffixCount => _suffixKeys.Length;

    /// <summary>
    ///     Reads a table.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt.</exception>
    public static BasicTables Load(ReadOnlySpan<byte> blob)
    {
        var reader = new Reader(Decompress(blob));
        for (var i = 0; i < Magic.Length; i++)
        {
            if (reader.Remaining == 0 || reader.ReadByte() != Magic[i])
            {
                throw new InvalidDataException("The basic table has an unknown format.");
            }
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
        var cues = ReadCues(reader);
        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("The basic table has trailing bytes.");
        }

        return new BasicTables(parameters, unigrams, bigrams, trigrams, words, suffixKeys, suffixProbabilities, cues);
    }

    /// <summary>
    ///     Finds the open-class distribution of the longest known suffix of a word.
    /// </summary>
    /// <param name="key">The lower-case word.</param>
    /// <returns>The probabilities of noun, verb, adjective and adverb for the longest suffix of up to three characters that is tabled, or null when none is.</returns>
    public double[]? FindSuffix(ReadOnlySpan<char> key)
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
                    return _suffixProbabilities[middle];
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

        return null;
    }

    private static BasicTables LoadEmbedded()
    {
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded basic table is missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    private static byte[] Decompress(ReadOnlySpan<byte> blob)
    {
        try
        {
            using var input = new MemoryStream(blob.ToArray());
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            brotli.CopyTo(raw);
            return raw.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The basic table is corrupt.", ex);
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

            words.Add(word, new WordEntry(closedMask, counts));
        }

        return words.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static (string[] Keys, double[][] Probabilities) ReadSuffixes(Reader reader)
    {
        var count = reader.ReadCount();
        var keys = new string[count];
        var probabilities = new double[count][];
        for (var i = 0; i < count; i++)
        {
            keys[i] = reader.ReadString();
            if (keys[i].Length is < 1 or > 3 || (i > 0 && string.CompareOrdinal(keys[i - 1], keys[i]) >= 0))
            {
                throw new InvalidDataException("The basic table has unordered suffixes.");
            }

            var counts = reader.ReadVarints(PosTag.OpenClasses.Length);
            var total = counts.Sum(c => (double)c) + (SuffixSmoothing * PosTag.OpenClasses.Length);
            probabilities[i] = counts.Select(c => (c + SuffixSmoothing) / total).ToArray();
        }

        return (keys, probabilities);
    }

    private static FrozenDictionary<string, LemmaCue> ReadCues(Reader reader)
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
            var senseCount = reader.ReadVarint();
            if (senseCount is < 2 or > MaximumSenses)
            {
                throw new InvalidDataException("The basic table has an invalid sense count.");
            }

            var senses = new CueSense[senseCount];
            for (var s = 0; s < senseCount; s++)
            {
                var pos = reader.ReadByte();
                if (pos > (byte)PartOfSpeech.Interjection)
                {
                    throw new InvalidDataException("The basic table has an invalid part of speech.");
                }

                var meaning = reader.ReadString();
                senses[s] = new CueSense((PartOfSpeech)pos, meaning.Length == 0 ? null : meaning, (sbyte)reader.ReadByte());
            }

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

            var weights = new sbyte[checked(featureCount * senseCount)];
            for (var w = 0; w < weights.Length; w++)
            {
                weights[w] = (sbyte)reader.ReadByte();
            }

            cues.Add(lemma, new LemmaCue(lemma, senses, hashes, weights));
        }

        return cues.ToFrozenDictionary(StringComparer.Ordinal);
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
