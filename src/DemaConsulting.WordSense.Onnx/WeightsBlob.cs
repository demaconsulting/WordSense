using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained weights of one model: the feature space description and one classifier per trained lemma.
/// </summary>
/// <param name="ModelId">The identifier of the model the weights were trained for.</param>
/// <param name="InventoryHash">The 32-byte hash of the sense inventory of the word image the weights were trained for (<c>WordImage.InventoryHash</c>); the reader returns it unchecked.</param>
/// <param name="Dimension">The dense feature dimension, which is the hidden size of the encoder.</param>
/// <param name="Mean">The global mean vector subtracted from a pooled vector before scoring; length <paramref name="Dimension"/>.</param>
/// <param name="Lemmas">The classifiers, one per trained lemma.</param>
internal sealed record WeightsData(string ModelId, byte[] InventoryHash, int Dimension, float[] Mean, IReadOnlyList<WeightedLemma> Lemmas);

/// <summary>
///     Reads the binary weights of an encoder model (<c>WSX3</c>, Brotli-compressed). The writer lives in the
///     repository's build tool.
/// </summary>
/// <remarks>
///     <para>
///     Content after the Brotli layer: the magic <c>WSX3</c>; the model id as a length-prefixed UTF-8 string; the
///     32-byte inventory hash (SHA-256 of the sense inventory of the word image the weights were trained for); the
///     dimension as a varint; a flag byte that is always 0 (dense weights); the mean as little-endian 32-bit floats;
///     the lemma count as a varint; then per lemma, in ordinal order of the lemma: the lemma, the fine threshold and the
///     group threshold (floats), the sense count <c>n</c> (varint, at least two), the <c>n</c> sense ids as strings, the
///     <c>n</c> biases (floats), the <c>n × dimension</c> weights as little-endian half-precision floats (row-major),
///     the chooser count (varint) and per chooser: the form, its threshold (float), its label count <c>k</c> (varint, at
///     least two), the labels, the <c>k</c> biases (floats) and the <c>k × dimension</c> weights (halves). The reader
///     widens every half to a float.
///     </para>
///     <para>
///     The reader checks the structure only: every sense id must be non-empty and used once in the whole blob, a
///     threshold must be finite and not negative, and every value must be finite. The hash is returned as read;
///     <see cref="WeightedLemmaTable"/> checks the blob against the word image. Varints are 7-bit little-endian groups;
///     strings are a varint byte length followed by UTF-8 bytes.
///     </para>
/// </remarks>
internal static class WeightsBlob
{
    /// <summary>
    ///     The size of the inventory hash.
    /// </summary>
    public const int HashSize = 32;

