namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseTable"/> and <see cref="LemmaModel"/>.
/// </summary>
public class WordSenseTableTests
{
    private const string Sha = "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1";

    /// <summary>
    ///     Builds a small table with a binary and a one-versus-rest lemma.
    /// </summary>
    /// <returns>The table.</returns>
    internal static WordSenseTable CreateSmallTable()
    {
        var binary = new LemmaModel("bat", ["animal", "tool"], LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3);
        var multi = new LemmaModel(
            "lead", ["metal", "verb", "wire"], LemmaModelKind.OneVersusRest,
            [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0.1f, -0.1f], 0.5f, 3);
        var forms = new Dictionary<string, string>
        {
            ["bat"] = "bat", ["bats"] = "bat", ["lead"] = "lead", ["led"] = "lead",
        };
        return new WordSenseTable("all-MiniLM-L6-v2", Sha, 3, [0.1f, 0.2f, 0.3f], [binary, multi], forms);
    }

    /// <summary>
    ///     Writing and reading a table reproduces every value.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesAllValues()
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
        Assert.Equal(["metal", "verb", "wire"], copy.Lemmas[1].Senses);
    }

    /// <summary>
    ///     Output is byte-for-byte deterministic.
    /// </summary>
    [Fact]
    public void ToArray_CalledTwice_ReturnsIdenticalBytes()
    {
        Assert.Equal(CreateSmallTable().ToArray(), CreateSmallTable().ToArray());
    }

    /// <summary>
    ///     Writing to a stream matches <see cref="WordSenseTable.ToArray"/>.
    /// </summary>
    [Fact]
    public void Write_Stream_MatchesToArray()
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
    public void Read_TruncatedAtEveryLength_Throws()
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
    public void Read_AnyByteFlipped_Throws()
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
    public void Read_Garbage_Throws()
    {
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(new byte[64]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseTable.Read(Enumerable.Repeat((byte)0xFF, 4096).ToArray()));
    }

    /// <summary>
    ///     Lemmas must be sorted and forms must refer to a known lemma.
    /// </summary>
    [Fact]
    public void Constructor_InconsistentInput_Throws()
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
    public void Score_Binary_UsesActivationSign()
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
    public void Score_OneVersusRest_PicksBestRow()
    {
        var model = CreateSmallTable().Lemmas[1];

        var (best, margin) = model.Score([0f, 1f, 0f]);

        Assert.Equal(1, best);
        Assert.Equal(1.1f - 0f, margin, 4);
    }

    /// <summary>
    ///     Invalid models are rejected at construction.
    /// </summary>
    [Fact]
    public void LemmaModel_BadShapes_Throw()
    {
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", ["x"], LemmaModelKind.Binary, [1f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", ["x", "y"], LemmaModelKind.Binary, [1f, 2f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new LemmaModel("a", ["x", "y"], LemmaModelKind.Binary, [float.NaN], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => CreateSmallTable().Lemmas[0].Score([1f]));
    }

    /// <summary>
    ///     The embedded table loads and lists the seed lemmas.
    /// </summary>
    [Fact]
    public void LoadEmbedded_ContainsSeedLemmas()
    {
        var table = WordSenseTable.LoadEmbedded();

        Assert.True(table.Lemmas.Count >= 34);
        Assert.True(table.TryGetLemma("record", out var record));
        Assert.Equal(["noun", "verb"], record.Senses);
        Assert.Equal("record", table.Forms["records"]);
        Assert.Equal(384, table.Dimension);
    }
}
