namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     End-to-end test of the BertBaseCased model. It runs on the committed model files and is skipped when they are
///     absent (for example a checkout without Git LFS content).
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the"; each answer states its level and is
    ///     never a group, and the period is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_ClassifiesRecord()
    {
        // Arrange: the model on the committed model files, verified up front
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
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
}
