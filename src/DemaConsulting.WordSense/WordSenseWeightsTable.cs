using System.Security.Cryptography;

namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: the trained classifiers of one encoder model: the feature centering mean and, for every context lemma
///     of a <see cref="WordSenseTable"/>, the <see cref="SenseWeights"/> that choose between its senses, plus a
///     versioned binary serialization.
/// </summary>
/// <remarks>
///     <para>
///     A weights table belongs to exactly one encoder model, named by <see cref="ModelId"/> and the SHA-256 of the
///     model file (<see cref="ModelSha256"/>), and to exactly one lexicon table, named by <see cref="LexiconSha256"/>.
///     Each model package embeds its own weights table; the classifier refuses a table that names another model or
///     another lexicon. Instances are immutable and thread-safe provided callers treat the arrays they expose as
///     read-only.
///     </para>
///     <para>
///     Binary layout (little-endian): magic <c>WSNW</c>, u16 version (1), string model id, 32-byte model SHA-256,
///     32-byte lexicon table SHA-256, u32 dimension, f32 mean[dimension], u32 lemma count, lemmas in ordinal order
///     (string lemma, u16 sense count, u8 kind, f32 weights, f32 bias, f32 abstain margin), and a trailing 32-byte
///     SHA-256 of everything before it. The reader checks the digest first and caps every count, so malformed input
///     fails with <see cref="WordSenseTableFormatException"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var mean = new float[2];
///     var weights = new Dictionary&lt;string, SenseWeights&gt;
///     {
///         ["record"] = new SenseWeights(LemmaModelKind.Binary, [1f, -1f], [0f], 0.1f, 2),
///     };
///     var table = new WordSenseWeightsTable(
///         "my-model", new string('0', 64), new string('1', 64), 2, mean, weights);
///     WordSenseWeightsTable copy = WordSenseWeightsTable.Read(table.ToArray());
///     </code>
/// </example>
public sealed class WordSenseWeightsTable
{
    /// <summary>
    ///     The four magic bytes that start every weights table.
    /// </summary>
    private static readonly byte[] Magic = "WSNW"u8.ToArray();

    /// <summary>
    ///     The only serialization version this library reads and writes.
    /// </summary>
    private const ushort CurrentVersion = 1;

    /// <summary>
    ///     The length of a SHA-256 digest in bytes.
    /// </summary>
    private const int HashLength = 32;

    /// <summary>
    ///     The largest feature dimension accepted from a file.
    /// </summary>
    private const int MaxDimension = 4096;

    /// <summary>
    ///     The largest lemma count accepted from a file.
    /// </summary>
    private const int MaxLemmas = 100_000;

    /// <summary>
    ///     The largest sense count per lemma accepted from a file.
    /// </summary>
    private const int MaxSenses = 64;

    /// <summary>
    ///     The fewest bytes a serialized lemma can take: name length, sense count, kind, one weight, one bias and the margin.
    /// </summary>
    private const int MinLemmaBytes = 17;

    private readonly Dictionary<string, SenseWeights> _weights;
    private readonly string[] _lemmas;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseWeightsTable"/> class.
    /// </summary>
    /// <param name="modelId">The identifier of the encoder model the weights were trained with.</param>
    /// <param name="modelSha256">The lower-case hexadecimal SHA-256 of that encoder model's file.</param>
    /// <param name="lexiconSha256">The <see cref="WordSenseTable.Sha256"/> of the lexicon table the weights belong to.</param>
    /// <param name="dimension">The feature dimension.</param>
    /// <param name="mean">The mean embedding used to center features; length <paramref name="dimension"/>.</param>
    /// <param name="weights">The classifier of every context lemma by lemma; every one has <paramref name="dimension"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the content is inconsistent.</exception>
    public WordSenseWeightsTable(
        string modelId,
        string modelSha256,
        string lexiconSha256,
        int dimension,
        float[] mean,
        IReadOnlyDictionary<string, SenseWeights> weights)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(modelSha256);
        ArgumentNullException.ThrowIfNull(lexiconSha256);
        ArgumentNullException.ThrowIfNull(mean);
        ArgumentNullException.ThrowIfNull(weights);

