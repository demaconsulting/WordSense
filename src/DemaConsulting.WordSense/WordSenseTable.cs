using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: the trained classifier table: per-lemma linear models, the feature centering mean, and the
///     form-to-lemma map, plus its versioned binary serialization.
/// </summary>
/// <remarks>
///     <para>
///     Binary layout (little-endian): magic <c>WSNS</c>, u16 version (4), u16 flags, model id string,
///     32-byte model SHA-256, u32 dimension, f32 mean[dimension], u32 lemma count, lemmas in ordinal order (string lemma,
///     u16 sense count, per sense: u8 part of speech, string meaning (empty when none), string respelling (empty when
///     none); then u8 kind,
///     f32 weights, f32 bias, f32 abstain margin), u32 form count, forms (string form, u32 lemma index; the writer
///     emits them in ordinal order and the reader accepts any order), and a trailing
///     32-byte SHA-256 of everything before it. Strings are a u16 byte length followed by UTF-8.
///     </para>
///     <para>
///     The reader validates every length against the bytes that remain, caps every count, and checks
///     the trailing digest first, so truncated, corrupted or hostile input fails with
///     <see cref="WordSenseTableFormatException"/> rather than a different exception or an
///     oversized allocation. Instances are thread-safe provided callers treat <see cref="Mean"/>, the
///     models in <see cref="Lemmas"/> and the <see cref="Forms"/> map supplied to the constructor as
///     read-only; the constructor copies the mean and the lemma list but not the forms map.
///     </para>
///     <para>
///     Most callers never touch a table: <see cref="WordSenseClassifier.CreateAsync"/> loads the embedded
///     one. Use this type to inspect which lemmas and senses a build supports, or to read a table from
///     bytes with <see cref="Read"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     WordSenseTable table = WordSenseTable.LoadEmbedded();
///     Console.WriteLine($"{table.Lemmas.Count} lemmas, trained with {table.ModelId}");
///
///     if (table.TryGetLemma("record", out LemmaModel model))
///     {
///         foreach (var sense in model.Senses)
///         {
///             Console.WriteLine($"{sense.Pos}: {sense.Meaning ?? "-"}");
///         }
///     }
///
///     // A table round-trips through its binary form
///     WordSenseTable copy = WordSenseTable.Read(table.ToArray());
///     </code>
/// </example>
public sealed class WordSenseTable
{
    /// <summary>
    ///     The four magic bytes that start every table.
    /// </summary>
    private static readonly byte[] Magic = "WSNS"u8.ToArray();

    /// <summary>
    ///     The only serialization version this library reads and writes.
    /// </summary>
    private const ushort CurrentVersion = 4;

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
    ///     The largest form count accepted from a file.
    /// </summary>
    private const int MaxForms = 2_000_000;

    /// <summary>
    ///     The largest sense count per lemma accepted from a file.
    /// </summary>
    private const int MaxSenses = 64;

    /// <summary>
    ///     The manifest resource name of the table embedded in this assembly.
    /// </summary>
    internal const string EmbeddedResourceName = "DemaConsulting.WordSense.wordsense-table.bin";

    /// <summary>
    ///     Maps each lemma to its index in <see cref="Lemmas"/>.
    /// </summary>
    private readonly Dictionary<string, int> _lemmaIndex;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTable"/> class.
    /// </summary>
    /// <param name="modelId">A human-readable identifier of the encoder model the table was trained with.</param>
    /// <param name="modelSha256">The lower-case hexadecimal SHA-256 of that encoder model file.</param>
    /// <param name="dimension">The feature dimension.</param>
    /// <param name="mean">The mean embedding used to center features; length <paramref name="dimension"/>.</param>
    /// <param name="lemmas">The lemma models, which must be unique and in ordinal order of lemma.</param>
    /// <param name="forms">The map from lower-case word form to lemma; every lemma must have a model.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="modelId"/>, <paramref name="modelSha256"/>, <paramref name="mean"/>,
    ///     <paramref name="lemmas"/> or <paramref name="forms"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the content is inconsistent.</exception>
    /// <remarks>
    ///     An element of <paramref name="lemmas"/> that is null also throws <see cref="ArgumentNullException"/>.
    /// </remarks>
    public WordSenseTable(
        string modelId,
        string modelSha256,
        int dimension,
        float[] mean,
        IReadOnlyList<LemmaModel> lemmas,
        IReadOnlyDictionary<string, string> forms)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(modelSha256);
        ArgumentNullException.ThrowIfNull(mean);
        ArgumentNullException.ThrowIfNull(lemmas);
        ArgumentNullException.ThrowIfNull(forms);

        // Validate everything the classifier will rely on
        if (modelSha256.Length != HashLength * 2 || !modelSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The model SHA-256 must be 64 hexadecimal characters.", nameof(modelSha256));
        }

