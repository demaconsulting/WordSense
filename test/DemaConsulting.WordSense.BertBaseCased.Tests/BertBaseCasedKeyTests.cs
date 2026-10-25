namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Checks that every key that the BertBaseCased model gives is a member of <c>WordLexicon.Keys</c>. The test is
///     skipped when the model files are not available (for example a checkout without Git LFS content).
/// </summary>
public class BertBaseCasedKeyTests
{
    /// <summary>
    ///     The keys of the words of the suite and of a sample of corpus sentences are all enumerated.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task BertBaseCasedKeys_SuiteAndCorpusSentences_AreAllEnumerated()
    {
        // Arrange
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var problems = await DemaConsulting.WordSense.Tests.KeyCompleteness.ProblemsAsync(model, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(problems.Count == 0, $"{problems.Count} key problem(s): {string.Join("; ", problems.Take(20))}");
    }
}
