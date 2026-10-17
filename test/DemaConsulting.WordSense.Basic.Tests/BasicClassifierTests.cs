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
    ///     A word with exactly one lexicon entry is answered with full confidence, whatever its neighbors are.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleSenseWord_ReturnsEntryWithFullConfidence()
    {
        var single = WordLexicon.Words.First(pair => pair.Value.Length == 1 && pair.Key.All(char.IsAsciiLetterLower));

        var results = await Run(single.Key.ToUpperInvariant());

        Assert.Equal(new WordSense(single.Key.ToUpperInvariant(), single.Value[0], 1.0), results[0]);
    }

    /// <summary>
    ///     An ambiguous word after a word that fires a rule gets the rule's single sense and fixed confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AmbiguousWordWithRule_ReturnsRuleConfidence()
    {
        var results = await Run("will", "record");

        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
        Assert.Equal(Rules.AfterModalConfidence, results[1].Confidence);
    }

    /// <summary>
    ///     The word after the infinitive marker is the verb.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_RecordAfterTo_IsVerb()
    {
        var results = await Run("to", "record");

        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
        Assert.Equal(Rules.AfterToConfidence, results[1].Confidence);
    }

    /// <summary>
    ///     The word after a determiner is the noun.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_RecordAfterThe_IsNoun()
    {
        var results = await Run("the", "record");

        Assert.Equal(PartOfSpeech.Noun, results[1].Details?.PartOfSpeech);
        Assert.Equal(Rules.AfterDeterminerConfidence, results[1].Confidence);
    }

    /// <summary>
    ///     An ambiguous word without any rule-firing neighbor has no details and no confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NoRuleFires_Abstains()
    {
        var results = await Run("record");

        Assert.Equal(new WordSense("record", null, 0), results[0]);
    }

    /// <summary>
    ///     When several senses share the part of speech a rule selects, the classifier abstains.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SamePartOfSpeechAmbiguity_Abstains()
    {
        var word = WordLexicon.Words.First(pair =>
            pair.Value.Count(s => s.PartOfSpeech == PartOfSpeech.Noun) >= 2
            && pair.Value.Any(s => s.PartOfSpeech != PartOfSpeech.Noun));

        var results = await Run("the", word.Key);

        Assert.Equal(new WordSense(word.Key, null, 0), results[1]);
    }

    /// <summary>
    ///     Lookup ignores case and reads the curly apostrophe as a straight one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_MixedCaseAndCurlyApostrophe_AreNormalized()
    {
        var mixed = await Run("To", "RECORD", "Don\u2019t");
        var plain = await Run("to", "record", "don't");

        Assert.Equal(plain[1].Details, mixed[1].Details);
        Assert.Equal(plain[2].Details, mixed[2].Details);
        Assert.Equal("Don\u2019t", mixed[2].Word);
        Assert.Equal("don't", BasicClassifier.Normalize("Don\u2019t"));
    }

    /// <summary>
    ///     A token without a letter is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Punctuation_IsUnknown()
    {
        var results = await Run(".", "123");

        Assert.All(results, r => Assert.Null(r.Details));
        Assert.All(results, r => Assert.Equal(0.0, r.Confidence));
        Assert.Equal(string.Empty, BasicClassifier.Normalize("..."));
    }

    /// <summary>
    ///     An empty list gives an empty array.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(await Run());
    }

    /// <summary>
    ///     The results map onto the input by index.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_ResultsAreParallelToInput()
    {
        string[] words = ["They", "will", "record", "the", "record", "."];

        var results = await Run(words);

        Assert.Equal(words, results.Select(r => r.Word));
    }

    /// <summary>
    ///     A null list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NullWords_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new BasicClassifier().ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null element is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NullElement_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run("a", null!));
    }

    /// <summary>
    ///     A cancelled token is honored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Cancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BasicClassifier().ClassifyAsync(["a"], cancelled.Token));
    }
}