    private static readonly byte[] Magic = "WSX3"u8.ToArray();

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
        var inventoryHash = span.Slice(p, HashSize).ToArray();
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
        var seenSenses = new HashSet<string>(StringComparer.Ordinal);
        var seenForms = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var i = 0; i < count; i++)
            {
                lemmas.Add(ReadLemma(span, ref p, dimension, seen, seenSenses, seenForms));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("The weights are inconsistent.", ex);
        }

        return p == span.Length
            ? new WeightsData(modelId, inventoryHash, dimension, mean, lemmas)
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
    ///     Reads the classifier of one lemma with its choosers.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="p">The read position, advanced past the lemma.</param>
    /// <param name="dimension">The dense feature dimension.</param>
    /// <param name="seen">The lemmas read so far; the new lemma is added.</param>
    /// <param name="seenSenses">The sense ids read so far; the new sense ids are added.</param>
    /// <param name="seenForms">The forms of the choosers read so far; the new forms are added.</param>
    /// <returns>The lemma with its classifier.</returns>
    /// <exception cref="InvalidDataException">Thrown when the lemma is duplicated or inconsistent.</exception>
    private static WeightedLemma ReadLemma(ReadOnlySpan<byte> span, ref int p, int dimension, HashSet<string> seen, HashSet<string> seenSenses, HashSet<string> seenForms)
    {
        var lemma = ReadString(span, ref p);
        if (!seen.Add(lemma))
        {
            throw new InvalidDataException("The weights have a duplicate lemma.");
        }

        var thresholds = ReadFloats(span, ref p, 2);
        var senseCount = ReadVarint(span, ref p);
        if (senseCount < 2)
        {
            throw new InvalidDataException($"The weights of '{lemma}' have {senseCount} senses; a lemma needs at least two.");
        }

        var senseIds = new List<string>(Math.Min(senseCount, 255));
        for (var s = 0; s < senseCount; s++)
        {
            var senseId = ReadString(span, ref p);
            if (senseId.Length == 0 || !seenSenses.Add(senseId))
            {
                throw new InvalidDataException($"The weights have an empty or duplicate sense id in '{lemma}'.");
            }

            senseIds.Add(senseId);
        }

        var weights = ReadRows(span, ref p, senseCount, dimension, thresholds[0], thresholds[1]);
        var choosers = new List<FormChooser>();
        var chooserCount = ReadVarint(span, ref p);
        for (var c = 0; c < chooserCount; c++)
        {
            choosers.Add(ReadChooser(span, ref p, dimension, seenForms));
        }

        return new WeightedLemma(lemma, senseIds, weights, choosers);
    }

    /// <summary>
    ///     Reads one form chooser.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="p">The read position, advanced past the chooser.</param>
    /// <param name="dimension">The dense feature dimension.</param>
    /// <param name="seenForms">The forms read so far; the new form is added.</param>
    /// <returns>The chooser.</returns>
    /// <exception cref="InvalidDataException">Thrown when the chooser is duplicated or inconsistent.</exception>
    private static FormChooser ReadChooser(ReadOnlySpan<byte> span, ref int p, int dimension, HashSet<string> seenForms)
    {
        var form = ReadString(span, ref p);
        if (form.Length == 0 || !seenForms.Add(form))
        {
            throw new InvalidDataException("The weights have an empty or duplicate form chooser.");
        }

        var threshold = ReadFloats(span, ref p, 1)[0];
        var labelCount = ReadVarint(span, ref p);
        if (labelCount < 2)
        {
            throw new InvalidDataException($"The chooser of '{form}' has {labelCount} labels; it needs at least two.");
        }

        var labels = new List<string>(Math.Min(labelCount, 255));
        for (var i = 0; i < labelCount; i++)
        {
            var label = ReadString(span, ref p);
            if (label.Length == 0 || labels.Contains(label, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"The chooser of '{form}' has an empty or duplicate label.");
            }

            labels.Add(label);
        }

        return new FormChooser(form, labels, ReadRows(span, ref p, labelCount, dimension, threshold, LemmaWeights.Never));
    }

    /// <summary>
    ///     Reads the biases and the half-precision weight rows of a classifier.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="p">The read position, advanced.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="dimension">The dense feature dimension.</param>
    /// <param name="fineThreshold">The fine threshold.</param>
    /// <param name="groupThreshold">The group threshold.</param>
    /// <returns>The classifier.</returns>
    private static LemmaWeights ReadRows(ReadOnlySpan<byte> span, ref int p, int rows, int dimension, float fineThreshold, float groupThreshold)
    {
        var bias = ReadFloats(span, ref p, rows);
        var weights = ReadHalves(span, ref p, checked(rows * dimension));
        if (!float.IsFinite(fineThreshold) || fineThreshold < 0 || !float.IsFinite(groupThreshold) || groupThreshold < 0)
        {
            throw new InvalidDataException("The weights have a threshold that is not a finite margin that is not negative.");
        }

        return new LemmaWeights(weights, bias, dimension, fineThreshold, groupThreshold);
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
    ///     Reads little-endian 16-bit half-precision floats and widens them to 32-bit floats.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced.</param>
    /// <param name="count">The number of values.</param>
    /// <returns>The values.</returns>
    private static float[] ReadHalves(ReadOnlySpan<byte> span, ref int position, int count)
    {
        if (count < 0 || count > (span.Length - position) / 2)
        {
            throw new InvalidDataException("The weights are truncated.");
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(span.Slice(position, 2));
            position += 2;
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
