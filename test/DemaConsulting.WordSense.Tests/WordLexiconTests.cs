namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordLexicon"/>.
/// </summary>
public class WordLexiconTests
{
    /// <summary>
    ///     A well-known word with several parts of speech lists them under one key.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_KnownWord_ReturnsItsSenses()
    {
        Assert.True(WordLexicon.Words.TryGetValue("record", out var senses));

        var parts = senses.Select(s => s.PartOfSpeech).ToHashSet();
        Assert.Contains(PartOfSpeech.Noun, parts);
        Assert.Contains(PartOfSpeech.Verb, parts);
        Assert.All(senses, s => Assert.Equal("record", s.Lemma));
    }

    /// <summary>
    ///     The inflected forms materialized at build time resolve to the lemma of the base form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_InflectedForm_ReturnsSensesOfBase()
    {
        Assert.True(WordLexicon.Words.TryGetValue("records", out var senses));

        Assert.Contains(senses, s => s.Lemma == "record");
    }

    /// <summary>
    ///     Every key is a lower-case surface form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_Keys_AreLowerCase()
    {
        Assert.All(WordLexicon.Words.Keys, key => Assert.Equal(key.ToLowerInvariant(), key));
    }

    /// <summary>
    ///     A word that is not in the lexicon is absent, and so is a key that is not lower case.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_UnknownWord_IsAbsent()
    {
        Assert.False(WordLexicon.Words.ContainsKey("zzqxjw"));
        Assert.False(WordLexicon.Words.ContainsKey("Record"));
    }

    /// <summary>
    ///     The embedded blob loads and holds many words, each with at least one sense.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_EmbeddedBlob_LoadsAndIsNotEmpty()
    {
        var words = WordLexicon.Words;

        Assert.True(words.Count > 100_000);
        Assert.All(words.Take(1000), pair => Assert.NotEmpty(pair.Value));
    }

    /// <summary>
    ///     Concurrent access returns the same instance everywhere.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_ConcurrentAccess_ReturnsSameInstance()
    {
        var instances = Enumerable.Range(0, 16).AsParallel().Select(_ => WordLexicon.Words).Distinct().ToList();

        Assert.Single(instances);
    }
}
