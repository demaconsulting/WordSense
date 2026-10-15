namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSense"/>, <see cref="SenseDefinition"/>, tag scoring and text splitting.
/// </summary>
public class SenseRecordTests
{
    private static WordSense Sense(string lemma, string name, params string[] tags) => new(lemma, name, tags);

    /// <summary>
    ///     The record exposes lemma, sense and tags and answers tag membership ordinally.
    /// </summary>
    [Fact]
    public void WordSense_HasTag_ReportsMembership()
    {
        var sense = Sense("used", "past", "past", "verb");

        Assert.Equal("used", sense.Lemma);
        Assert.Equal("past", sense.Sense);
        Assert.Equal(["past", "verb"], sense.Tags.Order(StringComparer.Ordinal));
        Assert.True(sense.HasTag("verb"));
        Assert.False(sense.HasTag("noun"));
        Assert.False(sense.HasTag("VERB"));
        Assert.False(sense.HasTag(string.Empty));
        Assert.Throws<ArgumentNullException>(() => sense.HasTag(null!));
        Assert.Equal("used:past", sense.ToString());
    }

    /// <summary>
    ///     The tag set cannot be modified by a caller.
    /// </summary>
    [Fact]
    public void WordSense_Tags_AreImmutable()
    {
        var source = new List<string> { "noun" };
        var sense = new WordSense("a", "b", source);

        source.Add("verb");

        Assert.False(sense.HasTag("verb"));
        Assert.Throws<NotSupportedException>(() => ((ISet<string>)sense.Tags).Add("verb"));
        Assert.False(sense.HasTag("verb"));
    }

    /// <summary>
    ///     A model creates exactly one shared record per sense.
    /// </summary>
    [Fact]
    public void WordSense_LemmaModel_SharesOneRecordPerSense()
    {
        var model = WordSenseTableTests.CreateSmallTable().Lemmas[0];

        Assert.Same(model.Senses[0], model.Senses[0]);
        Assert.Equal(2, model.Senses.Count);
        Assert.Equal(["animal", "tool"], model.Senses.Select(s => s.Sense));
    }

    /// <summary>
    ///     The group defaults to the sense label and an explicit group is kept; an invalid group is rejected.
    /// </summary>
    [Fact]
    public void WordSense_Group_DefaultsToSenseAndValidates()
    {
        Assert.Equal("weapon", new WordSense("bow", "weapon", ["noun"]).Group);
        Assert.Equal("weapon", new WordSense("bow", "weapon", ["noun"], string.Empty).Group);
        Assert.Equal("bend", new WordSense("bow", "bend-verb", ["verb"], "bend").Group);
        new SenseDefinition("bend-verb", ["verb"], "bend").Validate();
        new SenseDefinition("weapon", ["noun"]).Validate();
        Assert.Throws<ArgumentException>(() => new SenseDefinition("bend-verb", ["verb"], "Bend Group").Validate());
    }

    /// <summary>
    ///     Tag syntax is lower-case letters, digits and hyphens, starting with a letter.
    /// </summary>
    [Theory]
    [InlineData("noun", true)]
    [InlineData("past-tense", true)]
    [InlineData("a1", true)]
    [InlineData("", false)]
    [InlineData("Noun", false)]
    [InlineData("1a", false)]
    [InlineData("-a", false)]
    [InlineData("a b", false)]
    [InlineData("a_b", false)]
    [InlineData("été", false)]
    public void SenseDefinition_IsValidTag_ChecksSyntax(string tag, bool expected)
    {
        Assert.Equal(expected, SenseDefinition.IsValidTag(tag));
    }

    /// <summary>
    ///     Tags longer than the limit are invalid.
    /// </summary>
    [Fact]
    public void SenseDefinition_IsValidTag_LengthLimit()
    {
        Assert.True(SenseDefinition.IsValidTag(new string('a', SenseDefinition.MaxTagLength)));
        Assert.False(SenseDefinition.IsValidTag(new string('a', SenseDefinition.MaxTagLength + 1)));
    }

    private static readonly WordSense[] Mixed =
    [
        Sense("bow", "knot", "noun"),
        Sense("bow", "weapon", "noun"),
        Sense("bow", "stoop", "verb"),
        Sense("bow", "color", "adj"),
    ];

    private static readonly string[] NounVerb = ["noun", "verb"];

    /// <summary>
    ///     The tag with the best sense score wins.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_BestScoringTagWins()
    {
        Assert.Equal("noun", TagScorer.Classify(Mixed, [0f, 3f, 1f, 0f], NounVerb, 0.5));
        Assert.Equal("verb", TagScorer.Classify(Mixed, [0f, 1f, 3f, 0f], NounVerb, 0.5));
    }

    /// <summary>
    ///     Two queried tags with equal best scores tie, so a positive threshold abstains and the first tag wins at zero.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_EqualTagScores_TiesAndIsBelowAnyPositiveThreshold()
    {
        float[] scores = [0f, 3f, 3f, 0f];

        Assert.Null(TagScorer.Classify(Mixed, scores, NounVerb, 0.1));
        Assert.Equal("noun", TagScorer.Classify(Mixed, scores, NounVerb, 0));
    }

