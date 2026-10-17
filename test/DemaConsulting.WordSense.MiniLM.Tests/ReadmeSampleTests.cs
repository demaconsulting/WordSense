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
    private const string MiniLMUsings = "using DemaConsulting.WordSense;\nusing DemaConsulting.WordSense.MiniLM;\n\n";
    private const string BasicUsings = "using DemaConsulting.WordSense;\nusing DemaConsulting.WordSense.Basic;\n\n";

    private static async Task<(string Readme, string Source)> ReadAsync()
    {
        var root = DocSampleText.FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        return (
            await File.ReadAllTextAsync(Path.Combine(root, "README.md"), token),
            await File.ReadAllTextAsync(Path.Combine(root, "test", "DemaConsulting.WordSense.MiniLM.Tests", "ReadmeSample.cs"), token));
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

        Assert.Equal(Compiled(source, "readme", MiniLMUsings), DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:readme -->", "csharp")));
        Assert.Equal(Compiled(source, "basic", BasicUsings), DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:basic -->", "csharp")));
    }

    /// <summary>
    ///     The README marks exactly the two samples that are checked.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_SampleMarkers_AreAllChecked()
    {
        var (readme, _) = await ReadAsync();

        Assert.Equal(["readme", "basic"], DocSampleText.SampleNames(readme));
    }

    /// <summary>
    ///     The documented MiniLM output equals the output of the sample run against the real model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_MiniLMOutput_MatchesReadme()
    {
        var (readme, _) = await ReadAsync();
        var documented = DocSampleText.FencedBlock(readme, "<!-- output:readme -->", "text");
        FailWhenNotGenerated(documented);
        DocSampleText.RequireMiniLMDirectory();

        var sample = new ReadmeSample();
        await RunInvariantAsync(sample.RunMiniLMAsync);

        Assert.Equal(DocSampleText.Normalize(sample.Output), DocSampleText.Normalize(documented));
    }

    /// <summary>
    ///     The documented Basic output equals the output of the sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_BasicOutput_MatchesReadme()
    {
        var (readme, _) = await ReadAsync();
        var documented = DocSampleText.FencedBlock(readme, "<!-- output:basic -->", "text");
        FailWhenNotGenerated(documented);

        var sample = new ReadmeSample();
        await RunInvariantAsync(sample.RunBasicAsync);

        Assert.Equal(DocSampleText.Normalize(sample.Output), DocSampleText.Normalize(documented));
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
