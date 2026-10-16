namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Doc-check tests that keep the C# samples of the user guide compiling and equal to the documented code.
/// </summary>
/// <remarks>
///     <c>docs/user_guide/introduction.md</c> marks each sample with <c>&lt;!-- sample:name --&gt;</c> before its
///     code block. The block must equal the region of <c>UserGuideSamples.cs</c> between the
///     <c>&lt;sample:name&gt;</c> markers, which the compiler checks. Samples that need no download are also run.
/// </remarks>
public class UserGuideSampleTests
{
    private static readonly string[] SampleNames =
        ["quick-start", "text", "confidence", "pronunciation", "candidates", "tuning", "download", "mirror", "errors"];

    /// <summary>
    ///     The user guide code blocks equal the compiled sample code.
    /// </summary>
    /// <param name="name">The sample name.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("quick-start")]
    [InlineData("text")]
    [InlineData("confidence")]
    [InlineData("pronunciation")]
    [InlineData("candidates")]
    [InlineData("tuning")]
    [InlineData("download")]
    [InlineData("mirror")]
    [InlineData("errors")]
    public async Task WordSense_UserGuide_CodeBlock_MatchesCompiledSample(string name)
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var guide = await File.ReadAllTextAsync(Path.Combine(root, "docs", "user_guide", "introduction.md"), token);
        var source = await File.ReadAllTextAsync(
            Path.Combine(root, "test", "DemaConsulting.WordSense.Tests", "UserGuideSamples.cs"), token);

        var documented = DocSampleText.Normalize(DocSampleText.FencedBlock(guide, $"<!-- sample:{name} -->", "csharp"));
        var compiled = DocSampleText.Normalize(
            DocSampleText.Dedent(DocSampleText.Between(source, $"// <sample:{name}>", $"// </sample:{name}>")));

        Assert.Equal(compiled, documented);
    }

    /// <summary>
    ///     Every sample marked in the user guide is one of the checked samples.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_UserGuide_SampleMarkers_AreAllChecked()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var guide = await File.ReadAllTextAsync(
            Path.Combine(root, "docs", "user_guide", "introduction.md"), TestContext.Current.CancellationToken);

        Assert.Equal(SampleNames.Order(), DocSampleText.SampleNames(guide).Order());
    }

    /// <summary>
    ///     The samples that need only a classifier run against the committed model without an error.
    /// </summary>
    /// <returns>A task that completes when the samples have run.</returns>
    [Fact]
    public async Task WordSense_UserGuide_Samples_RunAgainstModel()
    {
        var location = WordSenseModelLocation.FromDirectory(ModelAssets.RequireModelDirectory());
        using var classifier = await WordSenseClassifier.CreateAsync(
            new WordSenseClassifierOptions { ModelDirectory = location.Directory },
            TestContext.Current.CancellationToken);

        await UserGuideSamples.TextAsync(classifier);
        await UserGuideSamples.ConfidenceAsync(classifier);
        await UserGuideSamples.PronunciationAsync(classifier);
        UserGuideSamples.Candidates(classifier);
        Assert.NotNull(UserGuideSamples.Mirror().Downloader?.Mirror);
    }
}
