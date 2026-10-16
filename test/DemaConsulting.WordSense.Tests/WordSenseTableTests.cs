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
    /// <param name="name">The sense name.</param>
    /// <param name="tags">The sorted tags.</param>
    /// <returns>The definition.</returns>
    internal static SenseDefinition Def(string name, params string[] tags) => new(name, tags);

    /// <summary>
    ///     Builds a small table with a binary and a one-versus-rest lemma.
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateSmallTable()
    {
        var binary = new LemmaModel("bat", [Def("animal", "noun"), Def("tool", "noun", "verb")], LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3);
        var multi = new LemmaModel(
            "lead", [Def("metal", "noun"), Def("verb", "verb"), Def("wire", "noun")], LemmaModelKind.OneVersusRest,
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
        Assert.Equal(["metal", "verb", "wire"], copy.Lemmas[1].Senses.Select(s => s.Sense));
        Assert.Equal(["noun", "verb"], copy.Lemmas[0].Senses[1].Tags.Order());
        Assert.Equal(["noun"], copy.Lemmas[1].Senses[2].Tags);
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
    ///     Tags are interned: senses with the same tag share the tag string.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_SensesWithSameTag_ShareTagInstance()
    {
        var copy = WordSenseTable.Read(CreateSmallTable().ToArray());

        var a = copy.Lemmas[1].Senses[0].Tags.Single();
        var b = copy.Lemmas[1].Senses[2].Tags.Single();
        Assert.Same(a, b);
    }

    /// <summary>
    ///     A table older than version 3 (versions 1 and 2 have no sense groups) is rejected.
    /// </summary>
    /// <param name="version">The old version number written into the header.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void WordSenseTable_Read_OldVersion_Throws(int version)
    {
        var bytes = CreateSmallTable().ToArray();
        var body = bytes[..^32];
        Assert.Equal(3, body[4]);
        body[4] = (byte)version;
        var old = body.Concat(System.Security.Cryptography.SHA256.HashData(body)).ToArray();

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(old));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A sense group round-trips through the binary table and defaults to the sense label.
    /// </summary>
    [Fact]
    public void WordSenseTable_RoundTrip_PreservesGroups()
    {
        var model = new LemmaModel(
            "bow", [new SenseDefinition("bend-noun", ["noun"], "bend"), new SenseDefinition("bend-verb", ["verb"], "bend"), Def("weapon", "noun")],
            LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0f, 0f], 0.5f, 3);
        var table = new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [model], new Dictionary<string, string> { ["bow"] = "bow" });

        var copy = WordSenseTable.Read(table.ToArray());

        Assert.Equal(["bend", "bend", "weapon"], copy.Lemmas[0].Senses.Select(s => s.Group));
        Assert.Equal(["bend-noun", "bend-verb", "weapon"], copy.Lemmas[0].Senses.Select(s => s.Sense));
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
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def("x", "noun")], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def("x", "noun"), Def("y", "verb")], LemmaModelKind.Binary, [1f, 2f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", [Def("x", "noun"), Def("y", "verb")], LemmaModelKind.Binary, [float.NaN], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => CreateSmallTable().Lemmas[0].Score([1f]));
    }

    /// <summary>
    ///     Invalid sense definitions are rejected at construction.
    /// </summary>
    [Fact]
    public void WordSenseTable_LemmaModel_BadSenseDefinitions_Throw()
    {
        LemmaModel Make(params SenseDefinition[] senses) =>
            new("a", senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1);

        Assert.Throws<ArgumentException>(() => Make(Def("x", "noun"), Def("x", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x"), Def("y", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x", "verb", "noun"), Def("y", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x", "noun", "noun"), Def("y", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x", "Noun"), Def("y", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x", new string('a', 33)), Def("y", "verb")));
        Assert.Throws<ArgumentException>(() => Make(Def("x", Enumerable.Range(0, 17).Select(i => $"t{i:00}").ToArray()), Def("y", "verb")));
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
        Assert.Equal(["noun", "verb"], record.Senses.Select(s => s.Sense));
        Assert.Equal(["noun"], record.Senses[0].Tags);
        Assert.All(table.Lemmas.SelectMany(l => l.Senses), s => Assert.NotEmpty(s.Tags));
        Assert.Equal("record", table.Forms["records"]);
        Assert.Equal(384, table.Dimension);
        Assert.All(table.Forms.Values, lemma => Assert.True(table.TryGetLemma(lemma, out _), lemma));
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
    ///     A correctly signed table that declares more tags than allowed is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedTagCountAboveCap_Throws()
    {
        // The tag count follows the dimension and the mean
        const int tagCountOffset = 8 + 2 + 16 + 32 + 4 + (3 * 4);
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[tagCountOffset] = 0x01; b[tagCountOffset + 1] = 0x01; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("tag dictionary", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    ///     A correctly signed table whose tag dictionary is unsorted or duplicated is rejected.
    /// </summary>
    /// <param name="duplicate">True to duplicate the first tag, false to swap the two tags.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordSenseTable_Read_ResignedUnsortedOrDuplicateTags_Throws(bool duplicate)
    {
        // Two tags follow the count: "noun" then "verb", each a u16 length and four bytes
        const int firstTag = 8 + 2 + 16 + 32 + 4 + (3 * 4) + 2 + 2;
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            var noun = b[firstTag..(firstTag + 4)];
            var verb = b[(firstTag + 6)..(firstTag + 10)];
            Assert.Equal("noun", System.Text.Encoding.UTF8.GetString(noun));
            Assert.Equal("verb", System.Text.Encoding.UTF8.GetString(verb));
            (duplicate ? noun : verb).CopyTo(b, firstTag);
            noun.CopyTo(b, firstTag + 6);
            return b;
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("tag dictionary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The offset of the lemma count in the single-lemma body: header, model id, digest, dimension, mean and
    ///     the two-tag dictionary precede it.
    /// </summary>
    private const int LemmaCountOffset = 8 + 2 + 16 + 32 + 4 + (3 * 4) + 2 + 12;

    /// <summary>
    ///     The offset of the sense count of the only lemma (after the lemma count and the string "bat").
    /// </summary>
    private const int SenseCountOffset = LemmaCountOffset + 4 + 2 + 3;

    /// <summary>
    ///     The offset of the tag count of the first sense (after the sense count and the name "animal").
    /// </summary>
    private const int FirstSenseTagCountOffset = SenseCountOffset + 2 + 2 + 6;

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
    ///     A correctly signed table whose sense declares no tags or more than 16 tags is rejected.
    /// </summary>
    /// <param name="tagCount">The declared tag count of the first sense.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void WordSenseTable_Read_ResignedSenseTagCountOutOfRange_Throws(int tagCount)
    {
        var bytes = Resign(CreateSingleLemmaBytes(), b => { b[FirstSenseTagCountOffset] = (byte)tagCount; return b; });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("tag count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A correctly signed table whose sense has a malformed group string is rejected.
    /// </summary>
    [Fact]
    public void WordSenseTable_Read_ResignedInvalidGroup_Throws()
    {
        // The first sense has one tag index (u16) and then an empty group string (u16 length 0); replace the
        // empty group with the one-character group "X", which is not a valid group
        const int groupOffset = FirstSenseTagCountOffset + 1 + 2;
        var bytes = Resign(CreateSingleLemmaBytes(), b =>
        {
            Assert.Equal(0, b[groupOffset]);
            Assert.Equal(0, b[groupOffset + 1]);
            return [.. b[..groupOffset], 1, 0, (byte)'X', .. b[(groupOffset + 2)..]];
        });

        var ex = Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(bytes));
        Assert.Contains("group", ex.Message, StringComparison.OrdinalIgnoreCase);
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
        SenseDefinition[] senses = [Def("x", "noun"), Def("y", "verb")];

        Assert.Throws<ArgumentNullException>(() => new LemmaModel(null!, senses, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", null!, LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, null!, [0f], 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", senses, LemmaModelKind.Binary, [1f], null!, 0f, 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaModel("a", [Def("x", "noun"), null!], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
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
        SenseDefinition[] senses = [Def("x", "noun"), Def("y", "verb")];

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
            "odd", [Def("a", "noun"), Def("b", "verb"), Def("c", "adj")], LemmaModelKind.OneVersusRest, weights, bias, 0f, dimension);
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
