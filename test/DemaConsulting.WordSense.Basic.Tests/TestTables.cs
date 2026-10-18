using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Builds small statistical tables for the tests, in the same layout the builder writes.
/// </summary>
internal static class TestTables
{
    /// <summary>
    ///     The training sentences of the toy tables, as tag sequences: determiners and pronouns are followed by nouns and verbs.
    /// </summary>
    public static readonly int[][] Sentences =
    [
        [PosTag.Determiner, PosTag.Noun, PosTag.Verb, PosTag.Determiner, PosTag.Noun],
        [PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun],
        [PosTag.Determiner, PosTag.Noun, PosTag.Verb],
        [PosTag.Pronoun, PosTag.Verb, PosTag.Pronoun],
    ];

    /// <summary>
    ///     Builds the blob of a toy table.
    /// </summary>
    /// <param name="words">The tabled words.</param>
    /// <param name="suffixes">The tabled suffixes.</param>
    /// <param name="cues">The cue tables.</param>
    /// <param name="hmmWeight">The weight of the tagger.</param>
    /// <param name="invTemperature">The inverse temperature of the cues.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Build(
        (string Word, int ClosedMask, int[] Counts)[] words,
        (string Suffix, int[] Counts)[] suffixes,
        (string Lemma, CueSense[] Senses, ushort[] Hashes, sbyte[] Weights)[] cues,
        double hmmWeight = 1.5,
        double invTemperature = 0.5) => Compress(Raw(words, suffixes, cues, hmmWeight, invTemperature));

    /// <summary>
    ///     Builds the toy table used by most tests: a few closed-class words, the verb and noun "record" and the
    ///     heteronym "bass" with a cue table.
    /// </summary>
    /// <returns>The compressed blob.</returns>
    public static byte[] Default()
    {
        int[] Counts(params (int Tag, int Count)[] values)
        {
            var counts = new int[PosTag.Count];
            foreach (var (tag, count) in values)
            {
                counts[tag] = count;
            }

            return counts;
        }

        return Build(
            [
                ("the", 1 << PosTag.Determiner, Counts((PosTag.Determiner, 40))),
                ("they", 1 << PosTag.Pronoun, Counts((PosTag.Pronoun, 20))),
                ("record", 0, Counts((PosTag.Noun, 6), (PosTag.Verb, 6))),
            ],
            [("ed", [1, 9, 1, 0])],
            [
                (
                    "bass",
                    [new CueSense(PartOfSpeech.Noun, "fish", -8), new CueSense(PartOfSpeech.Noun, "music", -8)],
                    HeteronymHashes(),
                    HeteronymWeights()),
            ]);
    }

    /// <summary>
    ///     Gets the ascending feature hashes of the "bass" table: those of the window words "river" and "guitar".
    /// </summary>
    /// <returns>The hashes.</returns>
    public static ushort[] HeteronymHashes()
    {
        var river = new FeatureRef(CueModel.Window, "river").Hash;
        var guitar = new FeatureRef(CueModel.Window, "guitar").Hash;
        return river < guitar ? [river, guitar] : [guitar, river];
    }

    /// <summary>
    ///     Gets the weights of the "bass" table for the hashes of "river" and "guitar" in ascending hash order.
    /// </summary>
    /// <returns>The weights, feature-major, two senses per feature: fish and music.</returns>
    public static sbyte[] HeteronymWeights()
    {
        var river = new FeatureRef(CueModel.Window, "river").Hash;
        var guitar = new FeatureRef(CueModel.Window, "guitar").Hash;
        sbyte[] riverRow = [-2, -40];
        sbyte[] guitarRow = [-40, -2];
        return river < guitar ? [.. riverRow, .. guitarRow] : [.. guitarRow, .. riverRow];
    }

    /// <summary>
    ///     Decompresses a blob.
    /// </summary>
    /// <param name="blob">The blob.</param>
    /// <returns>The raw bytes.</returns>
    public static byte[] Decompress(byte[] blob)
    {
        using var input = new MemoryStream(blob);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        brotli.CopyTo(raw);
        return raw.ToArray();
    }

    /// <summary>
    ///     Compresses raw bytes the way the builder does.
    /// </summary>
    /// <param name="raw">The raw bytes.</param>
    /// <returns>The blob.</returns>
    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static byte[] Raw(
        (string Word, int ClosedMask, int[] Counts)[] words,
        (string Suffix, int[] Counts)[] suffixes,
        (string Lemma, CueSense[] Senses, ushort[] Hashes, sbyte[] Weights)[] cues,
        double hmmWeight,
        double invTemperature)
    {
        using var raw = new MemoryStream();
        raw.Write("WSB1"u8);
        foreach (var value in new[] { 0.75, 5, 0.1, 0.3, 0.6, hmmWeight, invTemperature })
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
            raw.Write(bytes);
        }

        Varint(raw, PosTag.Count);
        var c1 = new int[PosTag.Extended];
        var c2 = new int[PosTag.Extended * PosTag.Extended];
        var c3 = new int[PosTag.Extended * PosTag.Extended * PosTag.Extended];
        foreach (var sentence in Sentences)
        {
            int[] padded = [PosTag.Boundary, PosTag.Boundary, .. sentence, PosTag.Boundary];
            for (var i = 2; i < padded.Length; i++)
            {
                c1[padded[i]]++;
                c2[(padded[i - 1] * PosTag.Extended) + padded[i]]++;
                c3[(((padded[i - 2] * PosTag.Extended) + padded[i - 1]) * PosTag.Extended) + padded[i]]++;
            }
        }

        foreach (var count in c1.Concat(c2).Concat(c3))
        {
            Varint(raw, count);
        }

        Varint(raw, words.Length);
        foreach (var (word, closedMask, counts) in words.OrderBy(w => w.Word, StringComparer.Ordinal))
        {
            Text(raw, word);
            var presence = 0;
            for (var t = 0; t < counts.Length; t++)
            {
                presence |= counts[t] > 0 ? 1 << t : 0;
            }

            raw.WriteByte(closedMask != 0 ? (byte)1 : (byte)0);
            if (closedMask != 0)
            {
                raw.WriteByte((byte)(closedMask & 0xFF));
                raw.WriteByte((byte)(closedMask >> 8));
            }

            raw.WriteByte((byte)(presence & 0xFF));
            raw.WriteByte((byte)(presence >> 8));
            foreach (var count in counts.Where(c => c > 0))
            {
                Varint(raw, count);
            }
        }

        Varint(raw, suffixes.Length);
        foreach (var (suffix, counts) in suffixes.OrderBy(s => s.Suffix, StringComparer.Ordinal))
        {
            Text(raw, suffix);
            foreach (var count in counts)
            {
                Varint(raw, count);
            }
        }

        Varint(raw, cues.Length);
        foreach (var (lemma, senses, hashes, weights) in cues.OrderBy(c => c.Lemma, StringComparer.Ordinal))
        {
            Text(raw, lemma);
            Varint(raw, senses.Length);
            foreach (var sense in senses)
            {
                raw.WriteByte((byte)sense.PartOfSpeech);
                Text(raw, sense.Meaning ?? string.Empty);
                raw.WriteByte((byte)sense.Prior);
            }

            Varint(raw, hashes.Length);
            for (var f = 0; f < hashes.Length; f++)
            {
                Varint(raw, f == 0 ? hashes[f] : hashes[f] - hashes[f - 1]);
            }

            foreach (var weight in weights)
            {
                raw.WriteByte((byte)weight);
            }
        }

        return raw.ToArray();
    }

    private static void Varint(Stream stream, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        stream.WriteByte((byte)v);
    }

    private static void Text(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Varint(stream, bytes.Length);
        stream.Write(bytes);
    }
}
