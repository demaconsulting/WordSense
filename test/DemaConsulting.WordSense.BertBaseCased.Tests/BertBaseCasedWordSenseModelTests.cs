namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Quick tests for <see cref="BertBaseCasedWordSenseModel"/>. They need no model files and no network access.
/// </summary>
public class BertBaseCasedWordSenseModelTests
{
    /// <summary>
    ///     The pinned model identity is available before the files are.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Identity_IsPinned()
    {
        using var model = new BertBaseCasedWordSenseModel();

        Assert.Equal("bert-base-cased-int8", model.ModelId);
        Assert.Equal(768, model.HiddenSize);
    }

    /// <summary>
    ///     A sequence length outside 8 to 512 is rejected.
    /// </summary>
    /// <param name="length">The sequence length.</param>
    [Theory]
    [InlineData(7)]
    [InlineData(513)]
    public void BertBaseCasedWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BertBaseCasedWordSenseModel(null, length));
    }

    /// <summary>
    ///     Creating a classifier before the files are available is an error that names the remedy.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Create_BeforeEnsureModel_Throws()
    {
        using var model = new BertBaseCasedWordSenseModel();

        var ex = Assert.Throws<InvalidOperationException>(model.Create);

        Assert.Contains("EnsureModelAsync", ex.Message, StringComparison.Ordinal);
    }
}
