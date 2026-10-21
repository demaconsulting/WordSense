using WordSense.Builder;

namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Runs the reviewed suite (<c>corpus/suite</c>) against the BertBaseCased model: every row that the model is expected
///     to keep must still pass, so a regression fails the build while the known misses stay documented in
///     <c>corpus/suite/expectations.tsv</c>. The test is skipped when the model files are not available (for example a
///     checkout without Git LFS content).
/// </summary>
public class BertBaseCasedSuiteTests
{
    /// <summary>
    ///     No row falls below the expectation of the BertBaseCased model.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task BertBaseCasedSuite_ExpectedPasses_StillPass()
    {
        // Arrange
        var suite = SuiteFile.Read(SuiteFile.Locate());
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var grades = await SuiteFile.GradeAsync(model, suite.Rows, TestContext.Current.CancellationToken);

        // Assert
        var regressions = SuiteFile.Regressions(suite.Rows, suite.Expectations["bert-base-cased"], grades);
        Assert.True(regressions.Count == 0, $"{regressions.Count} suite row(s) of the BertBaseCased model are below their expectation: {string.Join("; ", regressions)}");
    }
}
