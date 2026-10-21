namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Tests for <see cref="BertBaseCasedWordSenseModel"/>. Most need no model files and no network access; the two
///     that classify with the real model are skipped when the Git LFS files are absent.
/// </summary>
public class BertBaseCasedWordSenseModelTests
{
    private static readonly string[] Sentence = ["They", "will", "shelter", "the", "shelter", "."];

    private static BertBaseCasedWordSenseModel Open(string? directory = null)
    {
        var model = new BertBaseCasedWordSenseModel(directory);
        model.Encoder.IntraOpThreads = 2;
        return model;
    }

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

        Assert.False(model.Encoder.Spec.LowerCase);
        Assert.Equal(768, model.Encoder.Spec.HiddenSize);
    }

    /// <summary>
    ///     The model pins exactly its four files, each by a SHA-256 digest.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Files_ArePinnedBySha256()
    {
        using var model = new BertBaseCasedWordSenseModel();

        Assert.Equal(["config.json", "model_int8.onnx", "tokenizer.json", "vocab.txt"], model.Encoder.Spec.Files.Select(f => f.Name).Order().ToArray());
        Assert.All(model.Encoder.Spec.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256.ToLowerInvariant()));
    }

    /// <summary>
    ///     An ambiguous word that the encoder does not score is answered by the Basic fallback from the context, at the
    ///     part of speech level: a verb after "will", a noun after "the", the analyses summing to 1 and no scored sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback()
    {
        // Arrange: no model files are needed, the real Basic model is the fallback
        using var model = new BertBaseCasedWordSenseModel();

        // Act
        var results = await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert: the context decides the analysis, as the Basic model does
        Assert.Equal(PartOfSpeech.Verb, results[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, results[4].Details?.PartOfSpeech);
        foreach (var index in new[] { 2, 4 })
        {
            var result = results[index];
            Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
            Assert.Null(result.SenseId);
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
            Assert.True(result.Analyses.Length > 1);
            Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
            Assert.Equal(result.Details, result.Analyses[0].Details);
        }

        // The answer is the one of the Basic model, not the uniform answer of the encoder model
        using var basic = new BasicWordSenseModel();
        var expected = await basic.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        Assert.Equal(expected[2], results[2]);
        Assert.Equal(expected[4], results[4]);
    }

    /// <summary>
    ///     The assembly builds on the core and the Onnx packages and on nothing else of the WordSense family:
    ///     besides the framework and the ONNX Runtime it references exactly those two (the Basic model is in the core).
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSenseModel_Assembly_ReferencesCoreAndOnnx()
    {
        // Arrange: the names of the assemblies that the BertBaseCased assembly references
        var references = typeof(BertBaseCasedWordSenseModel).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .ToList();

        // Act: keep the assemblies that are neither the framework nor the ONNX Runtime
        var others = references
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !string.Equals(name, "mscorlib", StringComparison.Ordinal)
                && !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.Equal(["DemaConsulting.WordSense", "DemaConsulting.WordSense.Onnx"], others);
    }

    /// <summary>
    ///     Disposing the model disposes both the encoder model and the Basic fallback model, so both reject further calls.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_Dispose_DisposesEncoderAndFallback()
    {
        // Arrange: both models are usable while the model lives; a trained lemma makes the encoder open its session
        var model = Open(ModelFolder.Require());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        Assert.Single(await model.Encoder.ClassifyAsync(["back"], TestContext.Current.CancellationToken));
        Assert.False(model.Encoder.IsEncoderReleased);
        Assert.Single(await model.Fallback.ClassifyAsync(["record"], TestContext.Current.CancellationToken));

        // Act
        model.Dispose();

        // Assert: the encoder is released and both reject calls
        Assert.True(model.Encoder.IsEncoderReleased);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.Encoder.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.Fallback.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
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
        using var model = Open(ModelFolder.Require());

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The two uses of an ambiguous word get their part of speech from the context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_Classify_AmbiguousWord_ReturnsBestSense()
    {
        using var model = Open(ModelFolder.Require());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var result = await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, result[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, result[4].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A trained lemma in a clear sentence is answered by the encoder at the sense or the group level, with the ids
    ///     of the catalog, a probability for every analysis and its senses ranked inside the selected analysis.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCasedWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel()
    {
        // Arrange: "back" is a trained lemma; the sentence is about going back to a place
        using var model = Open(ModelFolder.Require());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        // Act
        var results = await model.ClassifyAsync(["I", "will", "go", "back", "to", "the", "house", "."], TestContext.Current.CancellationToken);

        // Assert: the trained word is answered with a sense or a group of its lemma, and lists its ranked senses
        var back = results[3];
        Assert.True(back.Level is SenseLevel.Sense or SenseLevel.Group, $"The level was {back.Level}.");
        Assert.Equal("back", back.Details?.Lemma);
        Assert.InRange(back.Confidence, 0.0, 1.0);
        var selected = back.Analyses.First(a => a.Details.Equals(back.Details));
        Assert.False(selected.Senses.IsEmpty);
        Assert.Equal(1.0, selected.Senses.Sum(s => s.Probability), 6);
        Assert.Equal(1.0, back.Analyses.Sum(a => a.Probability), 6);
        if (back.Level == SenseLevel.Sense)
        {
            Assert.True(WordLexicon.TryGetSense(back.SenseId!, out var sense));
            Assert.Equal("back", sense.Lemma);
            Assert.Equal(sense.GroupId, back.GroupId);
        }
        else
        {
            Assert.Null(back.SenseId);
            Assert.True(WordLexicon.TryGetGroup(back.GroupId!, out var group));
            Assert.Equal("back", group.Lemma);
        }
    }
}
