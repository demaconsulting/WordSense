using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the part-of-speech evaluation of the basic classifier.
/// </summary>
public sealed class TaggerEvaluatorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Tokens are scored when the classifier answers them and the gold tag is a word class; punctuation and other
    ///     gold tags are excluded, and tokens the classifier does not answer are counted apart.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TaggerEvaluator_EvaluateAsync_ScoresAnsweredWordTokens()
    {
        // Arrange: two sentences with an unanswered number, an unknown word, punctuation and a tag that is not scored
        TaggedSentence[] sentences =
        [
            new(["They", "walk", "5", "zzyzx", "."], [PosTag.Pronoun, PosTag.Verb, PosTag.Noun, PosTag.Noun, PosTag.Punctuation]),
            new(["The", "walk", "-"], [PosTag.Determiner, PosTag.Noun, PosTag.Other]),
        ];

        // Act: evaluate the sentences with the shipped table
        var score = await TaggerEvaluator.EvaluateAsync("toy", BasicTables.Default, sentences, TestContext.Current.CancellationToken);

        // Assert: the counts of the scored, unanswered, excluded, ambiguous and noun-verb tokens
        Assert.Equal("toy", score.Name);
        Assert.Equal(2, score.Sentences);
        Assert.Equal(4, score.Scored);
        Assert.Equal(2, score.Unanswered);
        Assert.Equal(2, score.Excluded);
        Assert.Equal(2, score.Ambiguous);
        Assert.InRange(score.Correct, 3, 4);
        Assert.InRange(score.AmbiguousCorrect, 1, 2);
        Assert.Equal(2, score.NounVerb);
        Assert.InRange(score.NounVerbCorrect, 1, 2);
        Assert.True(score.NounVerbCorrect <= score.AmbiguousCorrect);
    }

    private static readonly TaggedSentence[] MixedAndImperativeSentences =
    [
        new(["Have", "a", "book", "."], [PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]),
        new(["so", "we", "go"], [PosTag.Conjunction, PosTag.Pronoun, PosTag.Verb]),
        new(["We", "go", "the", "way"], [PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun]),
    ];

    /// <summary>
    ///     The words that have both a closed-class and an open-class analysis are counted apart.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TaggerEvaluator_EvaluateAsync_CountsClosedAndOpenWords()
    {
        // Arrange: sentences with words that are both closed-class and open-class (so, have, the way)

        // Act: evaluate the sentences with the shipped table
        var score = await TaggerEvaluator.EvaluateAsync("toy", BasicTables.Default, MixedAndImperativeSentences, TestContext.Current.CancellationToken);

        // Assert: the mixed words are a part of the scored tokens, and the correct ones a part of them
        Assert.True(score.Mixed >= 2);
        Assert.True(score.MixedCorrect <= score.Mixed);
        Assert.True(score.Mixed < score.Scored);
    }

    /// <summary>
    ///     The verbs that start a sentence before a determiner are counted apart.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TaggerEvaluator_EvaluateAsync_CountsStartingVerbs()
    {
        // Arrange: one sentence that starts with a verb before a determiner, among the shared sentences

        // Act: evaluate the sentences with the shipped table
        var score = await TaggerEvaluator.EvaluateAsync("toy", BasicTables.Default, MixedAndImperativeSentences, TestContext.Current.CancellationToken);

        // Assert: one starting verb is counted, and the correct ones are a part of it
        Assert.Equal(1, score.Imperative);
        Assert.InRange(score.ImperativeCorrect, 0, 1);
    }

    /// <summary>
    ///     A gold file groups tokens by sentence id and reads the tag letters.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerEvaluator_ReadGold_GroupsTokensBySentence()
    {
        var path = _dir.Write("gold.tsv", "# comment\n1\tThey\tR\n1\twalk\tV\n\n2\tGo\tV\n2\t.\tU\n");

        var sentences = TaggerEvaluator.ReadGold(path);

        Assert.Equal(2, sentences.Count);
        Assert.Equal(["They", "walk"], sentences[0].Words);
        Assert.Equal([PosTag.Pronoun, PosTag.Verb], sentences[0].Tags);
        Assert.Equal([PosTag.Verb, PosTag.Punctuation], sentences[1].Tags);
    }

    /// <summary>
    ///     A gold file with the lemma, the treebank tag and the features gives them to the tokens (an underscore is empty);
    ///     a file with three columns gives none, and a file that mixes the two is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerEvaluator_ReadGold_SixColumns_CarriesLemmaTagAndFeatures()
    {
        // Arrange
        var six = _dir.Write("six.tsv", "1\tThey\tR\tthey\tPRP\t_\n1\tput\tV\tput\tVBD\tMood=Ind|Tense=Past|VerbForm=Fin\n");
        var three = _dir.Write("three.tsv", "1\tThey\tR\n");
        var mixed = _dir.Write("mixed.tsv", "1\tThey\tR\n1\tput\tV\tput\tVBD\t_\n");

        // Act
        var withGold = TaggerEvaluator.ReadGold(six);
        var without = TaggerEvaluator.ReadGold(three);

        // Assert
        var gold = Assert.IsType<GoldToken[]>(Assert.Single(withGold).Gold);
        Assert.Equal(new GoldToken("they", "PRP", string.Empty), gold[0]);
        Assert.Equal(new GoldToken("put", "VBD", "Mood=Ind|Tense=Past|VerbForm=Fin"), gold[1]);
        Assert.Null(Assert.Single(without).Gold);
        Assert.Throws<InvalidDataException>(() => TaggerEvaluator.ReadGold(mixed));
    }

    /// <summary>
    ///     A gold line that is not three columns with a tag letter is rejected.
    /// </summary>
    /// <param name="line">The malformed line.</param>
    [Theory]
    [InlineData("1\tThey")]
    [InlineData("1\tThey\tQ")]
    [InlineData("1\tThey\tNN")]
    [InlineData("1\tThey\tS")]
    public void WordSenseBuilder_TaggerEvaluator_ReadGold_MalformedLine_Throws(string line)
    {
        var path = _dir.Write("bad.tsv", line + "\n");

        Assert.Throws<InvalidDataException>(() => TaggerEvaluator.ReadGold(path));
    }

    /// <summary>
    ///     The table has a row per set with the accuracy, and n/a for an empty set.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerEvaluator_Render_FormatsRows()
    {
        var text = TaggerEvaluator.Render([new TaggerScore("demo", 10, 200, 190, 100, 90, 3, 20), new TaggerScore("empty", 0, 0, 0, 0, 0, 0, 0)]);

        Assert.StartsWith("| Set | Sentences | Scored tokens | Accuracy |", text, StringComparison.Ordinal);
        Assert.Contains("| demo | 10 | 200 | 95.00% | 100 | 90.00% | 3 | 20 |\n", text, StringComparison.Ordinal);
        Assert.Contains("| empty | 0 | 0 | n/a | 0 | n/a | 0 | 0 |\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }
}
