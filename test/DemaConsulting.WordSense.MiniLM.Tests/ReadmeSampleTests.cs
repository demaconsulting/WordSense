using System.Globalization;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Doc-check tests that keep the usage samples of the README and the user guide and their documented output from
///     drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> holds the main sample and its Basic output. <c>docs/user_guide/introduction.md</c> holds the
///     download and capability samples and the encoder outputs. Each document marks a usage sample with
///     <c>&lt;!-- sample:NAME --&gt;</c> before the code block and <c>&lt;!-- output:NAME --&gt;</c> before the output
///     block. A code block must equal the using directives plus the region of <c>ReadmeSample.cs</c> between the
///     <c>&lt;sample:NAME&gt;</c> markers, and an output block must equal what that code writes. An output block that
///     still holds the text <c>TBD</c> has not been generated yet and fails the test.
/// </remarks>
public class ReadmeSampleTests
{
    /// <summary>
    ///     The using directives of the README samples that need only the core package.
    /// </summary>
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

    /// <summary>
    ///     Reads the README and the source of the compiled README samples.
    /// </summary>
    /// <returns>The text of <c>README.md</c> and of <c>ReadmeSample.cs</c>.</returns>
    private static async Task<(string Readme, string Source)> ReadAsync()
    {
        var root = DocSampleText.FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        return (
            await File.ReadAllTextAsync(Path.Join(root, "README.md"), token),
            await File.ReadAllTextAsync(Path.Join(root, "test", "DemaConsulting.WordSense.MiniLM.Tests", "ReadmeSample.cs"), token));
    }

    /// <summary>
    ///     Reads the user guide that holds the download and capability samples and the encoder outputs.
    /// </summary>
    /// <returns>The text of <c>docs/user_guide/introduction.md</c>.</returns>
    private static async Task<string> ReadGuideAsync() =>
        await File.ReadAllTextAsync(
            Path.Join(DocSampleText.FindRepositoryRoot(), "docs", "user_guide", "introduction.md"),
            TestContext.Current.CancellationToken);

    /// <summary>
    ///     Builds the text that a README code block must equal: the using directives plus the unindented region of the
    ///     sample source between the markers of the sample, with the common indentation removed.
    /// </summary>
    /// <param name="source">The text of <c>ReadmeSample.cs</c>.</param>
    /// <param name="name">The name of the sample in its <c>&lt;sample:NAME&gt;</c> markers.</param>
    /// <param name="usings">The using directives that the README block shows before the region.</param>
    /// <returns>The normalized expected code block.</returns>
    private static string Compiled(string source, string name, string usings) =>
        DocSampleText.Normalize(usings + DocSampleText.Dedent(DocSampleText.Between(source, $"// <sample:{name}>", $"// </sample:{name}>")));

    /// <summary>
    ///     The README code blocks equal the compiled sample code.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CodeBlocks_MatchCompiledSamples()
    {
        // Arrange: read the README and the compiled sample source
        var (readme, source) = await ReadAsync();

        // Act: take the documented block of the main sample
        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:readme -->", "csharp"));

        // Assert: the documented block equals the compiled sample
        Assert.Equal(Compiled(source, "readme", Usings), documented);
    }

    /// <summary>
    ///     The user guide block that calls <c>EnsureModelAsync</c> on the concrete model type equals the compiled
    ///     sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_DownloadBlock_MatchesCompiledSample()
    {
        // Arrange
        var (_, source) = await ReadAsync();
        var guide = await ReadGuideAsync();

        // Act
        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(guide, "<!-- sample:download -->", "csharp"));