        if (modelSha256.Length != HashLength * 2 || !modelSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The model SHA-256 must be 64 hexadecimal characters.", nameof(modelSha256));
        }

        if (lexiconSha256.Length != HashLength * 2 || !lexiconSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The lexicon SHA-256 must be 64 hexadecimal characters.", nameof(lexiconSha256));
        }

        if (dimension <= 0 || mean.Length != dimension || mean.Any(m => !float.IsFinite(m)))
        {
            throw new ArgumentException("The mean must be finite and match the dimension.", nameof(mean));
        }

        if (weights.Values.Any(w => w is null || w.Dimension != dimension))
        {
            throw new ArgumentException("Every lemma's weights must share the table dimension.", nameof(weights));
        }

        ModelId = modelId;
        ModelSha256 = modelSha256.ToLowerInvariant();
        LexiconSha256 = lexiconSha256.ToLowerInvariant();
        Dimension = dimension;
        Mean = (float[])mean.Clone();
        _weights = new Dictionary<string, SenseWeights>(weights, StringComparer.Ordinal);
        _lemmas = _weights.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    ///     Gets the identifier of the encoder model the weights were trained with.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the encoder model's file the weights were trained with.
    /// </summary>
    public string ModelSha256 { get; }

    /// <summary>
    ///     Gets the <see cref="WordSenseTable.Sha256"/> of the lexicon table the weights belong to.
    /// </summary>
    public string LexiconSha256 { get; }

    /// <summary>
    ///     Gets the feature dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the mean embedding that is subtracted from features before normalization.
    /// </summary>
    public float[] Mean { get; }

    /// <summary>
    ///     Gets the lemmas that have weights, in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Lemmas => _lemmas;

