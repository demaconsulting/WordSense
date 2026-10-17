using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Writes the binary weights of an encoder model (<c>WSX1</c>, Brotli-compressed) that the Onnx package reads.
/// </summary>
/// <remarks>
///     The layout is described by the reader, <c>WeightsBlob</c> in the Onnx package. Lemmas are written in ordinal
///     order, so equal input always gives equal bytes.
/// </remarks>
internal static class WeightsBlobWriter
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
        if (data.Mean.Length != data.Dimension)
        {
            throw new ArgumentException("The mean needs the model dimension.", nameof(data));
        }

        using var raw = new MemoryStream();
        raw.Write(Magic);
        WriteString(raw, data.ModelId);
        WordsBlobWriter.WriteVarint(raw, data.Dimension);
        raw.WriteByte(0);
        WriteFloats(raw, data.Mean);

        var lemmas = data.Lemmas.OrderBy(l => l.Lemma, StringComparer.Ordinal).ToList();
        WordsBlobWriter.WriteVarint(raw, lemmas.Count);
        foreach (var entry in lemmas)
        {
            var w = entry.Weights;
            WriteString(raw, entry.Lemma);
            WriteFloats(raw, [w.AbstainMargin]);
            raw.WriteByte((byte)w.Kind);
            WordsBlobWriter.WriteVarint(raw, entry.Senses.Count);
            foreach (var sense in entry.Senses)
            {
                raw.WriteByte((byte)sense.PartOfSpeech);
                WriteString(raw, sense.Meaning ?? string.Empty);
                raw.WriteByte(sense.Meaning is null ? (byte)0 : (byte)1);
            }

            WriteFloats(raw, w.Bias);
            WriteFloats(raw, w.Weights);
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return output.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WordsBlobWriter.WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteFloats(Stream stream, float[] values)
    {
        Span<byte> buffer = stackalloc byte[4];
        foreach (var value in values)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            stream.Write(buffer);
        }
    }
}
