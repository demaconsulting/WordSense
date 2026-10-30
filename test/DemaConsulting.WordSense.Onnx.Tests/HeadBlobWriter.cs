using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     The description of a part-of-speech head blob for <see cref="HeadBlobWriter"/>; unlike <see cref="PosHeadData"/>
///     it can describe blobs that the reader must reject.
/// </summary>
/// <param name="ModelId">The model id.</param>
/// <param name="HiddenSize">The hidden size that is written.</param>
/// <param name="EncoderSha256">The encoder digest that is written.</param>
/// <param name="FeatureLayout">The feature layout that is written.</param>
/// <param name="ClassNames">The class names that are written, in order.</param>
/// <param name="Weights">The weights that are written, row-major.</param>
/// <param name="Biases">The biases that are written.</param>
/// <param name="Sections">Extra sections as id, required flag and payload; they follow the part-of-speech section.</param>
internal sealed record HeadSpec(
    string ModelId,
    int HiddenSize,
    byte[] EncoderSha256,
    int FeatureLayout,
    string[] ClassNames,
    float[] Weights,
    float[] Biases,
    (int Id, bool Required, byte[] Payload)[]? Sections = null)
{
    /// <summary>
    ///     Creates a valid description over the parts of speech of the library with small distinct weights.
    /// </summary>
    /// <param name="hiddenSize">The hidden size.</param>
    /// <returns>The description.</returns>
    public static HeadSpec Create(int hiddenSize = 4)
    {
        var classes = Enum.GetNames<PartOfSpeech>();
        var weights = new float[classes.Length * 3 * hiddenSize];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = ((i % 7) - 3) / 4f;
        }

        var biases = new float[classes.Length];
        for (var c = 0; c < biases.Length; c++)
        {
            biases[c] = c / 8f;
        }

        return new HeadSpec("m", hiddenSize, [.. Enumerable.Range(1, PosHeadBlob.HashSize).Select(i => (byte)i)], PosHeadBlob.WordAndNeighbors, classes, weights, biases);
    }
}

/// <summary>
///     Writes part-of-speech head blobs in the <c>WSH1</c> layout of <see cref="PosHeadBlob"/> for the tests (the real
///     writer lives in the build tool, which this project does not reference).
/// </summary>
internal static class HeadBlobWriter
{
    /// <summary>
    ///     Serializes a head description with a valid trailing hash.
    /// </summary>
    /// <param name="spec">The description.</param>
    /// <returns>The blob.</returns>
    public static byte[] Write(HeadSpec spec)
    {
        var body = WriteBody(spec);
        return [.. body, .. SHA256.HashData(body)];
    }

    /// <summary>
    ///     Serializes a head description without its trailing hash.
    /// </summary>
    /// <param name="spec">The description.</param>
    /// <returns>The blob without the hash.</returns>
    public static byte[] WriteBody(HeadSpec spec)
    {
        using var stream = new MemoryStream();
        stream.Write("WSH1"u8);
        WriteString(stream, spec.ModelId);
        WriteVarint(stream, spec.HiddenSize);
        stream.Write(spec.EncoderSha256);
        WriteVarint(stream, spec.FeatureLayout);
        WriteVarint(stream, spec.ClassNames.Length);
        foreach (var name in spec.ClassNames)
        {
            WriteString(stream, name);
        }

        stream.Write(new byte[PosHeadBlob.HashSize]);
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(four, 0.01f);
        stream.Write(four);
        WriteString(stream, "word+-1");

        var pos = new MemoryStream();
        var two = new byte[2];
        foreach (var w in spec.Weights)
        {
            BinaryPrimitives.WriteHalfLittleEndian(two, (Half)w);
            pos.Write(two);
        }

        foreach (var b in spec.Biases)
        {
            BinaryPrimitives.WriteSingleLittleEndian(four, b);
            pos.Write(four);
        }

        var sections = new List<(int Id, bool Required, byte[] Payload)> { (PosHeadBlob.PosSection, true, pos.ToArray()) };
        sections.AddRange(spec.Sections ?? []);
        WriteVarint(stream, sections.Count);
        foreach (var (id, required, payload) in sections)
        {
            WriteVarint(stream, id);
            stream.WriteByte(required ? (byte)1 : (byte)0);
            WriteVarint(stream, payload.Length);
            stream.Write(payload);
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     Serializes the payload of a tense section.
    /// </summary>
    /// <param name="layout">The feature layout.</param>
    /// <param name="c">The regularization strength.</param>
    /// <param name="weights">The weights, written as half-precision numbers after their count.</param>
    /// <param name="bias">The bias.</param>
    /// <returns>The payload.</returns>
    public static byte[] TensePayload(int layout, float c, float[] weights, float bias)
    {
        using var stream = new MemoryStream();
        WriteVarint(stream, layout);
        var four = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(four, c);
        stream.Write(four);
        WriteVarint(stream, weights.Length);
        var two = new byte[2];
        foreach (var w in weights)
        {
            BinaryPrimitives.WriteHalfLittleEndian(two, (Half)w);
            stream.Write(two);
        }

        BinaryPrimitives.WriteSingleLittleEndian(four, bias);
        stream.Write(four);
        return stream.ToArray();
    }

    private static void WriteVarint(Stream stream, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        stream.WriteByte((byte)v);
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }
}
