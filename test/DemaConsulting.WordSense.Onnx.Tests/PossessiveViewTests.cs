namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="PossessiveView"/>.
/// </summary>
public class PossessiveViewTests
{
    /// <summary>
    ///     A possessive is split into its stem and its marker, and every word maps to its first token.
    /// </summary>
    [Fact]
    public void PossessiveView_Expand_Possessives_SplitsStemAndMarker()
    {
        // Arrange: words with a straight and a typographic possessive, and a predicate that recognizes both
        string[] words = ["The", "technician's", "tools", "of", "sergeants\u2019", "."];

        // Act: expand the possessives
        var (tokens, start) = PossessiveView.Expand(words, w => w.EndsWith("'s", StringComparison.Ordinal) || w.EndsWith('\u2019'));

        // Assert: stems and markers are separate tokens and the start indices point at each word's first token
        Assert.Equal(["The", "technician", "'s", "tools", "of", "sergeants", "\u2019", "."], tokens);
        Assert.Equal([0, 1, 3, 4, 5, 7], start);
    }

    /// <summary>
    ///     Words that are not possessives, a word that is only its marker and an empty text stay as they are.
    /// </summary>
    [Fact]
    public void PossessiveView_Expand_NoPossessives_IsIdentity()
    {
        // Arrange: a contraction, two bare markers and an ordinary word, with a predicate that accepts everything
        string[] words = ["don't", "'s", "'", "dog"];

        // Act: expand the words, and an empty text
        var (tokens, start) = PossessiveView.Expand(words, _ => true);
        var (none, noStart) = PossessiveView.Expand([], _ => true);

        // Assert: the tokens are the words, the starts are consecutive and the empty text stays empty
        Assert.Equal(words, tokens);
        Assert.Equal([0, 1, 2, 3], start);
        Assert.Empty(none);
        Assert.Empty(noStart);
    }

    /// <summary>
    ///     Null arguments and null words are rejected.
    /// </summary>
    [Fact]
    public void PossessiveView_NullArguments_Throw()
    {
        // Arrange: null words, a null predicate and a null word in the list
        // Act / Assert: each call throws
        Assert.Throws<ArgumentNullException>(() => PossessiveView.Expand(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => PossessiveView.Expand(["a"], null!));
        Assert.Throws<ArgumentNullException>(() => PossessiveView.Expand([null!], _ => true));
        Assert.Throws<ArgumentNullException>(() => PossessiveView.IsLexiconPossessive(null!));
    }

    /// <summary>
    ///     The lexicon test accepts a possessive of a known stem (straight or typographic apostrophe) and rejects every
    ///     other word.
    /// </summary>
    [Fact]
    public void PossessiveView_IsLexiconPossessive_DecidesByLexicon()
    {
        // Arrange: possessives of known stems, and contractions, ordinary words and unknown stems
        // Act / Assert: the lexicon test accepts the possessives
        Assert.True(PossessiveView.IsLexiconPossessive("technician's"));
        Assert.True(PossessiveView.IsLexiconPossessive("Technician\u2019s"));
        Assert.True(PossessiveView.IsLexiconPossessive("sergeants'"));

        // A contraction is not a possessive, and neither are an ordinary word, a bare marker and an unknown stem
        Assert.False(PossessiveView.IsLexiconPossessive("it's"));
        Assert.False(PossessiveView.IsLexiconPossessive("don't"));
        Assert.False(PossessiveView.IsLexiconPossessive("record"));
        Assert.False(PossessiveView.IsLexiconPossessive("'s"));
        Assert.False(PossessiveView.IsLexiconPossessive("xyzzyq's"));
        Assert.False(PossessiveView.IsLexiconPossessive("."));
    }
}
