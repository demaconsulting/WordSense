using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained weights of one model: the feature space description and one classifier per ambiguous lemma.
/// </summary>
/// <param name="ModelId">The identifier of the model the weights were trained for.</param>
/// <param name="ClassHash">The SHA-256 of the class table of the word image the weights were trained for (<c>WordImage.ClassHash</c>).</param>
/// <param name="Dimension">The dense feature dimension, which is the hidden size of the encoder.</param>
/// <param name="Mean">The global mean vector subtracted from a pooled vector before scoring; length <paramref name="Dimension"/>.</param>
/// <param name="Lemmas">The classifiers, one per lemma group.</param>
internal sealed record WeightsData(string ModelId, byte[] ClassHash, int Dimension, float[] Mean, IReadOnlyList<WeightedLemma> Lemmas);

/// <summary>
///     Reads the binary weights of an encoder model (<c>WSX2</c>, Brotli-compressed). The writer lives in the
///     repository's build tool.
/// </summary>
/// <remarks>
///     <para>
///     Content after the Brotli layer: the magic <c>WSX2</c>; the model id as a length-prefixed UTF-8 string; the
///     32-byte class hash (SHA-256 of the class table of the word image the weights were trained for); the dimension as
///     a varint; a flag byte that is always 0 (dense weights); the mean as little-endian 32-bit floats;
///     the lemma group count as a varint; then per group, in ordinal order of the name: the name, the abstain margin (float), the
///     model kind (byte: 1 binary, 2 one-versus-rest), the sense count (varint), per sense the class id as a string, then
///     the bias floats (one per row) and the weight floats (row-major). A binary model has exactly two senses.
///     </para>
///     <para>
///     The reader checks the structure only: every class id must be non-empty and used by one group at most. That the
///     ids exist in the word image and that the hash matches is checked by <see cref="WeightedLemmaTable"/>.
///     Varints are 7-bit little-endian groups; strings are a varint byte length followed by UTF-8 bytes.
///     </para>
/// </remarks>
internal static class WeightsBlob
{
    /// <summary>
    ///     The size of the class hash.
    /// </summary>
    public const int HashSize = 32;

    private static readonly byte[] Magic = "WSX2"u8.ToArray();

    /// <summary>
    ///     UTF-8 decoding that throws on malformed bytes instead of substituting replacement characters.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    ///     Deserializes weights.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The weights.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt.</exception>
    public static WeightsData Read(ReadOnlySpan<byte> blob)
    {
        var data = Decompress(blob);
        var span = data.AsSpan();
        if (span.Length < Magic.Length || !span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The weights have an unknown format.");
        }

        var p = Magic.Length;
        var modelId = ReadString(span, ref p);
        Need(span, p, HashSize);
        var classHash = span.Slice(p, HashSize).ToArray();
        p += HashSize;
        var dimension = ReadVarint(span, ref p);
        if (ReadByte(span, ref p) != 0)
        {
            throw new InvalidDataException("Only dense weights are supported.");
        }

        var mean = ReadFloats(span, ref p, dimension);
        if (dimension <= 0 || mean.Any(v => !float.IsFinite(v)))
        {
            throw new InvalidDataException("The weights have an invalid mean vector.");
        }

        var count = ReadVarint(span, ref p);
        var lemmas = new List<WeightedLemma>(Math.Min(count, 65536));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenClasses = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var i = 0; i < count; i++)
            {
                lemmas.Add(ReadLemma(span, ref p, dimension, seen, seenClasses));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("The weights are inconsistent.", ex);
        }

        return p == span.Length
            ? new WeightsData(modelId, classHash, dimension, mean, lemmas)
            : throw new InvalidDataException("The weights have trailing data.");
    }

    /// <summary>
    ///     Decompresses a weights blob.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The raw bytes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt.</exception>
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
            throw new InvalidDataException("The weights are corrupt.", ex);
        }
    }

    /// <summary>
    ///     Reads the classifier of one lemma.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="p">The read position, advanced past the lemma.</param>
    /// <param name="dimension">The dense feature dimension.</param>
    /// <param name="seen">The lemmas read so far; the new lemma is added.</param>
    /// <param name="seenClasses">The class ids read so far; the new class ids are added.</param>
    /// <returns>The lemma with its classifier.</returns>
    /// <exception cref="InvalidDataException">Thrown when the lemma is duplicated or inconsistent.</exception>
    private static WeightedLemma ReadLemma(ReadOnlySpan<byte> span, ref int p, int dimension, HashSet<string> seen, HashSet<string> seenClasses)
    {
        var lemma = ReadString(span, ref p);
        if (!seen.Add(lemma))
        {
            throw new InvalidDataException("The weights have a duplicate lemma.");
        }

        var abstain = ReadFloats(span, ref p, 1)[0];
        var kind = (LemmaModelKind)ReadByte(span, ref p);
        var senseCount = ReadVarint(span, ref p);
        if (kind == LemmaModelKind.Binary && senseCount != 2)
        {
            throw new InvalidDataException($"The weights of '{lemma}' are binary but have {senseCount} senses; a binary model needs exactly two.");
        }

        var classIds = new List<string>(Math.Min(senseCount, 255));
        for (var s = 0; s < senseCount; s++)
        {
            var classId = ReadString(span, ref p);
            if (classId.Length == 0 || !seenClasses.Add(classId))
            {
                throw new InvalidDataException($"The weights have an empty or duplicate class id in '{lemma}'.");
            }

            classIds.Add(classId);
        }

        var rows = kind == LemmaModelKind.Binary ? 1 : senseCount;
        var bias = ReadFloats(span, ref p, rows);
        var weights = ReadFloats(span, ref p, checked(rows * dimension));
        var lemmaWeights = new LemmaWeights(kind, weights, bias, abstain, dimension);
        if (classIds.Count != lemmaWeights.SenseCount)
        {
            throw new InvalidDataException($"The weights of '{lemma}' have {lemmaWeights.SenseCount} senses but name {classIds.Count} classes.");
        }

        return new WeightedLemma(lemma, classIds, lemmaWeights);
    }

    /// <summary>
    ///     Reads an unsigned 7-bit varint.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data ends early or the value is too large.</exception>
    private static int ReadVarint(ReadOnlySpan<byte> span, ref int position)
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            if (position >= span.Length || shift > 28)
            {
                throw new InvalidDataException("The data is truncated or malformed.");
            }

            var b = span[position++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result > int.MaxValue ? throw new InvalidDataException("A number is too large.") : (int)result;
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
            throw new InvalidDataException("The weights have an invalid text.", ex);
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
    ///     Reads little-endian 32-bit floats.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <param name="count">The number of values.</param>
    /// <returns>The values.</returns>
    private static float[] ReadFloats(ReadOnlySpan<byte> span, ref int position, int count)
    {
        if (count < 0 || count > (span.Length - position) / 4)
        {
            throw new InvalidDataException("The weights are truncated.");
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(position, 4));
            position += 4;
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
            throw new InvalidDataException("The weights are truncated.");
        }
    }
}
