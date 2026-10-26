using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Writes the table of the basic model (<c>basic.bin</c>) that <c>DemaConsulting.WordSense</c> embeds.
/// </summary>
/// <remarks>
///     The layout is described on <c>BasicTables</c>, the only reader. Words, suffixes and lemmas are written in the
///     ordinal order of the data, so equal data always gives equal bytes. The runtime hyper-parameters (gamma, beta, the
///     three lambdas, the weight of the tagger, the inverse temperature, the weight of the tense model, the smoothing
///     of the clause state and the four numbers of the word prior) are written to the header from
///     <see cref="BasicHyperparameters"/>.
/// </remarks>
internal static class BasicTablesWriter
{
    private static readonly byte[] Magic = "WSB6"u8.ToArray();

    /// <summary>
    ///     Serializes the model data.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="inventoryHash">The analysis inventory hash of the word image the data was trained for (<c>WordImage.InventoryHash</c>); 32 bytes.</param>
    /// <param name="level">The Brotli compression level; the shipped file uses the smallest size, the tuner a fast level.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Write(BasicModelData data, ReadOnlySpan<byte> inventoryHash, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (inventoryHash.Length != 32)
        {
            throw new ArgumentException("The inventory hash needs 32 bytes.", nameof(inventoryHash));
        }

        using var raw = new MemoryStream();
        raw.Write(Magic);
        raw.Write(inventoryHash);
        var hp = data.Hyperparameters;
        foreach (var value in new[] { hp.Gamma, hp.Beta, hp.Lambda1, hp.Lambda2, hp.Lambda3, hp.HmmWeight, hp.InvTemperature, hp.TenseWeight, hp.ClauseSmoothing, hp.PriorShare, hp.PriorSmoothing, hp.PriorSenses, hp.PriorExponent })
        {
            WriteDouble(raw, value);
        }

        WriteVarint(raw, DemaConsulting.WordSense.PosTag.Count);
        foreach (var count in data.Unigrams.Concat(data.Bigrams).Concat(data.Trigrams).Concat(data.ClauseBigrams).Concat(data.ClauseTrigrams))
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
            raw.WriteByte(cue.Reliability);
            WriteVarint(raw, cue.Analyses.Length);
            foreach (var analysis in cue.Analyses)
            {
                WriteString(raw, analysis.Lemma);
                raw.WriteByte((byte)analysis.PartOfSpeech);
                WriteString(raw, analysis.Features);
                raw.WriteByte((byte)analysis.Prior);
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

        // The tense table: a flag byte, then the prior, the delta-coded hashes and one weight per hash
        if (data.Tense is { } tense)
        {
            raw.WriteByte(1);
            raw.WriteByte((byte)tense.Prior);
            WriteVarint(raw, tense.Hashes.Length);
            var last = 0;
            for (var f = 0; f < tense.Hashes.Length; f++)
            {
                WriteVarint(raw, f == 0 ? tense.Hashes[f] : tense.Hashes[f] - last);
                last = tense.Hashes[f];
            }

            foreach (var weight in tense.Weights)
            {
                raw.WriteByte((byte)weight);
            }
        }
        else
        {
            raw.WriteByte(0);
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
