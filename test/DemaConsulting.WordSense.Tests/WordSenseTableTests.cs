namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseTable"/> and <see cref="LemmaModel"/>.
/// </summary>
public class WordSenseTableTests
{
    private const string Sha = "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1";

    /// <summary>
    ///     Creates a sense definition.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <param name="meaning">The pronunciation meaning, if any.</param>
    /// <param name="respelling">The respelling, if any.</param>
    /// <returns>The definition.</returns>
    internal static SenseDefinition Def(PartOfSpeech pos, string? meaning = null, string? respelling = null) => new(pos, meaning, respelling);

    /// <summary>
    ///     Builds a small table with a binary and a one-versus-rest lemma.
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateSmallTable()
    {
        var binary = new LemmaModel(
            "bat", [Def(PartOfSpeech.Noun, "animal", "bat"), Def(PartOfSpeech.Verb, "tool")],
            LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3);
        var multi = new LemmaModel(
            "lead", [Def(PartOfSpeech.Noun, "metal"), Def(PartOfSpeech.Verb, "guide"), Def(PartOfSpeech.Noun, "wire")], LemmaModelKind.OneVersusRest,
            [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0.1f, -0.1f], 0.5f, 3);
        var forms = new Dictionary<string, string>
        {
            ["bat"] = "bat",
            ["bats"] = "bat",
            ["lead"] = "lead",
            ["led"] = "lead",
        };
        return new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [binary, multi], forms);
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
        Assert.Equal(2, copy.Lemmas.Count);
        Assert.Equal(original.Lemmas[1].Weights, copy.Lemmas[1].Weights);
        Assert.Equal(LemmaModelKind.Binary, copy.Lemmas[0].Kind);
        Assert.Equal(0.75f, copy.Lemmas[0].AbstainMargin);
        Assert.Equal(["metal", "guide", "wire"], copy.Lemmas[1].Senses.Select(s => s.Meaning));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Noun], copy.Lemmas[1].Senses.Select(s => s.Pos));
        Assert.Equal(original.Lemmas[0].Senses, copy.Lemmas[0].Senses);
        Assert.Equal(new Pronunciation("animal", "bat"), copy.Lemmas[0].Candidates[0].Pronunciation);
        Assert.Null(copy.Lemmas[0].Candidates[1].Pronunciation!.Respelling);

        // Candidates are shared across reads and carry the lemma and part of speech of their sense
        foreach (var lemma in copy.Lemmas)
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
    ///     Writing a string longer than the format allows throws, and an unknown or null lemma is handled.
    /// </summary>
    [Fact]
    public void WordSenseTable_WriteAndTryGetLemma_EdgeCases()
    {
        var small = CreateSmallTable();
        var longId = new WordSenseTable(new string('m', 70_000), Sha, 3, [0.1f, 0.2f, 0.3f], small.Lemmas, small.Forms);

        Assert.Throws<InvalidOperationException>(() => longId.ToArray());
        Assert.False(small.TryGetLemma("unknown", out _));
        Assert.True(small.TryGetLemma("bat", out var bat));
        Assert.Equal("bat", bat.Lemma);
        Assert.Throws<ArgumentNullException>(() => small.TryGetLemma(null!, out _));
    }

    /// <summary>
    ///     A table older than version 4 (versions 1 to 3 describe senses with names and tags) is rejected.
    /// </summary>
    /// <param name="version">The old version number written into the header.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WordSenseTable_Read_OldVersion_Throws(int version)
    {
        var bytes = CreateSmallTable().ToArray();
        var body = bytes[..^32];
        Assert.Equal(4, body[4]);
        body[4] = (byte)version;
        var old = body.Concat(System.Security.Cryptography.SHA256.HashData(body)).ToArray();

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
        var model = new LemmaModel(
            "bow", [Def(PartOfSpeech.Noun, "bend", "bou"), Def(PartOfSpeech.Verb, "bend", "bou"), Def(PartOfSpeech.Noun, "weapon", "boh")],
            LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0f, 0f], 0.5f, 3);
        var plain = new LemmaModel(
            "run", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [1f, 0f, 0f], [0f], 0.5f, 3);
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [model, plain],
            new Dictionary<string, string> { ["bow"] = "bow", ["run"] = "run" });

        var copy = WordSenseTable.Read(table.ToArray());

        Assert.Equal(["bend", "bend", "weapon"], copy.Lemmas[0].Candidates.Select(c => c.Pronunciation!.Meaning));
        Assert.Equal(["bou", "bou", "boh"], copy.Lemmas[0].Candidates.Select(c => c.Pronunciation!.Respelling));
        Assert.True(copy.Lemmas[0].IsHeteronym);
        Assert.False(copy.Lemmas[1].IsHeteronym);
        Assert.All(copy.Lemmas[1].Candidates, c => Assert.Null(c.Pronunciation));
    }

    /// <summary>
    ///     Output is byte-for-byte deterministic.
    /// </summary>
    [Fact]
    public void WordSenseTable_ToArray_CalledTwice_ReturnsIdenticalBytes()
    {
        Assert.Equal(CreateSmallTable().ToArray(), CreateSmallTable().ToArray());
    }

    /// <summary>
    ///     Writing to a stream matches <see cref="WordSenseTable.ToArray"/>.
    /// </summary>
    [Fact]
    public void WordSenseTable_Write_Stream_MatchesToArray()
    {
        var table = CreateSmallTable();
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
        var bytes = CreateSmallTable().ToArray();

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
        var bytes = CreateSmallTable().ToArray();

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
    ///     Lemmas must be sorted and forms must refer to a known lemma.
    /// </summary>
    [Fact]
    public void WordSenseTable_Constructor_InconsistentInput_Throws()
    {
        var table = CreateSmallTable();
        var lemmas = table.Lemmas.Reverse().ToArray();

        Assert.Throws<ArgumentException>(() =>
            new WordSenseTable("m", Sha, 3, table.Mean, lemmas, table.Forms));
        Assert.Throws<ArgumentException>(() =>
            new WordSenseTable("m", Sha, 3, table.Mean, table.Lemmas, new Dictionary<string, string> { ["x"] = "missing" }));
    }

    /// <summary>
    ///     A binary model scores {-s, +s}; the margin is twice the activation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_Binary_UsesActivationSign()
    {
        var model = CreateSmallTable().Lemmas[0];

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
        var model = CreateSmallTable().Lemmas[1];

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
        var model = CreateSmallTable().Lemmas[0];
        var scores = new float[2];

        model.Scores([1f, 0f, 0f], scores);

        Assert.Equal(-1.25f, scores[0]);
        Assert.Equal(1.25f, scores[1]);
    }

    /// <summary>
    ///     A one-versus-rest model's per-sense scores are each row's activation.
    /// </summary>
    [Fact]
    public void WordSenseTable_Scores_OneVersusRest_ReturnsPerRowActivation()
    {
        var model = CreateSmallTable().Lemmas[1];
        var scores = new float[3];

        model.Scores([0f, 1f, 0f], scores);

        Assert.Equal([0f, 1.1f, -0.1f], scores);
    }

    /// <summary>
    ///     A feature or scores span of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Scores_LengthMismatch_Throws()
    {
        var model = CreateSmallTable().Lemmas[0];

        Assert.Throws<ArgumentException>(() => model.Scores(new float[2], new float[2]));
        Assert.Throws<ArgumentException>(() => model.Scores(new float[3], new float[3]));
    }

    /// <summary>
    ///     Invalid models are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaModel_BadShapes_Throw()
    {
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def(PartOfSpeech.Noun)], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [1f, 2f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)], LemmaModelKind.Binary, [float.NaN], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => CreateSmallTable().Lemmas[0].Score([1f]));
    }

    /// <summary>
    ///     Invalid sense definitions and inconsistent sense sets are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaModel_BadSenseDefinitions_Throw()
    {
        LemmaModel Make(params SenseDefinition[] senses) =>
            new("a", senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1);

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
    ///     A corrupted body with a valid checksum is rejected with a format exception, never anything else.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_MutatedBodyWithValidChecksum_ThrowsFormatExceptionOrLoads()
    {
        var bytes = CreateSmallTable().ToArray();
        var rejected = 0;
        var accepted = 0;

        for (var i = 0; i < bytes.Length - 32; i++)
        {
            var body = bytes[..^32];
            body[i] ^= 0xFF;
            var signed = body.Concat(System.Security.Cryptography.SHA256.HashData(body)).ToArray();
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

        Assert.True(table.Lemmas.Count >= 344);
        Assert.True(table.TryGetLemma("record", out var record));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], record.Senses.Select(s => s.Pos));
        Assert.Equal(["noun", "verb"], record.Senses.Select(s => s.Meaning));
        Assert.True(table.TryGetLemma("read", out var read));
        Assert.Contains(read.Senses, s => s.Meaning == "past" && s.Pos == PartOfSpeech.Verb);
        Assert.Equal("record", table.Forms["records"]);
        Assert.Equal(384, table.Dimension);
        Assert.All(table.Forms.Values, lemma => Assert.True(table.TryGetLemma(lemma, out _), lemma));
    }

    /// <summary>
    ///     In the embedded table every sense of every lemma that has meanings carries a pronunciation.
    /// </summary>
    [Fact]
    public void WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations()
    {
        var table = WordSenseTable.LoadEmbedded();

        var heteronyms = table.Lemmas.Where(l => l.IsHeteronym).ToList();
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
    private static byte[] CreateSingleLemmaBytes()
    {
        var bat = CreateSmallTable().Lemmas[0];
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [bat], new Dictionary<string, string>());
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
        return body.Concat(System.Security.Cryptography.SHA256.HashData(body)).ToArray();
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
    ///     A correctly signed table with extra bytes after the last form is rejected.
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
    ///     A correctly signed table with an unknown model kind is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedUnknownModelKind_Throws()
    {
        // The kind byte precedes weights (3 floats), bias (1 float), the margin and the empty form count
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[b.Length - 4 - 4 - 4 - 12 - 1] = 9; return b; });

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
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(LemmaCountOffset), 100_001);
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
        // With no forms the body ends with the u32 form count
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(b.Length - 4), 2_000_001);
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
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(SenseCountOffset), (ushort)senseCount);
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
        // Two forms "bar" and "bat", each a u16 length, three bytes and a u32 lemma index, follow the form count
        var bat = CreateSmallTable().Lemmas[0];
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [bat],
            new Dictionary<string, string> { ["bar"] = "bat", ["bat"] = "bat" });
        var bytes = Resign(table.ToArray(), b =>
        {
            const int entry = 2 + 3 + 4;
            var first = b.Length - (2 * entry);
            var second = b.Length - entry;
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
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[4] = 5; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("version 5", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose binary lemma declares more than two senses is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedBinaryKindWithThreeSenses_Throws()
    {
        var lead = CreateSmallTable().Lemmas[1];
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [lead], new Dictionary<string, string>());

        // The kind byte precedes weights (9 floats), bias (3 floats), the margin and the empty form count
        var bytes = Resign(table.ToArray(), b =>
        {
            var kindOffset = b.Length - 4 - 4 - 12 - 36 - 1;
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
        var model = new LemmaModel(
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
    ///     A binary model whose activation is exactly zero selects the second sense with a margin of zero.
    /// </summary>
    [Fact]
    public void WordSenseTable_Score_BinaryZeroActivation_SelectsSecondSense()
    {
        var model = CreateSmallTable().Lemmas[0];
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
        var model = CreateSmallTable().Lemmas[1];

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

        Assert.Throws<ArgumentNullException>(() => new WordSenseTable(null!, Sha, 3, mean, t.Lemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", null!, 3, mean, t.Lemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, null!, t.Lemmas, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, null!, t.Forms));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, t.Lemmas, null!));
        Assert.Throws<ArgumentNullException>(() => new WordSenseTable("m", Sha, 3, mean, [t.Lemmas[0], null!], t.Forms));
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

        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha[..63], 3, mean, t.Lemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", new string('z', 64), 3, mean, t.Lemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, float.NaN, 0.3f], t.Lemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, float.PositiveInfinity, 0.3f], t.Lemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, [0.1f, 0.2f], t.Lemmas, noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 3, mean, [t.Lemmas[0], t.Lemmas[0]], noForms));
        Assert.Throws<ArgumentException>(() => new WordSenseTable("m", Sha, 4, [0f, 0f, 0f, 0f], t.Lemmas, noForms));
    }

    /// <summary>
    ///     The lemma model constructor rejects null arguments, non-finite values and a bias of the wrong length.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaModel_NullAndNonFiniteArguments_Throw()
    {
        SenseDefinition[] senses = [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)];

        Assert.Throws<ArgumentNullException>(() => new LemmaModel(null!, senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", null!, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, null!, [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], null!, 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", [Def(PartOfSpeech.Noun), null!], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], [float.NaN], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], [0f], float.NaN, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], [0f], float.PositiveInfinity, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], [0f, 0f], 0f, 1));
    }

    /// <summary>
    ///     An undefined model kind is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaModel_UndefinedKind_Throws()
    {
        SenseDefinition[] senses = [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb)];

        Assert.Throws<ArgumentOutOfRangeException>(() => new LemmaModel("a", senses, (LemmaModelKind)0, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LemmaModel("a", senses, (LemmaModelKind)9, [1f], [0f], 0f, 1));
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
        var model = new LemmaModel(
            "odd", [Def(PartOfSpeech.Noun), Def(PartOfSpeech.Verb), Def(PartOfSpeech.Adjective)], LemmaModelKind.OneVersusRest, weights, bias, 0f, dimension);
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
