// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for retraining with the embedding cache, the fit cache and the frozen global mean; a stub encoder replaces
///     the ONNX model. The lemmas and senses are real ones of the catalog of the word image.
/// </summary>
public sealed class ModelTrainerTests : IDisposable
{
    private const string ModelId = "stub-model";
    private static readonly byte[] Hash = new byte[32];
    private static readonly byte[] Digest = EmbeddingCache.ComputeDigest("aa", "bb", false, "1.0");
    private static readonly SenseInfo WalkNoun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
    private static readonly SenseInfo WalkVerb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
    private static readonly SenseInfo RunNoun = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
    private static readonly SenseInfo RunVerb = SenseFixtures.Senses("run", PartOfSpeech.Verb, 1)[0];

    private readonly TestDirectory _dir = new();
    private int _encodeCalls;

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static List<CorpusRow> Anchors(int walkPerSense, int runPerSense)
    {
        var rows = new List<CorpusRow>();
        foreach (var (noun, verb, count) in new[] { (WalkNoun, WalkVerb, walkPerSense), (RunNoun, RunVerb, runPerSense) })
        {
            for (var i = 0; i < count; i++)
            {
                rows.Add(StubEmbedding.Row(noun, "noun", i));
                rows.Add(StubEmbedding.Row(verb, "verb", i));
            }
        }

        return rows;
    }

    private CachingEncoder Encoder(EmbeddingCache? cache) => new(
        (words, targets, _) =>
        {
            _encodeCalls++;
            return ValueTask.FromResult<double[]?[]>(targets.Select(_ => (double[]?)StubEmbedding.Embed(words)).ToArray());
        },
        cache);

