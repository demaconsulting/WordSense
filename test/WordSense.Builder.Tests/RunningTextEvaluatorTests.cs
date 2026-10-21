using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the running-text evaluation of the models: the sets, the scoring of any model and the table cells.
/// </summary>
public sealed class RunningTextEvaluatorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    /// <summary>
    ///     The default gold file is the converted test file of the treebank, never the development file that settings are selected with.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RunningText_DefaultGold_IsTheTestFileAndNotTheDevelopmentFile()
    {
        // Act
        var path = RunningTextEvaluator.DefaultGold("repo");

        // Assert
        Assert.Equal("ud_test.tsv", Path.GetFileName(path));
        Assert.DoesNotContain("dev", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.Contains("tagger-eval", path, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The sets are the representative test split, the gold file of the treebank test file and the challenge set; a missing
    ///     gold file leaves its set out with a note that says how to make it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RunningText_Load_ReadsTheSetsAndNotesAMissingGoldFile()
    {
        // Arrange
        var corpus = Path.Join(FindRepositoryRoot().FullName, "corpus");
        var gold = _dir.Write("gold/ud_test.tsv", "# test\n1\tThe\tD\n1\tcat\tN\n1\t.\tU\n2\tIt\tR\n2\tsleeps\tV\n");

        // Act
        var (sets, notes) = RunningTextEvaluator.Load(corpus, "test", gold);
        var (without, missing) = RunningTextEvaluator.Load(corpus, "test", Path.Join(_dir.Root, "none.tsv"));

        // Assert
        Assert.Equal([RunningTextEvaluator.OpenSetName, RunningTextEvaluator.TreebankSetName, RunningTextEvaluator.ChallengeSetName], sets.Select(s => s.Name));
        Assert.Empty(notes);
        Assert.Equal(2, sets[1].Sentences.Count);
        Assert.True(sets[0].Sentences.Count > 1000);
        Assert.Equal([RunningTextEvaluator.OpenSetName, RunningTextEvaluator.ChallengeSetName], without.Select(s => s.Name));
        Assert.Contains("prepare_tagger_eval.py", Assert.Single(missing), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Any model is scored through its public interface on every set: the accuracy on all scored tokens and on the ambiguous
    ///     ones, the tokens that are not answered and the tokens that the convention excludes, and the table cells say both accuracies.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_RunningText_Run_ReportsAllAndAmbiguousTokensForAModel()
    {
        // Arrange: "record" is ambiguous (noun and verb) and is answered as a verb; "the" is unambiguous; "zzz" is not answered
        using var model = new FakeModel(words => [.. words.Select(w => w switch
        {
            "record" => Known(w, "record", PartOfSpeech.Verb),
            "the" => Known(w, "the", PartOfSpeech.Determiner),
            _ => ClassifyResult.Unknown(w),
        })]);
        var sentence = new TaggedSentence(["the", "record", "zzz", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Noun, PosTag.Punctuation]);
        var sets = new List<(string Name, IReadOnlyList<TaggedSentence> Sentences)> { (RunningTextEvaluator.OpenSetName, [sentence]) };

        // Act
        var scores = await RunningTextEvaluator.RunAsync(model, sets, TestContext.Current.CancellationToken);
        var cells = RunningTextEvaluator.TableCells(scores);
        var text = RunningTextEvaluator.Render([("Fake", scores)], ["note of a left-out set"]);

        // Assert
        var score = Assert.Single(scores);
        Assert.Equal((1, 2, 1, 1, 0, 1, 1), (score.Sentences, score.Scored, score.Correct, score.Ambiguous, score.AmbiguousCorrect, score.Unanswered, score.Excluded));
        Assert.Equal("50.00% (ambiguous 0.00%)", cells[ModelTable.RunningColumn]);
        Assert.False(cells.ContainsKey(ModelTable.TreebankColumn));
        Assert.Contains("| Fake | open corpus test (representative) | 1 | 2 | 50.00% | 1 | 0.00% | 1 | 1 |", text, StringComparison.Ordinal);
        Assert.Contains("note of a left-out set", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command measures the basic model on the representative test split and the challenge set, reports the left-out
    ///     treebank set with a note, and writes the cells of the model table that it measured.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateRunning_Basic_PrintsTableAndWritesTheCells()
    {
        // Arrange
        var root = FindRepositoryRoot().FullName;
        var table = Path.Join(_dir.Root, "model-table.md");
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["evaluate-running", "--model", "basic", "--repository", root, "--gold", Path.Join(_dir.Root, "absent.tsv"), "--table", table], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("| Basic | open corpus test (representative) |", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("| Basic | STE challenge set (targeted test) |", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("not measured", output.ToString(), StringComparison.Ordinal);
        var row = ModelTable.Read(table)["Basic"];
        Assert.Matches(@"^\d+\.\d\d% \(ambiguous \d+\.\d\d%\)$", row[ModelTable.RunningColumn]);
        Assert.Matches(@"^\d+\.\d\d% \(ambiguous \d+\.\d\d%\)$", row[ModelTable.ChallengeColumn]);
        Assert.Equal(ModelTable.NotMeasured, row[ModelTable.TreebankColumn]);
    }

    private static ClassifyResult Known(string word, string lemma, PartOfSpeech pos)
    {
        var details = new WordDetails(lemma, pos, string.Empty);
        return new ClassifyResult(word, true, [new AnalysisCandidate(details, 1.0, ImmutableArray<SenseCandidate>.Empty)], details, SenseLevel.PartOfSpeech, 0.9, null, null);
    }
}
