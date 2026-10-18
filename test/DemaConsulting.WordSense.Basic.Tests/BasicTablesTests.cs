namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicTables"/> and the feature hash of <see cref="FeatureRef"/>.
/// </summary>
public class BasicTablesTests
{
    /// <summary>
    ///     A table reads back its header values, words, suffixes and cue tables.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ToyTable_ReadsContent()
    {
        var tables = BasicTables.Load(TestTables.Default());

        Assert.Equal(0.75, tables.Gamma);
        Assert.Equal(5.0, tables.Beta);
        Assert.Equal(1.5, tables.HmmWeight);
        Assert.Equal(0.5, tables.InvTemperature);
        Assert.Equal(3, tables.Words.Count);
        Assert.Equal(1 << PosTag.Determiner, tables.Words["the"].ClosedMask);
        Assert.Equal(12, tables.Words["record"].Total);
        Assert.Equal(1, tables.SuffixCount);
        Assert.Equal(2, tables.Cues["bass"].Senses.Length);
        Assert.Equal(2, tables.Cues["bass"].Hashes.Length);
    }

    /// <summary>
    ///     The interpolated transitions of an observed context sum to one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Transitions_AreDistributions()
    {
        var tables = BasicTables.Load(TestTables.Default());

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
    public void BasicTables_FindSuffix_KnownAndUnknown()
    {
        var tables = BasicTables.Load(TestTables.Default());

        var known = tables.FindSuffix("walked");

        Assert.NotNull(known);
        Assert.Equal(1.0, known.Sum(), 9);
        Assert.True(known[1] > known[0]);
        Assert.Null(tables.FindSuffix("xyz"));
    }

    /// <summary>
    ///     A blob with a wrong magic number is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_BadMagic_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());
        raw[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => BasicTables.Load(TestTables.Compress(raw)));
    }

    /// <summary>
    ///     A blob that is not compressed data is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => BasicTables.Load("not a table at all"u8));
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

        var ex = Assert.Throws<InvalidDataException>(() => BasicTables.Load(TestTables.Compress(raw)));

        Assert.IsType<System.Text.DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A truncated blob is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Truncated_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => BasicTables.Load(TestTables.Compress(raw[..(raw.Length - 3)])));
    }

    /// <summary>
    ///     A blob with extra bytes at the end is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TrailingBytes_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => BasicTables.Load(TestTables.Compress([.. raw, 0])));
    }

    /// <summary>
    ///     Feature hashes that are not ascending are rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnorderedHashes_Throws()
    {
        var hashes = TestTables.HeteronymHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("bass", [new CueSense("bass#fish", 0), new CueSense("bass#music", 0)], [hashes[1], hashes[0]], [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => BasicTables.Load(blob));
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
        Assert.Throws<InvalidDataException>(() => BasicTables.Load(blob));
    }

    /// <summary>
    ///     The table embedded in the assembly loads and has statistics.
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
    ///     A table trained for another class inventory is rejected, and the message names both hashes.
    /// </summary>
    [Fact]
    public void BasicTables_Load_OtherClassHash_ThrowsNamingBothHashes()
    {
        var other = new byte[32];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], classHash: other);

        var ex = Assert.Throws<InvalidDataException>(() => BasicTables.Load(blob));

        Assert.Contains(Convert.ToHexString(other), ex.Message, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(WordLexicon.Current.ClassHash), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A cue that names a class the word image does not have is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnknownClass_Throws()
    {
        var hashes = TestTables.HeteronymHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("bass", [new CueSense("bass#fish", 0), new CueSense("bass#no-such-class", 0)], hashes, [0, 0, 0, 0])]);

        var ex = Assert.Throws<InvalidDataException>(() => BasicTables.Load(blob));

        Assert.Contains("bass#no-such-class", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A class that two cue columns name is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_DuplicateClass_Throws()
    {
        var hashes = TestTables.HeteronymHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("bass", [new CueSense("bass#fish", 0), new CueSense("bass#fish", 0)], hashes, [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => BasicTables.Load(blob));
    }

    /// <summary>
    ///     An analysis without a class is scored by the meaning-less column of its part of speech; a class that is not
    ///     in the cue gets no column.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_AnalysisWithoutClass_UsesMeaninglessColumnOfItsPartOfSpeech()
    {
        var hashes = TestTables.HeteronymHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("record", [new CueSense("record#noun", 0), new CueSense("record#verb", 0)], hashes, [0, 0, 0, 0])]);
        var cue = BasicTables.Load(blob).Cues["record"];

        Assert.Equal(0, cue.RowOf(new WordDetails(null, "recorded", PartOfSpeech.Noun, string.Empty)));
        Assert.Equal(1, cue.RowOf(new WordDetails(null, "recorded", PartOfSpeech.Verb, string.Empty)));
        Assert.Equal(-1, cue.RowOf(new WordDetails(null, "recorded", PartOfSpeech.Adjective, string.Empty)));
        Assert.Equal(-1, cue.RowOf(new WordDetails("bass#fish", "bass", PartOfSpeech.Noun, string.Empty)));
        Assert.Equal(1, cue.RowOf(new WordDetails("record#verb", "record", PartOfSpeech.Verb, string.Empty)));
    }

    /// <summary>
    ///     The shipped table loads against the shipped word image (the load checks the class hash) and resolves every cue class.
    /// </summary>
    [Fact]
    public void BasicTables_Default_ShippedTable_ResolvesEveryCueClass()
    {
        var tables = BasicTables.Default;

        Assert.All(tables.Cues.Values, cue => Assert.All(cue.Senses, s => Assert.NotNull(s.Pos)));
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
}
