namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="SenseDefinition"/>, <see cref="LemmaModel"/> sense sets, the result records, the abstain
///     rule and text splitting.
/// </summary>
public class SenseRecordTests
{
    private static LemmaModel Model(params SenseDefinition[] senses)
    {
        var kind = senses.Length == 2 ? LemmaModelKind.Binary : LemmaModelKind.OneVersusRest;
        var rows = kind == LemmaModelKind.Binary ? 1 : senses.Length;
        return new LemmaModel("bow", senses, kind, new float[rows * 3], new float[rows], 0.5f, 3);
    }

    /// <summary>
    ///     A model exposes one shared answer per sense: lemma, part of speech and pronunciation.
    /// </summary>
    [Fact]
    public void LemmaModel_Candidates_MapSensesToDetails()
    {
        var model = Model(
            new SenseDefinition(PartOfSpeech.Noun, "weapon", "boh"),
            new SenseDefinition(PartOfSpeech.Verb, "bend", "bow"));

        Assert.True(model.IsHeteronym);
        Assert.Same(model.Candidates, model.Candidates);
        Assert.Equal(new WordDetails("bow", PartOfSpeech.Noun, new Pronunciation("weapon", "boh")), model.Candidates[0]);
        Assert.Equal(new WordDetails("bow", PartOfSpeech.Verb, new Pronunciation("bend", "bow")), model.Candidates[1]);
    }

    /// <summary>
    ///     A lemma whose senses have no meanings is not a heteronym and has no pronunciation.
    /// </summary>
    [Fact]
    public void LemmaModel_NonHeteronym_HasNoPronunciation()
    {
        var model = Model(new SenseDefinition(PartOfSpeech.Noun), new SenseDefinition(PartOfSpeech.Verb));

        Assert.False(model.IsHeteronym);
        Assert.All(model.Candidates, c => Assert.Null(c.Pronunciation));
    }

    /// <summary>
    ///     A respelling is optional and stays null when it is not authored.
    /// </summary>
    [Fact]
    public void LemmaModel_MissingRespelling_IsNull()
    {
        var model = Model(new SenseDefinition(PartOfSpeech.Noun, "weapon"), new SenseDefinition(PartOfSpeech.Verb, "bend"));

        Assert.Null(model.Candidates[0].Pronunciation!.Respelling);
        Assert.Equal("weapon", model.Candidates[0].Pronunciation!.Meaning);
    }

    /// <summary>
    ///     Senses of one meaning may differ in part of speech, and the same pair twice is rejected.
    /// </summary>
    [Fact]
    public void LemmaModel_SenseSet_Rules()
    {
        var ok = Model(
            new SenseDefinition(PartOfSpeech.Noun, "guide"),
            new SenseDefinition(PartOfSpeech.Verb, "guide"),
            new SenseDefinition(PartOfSpeech.Noun, "metal"));
        Assert.True(ok.IsHeteronym);

        // Meanings must be given for all or none of the senses
        Assert.Throws<ArgumentException>(() => Model(new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb)));

