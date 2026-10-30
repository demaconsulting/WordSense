namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Tests for <see cref="MiniLMWordSenseModel"/>. Most need no model files and no network access; those that
///     classify a word of several parts of speech use the real model files and are skipped when the Git LFS files are
///     absent.
/// </summary>
public class MiniLMWordSenseModelTests
{
    private static readonly string[] Sentence = ["They", "will", "release", "the", "release", "."];

    private static MiniLMWordSenseModel Open(string? directory = null)
    {
        var model = new MiniLMWordSenseModel(directory);
        model.Encoder.IntraOpThreads = 2;
        return model;
    }

    /// <summary>
    ///     The pinned model identity is available before the files are.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Identity_IsPinned()
    {
        using var model = new MiniLMWordSenseModel();

        Assert.Equal("all-MiniLM-L6-v2-int8", model.ModelId);
        Assert.Equal(384, model.HiddenSize);
        Assert.True(model.Encoder.Spec.LowerCase);
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

        var senses = await model.ClassifyAsync(["The", "."], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Determiner, senses[0].Details!.PartOfSpeech);
        Assert.Null(senses[1].Details);
    }

    /// <summary>
    ///     An ambiguous word that the encoder does not score is answered at the part of speech level with the head of the
    ///     encoder and the Basic fallback combined: a verb after "will", a noun after "the", the analyses summing to 1 and
    ///     no scored sense (the senses are ranked by usage).
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersAtThePartOfSpeechLevel()
    {
        // Arrange: the encoder decides the part of speech, the real Basic model is the fallback
        using var model = Open(ModelFolder.Require());

        // Act
        var results = await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert: the context decides the analysis
        Assert.Equal(PartOfSpeech.Verb, results[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, results[4].Details?.PartOfSpeech);
        foreach (var index in new[] { 2, 4 })
        {
            var result = results[index];
            Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
            Assert.Null(result.SenseId);
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
            Assert.True(result.Analyses.Length > 1);
            Assert.True(result.Analyses[0].Senses.Length >= 2, "The selected analysis lists its senses ranked by usage.");
            Assert.Equal(result.Details, result.Analyses[0].Details);
        }
    }

    /// <summary>
    ///     The selected analysis of an untrained polysemous word lists the senses of its lemma and part of speech ranked
    ///     by the usage prior of the Basic model, whatever the encoder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_UntrainedPolysemousWord_ListsSensesRankedByUsagePrior()
    {
        // Arrange: the encoder decides the part of speech, the real Basic model is the fallback for the senses
        using var model = Open(ModelFolder.Require());

        // Act
        var result = (await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[4];

        // Assert
        var details = Assert.IsType<WordDetails>(result.Details);
        var selected = result.Analyses.First(a => a.Details.Equals(details));
        Assert.Equal(UsagePrior.RankOf(details.Lemma, details.PartOfSpeech), selected.Senses);
        Assert.True(selected.Senses.Length >= 2);
    }

    /// <summary>
    ///     The ranking is never an answer: an untrained polysemous word is answered at the part-of-speech level with no
    ///     sense id and no group id.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_UntrainedPolysemousWord_AnswersPartOfSpeechLevelWithoutSenseOrGroup()
    {
        // Arrange
        using var model = Open(ModelFolder.Require());

        // Act
        var result = (await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[4];

        // Assert
        Assert.True(result.Analyses.First(a => a.Details.Equals(result.Details)).Senses.Length >= 2);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.SenseId);
        Assert.Null(result.GroupId);
    }

    /// <summary>
    ///     The key of an untrained polysemous word carries the label of the top-ranked sense of its selected analysis.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_UntrainedPolysemousWord_KeyCarriesTheLabelOfTheTopRankedSense()
    {
        // Arrange
        using var model = Open(ModelFolder.Require());

        // Act
        var result = (await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[4];
        var top = result.Analyses.First(a => a.Details.Equals(result.Details)).Senses[0].SenseId;

        // Assert
        Assert.EndsWith("#" + top.Split('#')[1], result.Key, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The model pins exactly its two files, each by a SHA-256 digest.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Files_ArePinnedBySha256()
    {
        using var model = new MiniLMWordSenseModel();

        Assert.Equal(["model_int8.onnx", "vocab.txt"], model.Encoder.Spec.Files.Select(f => f.Name).Order().ToArray());
        Assert.All(model.Encoder.Spec.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256.ToLowerInvariant()));
    }

    /// <summary>
    ///     The assembly builds on the core and the Onnx packages and on nothing else of the WordSense family:
    ///     besides the framework and the ONNX Runtime it references exactly those two (the Basic model is in the core).
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Assembly_ReferencesCoreAndOnnx()
    {
        // Arrange: the names of the assemblies that the MiniLM assembly references
        var references = typeof(MiniLMWordSenseModel).Assembly.GetReferencedAssemblies()
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
    ///     The model has the downloadable capability, so the explicit ensure is available through the interface.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_IsDownloadable()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel();

        // Act
        IWordSenseModel contract = model;

        // Assert
        Assert.IsType<IDownloadableWordSenseModel>(contract, exactMatch: false);
    }

    /// <summary>
    ///     A model directory that does not exist is reported by the public ensure as
    ///     <see cref="ModelUnavailableException"/>: the typed exception of the encoder reaches the caller through the
    ///     composition.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EnsureModelAsync_MissingDirectory_ThrowsModelUnavailable()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(Path.Join(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N")));

        // Act
        var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

        // Assert: the original exception is kept
        Assert.IsType<IOException>(failure.InnerException, exactMatch: false);
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
    ///     Disposing the model disposes both the encoder model and the Basic fallback model, so both reject further calls.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Dispose_DisposesEncoderAndFallback()
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
    ///     The weight of the encoder's part-of-speech head in the combination of slot probabilities is the tuned 0.5 by
    ///     default, reaches the composition, and an explicit weight of the internal constructor replaces it; it needs no
    ///     model file.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_PrimaryWeight_IsTunedAndReachesTheComposition()
    {
        // Arrange
        using var defaults = new MiniLMWordSenseModel();
        using var explicitWeight = new MiniLMWordSenseModel(null, 128, 1.0);

        // Act
        var defaultWeight = defaults.PrimaryWeight;
        var explicitValue = explicitWeight.PrimaryWeight;
        var invalid = () => new MiniLMWordSenseModel(null, 128, 1.5);

        // Assert
        Assert.Equal(0.5, MiniLMWordSenseModel.PartOfSpeechHeadWeight);
        Assert.Equal(0.5, defaultWeight);
        Assert.Equal(1.0, explicitValue);
        Assert.Throws<ArgumentOutOfRangeException>(invalid);
    }

    /// <summary>
    ///     The weight decides between the head of the encoder and the Basic tagger for the verb "use" after "not": with
    ///     the weight 1 the slot probabilities are those of the encoder model alone (its head), with the weight 0 they
    ///     are those of the Basic model, and the senses of the encoder stay listed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_VerbUse_FollowsThePrimaryWeight()
    {
        // Arrange
        string[] words = ["Do", "not", "use", "these", "guys", "."];
        using var headOnly = new MiniLMWordSenseModel(ModelFolder.Require(), 128, 1.0);
        headOnly.Encoder.IntraOpThreads = 2;
        using var taggerOnly = new MiniLMWordSenseModel(ModelFolder.Require(), 128, 0.0);
        taggerOnly.Encoder.IntraOpThreads = 2;
        static double Slot(ClassifyResult r, PartOfSpeech p) => r.Analyses.Where(a => a.Details.PartOfSpeech == p).Sum(a => a.Probability);

        // Act
        var encoded = (await headOnly.Encoder.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];
        var basic = (await headOnly.Fallback.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];
        var kept = (await headOnly.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];
        var result = (await taggerOnly.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];

        // Assert: the weight 1 keeps the slot probabilities of the encoder, the weight 0 takes those of Basic (floored at 1e-6)
        Assert.All(encoded.Analyses, a => Assert.Equal(Slot(encoded, a.Details.PartOfSpeech), Slot(kept, a.Details.PartOfSpeech), 6));
        Assert.All(basic.Analyses, a => Assert.Equal(Slot(basic, a.Details.PartOfSpeech), Slot(result, a.Details.PartOfSpeech), 6));
        Assert.Equal(PartOfSpeech.Verb, result.Details?.PartOfSpeech);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
        Assert.Equal(result.Analyses[0].Details, result.Details);
        Assert.False(result.Analyses[0].Senses.IsEmpty);
    }

    /// <summary>
    ///     The default model answers the verb "use" after "not" with the slot probabilities of the encoder's head and of
    ///     the Basic model combined with the weight of the model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_VerbUse_IsAnsweredWithTheCombinedSlotProbabilities()
    {
        // Arrange
        string[] words = ["Do", "not", "use", "these", "guys", "."];
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        static double Slot(ClassifyResult r, PartOfSpeech p) => r.Analyses.Where(a => a.Details.PartOfSpeech == p).Sum(a => a.Probability);

        // Act
        var encoded = (await model.Encoder.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];
        var basic = (await model.Fallback.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];
        var result = (await model.ClassifyAsync(words, TestContext.Current.CancellationToken))[2];

        // Assert: proportional to the product of the powers of the two, with the weight of the model
        var parts = encoded.Analyses.Select(a => a.Details.PartOfSpeech).Distinct().ToArray();
        var weight = MiniLMWordSenseModel.PartOfSpeechHeadWeight;
        var scores = parts.Select(p => Math.Pow(Math.Max(Slot(encoded, p), 1e-6), weight) * Math.Pow(Math.Max(Slot(basic, p), 1e-6), 1 - weight)).ToArray();
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
        for (var k = 0; k < parts.Length; k++)
        {
            Assert.Equal(scores[k] / scores.Sum(), Slot(result, parts[k]), 9);
        }

        Assert.Equal(PartOfSpeech.Verb, result.Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A word that the encoder names at the sense or the group level keeps the level and the ids of the encoder in
    ///     the default model while its slot stays the most probable one, with the probability of the sense inside the slot
    ///     unchanged; when the combination makes another slot the most probable one the answer is at the part-of-speech
    ///     level without sense and group.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_SenseOrGroupAnswer_KeepsTheEncodersSenseOrStepsDown()
    {
        // Arrange
        string[] words = ["They", "will", "record", "the", "record", "."];
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        static double SlotMass(ClassifyResult r) => r.Analyses.Where(a => a.Details.PartOfSpeech == r.Details!.PartOfSpeech && a.Details.Lemma == r.Details.Lemma).Sum(a => a.Probability);

        // Act
        var encoded = await model.Encoder.ClassifyAsync(words, TestContext.Current.CancellationToken);
        var results = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);

        // Assert
        var named = Enumerable.Range(0, words.Length).Where(i => encoded[i].Level is SenseLevel.Sense or SenseLevel.Group).ToArray();
        Assert.NotEmpty(named);
        foreach (var i in named)
        {
            if (results[i].Level == encoded[i].Level)
            {
                Assert.Equal((encoded[i].GroupId, encoded[i].SenseId), (results[i].GroupId, results[i].SenseId));
                Assert.Equal(encoded[i].Confidence / SlotMass(encoded[i]), results[i].Confidence / SlotMass(results[i]), 9);
            }
            else
            {
                Assert.Equal((SenseLevel.PartOfSpeech, null, null), (results[i].Level, results[i].GroupId, results[i].SenseId));
            }
        }
    }

    /// <summary>
    ///     The defaults construct a model with the per-user store and a maximum sequence length of 128 word pieces; the
    ///     limits 8 and 512 are accepted and reach the encoder model unchanged.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Constructor_Defaults_UseStoreAnd128()
    {
        // Arrange / Act
        using var defaults = new MiniLMWordSenseModel();
        using var smallest = new MiniLMWordSenseModel(null, 8);
        using var largest = new MiniLMWordSenseModel(null, 512);

        // Assert
        Assert.Equal(384, defaults.HiddenSize);
        Assert.Equal(384, smallest.HiddenSize);
        Assert.Equal(384, largest.HiddenSize);
        Assert.Equal(128, defaults.Encoder.MaxSequenceLength);
        Assert.Equal(8, smallest.Encoder.MaxSequenceLength);
        Assert.Equal(512, largest.Encoder.MaxSequenceLength);
    }

    /// <summary>
    ///     A directory with the pinned files is accepted without network access.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesPinnedFiles()
    {
        using var model = Open(ModelFolder.Require());

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The same text always gives the same classification, and the two uses of the ambiguous word get their part of
    ///     speech from the context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_AmbiguousWord_ReturnsDeterministicResults()
    {
        using var model = Open(ModelFolder.Require());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var first = await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var second = await model.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(PartOfSpeech.Verb, first[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, first[4].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A trained lemma in a clear sentence is answered by the encoder at the sense or the group level, with the ids
    ///     of the catalog, a probability for every analysis and its senses ranked inside the selected analysis.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel()
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