    private static Task<WeightsData> TrainAsync(List<CorpusRow> anchors, CachingEncoder encoder, float[]? mean, FitCache? fits = null, IReadOnlyList<CorpusRow>? holdout = null) =>
        ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, new TrainingOptions(Parallelism: 1), encoder, mean, fits, TextWriter.Null, Stopwatch.StartNew(), TestContext.Current.CancellationToken, holdout);

    private static WeightedLemma Lemma(WeightsData data, string lemma) => data.Lemmas.Single(l => l.Lemma == lemma);

    /// <summary>
    ///     A second run over the same anchors encodes nothing and gives byte-identical weights.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_SecondRun_EncodesNothingAndReproducesBytes()
    {
        // Arrange
        var anchors = Anchors(4, 3);
        var first = Encoder(EmbeddingCache.Open(_dir.Root, Digest));
        var firstData = await TrainAsync(anchors, first, null);
        var callsAfterFirst = _encodeCalls;

        // Act
        var second = Encoder(EmbeddingCache.Open(_dir.Root, Digest));
        var secondData = await TrainAsync(anchors, second, null);

        // Assert
        Assert.Equal(anchors.Count, first.Misses);
        Assert.Equal(0, first.Hits);
        Assert.Equal(anchors.Count, second.Hits);
        Assert.Equal(0, second.Misses);
        Assert.Equal(callsAfterFirst, _encodeCalls);
        Assert.Equal(WeightsBlobWriter.Write(firstData), WeightsBlobWriter.Write(secondData));
    }

    /// <summary>
    ///     Adding anchors encodes only the new sentences.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_AddedSentences_AreTheOnlyOnesEncoded()
    {
        // Arrange
        await TrainAsync(Anchors(4, 3), Encoder(EmbeddingCache.Open(_dir.Root, Digest)), null);

        // Act
        var encoder = Encoder(EmbeddingCache.Open(_dir.Root, Digest));
        await TrainAsync(Anchors(4, 5), encoder, null);

        // Assert
        Assert.Equal(4, encoder.Misses);
        Assert.Equal(14, encoder.Hits);
        Assert.Equal(18, encoder.Cache!.Count);
    }

    /// <summary>
    ///     When the model files change, the cache is not used and every sentence is encoded again.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_ChangedModelDigest_EncodesEverythingAgain()
    {
        // Arrange
        var anchors = Anchors(3, 3);
        await TrainAsync(anchors, Encoder(EmbeddingCache.Open(_dir.Root, Digest)), null);

        // Act
        var encoder = Encoder(EmbeddingCache.Open(_dir.Root, EmbeddingCache.ComputeDigest("other", "bb", false, "1.0")));
        await TrainAsync(anchors, encoder, null);

        // Assert
        Assert.Equal(anchors.Count, encoder.Misses);
        Assert.Equal(0, encoder.Hits);
    }

    /// <summary>
    ///     A corrupt cache entry is encoded again and the weights equal those of a run without a cache.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_CorruptEntry_IsEncodedAgain()
    {
        // Arrange
        var anchors = Anchors(3, 3);
        var plain = await TrainAsync(anchors, Encoder(null), null);
        var written = Encoder(EmbeddingCache.Open(_dir.Root, Digest));
        await TrainAsync(anchors, written, null);
        var bytes = await File.ReadAllBytesAsync(written.Cache!.Path, TestContext.Current.CancellationToken);
        bytes[^20] ^= 0x55;
        await File.WriteAllBytesAsync(written.Cache.Path, bytes, TestContext.Current.CancellationToken);

        // Act
        var encoder = Encoder(EmbeddingCache.Open(_dir.Root, Digest));
        var data = await TrainAsync(anchors, encoder, null);

        // Assert
        Assert.Equal(1, encoder.Cache!.Discarded);
        Assert.Equal(1, encoder.Misses);
        Assert.Equal(anchors.Count - 1, encoder.Hits);
        Assert.Equal(WeightsBlobWriter.Write(plain), WeightsBlobWriter.Write(data));
    }

    /// <summary>
    ///     With the frozen mean, changing the anchors of one lemma leaves the weights and thresholds of the other lemma unchanged.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_FrozenMean_EditingOneLemmaLeavesOthersUnchanged()
    {
        // Arrange
        var original = await TrainAsync(Anchors(4, 3), Encoder(null), null);

        // Act
        var edited = await TrainAsync(Anchors(4, 4), Encoder(null), original.Mean);

        // Assert
        Assert.Equal(original.Mean, edited.Mean);
        var before = Lemma(original, "walk").Weights;
        var after = Lemma(edited, "walk").Weights;
        Assert.Equal(before.Weights, after.Weights);
        Assert.Equal(before.Bias, after.Bias);
        Assert.Equal(before.FineThreshold, after.FineThreshold);
        Assert.Equal(before.GroupThreshold, after.GroupThreshold);
        Assert.NotEqual(Lemma(original, "run").Weights.Weights, Lemma(edited, "run").Weights.Weights);
    }

    /// <summary>
    ///     Without the frozen mean, changing one lemma moves the mean and with it the weights of the other lemma.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_RecomputedMean_CouplesTheLemmas()
    {
        // Arrange
        var original = await TrainAsync(Anchors(4, 3), Encoder(null), null);

        // Act
        var edited = await TrainAsync(Anchors(4, 4), Encoder(null), null);

        // Assert
        Assert.NotEqual(original.Mean, edited.Mean);
        Assert.NotEqual(Lemma(original, "walk").Weights.Weights, Lemma(edited, "walk").Weights.Weights);
    }

    /// <summary>
    ///     A frozen mean of the wrong length is not used; the mean is computed from the vectors instead.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_FrozenMeanOfOtherDimension_IsComputedInstead()
    {
        // Arrange
        var anchors = Anchors(3, 3);
        var expected = await TrainAsync(anchors, Encoder(null), null);

        // Act
        var data = await TrainAsync(anchors, Encoder(null), [1f, 2f]);

        // Assert
        Assert.Equal(expected.Mean, data.Mean);
    }

    /// <summary>
    ///     The mean of the existing weights of the same model is read back exactly.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_ReadFrozenMean_SameModel_ReturnsStoredMean()
    {
        // Arrange
        var data = await TrainAsync(Anchors(3, 3), Encoder(null), null);
        var path = Path.Join(_dir.Root, "weights.bin");
        await File.WriteAllBytesAsync(path, WeightsBlobWriter.Write(data), TestContext.Current.CancellationToken);
        using var output = new StringWriter();

        // Act
        var mean = ModelTrainer.ReadFrozenMean(path, ModelId, output, "stub");

        // Assert
        Assert.Equal(data.Mean, mean);
        Assert.Contains("reused", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing, other-model or unreadable weights file gives no frozen mean.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_ReadFrozenMean_MissingOtherOrUnreadable_ReturnsNull()
    {
        // Arrange
        var data = await TrainAsync(Anchors(3, 3), Encoder(null), null);
        var other = Path.Join(_dir.Root, "other.bin");
        await File.WriteAllBytesAsync(other, WeightsBlobWriter.Write(data), TestContext.Current.CancellationToken);
        var garbage = _dir.Write("garbage.bin", "not a weights blob");
        using var output = new StringWriter();

        // Act
        var missing = ModelTrainer.ReadFrozenMean(Path.Join(_dir.Root, "missing.bin"), ModelId, output, "stub");
        var otherModel = ModelTrainer.ReadFrozenMean(other, "another-model", output, "stub");
        var unreadable = ModelTrainer.ReadFrozenMean(garbage, ModelId, output, "stub");

        // Assert
        Assert.Null(missing);
        Assert.Null(otherModel);
        Assert.Null(unreadable);
        Assert.Contains("no existing weights", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("not 'another-model'", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("unreadable", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The progress output reports cache hits, misses and the lemma fits.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_Progress_ReportsHitsAndMisses()
    {
        // Arrange
        var anchors = Anchors(3, 3);
        using var output = new StringWriter();

        // Act
        await ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, new TrainingOptions(Parallelism: 1), Encoder(EmbeddingCache.Open(_dir.Root, Digest)), null, FitCache.Open(_dir.Root), output, Stopwatch.StartNew(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("0 cache hits, 12 encoded", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("lemma fits: 0 reused, 2 fitted", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A second run over unchanged anchors reuses every lemma fit and gives byte-identical weights.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_UnchangedAnchors_ReusesEveryLemmaFit()
    {
        // Arrange
        var anchors = Anchors(4, 3);
        var first = await TrainAsync(anchors, Encoder(null), null, FitCache.Open(_dir.Root));

        // Act
        var fits = FitCache.Open(_dir.Root);
        var second = await TrainAsync(anchors, Encoder(null), null, fits);

        // Assert
        Assert.Equal(2, fits.Loaded);
        Assert.Equal(2, fits.Hits);
        Assert.Equal(0, fits.Misses);
        Assert.Equal(WeightsBlobWriter.Write(first), WeightsBlobWriter.Write(second));
    }

    /// <summary>
    ///     With the frozen mean, editing the anchors of one lemma fits only that lemma again, and the weights equal those
    ///     of a run without any cache.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_FrozenMeanAndEditedLemma_FitsOnlyThatLemma()
    {
        // Arrange
        var original = await TrainAsync(Anchors(4, 3), Encoder(null), null, FitCache.Open(_dir.Root));
        var expected = await TrainAsync(Anchors(4, 4), Encoder(null), original.Mean);

        // Act
        var fits = FitCache.Open(_dir.Root);
        var edited = await TrainAsync(Anchors(4, 4), Encoder(null), original.Mean, fits);

        // Assert
        Assert.Equal(1, fits.Hits);
        Assert.Equal(1, fits.Misses);
        Assert.Equal(WeightsBlobWriter.Write(expected), WeightsBlobWriter.Write(edited));
    }

    /// <summary>
    ///     A recomputed mean changes every feature, so no lemma fit is reused.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_RecomputedMean_FitsEveryLemmaAgain()
    {
        // Arrange
        await TrainAsync(Anchors(4, 3), Encoder(null), null, FitCache.Open(_dir.Root));

        // Act
        var fits = FitCache.Open(_dir.Root);
        await TrainAsync(Anchors(4, 4), Encoder(null), null, fits);

        // Assert
        Assert.Equal(0, fits.Hits);
        Assert.Equal(2, fits.Misses);
    }

    /// <summary>
    ///     Every trained lemma carries a fine and a group threshold in the written blob; a lemma with groups whose
    ///     confusable senses fall through the fine level gets finite ones, a lemma without groups never answers the
    ///     group level, and the summary of the run counts them.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_WritesBothThresholdsPerLemma()
    {
        // Arrange: test has two senses of one group that the embedding cannot tell apart, a sense of another group
        // and a sense in no group; walk has no group at all
        var (a1, a2, b, none) = SenseFixtures.Grouped("test");
        var anchors = new List<CorpusRow>();
        for (var i = 0; i < 12; i++)
        {
            anchors.Add(StubEmbedding.Row(a1, "a1", i));
            anchors.Add(StubEmbedding.Row(a2, "a2", i));
            anchors.Add(StubEmbedding.Row(b, "b", i));
            anchors.Add(StubEmbedding.Row(none, "n", i));
            anchors.Add(StubEmbedding.Row(WalkNoun, "noun", i));
            anchors.Add(StubEmbedding.Row(WalkVerb, "verb", i));
        }

        using var output = new StringWriter();

        // Act
        var data = await ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, new TrainingOptions(Parallelism: 1), Encoder(null), null, null, output, Stopwatch.StartNew(), TestContext.Current.CancellationToken);
        var read = WeightsBlob.Read(WeightsBlobWriter.Write(data));

        // Assert
        Assert.Equal(["test", "walk"], read.Lemmas.Select(l => l.Lemma));
        foreach (var (written, trained) in read.Lemmas.Zip(data.Lemmas))
        {
            Assert.Equal(trained.Weights.FineThreshold, written.Weights.FineThreshold);
            Assert.Equal(trained.Weights.GroupThreshold, written.Weights.GroupThreshold);
            Assert.True(written.Weights.FineThreshold >= 0);
            Assert.True(written.Weights.GroupThreshold >= 0);
        }

        var test = read.Lemmas[0].Weights;
        Assert.True(test.FineThreshold < LemmaWeights.Never);
        Assert.True(test.GroupThreshold < LemmaWeights.Never);
        Assert.Equal(LemmaWeights.Never, read.Lemmas[1].Weights.GroupThreshold);
        Assert.Contains("trained 2 lemmas", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("group threshold found for 1 of 1 lemmas that have groups", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A lemma with anchors for a single sense gets no weights and is named in the output; it does not fail the run.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_LemmaWithOneAnchoredSense_IsReportedAndLeftOut()
    {
        // Arrange
        var anchors = Anchors(3, 3).Where(r => r.Lemma == "walk").Concat(Enumerable.Range(0, 3).Select(i => StubEmbedding.Row(RunNoun, "noun", i))).ToList();
        using var output = new StringWriter();

        // Act
        var data = await ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, new TrainingOptions(Parallelism: 1), Encoder(null), null, null, output, Stopwatch.StartNew(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["walk"], data.Lemmas.Select(l => l.Lemma));
        Assert.Contains("fewer than two anchored senses and got no weights: run (1)", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     With held-out rows the trainer encodes them through the same cache, prints the fine accuracy with the full and
    ///     the half-precision weights and the difference, and leaves the weights as they would be without them.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTrainer_TrainDataAsync_Holdout_ReportsTheHalfPrecisionGate()
    {
        // Arrange: held-out rows of a trained and of an untrained lemma
        var anchors = Anchors(6, 6);
        var holdout = Enumerable.Range(100, 6).SelectMany(i => new[] { StubEmbedding.Row(WalkNoun, "noun", i), StubEmbedding.Row(WalkVerb, "verb", i), StubEmbedding.Row(SenseFixtures.Senses("go", PartOfSpeech.Verb, 1)[0], "verb", i) }).ToList();
        using var output = new StringWriter();
        var encoder = Encoder(EmbeddingCache.Open(_dir.Root, Digest));

        // Act
        var data = await ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, new TrainingOptions(Parallelism: 1), encoder, null, null, output, Stopwatch.StartNew(), TestContext.Current.CancellationToken, holdout);

        // Assert: only the 12 held-out rows of walk were encoded and scored
        Assert.Equal(anchors.Count + 12, encoder.Misses);
        Assert.Contains("half-precision gate: 12 held-out rows: fine top-1", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("with full-precision weights", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("with the stored half-precision weights", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(["run", "walk"], data.Lemmas.Select(l => l.Lemma));
    }
}
