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
        string[] words = ["The", "technician's", "tools", "of", "sergeants\u2019", "."];

        var (tokens, start) = PossessiveView.Expand(words, w => w.EndsWith("'s", StringComparison.Ordinal) || w.EndsWith('\u2019'));

        Assert.Equal(["The", "technician", "'s", "tools", "of", "sergeants", "\u2019", "."], tokens);
        Assert.Equal([0, 1, 3, 4, 5, 7], start);
    }

    /// <summary>
    ///     Words that are not possessives, a word that is only its marker and an empty text stay as they are.
    /// </summary>
    [Fact]
    public void PossessiveView_Expand_NoPossessives_IsIdentity()
    {
        string[] words = ["don't", "'s", "'", "dog"];

        var (tokens, start) = PossessiveView.Expand(words, _ => true);
        var (none, noStart) = PossessiveView.Expand([], _ => true);

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
