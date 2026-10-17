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
            [("bass", [new CueSense(PartOfSpeech.Noun, "fish", 0), new CueSense(PartOfSpeech.Noun, "music", 0)], [hashes[1], hashes[0]], [0, 0, 0, 0])]);

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
