using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseTable"/>, <see cref="LemmaEntry"/> and <see cref="SenseWeights"/>.
/// </summary>
public class WordSenseTableTests
{
    private const string Sha = "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1";

    /// <summary>
    ///     The size of the dictionary section of a table without dictionary words: count, block size, decoded length
    ///     and compressed length.
    /// </summary>
    private const int EmptySectionLength = 4 + 2 + 4 + 4;

    /// <summary>
    ///     Creates a sense definition.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <param name="meaning">The pronunciation meaning, if any.</param>
    /// <param name="respelling">The respelling, if any.</param>
    /// <returns>The definition.</returns>
    internal static SenseDefinition Def(PartOfSpeech pos, string? meaning = null, string? respelling = null) => new(pos, meaning, respelling);

    /// <summary>
    ///     Creates a lemma entry that has trained weights.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senses">The senses.</param>
    /// <param name="kind">The model kind.</param>
    /// <param name="weights">The weight rows.</param>
    /// <param name="bias">The biases.</param>
    /// <param name="margin">The abstain margin.</param>
    /// <param name="dimension">The feature dimension.</param>
    /// <returns>The entry.</returns>
    internal static LemmaEntry Weighted(
        string lemma, IReadOnlyList<SenseDefinition> senses, LemmaModelKind kind, float[] weights, float[] bias, float margin, int dimension) =>
        new(lemma, senses, new SenseWeights(kind, weights, bias, margin, dimension));

    /// <summary>
    ///     Creates a dictionary entry: one meaning-free sense per part of speech.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="parts">The parts of speech.</param>
    /// <returns>The entry.</returns>
    internal static LemmaEntry Word(string lemma, params PartOfSpeech[] parts) => new(lemma, parts.Select(p => Def(p)).ToArray());

