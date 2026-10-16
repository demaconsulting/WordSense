using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseTable"/> and <see cref="LemmaEntry"/>: the lexicon-only table format.
/// </summary>
public class WordSenseTableTests
{
    /// <summary>
    ///     Creates a sense definition.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <param name="meaning">The pronunciation meaning, if any.</param>
    /// <param name="respelling">The respelling, if any.</param>
    /// <returns>The definition.</returns>
    internal static SenseDefinition Def(PartOfSpeech pos, string? meaning = null, string? respelling = null) => new(pos, meaning, respelling);

    /// <summary>
    ///     Creates a context lemma entry.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senses">The senses.</param>
    /// <returns>The entry.</returns>
    internal static LemmaEntry Context(string lemma, params SenseDefinition[] senses) => new(lemma, senses, true);

    /// <summary>
    ///     Creates a dictionary entry: one meaning-free sense per part of speech.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="parts">The parts of speech.</param>
    /// <returns>The entry.</returns>
    internal static LemmaEntry Word(string lemma, params PartOfSpeech[] parts) => new(lemma, parts.Select(p => Def(p)).ToArray());

    /// <summary>
    ///     Creates the two context entries of the small table.
    /// </summary>
    /// <returns>The entries, in ordinal order.</returns>
    private static LemmaEntry[] SmallEntries() =>
    [
        Context("bat", Def(PartOfSpeech.Noun, "animal", "bat"), Def(PartOfSpeech.Verb, "tool")),
        Context("lead", Def(PartOfSpeech.Noun, "metal"), Def(PartOfSpeech.Verb, "guide"), Def(PartOfSpeech.Noun, "wire")),
    ];

    private static Dictionary<string, string> SmallForms() =>
        new() { ["bat"] = "bat", ["bats"] = "bat", ["lead"] = "lead", ["led"] = "lead" };

    /// <summary>
    ///     Builds a small table with two context lemmas.
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateSmallTable() => new(SmallEntries(), SmallForms());

