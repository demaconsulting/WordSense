namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     End-to-end test of the BertBaseCased model. It runs the real model and is skipped when the model files are
///     absent (for example a checkout without Git LFS content).
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the".
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_ClassifiesRecord()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var classifier = model.Create();

        var senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        Assert.Equal(6, senses.Length);
        Assert.Equal(PartOfSpeech.Verb, senses[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, senses[4].Details?.PartOfSpeech);
        Assert.All(senses, s => Assert.InRange(s.Confidence, 0.0, 1.0));
    }
}
