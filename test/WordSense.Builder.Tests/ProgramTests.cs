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
        foreach (var command in new[] { "words", "weights", "evaluate", "basic-precision", "appendix" })
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
    ///     The rule-based model has no weights to train.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_Weights_BasicModel_IsRuleBased()
    {
        var (code, _, error) = await RunAsync("weights", "basic", "--repository", _dir.Root);

        Assert.Equal(1, code);
        Assert.Contains("rule based", error, StringComparison.Ordinal);
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
    ///     The basic-precision command prints and optionally writes the report.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_BasicPrecision_PrintsAndWritesReport()
    {
        var corpus = PrepareCorpus();
        var target = Path.Combine(_dir.Root, "out", "precision.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var (code, output, _) = await RunAsync("basic-precision", "--corpus", corpus, "--output", target);

        Assert.Equal(0, code);
        Assert.Contains("### Basic rule precision on the training anchors", output, StringComparison.Ordinal);
        Assert.Equal(output, await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The evaluate command accepts the model id as second argument and evaluates the rule-based model quickly.
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
        Assert.Contains("- Weights size: 0 KiB", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
