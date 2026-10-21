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
        TaggedSentence[] sentences =
        [
            new(["They", "walk", "5", "zzyzx", "."], [PosTag.Pronoun, PosTag.Verb, PosTag.Noun, PosTag.Noun, PosTag.Punctuation]),
            new(["The", "walk", "-"], [PosTag.Determiner, PosTag.Noun, PosTag.Other]),
        ];

        var score = await TaggerEvaluator.EvaluateAsync("toy", BasicTables.Default, sentences, TestContext.Current.CancellationToken);

        Assert.Equal("toy", score.Name);
        Assert.Equal(2, score.Sentences);
        Assert.Equal(4, score.Scored);
        Assert.Equal(2, score.Unanswered);
        Assert.Equal(2, score.Excluded);
        Assert.Equal(2, score.Ambiguous);
        Assert.InRange(score.Correct, 3, 4);
        Assert.InRange(score.AmbiguousCorrect, 1, 2);
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
