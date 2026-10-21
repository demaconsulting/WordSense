using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Writes the binary weights of an encoder model (<c>WSX3</c>, Brotli-compressed) that the Onnx package reads.
/// </summary>
/// <remarks>
///     <para>
///     The layout is described by the reader, <c>WeightsBlob</c> in the Onnx package, and the reader is the only
///     definition of it: after the Brotli layer come the magic, the model id, the inventory hash, the dimension, the
///     dense flag and the mean, then per lemma in ordinal order the lemma, the fine and the group threshold (32-bit
///     floats), the sense ids, the biases (32-bit floats), the weights as half-precision floats and the form choosers.
///     Half precision halves the blob; the loss in accuracy is measured by <see cref="PrecisionGate"/> and must stay
///     within the gate. Equal input always gives equal bytes (lemmas and choosers are written in ordinal order).
///     </para>
///     <para>
///     The writer refuses what the reader would reject, so a blob that is written can always be read: a lemma whose sense
///     id count differs from its row count, a chooser whose label count differs from its row count, and a weight that
///     is not finite in half precision (larger than 65504 in magnitude) raise an <see cref="ArgumentException"/>.
///     </para>
/// </remarks>
internal static class WeightsBlobWriter
{
    private static readonly byte[] Magic = "WSX3"u8.ToArray();

    /// <summary>
    ///     Serializes weights.
    /// </summary>
    /// <param name="data">The weights.</param>
    /// <returns>The compressed blob.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the mean or the hash has the wrong size, a sense id count differs from the number of rows, or a weight does not fit half precision.</exception>
    public static byte[] Write(WeightsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Mean.Length != data.Dimension)
        {
            throw new ArgumentException("The mean needs the model dimension.", nameof(data));
        }

        if (data.InventoryHash.Length != WeightsBlob.HashSize)
        {
            throw new ArgumentException("The inventory hash needs 32 bytes.", nameof(data));
        }

        using var raw = new MemoryStream();
        raw.Write(Magic);
        WriteString(raw, data.ModelId);
        raw.Write(data.InventoryHash);
        VarintWriter.WriteVarint(raw, data.Dimension);
        raw.WriteByte(0);
        WriteFloats(raw, data.Mean);

        var lemmas = data.Lemmas.OrderBy(l => l.Lemma, StringComparer.Ordinal).ToList();
        VarintWriter.WriteVarint(raw, lemmas.Count);
        foreach (var entry in lemmas)
        {
            WriteLemma(raw, entry);
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Writes one lemma with its thresholds, sense ids, rows and choosers.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="entry">The lemma.</param>
    /// <exception cref="ArgumentException">Thrown when the sense ids and the rows disagree or a weight does not fit half precision.</exception>
    private static void WriteLemma(Stream stream, WeightedLemma entry)
    {
        var w = entry.Weights;
        if (entry.SenseIds.Count != w.SenseCount)
        {
            throw new ArgumentException($"The weights of '{entry.Lemma}' have {w.SenseCount} rows but name {entry.SenseIds.Count} senses.", nameof(entry));
        }

        WriteString(stream, entry.Lemma);
        WriteFloats(stream, [w.FineThreshold, w.GroupThreshold]);
        VarintWriter.WriteVarint(stream, entry.SenseIds.Count);
        foreach (var senseId in entry.SenseIds)
        {
            WriteString(stream, senseId);
        }

        WriteFloats(stream, w.Bias);
        WriteHalves(stream, w.Weights, entry.Lemma);
        var choosers = entry.Choosers.OrderBy(c => c.Form, StringComparer.Ordinal).ToList();
        VarintWriter.WriteVarint(stream, choosers.Count);
        foreach (var chooser in choosers)
        {
            if (chooser.Labels.Count != chooser.Weights.SenseCount)
            {
                throw new ArgumentException($"The chooser of '{chooser.Form}' has {chooser.Weights.SenseCount} rows but {chooser.Labels.Count} labels.", nameof(entry));
            }

            WriteString(stream, chooser.Form);
            WriteFloats(stream, [chooser.Weights.FineThreshold]);
            VarintWriter.WriteVarint(stream, chooser.Labels.Count);
            foreach (var label in chooser.Labels)
            {
                WriteString(stream, label);
            }

            WriteFloats(stream, chooser.Weights.Bias);
            WriteHalves(stream, chooser.Weights.Weights, chooser.Form);
        }
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
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            stream.Write(buffer);
        }
    }

    /// <summary>
    ///     Writes values as little-endian half-precision floats, rounded to nearest.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="values">The values.</param>
    /// <param name="owner">The lemma or form the values belong to, for the message.</param>
    /// <exception cref="ArgumentException">Thrown when a value is too large for half precision.</exception>
    private static void WriteHalves(Stream stream, float[] values, string owner)
    {
        Span<byte> buffer = stackalloc byte[2];
        foreach (var value in values)
        {
            var half = (Half)value;
            if (!Half.IsFinite(half))
            {
                throw new ArgumentException($"The weight {value} of '{owner}' does not fit half precision.", nameof(values));
            }

            BinaryPrimitives.WriteHalfLittleEndian(buffer, half);
            stream.Write(buffer);
        }
    }
}
