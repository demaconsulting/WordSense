using System.Security.Cryptography;
using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the treebank counts of the basic model.
/// </summary>
public sealed class UdCountsTests : IDisposable
{
    private const string Conllu =
        "# sent_id = 1\n" +
        "1-2\tdon't\t_\t_\t_\t_\t_\t_\t_\t_\n" +
        "1\tdo\tdo\tAUX\t_\t_\t0\troot\t_\t_\n" +
        "2\tn't\tnot\tPART\t_\t_\t1\tadvmod\t_\t_\n" +
        "3\thome\thome\tNOUN\t_\t_\t1\tobj\t_\t_\n" +
        "3.1\tghost\tghost\tVERB\t_\t_\t_\t_\t_\t_\n" +
        "\n" +
        "1\tTo\tto\tPART\t_\t_\t2\tmark\t_\t_\n" +
        "2\trun\trun\tVERB\t_\t_\t0\troot\t_\t_\n" +
        "3\t.\t.\tPUNCT\t_\t_\t2\tpunct\t_\t_\n" +
        "\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private List<UdSentence> Read()
    {
        var path = _dir.Write("ud/sample.conllu", Conllu);
        return UdCounts.ReadConllu(path);
    }

    /// <summary>
    ///     The reader keeps a multi-word token as one token and skips empty nodes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_ReadConllu_RangeTokensAndEmptyNodes()
    {
        var sentences = Read();

        Assert.Equal(2, sentences.Count);
        Assert.Equal(["don't", "home"], sentences[0].Words);
        Assert.Equal([PosTag.Auxiliary, PosTag.Noun], sentences[0].Tags);
        Assert.Equal([PosTag.InfinitiveTo, PosTag.Verb, PosTag.Punctuation], sentences[1].Tags);
    }

    /// <summary>
    ///     A line with too few fields is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_ReadConllu_ShortLine_Throws()
    {
        var path = _dir.Write("ud/bad.conllu", "1\tword\n");

        Assert.Throws<InvalidDataException>(() => UdCounts.ReadConllu(path));
    }

    /// <summary>
    ///     The universal tags map onto the tags of the tagger.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_MapTag_MapsUniversalTags()
    {
        Assert.Equal(PosTag.Noun, UdCounts.MapTag("PROPN", "Paris"));
        Assert.Equal(PosTag.Conjunction, UdCounts.MapTag("SCONJ", "if"));
        Assert.Equal(PosTag.InfinitiveTo, UdCounts.MapTag("PART", "To"));
        Assert.Equal(PosTag.Other, UdCounts.MapTag("PART", "not"));
        Assert.Equal(PosTag.Other, UdCounts.MapTag("X", "x"));
    }

    /// <summary>
    ///     Only tokens made of letters and apostrophes get a word key.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_KeyOf_AcceptsLettersAndApostrophes()
    {
        Assert.Equal("don't", UdCounts.KeyOf("Don\u2019t"));
        Assert.Null(UdCounts.KeyOf("3rd"));
        Assert.Null(UdCounts.KeyOf("..."));
        Assert.Null(UdCounts.KeyOf("'"));
    }

    /// <summary>
    ///     The counts include the sentence boundaries and the word rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_Count_CountsNgramsAndWords()
    {
        var counts = UdCounts.Count(Read());

        Assert.Equal(2, counts.Unigrams[PosTag.Boundary]);
        Assert.Equal(1, counts.Unigrams[PosTag.Noun]);
        Assert.Equal(1, counts.Bigrams[(PosTag.Noun * PosTag.Extended) + PosTag.Boundary]);
        Assert.Equal(1, counts.Trigrams[(((PosTag.Boundary * PosTag.Extended) + PosTag.Boundary) * PosTag.Extended) + PosTag.Auxiliary]);
        Assert.Equal(1, counts.Words["to"][PosTag.InfinitiveTo]);
        Assert.False(counts.Words.ContainsKey("."));
    }

    /// <summary>
    ///     The count files read back as written and are written byte for byte the same each time.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_WriteAndRead_RoundTripsDeterministically()
    {
        var counts = UdCounts.Count(Read());
        var first = Path.Join(_dir.Root, "out1");
        var second = Path.Join(_dir.Root, "out2");

        counts.Write(first);
        counts.Write(second);
        var back = UdCounts.Read(first);

        Assert.Equal(counts.Unigrams, back.Unigrams);
        Assert.Equal(counts.Bigrams, back.Bigrams);
        Assert.Equal(counts.Trigrams, back.Trigrams);
        Assert.Equal(counts.Words.Keys, back.Words.Keys);
        Assert.All(counts.Words, pair => Assert.Equal(pair.Value, back.Words[pair.Key]));
        foreach (var file in new[] { UdCounts.NgramsFile, UdCounts.WordsFile })
        {
            Assert.Equal(File.ReadAllBytes(Path.Join(first, file)), File.ReadAllBytes(Path.Join(second, file)));
            Assert.DoesNotContain("\r", File.ReadAllText(Path.Join(first, file)), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A count file with a wrong header is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdCounts_Read_BadHeader_Throws()
    {
        var counts = UdCounts.Count(Read());
        var directory = Path.Join(_dir.Root, "bad");
        counts.Write(directory);
        File.WriteAllText(Path.Join(directory, UdCounts.WordsFile), "wrong\theader\n");

        Assert.Throws<InvalidDataException>(() => UdCounts.Read(directory));
    }

    /// <summary>
    ///     A file is accepted with its checksum and rejected with another.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_UdSource_Verify_ComparesChecksum()
    {
        var path = _dir.Write("ud/sample.conllu", Conllu);
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        UdSource.Verify(path, sha);

        Assert.Throws<InvalidDataException>(() => UdSource.Verify(path, new string('0', 64)));
        Assert.Equal(2, UdSource.Files.Count);
        Assert.DoesNotContain(UdSource.Files.Keys, k => k.Contains("test", StringComparison.Ordinal));
    }
}
