using System.Globalization;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Doc-check tests that keep the README usage samples and their documented output from drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> marks each sample with <c>&lt;!-- sample:name --&gt;</c> before its code block and
///     <c>&lt;!-- output:name --&gt;</c> before its output block. The code block must equal the region of
///     <c>ReadmeSample.cs</c> between <c>&lt;sample:name&gt;</c> markers (which is compiled and run here against the
///     committed model), and the output block must equal what that code writes.
/// </remarks>
public class ReadmeSampleTests
{
    /// <summary>
    ///     The README code blocks equal the compiled sample code.
    /// </summary>
    /// <param name="name">The sample name.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("download")]
    [InlineData("classify")]
    [InlineData("tags")]
    public async Task WordSense_Readme_CodeBlock_MatchesCompiledSample(string name)
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), token);
        var source = await File.ReadAllTextAsync(
            Path.Combine(root, "test", "DemaConsulting.WordSense.Tests", "ReadmeSample.cs"), token);

        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(readme, $"<!-- sample:{name} -->", "csharp"));
        var compiled = DocSampleText.Normalize(DocSampleText.Dedent(DocSampleText.Between(source, $"// <sample:{name}>", $"// </sample:{name}>")));

        Assert.Equal(compiled, documented);
    }

    /// <summary>
    ///     Every sample marked in the README is one of the checked samples.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_SampleMarkers_AreAllChecked()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), TestContext.Current.CancellationToken);

        Assert.Equal(["classify", "download", "tags"], DocSampleText.SampleNames(readme).Order());
    }

    /// <summary>
    ///     The documented output equals the output of the sample run against the real model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_Output_MatchesReadme()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var location = WordSenseModelLocation.FromDirectory(ModelAssets.RequireModelDirectory());
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), TestContext.Current.CancellationToken);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        ReadmeSample classify;
        ReadmeSample tags;
        try
        {
            classify = new ReadmeSample();
            await classify.ClassifyAsync(location);

            using var classifier = await WordSenseClassifier.CreateAsync(
                new WordSenseClassifierOptions { ModelDirectory = location.Directory },
                TestContext.Current.CancellationToken);
            tags = new ReadmeSample();
            await tags.TagsAsync(classifier);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(DocSampleText.Normalize(classify.Output), DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- output:classify -->", "text")));
        Assert.Equal(DocSampleText.Normalize(tags.Output), DocSampleText.Normalize(DocSampleText.FencedBlock(readme, "<!-- output:tags -->", "text")));
    }
}