    /// <summary>
    ///     The margin is taken between tag scores, not between senses: two senses of the same tag do not compete.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_SensesOfSameTag_DoNotReduceMargin()
    {
        WordSense[] senses = [Sense("x", "a", "noun"), Sense("x", "b", "noun"), Sense("x", "c", "verb")];

        Assert.Equal("noun", TagScorer.Classify(senses, [2.0f, 1.9f, 0f], NounVerb, 1.5));
    }

    /// <summary>
    ///     A margin below the threshold abstains at tag level, and one at the threshold does not.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_BelowThreshold_Abstains()
    {
        WordSense[] senses = [Sense("x", "a", "noun"), Sense("x", "b", "verb")];

        Assert.Null(TagScorer.Classify(senses, [1.0f, 0.6f], NounVerb, 0.5));
        Assert.Equal("noun", TagScorer.Classify(senses, [1.0f, 0.5f], NounVerb, 0.5));
    }

    /// <summary>
    ///     When a sense with none of the queried tags scores best, the answer is null.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_OtherWins_ReturnsNull()
    {
        Assert.Null(TagScorer.Classify(Mixed, [0f, 1f, 0f, 9f], NounVerb, 0));
    }

    /// <summary>
    ///     The other group counts as the runner-up of a queried tag.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_OtherAsRunnerUp_LimitsMargin()
    {
        WordSense[] senses = [Sense("x", "a", "noun"), Sense("x", "b", "adj")];

        Assert.Null(TagScorer.Classify(senses, [1.0f, 0.8f], NounVerb, 0.5));
        Assert.Equal("noun", TagScorer.Classify(senses, [1.0f, 0.2f], NounVerb, 0.5));
        Assert.Equal("noun", TagScorer.Classify(senses, [1.0f, 1.0f], NounVerb, 0));
    }

    /// <summary>
    ///     A tag that no sense of the lemma carries gives null, and a single present tag has an infinite margin.
    /// </summary>
    [Fact]
    public void TagScorer_Classify_AbsentTag_ReturnsNull_SinglePresentTag_Answers()
    {
        WordSense[] adjectives = [Sense("x", "a", "adj"), Sense("x", "b", "adv")];
        WordSense[] nounsOnly = [Sense("x", "a", "noun"), Sense("x", "b", "noun")];

        Assert.Null(TagScorer.Classify(adjectives, [1f, 0f], NounVerb, 0));
        Assert.Equal("noun", TagScorer.Classify(nounsOnly, [1f, 1f], NounVerb, 100));
    }

    /// <summary>
    ///     A has-tag query is true when the tag group wins, false when other wins, null when absent or unsure.
    /// </summary>
    [Fact]
    public void TagScorer_IsTag_AnswersBoolOrNull()
    {
        Assert.True(TagScorer.IsTag(Mixed, [0f, 3f, 0f, 0f], "noun", 0.5));
        Assert.False(TagScorer.IsTag(Mixed, [0f, 0f, 0f, 3f], "noun", 0.5));
        Assert.Null(TagScorer.IsTag(Mixed, [0f, 1f, 0f, 1.2f], "noun", 0.5));
        Assert.Null(TagScorer.IsTag(Mixed, [0f, 1f, 0f, 1f], "adv", 0));
        Assert.True(TagScorer.IsTag([Sense("x", "a", "noun"), Sense("x", "b", "noun")], [0f, 1f], "noun", 100));
    }

    /// <summary>
    ///     Segments tile the text exactly, as words, punctuation and whitespace.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("the bat flies.")]
    [InlineData("  A  bat's wing\t\nends — here 😀 now.  ")]
    [InlineData("well-known don't x1 café")]
    public void TextSplitter_SplitSegments_TileTheText(string text)
    {
        var segments = TextSplitter.SplitSegments(text);

        var position = 0;
        foreach (var segment in segments)
        {
            Assert.Equal(position, segment.Start);
            Assert.True(segment.Length > 0);
            position += segment.Length;
        }

        Assert.Equal(text.Length, position);
        Assert.Equal(text, string.Concat(segments.Select(s => text.Substring(s.Start, s.Length))));
        Assert.All(segments, s => Assert.Equal(s.IsSpace, string.IsNullOrWhiteSpace(text.Substring(s.Start, s.Length))));
    }

    /// <summary>
    ///     Words are the non-space tiles with their offsets.
    /// </summary>
    [Fact]
    public void TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets()
    {
        var words = TextSplitter.SplitWords("a  bat.");

        Assert.All(words, w => Assert.Equal(w.Text, "a  bat.".Substring(w.Start, w.Length)));
        Assert.DoesNotContain(words, w => string.IsNullOrWhiteSpace(w.Text));
        Assert.Equal(0, words[0].Start);
    }
}
