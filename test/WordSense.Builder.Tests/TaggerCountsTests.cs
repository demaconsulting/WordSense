using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the tag counts of the basic model.
/// </summary>
public sealed class TaggerCountsTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static List<TaggedSentence> Sentences() =>
    [
        new(["don't", "home"], [PosTag.Auxiliary, PosTag.Noun]),
        new(["To", "run", "."], [PosTag.InfinitiveTo, PosTag.Verb, PosTag.Punctuation]),
    ];

    /// <summary>
    ///     The universal tags map onto the tags of the tagger.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_MapTag_MapsUniversalTags()
    {
        Assert.Equal(PosTag.Noun, TaggerCounts.MapTag("PROPN", "Paris"));
        Assert.Equal(PosTag.Conjunction, TaggerCounts.MapTag("SCONJ", "if"));
        Assert.Equal(PosTag.InfinitiveTo, TaggerCounts.MapTag("PART", "To"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("PART", "not"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("X", "x"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("NUM", "3"));
        Assert.Equal(PosTag.Punctuation, TaggerCounts.MapTag("PUNCT", "."));
        Assert.Equal(PosTag.Auxiliary, TaggerCounts.MapTag("AUX", "will"));
    }

    /// <summary>
    ///     Only tokens made of letters and apostrophes get a word key.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_KeyOf_AcceptsLettersAndApostrophes()
    {
        Assert.Equal("don't", TaggerCounts.KeyOf("Don\u2019t"));
        Assert.Null(TaggerCounts.KeyOf("3rd"));
        Assert.Null(TaggerCounts.KeyOf("..."));
        Assert.Null(TaggerCounts.KeyOf("'"));
    }

    /// <summary>
    ///     The counts include the sentence boundaries and the word rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Count_CountsNgramsAndWords()
    {
        var counts = TaggerCounts.Count(Sentences());

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
    public void WordSenseBuilder_TaggerCounts_WriteAndRead_RoundTripsDeterministically()
    {
        var counts = TaggerCounts.Count(Sentences());
        var first = Path.Join(_dir.Root, "out1");
        var second = Path.Join(_dir.Root, "out2");

        counts.Write(first);
        counts.Write(second);
        var back = TaggerCounts.Read(first);

        Assert.Equal(counts.Unigrams, back.Unigrams);
        Assert.Equal(counts.Bigrams, back.Bigrams);
        Assert.Equal(counts.Trigrams, back.Trigrams);
        Assert.Equal(counts.Words.Keys, back.Words.Keys);
        Assert.All(counts.Words, pair => Assert.Equal(pair.Value, back.Words[pair.Key]));
        foreach (var file in new[] { TaggerCounts.NgramsFile, TaggerCounts.WordsFile })
        {
            Assert.Equal(File.ReadAllBytes(Path.Join(first, file)), File.ReadAllBytes(Path.Join(second, file)));
            Assert.DoesNotContain("\r", File.ReadAllText(Path.Join(first, file)), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A count file with a wrong header or a malformed line is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_MalformedFiles_Throw()
    {
        var counts = TaggerCounts.Count(Sentences());
        var directory = Path.Join(_dir.Root, "bad");
        counts.Write(directory);
        File.WriteAllText(Path.Join(directory, TaggerCounts.WordsFile), "wrong\theader\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));

        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.WordsFile), "home\tN:1\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));

        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.NgramsFile), "4\tN N N N\t1\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));
    }
}