    /// <summary>
    ///     Builds a small table with a binary and a one-versus-rest lemma.
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateSmallTable()
    {
        var forms = new Dictionary<string, string>
        {
            ["bat"] = "bat",
            ["bats"] = "bat",
            ["lead"] = "lead",
            ["led"] = "lead",
        };
        return new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], SmallEntries(), forms);
    }

    /// <summary>
    ///     Creates the two trained entries of the small table.
    /// </summary>
    /// <returns>The entries, in ordinal order.</returns>
    private static LemmaEntry[] SmallEntries() =>
    [
        Weighted(
            "bat", [Def(PartOfSpeech.Noun, "animal", "bat"), Def(PartOfSpeech.Verb, "tool")],
            LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3),
        Weighted(
            "lead", [Def(PartOfSpeech.Noun, "metal"), Def(PartOfSpeech.Verb, "guide"), Def(PartOfSpeech.Noun, "wire")], LemmaModelKind.OneVersusRest,
            [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0.1f, -0.1f], 0.5f, 3),
    ];

    /// <summary>
    ///     Builds a table of trained lemmas (bat, lead) and dictionary words (cat, dog, walk, zebra).
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateMixedTable()
    {
        var trained = SmallEntries();
        LemmaEntry[] lemmas =
        [
            trained[0],
            Word("cat", PartOfSpeech.Noun),
            Word("dog", PartOfSpeech.Noun),
            trained[1],
            Word("walk", PartOfSpeech.Noun, PartOfSpeech.Verb),
            Word("zebra", PartOfSpeech.Noun),
        ];
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["bats"] = "bat", ["lead"] = "lead", ["led"] = "lead" };
        return new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], lemmas, forms);
    }

    /// <summary>
    ///     Writing and reading a table reproduces every value.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_PreservesAllValues()
    {
        var original = CreateSmallTable();

        var copy = WordSenseTable.Read(original.ToArray());

        Assert.Equal(original.ModelId, copy.ModelId);
        Assert.Equal(original.ModelSha256, copy.ModelSha256);
        Assert.Equal(original.Dimension, copy.Dimension);
        Assert.Equal(original.Mean, copy.Mean);
        Assert.Equal(original.Forms.OrderBy(f => f.Key), copy.Forms.OrderBy(f => f.Key));
        Assert.Equal(2, copy.ContextLemmas.Count);
        Assert.Equal(2, copy.Count);
        Assert.Equal(original.ContextLemmas[1].Weights!.Weights, copy.ContextLemmas[1].Weights!.Weights);
        Assert.Equal(LemmaModelKind.Binary, copy.ContextLemmas[0].Weights!.Kind);
        Assert.Equal(0.75f, copy.ContextLemmas[0].Weights!.AbstainMargin);
        Assert.Equal(["metal", "guide", "wire"], copy.ContextLemmas[1].Senses.Select(s => s.Meaning));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Noun], copy.ContextLemmas[1].Senses.Select(s => s.Pos));
        Assert.Equal(original.ContextLemmas[0].Senses, copy.ContextLemmas[0].Senses);
        Assert.Equal(new Pronunciation("animal", "bat"), copy.ContextLemmas[0].Candidates[0].Pronunciation);
        Assert.Null(copy.ContextLemmas[0].Candidates[1].Pronunciation!.Respelling);
        Assert.Equal(LemmaMorphologyRules.Default, copy.Morphology);

        // Candidates are shared across reads and carry the lemma and part of speech of their sense
        foreach (var lemma in copy.ContextLemmas)
        {
            Assert.Same(lemma.Candidates, lemma.Candidates);
            Assert.Same(lemma.Candidates[0], lemma.Candidates[0]);
            for (var i = 0; i < lemma.Senses.Count; i++)
            {
                Assert.Equal(lemma.Lemma, lemma.Candidates[i].Lemma);
                Assert.Equal(lemma.Senses[i].Pos, lemma.Candidates[i].PartOfSpeech);
            }
        }
    }

    /// <summary>
    ///     Dictionary lemmas round-trip, are enumerated in ordinal order with the trained ones, and are found by
    ///     <see cref="WordSenseTable.TryGetLemma"/>.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_WithDictionary_PreservesLemmasAndMorphology()
    {
        var original = CreateMixedTable();
        var withRules = new WordSenseTable(
            "m", Sha, 3, [0.1f, 0.2f, 0.3f], original.Lemmas.ToArray(), original.Forms, LemmaMorphologyRules.Plural);

        var copy = WordSenseTable.Read(withRules.ToArray());

        Assert.Equal(6, copy.Count);
        Assert.Equal(["bat", "lead"], copy.ContextLemmas.Select(l => l.Lemma));
        Assert.Equal(["bat", "cat", "dog", "lead", "walk", "zebra"], copy.Lemmas.Select(l => l.Lemma));
        Assert.Equal(LemmaMorphologyRules.Plural, copy.Morphology);
        Assert.True(copy.TryGetLemma("walk", out var walk));
        Assert.Null(walk.Weights);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], walk.Senses.Select(s => s.Pos));
        Assert.Equal(["walk", "walk"], walk.Candidates.Select(c => c.Lemma));
        Assert.All(walk.Candidates, c => Assert.Null(c.Pronunciation));
        Assert.True(copy.TryGetLemma("bat", out var bat));
        Assert.NotNull(bat.Weights);
        Assert.False(copy.TryGetLemma("Cat", out _));
        Assert.False(copy.TryGetLemma("cats", out _));
        Assert.Equal(withRules.ToArray(), copy.ToArray());
    }

    /// <summary>
    ///     Writing a string longer than the format allows throws, and an unknown or null lemma is handled.
    /// </summary>
    [Fact]
    public void WordSenseTable_WriteAndTryGetLemma_EdgeCases()
    {
        var small = CreateSmallTable();
        var longId = new WordSenseTable(new string('m', 70_000), Sha, 3, [0.1f, 0.2f, 0.3f], small.ContextLemmas, small.Forms);

        Assert.Throws<InvalidOperationException>(() => longId.ToArray());
        Assert.False(small.TryGetLemma("unknown", out _));
        Assert.True(small.TryGetLemma("bat", out var bat));
        Assert.Equal("bat", bat.Lemma);
        Assert.Throws<ArgumentNullException>(() => small.TryGetLemma(null!, out _));
    }

    /// <summary>
    ///     A table older than version 5 (versions 1 to 4 have no dictionary section) is rejected.
    /// </summary>
    /// <param name="version">The old version number written into the header.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void WordSenseTable_Read_OldVersion_Throws(int version)
    {
        var bytes = CreateSmallTable().ToArray();
        var body = bytes[..^32];
        Assert.Equal(5, body[4]);
        body[4] = (byte)version;
        var old = body.Concat(SHA256.HashData(body)).ToArray();

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(old));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A pronunciation round-trips through the binary table, with and without a respelling, and a lemma that is
    ///     not a heteronym round-trips without one.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_PreservesPronunciations()
    {
        var model = Weighted(
            "bow", [Def(PartOfSpeech.Noun, "bend", "bou"), Def(PartOfSpeech.Verb, "bend", "bou"), Def(PartOfSpeech.Noun, "weapon", "boh")],
            LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0f, 0f], 0.5f, 3);
        var plain = Weighted(
            "run", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [1f, 0f, 0f], [0f], 0.5f, 3);
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [model, plain],
            new Dictionary<string, string> { ["bow"] = "bow", ["run"] = "run" });

        var copy = WordSenseTable.Read(table.ToArray());

        Assert.Equal(["bend", "bend", "weapon"], copy.ContextLemmas[0].Candidates.Select(c => c.Pronunciation!.Meaning));
        Assert.Equal(["bou", "bou", "boh"], copy.ContextLemmas[0].Candidates.Select(c => c.Pronunciation!.Respelling));
        Assert.True(copy.ContextLemmas[0].IsHeteronym);
        Assert.False(copy.ContextLemmas[1].IsHeteronym);
        Assert.All(copy.ContextLemmas[1].Candidates, c => Assert.Null(c.Pronunciation));
    }

    /// <summary>
    ///     Output is byte-for-byte deterministic, with and without dictionary words.
    /// </summary>
    [Fact]
    public void WordSenseTable_ToArray_CalledTwice_ReturnsIdenticalBytes()
    {
        Assert.Equal(CreateSmallTable().ToArray(), CreateSmallTable().ToArray());
        Assert.Equal(CreateMixedTable().ToArray(), CreateMixedTable().ToArray());
    }

    /// <summary>
    ///     Writing to a stream matches <see cref="WordSenseTable.ToArray"/>.
    /// </summary>
    [Fact]
    public void WordSenseTable_Write_Stream_MatchesToArray()
    {
        var table = CreateMixedTable();
        using var stream = new MemoryStream();

        table.Write(stream);

        Assert.Equal(table.ToArray(), stream.ToArray());
    }

    /// <summary>
    ///     Every proper prefix of a valid table is rejected with a format exception.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_TruncatedAtEveryLength_Throws()
    {
        var bytes = CreateMixedTable().ToArray();

        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(prefix));
        }
    }

    /// <summary>
    ///     Flipping any single byte is detected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_AnyByteFlipped_Throws()
    {
        var bytes = CreateMixedTable().ToArray();

        for (var i = 0; i < bytes.Length; i++)
        {
            var copy = (byte[])bytes.Clone();
            copy[i] ^= 0x01;
            Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(copy));
        }
    }

    /// <summary>
    ///     Garbage and oversized declared counts are rejected without large allocations.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_Garbage_Throws()
    {
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(new byte[64]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Enumerable.Repeat((byte)0xFF, 4096).ToArray()));
    }

    /// <summary>
    ///     Lemmas must be sorted and forms must refer to a lemma that has a trained classifier.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_InconsistentInput_Throws()
    {
        var table = CreateSmallTable();
        var lemmas = table.ContextLemmas.Reverse().ToArray();

        Assert.Throws<ArgumentException>(() =>
            new WordSenseTable("m", Sha, 3, table.Mean, lemmas, table.Forms));
        Assert.Throws<ArgumentException>(() =>
            new WordSenseTable("m", Sha, 3, table.Mean, table.ContextLemmas, new Dictionary<string, string> { ["x"] = "missing" }));

        // A dictionary lemma has no classifier, so a form cannot point to it
        var mixed = CreateMixedTable();
        Assert.Throws<ArgumentException>(() =>
            new WordSenseTable("m", Sha, 3, table.Mean, mixed.Lemmas.ToArray(), new Dictionary<string, string> { ["cats"] = "cat" }));
    }

    /// <summary>
    ///     A form may not spell another lemma, and a dictionary lemma must be storable as lower-case letters.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_ShadowingAndBadDictionaryWords_Throw()
    {
        var mixed = CreateMixedTable();
        var lemmas = mixed.Lemmas.ToArray();
        float[] mean = [0.1f, 0.2f, 0.3f];

        // The form "dog" is a dictionary lemma; the form "lead" of "bat" is another trained lemma
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, lemmas, new Dictionary<string, string> { ["dog"] = "bat" }));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, lemmas, new Dictionary<string, string> { ["lead"] = "bat" }));

        // A form that spells its own lemma is the normal case
        _ = new WordSenseTable("m", Sha, 3, mean, lemmas, new Dictionary<string, string> { ["bat"] = "bat" });

        // Dictionary lemmas must be 1 to 255 lower-case letters
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [Word("can't", PartOfSpeech.Noun)], new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [Word("Cat", PartOfSpeech.Noun)], new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [Word(string.Empty, PartOfSpeech.Noun)], new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [Word(new string('a', 256), PartOfSpeech.Noun)], new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, lemmas, mixed.Forms, (LemmaMorphologyRules)8));
    }

    /// <summary>
    ///     A binary model scores {-s, +s}; the margin is twice the activation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_Binary_UsesActivationSign()
    {
        var model = CreateSmallTable().ContextLemmas[0].Weights!;

        var (best, margin) = model.Score([1f, 0f, 0f]);
        var (best2, _) = model.Score([-1f, 0f, 0f]);

        Assert.Equal(1, best);
        Assert.Equal(2 * 1.25f, margin);
        Assert.Equal(0, best2);
    }

    /// <summary>
    ///     A one-versus-rest model picks the highest row and reports the gap to the runner-up.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_OneVersusRest_PicksBestRow()
    {
        var model = CreateSmallTable().ContextLemmas[1].Weights!;

        var (best, margin) = model.Score([0f, 1f, 0f]);

        Assert.Equal(1, best);
        Assert.Equal(1.1f - 0f, margin, 4);
    }

    /// <summary>
    ///     A binary model's per-sense scores are {-a, +a} for activation a.
    /// </summary>
    [Fact]
    public void WordSenseTable_Scores_Binary_ReturnsNegatedAndActivation()
    {
        var model = CreateSmallTable().ContextLemmas[0].Weights!;
        var scores = new float[2];

        model.Scores([1f, 0f, 0f], scores);

        Assert.Equal(-1.25f, scores[0]);
        Assert.Equal(1.25f, scores[1]);
        Assert.Equal(2, model.SenseCount);
    }

    /// <summary>
    ///     A one-versus-rest model's per-sense scores are each row's activation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Scores_OneVersusRest_ReturnsPerRowActivation()
    {
        var model = CreateSmallTable().ContextLemmas[1].Weights!;
        var scores = new float[3];

        model.Scores([0f, 1f, 0f], scores);

        Assert.Equal([0f, 1.1f, -0.1f], scores);
        Assert.Equal(3, model.SenseCount);
    }

    /// <summary>
    ///     A feature or scores span of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Scores_LengthMismatch_Throws()
    {
        var model = CreateSmallTable().ContextLemmas[0].Weights!;

        Assert.Throws<ArgumentException>(() => model.Scores(new float[2], new float[2]));
        Assert.Throws<ArgumentException>(() => model.Scores(new float[3], new float[3]));
    }

    /// <summary>
    ///     Invalid weights are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_Weights_BadShapes_Throw()
    {
        Assert.Throws<ArgumentException>(() => Weighted("a", [Def(PartOfSpeech.Noun)], LemmaModelKind.Binary, [1f], [0f], 0f, 1));

        // A lemma with trained weights has at least two senses, so a runner-up always exists
        Assert.Throws<ArgumentException>(() => Weighted("a", [Def(PartOfSpeech.Noun)], LemmaModelKind.OneVersusRest, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [1f, 2f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [float.NaN], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => CreateSmallTable().ContextLemmas[0].Weights!.Score([1f]));

        // The senses must be as many as the weights score
        Assert.Throws<ArgumentException>(() => Weighted(
            "a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb), Def(PartOfSpeech.Adjective)], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => Weighted(
            "a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.OneVersusRest, [1f, 1f, 1f], [0f, 0f, 0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new SenseWeights(LemmaModelKind.OneVersusRest, [1f], [0f], 0f, 1));
    }

    /// <summary>
    ///     Invalid sense definitions and inconsistent sense sets are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaEntry_BadSenseDefinitions_Throw()
    {
        LemmaEntry Make(params SenseDefinition[] senses) =>
            Weighted("a", senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1);

        // Mixed meanings, duplicate pairs, a single meaning, a bad meaning, an undefined part of speech and bad respellings
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, "x"), Def(PartOfSpeech.Verb)));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun), Def(PartOfSpeech.Noun)));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, "x"), Def(PartOfSpeech.Verb, "x")));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, "X"), Def(PartOfSpeech.Verb, "y")));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, new string('a', 33)), Def(PartOfSpeech.Verb, "y")));
        Assert.Throws<ArgumentException>(() => Make(Def((PartOfSpeech)99), Def(PartOfSpeech.Verb)));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, "x", "a b"), Def(PartOfSpeech.Verb, "y")));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun, "x", "same"), Def(PartOfSpeech.Verb, "y", "same")));
        Assert.Throws<ArgumentException>(() => Make(Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb) with { Respelling = "x" }));
    }

    /// <summary>
    ///     An entry without weights has at least one sense and carries no meanings; one sense is enough.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaEntry_WithoutWeights_FollowsDictionaryRules()
    {
        var single = new LemmaEntry("cat", [Def(PartOfSpeech.Noun)]);

        Assert.Null(single.Weights);
        Assert.False(single.IsHeteronym);
        Assert.Equal(new WordDetails("cat", PartOfSpeech.Noun, null), Assert.Single(single.Candidates));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("cat", []));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("cat", [Def(PartOfSpeech.Noun, "a"), Def(PartOfSpeech.Verb, "b")]));
        Assert.Throws<ArgumentNullException>(() => new LemmaEntry(null!, [Def(PartOfSpeech.Noun)]));
        Assert.Throws<ArgumentNullException>(() => new LemmaEntry("cat", null!));
    }

    /// <summary>
    ///     A corrupted body with a valid checksum is rejected with a format exception, never anything else.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_MutatedBodyWithValidChecksum_ThrowsFormatExceptionOrLoads()
    {
        var bytes = CreateMixedTable().ToArray();
        var rejected = 0;
        var accepted = 0;

        for (var i = 0; i < bytes.Length - 32; i++)
        {
            var body = bytes[..^32];
            body[i] ^= 0xFF;
            var signed = body.Concat(SHA256.HashData(body)).ToArray();
            try
            {
                var loaded = WordSenseTable.Read(signed);

                // A mutation that only changes a numeric value may load; the result must still be usable
                Assert.NotEmpty(loaded.ToArray());
                accepted++;
            }
            catch (WordSenseTableFormatException)
            {
                rejected++;
            }
        }

        Assert.True(rejected > 0);
        Assert.True(accepted > 0);
        Assert.Equal(bytes.Length - 32, rejected + accepted);
    }

    /// <summary>
    ///     The embedded table loads and lists the seed and batch lemmas, and every form resolves to a lemma with a classifier.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_ContainsSeedLemmas()
    {
        var table = WordSenseTable.LoadEmbedded();

        Assert.True(table.ContextLemmas.Count >= 344);
        Assert.True(table.Count >= table.ContextLemmas.Count);
        Assert.True(table.TryGetLemma("record", out var record));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], record.Senses.Select(s => s.Pos));
        Assert.Equal(["noun", "verb"], record.Senses.Select(s => s.Meaning));
        Assert.True(table.TryGetLemma("read", out var read));
        Assert.Contains(read.Senses, s => s.Meaning == "past" && s.Pos == PartOfSpeech.Verb);
        Assert.Equal("record", table.Forms["records"]);
        Assert.Equal(384, table.Dimension);
        Assert.All(table.Forms.Values, lemma => Assert.True(table.TryGetLemma(lemma, out var entry) && entry.Weights is not null, lemma));
    }

    /// <summary>
    ///     The embedded table holds the dictionary: nouns resolve directly, the adverb rule derives 'quickly', and
    ///     determiners are not in the dictionary.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_ContainsDictionaryWords()
    {
        var table = WordSenseTable.LoadEmbedded();

        Assert.True(table.Count > 100_000);
        Assert.True(table.ContextLemmas.Count < 1_000);
        Assert.Equal(table.Count - table.ContextLemmas.Count, table.Lemmas.Count(l => l.Weights is null));
        Assert.True(table.Count - table.ContextLemmas.Count > 100_000);
        Assert.True(table.TryResolve("hunter", true, out var hunter));
        Assert.Equal(PartOfSpeech.Noun, Assert.Single(hunter.Entry.Senses).Pos);
        Assert.Null(hunter.Entry.Weights);
        Assert.True(table.TryResolve("quickly", true, out var quickly));
        Assert.Contains(PartOfSpeech.Adverb, quickly.Entry.Senses.Select(s => s.Pos));
        Assert.True(table.TryResolve("hunters", true, out var hunters));
        Assert.Equal("hunter", hunters.Entry.Lemma);
        Assert.False(table.TryResolve("the", true, out _));
        Assert.False(table.TryResolve("an", true, out _));
        Assert.False(table.TryResolve("hunter", false, out _));
        Assert.True(table.Morphology.HasFlag(LemmaMorphologyRules.Plural));
    }

    /// <summary>
    ///     In the embedded table every sense of every lemma that has meanings carries a pronunciation.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations()
    {
        var table = WordSenseTable.LoadEmbedded();

        var heteronyms = table.ContextLemmas.Where(l => l.IsHeteronym).ToList();
        Assert.NotEmpty(heteronyms);
        foreach (var lemma in heteronyms)
        {
            Assert.All(lemma.Candidates, c => Assert.NotNull(c.Pronunciation));
        }
    }

    /// <summary>
    ///     Builds a one-lemma table without forms; its body layout is fixed so tests can patch known offsets.
    /// </summary>
    /// <returns>The serialized table.</returns>
    private static byte[] CreateSingleLemmaBytes() => CreateSingleLemmaBytes(new Dictionary<string, string>());

    /// <summary>
    ///     Builds a one-lemma table with the given forms and no dictionary words.
    /// </summary>
    /// <param name="forms">The forms.</param>
    /// <returns>The serialized table.</returns>
    private static byte[] CreateSingleLemmaBytes(Dictionary<string, string> forms)
    {
        var table = new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [SmallEntries()[0]], forms);
        return table.ToArray();
    }

    /// <summary>
    ///     Applies a change to the body of a serialized table and signs the result with a valid checksum.
    /// </summary>
    /// <param name="bytes">The serialized table.</param>
    /// <param name="change">The change applied to the body (without checksum).</param>
    /// <returns>The re-signed bytes.</returns>
    private static byte[] Resign(byte[] bytes, Func<byte[], byte[]> change)
    {
        var body = change(bytes[..^32]);
        return body.Concat(SHA256.HashData(body)).ToArray();
    }

    /// <summary>
    ///     Replaces the (empty) dictionary section of a table with a hand-made one and re-signs the table.
    /// </summary>
    /// <param name="bytes">A serialized table without dictionary words.</param>
    /// <param name="count">The declared word count.</param>
    /// <param name="blockSize">The declared block size.</param>
    /// <param name="declaredRaw">The declared decoded length.</param>
    /// <param name="compressed">The compressed bytes.</param>
    /// <returns>The re-signed bytes.</returns>
    private static byte[] WithSection(byte[] bytes, uint count, ushort blockSize, uint declaredRaw, byte[] compressed) =>
        Resign(bytes, b =>
        {
            var section = new byte[EmptySectionLength + compressed.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(section, count);
            BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(4), blockSize);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(6), declaredRaw);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(10), (uint)compressed.Length);
            compressed.CopyTo(section, EmptySectionLength);
            return [.. b[..^EmptySectionLength], .. section];
        });

    /// <summary>
    ///     Replaces the dictionary section with the given decoded records.
    /// </summary>
    /// <param name="bytes">A serialized table without dictionary words.</param>
    /// <param name="count">The declared word count.</param>
    /// <param name="raw">The decoded records.</param>
    /// <returns>The re-signed bytes.</returns>
    private static byte[] WithRecords(byte[] bytes, uint count, byte[] raw) =>
        WithSection(bytes, count, 32, (uint)raw.Length, LexicalIndexTests.Deflate(raw));

    /// <summary>
    ///     A correctly signed table that declares more forms than its data can hold is rejected before any allocation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedFormCountBeyondData_Throws()
    {
        var bytes = Resign(
            CreateSingleLemmaBytes(),
            b =>
            {
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(b.Length - EmptySectionLength - 4), 1_000_000);
                return b;
            });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("form count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table that declares more lemmas than its data can hold is rejected before any allocation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedLemmaCountBeyondData_Throws()
    {
        var bytes = Resign(
            CreateSingleLemmaBytes(),
            b =>
            {
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(LemmaCountOffset), 90_000);
                return b;
            });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("lemma count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A dictionary section that declares a decoded size no DEFLATE stream could produce is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedImpossibleDeflateRatio_Throws()
    {
        var raw = new byte[] { 0, 1, (byte)'a', 1, 0, 1, (byte)'b', 1 };
        var deflated = LexicalIndexTests.Deflate(raw);
        var bytes = WithSection(CreateSingleLemmaBytes(), 2, 32, 4u * 1024 * 1024, deflated);

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
    }

    /// <summary>
    ///     A correctly signed table with a wrong magic is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBadMagic_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[0] ^= 0xFF; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table with extra bytes after the dictionary section is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedTrailingData_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => [.. b, 0]);

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("trailing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table that declares a dimension above the cap is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedDimensionAboveCap_Throws()
    {
        // The dimension follows magic, version, flags, the model id string and the 32-byte digest
        const int dimensionOffset = 8 + 2 + 16 + 32;
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[dimensionOffset] = 0x01; b[dimensionOffset + 1] = 0x10; return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
    }

    /// <summary>
    ///     A correctly signed table that enables a morphology rule that does not exist is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUndefinedMorphologyRule_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[6] = 8; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("morphology", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table with an unknown model kind is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUnknownModelKind_Throws()
    {
        // The kind byte precedes weights (3 floats), bias (1 float), the margin, the empty form count and the empty dictionary
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[b.Length - EmptySectionLength - 4 - 4 - 4 - 12 - 1] = 9; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("model kind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The offset of the lemma count in the single-lemma body: header, model id, digest, dimension and mean
    ///     precede it.
    /// </summary>
    private const int LemmaCountOffset = 8 + 2 + 16 + 32 + 4 + (3 * 4);

    /// <summary>
    ///     The offset of the sense count of the only lemma (after the lemma count and the string "bat").
    /// </summary>
    private const int SenseCountOffset = LemmaCountOffset + 4 + 2 + 3;

    /// <summary>
    ///     The offset of the part-of-speech byte of the first sense.
    /// </summary>
    private const int FirstSensePosOffset = SenseCountOffset + 2;

    /// <summary>
    ///     A correctly signed table that declares a lemma count above the cap is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedLemmaCountAboveCap_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(LemmaCountOffset), 100_001);
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table that declares a form count above the cap is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedFormCountAboveCap_Throws()
    {
        // With no forms the form count precedes the dictionary section
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(b.Length - EmptySectionLength - 4), 2_000_001);
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose lemma declares fewer than 2 or more than 64 senses is rejected.
    /// </summary>
    /// <param name="senseCount">The declared sense count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(65)]
    public void WordSenseTable_Read_ResignedSenseCountOutOfRange_Throws(int senseCount)
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(SenseCountOffset), (ushort)senseCount);
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("sense count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose sense has an undefined part of speech is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUndefinedPos_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[FirstSensePosOffset] = 99; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("part of speech", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose sense has a malformed meaning string is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedInvalidMeaning_Throws()
    {
        // The first meaning is "animal" (u16 length then six bytes); upper-case its first letter
        const int meaningOffset = FirstSensePosOffset + 1 + 2;
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            Assert.Equal((byte)'a', b[meaningOffset]);
            b[meaningOffset] = (byte)'A';
            return b;
        });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
    }

    /// <summary>
    ///     A correctly signed table with a form that points past the lemma list or repeats a form is rejected.
    /// </summary>
    /// <param name="duplicate">True to repeat a form, false to use an out-of-range lemma index.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordSenseTable_Read_ResignedBadFormEntry_Throws(bool duplicate)
    {
        // Two forms "bar" and "bat", each a u16 length, three bytes and a u32 lemma index, precede the dictionary section
        var bytes = Resign(CreateSingleLemmaBytes(new Dictionary<string, string> { ["bar"] = "bat", ["bat"] = "bat" }), b =>
        {
            const int entry = 2 + 3 + 4;
            var end = b.Length - EmptySectionLength;
            var first = end - (2 * entry);
            var second = end - entry;
            Assert.Equal("bar", System.Text.Encoding.UTF8.GetString(b, first + 2, 3));
            Assert.Equal("bat", System.Text.Encoding.UTF8.GetString(b, second + 2, 3));
            if (duplicate)
            {
                b[first + 4] = (byte)'t';
            }
            else
            {
                b[second + 5] = 1;
            }

            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("form", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table with a version above the current one is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedNewerVersion_Throws()
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[4] = 6; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("version 6", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose binary lemma declares more than two senses is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBinaryKindWithThreeSenses_Throws()
    {
        var lead = SmallEntries()[1];
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [lead], new Dictionary<string, string>());

        // The kind byte precedes weights (9 floats), bias (3 floats), the margin, the empty form count and the empty dictionary
        var bytes = Resign(table.ToArray(), b =>
        {
            var kindOffset = b.Length - EmptySectionLength - 4 - 4 - 12 - 36 - 1;
            Assert.Equal((byte)LemmaModelKind.OneVersusRest, b[kindOffset]);
            b[kindOffset] = (byte)LemmaModelKind.Binary;
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("inconsistent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose senses disagree as a set (two meanings with different respellings that
    ///     are made identical) is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedInconsistentSenseSet_Throws()
    {
        var model = Weighted(
            "bat", [Def(PartOfSpeech.Noun, "cccc", "xx"), Def(PartOfSpeech.Verb, "bbbb", "yy")],
            LemmaModelKind.Binary, [1f, 0f, 0f], [0f], 0.5f, 3);
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [model], new Dictionary<string, string>());

        // The second meaning now equals the first, so one meaning has two respellings
        var bytes = Resign(table.ToArray(), b =>
        {
            var index = b.AsSpan().IndexOf("bbbb"u8);
            Assert.True(index > 0);
            "cccc"u8.CopyTo(b.AsSpan(index));
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("inconsistent", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<ArgumentException>(ex.InnerException);
    }

    /// <summary>
    ///     A correctly signed dictionary section with a valid word is accepted (the baseline of the next tests).
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedValidDictionary_Loads()
    {
        var bytes = WithRecords(CreateSingleLemmaBytes(), 2, [0, 3, (byte)'c', (byte)'a', (byte)'t', 1, 3, 1, (byte)'s', 1]);

        var table = WordSenseTable.Read(bytes);

        Assert.Equal(["bat", "cat", "cats"], table.Lemmas.Select(l => l.Lemma));
    }

    /// <summary>
    ///     A correctly signed table whose dictionary records break a rule of the format is rejected.
    /// </summary>
    /// <param name="name">The scenario, for the test report.</param>
    /// <param name="count">The declared word count.</param>
    /// <param name="records">The decoded records, as hexadecimal bytes.</param>
    [Theory]
    [InlineData("unordered", 2u, "00 01 62 01 00 01 61 01")]
    [InlineData("duplicate", 2u, "00 01 61 01 01 00 61 01")]
    [InlineData("upper case", 1u, "00 01 41 01")]
    [InlineData("digit", 1u, "00 01 31 01")]
    [InlineData("no part of speech", 1u, "00 01 61 00")]
    [InlineData("block head with prefix", 1u, "01 01 61 01")]
    [InlineData("prefix longer than previous word", 2u, "00 01 61 01 05 01 62 01")]
    [InlineData("empty suffix", 1u, "00 00 01")]
    [InlineData("prefix not the longest", 2u, "00 02 61 62 01 00 02 61 63 01")]
    [InlineData("count smaller than data", 1u, "00 01 61 01 00 01 62 01")]
    [InlineData("truncated record", 1u, "00 05 61 62")]
    [InlineData("trailing byte", 1u, "00 01 61 01 00")]
    public void WordSenseTable_Read_ResignedBadDictionaryRecords_Throws(string name, uint count, string records)
    {
        var raw = Convert.FromHexString(records.Replace(" ", string.Empty, StringComparison.Ordinal));

        var ex = Record.Exception(() => WordSenseTable.Read(WithRecords(CreateSingleLemmaBytes(), count, raw)));

        Assert.True(ex is WordSenseTableFormatException, name);
    }

    /// <summary>
    ///     A dictionary section that inflates to more or fewer bytes than declared, has a bad block size, an excess
    ///     count or a declared size above the cap, or is not DEFLATE data, is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBadDictionarySection_Throws()
    {
        var baseBytes = CreateSingleLemmaBytes();
        byte[] raw = [0, 1, (byte)'a', 1, 0, 1, (byte)'b', 1];
        var deflated = LexicalIndexTests.Deflate(raw);

        // Declared decoded length too small (a bomb: the data inflates to more) and too large
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 32, (uint)raw.Length - 1, deflated)));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 32, (uint)raw.Length + 1, deflated)));

        // Caps on the word count and the decoded size, and an unsupported block size
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 400_001, 32, (uint)raw.Length, deflated)));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 32, (4u * 1024 * 1024) + 1, deflated)));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 16, (uint)raw.Length, deflated)));

        // Not DEFLATE data, no data for words, and data for no words
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 32, (uint)raw.Length, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF])));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 2, 32, (uint)raw.Length, [])));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 0, 32, (uint)raw.Length, deflated)));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(WithSection(baseBytes, 0, 32, 0, deflated)));

        // The same data with honest sizes loads
        Assert.Equal(3, WordSenseTable.Read(WithSection(baseBytes, 2, 32, (uint)raw.Length, deflated)).Count);
    }

    /// <summary>
    ///     A correctly signed table in which a dictionary word is also a trained lemma, or spells a form, is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedShadowing_Throws()
    {
        // The dictionary word "bat" is also the trained lemma "bat"
        var lemmaShadow = WithRecords(CreateSingleLemmaBytes(), 1, [0, 3, (byte)'b', (byte)'a', (byte)'t', 1]);
        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(lemmaShadow));
        Assert.Contains("inconsistent", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The dictionary word "bats" is also the form "bats" of "bat"
        var withForm = CreateSingleLemmaBytes(new Dictionary<string, string> { ["bats"] = "bat" });
        var formShadow = WithRecords(withForm, 1, [0, 4, (byte)'b', (byte)'a', (byte)'t', (byte)'s', 1]);
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(formShadow));

        // A dictionary word that only resembles a form is fine
        var fine = WithRecords(withForm, 1, [0, 3, (byte)'c', (byte)'a', (byte)'t', 1]);
        Assert.Equal(2, WordSenseTable.Read(fine).Count);
    }

    /// <summary>
    ///     A binary model whose activation is exactly zero selects the second sense with a margin of zero.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_BinaryZeroActivation_SelectsSecondSense()
    {
        var model = CreateSmallTable().ContextLemmas[0].Weights!;
        var scores = new float[2];

        // Weights [1, -2, 0.5] and bias 0.25: this feature gives -0.25 + 0.25 = 0 exactly
        var (best, margin) = model.Score([0f, 0.125f, 0f]);
        model.Scores([0f, 0.125f, 0f], scores);

        Assert.Equal(1, best);
        Assert.Equal(0f, margin);
        Assert.Equal(0f, scores[1]);
    }

    /// <summary>
    ///     A one-versus-rest model with equal best activations selects the first of them with a margin of zero.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_OneVersusRestTie_SelectsFirstHighest()
    {
        var model = CreateSmallTable().ContextLemmas[1].Weights!;

        // Rows 0 and 1 both score 0.1 (feature 0.1 on row 0; bias 0.1 on row 1); row 2 scores -0.1
        var (best, margin) = model.Score([0.1f, 0f, 0f]);

        Assert.Equal(0, best);
        Assert.Equal(0f, margin);
    }

    /// <summary>
    ///     The table constructor rejects null arguments.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_NullArguments_Throw()
    {
        var t = CreateSmallTable();
        float[] mean = [0.1f, 0.2f, 0.3f];

        Assert.Throws<ArgumentNullException>(() => new WordSenseTable(null!, Sha, 3, mean, t.ContextLemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", null!, 3, mean, t.ContextLemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, null!, t.ContextLemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, null!, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, t.ContextLemmas, null!));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, [t.ContextLemmas[0], null!], t.Forms));
    }

    /// <summary>
    ///     The table constructor rejects a malformed digest, a bad mean, duplicate lemmas and a dimension mismatch.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_InvalidValues_Throw()
    {
        var t = CreateSmallTable();
        float[] mean = [0.1f, 0.2f, 0.3f];
        var noForms = new Dictionary<string, string>();

        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha[..63], 3, mean, t.ContextLemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", new string('z', 64), 3, mean, t.ContextLemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, float.NaN, 0.3f], t.ContextLemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, float.PositiveInfinity, 0.3f], t.ContextLemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, 0.2f], t.ContextLemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [t.ContextLemmas[0], t.ContextLemmas[0]], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 4, [0f, 0f, 0f, 0f], t.ContextLemmas, noForms));
    }

    /// <summary>
    ///     The entry and weights constructors reject null arguments, non-finite values and a bias of the wrong length.
    /// </summary>
    [Fact]
    public void WordSenseTable_Weights_NullAndNonFiniteArguments_Throw()
    {
        SenseDefinition[] senses = [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)];

        Assert.Throws<ArgumentNullException>(() => Weighted(null!, senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => Weighted("a", null!, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => Weighted("a", senses, LemmaModelKind.Binary, null!, [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => Weighted("a", senses, LemmaModelKind.Binary, [1f], null!, 0f, 1));
        Assert.Throws<ArgumentNullException>(() => Weighted("a", [Def(PartOfSpeech.Noun), null!], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", senses, LemmaModelKind.Binary, [1f], [float.NaN], 0f, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", senses, LemmaModelKind.Binary, [1f], [0f], float.NaN, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", senses, LemmaModelKind.Binary, [1f], [0f], float.PositiveInfinity, 1));
        Assert.Throws<ArgumentException>(() => Weighted("a", senses, LemmaModelKind.Binary, [1f], [0f, 0f], 0f, 1));
    }

    /// <summary>
    ///     An undefined model kind is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Weights_UndefinedKind_Throws()
    {
        SenseDefinition[] senses = [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Weighted("a", senses, (LemmaModelKind)0, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Weighted("a", senses, (LemmaModelKind)9, [1f], [0f], 0f, 1));
    }

    /// <summary>
    ///     A model with a dimension that is not a multiple of the SIMD width scores like a scalar reference.
    /// </summary>
    /// <param name="dimension">The feature dimension.</param>
    [Theory]
    [InlineData(19)]
    [InlineData(37)]
    public void WordSenseTable_Scores_OddDimension_MatchesScalarReference(int dimension)
    {
        var weights = Enumerable.Range(0, 3 * dimension).Select(i => (float)Math.Sin(i * 0.7)).ToArray();
        var bias = new[] { 0.5f, -0.25f, 0.125f };
        var feature = Enumerable.Range(0, dimension).Select(i => (float)Math.Cos(i * 1.3)).ToArray();
        var model = new SenseWeights(LemmaModelKind.OneVersusRest, weights, bias, 0f, dimension);
        var scores = new float[3];

        model.Scores(feature, scores);

        for (var row = 0; row < 3; row++)
        {
            double expected = bias[row];
            for (var i = 0; i < dimension; i++)
            {
                expected += (double)weights[(row * dimension) + i] * feature[i];
            }

            Assert.Equal(expected, scores[row], 3);
        }
    }
}