        // Assert: the code the guide shows is the code that compiles, with the call made directly on the model
        Assert.Equal(Compiled(source, "download", MiniLMUsings), documented);
        Assert.Contains("await model.EnsureModelAsync();", documented, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDownloadableWordSenseModel", documented, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The user guide block that asks an <c>IWordSenseModel</c> for the downloadable capability equals the compiled
    ///     sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CapabilityBlock_MatchesCompiledSample()
    {
        // Arrange
        var (_, source) = await ReadAsync();
        var guide = await ReadGuideAsync();

        // Act
        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(guide, "<!-- sample:capability -->", "csharp"));

        // Assert: the type check pattern for code that holds only the model contract
        Assert.Equal(Compiled(source, "capability", MiniLMUsings), documented);
        Assert.Contains("if (model is IDownloadableWordSenseModel downloadable)", documented, StringComparison.Ordinal);
        Assert.Contains("await downloadable.EnsureModelAsync();", documented, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The README and the user guide mark exactly the samples that are checked.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_SampleMarkers_AreAllChecked()
    {
        // Arrange: read the README and the user guide
        var (readme, _) = await ReadAsync();
        var guide = await ReadGuideAsync();

        // Act: list the samples that each document marks
        var readmeNames = DocSampleText.SampleNames(readme);
        var guideNames = DocSampleText.SampleNames(guide);

        // Assert: every marked sample is one of the checked samples, in document order
        Assert.Equal(["readme"], readmeNames);
        Assert.Equal(["download", "capability"], guideNames);
    }

    /// <summary>
    ///     The two commented alternatives of the README sample name model classes that exist and can be created.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CommentedAlternatives_NameExistingModels()
    {
        // Arrange: read the compiled README sample
        var (_, source) = await ReadAsync();
        var sample = DocSampleText.Between(source, "// <sample:readme>", "// </sample:readme>");

        // Act / Assert: the sample names both alternatives as comments
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
    public Task WordSense_Readme_BasicOutput_MatchesReadme() => CheckOutputAsync("basic", "basic", null, BasicConfidenceTolerance, false);

    /// <summary>
    ///     The documented MiniLM output of the user guide equals the output of the sample run against the real model;
    ///     the confidences match within <see cref="EncoderConfidenceTolerance"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public Task WordSense_Readme_MiniLMOutput_MatchesReadme() => CheckOutputAsync("minilm", "minilm", "all-MiniLM-L6-v2-int8", EncoderConfidenceTolerance, true);

    /// <summary>
    ///     The documented BertBaseCased output of the user guide equals the output of the sample run against the real
    ///     model; the confidences match within <see cref="EncoderConfidenceTolerance"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public Task WordSense_Readme_BertBaseCasedOutput_MatchesReadme() => CheckOutputAsync("bertbasecased", "bertbasecased", "bert-base-cased-int8", EncoderConfidenceTolerance, true);

    /// <summary>
    ///     Compares the documented output block of a sample with the output of the sample run.
    /// </summary>
    /// <param name="marker">The name in the <c>&lt;!-- output:NAME --&gt;</c> marker of the documented block.</param>
    /// <param name="modelKey">The key of the model that the sample runs against.</param>
    /// <param name="modelId">The id of the model whose files must be available, or <c>null</c> for Basic.</param>
    /// <param name="tolerance">The largest allowed absolute difference of a documented confidence.</param>
    /// <param name="inGuide">Whether the output block is in the user guide (otherwise in the README).</param>
    /// <returns>A task that completes when the check has run.</returns>
    private static async Task CheckOutputAsync(string marker, string modelKey, string? modelId, decimal tolerance, bool inGuide)
    {
        // Arrange: read the documented output, require the model files and create the sample
        var document = inGuide ? await ReadGuideAsync() : (await ReadAsync()).Readme;
        var documented = DocSampleText.FencedBlock(document, $"<!-- output:{marker} -->", "text");
        FailWhenNotGenerated(documented);
        if (modelId is not null)
        {
            DocSampleText.RequireModelDirectory(modelId);
        }

        var sample = new ReadmeSample();

        // Act: run the sample with the invariant culture
        await RunInvariantAsync(() => sample.RunAsync(modelKey));

        // Assert: the README is the expected value: the words, parts of speech must match exactly
        DocSampleText.AssertSampleOutput(documented, sample.Output, tolerance);
    }

    /// <summary>
    ///     Fails the test when a documented output block still holds the placeholder text.
    /// </summary>
    /// <param name="documented">The documented output block.</param>
    private static void FailWhenNotGenerated(string documented)
    {
        if (documented.Contains("TBD", StringComparison.Ordinal))
        {
            Assert.Fail("The documented output has not been generated yet: replace the TBD text with the real output.");
        }
    }

    /// <summary>
    ///     Runs an action with the invariant culture as the current culture and restores the previous culture.
    /// </summary>
    /// <param name="run">The action to run.</param>
    /// <returns>A task that completes when the action has completed.</returns>
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
