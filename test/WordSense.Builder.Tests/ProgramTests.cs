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
        _dir.Write("corpus/anchors/a.tsv", "walk\tverb\tThey will {walk} home.\nwalk\tnoun\tThe {walk} was long.\n");
        _dir.Write("corpus/holdout/h.tsv", "walk\tverb\tYou should {walk} more.\n");
        return Path.Combine(_dir.Root, "corpus");
    }

    /// <summary>
    ///     Without arguments the usage lists every command.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_NoArguments_PrintsUsage()
    {
        var (code, _, error) = await RunAsync();

        Assert.Equal(1, code);
        foreach (var command in new[] { "words", "import-moby", "weights", "evaluate", "ud-counts", "train-basic", "tune-basic", "appendix" })
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
        var (wrongCode, _, wrongError) = await RunAsync("import-moby", "--input", input, "--output", Path.Combine(_dir.Root, "lexicon.tsv"));

        Assert.Equal(1, missingCode);
        Assert.Contains("--input", missingError, StringComparison.Ordinal);
        Assert.Equal(1, wrongCode);
        Assert.Contains("pinned", wrongError, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir.Root, "lexicon.tsv")));
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

        UdCounts.Count(sentences).Write(Path.Combine(corpus, "ud"));
        var table = Path.Combine(_dir.Root, "out", "basic.bin");

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
        UdCounts.Count([new UdSentence(["the", "walk"], [PosTag.Determiner, PosTag.Noun])]).Write(Path.Combine(corpus, "ud"));

        var (code, _, error) = await RunAsync("train-basic", "--repository", _dir.Root, "--corpus", corpus, "--output", Path.Combine(_dir.Root, "basic.bin"));

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
        string[] args = next.Length == 0 ? ["words", option] : ["words", option, next];

        var (code, _, error) = await RunAsync(args);

        Assert.Equal(1, code);
        Assert.Contains("Expected '--name value'", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The words command writes the table, reports the word count and the table is readable.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Words_WritesTable()
    {
        var corpus = PrepareCorpus();
        var target = Path.Combine(_dir.Root, "out", "words.bin");

        var (code, output, error) = await RunAsync("words", "--corpus", corpus, "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("Wrote 1 words to", output, StringComparison.Ordinal);
        var table = DemaConsulting.WordSense.WordsBlob.Read(await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(2, table["walk"].Length);
    }

    /// <summary>
    ///     The words command reports an inconsistent corpus with exit code 1 and writes nothing.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Words_InconsistentCorpus_Fails()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "walk\tnoun\tnoun\t\t\n");
        var target = Path.Combine(_dir.Root, "out", "words.bin");

        var (code, _, error) = await RunAsync("words", "--corpus", Path.Combine(_dir.Root, "corpus"), "--output", target);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.False(File.Exists(target));
    }

    /// <summary>
    ///     The appendix command writes the file and reports the number of lemmas.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Appendix_WritesFile()
    {
        var corpus = PrepareCorpus();
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nwalks\twalk\tnoun,verb\n");
        var target = Path.Combine(_dir.Root, "out", "appendix.md");

        var (code, output, _) = await RunAsync("appendix", "--corpus", corpus, "--output", target);

        Assert.Equal(0, code);
        Assert.Contains("Wrote the appendix of 1 lemmas", output, StringComparison.Ordinal);
        Assert.Equal(AppendixGenerator.Generate(corpus), await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The appendix command reports a missing corpus with exit code 1.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Appendix_MissingCorpus_Fails()
    {
        var (code, _, error) = await RunAsync("appendix", "--corpus", Path.Combine(_dir.Root, "missing"), "--output", Path.Combine(_dir.Root, "x.md"));

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
        var target = Path.Combine(_dir.Root, "out", "evaluation.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var (code, output, error) = await RunAsync("evaluate", "basic", "--repository", _dir.Root, "--corpus", corpus, "--output", target);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("### ", output, StringComparison.Ordinal);
        Assert.Contains("| holdout | 1 |", output, StringComparison.Ordinal);
        Assert.Contains("- Weights size:", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
