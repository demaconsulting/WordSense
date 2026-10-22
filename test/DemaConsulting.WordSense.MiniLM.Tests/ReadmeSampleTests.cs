using System.Globalization;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Doc-check tests that keep the README usage samples and their documented output from drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> marks each usage sample with <c>&lt;!-- sample:NAME --&gt;</c> before the code block and
///     <c>&lt;!-- output:NAME --&gt;</c> before the output block. A code block must equal the using directives plus the
///     region of <c>ReadmeSample.cs</c> between the <c>&lt;sample:NAME&gt;</c> markers, and an output block must equal
///     what that code writes. An output block that still holds the text <c>TBD</c> has not been generated yet and
///     fails the test.
/// </remarks>
public class ReadmeSampleTests
{
    private const string Usings = "using DemaConsulting.WordSense;\n\n";

    /// <summary>
    ///     The using directives of the README samples that name the MiniLM model.
    /// </summary>
    private const string MiniLMUsings = "using DemaConsulting.WordSense;\nusing DemaConsulting.WordSense.MiniLM;\n\n";

    /// <summary>
    ///     The allowed difference of a documented Basic confidence: the Basic model is deterministic managed code, so
    ///     only the last digit of a rounded value may differ between math libraries.
    /// </summary>
    private const decimal BasicConfidenceTolerance = 0.01m;

    /// <summary>
    ///     The allowed difference of a documented encoder confidence: the int8 ONNX output differs slightly between
    ///     CPUs and operating systems (for example 0.80 on one and 0.85 on another).
    /// </summary>
    private const decimal EncoderConfidenceTolerance = 0.10m;

    private static async Task<(string Readme, string Source)> ReadAsync()
    {
        var root = DocSampleText.FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        return (
            await File.ReadAllTextAsync(Path.Join(root, "README.md"), token),
            await File.ReadAllTextAsync(Path.Join(root, "test", "DemaConsulting.WordSense.MiniLM.Tests", "ReadmeSample.cs"), token));
    }

    private static string Compiled(string source, string name, string usings) =>
        DocSampleText.Normalize(usings + DocSampleText.Dedent(DocSampleText.Between(source, $"// <sample:{name}>", $"// </sample:{name}>")));

    /// <summary>
    ///     The README code blocks equal the compiled sample code.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CodeBlocks_MatchCompiledSamples()
    {
        var (readme, source) = await ReadAsync();

        Assert.Equal(Compiled(source, "readme", Usings), DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:readme -->", "csharp")));
    }

    /// <summary>
    ///     The README block that calls <c>EnsureModelAsync</c> on the concrete model type equals the compiled sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_DownloadBlock_MatchesCompiledSample()
    {
        // Arrange
        var (readme, source) = await ReadAsync();

        // Act
        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:download -->", "csharp"));

        // Assert: the code the README shows is the code that compiles, with the call made directly on the model
        Assert.Equal(Compiled(source, "download", MiniLMUsings), documented);
        Assert.Contains("await model.EnsureModelAsync();", documented, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDownloadableWordSenseModel", documented, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The README block that asks an <c>IWordSenseModel</c> for the downloadable capability equals the compiled
    ///     sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CapabilityBlock_MatchesCompiledSample()
    {
        // Arrange
        var (readme, source) = await ReadAsync();

        // Act
        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:capability -->", "csharp"));

        // Assert: the type check pattern for code that holds only the model contract
        Assert.Equal(Compiled(source, "capability", MiniLMUsings), documented);
        Assert.Contains("if (model is IDownloadableWordSenseModel downloadable)", documented, StringComparison.Ordinal);
        Assert.Contains("await downloadable.EnsureModelAsync();", documented, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The README marks exactly the samples that are checked.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_SampleMarkers_AreAllChecked()
    {
        var (readme, _) = await ReadAsync();

        Assert.Equal(["readme", "download", "capability"], DocSampleText.SampleNames(readme));
    }

    /// <summary>
    ///     The two commented alternatives of the README sample name model classes that exist and can be created.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CommentedAlternatives_NameExistingModels()
    {
        var (_, source) = await ReadAsync();
        var sample = DocSampleText.Between(source, "// <sample:readme>", "// </sample:readme>");

        Assert.Contains("// using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;", sample, StringComparison.Ordinal);
        Assert.Contains("// using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;", sample, StringComparison.Ordinal);

        // The alternatives compile as written: both classes have a parameterless constructor
        using var miniLM = new MiniLMWordSenseModel();
        using var bertBaseCased = new DemaConsulting.WordSense.BertBaseCased.BertBaseCasedWordSenseModel();
        Assert.Equal("all-MiniLM-L6-v2-int8", miniLM.ModelId);
        Assert.Equal("bert-base-cased-int8", bertBaseCased.ModelId);
    }

    /// <summary>
    ///     The documented Basic output equals the output of the sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public Task WordSense_Readme_BasicOutput_MatchesReadme() => CheckOutputAsync("basic", "basic", null, BasicConfidenceTolerance);

    /// <summary>
    ///     The documented MiniLM output equals the output of the sample run against the real model; the confidences
    ///     match within <see cref="EncoderConfidenceTolerance"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public Task WordSense_Readme_MiniLMOutput_MatchesReadme() => CheckOutputAsync("minilm", "minilm", "all-MiniLM-L6-v2-int8", EncoderConfidenceTolerance);

    /// <summary>
    ///     The documented BertBaseCased output equals the output of the sample run against the real model; the
    ///     confidences match within <see cref="EncoderConfidenceTolerance"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public Task WordSense_Readme_BertBaseCasedOutput_MatchesReadme() => CheckOutputAsync("bertbasecased", "bertbasecased", "bert-base-cased-int8", EncoderConfidenceTolerance);

    private static async Task CheckOutputAsync(string marker, string modelKey, string? modelId, decimal tolerance)
    {
        var (readme, _) = await ReadAsync();
        var documented = DocSampleText.FencedBlock(readme, $"<!-- output:{marker} -->", "text");
        FailWhenNotGenerated(documented);
        if (modelId is not null)
        {
            DocSampleText.RequireModelDirectory(modelId);
        }

        var sample = new ReadmeSample();
        await RunInvariantAsync(() => sample.RunAsync(modelKey));

        // The README is the expected value: the words, parts of speech must match exactly
        DocSampleText.AssertSampleOutput(documented, sample.Output, tolerance);
    }

    private static void FailWhenNotGenerated(string documented)
    {
        if (documented.Contains("TBD", StringComparison.Ordinal))
        {
            Assert.Fail("The documented output has not been generated yet: replace the TBD text with the real output.");
        }
    }

    private static async Task RunInvariantAsync(Func<Task> run)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await run();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
