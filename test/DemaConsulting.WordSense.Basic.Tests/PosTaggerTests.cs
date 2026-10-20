namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="PosTagger"/> on a toy table.
/// </summary>
public class PosTaggerTests
{
    private const int Open = (1 << PosTag.Noun) | (1 << PosTag.Verb);

    private static double[] Run(TaggerToken[] sentence)
    {
        var posteriors = new double[sentence.Length * PosTag.Count];
        PosTagger.Posteriors(TestTables.Load(TestTables.Default()), sentence, posteriors);
        return posteriors;
    }

    private static double At(double[] posteriors, int position, int tag) => posteriors[(position * PosTag.Count) + tag];

    /// <summary>
    ///     An unknown word that is capitalized inside the sentence is a noun; at the sentence start it is not forced.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_CapitalizedUnknownWord_IsNounInsideSentenceOnly()
    {
        var inside = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("zorped", 0, true)]);
        var start = Run([TaggerToken.Word("zorped", 0, true), TaggerToken.Word("they", 0, false)]);

        Assert.Equal(1.0, At(inside, 1, PosTag.Noun), 9);
        Assert.True(At(start, 0, PosTag.Noun) < 1.0);
    }

    /// <summary>
    ///     A word after a determiner is a noun when the word could be either.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_AfterDeterminer_PrefersNoun()
    {
        var posteriors = Run([TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)]);

        Assert.True(At(posteriors, 1, PosTag.Noun) > 0.5);
        Assert.True(At(posteriors, 1, PosTag.Noun) > At(posteriors, 1, PosTag.Verb));
    }

    /// <summary>
    ///     A word after a pronoun is a verb when the word could be either.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_AfterPronoun_PrefersVerb()
    {
        var posteriors = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false)]);

        Assert.True(At(posteriors, 1, PosTag.Verb) > 0.5);
    }

    /// <summary>
    ///     Every row sums to one and only the possible tags have probability.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_RowsSumToOne()
    {
        var posteriors = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false), TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)]);

        for (var i = 0; i < 4; i++)
        {
            var row = Enumerable.Range(0, PosTag.Count).Select(t => At(posteriors, i, t)).ToArray();
            Assert.Equal(1.0, row.Sum(), 9);
            Assert.All(row, p => Assert.InRange(p, 0.0, 1.0));
        }

        Assert.Equal(0.0, At(posteriors, 0, PosTag.Noun));
    }

    /// <summary>
    ///     A token with a single possible tag has the posterior 1 for it.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_SingleTag_IsCertain()
    {
        var posteriors = Run([TaggerToken.Word("the", 0, false), TaggerToken.Fixed(PosTag.Punctuation)]);

        Assert.Equal(1.0, At(posteriors, 0, PosTag.Determiner), 9);
        Assert.Equal(1.0, At(posteriors, 1, PosTag.Punctuation), 9);
    }

    /// <summary>
    ///     An unknown capitalized word inside a sentence is a noun; at the start its suffix decides.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_UnknownWords_UseCapitalizationAndSuffix()
    {
        var inside = Run([TaggerToken.Word("the", 0, false), TaggerToken.Word("zed", 0, true)]);
        var suffix = Run([TaggerToken.Word("walked", 0, false)]);

        Assert.Equal(1.0, At(inside, 1, PosTag.Noun), 9);
        Assert.True(At(suffix, 0, PosTag.Verb) > At(suffix, 0, PosTag.Noun));
        Assert.Equal(0.0, At(suffix, 0, PosTag.Determiner));
    }

    /// <summary>
    ///     An empty sentence needs no result space; a buffer that is too small is rejected.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_EmptyAndSmallBuffer()
    {
        var tables = TestTables.Load(TestTables.Default());

        PosTagger.Posteriors(tables, [], []);

        Assert.Throws<ArgumentException>(() => PosTagger.Posteriors(tables, [TaggerToken.Fixed(PosTag.Other)], new double[PosTag.Count - 1]));
    }

    /// <summary>
    ///     The same input gives bit-identical posteriors.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_IsDeterministic()
    {
        TaggerToken[] sentence = [TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false), TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)];

        Assert.Equal(Run(sentence), Run(sentence));
    }
}
