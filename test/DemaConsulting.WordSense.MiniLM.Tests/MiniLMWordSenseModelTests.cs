namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Quick tests for <see cref="MiniLMWordSenseModel"/>. They need no model files and no network access.
/// </summary>
public class MiniLMWordSenseModelTests
{
    /// <summary>
    ///     The pinned model identity is available before the files are.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Identity_IsPinned()
    {
        using var model = new MiniLMWordSenseModel();

        Assert.Equal("all-MiniLM-L6-v2-int8", model.ModelId);
        Assert.Equal(384, model.HiddenSize);
    }

    /// <summary>
    ///     A sequence length outside 8 to 512 is rejected.
    /// </summary>
    /// <param name="length">The sequence length.</param>
    [Theory]
    [InlineData(7)]
    [InlineData(513)]
    public void MiniLMWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMWordSenseModel(null, length));
    }

    /// <summary>
    ///     Creating a classifier before the files are available is an error that names the remedy.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Create_BeforeEnsureModel_Throws()
    {
        using var model = new MiniLMWordSenseModel();

        var ex = Assert.Throws<InvalidOperationException>(model.Create);

        Assert.Contains("EnsureModelAsync", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A disposed model rejects use.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EnsureModelAsync_AfterDispose_Throws()
    {
        var model = new MiniLMWordSenseModel();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }
}
