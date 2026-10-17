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
    ///     A lexicon-only call needs no model files, so it works on a model that was never ensured.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_ClassifyAsync_LexiconOnly_NeedsNoEnsure()
    {
        using var model = new MiniLMWordSenseModel();

        var senses = await model.ClassifyAsync(["the", "."], TestContext.Current.CancellationToken);

        Assert.All(senses, s => Assert.Null(s.Details));
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

    /// <summary>
    ///     A disposed model rejects <c>ClassifyAsync</c> and disposing again is harmless.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_ClassifyAsync_AfterDispose_Throws()
    {
        var model = new MiniLMWordSenseModel();
        model.Dispose();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The defaults construct a model with the per-user store and a 128-token window; the limits are accepted.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Constructor_Defaults_UseStoreAnd128()
    {
        using var defaults = new MiniLMWordSenseModel();
        using var smallest = new MiniLMWordSenseModel(null, 8);
        using var largest = new MiniLMWordSenseModel(null, 512);

        Assert.Equal(384, defaults.HiddenSize);
        Assert.Equal(384, smallest.HiddenSize);
        Assert.Equal(384, largest.HiddenSize);
    }

    /// <summary>
    ///     A directory with the pinned files is accepted without network access.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesPinnedFiles()
    {
        using var model = new MiniLMWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The same text always gives the same classification, and the ambiguous word is resolved.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_AmbiguousWord_ReturnsDeterministicResults()
    {
        using var model = new MiniLMWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        string[] words = ["They", "will", "record", "the", "record", "."];

        var first = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);
        var second = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(PartOfSpeech.Verb, first[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, first[4].Details?.PartOfSpeech);
    }
}
