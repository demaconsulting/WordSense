using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="BasicTables"/>, <see cref="LemmaCue"/> and the feature hash of <see cref="FeatureRef"/>. The
///     tables are loaded against a toy analysis inventory, so these tests need no word image except the two that name
///     the embedded table.
/// </summary>
public class BasicTablesTests
{
    /// <summary>
    ///     A table reads back its header values, words, suffixes and cue tables.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ToyTable_ReadsContent()
    {
        var tables = TestTables.Load(TestTables.Default());

        Assert.Equal(0.75, tables.Gamma);
        Assert.Equal(5.0, tables.Beta);
        Assert.Equal(1.5, tables.HmmWeight);
        Assert.Equal(0.5, tables.InvTemperature);
        Assert.Equal(3, tables.Words.Count);
        Assert.Equal(1 << PosTag.Determiner, tables.Words["the"].ClosedMask);
        Assert.Equal(12, tables.Words["record"].Total);
        Assert.Equal(1, tables.SuffixCount);
        var cue = tables.Cues["read"];
        Assert.Equal(TestTables.ReadColumns(), cue.Analyses);
        Assert.Equal(2, cue.Hashes.Length);
    }

    /// <summary>
    ///     The interpolated transitions of an observed context sum to one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Transitions_AreDistributions()
    {
        var tables = TestTables.Load(TestTables.Default());

        var sum = 0.0;
        for (var c = 0; c < PosTag.Extended; c++)
        {
            sum += tables.Transition[(((PosTag.Determiner * PosTag.Extended) + PosTag.Noun) * PosTag.Extended) + c];
        }

        Assert.Equal(1.0, sum, 9);
    }

    /// <summary>
    ///     The longest known suffix gives a distribution over the open classes; an unknown ending gives none.
    /// </summary>
    [Fact]
    public void BasicTables_TryFindSuffix_KnownAndUnknown()
    {
        var tables = TestTables.Load(TestTables.Default());

        var found = tables.TryFindSuffix("walked", out var known);

        Assert.True(found);
        Assert.False(known.IsDefault);
        Assert.Equal(1.0, known.Sum(), 9);
        Assert.True(known[1] > known[0]);
        Assert.False(tables.TryFindSuffix("xyz", out var unknown));
        Assert.True(unknown.IsDefault);
    }

