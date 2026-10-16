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
        ["quick-start", "outcomes", "text", "confidence", "pronunciation", "candidates", "tuning", "lexicon", "download",
            "mirror", "errors"];

    /// <summary>
    ///     Gets the sample names as theory data, so the theory covers exactly the checked samples.
    /// </summary>
    public static TheoryData<string> SampleNameData => new(SampleNames);

    /// <summary>
    ///     The user guide code blocks equal the compiled sample code.
    /// </summary>
    /// <param name="name">The sample name.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [MemberData(nameof(SampleNameData))]
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

        await UserGuideSamples.OutcomesAsync(classifier);

        // The outcomes sample documents these results, so check them rather than only that the sample ran
        var outcomes = await classifier.ClassifyAsync(
            ["The", "hunter", "will", "record", "the", "record"], TestContext.Current.CancellationToken);
        Assert.Equal(AnswerBasis.None, outcomes[0].Basis);
        Assert.Equal(AnswerBasis.Dictionary, outcomes[1].Basis);
        Assert.NotNull(outcomes[1].Details);
        Assert.Equal(AnswerBasis.Dictionary, outcomes[2].Basis);
        Assert.Null(outcomes[2].Details);
        Assert.True(outcomes[2].Abstained);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], outcomes[2].Candidates.Select(c => c.PartOfSpeech).Order());
        Assert.Equal(AnswerBasis.Context, outcomes[3].Basis);
        Assert.Equal(AnswerBasis.Context, outcomes[5].Basis);
        // A context basis without details is the context-abstention outcome: abstained, with the margin still set
        Assert.All(outcomes.Where(o => o is { Basis: AnswerBasis.Context, Details: null }), o => Assert.True(o.Abstained));
        Assert.Equal(
            ["noun", "verb"],
            classifier.GetCandidates("record").Select(c => c.Pronunciation!.Meaning).Order());
        Assert.Equal(
            ["bend", "bend", "weapon"],
            classifier.GetCandidates("bow").Select(c => c.Pronunciation!.Meaning).Order());
        await UserGuideSamples.TextAsync(classifier);
        await UserGuideSamples.ConfidenceAsync(classifier);
        await UserGuideSamples.PronunciationAsync(classifier);
        UserGuideSamples.Candidates(classifier);
        Assert.NotNull(UserGuideSamples.Mirror().Downloader?.Mirror);
    }
}