    /// <summary>
    ///     Looks up the classifier of a lemma.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="weights">Receives the weights when found.</param>
    /// <returns>True when the lemma has weights.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    public bool TryGet(string lemma, out SenseWeights weights)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        return _weights.TryGetValue(lemma, out weights!);
    }

    /// <summary>
    ///     Checks that these weights fit a lexicon table: they name its digest, and cover exactly its context lemmas
    ///     with one weight row per sense.
    /// </summary>
    /// <param name="table">The lexicon table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="table"/> is null.</exception>
    /// <exception cref="WordSenseModelMismatchException">Thrown when the weights do not fit the table.</exception>
    public void EnsureMatches(WordSenseTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!string.Equals(LexiconSha256, table.Sha256, StringComparison.Ordinal))
        {
            throw new WordSenseModelMismatchException("The weights table was trained for a different lexicon table.");
        }

        if (_weights.Count != table.ContextLemmas.Count)
        {
            throw new WordSenseModelMismatchException("The weights table does not cover the context lemmas of the lexicon table.");
        }

        foreach (var lemma in table.ContextLemmas)
        {
            if (!_weights.TryGetValue(lemma.Lemma, out var weights) || weights.SenseCount != lemma.Senses.Count)
            {
                throw new WordSenseModelMismatchException($"The weights of '{lemma.Lemma}' do not match its senses.");
            }
        }
    }

    /// <summary>
    ///     Parses a serialized weights table.
    /// </summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The parsed table.</returns>
    /// <exception cref="WordSenseTableFormatException">
    ///     Thrown when the data is truncated, corrupted, of an unsupported version, or otherwise malformed.
    /// </exception>
    public static WordSenseWeightsTable Read(ReadOnlySpan<byte> data)
    {
        try
        {
            return ReadCore(data);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            throw new WordSenseTableFormatException("The weights table content is inconsistent.", ex);
        }
    }

    /// <summary>
    ///     Serializes the table to its binary format.
    /// </summary>
    /// <returns>The serialized bytes, including the trailing digest.</returns>
    public byte[] ToArray()
    {
        var buffer = new MemoryStream();
        buffer.Write(Magic);
        TableWriter.WriteU16(buffer, CurrentVersion);
        TableWriter.WriteString(buffer, ModelId);
        buffer.Write(Convert.FromHexString(ModelSha256));
        buffer.Write(Convert.FromHexString(LexiconSha256));
        TableWriter.WriteU32(buffer, Dimension);
        TableWriter.WriteFloats(buffer, Mean);
        TableWriter.WriteU32(buffer, _lemmas.Length);
        foreach (var lemma in _lemmas)
        {
            var weights = _weights[lemma];
            TableWriter.WriteString(buffer, lemma);
            TableWriter.WriteU16(buffer, weights.SenseCount);
            buffer.WriteByte((byte)weights.Kind);
            TableWriter.WriteFloats(buffer, weights.Weights);
            TableWriter.WriteFloats(buffer, weights.Bias);
            TableWriter.WriteFloats(buffer, [weights.AbstainMargin]);
        }

        var body = buffer.ToArray();
        return [.. body, .. SHA256.HashData(body)];
    }

    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the serialized table, which identifies these exact weights.
    /// </summary>
    /// <returns>The digest.</returns>
    public string ComputeSha256()
    {
        return Convert.ToHexString(ToArray().AsSpan()[^HashLength..]).ToLowerInvariant();
    }

    /// <summary>
    ///     Parses a serialized table, letting constructor validation errors escape to the caller.
    /// </summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The parsed table.</returns>
    private static WordSenseWeightsTable ReadCore(ReadOnlySpan<byte> data)
    {
        if (data.Length < Magic.Length + 2 + HashLength)
        {
            throw new WordSenseTableFormatException("The weights table is truncated.");
        }

        var body = data[..^HashLength];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(data[^HashLength..]))
        {
            throw new WordSenseTableFormatException("The weights table is corrupted: the checksum does not match.");
        }

        var reader = new TableReader(body);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new WordSenseTableFormatException("The weights table has an invalid signature.");
        }

        var version = reader.ReadU16();
        if (version != CurrentVersion)
        {
            throw new WordSenseTableFormatException($"Unsupported weights table version {version}.");
        }

        var modelId = reader.ReadString();
        var modelSha = Convert.ToHexString(reader.ReadBytes(HashLength)).ToLowerInvariant();
        var lexiconSha = Convert.ToHexString(reader.ReadBytes(HashLength)).ToLowerInvariant();
        var dimension = reader.ReadCount(MaxDimension, 1);
        var mean = reader.ReadFloats(dimension);

        var count = reader.ReadCount(MaxLemmas, 0);
        if ((long)count * MinLemmaBytes > reader.Remaining)
        {
            throw new WordSenseTableFormatException("The lemma count exceeds the data of the weights table.");
        }

        var weights = new Dictionary<string, SenseWeights>(count, StringComparer.Ordinal);
        string? previous = null;
        for (var i = 0; i < count; i++)
        {
            var lemma = reader.ReadString();
            if (previous is not null && string.CompareOrdinal(previous, lemma) >= 0)
            {
                throw new WordSenseTableFormatException("The lemmas of the weights table are not unique and in ordinal order.");
            }

            previous = lemma;
            var senseCount = reader.ReadU16();
            if (senseCount < 2 || senseCount > MaxSenses)
            {
                throw new WordSenseTableFormatException("A lemma has an invalid sense count.");
            }

            var kind = (LemmaModelKind)reader.ReadByte();
            if (kind is not (LemmaModelKind.Binary or LemmaModelKind.OneVersusRest))
            {
                throw new WordSenseTableFormatException("A lemma has an unknown model kind.");
            }

            var rows = kind == LemmaModelKind.Binary ? 1 : senseCount;
            var values = reader.ReadFloats(checked(rows * dimension));
            var bias = reader.ReadFloats(rows);
            var margin = reader.ReadFloats(1)[0];
            weights.Add(lemma, new SenseWeights(kind, values, bias, margin, dimension));
        }

        if (!reader.AtEnd)
        {
            throw new WordSenseTableFormatException("The weights table has unexpected trailing data.");
        }

        return new WordSenseWeightsTable(modelId, modelSha, lexiconSha, dimension, mean, weights);
    }
}