    /// <summary>
    ///     A blob with a wrong magic number is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_BadMagic_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());
        raw[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));
    }

    /// <summary>
    ///     A blob that is not compressed data is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => TestTables.Load("not a table at all"u8));
    }

    /// <summary>
    ///     A string with malformed UTF-8 is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void BasicTables_Load_MalformedUtf8String_ThrowsInvalidData()
    {
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], []));
        var at = raw.AsSpan().IndexOf("record"u8);
        Assert.True(at >= 0);
        raw[at] = 0xFF; // never valid in UTF-8

        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        Assert.IsType<System.Text.DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A truncated blob is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Truncated_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw[..(raw.Length - 3)])));
    }

    /// <summary>
    ///     A blob cut at any byte, so at every section boundary (header, tag counts, words, suffixes, cue tables) and
    ///     inside every section, is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TestTables.Decompress(TestTables.Default());

        // Act / Assert: every proper prefix is rejected
        for (var length = 0; length < raw.Length; length++)
        {
            var blob = TestTables.Compress(raw[..length]);
            Assert.True(
                Throws(() => TestTables.Load(blob)),
                $"A table cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     The retired class-keyed format and a tag count that differs from the tag set are rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_WrongVersionOrTagCount_Throws()
    {
        // Arrange: the tag count is a single-byte varint after the magic, the hash and eight doubles
        var version = TestTables.Decompress(TestTables.Default());
        version[3] = (byte)'2';
        var tags = TestTables.Decompress(TestTables.Default());
        const int tagCountOffset = 4 + 32 + (8 * 8);
        Assert.Equal(PosTag.Count, tags[tagCountOffset]);
        tags[tagCountOffset]++;

        // Act / Assert
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(version))), "Version WSB2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(tags))), "Tag count.");
    }

    /// <summary>
    ///     The previous format <c>WSB3</c>, which has no tense weight and no tense table, is rejected as an unknown format
    ///     whatever else it holds, so a table that was trained before the tense model is never read as a current one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PreviousFormatWsb3_ThrowsUnknownFormat()
    {
        // Arrange: a current table whose magic is changed back to the previous revision
        var raw = TestTables.Decompress(TestTables.Default());
        Assert.Equal((byte)'4', raw[3]);
        raw[3] = (byte)'3';

        // Act
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        // Assert
        Assert.Contains("unknown format", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tense section reads back: the weight of the model from the header, and the prior, the ascending hashes and the
    ///     weights of the table.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TenseTable_ReadsWeightPriorHashesAndWeights()
    {
        // Arrange
        ushort[] hashes = [3, 70, 40000];
        sbyte[] weights = [-12, 7, 127];
        var with = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (-2, hashes, weights), tenseWeight: 0.75);

        // Act
        var tables = TestTables.Load(with);

        // Assert
        Assert.Equal(0.75, tables.TenseWeight);
        var tense = Assert.IsType<TenseCue>(tables.Tense);
        Assert.Equal((sbyte)-2, tense.Prior);
        Assert.Equal(hashes, tense.Hashes.ToArray());
        Assert.Equal(weights, tense.Weights.ToArray());
    }

    /// <summary>
    ///     A table without the tense section has no tense table and the default weight of the model.
    /// </summary>
    [Fact]
    public void BasicTables_Load_WithoutTenseSection_HasNoTenseTableAndTheDefaultWeight()
    {
        // Arrange
        var without = TestTables.Build([("record", 0, new int[PosTag.Count])], [], []);

        // Act
        var plain = TestTables.Load(without);

        // Assert
        Assert.Null(plain.Tense);
        Assert.Equal(1.0, plain.TenseWeight);
    }

    /// <summary>
    ///     A tense table cut at any byte is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TenseTableTruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70, 400], [1, -2, 3])));

        // Act / Assert: every proper prefix is rejected
        for (var length = 0; length < raw.Length; length++)
        {
            var blob = TestTables.Compress(raw[..length]);
            Assert.True(Throws(() => TestTables.Load(blob)), $"A table with a tense section cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     A tense section with an unknown flag, without features or with unordered hashes is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_InvalidTenseSection_Throws()
    {
        // Arrange: the section is the last bytes of the table: flag, prior, count, deltas and weights
        var good = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70], [1, -2])));
        var flag = (byte[])good.Clone();
        flag[^7] = 2;
        var noFeatures = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [], [])));

        // Act / Assert
        Assert.Equal(1, good[^7]);
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(flag))), "Flag 2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(noFeatures))), "No features.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [70, 3], [1, -2])))), "Unordered hashes.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70], [1])))), "Fewer weights than hashes.");
    }

    /// <summary>
    ///     A tense table refuses the default (uninitialized) immutable arrays and arrays of different lengths.
    /// </summary>
    [Fact]
    public void TenseCue_Constructor_DefaultOrUnequalArrays_Throws()
    {
        // Arrange: a default array, and arrays of different lengths
        ImmutableArray<ushort> noHashes = default;
        ImmutableArray<sbyte> noWeights = default;

        // Act
        var defaultHashes = Record.Exception(() => new TenseCue(0, noHashes, [1]));
        var defaultWeights = Record.Exception(() => new TenseCue(0, [1], noWeights));
        var unequal = Record.Exception(() => new TenseCue(0, [1, 2], [1]));

        // Assert
        Assert.IsType<ArgumentException>(defaultHashes);
        Assert.IsType<ArgumentException>(defaultWeights);
        Assert.IsType<ArgumentException>(unequal);
    }

    /// <summary>
    ///     Runs an action and reports whether it threw <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <returns><c>true</c> when the action threw <see cref="InvalidDataException"/>; otherwise <c>false</c>.</returns>
    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>
    ///     A blob with extra bytes at the end is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TrailingBytes_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress([.. raw, 0])));
    }

    /// <summary>
    ///     A blob with bytes after the end of its Brotli stream is rejected, naming the trailing bytes.
    /// </summary>
    [Fact]
    public void BasicTables_Load_BytesAfterCompressedStream_Throws()
    {
        // Arrange: a complete stream followed by one more byte
        byte[] blob = [.. TestTables.Default(), 0];

        // Act
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("trailing bytes", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The real embedded table with bytes after its compressed stream, with one inventory-hash byte changed and cut
    ///     short is rejected at the public boundary as <see cref="ModelIntegrityException"/> that carries the original
    ///     <see cref="InvalidDataException"/>, and the unaltered table still loads.
    /// </summary>
    /// <param name="alteration">Which region of the real table to alter.</param>
    [Theory]
    [InlineData("trailing")]
    [InlineData("hash")]
    [InlineData("truncated")]
    public void BasicTables_LoadEmbedded_RealTableWithAlteredRegion_ThrowsModelIntegrity(string alteration)
    {
        // Arrange: the real blob; the hash is the 32 bytes after the four-byte magic of the decompressed table
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(BasicTables.ResourceName)!;
        var real = new byte[stream.Length];
        stream.ReadExactly(real);
        Assert.NotEmpty(BasicTables.LoadEmbedded(new MemoryStream(real)).Words);
        byte[] altered;
        if (alteration == "trailing")
        {
            altered = [.. real, 0x00];
        }
        else if (alteration == "hash")
        {
            var raw = TestTables.Decompress(real);
            raw[4] ^= 0xFF;
            altered = TestTables.Compress(raw);
        }
        else
        {
            altered = real[..(real.Length - 8)];
        }

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(new MemoryStream(altered)));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    ///     The table, its word entries and its cue tables expose no array: every statistic is a read-only view, so a
    ///     consumer cannot change what another consumer sees.
    /// </summary>
    [Fact]
    public void BasicTables_Properties_ExposeNoArrays()
    {
        // Arrange
        Type[] types = [typeof(BasicTables), typeof(WordEntry), typeof(LemmaCue), typeof(TenseCue)];

        // Act
        var arrays = types
            .SelectMany(t => t.GetProperties().Select(p => (Name: $"{t.Name}.{p.Name}", p.PropertyType)))
            .Where(p => p.PropertyType.IsArray)
            .Select(p => p.Name)
            .ToArray();

        // Assert
        Assert.Empty(arrays);
    }

    /// <summary>
    ///     The statistics of a loaded table do not change when the blob it was read from is changed afterwards, and the
    ///     views of the table agree with the values that were written.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Views_AreIndependentOfTheBlob()
    {
        // Arrange
        var blob = TestTables.Default();
        var tables = TestTables.Load(blob);
        var before = tables.Cues["read"].Weights.ToArray();
        var transition = tables.Transition.ToArray();

        // Act: damage the blob after the load
        Array.Fill(blob, (byte)0);

        // Assert
        Assert.Equal(before, tables.Cues["read"].Weights.ToArray());
        Assert.Equal(transition, tables.Transition.ToArray());
        Assert.Equal(PosTag.Extended, tables.Unigrams.Length);
        Assert.Equal(PosTag.Extended * PosTag.Extended, tables.Bigrams.Length);
        Assert.Equal(PosTag.Extended * PosTag.Extended * PosTag.Extended, tables.Trigrams.Length);
        Assert.Equal(PosTag.Count, tables.TagPrior.Length);
        Assert.Equal(PosTag.Count, tables.Words["record"].Counts.Length);
    }

    /// <summary>
    ///     A word entry and a cue table refuse the default (uninitialized) immutable array.
    /// </summary>
    [Fact]
    public void BasicTables_Constructors_DefaultArray_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => new WordEntry(0, default));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", default, [1], [0]));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", [], default, [0]));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", [], [1], default));
    }

    /// <summary>
    ///     Feature hashes that are not ascending are rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnorderedHashes_Throws()
    {
        var hashes = TestTables.CueHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("read", TestTables.ReadColumns(), [hashes[1], hashes[0]], [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     A cue whose declared feature hashes need more weight bytes than the table holds is rejected as corrupt
    ///     data before any weight block is allocated.
    /// </summary>
    [Fact]
    public void BasicTables_Load_CueWeightsPastEnd_ThrowsInvalidData()
    {
        // Arrange: the last cue declares 300 ascending feature hashes for two analyses but carries only four weights
        var hashes = Enumerable.Range(1, 300).Select(h => (ushort)h).ToArray();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("read", TestTables.ReadColumns(), hashes, [0, 0, 0, 0])]);

        // Act and Assert: the size check reports the truncation as invalid data
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     Word counts whose sum exceeds the 32-bit range are rejected as corrupt data, not as an overflow.
    /// </summary>
    [Fact]
    public void BasicTables_Load_CountSumOverflow_ThrowsInvalidData()
    {
        // Arrange: a word whose two counts together exceed int.MaxValue
        var counts = new int[PosTag.Count];
        counts[0] = int.MaxValue;
        counts[1] = int.MaxValue;
        var blob = TestTables.Build([("record", 0, counts)], [], []);

        // Act and Assert
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     The table embedded in the assembly loads and has statistics. It needs the embedded word image and the
    ///     regenerated table.
    /// </summary>
    [Fact]
    public void BasicTables_Default_LoadsEmbeddedTable()
    {
        var tables = BasicTables.Default;

        Assert.NotEmpty(tables.Words);
        Assert.NotEmpty(tables.Cues);
        Assert.Same(tables, BasicTables.Default);
    }

    /// <summary>
    ///     The embedded table loads from its own resource stream.
    /// </summary>
    [Fact]
    public void BasicTables_LoadEmbedded_EmbeddedResource_Loads()
    {
        // Arrange
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(BasicTables.ResourceName);

        // Act
        var tables = BasicTables.LoadEmbedded(stream);

        // Assert
        Assert.NotEmpty(tables.Words);
    }

    /// <summary>
    ///     A missing embedded table is reported as <see cref="ModelIntegrityException"/> that carries the original
    ///     <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void BasicTables_LoadEmbedded_MissingResource_ThrowsModelIntegrity()
    {
        // Arrange / Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(null));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corrupt or foreign embedded table (bytes that are not a table, and a table of another inventory) is reported
    ///     as <see cref="ModelIntegrityException"/> that carries the original <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="toyTable">Whether to use a well-formed table for another inventory, instead of bytes that are not a table.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BasicTables_LoadEmbedded_CorruptOrForeignResource_ThrowsModelIntegrity(bool toyTable)
    {
        // Arrange
        using var corrupt = new MemoryStream(toyTable ? TestTables.Default() : "not a table at all"u8.ToArray());

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(corrupt));

        // Assert: the original exception is kept
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    ///     A table trained for another analysis inventory is rejected, and the message names both hashes.
    /// </summary>
    [Fact]
    public void BasicTables_Load_OtherInventoryHash_ThrowsNamingBothHashes()
    {
        // Arrange: a table that carries the all-zero hash, loaded against the toy inventory
        var other = new byte[32];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], inventoryHash: other);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the hash of the table and the hash of the inventory are in the message
        Assert.Contains(Convert.ToHexString(other), ex.Message, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(TestTables.ToyHash), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A cue column that names an analysis the inventory does not have is rejected, and the message names it.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnknownAnalysis_Throws()
    {
        // Arrange: the second column is a noun "read", which the toy inventory lacks
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns =
        [
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
            new("read", PartOfSpeech.Noun, "Number=Sing", 0),
        ];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("'read'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Noun", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A column with a part-of-speech byte that is not a part of speech is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_InvalidPartOfSpeech_Throws()
    {
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns = [new("read", PartOfSpeech.Verb, "Tense=Past", 0), new("read", (PartOfSpeech)99, "Tense=Pres", 0)];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     An analysis that two columns of one cue table name is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_DuplicateAnalysis_Throws()
    {
        // Arrange: the same lemma, part of speech and features twice
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns = [new("read", PartOfSpeech.Verb, "Tense=Past", 0), new("read", PartOfSpeech.Verb, "Tense=Past", 0)];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("twice", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An analysis is scored by the column of its own features when there is one, and otherwise by the column with
    ///     empty features, which stands for every analysis of its lemma and part of speech; an analysis of another
    ///     lemma or part of speech has no column.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_Analysis_UsesTheColumnOfItsFeatures()
    {
        // Arrange: the empty-features column, the past tense and the past participle of read
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns =
        [
            new("read", PartOfSpeech.Verb, string.Empty, 0),
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
            new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part", 0),
        ];
        var cue = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, new sbyte[6])])).Cues["read"];

        // Act / Assert: the column of the exact features, the fallback column and no column
        Assert.Equal(1, cue.RowOf(TestTables.ReadPast));
        Assert.Equal(2, cue.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(0, cue.RowOf(TestTables.ReadPres));
        Assert.Equal(0, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, string.Empty)));
        Assert.Equal(-1, cue.RowOf(TestTables.RecordVerb));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Noun, string.Empty)));
    }

    /// <summary>
    ///     Without an empty-features column, an analysis whose features contain none of the column items has no column.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_NoEmptyFeaturesColumn_UnmatchedAnalysisHasNoColumn()
    {
        var cue = TestTables.Load(TestTables.Default()).Cues["read"];

        Assert.Equal(0, cue.RowOf(TestTables.ReadPast));
        Assert.Equal(1, cue.RowOf(TestTables.ReadPres));
        Assert.Equal(0, cue.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, "VerbForm=Inf")));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, string.Empty)));
    }

    /// <summary>
    ///     Of the columns whose feature items are all contained in the analysis, the one with the most items wins and the
    ///     first of equally specific columns is taken.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_SeveralContainedColumns_TakesTheMostSpecificThenTheFirst()
    {
        // Arrange: two one-item columns that the participle contains, then a two-item column
        var hashes = TestTables.CueHashes();
        CueAnalysis[] ties =
        [
            new("read", PartOfSpeech.Verb, "VerbForm=Part", 0),
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
        ];
        CueAnalysis[] specific = [.. ties, new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part", 0)];
        var tied = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", ties, hashes, new sbyte[4])])).Cues["read"];
        var most = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", specific, hashes, new sbyte[6])])).Cues["read"];

        // Act / Assert
        Assert.Equal(0, tied.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(2, most.RowOf(TestTables.ReadPastParticiple));
    }

    /// <summary>
    ///     The cue table of a word is the table named by the normalized word first, then the table of the lemma of its
    ///     first analysis, and none when neither exists.
    /// </summary>
    [Fact]
    public void BasicTables_FindCue_WordTableBeforeLemmaTable()
    {
        // Arrange: a table of the form "left" that spans two lemmas, and the table of the lemma "read"
        var hashes = TestTables.CueHashes();
        CueAnalysis[] leftColumns =
        [
            new("leave", PartOfSpeech.Verb, "Tense=Past", 0),
            new("left", PartOfSpeech.Adjective, string.Empty, 0),
        ];
        var tables = TestTables.Load(TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("left", leftColumns, hashes, new sbyte[4]), ("read", TestTables.ReadColumns(), hashes, new sbyte[4])]));
        ImmutableArray<WordDetails> leftAnalyses = [TestTables.LeftAdjective, TestTables.LeavePast];
        ImmutableArray<WordDetails> readAnalyses = [TestTables.ReadPres, TestTables.ReadPast];
        ImmutableArray<WordDetails> readFormAnalyses = [TestTables.ReadPast, TestTables.ReadPres];

        // Act
        var byWord = tables.FindCue("left", leftAnalyses);
        var byLemma = tables.FindCue("reads", readAnalyses);
        var byWordFirst = tables.FindCue("read", readFormAnalyses);
        var none = tables.FindCue("record", [TestTables.RecordNoun, TestTables.RecordVerb]);

        // Assert
        Assert.Same(tables.Cues["left"], byWord);
        Assert.Same(tables.Cues["read"], byLemma);
        Assert.Same(tables.Cues["read"], byWordFirst);
        Assert.Null(none);
        Assert.Equal(1, byWord!.RowOf(TestTables.LeftAdjective));
        Assert.Equal(0, byWord.RowOf(TestTables.LeavePast));
    }

    /// <summary>
    ///     The shipped table loads against the shipped word image (the load checks the inventory hash) and every analysis
    ///     it names exists there. It needs the regenerated word image and table.
    /// </summary>
    [Fact]
    public void BasicTables_Default_ShippedTable_ResolvesEveryCueClass()
    {
        var tables = BasicTables.Default;

        Assert.All(
            tables.Cues.Values,
            cue => Assert.All(cue.Analyses, a => Assert.True(WordLexicon.Current.AnalysisExists(a.Lemma, a.PartOfSpeech, a.Features), $"{a.Lemma} {a.PartOfSpeech} '{a.Features}'")));
    }

    /// <summary>
    ///     The hash of an immediate-neighbor feature equals the hash of the window token that spells its prefix, and the hash is stable.
    /// </summary>
    [Fact]
    public void FeatureRef_Hash_PrefixAndDeterminism()
    {
        var left = new FeatureRef(CueModel.Left, "dog").Hash;
        var right = new FeatureRef(CueModel.Right, "dog").Hash;

        Assert.Equal(new FeatureRef(CueModel.Window, "L1:dog").Hash, left);
        Assert.Equal(new FeatureRef(CueModel.Window, "R1:dog").Hash, right);
        Assert.Equal(left, new FeatureRef(CueModel.Left, "dog").Hash);
    }

    /// <summary>
    ///     The feature kinds of the tense model hash with their own prefixes, so the same token in another place has another
    ///     feature, and the prefixes of the cue kinds are unchanged (the cue tables stay valid).
    /// </summary>
    [Fact]
    public void FeatureRef_Hash_TenseKindsHaveOwnPrefixes()
    {
        // Arrange: the same token as a plain window feature and as each kind of the tense model
        var window = new FeatureRef(CueModel.Window, "have").Hash;
        var leftWindow = new FeatureRef(CueModel.LeftWindow, "have").Hash;
        var rightWindow = new FeatureRef(CueModel.RightWindow, "have").Hash;
        var governor = new FeatureRef(CueModel.Governor, "have").Hash;

        // Act
        var prefixed = new[]
        {
            new FeatureRef(CueModel.Window, "LW:have").Hash,
            new FeatureRef(CueModel.Window, "RW:have").Hash,
            new FeatureRef(CueModel.Window, "NA:have").Hash,
            new FeatureRef(CueModel.Window, "L1:have").Hash,
        };

        // Assert: each kind hashes as its prefix and the token, the cue kinds keep theirs, and the kinds differ
        Assert.Equal(prefixed[0], leftWindow);
        Assert.Equal(prefixed[1], rightWindow);
        Assert.Equal(prefixed[2], governor);
        Assert.Equal(prefixed[3], new FeatureRef(CueModel.Left, "have").Hash);
        Assert.Equal(4, new[] { window, leftWindow, rightWindow, governor }.Distinct().Count());
    }
}
