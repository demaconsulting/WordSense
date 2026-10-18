using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Writes the table of the basic model (<c>basic.bin</c>) that <c>DemaConsulting.WordSense.Basic</c> embeds.
/// </summary>
/// <remarks>
///     The layout is described on <c>BasicTables</c>, the only reader. Words, suffixes and lemmas are written in the
///     ordinal order of the data, so equal data always gives equal bytes. The runtime hyper-parameters (gamma, beta, the
///     three lambdas, the weight of the tagger and the inverse temperature) are written to the header from
///     <see cref="BasicHyperparameters"/>.
/// </remarks>
internal static class BasicTablesWriter
{
    private static readonly byte[] Magic = "WSB1"u8.ToArray();

    /// <summary>
    ///     Serializes the model data.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="level">The Brotli compression level; the shipped file uses the smallest size, the tuner a fast level.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Write(BasicModelData data, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var raw = new MemoryStream();
        raw.Write(Magic);
        var hp = data.Hyperparameters;
        foreach (var value in new[] { hp.Gamma, hp.Beta, hp.Lambda1, hp.Lambda2, hp.Lambda3, hp.HmmWeight, hp.InvTemperature })
        {
            WriteDouble(raw, value);
        }

        WriteVarint(raw, DemaConsulting.WordSense.Basic.PosTag.Count);
        foreach (var count in data.Unigrams.Concat(data.Bigrams).Concat(data.Trigrams))
        {
            WriteVarint(raw, count);
        }

        WriteVarint(raw, data.Words.Count);
        foreach (var word in data.Words)
        {
            WriteString(raw, word.Word);
            var presence = 0;
            for (var t = 0; t < word.Counts.Length; t++)
            {
                presence |= word.Counts[t] > 0 ? 1 << t : 0;
            }

            if (word.ClosedMask != 0)
            {
                raw.WriteByte(1);
                WriteUInt16(raw, word.ClosedMask);
            }
            else
            {
                raw.WriteByte(0);
            }

            WriteUInt16(raw, presence);
            foreach (var count in word.Counts.Where(c => c > 0))
            {
                WriteVarint(raw, count);
            }
        }

        WriteVarint(raw, data.Suffixes.Count);
        foreach (var suffix in data.Suffixes)
        {
            WriteString(raw, suffix.Suffix);
            foreach (var count in suffix.Counts)
            {
                WriteVarint(raw, count);
            }
        }

        WriteVarint(raw, data.Cues.Count);
        foreach (var cue in data.Cues)
        {
            WriteString(raw, cue.Lemma);
            WriteVarint(raw, cue.Senses.Length);
            foreach (var sense in cue.Senses)
            {
                raw.WriteByte((byte)sense.PartOfSpeech);
                WriteString(raw, sense.Meaning ?? string.Empty);
                raw.WriteByte((byte)sense.Prior);
            }

            WriteVarint(raw, cue.Hashes.Length);
            var previous = 0;
            for (var f = 0; f < cue.Hashes.Length; f++)
            {
                WriteVarint(raw, f == 0 ? cue.Hashes[f] : cue.Hashes[f] - previous);
                previous = cue.Hashes[f];
            }

            foreach (var weight in cue.Weights)
            {
                raw.WriteByte((byte)weight);
            }
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, level, leaveOpen: true))
        {
            brotli.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return output.ToArray();
    }

    private static void WriteVarint(Stream stream, int value) => VarintWriter.WriteVarint(stream, value);

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