    /// <summary>
    ///     Builds a table of context lemmas (bat, lead) and dictionary words (cat, dog, walk, zebra).
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateMixedTable()
    {
        var context = SmallEntries();
        LemmaEntry[] lemmas =
        [
            context[0],
            Word("cat", PartOfSpeech.Noun),
            Word("dog", PartOfSpeech.Noun),
            context[1],
            Word("walk", PartOfSpeech.Noun, PartOfSpeech.Verb),
            Word("zebra", PartOfSpeech.Noun),
        ];
        return new WordSenseTable(lemmas, SmallForms());
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
    ///     Writing and reading a table reproduces every value.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_PreservesAllValues()
    {
        var original = CreateSmallTable();

        var copy = WordSenseTable.Read(original.ToArray());

        Assert.Equal(original.Forms.OrderBy(f => f.Key), copy.Forms.OrderBy(f => f.Key));
        Assert.Equal(2, copy.ContextLemmas.Count);
        Assert.Equal(2, copy.Count);
        Assert.All(copy.ContextLemmas, l => Assert.True(l.IsContextual));
        Assert.Equal(["metal", "guide", "wire"], copy.ContextLemmas[1].Senses.Select(s => s.Meaning));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Noun], copy.ContextLemmas[1].Senses.Select(s => s.Pos));
        Assert.Equal(original.ContextLemmas[0].Senses, copy.ContextLemmas[0].Senses);
        Assert.Equal(new Pronunciation("animal", "bat"), copy.ContextLemmas[0].Candidates[0].Pronunciation);
        Assert.Null(copy.ContextLemmas[0].Candidates[1].Pronunciation!.Respelling);
        Assert.Equal(LemmaMorphologyRules.Default, copy.Morphology);
        Assert.Equal(original.Sha256, copy.Sha256);

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
    ///     Dictionary lemmas and the morphology rules survive a round trip.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_WithDictionary_PreservesLemmasAndMorphology()
    {
        var original = CreateMixedTable();

        var copy = WordSenseTable.Read(original.ToArray());

        Assert.Equal(6, copy.Count);
        Assert.Equal(["bat", "lead"], copy.ContextLemmas.Select(l => l.Lemma));
        Assert.Equal(["bat", "cat", "dog", "lead", "walk", "zebra"], copy.Lemmas.Select(l => l.Lemma));
        Assert.True(copy.TryGetLemma("walk", out var walk));
        Assert.False(walk.IsContextual);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], walk.Senses.Select(s => s.Pos));
        Assert.True(copy.TryGetLemma("bat", out var bat));
        Assert.True(bat.IsContextual);
        Assert.True(copy.TryResolve("cats", true, out var cats));
        Assert.Equal("cat", cats.Entry.Lemma);
        Assert.Equal(original.Sha256, copy.Sha256);
        Assert.Equal(original.ToArray(), copy.ToArray());

        var noMorphology = new WordSenseTable(SmallEntries(), SmallForms(), LemmaMorphologyRules.None);
        Assert.Equal(LemmaMorphologyRules.None, WordSenseTable.Read(noMorphology.ToArray()).Morphology);
        Assert.NotEqual(noMorphology.Sha256, CreateSmallTable().Sha256);
    }

    /// <summary>
    ///     TryGetLemma and the digest handle the edge cases.
    /// </summary>
    [Fact]
    public void WordSenseTable_TryGetLemma_EdgeCases()
    {
        var table = CreateMixedTable();

        Assert.True(table.TryGetLemma("bat", out _));
        Assert.True(table.TryGetLemma("zebra", out _));
        Assert.False(table.TryGetLemma("bats", out _));
        Assert.False(table.TryGetLemma(string.Empty, out _));
        Assert.Throws<ArgumentNullException>(() => table.TryGetLemma(null!, out _));
        Assert.Equal(64, table.Sha256.Length);
        Assert.Equal(table.Sha256, table.Sha256.ToLowerInvariant());
    }

    /// <summary>
    ///     Serializing twice gives identical bytes, and the stream overload matches the array overload.
    /// </summary>
    [Fact]
    public void WordSenseTable_ToArray_AndWrite_AreDeterministic()
    {
        var table = CreateMixedTable();
        using var stream = new MemoryStream();

        table.Write(stream);

        Assert.Equal(table.ToArray(), table.ToArray());
        Assert.Equal(table.ToArray(), stream.ToArray());
        Assert.Throws<ArgumentNullException>(() => table.Write(null!));
    }

    /// <summary>
    ///     Every proper prefix of a serialized table is rejected with a format exception.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_TruncatedAtEveryLength_Throws()
    {
        var bytes = CreateMixedTable().ToArray();

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes.AsSpan(0, length)));
        }
    }

    /// <summary>
    ///     Flipping any single byte is detected by the checksum.
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
    ///     Garbage input is rejected with a format exception.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_Garbage_Throws()
    {
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read([]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(new byte[100]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read("not a table at all, just some text"u8));
    }

    /// <summary>
    ///     A table with a valid checksum but a wrong signature is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBadMagic_Throws()
    {
        var bytes = Resign(CreateSmallTable().ToArray(), b => { b[0] ^= 0xFF; return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
    }

    /// <summary>
    ///     A table that carries a weights-table signature is not a lexicon table.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_WeightsMagic_Throws()
    {
        var bytes = Resign(CreateSmallTable().ToArray(), b => { "WSNW"u8.CopyTo(b); return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
    }

    /// <summary>
    ///     Only the current version is read; older and newer versions are rejected.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void WordSenseTable_Read_OtherVersion_Throws(int version)
    {
        var bytes = Resign(CreateSmallTable().ToArray(), b => { b[4] = (byte)version; b[5] = 0; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Trailing data after the dictionary section and undefined morphology flags are rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedTrailingDataOrUndefinedFlags_Throws()
    {
        var bytes = CreateSmallTable().ToArray();

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => [.. b, 0])));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[6] = 8; return b; })));
    }

    /// <summary>
    ///     A lemma with an undefined part of speech is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUndefinedPos_Throws()
    {
        var bytes = CreateSmallTable().ToArray();

        // Header 8 bytes, lemma count 4, then the first lemma: length 2 + "bat" 3 + sense count 2, then the first part of speech
        const int posOffset = 8 + 4 + 2 + 3 + 2;
        Assert.Equal((byte)PartOfSpeech.Noun, bytes[posOffset]);

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[posOffset] = 99; return b; })));
    }

    /// <summary>
    ///     A lemma count or sense count that the data cannot back, or a lemma with a single sense, is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBadCounts_Throws()
    {
        var bytes = CreateSmallTable().ToArray();

        // Lemma count 8..11: claim far more lemmas than the data holds
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[8] = 0xFF; return b; })));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[11] = 0x7F; return b; })));

        // Sense count of the first lemma: offset 8 + 4 + 2 + 3
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[17] = 1; return b; })));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Resign(bytes, b => { b[17] = 65; return b; })));
    }

    /// <summary>
    ///     Context lemmas that are not in ordinal order are rejected on read.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUnorderedLemmas_Throws()
    {
        var bytes = CreateSmallTable().ToArray();

        // Rename "bat" to "zat" so it sorts after "lead"
        var changed = Resign(bytes, b => { b[14] = (byte)'z'; return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(changed));
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

                // A mutation that only changes a text value may load; the result must still be usable
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
    ///     Inconsistent constructor input is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_InconsistentInput_Throws()
    {
        var context = SmallEntries();

        // Not in ordinal order, duplicated, and a form for a missing lemma
        Assert.Throws<ArgumentException>(() => new WordSenseTable([context[1], context[0]], SmallForms()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable([context[0], context[0]], SmallForms()));
        Assert.Throws<ArgumentException>(() => new WordSenseTable(context, new Dictionary<string, string> { ["x"] = "missing" }));

        // A form may not point at a dictionary lemma, since only context lemmas have forms
        Assert.Throws<ArgumentException>(() => new WordSenseTable([context[0], Word("cat", PartOfSpeech.Noun)], new Dictionary<string, string> { ["cats"] = "cat" }));

        // A context lemma needs at least two senses
        Assert.Throws<ArgumentException>(() => new WordSenseTable([Context("solo", Def(PartOfSpeech.Noun))], new Dictionary<string, string>()));
    }

    /// <summary>
    ///     A form or lemma that shadows another lemma, and bad dictionary words, are rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_ShadowingAndBadDictionaryWords_Throw()
    {
        var context = SmallEntries();
        LemmaEntry[] withCat = [context[0], Word("cat", PartOfSpeech.Noun)];
        var noForms = new Dictionary<string, string>();

        // A form spelling a dictionary word, and a form spelling another context lemma
        Assert.Throws<ArgumentException>(() => new WordSenseTable(withCat, new Dictionary<string, string> { ["cat"] = "bat" }));
        Assert.Throws<ArgumentException>(() => new WordSenseTable(context, new Dictionary<string, string> { ["lead"] = "bat" }));

        // Dictionary lemmas must be 1 to 255 lower-case letters
        Assert.Throws<ArgumentException>(() => new WordSenseTable([Word("can't", PartOfSpeech.Noun)], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable([Word("Cat", PartOfSpeech.Noun)], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable([Word(string.Empty, PartOfSpeech.Noun)], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable([Word(new string('a', 256), PartOfSpeech.Noun)], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable(context, SmallForms(), (LemmaMorphologyRules)8));
    }

    /// <summary>
    ///     Null arguments are rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable(null!, new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable(SmallEntries(), null!));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable([null!], new Dictionary<string, string>()));
    }

    /// <summary>
    ///     Invalid sense definitions and inconsistent sense sets are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaEntry_BadSenseDefinitions_Throw()
    {
        LemmaEntry Make(params SenseDefinition[] senses) => new("a", senses, true);

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
    ///     A dictionary entry has at least one sense and carries no meanings; one sense is enough.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaEntry_Dictionary_FollowsDictionaryRules()
    {
        var single = new LemmaEntry("cat", [Def(PartOfSpeech.Noun)]);

        Assert.False(single.IsContextual);
        Assert.False(single.IsHeteronym);
        Assert.Equal(new WordDetails("cat", PartOfSpeech.Noun, null), Assert.Single(single.Candidates));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("cat", []));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("cat", [Def(PartOfSpeech.Noun, "a"), Def(PartOfSpeech.Verb, "b")]));
        Assert.Throws<ArgumentNullException>(() => new LemmaEntry(null!, [Def(PartOfSpeech.Noun)]));
        Assert.Throws<ArgumentNullException>(() => new LemmaEntry("cat", null!));
    }

    /// <summary>
    ///     The embedded table loads and lists the seed and batch lemmas, and every form resolves to a context lemma.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_ContainsSeedLemmas()
    {
        var table = WordSenseTable.LoadEmbedded();

        Assert.True(table.ContextLemmas.Count >= 344);
        Assert.True(table.Count >= table.ContextLemmas.Count);
        Assert.True(table.TryGetLemma("record", out var record));
        Assert.True(record.IsContextual);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], record.Senses.Select(s => s.Pos));
        Assert.Equal(["noun", "verb"], record.Senses.Select(s => s.Meaning));
        Assert.True(table.TryGetLemma("read", out var read));
        Assert.Contains(read.Senses, s => s.Meaning == "past" && s.Pos == PartOfSpeech.Verb);
        Assert.Equal("record", table.Forms["records"]);
        Assert.All(table.Forms.Values, lemma => Assert.True(table.TryGetLemma(lemma, out var entry) && entry.IsContextual, lemma));
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
        Assert.Equal(table.Count - table.ContextLemmas.Count, table.Lemmas.Count(l => !l.IsContextual));
        Assert.True(table.TryResolve("hunter", true, out var hunter));
        Assert.Equal(PartOfSpeech.Noun, Assert.Single(hunter.Entry.Senses).Pos);
        Assert.False(hunter.Entry.IsContextual);
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
    ///     The embedded lexicon round-trips through its serialized form.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_RoundTrips()
    {
        var table = WordSenseTable.LoadEmbedded();

        var bytes = table.ToArray();
        var copy = WordSenseTable.Read(bytes);

        // The digest is that of the bytes read (DEFLATE output may differ between runtimes), the content is the same
        Assert.Equal(Convert.ToHexString(bytes.AsSpan()[^32..]).ToLowerInvariant(), copy.Sha256);
        Assert.Equal(table.Count, copy.Count);
    }
}
