namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicWordSenseModel"/>.
/// </summary>
public class BasicWordSenseModelTests
{
    /// <summary>
    ///     Making the model ready needs no resources and completes immediately.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_CompletesImmediately()
    {
        var model = new BasicWordSenseModel();

        var task = model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    /// <summary>
    ///     The model creates a classifier that answers.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Create_ReturnsWorkingClassifier()
    {
        IWordSenseModel model = new BasicWordSenseModel();

        var results = await model.Create().ClassifyAsync(["Hello", "world"], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }
}
