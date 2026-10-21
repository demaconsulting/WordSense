using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     The serialized form of a form chooser for <see cref="BlobWriter"/>.
/// </summary>
/// <param name="Form">The form.</param>
/// <param name="Labels">The labels in score order.</param>
/// <param name="Threshold">The threshold of the chooser.</param>
/// <param name="Bias">One bias per label.</param>
/// <param name="Weights">The weights, row-major.</param>
internal sealed record ChooserSpec(string Form, string[] Labels, float Threshold, float[] Bias, float[] Weights);

/// <summary>
///     The serialized form of a lemma for <see cref="BlobWriter"/>; unlike <see cref="WeightedLemma"/> it can describe
///     blobs that the reader must reject.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="SenseIds">The sense ids; their number is the sense count that is written.</param>
/// <param name="Bias">The biases that are written.</param>
/// <param name="Weights">The weights that are written, row-major.</param>
/// <param name="FineThreshold">The fine threshold.</param>
/// <param name="GroupThreshold">The group threshold.</param>
/// <param name="Choosers">The form choosers.</param>
internal sealed record LemmaSpec(
    string Lemma,
    string[] SenseIds,
    float[] Bias,
    float[] Weights,
    float FineThreshold = 0f,
    float GroupThreshold = LemmaWeights.Never,
    ChooserSpec[]? Choosers = null)
{
    /// <summary>
    ///     Creates a lemma with zero weights and zero biases.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="senseIds">The sense ids.</param>
    /// <returns>The specification.</returns>
    public static LemmaSpec Of(string lemma, int dimension, params string[] senseIds) =>
        new(lemma, senseIds, new float[senseIds.Length], new float[senseIds.Length * dimension]);

    /// <summary>
    ///     Creates the specification of a weighted lemma.
    /// </summary>
    /// <param name="lemma">The weighted lemma.</param>
    /// <returns>The specification.</returns>
    public static LemmaSpec From(WeightedLemma lemma) => new(
        lemma.Lemma,
        [.. lemma.SenseIds],
        lemma.Weights.Bias,
        lemma.Weights.Weights,
        lemma.Weights.FineThreshold,
        lemma.Weights.GroupThreshold,
        [.. lemma.Choosers.Select(c => new ChooserSpec(c.Form, [.. c.Labels], c.Weights.FineThreshold, c.Weights.Bias, c.Weights.Weights))]);
}

/// <summary>
///     Writes weights blobs in the WSX3 layout of <see cref="WeightsBlob"/> for the tests (the real writer lives in
///     the build tool, which this project does not reference).
/// </summary>
internal static class BlobWriter
{
    /// <summary>
    ///     The identifier of the model in the blobs that the tests write.
    /// </summary>
    public const string ModelId = "m";

    /// <summary>
    ///     Creates a hash of 32 distinct bytes.
    /// </summary>
    /// <returns>The hash.</returns>
    public static byte[] Hash() => [.. Enumerable.Range(1, WeightsBlob.HashSize).Select(i => (byte)i)];

    /// <summary>
    ///     Brotli-compresses raw blob bytes.
    /// </summary>
    /// <param name="raw">The raw bytes.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            brotli.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Writes the header of a blob: magic, model id, hash, dimension, flag and mean.
    /// </summary>
    /// <param name="modelId">The model id.</param>
    /// <param name="hash">The inventory hash.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="mean">The mean, or null for zeros.</param>
    /// <returns>The header bytes.</returns>
    public static byte[] Header(string modelId, byte[] hash, int dimension, float[]? mean = null)
    {
        List<byte> bytes = [.. "WSX3"u8.ToArray()];
        Text(bytes, modelId);
        bytes.AddRange(hash);
        Varint(bytes, dimension);
        bytes.Add(0);
        Floats(bytes, mean ?? new float[dimension]);
        return [.. bytes];
    }

    /// <summary>
    ///     Writes the raw (uncompressed) bytes of a blob.
    /// </summary>
    /// <param name="modelId">The model id.</param>
    /// <param name="hash">The inventory hash.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The raw bytes.</returns>
    public static byte[] Raw(string modelId, byte[] hash, int dimension, params LemmaSpec[] lemmas)
    {
        List<byte> bytes = [.. Header(modelId, hash, dimension)];
        Varint(bytes, lemmas.Length);
        foreach (var lemma in lemmas)
        {
            Text(bytes, lemma.Lemma);
            Floats(bytes, [lemma.FineThreshold, lemma.GroupThreshold]);
            Varint(bytes, lemma.SenseIds.Length);
            foreach (var id in lemma.SenseIds)
            {
                Text(bytes, id);
            }

            Floats(bytes, lemma.Bias);
            Halves(bytes, lemma.Weights);
            var choosers = lemma.Choosers ?? [];
            Varint(bytes, choosers.Length);
            foreach (var chooser in choosers)
            {
                Text(bytes, chooser.Form);
                Floats(bytes, [chooser.Threshold]);
                Varint(bytes, chooser.Labels.Length);
                foreach (var label in chooser.Labels)
                {
                    Text(bytes, label);
                }

                Floats(bytes, chooser.Bias);
                Halves(bytes, chooser.Weights);
            }
        }

        return [.. bytes];
    }

    /// <summary>
    ///     Writes a compressed blob of weights.
    /// </summary>
    /// <param name="data">The weights.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Write(WeightsData data) =>
        Compress(Raw(data.ModelId, data.InventoryHash, data.Dimension, [.. data.Lemmas.Select(LemmaSpec.From)]));

    /// <summary>
    ///     Appends an unsigned varint.
    /// </summary>
    /// <param name="bytes">The bytes to append to.</param>
    /// <param name="value">The value.</param>
    public static void Varint(List<byte> bytes, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            bytes.Add((byte)(v | 0x80));
            v >>= 7;
        }

        bytes.Add((byte)v);
    }

    /// <summary>
    ///     Appends a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="bytes">The bytes to append to.</param>
    /// <param name="value">The string.</param>
    public static void Text(List<byte> bytes, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        Varint(bytes, utf8.Length);
        bytes.AddRange(utf8);
    }

    /// <summary>
    ///     Appends little-endian 32-bit floats.
    /// </summary>
    /// <param name="bytes">The bytes to append to.</param>
    /// <param name="values">The values.</param>
    public static void Floats(List<byte> bytes, IEnumerable<float> values)
    {
        var buffer = new byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            bytes.AddRange(buffer);
        }
    }

    /// <summary>
    ///     Appends little-endian half-precision floats.
    /// </summary>
    /// <param name="bytes">The bytes to append to.</param>
    /// <param name="values">The values.</param>
    public static void Halves(List<byte> bytes, IEnumerable<float> values)
    {
        var buffer = new byte[2];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteHalfLittleEndian(buffer, (Half)value);
            bytes.AddRange(buffer);
        }
    }
}
