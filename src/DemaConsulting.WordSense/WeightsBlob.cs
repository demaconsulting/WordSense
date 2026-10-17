using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     The senses and trained weights of one lemma, as stored in a weights blob.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Senses">The senses in score order; each is a part of speech and an optional meaning.</param>
/// <param name="Weights">The trained classifier; it scores exactly these senses.</param>
internal sealed record LemmaWeightsEntry(string Lemma, IReadOnlyList<(PartOfSpeech PartOfSpeech, string? Meaning)> Senses, LemmaWeights Weights);

/// <summary>
///     The trained weights of one model: the feature space description and one classifier per ambiguous lemma.
/// </summary>
/// <param name="ModelId">The identifier of the model the weights were trained for.</param>
/// <param name="Dimension">The dense feature dimension; for a sparse (feature-hashed) model the hash space size.</param>
/// <param name="Sparse">True when the lemma classifiers use sparse feature ids.</param>
/// <param name="Mean">The anchor mean subtracted from a dense feature before scoring; empty for a sparse model.</param>
/// <param name="Lemmas">The classifiers.</param>
internal sealed record WeightsData(string ModelId, int Dimension, bool Sparse, float[] Mean, IReadOnlyList<LemmaWeightsEntry> Lemmas);

/// <summary>
///     Reads and writes the binary weights of a model (<c>WSX1</c>, Brotli-compressed).
/// </summary>
internal static class WeightsBlob
{
    private static readonly byte[] Magic = "WSX1"u8.ToArray();

    /// <summary>
    ///     Serializes weights.
    /// </summary>
    /// <param name="data">The weights.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Write(WeightsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var raw = new MemoryStream();
        raw.Write(Magic);
        WriteString(raw, data.ModelId);
        WordsBlob.WriteVarint(raw, data.Dimension);
        raw.WriteByte(data.Sparse ? (byte)1 : (byte)0);
        if (!data.Sparse)
        {
            if (data.Mean.Length != data.Dimension)
            {
                throw new ArgumentException("A dense model needs a mean of the model dimension.", nameof(data));
            }

            WriteFloats(raw, data.Mean);
        }

        var lemmas = data.Lemmas.OrderBy(l => l.Lemma, StringComparer.Ordinal).ToList();
        WordsBlob.WriteVarint(raw, lemmas.Count);
        foreach (var entry in lemmas)
        {
            var w = entry.Weights;
            WriteString(raw, entry.Lemma);
            WriteFloats(raw, [w.AbstainMargin]);
            raw.WriteByte((byte)w.Kind);
            WordsBlob.WriteVarint(raw, entry.Senses.Count);
            foreach (var (pos, meaning) in entry.Senses)
            {
                raw.WriteByte((byte)pos);
                WriteString(raw, meaning ?? string.Empty);
                raw.WriteByte(meaning is null ? (byte)0 : (byte)1);
            }

            WriteFloats(raw, w.Bias);
            if (data.Sparse)
            {
                var ids = w.FeatureIds ?? throw new ArgumentException("A sparse model needs feature ids.", nameof(data));
                WordsBlob.WriteVarint(raw, ids.Length);
                var previous = 0;
                foreach (var id in ids)
                {
                    WordsBlob.WriteVarint(raw, id - previous);
                    previous = id;
                }
            }

            WriteFloats(raw, w.Weights);
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Deserializes weights.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The weights.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt.</exception>
    public static WeightsData Read(ReadOnlySpan<byte> blob)
    {
        byte[] data;
        try
        {
            using var input = new MemoryStream(blob.ToArray());
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            brotli.CopyTo(raw);
            data = raw.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The weights are corrupt.", ex);
        }

        var span = data.AsSpan();
        if (span.Length < Magic.Length || !span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The weights have an unknown format.");
        }

        var p = Magic.Length;
        var modelId = ReadString(span, ref p);
        var dimension = WordsBlob.ReadVarint(span, ref p);
        var sparse = ReadByte(span, ref p) == 1;
        var mean = sparse ? Array.Empty<float>() : ReadFloats(span, ref p, dimension);
        var count = WordsBlob.ReadVarint(span, ref p);
        var lemmas = new List<LemmaWeightsEntry>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var lemma = ReadString(span, ref p);
                var abstain = ReadFloats(span, ref p, 1)[0];
                var kind = (LemmaModelKind)ReadByte(span, ref p);
                var senseCount = WordsBlob.ReadVarint(span, ref p);
                var senses = new List<(PartOfSpeech, string?)>(senseCount);
                for (var s = 0; s < senseCount; s++)
                {
                    var pos = ReadByte(span, ref p);
                    var meaning = ReadString(span, ref p);
                    var present = ReadByte(span, ref p) == 1;
                    senses.Add(((PartOfSpeech)pos, present ? meaning : null));
                }

                var rows = kind == LemmaModelKind.Binary ? 1 : senseCount;
                var bias = ReadFloats(span, ref p, rows);
                int[]? ids = null;
                var width = dimension;
                if (sparse)
                {
                    width = WordsBlob.ReadVarint(span, ref p);
                    ids = new int[width];
                    var previous = 0;
                    for (var k = 0; k < width; k++)
                    {
                        previous += WordsBlob.ReadVarint(span, ref p);
                        ids[k] = previous;
                    }
                }

                var weights = ReadFloats(span, ref p, checked(rows * width));
                lemmas.Add(new LemmaWeightsEntry(lemma, senses, new LemmaWeights(kind, weights, bias, abstain, width, ids)));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("The weights are inconsistent.", ex);
        }

        return p == span.Length
            ? new WeightsData(modelId, dimension, sparse, mean, lemmas)
            : throw new InvalidDataException("The weights have trailing data.");
    }

    /// <summary>
    ///     Writes a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The string.</param>
    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WordsBlob.WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }

    /// <summary>
    ///     Reads a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the string.</param>
    /// <returns>The string.</returns>
    private static string ReadString(ReadOnlySpan<byte> span, ref int position)
    {
        var length = WordsBlob.ReadVarint(span, ref position);
        Need(span, position, length);
        var value = Encoding.UTF8.GetString(span.Slice(position, length));
        position += length;
        return value;
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
    ///     Writes little-endian 32-bit floats.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="values">The values.</param>
    private static void WriteFloats(Stream stream, ReadOnlySpan<float> values)
    {
        Span<byte> buffer = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            stream.Write(buffer);
        }
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
        if (length < 0 || position + length > span.Length)
        {
            throw new InvalidDataException("The weights are truncated.");
        }
    }
}
