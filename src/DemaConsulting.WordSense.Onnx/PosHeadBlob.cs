using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained part-of-speech head of one encoder model: the binding to the encoder and to the part-of-speech
///     classes, the training provenance and the linear classifier.
/// </summary>
/// <param name="ModelId">The identifier of the encoder model the head was trained for.</param>
/// <param name="HiddenSize">The hidden size of the encoder; the head reads <c>3 × HiddenSize</c> features.</param>
/// <param name="EncoderSha256">The 32-byte SHA-256 of the pinned ONNX file of the encoder the head was trained on.</param>
/// <param name="FeatureLayout">The feature layout: <see cref="PosHeadBlob.WordAndNeighbors"/> is the only one defined.</param>
/// <param name="ClassNames">The names of the classes in output order, which must be the names of <see cref="PartOfSpeech"/> in declaration order.</param>
/// <param name="TrainSha256">The 32-byte SHA-256 of the training file the head was fitted on.</param>
/// <param name="C">The inverse regularization strength that was selected on the development split.</param>
/// <param name="Variant">The name of the feature variant that was fitted (<c>word+-1</c>).</param>
/// <param name="Weights">The weights widened to 32-bit floats: one row per class, <c>3 × HiddenSize</c> values per row, in the order previous word, word, next word.</param>
/// <param name="Biases">One bias per class.</param>
internal sealed record PosHeadData(
    string ModelId,
    int HiddenSize,
    byte[] EncoderSha256,
    int FeatureLayout,
    IReadOnlyList<string> ClassNames,
    byte[] TrainSha256,
    float C,
    string Variant,
    float[] Weights,
    float[] Biases);

/// <summary>
///     Reads the binary part-of-speech head of an encoder model (<c>WSH1</c>). The writer lives in the repository's
///     build tool.
/// </summary>
/// <remarks>
///     <para>
///     The layout is little-endian and uncompressed (the weights are half-precision numbers, which do not compress):
///     the magic <c>WSH1</c>; the model id as a length-prefixed UTF-8 string; the hidden size as a varint; the 32-byte
///     SHA-256 of the pinned ONNX file of the encoder; the feature layout id as a varint (1 = word, previous and next
///     word, with zeros beyond the sentence edge); the class count as a varint and the class names as strings, in output
///     order; the 32-byte SHA-256 of the training file; the regularization strength as a float; the variant name as a
///     string; the section count as a varint; per section its id (varint), a required flag (byte: 1 = a reader that does
///     not know the section must reject the blob, 0 = it skips it), its payload length (varint) and its payload; and
///     last the 32-byte SHA-256 of every byte before it.
///     </para>
///     <para>
///     Section 1 is the part-of-speech head and is required: <c>k × 3 × HiddenSize</c> half-precision weights
///     (row-major, one row per class) followed by <c>k</c> float biases, where <c>k</c> is the class count. Section 2 is
///     reserved for a tense head. The reader checks the structure and the trailing hash only;
///     <see cref="PosHead.LoadEmbedded"/> binds the head to its encoder and to the classes of the library. Varints are
///     7-bit little-endian groups; strings are a varint byte length followed by UTF-8 bytes.
///     </para>
/// </remarks>
internal static class PosHeadBlob
{
    /// <summary>
    ///     The feature layout of the word and its previous and next word.
    /// </summary>
    public const int WordAndNeighbors = 1;

    /// <summary>
    ///     The id of the section that holds the part-of-speech head.
    /// </summary>
    public const int PosSection = 1;

    /// <summary>
    ///     The size of a SHA-256 hash.
    /// </summary>
    public const int HashSize = 32;

    /// <summary>
    ///     The largest hidden size the reader accepts; far above the hidden size of any encoder the library pins, and small
    ///     enough that no size computed from it overflows.
    /// </summary>
    public const int MaxHiddenSize = 16384;

    /// <summary>
    ///     The largest class count the reader accepts.
    /// </summary>
    public const int MaxClassCount = 1024;

    private static readonly byte[] Magic = "WSH1"u8.ToArray();

    /// <summary>
    ///     UTF-8 decoding that throws on malformed bytes instead of substituting replacement characters.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    ///     Gets the class names a head must carry: the names of <see cref="PartOfSpeech"/> in declaration order.
    /// </summary>
    /// <returns>The names.</returns>
    public static string[] ExpectedClassNames() => Enum.GetNames<PartOfSpeech>();

