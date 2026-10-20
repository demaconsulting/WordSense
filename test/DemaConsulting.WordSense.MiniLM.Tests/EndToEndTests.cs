namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     End-to-end test of the MiniLM model: the same test runs on every platform and runtime of the build matrix, which
///     makes it the evidence for the platform requirements and for the ONNX Runtime package.
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the"; each answer states its level and is
    ///     never a group, and the period is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ClassifiesRecord()
    {
        // Arrange: the model on the committed model files, verified up front
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert: one result per word, the two uses of "record" told apart, the period unknown
        Assert.Equal(6, results.Length);
        Assert.Equal(PartOfSpeech.Verb, results[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, results[4].Details?.PartOfSpeech);
        Assert.Equal(SenseLevel.None, results[5].Level);
        Assert.Null(results[5].Details);
        Assert.All(results, r => Assert.InRange(r.Confidence, 0.0, 1.0));
        Assert.All(results, r => Assert.NotEqual(SenseLevel.Group, r.Level));
    }

    /// <summary>
    ///     Every result carries its candidate analyses, the selected analysis, a level and, at the sense level, the
    ///     sense id: the determiner "the" has one sense, so it is answered at the sense level.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ResultCarriesAnalysisLevelAndSense()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert: the one-sense determiner is a sense answer with its id, an unknown token has no analysis
        var the = results[3];
        Assert.True(the.IsKnown);
        Assert.Equal(SenseLevel.Sense, the.Level);
        Assert.Equal("the#determiner", the.SenseId);
        Assert.Null(the.GroupId);
        Assert.NotNull(the.Details);
        Assert.Equal(1.0, the.Confidence, 6);
        Assert.False(results[5].IsKnown);
        Assert.Empty(results[5].Analyses);
        Assert.All(results.Where(r => r.IsKnown), r => Assert.NotEmpty(r.Analyses));
    }

    /// <summary>
    ///     A word with several meanings that the model does not score is a known word, answered at the part-of-speech
    ///     level (never none) with its analysis and no sense or group id.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_UntrainedWord_AnswersPartOfSpeech()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert
        var record = results[4];
        Assert.True(record.IsKnown);
        Assert.NotNull(record.Details);
        Assert.Equal(SenseLevel.PartOfSpeech, record.Level);
        Assert.Null(record.SenseId);
        Assert.Null(record.GroupId);
        Assert.InRange(record.Confidence, 0.0, 1.0);
    }

    /// <summary>
    ///     The analyses of a word are sorted by probability, largest first, and sum to one; the selected analysis is
    ///     the first of them.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_AnalysesAreRankedAndSumToOne()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert
        foreach (var result in results.Where(r => r.IsKnown))
        {
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 6);
            for (var i = 1; i < result.Analyses.Length; i++)
            {
                Assert.True(result.Analyses[i - 1].Probability >= result.Analyses[i].Probability);
            }

            Assert.Equal(result.Analyses[0].Details, result.Details);
        }
    }
}