        // The part of speech and meaning pair is unique
        Assert.Throws<ArgumentException>(() => Model(new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Noun, "a")));
        Assert.Throws<ArgumentException>(() => Model(new SenseDefinition(PartOfSpeech.Noun), new SenseDefinition(PartOfSpeech.Noun)));

        // A heteronym needs two distinct meanings
        Assert.Throws<ArgumentException>(() => Model(new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb, "a")));

        // One respelling per meaning, and distinct meanings sound different
        Assert.Throws<ArgumentException>(() => Model(
            new SenseDefinition(PartOfSpeech.Noun, "a", "ay"),
            new SenseDefinition(PartOfSpeech.Verb, "a", "ee"),
            new SenseDefinition(PartOfSpeech.Adjective, "b", "bee")));
        Assert.Throws<ArgumentException>(() => Model(new SenseDefinition(PartOfSpeech.Noun, "a", "same"), new SenseDefinition(PartOfSpeech.Verb, "b", "same")));
    }

    /// <summary>
    ///     Definitions with an undefined part of speech, an invalid meaning or a respelling without a meaning are rejected.
    /// </summary>
    [Fact]
    public void SenseDefinition_Validate_InvalidDefinitions_Throw()
    {
        new SenseDefinition(PartOfSpeech.Noun).Validate();
        new SenseDefinition(PartOfSpeech.Noun, "weapon", "boh").Validate();
        Assert.Throws<ArgumentException>(() => new SenseDefinition((PartOfSpeech)99).Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "Weapon").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "a\n").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, null, "boh").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "a", "b o").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "a", "").Validate());
    }

    /// <summary>
    ///     Meaning identifiers are lower-case letters, digits and hyphens, starting with a letter.
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
    [InlineData("noun\n", false)]
    [InlineData(null, false)]
    public void SenseDefinition_IsValidMeaning_ChecksSyntax(string? meaning, bool expected)
    {
        Assert.Equal(expected, SenseDefinition.IsValidMeaning(meaning));
    }

    /// <summary>
    ///     Respellings are letters, hyphens and apostrophes, and both texts have a length limit.
    /// </summary>
    [Fact]
    public void SenseDefinition_IsValidRespelling_ChecksSyntaxAndLength()
    {
        Assert.True(SenseDefinition.IsValidRespelling("ree-KORD"));
        Assert.True(SenseDefinition.IsValidRespelling("don't"));
        Assert.False(SenseDefinition.IsValidRespelling("a1"));
        Assert.False(SenseDefinition.IsValidRespelling("a b"));
        Assert.False(SenseDefinition.IsValidRespelling(null));
        Assert.True(SenseDefinition.IsValidRespelling(new string('a', SenseDefinition.MaxRespellingLength)));
        Assert.False(SenseDefinition.IsValidRespelling(new string('a', SenseDefinition.MaxRespellingLength + 1)));
        Assert.True(SenseDefinition.IsValidMeaning(new string('a', SenseDefinition.MaxMeaningLength)));
        Assert.False(SenseDefinition.IsValidMeaning(new string('a', SenseDefinition.MaxMeaningLength + 1)));
    }

    /// <summary>
    ///     The result records compare by value and the analysis of a word keeps its input exactly.
    /// </summary>
    [Fact]
    public void ResultRecords_HaveValueEquality()
    {
        var details = new WordDetails("record", PartOfSpeech.Verb, new Pronunciation("verb", null));
        var analysis = new WordAnalysis("Records", details, 1.5, false, null, 0.0);

        Assert.Equal(new WordDetails("record", PartOfSpeech.Verb, new Pronunciation("verb", null)), details);
        Assert.Equal(analysis, analysis with { });
        Assert.Equal("Records", analysis.Word);
        Assert.Equal(new WordSenseToken(0, 7, "Records", analysis), new WordSenseToken(0, 7, "Records", analysis));
        Assert.Equal(8, Enum.GetValues<PartOfSpeech>().Length);
    }

    /// <summary>
    ///     A NaN margin or a NaN threshold abstains, and a margin at the threshold answers.
    /// </summary>
    [Fact]
    public void AbstainRule_Abstains_NaNMarginOrThreshold_Abstains()
    {
        Assert.True(AbstainRule.Abstains(double.NaN, 0.5));
        Assert.True(AbstainRule.Abstains(0.5, double.NaN));
        Assert.True(AbstainRule.Abstains(double.NaN, double.NaN));
        Assert.True(AbstainRule.Abstains(0.4, 0.5));
        Assert.False(AbstainRule.Abstains(0.5, 0.5));
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

    /// <summary>
    ///     Internal apostrophes, hyphens and digits stay inside one word; typographic apostrophes, combining
    ///     marks and surrogate pairs are handled.
    /// </summary>
    [Theory]
    [InlineData("don't")]
    [InlineData("don\u2019t")]
    [InlineData("well-known")]
    [InlineData("x1")]
    [InlineData("cafe\u0301")]
    [InlineData("\U0001D400bc")]
    public void TextSplitter_SplitWords_InternalPunctuationAndMarks_StayOneWord(string text)
    {
        var words = TextSplitter.SplitWords(text);

        var word = Assert.Single(words);
        Assert.Equal(text, word.Text);
    }

    /// <summary>
    ///     A symbol outside the basic plane is one tile and a trailing apostrophe is not part of the word.
    /// </summary>
    [Fact]
    public void TextSplitter_SplitWords_SurrogatePairAndTrailingApostrophe_AreSeparateTiles()
    {
        Assert.Equal(["a", "\U0001F600", "b"], TextSplitter.SplitWords("a \U0001F600 b").Select(w => w.Text));
        Assert.Equal(["dogs", "'"], TextSplitter.SplitWords("dogs'").Select(w => w.Text));
    }

    /// <summary>
    ///     A trailing hyphen is not part of the word and is its own tile; the tiles still cover the text.
    /// </summary>
    [Fact]
    public void TextSplitter_SplitWords_TrailingHyphen_IsOwnTile()
    {
        Assert.Equal(["well", "-"], TextSplitter.SplitWords("well-").Select(w => w.Text));
        Assert.Equal(["well-known", "-"], TextSplitter.SplitWords("well-known-").Select(w => w.Text));
        Assert.Equal(["a", "-", "-", "b"], TextSplitter.SplitWords("a--b").Select(w => w.Text));
    }
}