    /// <summary>
    ///     Deserializes a part-of-speech head.
    /// </summary>
    /// <param name="blob">The blob.</param>
    /// <returns>The head.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt, truncated or not a part-of-speech head.</exception>
    public static PosHeadData Read(ReadOnlySpan<byte> blob)
    {
        // The trailing hash covers everything before it; check it first so that corruption is reported as such
        if (blob.Length < Magic.Length + HashSize || !blob[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The part-of-speech head has an unknown format.");
        }

        var body = blob[..^HashSize];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(blob[^HashSize..]))
        {
            throw new InvalidDataException("The part-of-speech head is corrupt: its checksum does not match.");
        }

        var p = Magic.Length;
        var modelId = ReadString(body, ref p);
        var hiddenSize = ReadVarint(body, ref p);
        if (hiddenSize is <= 0 or > MaxHiddenSize)
        {
            throw new InvalidDataException($"The part-of-speech head has an invalid hidden size; it must be 1 to {MaxHiddenSize}.");
        }

        var encoder = ReadBytes(body, ref p, HashSize);
        var layout = ReadVarint(body, ref p);
        var classes = ReadStrings(body, ref p, MaxClassCount);
        var train = ReadBytes(body, ref p, HashSize);
        var c = ReadFloats(body, ref p, 1)[0];
        var variant = ReadString(body, ref p);
        if (!float.IsFinite(c) || c <= 0)
        {
            throw new InvalidDataException("The part-of-speech head has an invalid regularization strength.");
        }

        // The hidden size and the class count are bounded, so the products below cannot overflow
        var (weights, biases) = ReadSections(body, ref p, classes.Count, 3 * hiddenSize);
        return p == body.Length
            ? new PosHeadData(modelId, hiddenSize, encoder, layout, classes, train, c, variant, weights, biases)
            : throw new InvalidDataException("The part-of-speech head has trailing data.");
    }

    /// <summary>
    ///     Reads the sections and returns the weights and biases of the part-of-speech section.
    /// </summary>
    /// <param name="span">The data without the trailing hash.</param>
    /// <param name="p">The read position, advanced past the sections.</param>
    /// <param name="classCount">The number of classes.</param>
    /// <param name="features">The number of features.</param>
    /// <returns>The weights and the biases.</returns>
    /// <exception cref="InvalidDataException">Thrown when a section is malformed, duplicated or required and unknown, or the part-of-speech section is missing.</exception>
    private static (float[] Weights, float[] Biases) ReadSections(ReadOnlySpan<byte> span, ref int p, int classCount, int features)
    {
        var count = ReadVarint(span, ref p);
        var seen = new HashSet<int>();
        float[]? weights = null;
        float[]? biases = null;
        for (var i = 0; i < count; i++)
        {
            var id = ReadVarint(span, ref p);
            var required = ReadByte(span, ref p);
            var length = ReadVarint(span, ref p);
            Need(span, p, length);
            if (required > 1 || !seen.Add(id))
            {
                throw new InvalidDataException("The part-of-speech head has an invalid or duplicate section.");
            }

            var payload = span.Slice(p, length);
            p += length;
            if (id == PosSection)
            {
                (weights, biases) = ReadPosSection(payload, classCount, features);
            }
            else if (required == 1)
            {
                throw new InvalidDataException($"The part-of-speech head has a required section {id} that this reader does not know.");
            }
        }

        return weights is not null && biases is not null
            ? (weights, biases)
            : throw new InvalidDataException("The part-of-speech head has no part-of-speech section.");
    }

    /// <summary>
    ///     Reads the payload of the part-of-speech section.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="classCount">The number of classes.</param>
    /// <param name="features">The number of features.</param>
    /// <returns>The weights widened to floats and the biases.</returns>
    private static (float[] Weights, float[] Biases) ReadPosSection(ReadOnlySpan<byte> payload, int classCount, int features)
    {
        if (classCount < 2)
        {
            throw new InvalidDataException("The part-of-speech head needs at least two classes.");
        }

        // The class count and the feature count are bounded by the caller: 1,024 × 49,152 fits an int
        var weightCount = classCount * features;
        var expected = (weightCount * 2L) + (classCount * 4L);
        if (payload.Length != expected)
        {
            throw new InvalidDataException("The part-of-speech section has the wrong length.");
        }

        var p = 0;
        var weights = ReadHalves(payload, ref p, weightCount);
        var biases = ReadFloats(payload, ref p, classCount);
        return (weights, biases);
    }

