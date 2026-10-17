namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicClassifier"/>.
/// </summary>
public class BasicClassifierTests
{
    private static Task<WordSense[]> Run(params string[] words) =>
        new BasicClassifier().ClassifyAsync(words, TestContext.Current.CancellationToken);

    /// <summary>
    ///     A word that is not in the lexicon has no details and no confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails()
    {
        var results = await Run("zzqxjw");

        Assert.Equal(new WordSense("zzqxjw", null, 0), results[0]);
    }

    /// <summary>
    ///     Punctuation has no details and no confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Punctuation_ReturnsNoDetails()
    {
        var results = await Run(".", "123");

        Assert.All(results, r => Assert.Null(r.Details));
        Assert.All(results, r => Assert.Equal(0.0, r.Confidence));
    }

    /// <summary>
    ///     A word with exactly one lexicon entry is answered with full confidence, whatever its neighbors are.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleEntry_ReturnsEntryWithFullConfidence()
    {
        var single = WordLexicon.Words.First(pair => pair.Value.Length == 1 && pair.Key.All(char.IsAsciiLetterLower));

        var results = await Run(single.Key.ToUpperInvariant());

        Assert.Equal(new WordSense(single.Key.ToUpperInvariant(), single.Value[0], 1.0), results[0]);
    }

    /// <summary>
    ///     Every answer is either empty or carries a rule's confidence, and a null element is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Sentence_AnswersAreConsistent()
    {
        var results = await Run("They", "will", "record", "the", "record", "and", "read", "the", "book", ".");

        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.True(
            r.Details is null ? r.Confidence == 0 : r.Confidence is > 0 and <= 1));
        await Assert.ThrowsAsync<ArgumentException>(() => Run("a", null!));
    }

    /// <summary>
    ///     Normalizing straightens the typographic apostrophe and empties a token without a letter.
    /// </summary>
    [Fact]
    public void BasicClassifier_Normalize_HandlesApostropheAndPunctuation()
    {
        Assert.Equal("don't", BasicClassifier.Normalize("Don\u2019t"));
        Assert.Equal(string.Empty, BasicClassifier.Normalize("..."));
    }
}
