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

    /// <summary>
    ///     The defaults construct a model with the per-user store and a 128-token window; the limits are accepted.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Constructor_Defaults_UseStoreAnd128()
    {
        using var defaults = new BertBaseCasedWordSenseModel();
        using var smallest = new BertBaseCasedWordSenseModel(null, 8);
        using var largest = new BertBaseCasedWordSenseModel(null, 512);

        Assert.Equal(768, defaults.HiddenSize);
        Assert.Equal(768, smallest.HiddenSize);
        Assert.Equal(768, largest.HiddenSize);
    }

    /// <summary>
    ///     The identity names the cased model and its hidden size.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_ModelIdAndHiddenSize_AreBertBaseCased()
    {
        using var model = new BertBaseCasedWordSenseModel();

        Assert.Equal("bert-base-cased-int8", model.ModelId);
    }

    /// <summary>
    ///     A disposed model rejects <c>Create</c> and disposing again is harmless.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Create_AfterDispose_Throws()
    {
        var model = new BertBaseCasedWordSenseModel();
        model.Dispose();
        model.Dispose();

        Assert.Throws<ObjectDisposedException>(model.Create);
    }

    /// <summary>
    ///     A directory with the pinned files is accepted without network access.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesPinnedFiles()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(model.Create());
    }

    /// <summary>
    ///     An ambiguous word is resolved to its best sense from the context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_Classify_AmbiguousWord_ReturnsBestSense()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var classifier = model.Create();

        var result = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, result[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, result[4].Details?.PartOfSpeech);
    }
}