    /// <summary>
    ///     Reads a count and that many strings.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="p">The read position, advanced.</param>
    /// <param name="maximum">The largest count accepted; it is checked before anything is allocated.</param>
    /// <returns>The strings.</returns>
    /// <exception cref="InvalidDataException">Thrown when the count exceeds the maximum or the data, or a string is malformed.</exception>
    private static List<string> ReadStrings(ReadOnlySpan<byte> span, ref int p, int maximum)
    {
        var count = ReadVarint(span, ref p);
        if (count > maximum)
        {
            throw new InvalidDataException($"The part-of-speech head has too many classes; the most is {maximum}.");
        }

        if (count > span.Length - p)
        {
            throw new InvalidDataException("The part-of-speech head is truncated.");
        }

        var values = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(ReadString(span, ref p));
        }

        return values;
    }

    /// <summary>
    ///     Reads an unsigned 7-bit varint.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data ends early or the value does not fit a non-negative 32-bit integer.</exception>
    private static int ReadVarint(ReadOnlySpan<byte> span, ref int position)
    {
        var result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (position >= span.Length)
            {
                throw new InvalidDataException("The part-of-speech head is truncated or malformed.");
            }

            var b = span[position++];
            if (shift == 28 && (b & 0xF8) != 0)
            {
                // The fifth byte holds bits 28 to 34 and may only set bits 28 to 30, with no continuation
                throw new InvalidDataException("A number in the part-of-speech head is too large.");
            }

            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("A number in the part-of-speech head is too large.");
    }

    /// <summary>
    ///     Reads a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the string.</param>
    /// <returns>The string.</returns>
    private static string ReadString(ReadOnlySpan<byte> span, ref int position)
    {
        var length = ReadVarint(span, ref position);
        Need(span, position, length);
        try
        {
            var value = StrictUtf8.GetString(span.Slice(position, length));
            position += length;
            return value;
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("The part-of-speech head has an invalid text.", ex);
        }
    }

    /// <summary>
    ///     Reads one byte.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <returns>The byte.</returns>
    private static byte ReadByte(ReadOnlySpan<byte> span, ref int position)
    {
        Need(span, position, 1);
        return span[position++];
    }

    /// <summary>
    ///     Reads a number of raw bytes.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>A copy of the bytes.</returns>
    private static byte[] ReadBytes(ReadOnlySpan<byte> span, ref int position, int count)
    {
        Need(span, position, count);
        var bytes = span.Slice(position, count).ToArray();
        position += count;
        return bytes;
    }

    /// <summary>
    ///     Reads little-endian 32-bit floats; every value must be finite.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <param name="count">The number of values.</param>
    /// <returns>The values.</returns>
    private static float[] ReadFloats(ReadOnlySpan<byte> span, ref int position, int count)
    {
        if (count < 0 || count > (span.Length - position) / 4)
        {
            throw new InvalidDataException("The part-of-speech head is truncated.");
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(position, 4));
            position += 4;
            if (!float.IsFinite(values[i]))
            {
                throw new InvalidDataException("The part-of-speech head has a value that is not finite.");
            }
        }

        return values;
    }

    /// <summary>
    ///     Reads little-endian 16-bit half-precision floats and widens them to 32-bit floats; every value must be finite.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <param name="count">The number of values.</param>
    /// <returns>The values.</returns>
    private static float[] ReadHalves(ReadOnlySpan<byte> span, ref int position, int count)
    {
        if (count < 0 || count > (span.Length - position) / 2)
        {
            throw new InvalidDataException("The part-of-speech head is truncated.");
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(span.Slice(position, 2));
            position += 2;
            if (!float.IsFinite(values[i]))
            {
                throw new InvalidDataException("The part-of-speech head has a value that is not finite.");
            }
        }

        return values;
    }

    /// <summary>
    ///     Checks that a number of bytes is available.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The position.</param>
    /// <param name="length">The needed length.</param>
    private static void Need(ReadOnlySpan<byte> span, int position, int length)
    {
        if (length < 0 || length > span.Length - position)
        {
            throw new InvalidDataException("The part-of-speech head is truncated.");
        }
    }
}
