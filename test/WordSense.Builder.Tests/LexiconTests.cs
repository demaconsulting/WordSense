using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the lexicon reader, the morphology expander and the text splitter.
/// </summary>
public sealed class LexiconTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Letters convert to a mask and back.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconSource_MaskAndLettersRoundTrip()
    {
        var mask = LexiconSource.ToMask("NVJ", "test");

        Assert.Equal("NVJ", LexiconSource.ToLetters(mask));
        Assert.Equal(LexiconSource.Bit(PartOfSpeech.Noun) | LexiconSource.Bit(PartOfSpeech.Verb) | LexiconSource.Bit(PartOfSpeech.Adjective), (int)mask);
    }

    /// <summary>
    ///     Empty, unknown, repeated and out-of-order letters are rejected.
    /// </summary>
    /// <param name="letters">The letters.</param>
    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("NN")]
    [InlineData("N V")]
    public void WordSenseBuilder_LexiconSource_ToMask_RejectsBadLetters(string letters)
    {
        Assert.Throws<InvalidDataException>(() => LexiconSource.ToMask(letters, "test"));
    }

    /// <summary>
    ///     The letters may be in any order, the order is kept by the reader, and the determiner letter is known.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconSource_ReadWords_KeepsSenseOrderAndKnowsDeterminer()
    {
        var path = _dir.Write("lexicon.tsv", "so\tBCRJIN\nthe\tD\n");

        var words = LexiconSource.ReadWords(path);

        Assert.Equal("BCRJIN", words[0].Letters);
        Assert.Equal("NRJBCI", LexiconSource.ToLetters(words[0].Mask));
        Assert.Equal(LexiconSource.Bit(PartOfSpeech.Determiner), words[1].Mask);
    }

    /// <summary>
    ///     A lexicon file is read in order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconSource_ReadWords_ReadsAscendingWords()
    {
        var path = _dir.Write("lexicon.tsv", "ant\tN\nbee\tNV\n\ncat\tJ\n");

        var words = LexiconSource.ReadWords(path);

        Assert.Equal(new[] { "ant", "bee", "cat" }, words.Select(w => w.Word));
        Assert.Equal("NV", LexiconSource.ToLetters(words[1].Mask));
    }

    /// <summary>
    ///     Unsorted, duplicate and malformed lines are rejected.
    /// </summary>
    /// <param name="content">The file content.</param>
    [Theory]
    [InlineData("bee\tN\nant\tN\n")]
    [InlineData("ant\tN\nant\tN\n")]
    [InlineData("Ant\tN\n")]
    [InlineData("ant\n")]
    [InlineData("ant\tN\textra\n")]
    public void WordSenseBuilder_LexiconSource_ReadWords_RejectsBadFiles(string content)
    {
        var path = _dir.Write("lexicon.tsv", content);

        Assert.Throws<InvalidDataException>(() => LexiconSource.ReadWords(path));
    }

    /// <summary>
    ///     The morphology file is optional and lists the rule states.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconSource_ReadMorphologyIfPresent_ReadsStates()
    {
        Assert.Null(LexiconSource.ReadMorphologyIfPresent(_dir.Root));
        _dir.Write(LexiconSource.MorphologyFile, "# comment\nPlural\tenabled\tx\nPastTense\tdisabled\ty\n");

        var rules = LexiconSource.ReadMorphologyIfPresent(_dir.Root);

        Assert.Equal(new[] { ("Plural", true), ("PastTense", false) }, rules!.ToArray());
    }

    /// <summary>
    ///     Only enabled rules are returned, and a missing file enables none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MorphologyExpander_ReadEnabled_ReturnsEnabledRules()
    {
        var path = _dir.Write("morphology.tsv", "# c\nPlural\tenabled\tx\nPastTense\tdisabled\ty\nAdverb\tenabled\tz\n");

        Assert.Equal(new HashSet<string> { "Plural", "Adverb" }, MorphologyExpander.ReadEnabled(path));
        Assert.Empty(MorphologyExpander.ReadEnabled(Path.Combine(_dir.Root, "missing.tsv")));
    }

    /// <summary>
    ///     Every rule generates the expected forms with the base as lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MorphologyExpander_Expand_GeneratesForms()
    {
        var verb = LexiconSource.Bit(PartOfSpeech.Verb);
        var adjective = LexiconSource.Bit(PartOfSpeech.Adjective);
        var noun = LexiconSource.Bit(PartOfSpeech.Noun);
        (string, ushort)[] bases = [("city", noun), ("happy", adjective), ("walk", verb)];

        var forms = MorphologyExpander.Expand(bases, _ => false, new HashSet<string> { "Plural", "PastTense", "Adverb" }, Single);

        Assert.Equal(new[] { "cities", "citys", "happily", "walked", "walks" }, forms.Keys.ToArray());
        Assert.Equal(PartOfSpeech.Adverb, forms["happily"][0].PartOfSpeech);
        Assert.Equal("happy", forms["happily"][0].Lemma);
        Assert.Equal(PartOfSpeech.Verb, forms["walked"][0].PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, forms["cities"][0].PartOfSpeech);
    }

    /// <summary>
    ///     Known words are not replaced and disabled rules generate nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MorphologyExpander_Expand_RespectsKnownWordsAndDisabledRules()
    {
        (string, ushort)[] bases = [("walk", LexiconSource.Bit(PartOfSpeech.Verb))];

        var withKnown = MorphologyExpander.Expand(bases, w => w == "walks", new HashSet<string> { "Plural", "PastTense" }, Single);
        var none = MorphologyExpander.Expand(bases, _ => false, new HashSet<string>(), Single);

        Assert.Equal(new[] { "walked" }, withKnown.Keys.ToArray());
        Assert.Empty(none);
    }

    /// <summary>
    ///     Words, spaces and punctuation tile the text exactly, and apostrophes and hyphens stay inside words.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TextSplitter_SplitSegments_TilesText()
    {
        const string text = "Don't re-read  it, ok?";

        var segments = TextSplitter.SplitSegments(text);

        Assert.Equal(text, string.Concat(segments.Select(s => text.Substring(s.Start, s.Length))));
        Assert.Equal(new[] { "Don't", "re-read", "it", ",", "ok", "?" }, TextSplitter.SplitWords(text).Select(w => w.Text).ToArray());
        Assert.Contains(segments, s => s.IsSpace && s.Length == 2);
    }

    /// <summary>
    ///     Offsets point at the words and surrogate pairs are never split.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TextSplitter_SplitWords_KeepsOffsetsAndSurrogatePairs()
    {
        const string text = "a \U0001F600 b";

        var words = TextSplitter.SplitWords(text);

        Assert.Equal(3, words.Count);
        Assert.Equal((0, 1, "a"), words[0]);
        Assert.Equal((2, 2, "\U0001F600"), words[1]);
        Assert.Equal((5, 1, "b"), words[2]);
        Assert.Empty(TextSplitter.SplitWords("   "));
    }

    private static WordDetails[] Single(string lemma, PartOfSpeech pos) => [new WordDetails(lemma, pos, null, null)];
}
