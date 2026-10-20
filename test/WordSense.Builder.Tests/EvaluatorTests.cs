using System.Collections.Immutable;
using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the evaluator: the verdict on one result and the measurement of a model on a small holdout corpus.
/// </summary>
public sealed class EvaluatorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     A model that answers every sentence from a script keyed by the number word of the sentence.
    /// </summary>
    private sealed class ScriptedModel : IWordSenseModel
    {
        private readonly Dictionary<string, ClassifyResult> _script;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScriptedModel"/> class.
        /// </summary>
        /// <param name="script">The result of the target word by the number of the sentence.</param>
        public ScriptedModel(Dictionary<string, ClassifyResult> script)
        {
            _script = script;
        }

        /// <inheritdoc />
        public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
        {
            var list = words.ToList();
            return Task.FromResult(list.Select(w => w == "test" ? _script[list[2]] : ClassifyResult.Unknown(w)).ToArray());
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    private static AnalysisCandidate Analysis(PartOfSpeech pos, double probability, params (string Id, double P)[] senses) =>
        new(new WordDetails("test", pos, string.Empty), probability, [.. senses.Select(s => new SenseCandidate(s.Id, s.P))]);

    private static ClassifyResult Result(SenseLevel level, ImmutableArray<AnalysisCandidate> analyses, PartOfSpeech? pos, string? groupId = null, string? senseId = null) =>
        new("test", true, analyses, pos is null ? null : new WordDetails("test", pos.Value, string.Empty), level, 0.8, groupId, senseId);

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    /// <summary>
    ///     The result of the target word is judged at the level it was answered: the sense, the group of the sense of the
    ///     row (never for a sense without a group) or the part of speech; the most probable sense is the largest product
    ///     of the probability of its analysis and its own; a result without ranked senses counts as wrong.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Evaluator_Judge_JudgesEachLevelAtItsOwnLevel()
    {
        // Arrange
        var (a1, a2, b, none) = SenseFixtures.Grouped("test");
        var row = SenseFixtures.Row(a1, 1);
        var noun = Analysis(PartOfSpeech.Noun, 0.7, (a1.SenseId, 0.4), (a2.SenseId, 0.6));
        var verb = Analysis(PartOfSpeech.Verb, 0.3, (b.SenseId, 1.0));

        // Act
        var sense = Evaluator.Judge(row, Result(SenseLevel.Sense, [noun, verb], PartOfSpeech.Noun, a1.GroupId, a1.SenseId));
        var wrongSense = Evaluator.Judge(row, Result(SenseLevel.Sense, [noun, verb], PartOfSpeech.Noun, a2.GroupId, a2.SenseId));
        var group = Evaluator.Judge(row, Result(SenseLevel.Group, [noun, verb], PartOfSpeech.Noun, a1.GroupId));
        var ungrouped = Evaluator.Judge(SenseFixtures.Row(none, 2), Result(SenseLevel.Group, [noun, verb], PartOfSpeech.Noun, a1.GroupId));
        var pos = Evaluator.Judge(row, Result(SenseLevel.PartOfSpeech, [noun, verb], PartOfSpeech.Noun));
        var wrongPos = Evaluator.Judge(row, Result(SenseLevel.PartOfSpeech, [Analysis(PartOfSpeech.Verb, 1.0)], PartOfSpeech.Verb));
        var unknown = Evaluator.Judge(row, ClassifyResult.Unknown("test"));

        // Assert
        Assert.True(sense.CorrectAtLevel);
        Assert.False(wrongSense.CorrectAtLevel);
        Assert.True(group.CorrectAtLevel);
        Assert.False(ungrouped.CorrectAtLevel);
        Assert.True(pos.CorrectAtLevel);
        Assert.False(wrongPos.CorrectAtLevel);
        Assert.False(unknown.CorrectAtLevel);
        Assert.Equal(SenseLevel.None, unknown.Level);

        // The best sense is a2 (0.7 x 0.6 = 0.42 beats a1 with 0.28 and b with 0.3), so the row of a1 is wrong at the top
        Assert.True(sense.Ranked);
        Assert.False(sense.FineTop1);
        Assert.True(Evaluator.Judge(SenseFixtures.Row(a2, 3), Result(SenseLevel.Sense, [noun, verb], PartOfSpeech.Noun, a2.GroupId, a2.SenseId)).FineTop1);
        Assert.False(wrongPos.Ranked);
        Assert.False(wrongPos.FineTop1);
        Assert.True(pos.PosCorrect);
        Assert.False(wrongPos.PosCorrect);
        Assert.True(pos.AnalysisCorrect);
        Assert.False(unknown.PosCorrect);
    }

    /// <summary>
    ///     A scripted model is measured on a small holdout corpus: the share answered at each level, the precision at
    ///     each level, the top-1 fine accuracy over all rows and over the ranked rows, the part-of-speech accuracy and
    ///     the share that is right at its own level.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_ReportsShareAndAccuracyPerLevel()
    {
        // Arrange: seven rows, each scripted to land on one case
        var (a1, a2, b, none) = SenseFixtures.Grouped("test");
        (SenseInfo Sense, ClassifyResult Result)[] cases =
        [
            (a1, Result(SenseLevel.Sense, [Analysis(PartOfSpeech.Noun, 1.0, (a1.SenseId, 0.8), (a2.SenseId, 0.2))], PartOfSpeech.Noun, a1.GroupId, a1.SenseId)),
            (b, Result(SenseLevel.Sense, [Analysis(PartOfSpeech.Noun, 1.0, (a1.SenseId, 0.6), (b.SenseId, 0.4))], PartOfSpeech.Noun, a1.GroupId, a1.SenseId)),
            (a1, Result(SenseLevel.Group, [Analysis(PartOfSpeech.Noun, 1.0, (a2.SenseId, 0.55), (a1.SenseId, 0.45))], PartOfSpeech.Noun, a1.GroupId)),
            (none, Result(SenseLevel.Group, [Analysis(PartOfSpeech.Noun, 1.0, (none.SenseId, 0.9), (a1.SenseId, 0.1))], PartOfSpeech.Noun, a1.GroupId)),
            (a2, Result(SenseLevel.PartOfSpeech, [Analysis(PartOfSpeech.Noun, 0.7, (a2.SenseId, 0.9), (a1.SenseId, 0.1)), Analysis(PartOfSpeech.Verb, 0.3)], PartOfSpeech.Noun)),
            (a1, Result(SenseLevel.PartOfSpeech, [Analysis(PartOfSpeech.Verb, 1.0)], PartOfSpeech.Verb)),
            (b, ClassifyResult.Unknown("test")),
        ];
        var holdout = string.Concat(cases.Select((c, i) => $"test\t{c.Sense.SenseId}\t{c.Sense.PartOfSpeech}\tA {{test}} {i} here.\n"));
        _dir.Write("corpus/holdout/h.tsv", holdout);
        var script = cases.Select((c, i) => (Key: i.ToString(CultureInfo.InvariantCulture), c.Result)).ToDictionary(p => p.Key, p => p.Result);
        var info = new ModelInfo("fake", "pkg", null, _ => new ScriptedModel(script));
        var outcomes = Path.Join(_dir.Root, "outcomes.tsv");
        using var progress = new StringWriter();

        // Act
        var result = await Evaluator.RunAsync(_dir.Root, Path.Join(_dir.Root, "corpus"), info, progress, TestContext.Current.CancellationToken, outcomes);

        // Assert
        var set = Assert.Single(result.Sets);
        Assert.Equal(7, set.Rows);
        Assert.Equal(new LevelCount(2, 1), set.Sense);
        Assert.Equal(new LevelCount(2, 1), set.Group);
        Assert.Equal(new LevelCount(2, 1), set.PartOfSpeech);
        Assert.Equal(1, set.None);
        Assert.Equal(3, set.CorrectAtLevel);
        Assert.Equal(5, set.Ranked);
        Assert.Equal(3, set.FineTop1);
        Assert.Equal(3, set.FineTop1OfRanked);
        Assert.Equal(5, set.PosCorrect);
        Assert.Equal(5, set.AnalysisCorrect);
        var text = result.Render();
        Assert.Contains("| holdout | 7 | 28.57% | 28.57% | 28.57% | 14.29% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 50.00% | 50.00% | 50.00% | 42.86% | 42.86% | 60.00% | 71.43% | 71.43% |\n", text, StringComparison.Ordinal);
        var lines = await File.ReadAllLinesAsync(outcomes, TestContext.Current.CancellationToken);
        Assert.Equal(7, lines.Length);
        Assert.StartsWith($"holdout\t0\t{a1.SenseId}\tSense:{a1.SenseId}\t0.800000\t1", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("\tNone\t0.000000\t0", lines[6], StringComparison.Ordinal);
    }

    /// <summary>
    ///     The evaluator reads only the holdout: a missing holdout folder gives an empty set, and a row that disagrees with the catalog is an error.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_ReadsOnlyTheHoldout()
    {
        // Arrange
        var info = new ModelInfo("fake", "pkg", null, _ => new ScriptedModel([]));
        using var progress = new StringWriter();

        // Act
        var empty = await Evaluator.RunAsync(_dir.Root, Path.Join(_dir.Root, "corpus"), info, progress, TestContext.Current.CancellationToken);
        _dir.Write("corpus/holdout/h.tsv", "walk\twalk#no-such-sense\tNoun\tA {walk} here.\n");

        // Assert
        Assert.Equal(0, Assert.Single(empty.Sets).Rows);
        await Assert.ThrowsAsync<InvalidDataException>(() => Evaluator.RunAsync(_dir.Root, Path.Join(_dir.Root, "corpus"), info, progress, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The committed holdout can be measured with the Basic model: every row is answered at the part-of-speech level
    ///     or as a single-sense lemma, never at the group level, and the figures are consistent.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_BasicModel_CommittedHoldout_IsMeasuredWithoutGroups()
    {
        var root = FindRepositoryRoot();
        using var progress = new StringWriter();

        var result = await Evaluator.RunAsync(root.FullName, Path.Join(root.FullName, "corpus"), ModelCatalog.Find("basic"), progress, TestContext.Current.CancellationToken);

        var set = result.Sets[0];
        Assert.Equal("holdout", set.Name);
        Assert.True(set.Rows > 900);
        Assert.Equal(0, set.Group.Answered);
        Assert.Equal(set.Rows, set.Sense.Answered + set.PartOfSpeech.Answered + set.None);
        Assert.True(set.PosCorrect <= set.Rows);
        Assert.True(set.PartOfSpeech.Correct <= set.PartOfSpeech.Answered);
    }

    /// <summary>
    ///     The held-out rows are also reported by origin when the provenance names it: targeted real text, earlier
    ///     harvests and written sentences are separate sets, and the report says that the figures are not representative.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_ProvenanceNamesTheOrigin_ReportsSubsetsSeparately()
    {
        // Arrange: two rows, one targeted real sentence and one written sentence
        var (a1, _, b, _) = SenseFixtures.Grouped("test");
        var good = Result(SenseLevel.Sense, [Analysis(PartOfSpeech.Noun, 1.0, (a1.SenseId, 1.0))], PartOfSpeech.Noun, a1.GroupId, a1.SenseId);
        var script = new Dictionary<string, ClassifyResult> { ["0"] = good, ["1"] = good };
        var realSentence = "A {test} 0 here.";
        var writtenSentence = "A {test} 1 here.";
        _dir.Write("corpus/holdout/h.tsv", $"test\t{a1.SenseId}\t{a1.PartOfSpeech}\t{realSentence}\ntest\t{b.SenseId}\t{b.PartOfSpeech}\t{writtenSentence}\n");
        static string Id(string sentence) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("test|" + sentence)))[..12].ToLowerInvariant();
        var header = string.Join('\t', "id", "lemma", "sense", "split", "source", "licence", "source_key", "url", "contributor", "origin", "selection");
        var realRow = string.Join('\t', Id(realSentence), "test", a1.SenseId, "holdout", "gutenberg", "Public domain", "k", "-", "c", "real", "targeted");
        var writtenRow = string.Join('\t', Id(writtenSentence), "test", b.SenseId, "holdout", "llm-corpus", "MIT (project-authored)", "k", "-", "c", "llm-corpus", "targeted");
        _dir.Write("corpus/provenance/p.tsv", header + "\n" + realRow + "\n" + writtenRow + "\n");
        var info = new ModelInfo("fake", "pkg", null, _ => new ScriptedModel(script));
        using var progress = new StringWriter();

        // Act
        var result = await Evaluator.RunAsync(_dir.Root, Path.Join(_dir.Root, "corpus"), info, progress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["holdout", "holdout: real text, targeted (found for a sense)", "holdout: LLM-written"], result.Sets.Select(s => s.Name));
        Assert.Equal([2, 1, 1], result.Sets.Select(s => s.Rows));
        Assert.Equal([1, 1, 0], result.Sets.Select(s => s.Sense.Correct));
        Assert.Contains("not the accuracy on representative text", result.Render(), StringComparison.Ordinal);
    }
}
