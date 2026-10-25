using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the tense evaluation: the gold class, the groups of same-spelling verbs, the scoring of a model, the pooling
///     of the treebank files, the sets, the report and the command.
/// </summary>
public sealed class TenseEvaluatorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string FindCorpus()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return Path.Join(root.FullName, "corpus");
    }

    private static ClassifyResult Known(string word, string lemma, PartOfSpeech pos, string features)
    {
        var details = new WordDetails(lemma, pos, features);
        return new ClassifyResult(word, true, [new AnalysisCandidate(details, 1.0, ImmutableArray<SenseCandidate>.Empty)], details, SenseLevel.PartOfSpeech, 0.9, null, null);
    }

    // A model that reads every put as a past tense and does not answer cut
    private static FakeModel PastPutModel() => new(words => [.. words.Select(w => BasicClassifier.Lower(w) switch
    {
        "put" => Known(w, "put", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part"),
        "cut" => ClassifyResult.Unknown(w),
        _ => Known(w, w, PartOfSpeech.Noun, "Number=Sing"),
    })]);

    private static TaggedSentence Sentence(string[] words, int[] tags, params GoldToken[] gold) => new(words, tags, gold);

    private static readonly GoldToken Other = new("x", "NN", "Number=Sing");

    /// <summary>
    ///     The gold class is past for the treebank tags VBD and VBN, non-past for the others, and without a treebank tag the
    ///     feature <c>Tense=Past</c> decides.
    /// </summary>
    /// <param name="xpos">The treebank tag.</param>
    /// <param name="features">The features.</param>
    /// <param name="expected">The expected class.</param>
    [Theory]
    [InlineData("VBD", "Mood=Ind|Tense=Past|VerbForm=Fin", true)]
    [InlineData("VBN", "Tense=Past|VerbForm=Part", true)]
    [InlineData("VB", "VerbForm=Inf", false)]
    [InlineData("VBP", "Mood=Ind|Tense=Pres|VerbForm=Fin", false)]
    [InlineData("VBZ", "Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin", false)]
    [InlineData("", "Mood=Ind|Tense=Past|VerbForm=Fin", true)]
    [InlineData("", "Tense=Past|VerbForm=Part", true)]
    [InlineData("", "Tense=Pres|VerbForm=Part", false)]
    [InlineData("", "VerbForm=Inf", false)]
    [InlineData("", "", false)]
    public void WordSenseBuilder_TenseEvaluator_IsGoldPast_UsesTheTreebankTagThenTheFeatures(string xpos, string features, bool expected)
    {
        // Arrange: the gold token of the theory row
        var token = new GoldToken("x", xpos, features);

        // Act
        var actual = TenseEvaluator.IsGoldPast(token);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     A form is scored when it has a past and a non-past verb analysis; the group says whether they share a lemma and
    ///     whether the form is of <c>read</c> and its compounds (and not of <c>spread</c>, which only ends alike).
    /// </summary>
    /// <param name="word">The token.</param>
    /// <param name="groups">The groups, joined by a bar.</param>
    [Theory]
    [InlineData("put", "all same-spelling verbs|same lemma")]
    [InlineData("Put", "all same-spelling verbs|same lemma")]
    [InlineData("come", "all same-spelling verbs|same lemma")]
    [InlineData("read", "all same-spelling verbs|same lemma|read family")]
    [InlineData("reread", "all same-spelling verbs|same lemma|read family")]
    [InlineData("spread", "all same-spelling verbs|same lemma")]
    [InlineData("found", "all same-spelling verbs")]
    [InlineData("walk", "")]
    [InlineData("walked", "")]
    [InlineData("zzyzx", "")]
    [InlineData(".", "")]
    public void WordSenseBuilder_TenseEvaluator_GroupsOf_ClassifiesTheSurfaceForm(string word, string groups)
    {
        // Arrange: the token of the theory row, looked up in the embedded lexicon

        // Act
        var actual = string.Join('|', TenseEvaluator.GroupsOf(word));

        // Assert
        Assert.Equal(groups, actual);
    }

    /// <summary>
    ///     A model is scored on the gold verbs whose form has a past and a non-past analysis: the four cells of the
    ///     confusion, the tokens it does not answer, and the groups; tokens that are no gold verb are left out.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TenseEvaluator_RunAsync_ScoresTheConfusionOfPastAgainstNonPast()
    {
        // Arrange: put as a past verb (right), put as an imperative (wrong), cut that is not answered, put as a gold noun (not scored)
        // and walk (not a same-spelling verb)
        var sets = new List<(string Name, IReadOnlyList<TaggedSentence> Sentences)>
        {
            (
                "toy",
                [
                    Sentence(["They", "put", "it"], [PosTag.Pronoun, PosTag.Verb, PosTag.Pronoun], Other, new("put", "VBD", "Mood=Ind|Tense=Past|VerbForm=Fin"), Other),
                    Sentence(["Put", "it", "down"], [PosTag.Verb, PosTag.Pronoun, PosTag.Adverb], new("put", "VB", "VerbForm=Inf"), Other, Other),
                    Sentence(["Cut", "it"], [PosTag.Verb, PosTag.Pronoun], new("cut", "VB", "VerbForm=Inf"), Other),
                    Sentence(["A", "put", "walk"], [PosTag.Determiner, PosTag.Noun, PosTag.Verb], Other, new("put", "NN", "Number=Sing"), new("walk", "VB", "VerbForm=Inf")),
                ]),
        };
        using var model = PastPutModel();

        // Act
        var scores = await TenseEvaluator.RunAsync(model, sets, TestContext.Current.CancellationToken);

        // Assert: three scored tokens, none of them of the read family
        Assert.Equal([TenseEvaluator.AllGroup, TenseEvaluator.SameLemmaGroup], scores.Select(s => s.Group));
        var all = scores[0];
        Assert.Equal((1, 0, 1, 0, 1), (all.PastAsPast, all.PastAsNonPast, all.NonPastAsPast, all.NonPastAsNonPast, all.Unanswered));
        Assert.Equal(3, all.Tokens);
        Assert.Equal(1, all.Correct);
        Assert.Equal(2, all.Majority);
        Assert.Equal(1, all.GoldPast);
    }

    /// <summary>
    ///     A set whose gold has no features is skipped, and the three treebank files are pooled into one row per group.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TenseEvaluator_RunAsync_PoolsTheTreebankFilesAndSkipsGoldWithoutFeatures()
    {
        // Arrange
        TaggedSentence[] one = [Sentence(["They", "put", "it"], [PosTag.Pronoun, PosTag.Verb, PosTag.Pronoun], Other, new("put", "VBD", "Tense=Past|VerbForm=Fin"), Other)];
        TaggedSentence[] none = [new(["They", "put", "it"], [PosTag.Pronoun, PosTag.Verb, PosTag.Pronoun])];
        var sets = new List<(string Name, IReadOnlyList<TaggedSentence> Sentences)>
        {
            (TenseEvaluator.TreebankTestName, one),
            (TenseEvaluator.TreebankDevName, one),
            (TenseEvaluator.TreebankTrainName, one),
            ("without features", none),
        };
        using var model = PastPutModel();

        // Act
        var scores = await TenseEvaluator.RunAsync(model, sets, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(scores, s => s.Name == "without features");
        var pooled = scores.Where(s => s.Name == TenseEvaluator.TreebankPooledName).ToList();
        Assert.Equal([TenseEvaluator.AllGroup, TenseEvaluator.SameLemmaGroup], pooled.Select(s => s.Group));
        Assert.All(pooled, s => Assert.Equal((3, 3), (s.Tokens, s.Correct)));
        Assert.Equal(TenseEvaluator.TreebankPooledName, pooled[0].Name);
    }

    /// <summary>
    ///     The sets are the open corpus test and development splits and the treebank files next to the gold file; a file that
    ///     does not exist, or has no lemma, tag and feature columns, is left out with a note.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TenseEvaluator_Load_ReadsTheSetsAndNotesWhatIsMissing()
    {
        // Arrange: a test file with the six columns, a development file with three, and no training file
        var gold = _dir.Write("gold/ud_test.tsv", "1\tThey\tR\tthey\tPRP\t_\n1\tput\tV\tput\tVBD\tTense=Past|VerbForm=Fin\n");
        _dir.Write("gold/ud_dev.tsv", "1\tThey\tR\n1\tput\tV\n");

        // Act
        var (sets, notes) = TenseEvaluator.Load(FindCorpus(), gold);

        // Assert
        Assert.Equal([TenseEvaluator.OpenTestName, TenseEvaluator.OpenDevName, TenseEvaluator.TreebankTestName], sets.Select(s => s.Name));
        Assert.True(sets[0].Sentences.Count > 1000);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.StartsWith(TenseEvaluator.TreebankDevName, StringComparison.Ordinal) && n.Contains("no lemma, tag and feature columns", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.StartsWith(TenseEvaluator.TreebankTrainName, StringComparison.Ordinal) && n.Contains("prepare_tagger_eval.py", StringComparison.Ordinal));
        Assert.NotNull(sets[0].Sentences[0].Gold);
    }

    /// <summary>
    ///     The treebank training file next to the gold file is part of the sets, so that the three files can be pooled: the
    ///     pooled row of a run counts the tokens of the test, development and training files.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_TenseEvaluator_Load_IncludesTheAdjacentTreebankTrainingFileForPooling()
    {
        // Arrange: three treebank files with the six columns; the test and development files hold one "put" each, the training file two
        const string one = "s1\tThey\tR\tthey\tPRP\t_\ns1\tput\tV\tput\tVBD\tTense=Past|VerbForm=Fin\ns1\tit\tR\tit\tPRP\t_\n";
        var gold = _dir.Write("gold/ud_test.tsv", one);
        _dir.Write("gold/ud_dev.tsv", one);
        _dir.Write("gold/ud_train.tsv", one + one.Replace("s1", "s2", StringComparison.Ordinal));
        using var model = PastPutModel();

        // Act
        var (sets, notes) = TenseEvaluator.Load(FindCorpus(), gold);
        var treebank = sets.Where(s => s.Name.StartsWith("UD-EWT", StringComparison.Ordinal)).ToList();
        var scores = await TenseEvaluator.RunAsync(model, treebank, TestContext.Current.CancellationToken);

        // Assert: the training file is a set of its own and the pooled row has the tokens of all three files
        Assert.Empty(notes);
        Assert.Equal([TenseEvaluator.TreebankTestName, TenseEvaluator.TreebankDevName, TenseEvaluator.TreebankTrainName], treebank.Select(s => s.Name));
        Assert.Equal(2, treebank[2].Sentences.Count);
        var pooled = scores.First(s => s.Name == TenseEvaluator.TreebankPooledName && s.Group == TenseEvaluator.AllGroup);
        Assert.Equal(4, pooled.Tokens);
    }

    /// <summary>
    ///     The report has a row per model, set and group with the count, the accuracy, the majority baseline and the confusion.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TenseEvaluator_Render_FormatsRowsAndNotes()
    {
        // Arrange
        var score = new TenseScore("UD-EWT test", TenseEvaluator.AllGroup, 20, 5, 10, 65, 0);

        // Act
        var text = TenseEvaluator.Render([("Fake", [score])], ["a note"]);

        // Assert
        Assert.Contains("### Tense of same-spelling verbs", text, StringComparison.Ordinal);
        Assert.Contains("| Fake | UD-EWT test | all same-spelling verbs | 100 | 85.00% | 75.00% | 20 | 5 | 10 | 65 | 0 |", text, StringComparison.Ordinal);
        Assert.Contains("a note", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    /// <summary>
    ///     The command reports the tense of the basic model next to the part of speech, and <c>--report tense</c> only the
    ///     tense; a report that is neither is refused.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateRunning_Basic_ReportsTheTenseAndHonorsTheReportOption()
    {
        // Arrange
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var absent = Path.Join(_dir.Root, "absent.tsv");
        using var both = new StringWriter();
        using var tenseOnly = new StringWriter();
        using var refused = new StringWriter();
        using var error = new StringWriter();
        using var refusedError = new StringWriter();

        // Act
        var bothCode = await Program.RunAsync(["evaluate-running", "--model", "basic", "--repository", root.FullName, "--gold", absent], both, error, TestContext.Current.CancellationToken);
        var tenseCode = await Program.RunAsync(["evaluate-running", "--model", "basic", "--repository", root.FullName, "--gold", absent, "--report", "tense"], tenseOnly, error, TestContext.Current.CancellationToken);
        var refusedCode = await Program.RunAsync(["evaluate-running", "--model", "basic", "--repository", root.FullName, "--report", "nonsense"], refused, refusedError, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((0, 0, 1), (bothCode, tenseCode, refusedCode));
        Assert.Contains("### Part of speech on running text", both.ToString(), StringComparison.Ordinal);
        Assert.Contains("| Basic | open corpus test | all same-spelling verbs |", both.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("### Part of speech on running text", tenseOnly.ToString(), StringComparison.Ordinal);
        Assert.Contains("| Basic | open corpus dev | same lemma |", tenseOnly.ToString(), StringComparison.Ordinal);
        Assert.Contains("--report must be", refusedError.ToString(), StringComparison.Ordinal);
    }
}
