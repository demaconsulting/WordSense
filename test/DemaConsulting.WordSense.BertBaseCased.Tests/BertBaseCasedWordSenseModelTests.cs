namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Tests for <see cref="BertBaseCasedWordSenseModel"/>. Most need no model files and no network access; the two
///     that classify with the real model are skipped when the Git LFS files are absent.
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
    ///     A lexicon-only call needs no model files, so it works on a model that was never ensured.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_ClassifyAsync_LexiconOnly_NeedsNoEnsure()
    {
        using var model = new BertBaseCasedWordSenseModel();

        var senses = await model.ClassifyAsync(["The", "."], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Determiner, senses[0].Details!.PartOfSpeech);
        Assert.Null(senses[1].Details);
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
    ///     The identity names the cased model: the vocabulary is cased, so the case of a word is part of its context.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Identity_IsCased()
    {
        using var model = new BertBaseCasedWordSenseModel();

        Assert.False(model.Spec.LowerCase);
        Assert.Equal(768, model.Spec.HiddenSize);
    }

    /// <summary>
    ///     The model pins exactly its four files, each by a SHA-256 digest.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Files_ArePinnedBySha256()
    {
        using var model = new BertBaseCasedWordSenseModel();

        Assert.Equal(["config.json", "model_int8.onnx", "tokenizer.json", "vocab.txt"], model.Spec.Files.Select(f => f.Name).Order().ToArray());
        Assert.All(model.Spec.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256.ToLowerInvariant()));
    }

    /// <summary>
    ///     A disposed model rejects <c>EnsureModelAsync</c>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_EnsureModelAsync_AfterDispose_Throws()
    {
        var model = new BertBaseCasedWordSenseModel();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A disposed model rejects <c>ClassifyAsync</c> and disposing again is harmless.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_ClassifyAsync_AfterDispose_Throws()
    {
        var model = new BertBaseCasedWordSenseModel();
        model.Dispose();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
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

        Assert.NotNull(await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
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

        var result = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, result[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, result[4].Details?.PartOfSpeech);
    }
}
