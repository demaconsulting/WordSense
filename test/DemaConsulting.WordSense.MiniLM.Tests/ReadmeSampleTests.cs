using System.Globalization;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Doc-check tests that keep the README usage samples and their documented output from drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> marks its usage sample with <c>&lt;!-- sample:readme --&gt;</c> before the code block and
///     <c>&lt;!-- output:readme --&gt;</c> before the output block. The code block must equal the using directive plus
///     the region of <c>ReadmeSample.cs</c> between the <c>&lt;sample:readme&gt;</c> markers (which is compiled and run
///     here against the committed model), and the output block must equal what that code writes.
/// </remarks>
public class ReadmeSampleTests
{
    private const string Usings = "using DemaConsulting.WordSense.MiniLM;\n\n";

    /// <summary>
    ///     The README code block equals the compiled sample code.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_CodeBlock_MatchesCompiledSample()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), token);
        var source = await File.ReadAllTextAsync(
            Path.Combine(root, "test", "DemaConsulting.WordSense.MiniLM.Tests", "ReadmeSample.cs"), token);

        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- sample:readme -->", "csharp"));
        var compiled = DocSampleText.Normalize(
            Usings + DocSampleText.Dedent(DocSampleText.Between(source, "// <sample:readme>", "// </sample:readme>")));

        Assert.Equal(compiled, documented);
    }

    /// <summary>
    ///     The README marks exactly one sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_SampleMarkers_AreAllChecked()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), TestContext.Current.CancellationToken);

        Assert.Equal(["readme"], DocSampleText.SampleNames(readme));
    }

    /// <summary>
    ///     The documented output equals the output of the sample run against the real model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_Output_MatchesReadme()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), TestContext.Current.CancellationToken);
        var sample = new ReadmeSample();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await sample.RunAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(
            DocSampleText.Normalize(sample.Output),
            DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- output:readme -->", "text")));
    }
}