        if (dimension <= 0 || mean.Length != dimension || mean.Any(m => !float.IsFinite(m)))
        {
            throw new ArgumentException("The mean must be finite and match the dimension.", nameof(mean));
        }

        _lemmaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < lemmas.Count; i++)
        {
            var model = lemmas[i] ?? throw new ArgumentNullException(nameof(lemmas), "A lemma model is null.");
            if (model.Dimension != dimension || !_lemmaIndex.TryAdd(model.Lemma, i))
            {
                throw new ArgumentException("Lemma models must be unique and share the table dimension.", nameof(lemmas));
            }

            if (i > 0 && string.CompareOrdinal(lemmas[i - 1].Lemma, model.Lemma) >= 0)
            {
                throw new ArgumentException("Lemma models must be in ordinal order.", nameof(lemmas));
            }
        }

        var unknown = forms.Values.FirstOrDefault(l => !_lemmaIndex.ContainsKey(l));
        if (unknown is not null)
        {
            throw new ArgumentException($"A form refers to lemma '{unknown}' that has no model.", nameof(forms));
        }

        ModelId = modelId;
        ModelSha256 = modelSha256.ToLowerInvariant();
        Dimension = dimension;
        Mean = (float[])mean.Clone();
        Lemmas = lemmas.ToArray();
        Forms = forms;
    }

    /// <summary>
    ///     Gets the human-readable identifier of the encoder model the table was trained with.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the encoder model file the table was trained with.
    /// </summary>
    public string ModelSha256 { get; }

    /// <summary>
    ///     Gets the feature dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the mean embedding that is subtracted from features before normalization.
    /// </summary>
    public float[] Mean { get; }

    /// <summary>
    ///     Gets the lemma models in ordinal order of lemma.
    /// </summary>
    public IReadOnlyList<LemmaModel> Lemmas { get; }

    /// <summary>
    ///     Gets the map from lower-case word form to lemma.
    /// </summary>
    public IReadOnlyDictionary<string, string> Forms { get; }

    /// <summary>
    ///     Looks up the model of a lemma.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="model">Receives the model when found.</param>
    /// <returns>True when the lemma has a model.</returns>
    public bool TryGetLemma(string lemma, out LemmaModel model)
    {
        if (_lemmaIndex.TryGetValue(lemma, out var index))
        {
            model = Lemmas[index];
            return true;
        }

        model = null!;
        return false;
    }

    /// <summary>
    ///     Loads the table that is embedded in this assembly. It needs no model files and no network access, so it can
    ///     be used to browse <see cref="Lemmas"/> and <see cref="Forms"/> offline.
    /// </summary>
    /// <returns>The embedded table.</returns>
    /// <exception cref="WordSenseTableFormatException">Thrown when the table is missing or malformed.</exception>
    /// <example>
    ///     <code>
    ///     WordSenseTable table = WordSenseTable.LoadEmbedded();
    ///     bool hasRecord = table.TryGetLemma("record", out _);
    ///     </code>
    /// </example>
    public static WordSenseTable LoadEmbedded()
    {
        // Copy the resource into memory; it is about 1.3 MB
        using var stream = typeof(WordSenseTable).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new WordSenseTableFormatException("The embedded classifier table is missing from the assembly.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>
    ///     Parses a serialized table.
    /// </summary>
    /// <param name="data">The serialized table bytes.</param>
    /// <returns>The parsed table.</returns>
    /// <remarks>
    ///     To bound memory the reader rejects, whatever the checksum says: a dimension above 4,096; more than
    ///     100,000 lemmas; more than 2,000,000 forms; fewer than 2 or more than 64 senses in a lemma; an undefined
    ///     part of speech; and sense sets that are inconsistent (see <see cref="LemmaModel"/>). Strings are limited to 65,535
    ///     UTF-8 bytes by their length prefix.
    /// </remarks>
    /// <exception cref="WordSenseTableFormatException">
    ///     Thrown when the data is truncated, corrupted, of an unsupported version, or otherwise malformed.
    /// </exception>
    public static WordSenseTable Read(ReadOnlySpan<byte> data)
    {
        try
        {
            return ReadCore(data);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            // Constructor validation failures mean the content is inconsistent, which is a format error
            throw new WordSenseTableFormatException("The classifier table content is inconsistent.", ex);
        }
    }

    /// <summary>
    ///     Serializes the table to its binary format.
    /// </summary>
    /// <returns>The serialized bytes, including the trailing digest.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a string is too long to encode.</exception>
    /// <exception cref="OverflowException">
    ///     Thrown when a count does not fit its field in the format, for example more than 65,535 senses in one
    ///     lemma. The writer does not apply the limits that <see cref="Read"/>
    ///     enforces, so a table that is written successfully can still be rejected when read back.
    /// </exception>
    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }

    /// <summary>
    ///     Serializes the table to a stream.
    /// </summary>
    /// <param name="destination">The stream receiving the bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a string is too long to encode.</exception>
    /// <exception cref="OverflowException">
    ///     Thrown when a count does not fit its field in the format (see <see cref="ToArray"/>).
    /// </exception>
    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Build the body in memory so the digest can be appended
        var buffer = new MemoryStream();
        buffer.Write(Magic);
        WriteU16(buffer, CurrentVersion);
        WriteU16(buffer, 0);
        WriteString(buffer, ModelId);
        buffer.Write(Convert.FromHexString(ModelSha256));
        WriteU32(buffer, Dimension);
        WriteFloats(buffer, Mean);

        WriteU32(buffer, Lemmas.Count);
        foreach (var lemma in Lemmas)
        {
            WriteString(buffer, lemma.Lemma);
            WriteU16(buffer, lemma.Senses.Count);
            foreach (var sense in lemma.Senses)
            {
                buffer.WriteByte((byte)sense.Pos);
                WriteString(buffer, sense.Meaning ?? string.Empty);
                WriteString(buffer, sense.Respelling ?? string.Empty);
            }

            buffer.WriteByte((byte)lemma.Kind);
            WriteFloats(buffer, lemma.Weights);
            WriteFloats(buffer, lemma.Bias);
            WriteFloats(buffer, [lemma.AbstainMargin]);
        }

        // Forms are written in ordinal order so output is deterministic
        var forms = Forms.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        WriteU32(buffer, forms.Count);
        foreach (var form in forms)
        {
            WriteString(buffer, form.Key);
            WriteU32(buffer, _lemmaIndex[form.Value]);
        }

        // Append the integrity digest of everything written so far
        var body = buffer.ToArray();
        destination.Write(body);
        destination.Write(SHA256.HashData(body));
    }

    /// <summary>
    ///     Parses a serialized table, letting constructor validation errors escape to the caller.
    /// </summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The parsed table.</returns>
    private static WordSenseTable ReadCore(ReadOnlySpan<byte> data)
    {
        // Verify the digest before trusting any length field
        if (data.Length < Magic.Length + 4 + HashLength)
        {
            throw new WordSenseTableFormatException("The classifier table is truncated.");
        }

        var body = data[..^HashLength];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(data[^HashLength..]))
        {
            throw new WordSenseTableFormatException("The classifier table is corrupted: the checksum does not match.");
        }

        var reader = new TableReader(body);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new WordSenseTableFormatException("The classifier table has an invalid signature.");
        }

        var version = reader.ReadU16();
        if (version != CurrentVersion)
        {
            throw new WordSenseTableFormatException($"Unsupported classifier table version {version}.");
        }

        _ = reader.ReadU16();
        var modelId = reader.ReadString();
        var modelSha = Convert.ToHexString(reader.ReadBytes(HashLength)).ToLowerInvariant();
        var dimension = reader.ReadCount(MaxDimension, 1);
        var mean = reader.ReadFloats(dimension);

        var lemmaCount = reader.ReadCount(MaxLemmas, 0);
        var lemmas = new List<LemmaModel>(lemmaCount);
        for (var i = 0; i < lemmaCount; i++)
        {
            var lemma = reader.ReadString();
            var senseCount = reader.ReadU16();
            if (senseCount < 2 || senseCount > MaxSenses)
            {
                throw new WordSenseTableFormatException("A lemma has an invalid sense count.");
            }

            var senses = new SenseDefinition[senseCount];
            for (var s = 0; s < senseCount; s++)
            {
                senses[s] = ReadSense(ref reader);
            }

            var kind = (LemmaModelKind)reader.ReadByte();
            if (kind is not (LemmaModelKind.Binary or LemmaModelKind.OneVersusRest))
            {
                throw new WordSenseTableFormatException("A lemma has an unknown model kind.");
            }

            var rows = kind == LemmaModelKind.Binary ? 1 : senseCount;
            var weights = reader.ReadFloats(checked(rows * dimension));
            var bias = reader.ReadFloats(rows);
            var margin = reader.ReadFloats(1)[0];
            lemmas.Add(new LemmaModel(lemma, senses, kind, weights, bias, margin, dimension));
        }

        var formCount = reader.ReadCount(MaxForms, 0);
        var forms = new Dictionary<string, string>(formCount, StringComparer.Ordinal);
        for (var i = 0; i < formCount; i++)
        {
            var form = reader.ReadString();
            var lemmaIndex = reader.ReadU32();
            if (lemmaIndex >= lemmas.Count || !forms.TryAdd(form, lemmas[(int)lemmaIndex].Lemma))
            {
                throw new WordSenseTableFormatException("A form entry is invalid or duplicated.");
            }
        }

        if (!reader.AtEnd)
        {
            throw new WordSenseTableFormatException("The classifier table has unexpected trailing data.");
        }

        return new WordSenseTable(modelId, modelSha, dimension, mean, lemmas, forms);
    }

    /// <summary>
    ///     Reads one sense definition: its part of speech, meaning and respelling.
    /// </summary>
    /// <param name="reader">The table reader.</param>
    /// <returns>The sense definition.</returns>
    private static SenseDefinition ReadSense(ref TableReader reader)
    {
        var pos = reader.ReadByte();
        if (!Enum.IsDefined((PartOfSpeech)pos))
        {
            throw new WordSenseTableFormatException("A sense has an undefined part of speech.");
        }

        var meaning = reader.ReadString();
        var respelling = reader.ReadString();
        return new SenseDefinition((PartOfSpeech)pos, meaning.Length == 0 ? null : meaning, respelling.Length == 0 ? null : respelling);
    }

    /// <summary>
    ///     Writes a 16-bit unsigned value, little-endian.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The value, which must fit in 16 bits.</param>
    private static void WriteU16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value));
        stream.Write(bytes);
    }

    /// <summary>
    ///     Writes a 32-bit unsigned value, little-endian.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The non-negative value.</param>
    private static void WriteU32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
        stream.Write(bytes);
    }

    /// <summary>
    ///     Writes single-precision values, little-endian.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="values">The values.</param>
    private static void WriteFloats(Stream stream, float[] values)
    {
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
            stream.Write(bytes);
        }
    }

    /// <summary>
    ///     Writes a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The string.</param>
    /// <exception cref="InvalidOperationException">Thrown when the encoded string exceeds 65,535 bytes.</exception>
    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("A string is too long for the classifier table format.");
        }

        WriteU16(stream, bytes.Length);
        stream.Write(bytes);
    }

    /// <summary>
    ///     Reads a table body with bounds checking on every access.
    /// </summary>
    private ref struct TableReader
    {
        /// <summary>
        ///     The bytes not yet consumed.
        /// </summary>
        private ReadOnlySpan<byte> _remaining;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TableReader"/> struct.
        /// </summary>
        /// <param name="data">The body bytes.</param>
        public TableReader(ReadOnlySpan<byte> data)
        {
            _remaining = data;
        }

        /// <summary>
        ///     Gets a value indicating whether every byte has been consumed.
        /// </summary>
        public readonly bool AtEnd => _remaining.IsEmpty;

        /// <summary>
        ///     Consumes a fixed number of bytes.
        /// </summary>
        /// <param name="count">The number of bytes.</param>
        /// <returns>The consumed bytes.</returns>
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > _remaining.Length)
            {
                throw new WordSenseTableFormatException("The classifier table is truncated.");
            }

            var result = _remaining[..count];
            _remaining = _remaining[count..];
            return result;
        }

        /// <summary>
        ///     Consumes one byte.
        /// </summary>
        /// <returns>The byte.</returns>
        public byte ReadByte() => ReadBytes(1)[0];

        /// <summary>
        ///     Consumes a little-endian 16-bit unsigned value.
        /// </summary>
        /// <returns>The value.</returns>
        public ushort ReadU16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

        /// <summary>
        ///     Consumes a little-endian 32-bit unsigned value.
        /// </summary>
        /// <returns>The value.</returns>
        public uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

        /// <summary>
        ///     Consumes a 32-bit count and checks it against a limit.
        /// </summary>
        /// <param name="max">The largest accepted value.</param>
        /// <param name="min">The smallest accepted value.</param>
        /// <returns>The count.</returns>
        public int ReadCount(int max, int min)
        {
            var value = ReadU32();
            if (value > (uint)max || value < (uint)min)
            {
                throw new WordSenseTableFormatException("A count in the classifier table is out of range.");
            }

            return (int)value;
        }

        /// <summary>
        ///     Consumes a length-prefixed UTF-8 string.
        /// </summary>
        /// <returns>The string.</returns>
        public string ReadString()
        {
            var length = ReadU16();
            try
            {
                return new UTF8Encoding(false, true).GetString(ReadBytes(length));
            }
            catch (ArgumentException ex)
            {
                throw new WordSenseTableFormatException("A string in the classifier table is not valid UTF-8.", ex);
            }
        }

        /// <summary>
        ///     Consumes single-precision values.
        /// </summary>
        /// <param name="count">The number of values.</param>
        /// <returns>The values.</returns>
        public float[] ReadFloats(int count)
        {
            // Check the byte budget before allocating so a huge count cannot cause a huge allocation
            var bytes = ReadBytes(checked(count * 4));
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..]);
            }

            return values;
        }
    }
}
