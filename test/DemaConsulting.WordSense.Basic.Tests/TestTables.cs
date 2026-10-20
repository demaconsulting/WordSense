using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Builds small statistical tables for the tests, in the same layout the builder writes, together with a toy analysis
///     inventory that the tables are loaded against, so that no test of the tables needs the embedded word image.
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
    ///     The analysis inventory hash of the toy inventory; the toy tables carry it unless a test says otherwise.
    /// </summary>
    public static readonly byte[] ToyHash = SHA256.HashData("basic toy analysis inventory"u8);

    /// <summary>
    ///     The past tense of the verb <c>read</c>.
    /// </summary>
    public static readonly WordDetails ReadPast = new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");

    /// <summary>
    ///     The present tense of the verb <c>read</c>.
    /// </summary>
    public static readonly WordDetails ReadPres = new("read", PartOfSpeech.Verb, "Tense=Pres+VerbForm=Fin");

    /// <summary>
    ///     The past participle of the verb <c>read</c>.
    /// </summary>
    public static readonly WordDetails ReadPastParticiple = new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part");

    /// <summary>
    ///     The noun <c>record</c>.
    /// </summary>
    public static readonly WordDetails RecordNoun = new("record", PartOfSpeech.Noun, "Number=Sing");

    /// <summary>
    ///     The verb <c>record</c>.
    /// </summary>
    public static readonly WordDetails RecordVerb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");

    /// <summary>
    ///     The adjective <c>left</c>.
    /// </summary>
    public static readonly WordDetails LeftAdjective = new("left", PartOfSpeech.Adjective, "Degree=Pos");

    /// <summary>
    ///     The past tense of the verb <c>leave</c>, a form that is spelled <c>left</c>.
    /// </summary>
    public static readonly WordDetails LeavePast = new("leave", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");

    /// <summary>
    ///     The noun <c>cat</c>.
    /// </summary>
    public static readonly WordDetails CatNoun = new("cat", PartOfSpeech.Noun, "Number=Sing");

    private static readonly WordDetails[] ToyInventory =
    [
        ReadPast, ReadPres, ReadPastParticiple, RecordNoun, RecordVerb, LeftAdjective, LeavePast, CatNoun,
    ];

    /// <summary>
    ///     Builds the blob of a toy table.
    /// </summary>
    /// <param name="words">The tabled words.</param>
    /// <param name="suffixes">The tabled suffixes.</param>
    /// <param name="cues">The cue tables.</param>
    /// <param name="hmmWeight">The weight of the tagger.</param>
    /// <param name="invTemperature">The inverse temperature of the cues.</param>
    /// <param name="inventoryHash">The inventory hash to write, or null for <see cref="ToyHash"/>.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Build(
        (string Word, int ClosedMask, int[] Counts)[] words,
        (string Suffix, int[] Counts)[] suffixes,
        (string Lemma, CueAnalysis[] Analyses, ushort[] Hashes, sbyte[] Weights)[] cues,
        double hmmWeight = 1.5,
        double invTemperature = 0.5,
        byte[]? inventoryHash = null) => Compress(Raw(words, suffixes, cues, hmmWeight, invTemperature, inventoryHash));

    /// <summary>
    ///     Reads a blob against the toy inventory.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The table.</returns>
    public static BasicTables Load(ReadOnlySpan<byte> blob) => BasicTables.Load(blob, ToyHash, ToyExists);

    /// <summary>
    ///     Tells whether an analysis is in the toy inventory, with the rule of the word image: the lemma and part of
    ///     speech are equal and every required feature item is present (any features when none is required).
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="pos">The part of speech.</param>
    /// <param name="features">The required feature items joined by <c>+</c>, or empty.</param>
    /// <returns>True when the toy inventory has such an analysis.</returns>
    public static bool ToyExists(string lemma, PartOfSpeech pos, string features)
    {
        var required = features.Length == 0 ? [] : features.Split('+');
        return ToyInventory.Any(a => a.Lemma == lemma && a.PartOfSpeech == pos && required.All(item => a.Features.Split('+').Contains(item)));
    }

    /// <summary>
    ///     Builds the toy table used by most tests: a few closed-class words, the verb and noun "record" and the verb
    ///     "read" with a cue table that separates its past from its present.
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
            [("read", ReadColumns(), CueHashes(), CueWeights())]);
    }

    /// <summary>
    ///     Gets the columns of the "read" cue table: the past and the present tense, in ordinal order of their features.
    /// </summary>
    /// <returns>The columns.</returns>
    public static CueAnalysis[] ReadColumns() =>
        [new CueAnalysis("read", PartOfSpeech.Verb, "Tense=Past", -8), new CueAnalysis("read", PartOfSpeech.Verb, "Tense=Pres", -8)];

    /// <summary>
    ///     Gets the ascending feature hashes of the "read" table: those of the window words "yesterday" and "tomorrow".
    /// </summary>
    /// <returns>The hashes.</returns>
    public static ushort[] CueHashes()
    {
        var yesterday = new FeatureRef(CueModel.Window, "yesterday").Hash;
        var tomorrow = new FeatureRef(CueModel.Window, "tomorrow").Hash;
        return yesterday < tomorrow ? [yesterday, tomorrow] : [tomorrow, yesterday];
    }

    /// <summary>
    ///     Gets the weights of the "read" table for the hashes of "yesterday" and "tomorrow" in ascending hash order.
    /// </summary>
    /// <returns>The weights, feature-major, two columns per feature: the past and the present.</returns>
    public static sbyte[] CueWeights()
    {
        var yesterday = new FeatureRef(CueModel.Window, "yesterday").Hash;
        var tomorrow = new FeatureRef(CueModel.Window, "tomorrow").Hash;
        sbyte[] yesterdayRow = [-2, -40];
        sbyte[] tomorrowRow = [-40, -2];
        return yesterday < tomorrow ? [.. yesterdayRow, .. tomorrowRow] : [.. tomorrowRow, .. yesterdayRow];
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
        (string Lemma, CueAnalysis[] Analyses, ushort[] Hashes, sbyte[] Weights)[] cues,
        double hmmWeight,
        double invTemperature,
        byte[]? inventoryHash)
    {
        using var raw = new MemoryStream();
        raw.Write("WSB3"u8);
        raw.Write(inventoryHash ?? ToyHash);
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
        foreach (var (lemma, analyses, hashes, weights) in cues.OrderBy(c => c.Lemma, StringComparer.Ordinal))
        {
            Text(raw, lemma);
            Varint(raw, analyses.Length);
            foreach (var analysis in analyses)
            {
                Text(raw, analysis.Lemma);
                raw.WriteByte((byte)analysis.PartOfSpeech);
                Text(raw, analysis.Features);
                raw.WriteByte((byte)analysis.Prior);
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
