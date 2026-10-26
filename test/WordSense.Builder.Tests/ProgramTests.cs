using DemaConsulting.WordSense;
namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the command line of the builder.
/// </summary>
public sealed class ProgramTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await Program.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    private string PrepareCorpus()
    {
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        _dir.Write("corpus/anchors/a.tsv", $"walk\t{verb.SenseId}\tVerb\tThey will {{walk}} home.\nwalk\t{noun.SenseId}\tNoun\tThe {{walk}} was long.\n");
        _dir.Write("corpus/holdout/h.tsv", $"walk\t{verb.SenseId}\tVerb\tYou should {{walk}} more.\n");
        return Path.Join(_dir.Root, "corpus");
    }

    /// <summary>
    ///     Without arguments the usage lists every command.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_NoArguments_PrintsUsage()
    {
        var (code, _, error) = await RunAsync();

        Assert.Equal(1, code);
        foreach (var command in new[] { "compile-image", "import-moby", "weights", "evaluate", "open-counts", "evaluate-tagger", "coverage", "train-basic", "tune-basic", "appendix", "keys", "heteronyms" })
        {
            Assert.Contains(command, error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     The usage lists the commands of the sense inventory.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Usage_ListsTheSenseCommands()
    {
        var (code, _, error) = await RunAsync();

        Assert.Equal(1, code);
        foreach (var command in new[] { "import-oewn", "validate-data", "import-wn30-counts", "label-senses", "seed-inventory", "appendix", "evaluate-senses", "evaluate-allwords", "allwords-sample", "allwords-merge", "evaluate-running", "evaluate-suite", "check-leakage", "tiers" })
        {
            Assert.Contains(command, error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     An unknown command is named in the error together with the usage.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_UnknownCommand_ReportsCommandAndUsage()
    {
        var (code, output, error) = await RunAsync("frobnicate");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("Unknown command 'frobnicate'", error, StringComparison.Ordinal);
        Assert.Contains("Usage:", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The import-moby command needs an input, and rejects an input that is not the pinned file.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_ImportMoby_RequiresPinnedInput()
    {
        var (missingCode, _, missingError) = await RunAsync("import-moby");
        var input = _dir.Write("mobypos.txt", "the\\D\n");
        var (wrongCode, _, wrongError) = await RunAsync("import-moby", "--input", input, "--output", Path.Join(_dir.Root, "lexicon.tsv"));

        Assert.Equal(1, missingCode);
        Assert.Contains("--input", missingError, StringComparison.Ordinal);
        Assert.Equal(1, wrongCode);
        Assert.Contains("pinned", wrongError, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Join(_dir.Root, "lexicon.tsv")));
    }

    /// <summary>
    ///     The old command names are gone.
    /// </summary>
    /// <param name="command">The removed command.</param>
    [Theory]
    [InlineData("build")]
    [InlineData("train")]
    [InlineData("generate-appendix")]
    [InlineData("validate-pos")]
    [InlineData("ud-counts")]
    [InlineData("ud-irregular")]
    public async Task WordSenseBuilder_Program_RemovedCommand_IsUnknown(string command)
    {
        var (code, _, error) = await RunAsync(command);

        Assert.Equal(1, code);
        Assert.Contains("Unknown command", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The weights and evaluate commands need a model id.
    /// </summary>
    /// <param name="command">The command.</param>
    [Theory]
    [InlineData("weights")]
    [InlineData("evaluate")]
    public async Task WordSenseBuilder_Program_ModelCommandWithoutModel_ReportsUsage(string command)
    {
        var (code, output, error) = await RunAsync(command, "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("--model is required", error, StringComparison.Ordinal);
        Assert.Contains("Usage:", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An unknown model id is reported with the known ones.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Weights_UnknownModel_ListsTheModels()
    {
        var (code, _, error) = await RunAsync("weights", "nosuchmodel", "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Contains("Unknown model 'nosuchmodel'", error, StringComparison.Ordinal);
        Assert.Contains("minilm", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An unknown model id is reported with the known ones by the evaluation too.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Evaluate_UnknownModel_ReportsError()
    {
        var (code, _, error) = await RunAsync("evaluate", "nosuchmodel", "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Contains("Unknown model 'nosuchmodel'", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The statistical model has no encoder weights; the error names the command that retrains it.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Weights_BasicModel_PointsToTrainBasic()
    {
        var (code, _, error) = await RunAsync("weights", "basic", "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Contains("train-basic", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The switches --no-cache and --recompute-mean need no value, and accept an explicit true or false; the
    ///     command then proceeds to the model check.
    /// </summary>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    [Theory]
    [InlineData("--no-cache", "--recompute-mean")]
    [InlineData("--recompute-mean", "--no-cache")]
    [InlineData("--no-cache", "false")]
    [InlineData("--recompute-mean", "true")]
    public async Task WordSenseBuilder_Program_Weights_SwitchOptions_AreAcceptedWithoutValue(string first, string second)
    {
        var (code, _, error) = await RunAsync("weights", "basic", first, second, "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Contains("train-basic", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A cache folder together with --no-cache is contradictory and is reported.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Weights_CacheAndNoCache_AreRejected()
    {
        var (code, _, error) = await RunAsync("weights", "basic", "--cache", Path.Join(_dir.Root, "c"), "--no-cache");

        Assert.Equal(1, code);
        Assert.Contains("cannot be combined", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The cache folder defaults to the repository, is overridden by --cache and is off with --no-cache.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Program_TrainSettingsOf_ResolvesCacheAndMean()
    {
        var defaults = Program.TrainSettingsOf([], _dir.Root);
        var custom = Program.TrainSettingsOf(new() { ["cache"] = Path.Join(_dir.Root, "mine"), ["recompute-mean"] = "true" }, _dir.Root);
        var off = Program.TrainSettingsOf(new() { ["no-cache"] = "true", ["recompute-mean"] = "false" }, _dir.Root);

        Assert.Equal(Path.Join(_dir.Root, ".cache", "embeddings"), defaults.CacheRoot);
        Assert.False(defaults.RecomputeMean);
        Assert.Equal(Path.Join(_dir.Root, "mine"), custom.CacheRoot);
        Assert.True(custom.RecomputeMean);
        Assert.Null(off.CacheRoot);
        Assert.False(off.RecomputeMean);
    }

    private const string OpenHeader = "# corpus = test\n# columns = id form lemma upos collapsed feats consensus\n\n";

    private string PrepareOpenCorpus()
    {
        var corpus = PrepareCorpus();
        const string train =
            "# sent_id = s1\n# source_id = d1\n# text = They walk.\n1\tThey\tthey\tPRON\tR\t_\tunanimous\n2\twalk\twalk\tVERB\tV\t_\tunanimous\n3\t.\t.\tPUNCT\tO\t_\tunanimous\n\n" +
            "# sent_id = s2\n# source_id = d2\n# text = The walk is long.\n1\tThe\tthe\tDET\tD\t_\tunanimous\n2\twalk\twalk\tNOUN\tN\t_\tunanimous\n3\tis\tbe\tVERB\tV\t_\tunanimous\n4\tlong\tlong\tADJ\tJ\t_\tunanimous\n5\t.\t.\tPUNCT\tO\t_\tunanimous\n";
        _dir.Write("corpus/open/representative/data/train.tsv", OpenHeader + train);
        _dir.Write("corpus/open/representative/data/test.tsv", OpenHeader + train);
        return corpus;
    }

    /// <summary>
    ///     The open-counts command counts the training split of the representative part into the counts folder.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_OpenCounts_WritesCountFiles()
    {
        var corpus = PrepareOpenCorpus();

        var (code, output, error) = await RunAsync("open-counts", "--corpus", corpus);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("Counted 2 sentences (8 tokens)", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(corpus, "open", "counts", TaggerCounts.NgramsFile)));
        Assert.True(File.Exists(Path.Join(corpus, "open", "counts", TaggerCounts.ClauseNgramsFile)));
        Assert.Equal(1, TaggerCounts.Read(Path.Join(corpus, "open", "counts")).Words["walk"][PosTag.Noun]);
    }

    /// <summary>
    ///     The train-basic command trains a loadable table from the open-corpus counts and the fine-sense anchors, and
    ///     the evaluate-tagger command scores it on the test split.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_TrainBasicAndEvaluateTagger_TinyCorpus_WriteAndScoreTable()
    {
        var corpus = PrepareOpenCorpus();
        var table = Path.Join(_dir.Root, "out", "basic.bin");
        await RunAsync("open-counts", "--corpus", corpus);

        var (code, output, error) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", table);
        var (scoreCode, scores, scoreError) = await RunAsync("evaluate-tagger", "--repository", _dir.Root, "--corpus", corpus, "--input", table);

        Assert.True(code == 0, error);
        Assert.Contains("Trained", output, StringComparison.Ordinal);
        Assert.True(BasicTables.Load(await File.ReadAllBytesAsync(table, TestContext.Current.CancellationToken)).Words.Count > 0);
        Assert.True(scoreCode == 0, scoreError);
        Assert.Contains("| open corpus test (representative) | 2 |", scores, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Without the count files train-basic fails and names the command that makes them.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_TrainBasic_WithoutCounts_ReportsError()
    {
        var corpus = PrepareCorpus();

        var (code, _, error) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", Path.Join(_dir.Root, "basic.bin"));

        Assert.Equal(1, code);
        Assert.Contains("open-counts", error, StringComparison.Ordinal);
    }

    private const string BrokenOpenFile = OpenHeader + "# sent_id = broken\n# source_id = broken\n# text = Never read.\nthis is not a token line\n";

    private const string PutTrainSentence =
        "# sent_id = p1\n# source_id = d1\n# text = They put it.\n1\tThey\tthey\tPRON\tR\t_\tunanimous\n2\tput\tput\tVERB\tV\tTense=Past|VerbForm=Fin\tunanimous\n3\tit\tit\tPRON\tR\t_\tunanimous\n4\t.\t.\tPUNCT\tO\t_\tunanimous\n\n" +
        "# sent_id = p2\n# source_id = d2\n# text = To put it.\n1\tTo\tto\tPART\tT\t_\tunanimous\n2\tput\tput\tVERB\tV\tVerbForm=Inf\tunanimous\n3\tit\tit\tPRON\tR\t_\tunanimous\n4\t.\t.\tPUNCT\tO\t_\tunanimous\n";

    /// <summary>
    ///     The training commands read only the training split of the representative open corpus: with development and test
    ///     files that cannot be read (a malformed line makes a read fail), open-counts and train-basic still succeed.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_TrainBasic_ReadsOnlyTheTrainSplit()
    {
        // Arrange: a valid training split, and development and test splits (also of the targeted part) that fail when read
        var corpus = PrepareCorpus();
        _dir.Write("corpus/open/representative/data/train.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/representative/data/test.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/dev.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/test.tsv", BrokenOpenFile);
        var table = Path.Join(_dir.Root, "out", "basic.bin");

        // Act
        var (countsCode, _, countsError) = await RunAsync("open-counts", "--corpus", corpus);
        var (trainCode, trainOutput, trainError) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", table);

        // Assert: both commands succeeded, so no unreadable split was read, and the tense table came from the training verbs
        Assert.True(countsCode == 0, countsError);
        Assert.True(trainCode == 0, trainError);
        Assert.Contains("a tense table of", trainOutput, StringComparison.Ordinal);
        Assert.NotNull(BasicTables.Load(await File.ReadAllBytesAsync(table, TestContext.Current.CancellationToken)).Tense);
    }

    /// <summary>
    ///     The tuning command scores only development inputs, the development split of the open corpus and the development
    ///     file of the treebank, and trains from the training split: with test and training files of the treebank, and test
    ///     splits of the open corpus, that cannot be read, it still succeeds, and the report names only development sets.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_TuneBasic_ScoresOnlyTheDevelopmentInputs()
    {
        // Arrange: valid training and development splits with a same-spelling verb, and unreadable test inputs
        var corpus = PrepareCorpus();
        _dir.Write("corpus/open/representative/data/train.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/test.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/dev.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/test.tsv", BrokenOpenFile);
        _dir.Write("gold/ud_dev.tsv", "d1\tThey\tR\tthey\tPRP\t_\nd1\tput\tV\tput\tVBD\tTense=Past|VerbForm=Fin\nd1\tit\tR\tit\tPRP\t_\n");
        _dir.Write("gold/ud_test.tsv", "this is not a gold line\n");
        _dir.Write("gold/ud_train.tsv", "this is not a gold line\n");
        await RunAsync("open-counts", "--corpus", corpus);

        // Act: tune only the development inputs, with the test files of the treebank unreadable
        var (code, output, error) = await RunAsync("tune-basic", "--repository", _dir.Root, "--corpus", corpus, "--gold", Path.Join(_dir.Root, "gold", "ud_dev.tsv"));

        // Assert: the tense weights are scored on the two development sets and on nothing else; the word prior and the
        // cue reliability tables are in the report
        Assert.True(code == 0, error);
        Assert.Contains("clause-smoothing:", output, StringComparison.Ordinal);
        Assert.Contains("Treebank dev classifier", output, StringComparison.Ordinal);
        Assert.Contains("Word prior variant", output, StringComparison.Ordinal);
        Assert.Contains("Cue skill target", output, StringComparison.Ordinal);
        Assert.Contains("| 1.00 | open corpus dev |", output, StringComparison.Ordinal);
        Assert.Contains("| 1.00 | UD-EWT dev |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("UD-EWT test", output, StringComparison.Ordinal);
        Assert.DoesNotContain("UD-EWT train", output, StringComparison.Ordinal);
        Assert.DoesNotContain("open corpus test", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tuning command runs only the section that <c>--sections</c> names.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_TuneBasic_SectionsPrior_RunsOnlyThePriorSection()
    {
        // Arrange: valid training and development splits, and the counts
        var corpus = PrepareCorpus();
        _dir.Write("corpus/open/representative/data/train.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/test.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/dev.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/test.tsv", BrokenOpenFile);
        await RunAsync("open-counts", "--corpus", corpus);

        // Act: tune only the section of the word prior
        var (code, output, error) = await RunAsync("tune-basic", "--repository", _dir.Root, "--corpus", corpus, "--sections", "prior");

        // Assert: the table of the word prior is there and no table of another section is
        Assert.True(code == 0, error);
        Assert.Contains("Word prior variant", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Open dev ambiguous POS", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Cue skill target", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Tense weight", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tuning command runs only the section that <c>--sections</c> names: the section of the noun/verb model scores
    ///     the penalties and weights of the model and no other table.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_TuneBasic_SectionsNounVerb_RunsOnlyTheNounVerbSection()
    {
        // Arrange: valid training and development splits, and the counts
        var corpus = PrepareCorpus();
        _dir.Write("corpus/open/representative/data/train.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", OpenHeader + PutTrainSentence);
        _dir.Write("corpus/open/representative/data/test.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/dev.tsv", BrokenOpenFile);
        _dir.Write("corpus/open/targeted/data/test.tsv", BrokenOpenFile);
        await RunAsync("open-counts", "--corpus", corpus);

        // Act: tune only the section of the noun/verb model
        var (code, output, error) = await RunAsync("tune-basic", "--repository", _dir.Root, "--corpus", corpus, "--sections", "nounverb");

        // Assert: the table of the noun/verb model is there, with a row for every penalty and weight, and no other table is
        Assert.True(code == 0, error);
        Assert.Contains("| L2 | Noun/verb weight |", output, StringComparison.Ordinal);
        Assert.Equal(
            BasicHyperparameters.NounVerbL2Grid.Count * BasicHyperparameters.NounVerbWeightGrid.Count,
            System.Text.RegularExpressions.Regex.Matches(output, @"^\| \d+\.\d \| \d\.\d\d \|", System.Text.RegularExpressions.RegexOptions.Multiline).Count);
        Assert.DoesNotContain("Word prior variant", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Cue skill target", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Tense weight", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A number option that is not a number, or is out of range, is a one-line error with exit code 1, not an
    ///     unhandled exception with a stack trace.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="option">The option.</param>
    /// <param name="value">The value.</param>
    /// <param name="expected">The text the message must contain.</param>
    [Theory]
    [InlineData("coverage", "--top", "nope", "--top must be an integer of at least 1, not 'nope'")]
    [InlineData("coverage", "--top", "0", "--top must be an integer of at least 1, not '0'")]
    [InlineData("generate-facts", "--sample", "x1", "--sample must be an integer of at least 1, not 'x1'")]
    [InlineData("generate-facts", "--seed", "1.5", "--seed must be an integer, not '1.5'")]
    [InlineData("audit-forms", "--max-error", "high", "--max-error must be a number, not 'high'")]
    [InlineData("audit-forms", "--seed", "99999999999", "--seed must be an integer, not '99999999999'")]
    [InlineData("import-oewn", "--threshold", "-3", "--threshold must be an integer of at least 0, not '-3'")]
    public async Task WordSenseBuilder_Program_MalformedNumberOption_ReportsOneLineError(string command, string option, string value, string expected)
    {
        // Arrange
        var text = _dir.Write("text/t.txt", "x\n");
        string[] args = command switch
        {
            "coverage" => [command, "--text", Path.GetDirectoryName(text)!, option, value],
            "import-oewn" => [command, "--input", text, option, value],
            _ => [command, option, value],
        };

        // Act
        var (code, _, error) = await RunAsync(args);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    ///     A malformed option list is a usage error.
    /// </summary>
    /// <param name="option">The option that lacks a value.</param>
    /// <param name="next">The text after it, or an empty string for none.</param>
    [Theory]
    [InlineData("--corpus", "")]
    [InlineData("--corpus", "--output")]
    [InlineData("corpus", "x")]
    public async Task WordSenseBuilder_Program_MalformedOptions_ReportUsage(string option, string next)
    {
        string[] args = next.Length == 0 ? ["compile-image", option] : ["compile-image", option, next];

        var (code, _, error) = await RunAsync(args);

        Assert.Equal(1, code);
        Assert.Contains("Expected '--name value'", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The coverage command reports the figures of a text directory and can write them to a file.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Coverage_ReportsAndWritesFile()
    {
        var corpus = PrepareCorpus();
        _dir.Write("text/t.txt", "*** START ***\nwalk walked zzqxj\n*** END ***\n");
        var target = Path.Join(_dir.Root, "out", "coverage.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var (code, output, error) = await RunAsync("coverage", "--corpus", corpus, "--text", Path.Join(_dir.Root, "text"), "--top", "5", "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("| Known tokens | 2 (66.67 percent) |", output, StringComparison.Ordinal);
        Assert.Contains("| Known tokens of trained lemmas | 2 (100.00 percent of the known tokens) |", output, StringComparison.Ordinal);
        Assert.Contains("| zzqxj | 1 |", output, StringComparison.Ordinal);
        Assert.Equal(output, await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The coverage command requires the text directory.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Coverage_WithoutText_Fails()
    {
        var (code, _, error) = await RunAsync("coverage", "--corpus", PrepareCorpus());

        Assert.Equal(1, code);
        Assert.Contains("text", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The compile-image command compiles the committed data into an image that equals the committed one.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_CompileImage_WritesImage()
    {
        var root = FindRepositoryRoot();
        var target = Path.Join(_dir.Root, "out", "wordsense.img");

        var (code, output, error) = await RunAsync("compile-image", "--data", Path.Join(root, "data"), "--corpus", Path.Join(root, "corpus"), "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("Wrote the word image to", output, StringComparison.Ordinal);
        var image = DemaConsulting.WordSense.WordImage.Open(await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(DemaConsulting.WordSense.WordLexicon.Count, image.SpellingCount);
        Assert.True(image.TryLookup("record", out var senses));
        Assert.True(senses.Length >= 2);
    }

    /// <summary>
    ///     The compile-image command reports missing data with exit code 1 and writes nothing.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_CompileImage_MissingData_Fails()
    {
        var target = Path.Join(_dir.Root, "out", "wordsense.img");

        var (code, _, error) = await RunAsync("compile-image", "--data", Path.Join(_dir.Root, "nodata"), "--corpus", Path.Join(_dir.Root, "corpus"), "--output", target);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.False(File.Exists(target));
    }

    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    /// <summary>
    ///     The appendix command writes the file and reports the number of lemmas.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Appendix_WritesFile()
    {
        var data = ImageCompilerTests.WriteSmallData(_dir.Root);
        _dir.Write("data/gloss/g.tsv", "synset_id\tseq\ttext\n" + string.Concat(Enumerable.Range(1, 6).Select(i => $"oewn-{i}-{(i < 5 ? "n" : "v")}\t1\tgloss {i}\n")));
        _dir.Write("corpus/anchors/a.tsv", "bank\tbank#ridge\tNoun\tA {bank} here.\nbank\tbank#cover\tVerb\tThey {bank} it.\n");
        _dir.Write("corpus/curation/heteronyms.tsv", "level\tgroup\tclass\tpattern\tnote\n");
        var corpus = Path.Join(_dir.Root, "corpus");
        var target = Path.Join(_dir.Root, "out", "appendix.md");

        var (code, output, error) = await RunAsync("appendix", "--corpus", corpus, "--data", data, "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("Wrote the appendix of 1 lemmas", output, StringComparison.Ordinal);
        Assert.Equal(AppendixGenerator.Generate(corpus, data), await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The appendix command reports a missing corpus with exit code 1.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Appendix_MissingCorpus_Fails()
    {
        var (code, _, error) = await RunAsync("appendix", "--corpus", Path.Join(_dir.Root, "missing"), "--output", Path.Join(_dir.Root, "x.md"));

        Assert.Equal(1, code);
        Assert.Contains("was not found", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The keys command needs an output file and writes the header and the keys.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Keys_WritesTheKeyFile()
    {
        var target = Path.Join(_dir.Root, "out", "keys.txt");

        var (missingCode, _, missingError) = await RunAsync("keys");
        var (code, output, error) = await RunAsync("keys", "--output", target);

        Assert.Equal(1, missingCode);
        Assert.Contains("--output", missingError, StringComparison.Ordinal);
        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains($"keys of the lexicon {WordLexicon.Version}", output, StringComparison.Ordinal);
        using var reader = new StreamReader(target);
        Assert.StartsWith("# WordSense keys; format: ", await reader.ReadLineAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The heteronyms command needs an output file and writes the table of the committed data.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Heteronyms_WritesTheTable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var target = Path.Join(_dir.Root, "out", "heteronyms.tsv");

        var (missingCode, _, missingError) = await RunAsync("heteronyms");
        var (code, output, error) = await RunAsync("heteronyms", "--repository", root.FullName, "--output", target);

        Assert.Equal(1, missingCode);
        Assert.Contains("--output", missingError, StringComparison.Ordinal);
        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("heteronym records", output, StringComparison.Ordinal);
        Assert.StartsWith("level\tgroup\tspelling\tclass\tkey\tnote\n", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The evaluate command accepts the model id as second argument and evaluates the basic model quickly.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Evaluate_BasicModel_PrintsMeasurements()
    {
        var corpus = PrepareCorpus();
        var target = Path.Join(_dir.Root, "out", "evaluation.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var (code, output, error) = await RunAsync("evaluate", "basic", "--repository", _dir.Root, "--corpus", corpus, "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("### ", output, StringComparison.Ordinal);
        Assert.Contains("| holdout | 1 |", output, StringComparison.Ordinal);
        Assert.Contains("- Weights size:", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The forms audit rejects a sample size that would leave no row to check.
    /// </summary>
    /// <param name="sample">The sample size.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task WordSenseBuilder_Program_AuditForms_NonPositiveSample_Fails(string sample)
    {
        var (code, _, error) = await RunAsync("audit-forms", "--sample", sample);

        Assert.Equal(1, code);
        Assert.Contains("--sample", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The forms audit rejects an empty oracle file instead of treating every row as skipped.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_AuditForms_EmptyOracleFile_Fails()
    {
        var oracle = Path.Join(_dir.Root, "oracle.tsv");
        await File.WriteAllTextAsync(oracle, string.Empty, TestContext.Current.CancellationToken);

        var (code, _, error) = await RunAsync("audit-forms", "--oracle", oracle);

        Assert.Equal(1, code);
        Assert.Contains("no verdict", error, StringComparison.Ordinal);
    }
}
