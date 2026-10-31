using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Writes the binary part-of-speech head of an encoder model (<c>WSH1</c>), the format that
///     <c>PosHeadBlob</c> reads.
/// </summary>
/// <remarks>
///     The weights are stored as half-precision numbers; <see cref="Round"/> gives the weights as the reader will see
///     them. The output is a function of the head only (no time stamp, no path), so the same head always gives the same
///     bytes.
/// </remarks>
internal static class PosHeadBlobWriter
{
    /// <summary>
    ///     Rounds weights to half precision and widens them again: the values the reader returns for a written head.
    /// </summary>
    /// <param name="weights">The weights.</param>
    /// <returns>The rounded weights.</returns>
    /// <exception cref="InvalidDataException">Thrown when a weight is not finite or does not fit half precision.</exception>
    public static float[] Round(IReadOnlyList<float> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var rounded = new float[weights.Count];
        for (var i = 0; i < rounded.Length; i++)
        {
            rounded[i] = (float)(Half)weights[i];
            if (!float.IsFinite(rounded[i]))
            {
                throw new InvalidDataException("A weight of the head is not finite or does not fit half precision.");
            }
        }

        return rounded;
    }

    /// <summary>
    ///     Serializes a head.
    /// </summary>
    /// <param name="head">The head; its class names must be the parts of speech of the library in order.</param>
    /// <returns>The blob with its trailing SHA-256.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the head is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the head is inconsistent.</exception>
    /// <exception cref="InvalidDataException">Thrown when a weight is not finite or does not fit half precision.</exception>
    public static byte[] Write(PosHeadData head)
    {
        ArgumentNullException.ThrowIfNull(head);
        var classes = head.ClassNames.Count;
        if (head.EncoderSha256.Length != PosHeadBlob.HashSize || head.TrainSha256.Length != PosHeadBlob.HashSize)
        {
            throw new ArgumentException("A digest must have 32 bytes.", nameof(head));
        }

        if (classes < 2 || head.Biases.Length != classes || head.Weights.Length != checked(classes * 3 * head.HiddenSize))
        {
            throw new ArgumentException("The weights and the biases do not fit the classes and the hidden size.", nameof(head));
        }

        using var body = new MemoryStream();
        body.Write("WSH1"u8);
        WriteString(body, head.ModelId);
        VarintWriter.WriteVarint(body, head.HiddenSize);
        body.Write(head.EncoderSha256);
        VarintWriter.WriteVarint(body, head.FeatureLayout);
        VarintWriter.WriteVarint(body, classes);
        foreach (var name in head.ClassNames)
        {
            WriteString(body, name);
        }

        body.Write(head.TrainSha256);
        WriteFloat(body, head.C);
        WriteString(body, head.Variant);

        // The part-of-speech head is the one required section; the tense head, when there is one, is optional (section 2)
        using var section = new MemoryStream();
        var half = new byte[2];
        foreach (var w in Round(head.Weights))
        {
            BinaryPrimitives.WriteHalfLittleEndian(half, (Half)w);
            section.Write(half);
        }

        foreach (var b in head.Biases)
        {
            if (!float.IsFinite(b))
            {
                throw new InvalidDataException("A bias of the head is not finite.");
            }

            WriteFloat(section, b);
        }

        VarintWriter.WriteVarint(body, head.Tense is null ? 1 : 2);
        VarintWriter.WriteVarint(body, PosHeadBlob.PosSection);
        body.WriteByte(1);
        VarintWriter.WriteVarint(body, checked((int)section.Length));
        body.Write(section.GetBuffer().AsSpan(0, (int)section.Length));
        if (head.Tense is { } tense)
        {
            WriteTense(body, tense, head.HiddenSize);
        }

        var bytes = body.ToArray();
        return [.. bytes, .. SHA256.HashData(bytes)];
    }

    /// <summary>
    ///     Writes the tense section.
    /// </summary>
    /// <param name="body">The stream of the blob.</param>
    /// <param name="tense">The tense head.</param>
    /// <param name="hiddenSize">The hidden size.</param>
    private static void WriteTense(MemoryStream body, TenseHeadData tense, int hiddenSize)
    {
        var vectors = tense.Layout switch
        {
            PosHeadBlob.TenseWord => 1,
            PosHeadBlob.TenseWordAndNeighbors => 3,
            _ => throw new ArgumentException($"The tense layout {tense.Layout} is unknown.", nameof(tense)),
        };

        if (tense.Weights.Length != checked(vectors * hiddenSize) || !float.IsFinite(tense.C) || tense.C <= 0 || !float.IsFinite(tense.Bias))
        {
            throw new ArgumentException("The tense head does not fit its layout and the hidden size.", nameof(tense));
        }

        using var payload = new MemoryStream();
        VarintWriter.WriteVarint(payload, tense.Layout);
        WriteFloat(payload, tense.C);
        VarintWriter.WriteVarint(payload, tense.Weights.Length);
        var half = new byte[2];
        foreach (var w in Round(tense.Weights))
        {
            BinaryPrimitives.WriteHalfLittleEndian(half, (Half)w);
            payload.Write(half);
        }

        WriteFloat(payload, tense.Bias);
        VarintWriter.WriteVarint(body, PosHeadBlob.TenseSection);
        body.WriteByte(0);
        VarintWriter.WriteVarint(body, checked((int)payload.Length));
        body.Write(payload.GetBuffer().AsSpan(0, (int)payload.Length));
    }

    private static void WriteFloat(Stream stream, float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        VarintWriter.WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }
}
