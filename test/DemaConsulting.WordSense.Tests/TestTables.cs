using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Tests;

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
    /// <param name="tense">The tense table (prior, ascending feature hashes and one weight per hash), or null for a table without one.</param>
    /// <param name="tenseWeight">The weight of the tense model, written to the header.</param>
    /// <param name="sentences">The training sentences of the tag counts (tag sequences), or null for <see cref="Sentences"/>.</param>
    /// <param name="clauseSmoothing">The smoothing of the clause-state transitions, written to the header.</param>
    /// <param name="priorShare">The share of the word prior, written to the header (0 turns it off).</param>
    /// <param name="priorSmoothing">The smoothing of every slot of the word prior, written to the header.</param>
    /// <param name="priorSenses">The weight of every sense in the word prior, written to the header.</param>
    /// <param name="priorExponent">The exponent of the word prior, written to the header.</param>
    /// <param name="reliability">The stored reliability of every cue table (255 is fully reliable).</param>
    /// <param name="nounVerb">The noun/verb table (ascending feature hashes and one weight per hash), or null for a table without one.</param>
    /// <param name="nounVerbWeight">The weight of the noun/verb model, written to the header.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Build(
        (string Word, int ClosedMask, int[] Counts)[] words,
        (string Suffix, int[] Counts)[] suffixes,
        (string Lemma, CueAnalysis[] Analyses, ushort[] Hashes, sbyte[] Weights)[] cues,
        double hmmWeight = 1.5,
        double invTemperature = 0.5,
        byte[]? inventoryHash = null,
        (sbyte Prior, ushort[] Hashes, sbyte[] Weights)? tense = null,
        double tenseWeight = 1.0,
        int[][]? sentences = null,
        double clauseSmoothing = 4.0,
        double priorShare = 0.0,
        double priorSmoothing = 0.5,
        double priorSenses = 0.0,
        double priorExponent = 1.0,
        byte reliability = 255,
        (ushort[] Hashes, sbyte[] Weights)? nounVerb = null,
        double nounVerbWeight = 1.0) => Compress(Raw(words, suffixes, cues, hmmWeight, invTemperature, inventoryHash, tense, tenseWeight, sentences ?? Sentences, clauseSmoothing, [priorShare, priorSmoothing, priorSenses, priorExponent], reliability, nounVerb, nounVerbWeight));

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
        byte[]? inventoryHash,
        (sbyte Prior, ushort[] Hashes, sbyte[] Weights)? tense,
        double tenseWeight,
        int[][] sentences,
        double clauseSmoothing,
        double[] prior,
        byte reliability,
        (ushort[] Hashes, sbyte[] Weights)? nounVerb,
        double nounVerbWeight)
    {
        using var raw = new MemoryStream();
        raw.Write("WSB7"u8);
        raw.Write(inventoryHash ?? ToyHash);
        foreach (var value in new[] { 0.75, 5, 0.1, 0.3, 0.6, hmmWeight, invTemperature, tenseWeight, clauseSmoothing }.Concat(prior).Append(nounVerbWeight))
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
            raw.Write(bytes);
        }

        Varint(raw, PosTag.Count);
        var c1 = new int[PosTag.Extended];
        var c2 = new int[PosTag.Extended * PosTag.Extended];
        var c3 = new int[PosTag.Extended * PosTag.Extended * PosTag.Extended];
        var v2 = new int[c2.Length];
        var v3 = new int[c3.Length];
        foreach (var sentence in sentences)
        {
            int[] padded = [PosTag.Boundary, PosTag.Boundary, .. sentence, PosTag.Boundary];
            var hasVerb = 0;
            for (var i = 2; i < padded.Length; i++)
            {
                var bigram = (padded[i - 1] * PosTag.Extended) + padded[i];
                var trigram = (((padded[i - 2] * PosTag.Extended) + padded[i - 1]) * PosTag.Extended) + padded[i];
                c1[padded[i]]++;
                c2[bigram]++;
                c3[trigram]++;
                v2[bigram] += hasVerb;
                v3[trigram] += hasVerb;
                hasVerb = PosTag.NextClause(hasVerb, padded[i]);
            }
        }

        foreach (var count in c1.Concat(c2).Concat(c3).Concat(v2).Concat(v3))
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
            raw.WriteByte(reliability);
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

        // The tense section: a flag byte, then the prior, the delta-coded hashes and the weights
        if (tense is { } table)
        {
            raw.WriteByte(1);
            raw.WriteByte((byte)table.Prior);
            Varint(raw, table.Hashes.Length);
            for (var f = 0; f < table.Hashes.Length; f++)
            {
                Varint(raw, f == 0 ? table.Hashes[f] : table.Hashes[f] - table.Hashes[f - 1]);
            }

            foreach (var weight in table.Weights)
            {
                raw.WriteByte((byte)weight);
            }
        }
        else
        {
            raw.WriteByte(0);
        }

        // The noun/verb section: a flag byte, then the delta-coded hashes and the weights
        if (nounVerb is { } nvTable)
        {
            raw.WriteByte(1);
            Varint(raw, nvTable.Hashes.Length);
            for (var f = 0; f < nvTable.Hashes.Length; f++)
            {
                Varint(raw, f == 0 ? nvTable.Hashes[f] : nvTable.Hashes[f] - nvTable.Hashes[f - 1]);
            }

            foreach (var weight in nvTable.Weights)
            {
                raw.WriteByte((byte)weight);
            }
        }
        else
        {
            raw.WriteByte(0);
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
