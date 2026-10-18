using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Writes the binary weights of an encoder model (<c>WSX2</c>, Brotli-compressed) that the Onnx package reads.
/// </summary>
/// <remarks>
///     The layout is described by the reader, <c>WeightsBlob</c> in the Onnx package. Lemmas are written in ordinal
///     order, so equal input always gives equal bytes. A binary model must have exactly two senses.
/// </remarks>
internal static class WeightsBlobWriter
{
    private static readonly byte[] Magic = "WSX2"u8.ToArray();

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

        if (data.ClassHash.Length != 32)
        {
            throw new ArgumentException("The class hash needs 32 bytes.", nameof(data));
        }

        using var raw = new MemoryStream();
        raw.Write(Magic);
        WriteString(raw, data.ModelId);
        raw.Write(data.ClassHash);
        VarintWriter.WriteVarint(raw, data.Dimension);
        raw.WriteByte(0);
        WriteFloats(raw, data.Mean);

        var lemmas = data.Lemmas.OrderBy(l => l.Lemma, StringComparer.Ordinal).ToList();
        VarintWriter.WriteVarint(raw, lemmas.Count);
        foreach (var entry in lemmas)
        {
            var w = entry.Weights;
            if (entry.ClassIds.Count != w.SenseCount)
            {
                throw new ArgumentException($"The weights of '{entry.Lemma}' have {w.SenseCount} senses but name {entry.ClassIds.Count} classes.", nameof(data));
            }

            WriteString(raw, entry.Lemma);
            WriteFloats(raw, [w.AbstainMargin]);
            raw.WriteByte((byte)w.Kind);
            VarintWriter.WriteVarint(raw, entry.ClassIds.Count);
            foreach (var classId in entry.ClassIds)
            {
                WriteString(raw, classId);
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
        VarintWriter.WriteVarint(stream, bytes.Length);
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
