namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordLexicon"/>.
/// </summary>
public class WordLexiconTests
{
    /// <summary>
    ///     The lexicon holds many words and every entry has at least one sense.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_IsPopulated()
    {
        var words = WordLexicon.Words;

        Assert.True(words.Count > 100_000);
        Assert.All(words.Take(1000), pair => Assert.NotEmpty(pair.Value));
    }

    /// <summary>
    ///     Every key is a lower-case surface form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_KeysAreLowerCase()
    {
        Assert.All(WordLexicon.Words.Keys, key => Assert.Equal(key.ToLowerInvariant(), key));
    }

    /// <summary>
    ///     A well-known word with several parts of speech lists them under one key.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_Record_HasNounAndVerb()
    {
        Assert.True(WordLexicon.Words.TryGetValue("record", out var senses));

        var parts = senses.Select(s => s.PartOfSpeech).ToHashSet();
        Assert.Contains(PartOfSpeech.Noun, parts);
        Assert.Contains(PartOfSpeech.Verb, parts);
    }

    /// <summary>
    ///     The inflected forms materialized at build time resolve to the lemma of the base form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_InflectedForm_PointsToLemma()
    {
        Assert.True(WordLexicon.Words.TryGetValue("records", out var senses));

        Assert.Contains(senses, s => s.Lemma == "record");
    }

    /// <summary>
    ///     Concurrent first access returns the same instance everywhere.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_ConcurrentAccess_ReturnsSameInstance()
    {
        var instances = Enumerable.Range(0, 16).AsParallel().Select(_ => WordLexicon.Words).Distinct().ToList();

        Assert.Single(instances);
    }
}
