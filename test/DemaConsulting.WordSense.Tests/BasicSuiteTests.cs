using WordSense.Builder;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Runs the reviewed suite (<c>corpus/suite</c>) against the Basic model: every row that the model is expected to keep
///     must still pass, so a regression fails the build while the known misses stay documented in
///     <c>corpus/suite/expectations.tsv</c>.
/// </summary>
/// <remarks>
///     The expectation of a row is raised in a reviewed commit when the model improves; it is never lowered to hide a
///     regression (see <c>corpus/suite/README.md</c>). The reader and the grading rule are those of the builder
///     (<c>tools/WordSense.Builder/Suite.cs</c> is compiled into this project).
/// </remarks>
public class BasicSuiteTests
{
    /// <summary>
    ///     No row falls below the expectation of the Basic model.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task BasicSuite_ExpectedPasses_StillPass()
    {
        // Arrange
        var suite = SuiteFile.Read(SuiteFile.Locate());
        using var model = new BasicWordSenseModel();

        // Act
        var grades = await SuiteFile.GradeAsync(model, suite.Rows, TestContext.Current.CancellationToken);

        // Assert
        var regressions = SuiteFile.Regressions(suite.Rows, suite.Expectations["basic"], grades);
        Assert.True(regressions.Count == 0, $"{regressions.Count} suite row(s) of the Basic model are below their expectation: {string.Join("; ", regressions)}");
    }
}
