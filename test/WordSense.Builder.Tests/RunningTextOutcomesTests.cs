using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the per-token outcomes, the comparison command and the latency measurement of the running-text evaluation.
/// </summary>
public sealed class RunningTextOutcomesTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    private static ClassifyResult Known(string word, PartOfSpeech pos)
    {
        var details = new WordDetails(word, pos, string.Empty);
        return new ClassifyResult(word, true, [new AnalysisCandidate(details, 1.0, [])], details, SenseLevel.PartOfSpeech, 0.9, null, null);
    }

    /// <summary>
    ///     A scored token is recorded with its set, sentence, document, gold and answer; punctuation and unanswered tokens are not.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_RunningText_Run_RecordsTheOutcomeOfEveryScoredToken()
    {
        // Arrange
        using var model = new FakeModel(words => [.. words.Select(w => w switch
        {
            "record" => Known(w, PartOfSpeech.Verb),
            "the" => Known(w, PartOfSpeech.Determiner),
            _ => ClassifyResult.Unknown(w),
        })]);
        var sentence = new TaggedSentence(["the", "record", "zzz", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Noun, PosTag.Punctuation]);
        List<(string Name, IReadOnlyList<TaggedSentence> Sentences)> sets = [("set", [sentence, sentence])];
        Dictionary<string, IReadOnlyList<string>> documents = new(StringComparer.Ordinal) { ["set"] = ["doc-a", "doc-b"] };
        List<TokenOutcome> outcomes = [];

        // Act
        await RunningTextEvaluator.RunAsync(model, sets, TestContext.Current.CancellationToken, outcomes, documents);
        List<TokenOutcome> withoutDocuments = [];
        await RunningTextEvaluator.RunAsync(model, sets, TestContext.Current.CancellationToken, withoutDocuments);

        // Assert
        Assert.Equal(4, outcomes.Count);
        Assert.Equal(new TokenOutcome("set", 0, "doc-a", 0, "the", "Determiner", "Determiner", true, false), outcomes[0]);
        Assert.Equal(new TokenOutcome("set", 1, "doc-b", 1, "record", "Noun", "Verb", false, true), outcomes[3]);
        Assert.Equal("s1", withoutDocuments[2].Document);
    }

    /// <summary>
    ///     The documents of the open sets are the source ids of their sentences, parallel to the sentences of the set.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RunningText_Documents_AreTheSourceIdsOfTheOpenSets()
    {
        var corpus = Path.Join(FindRepositoryRoot(), "corpus");

        var documents = RunningTextEvaluator.Documents(corpus, "test");
        var (sets, _) = RunningTextEvaluator.Load(corpus, "test", Path.Join(_dir.Root, "none.tsv"));

        Assert.Equal(sets[0].Sentences.Count, documents[RunningTextEvaluator.OpenSetName].Count);
        Assert.True(documents[RunningTextEvaluator.OpenSetName].Distinct(StringComparer.Ordinal).Count() < documents[RunningTextEvaluator.OpenSetName].Count);
        Assert.Equal(sets[1].Sentences.Count, documents[RunningTextEvaluator.ChallengeSetName].Count);
    }

    /// <summary>
    ///     The latency sample is the same for a seed and differs for another one, it is bounded by the number of sentences, and
    ///     an empty sample gives zeros.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_RunningText_MeasureLatency_UsesASeededSample()
    {
        // Arrange
        List<string> seen = [];
        using var model = new FakeModel(words =>
        {
            seen.Add(words[0]);
            return [.. words.Select(w => ClassifyResult.Unknown(w))];
        });
        TaggedSentence[] sentences = [.. Enumerable.Range(0, 60).Select(i => new TaggedSentence(["w" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)], [PosTag.Noun]))];

        // Act
        var first = await RunningTextEvaluator.MeasureLatencyAsync(model, sentences, 30, 5, TestContext.Current.CancellationToken);
        var firstSeen = seen.ToList();
        seen.Clear();
        await RunningTextEvaluator.MeasureLatencyAsync(model, sentences, 30, 5, TestContext.Current.CancellationToken);
        var repeated = seen.ToList();
        seen.Clear();
        await RunningTextEvaluator.MeasureLatencyAsync(model, sentences, 30, 6, TestContext.Current.CancellationToken);
        var other = seen.ToList();
        var all = await RunningTextEvaluator.MeasureLatencyAsync(model, sentences, 500, 5, TestContext.Current.CancellationToken);
        var none = await RunningTextEvaluator.MeasureLatencyAsync(model, [], 10, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(30, first.Sentences);
        Assert.Equal(60, all.Sentences);
        Assert.Equal((0.0, 0.0, 0.0, 0), none);
        Assert.Equal(firstSeen, repeated);
        Assert.NotEqual(firstSeen, other);
        Assert.True(first.Median >= 0 && first.P95 >= first.Median);
        Assert.Contains("| Fake | 30 | ", RunningTextEvaluator.RenderLatency([("Fake", first.Median, first.Mean, first.P95, first.Sentences)]), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command writes the outcomes and the latency, and the comparison of a file with itself shows no gain and a
    ///     degenerate interval; a file with two models needs the model to be named.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateRunningAndCompareOutcomes_Basic_WriteAndCompare()
    {
        // Arrange
        var root = FindRepositoryRoot();
        var outcomes = Path.Join(_dir.Root, "basic.tsv");
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var run = await Program.RunAsync(["evaluate-running", "--model", "basic", "--repository", root, "--gold", Path.Join(_dir.Root, "absent.tsv"), "--report", "running", "--outcomes", outcomes, "--latency", "--latency-sentences", "40"], output, error, TestContext.Current.CancellationToken);
        using var compareOutput = new StringWriter();
        var compare = await Program.RunAsync(["compare-outcomes", "--base", outcomes, "--new", outcomes, "--reps", "300", "--cluster", "document"], compareOutput, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, run);
        Assert.Equal(0, compare);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("### Latency on running text", output.ToString(), StringComparison.Ordinal);
        var read = OutcomeFile.Read(outcomes);
        Assert.All(read, o => Assert.Equal("basic", o.Model));
        Assert.Contains(read, o => o.Outcome.Set == RunningTextEvaluator.OpenSetName && o.Outcome.Ambiguous);
        Assert.Contains("| open corpus test (representative) | all tokens |", compareOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("| 0.00 | 0.00 to 0.00 | 1.0e+00 |", compareOutput.ToString(), StringComparison.Ordinal);

        // A file that has two models cannot be compared without a name
        var two = _dir.Write("two.tsv", OutcomeFile.Render([.. read.Take(3), .. read.Take(3).Select(o => ("minilm", o.Outcome))]));
        using var failure = new StringWriter();
        var ambiguous = await Program.RunAsync(["compare-outcomes", "--base", two, "--new", two], output, failure, TestContext.Current.CancellationToken);
        var named = await Program.RunAsync(["compare-outcomes", "--base", two, "--new", two, "--base-model", "basic", "--new-model", "minilm", "--reps", "10"], output, failure, TestContext.Current.CancellationToken);
        Assert.Equal(1, ambiguous);
        Assert.Equal(0, named);
        Assert.Contains("--base-model", failure.ToString(), StringComparison.Ordinal);
    }
}
