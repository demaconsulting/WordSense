using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseWeightsTable"/>.
/// </summary>
public class WordSenseWeightsTableTests
{
    private static readonly string ModelSha = new('a', 64);

    private static readonly string OtherSha = new('b', 64);

    private static Dictionary<string, SenseWeights> SmallWeights() =>
        new()
        {
            ["bat"] = new SenseWeights(LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3),
            ["lead"] = new SenseWeights(
                LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0.1f, -0.1f], 0.5f, 3),
        };

    private static WordSenseWeightsTable Create(string? lexiconSha = null) =>
        new("my-model", ModelSha, lexiconSha ?? WordSenseTableTests.CreateSmallTable().Sha256, 3, [0.1f, 0.2f, 0.3f], SmallWeights());

    private static byte[] Resign(byte[] bytes, Func<byte[], byte[]> change)
    {
        var body = change(bytes[..^32]);
        return body.Concat(SHA256.HashData(body)).ToArray();
    }

    /// <summary>
    ///     Writing and reading a weights table reproduces every value.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_RoundTrip_PreservesAllValues()
    {
        var original = Create();

        var copy = WordSenseWeightsTable.Read(original.ToArray());

        Assert.Equal("my-model", copy.ModelId);
        Assert.Equal(ModelSha, copy.ModelSha256);
        Assert.Equal(original.LexiconSha256, copy.LexiconSha256);
        Assert.Equal(3, copy.Dimension);
        Assert.Equal([0.1f, 0.2f, 0.3f], copy.Mean);
        Assert.Equal(["bat", "lead"], copy.Lemmas);
        Assert.True(copy.TryGet("bat", out var bat));
        Assert.Equal(LemmaModelKind.Binary, bat.Kind);
        Assert.Equal(0.75f, bat.AbstainMargin);
        Assert.Equal([1f, -2f, 0.5f], bat.Weights);
        Assert.Equal([0.25f], bat.Bias);
        Assert.True(copy.TryGet("lead", out var lead));
        Assert.Equal(3, lead.SenseCount);
        Assert.Equal(original.Lemmas.Select(l => { original.TryGet(l, out var w); return w.Weights; }),
            copy.Lemmas.Select(l => { copy.TryGet(l, out var w); return w.Weights; }));
        Assert.False(copy.TryGet("cat", out _));
        Assert.Equal(original.ComputeSha256(), copy.ComputeSha256());
        Assert.Equal(original.ToArray(), copy.ToArray());
        Assert.Throws<ArgumentNullException>(() => copy.TryGet(null!, out _));
    }

    /// <summary>
    ///     Upper-case digests are normalized to lower case, and the mean is copied.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Constructor_NormalizesDigestsAndCopiesMean()
    {
        var mean = new[] { 0.1f, 0.2f, 0.3f };

        var table = new WordSenseWeightsTable("m", new string('A', 64), new string('B', 64), 3, mean, SmallWeights());
        mean[0] = 9f;

        Assert.Equal(new string('a', 64), table.ModelSha256);
        Assert.Equal(new string('b', 64), table.LexiconSha256);
        Assert.Equal(0.1f, table.Mean[0]);
    }

    /// <summary>
    ///     Invalid constructor arguments are rejected.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Constructor_InvalidArguments_Throw()
    {
        var sha = WordSenseTableTests.CreateSmallTable().Sha256;
        float[] mean = [0f, 0f, 0f];

        Assert.Throws<ArgumentNullException>(() => new WordSenseWeightsTable(null!, ModelSha, sha, 3, mean, SmallWeights()));
        Assert.Throws<ArgumentNullException>(() => new WordSenseWeightsTable("m", null!, sha, 3, mean, SmallWeights()));
        Assert.Throws<ArgumentNullException>(() => new WordSenseWeightsTable("m", ModelSha, null!, 3, mean, SmallWeights()));
        Assert.Throws<ArgumentNullException>(() => new WordSenseWeightsTable("m", ModelSha, sha, 3, null!, SmallWeights()));
        Assert.Throws<ArgumentNullException>(() => new WordSenseWeightsTable("m", ModelSha, sha, 3, mean, null!));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", "abc", sha, 3, mean, SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", new string('z', 64), sha, 3, mean, SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", ModelSha, "abc", 3, mean, SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", ModelSha, sha, 4, mean, SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", ModelSha, sha, 0, [], SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable("m", ModelSha, sha, 3, [0f, float.NaN, 0f], SmallWeights()));
        Assert.Throws<ArgumentException>(() => new WordSenseWeightsTable(
            "m", ModelSha, sha, 3, mean, new Dictionary<string, SenseWeights> { ["x"] = new(LemmaModelKind.Binary, [1f], [0f], 0f, 1) }));
    }

    /// <summary>
    ///     Weights match the lexicon table they were built for.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_EnsureMatches_MatchingTable_DoesNotThrow()
    {
        var table = WordSenseTableTests.CreateSmallTable();

        Create(table.Sha256).EnsureMatches(table);
        Assert.Throws<ArgumentNullException>(() => Create().EnsureMatches(null!));
    }

    /// <summary>
    ///     Weights built for another lexicon digest are refused.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_EnsureMatches_OtherLexicon_Throws()
    {
        var table = WordSenseTableTests.CreateSmallTable();

        Assert.Throws<WordSenseModelMismatchException>(() => Create(new string('c', 64)).EnsureMatches(table));
    }

    /// <summary>
    ///     Weights that do not cover exactly the context lemmas, or that score a different number of senses, are refused.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_EnsureMatches_LemmaOrSenseCountMismatch_Throws()
    {
        var table = WordSenseTableTests.CreateSmallTable();
        var sha = table.Sha256;

        // Missing lemma
        var fewer = new Dictionary<string, SenseWeights> { ["bat"] = SmallWeights()["bat"] };
        var missing = new WordSenseWeightsTable("m", ModelSha, sha, 3, [0f, 0f, 0f], fewer);
        Assert.Throws<WordSenseModelMismatchException>(() => missing.EnsureMatches(table));

        // Extra lemma
        var more = SmallWeights();
        more["zed"] = more["bat"];
        var extra = new WordSenseWeightsTable("m", ModelSha, sha, 3, [0f, 0f, 0f], more);
        Assert.Throws<WordSenseModelMismatchException>(() => extra.EnsureMatches(table));

        // Same count, other lemma name
        var renamed = SmallWeights();
        renamed["zed"] = renamed["bat"];
        renamed.Remove("bat");
        var wrongName = new WordSenseWeightsTable("m", ModelSha, sha, 3, [0f, 0f, 0f], renamed);
        Assert.Throws<WordSenseModelMismatchException>(() => wrongName.EnsureMatches(table));

        // "lead" has three senses, so a binary classifier for it does not fit
        var wrongSenses = SmallWeights();
        wrongSenses["lead"] = wrongSenses["bat"];
        var binaryLead = new WordSenseWeightsTable("m", ModelSha, sha, 3, [0f, 0f, 0f], wrongSenses);
        Assert.Throws<WordSenseModelMismatchException>(() => binaryLead.EnsureMatches(table));
    }

    /// <summary>
    ///     The digest depends on every value.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_ComputeSha256_ChangesWithContent()
    {
        var table = Create();

        Assert.Equal(64, table.ComputeSha256().Length);
        Assert.Equal(table.ComputeSha256(), Create().ComputeSha256());
        Assert.NotEqual(
            table.ComputeSha256(),
            new WordSenseWeightsTable("my-model", OtherSha, table.LexiconSha256, 3, [0.1f, 0.2f, 0.3f], SmallWeights()).ComputeSha256());
        Assert.NotEqual(
            table.ComputeSha256(),
            new WordSenseWeightsTable("other", ModelSha, table.LexiconSha256, 3, [0.1f, 0.2f, 0.3f], SmallWeights()).ComputeSha256());
    }

    /// <summary>
    ///     Every proper prefix of a serialized weights table is rejected with a format exception.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_TruncatedAtEveryLength_Throws()
    {
        var bytes = Create().ToArray();

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(bytes.AsSpan(0, length)));
        }
    }

    /// <summary>
    ///     Flipping any single byte is detected by the checksum.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_AnyByteFlipped_Throws()
    {
        var bytes = Create().ToArray();

        for (var i = 0; i < bytes.Length; i++)
        {
            var copy = (byte[])bytes.Clone();
            copy[i] ^= 0x01;
            Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(copy));
        }
    }

    /// <summary>
    ///     Garbage input and a lexicon table are rejected with a format exception.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_Garbage_Throws()
    {
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read([]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(new byte[100]));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(WordSenseTableTests.CreateSmallTable().ToArray()));
    }

    /// <summary>
    ///     A valid checksum does not excuse a wrong signature, an unsupported version, or trailing data.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_ResignedBadHeaderOrTrailingData_Throws()
    {
        var bytes = Create().ToArray();

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[0] ^= 0xFF; return b; })));
        var version = Assert.Throws<WordSenseTableFormatException>(
            () => WordSenseWeightsTable.Read(Resign(bytes, b => { b[4] = 2; return b; })));
        Assert.Contains("version", version.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => [.. b, 0])));
    }

    /// <summary>
    ///     Lemma counts the data cannot back and sense counts out of range are rejected.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_ResignedBadCounts_Throws()
    {
        var bytes = Create().ToArray();

        // Header: magic 4, version 2, model id (2 + 8), two digests 64, dimension 4, mean 12, then the lemma count
        const int countOffset = 4 + 2 + 2 + 8 + 64 + 4 + 12;
        Assert.Equal(2, bytes[countOffset]);

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[countOffset] = 0xFF; return b; })));

        // First lemma: length 2 + "bat" 3, then the sense count
        const int senseOffset = countOffset + 4 + 2 + 3;
        Assert.Equal(2, bytes[senseOffset]);
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[senseOffset] = 1; return b; })));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[senseOffset] = 65; return b; })));

        // The model kind byte follows the sense count
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[senseOffset + 2] = 9; return b; })));

        // The dimension: zero, and above the cap
        const int dimensionOffset = 4 + 2 + 2 + 8 + 64;
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[dimensionOffset] = 0; return b; })));
        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(Resign(bytes, b => { b[dimensionOffset + 1] = 0x20; return b; })));
    }

    /// <summary>
    ///     Lemmas that are not in ordinal order are rejected.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_ResignedUnorderedLemmas_Throws()
    {
        var bytes = Create().ToArray();
        const int firstLemmaChar = 4 + 2 + 2 + 8 + 64 + 4 + 12 + 4 + 2;
        Assert.Equal((byte)'b', bytes[firstLemmaChar]);

        // "bat" becomes "zat", which sorts after "lead"
        var changed = Resign(bytes, b => { b[firstLemmaChar] = (byte)'z'; return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(changed));
    }

    /// <summary>
    ///     A non-finite mean or weight read from a signed file is a format error, never another exception.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_Read_ResignedNonFiniteMean_Throws()
    {
        var bytes = Create().ToArray();
        const int meanOffset = 4 + 2 + 2 + 8 + 64 + 4;

        var changed = Resign(bytes, b => { BitConverter.GetBytes(float.NaN).CopyTo(b, meanOffset); return b; });

        Assert.Throws<WordSenseTableFormatException>(() => WordSenseWeightsTable.Read(changed));
    }

    /// <summary>
    ///     A table with no lemmas is valid.
    /// </summary>
    [Fact]
    public void WordSenseWeightsTable_RoundTrip_Empty_Works()
    {
        var table = new WordSenseWeightsTable("m", ModelSha, OtherSha, 2, [0f, 1f], new Dictionary<string, SenseWeights>());

        var copy = WordSenseWeightsTable.Read(table.ToArray());

        Assert.Empty(copy.Lemmas);
        Assert.Equal(2, copy.Dimension);
    }
}
