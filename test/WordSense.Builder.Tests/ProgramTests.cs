using DemaConsulting.WordSense.Basic;

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
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "walk\tnoun\tnoun\t\t\nwalk\tverb\tverb\t\t\n");
        _dir.Write("corpus/migration/legacy-class-map.tsv", "class_id\tkind\tlemma\tpos\tmeaning\trespelling\tsense_count\tsense_id\tgroup_id\tentry_id\tentry_lemma\tentry_pos\tfeatures\n");
        _dir.Write("corpus/anchors/a.tsv", "walk\tverb\tThey will {walk} home.\nwalk\tnoun\tThe {walk} was long.\n");
        _dir.Write("corpus/holdout/h.tsv", "walk\tverb\tYou should {walk} more.\n");
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
        foreach (var command in new[] { "compile-image", "import-moby", "weights", "evaluate", "ud-counts", "ud-irregular", "coverage", "train-basic", "tune-basic", "appendix" })
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
        foreach (var command in new[] { "import-oewn", "validate-data", "import-wn30-counts", "label-senses", "seed-inventory", "appendix" })
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

    /// <summary>
    ///     The train-basic command trains a loadable table from a tiny corpus and treebank counts.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_TrainBasic_TinyCorpus_WritesLoadableTable()
    {
        var corpus = PrepareCorpus();
        var sentences = new List<UdSentence>();
        for (var i = 0; i < 4; i++)
        {
            sentences.Add(new UdSentence(["they", "walk", "."], [PosTag.Pronoun, PosTag.Verb, PosTag.Punctuation]));
            sentences.Add(new UdSentence(["the", "walk", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]));
        }

        UdCounts.Count(sentences).Write(Path.Join(corpus, "ud"));
        var table = Path.Join(_dir.Root, "out", "basic.bin");

        var (code, output, error) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", table);

        Assert.True(code == 0, error);
        Assert.Contains("Trained", output, StringComparison.Ordinal);
        Assert.True(BasicTables.Load(await File.ReadAllBytesAsync(table, TestContext.Current.CancellationToken)).Words.Count > 0);
    }

    /// <summary>
    ///     A corpus whose anchors name a sense that the inventory lacks makes train-basic fail with an error.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_TrainBasic_InconsistentCorpus_ReportsError()
    {
        var corpus = PrepareCorpus();
        _dir.Write("corpus/anchors/a.tsv", "walk\tbogus\tThey will {walk} home.\n");
        UdCounts.Count([new UdSentence(["the", "walk"], [PosTag.Determiner, PosTag.Noun])]).Write(Path.Join(corpus, "ud"));

        var (code, _, error) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", Path.Join(_dir.Root, "basic.bin"));

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
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
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
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
