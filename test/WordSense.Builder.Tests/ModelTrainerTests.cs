// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using DemaConsulting.WordSense.Onnx;
using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for retraining with the embedding cache and the frozen global mean; a stub encoder replaces the ONNX
///     model.
/// </summary>
public sealed class ModelTrainerTests : IDisposable
{
    private const string ModelId = "stub-model";
    private static readonly byte[] Hash = new byte[32];
    private static readonly byte[] Digest = EmbeddingCache.ComputeDigest("aa", "bb", false, "1.0");
    private static readonly SenseInventory Inventory = new(
    [
        new InventoryRow("bank", "noun", PartOfSpeech.Noun, null, null, "s.tsv"),
        new InventoryRow("bank", "verb", PartOfSpeech.Verb, null, null, "s.tsv"),
        new InventoryRow("bat", "noun", PartOfSpeech.Noun, null, null, "s.tsv"),
        new InventoryRow("bat", "verb", PartOfSpeech.Verb, null, null, "s.tsv"),
    ]);

    private readonly TestDirectory _dir = new();
    private int _encodeCalls;

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static CorpusRow Row(string lemma, string sense, int number) =>
        new(lemma, sense, $"The {{{lemma}}} {sense} {number}.", "a.tsv", ["The", lemma, sense, number.ToString(System.Globalization.CultureInfo.InvariantCulture), "."], 1);

    private static List<CorpusRow> Anchors(int bankPerSense, int batPerSense)
    {
        var rows = new List<CorpusRow>();
        foreach (var (lemma, count) in new[] { ("bank", bankPerSense), ("bat", batPerSense) })
        {
            for (var i = 0; i < count; i++)
            {
                rows.Add(Row(lemma, "noun", i));
                rows.Add(Row(lemma, "verb", i));
            }
        }

        return rows;
    }

    private static double[] Embed(IReadOnlyList<string> words)
    {
        // A deterministic pseudo-embedding: a sense signal plus a spread that depends on the text
        var seed = 17u;
        foreach (var ch in string.Join(' ', words))
        {
            seed = (seed * 31) + ch;
        }

        double Unit(int k) => ((((seed >> k) * 2654435761u) >> 8) & 0xFFFF) / 65535.0;
        var sign = words[2] == "noun" ? 1.0 : -1.0;
        return [(sign * 2) + Unit(1), (sign * 0.5) + Unit(3), Unit(5), Unit(7) - 0.5];
    }

    private CachingEncoder Encoder(EmbeddingCache? cache) => new(
        (words, targets, _) =>
        {
            _encodeCalls++;
            return ValueTask.FromResult<double[]?[]>(targets.Select(_ => (double[]?)Embed(words)).ToArray());
        },
        cache);

    private static Task<WeightsData> TrainAsync(List<CorpusRow> anchors, CachingEncoder encoder, float[]? mean, FitCache? fits = null) =>
        ModelTrainer.TrainDataAsync(
            "stub", ModelId, Hash, anchors, Inventory, new TrainingOptions(Parallelism: 1), encoder, mean, fits, TextWriter.Null, Stopwatch.StartNew(), TestContext.Current.CancellationToken);

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
    ///     With the frozen mean, changing the anchors of one lemma leaves the weights of the other lemma unchanged.
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
        var before = Lemma(original, "bank").Weights;
        var after = Lemma(edited, "bank").Weights;
        Assert.Equal(before.Weights, after.Weights);
        Assert.Equal(before.Bias, after.Bias);
        Assert.Equal(before.AbstainMargin, after.AbstainMargin);
        Assert.NotEqual(Lemma(original, "bat").Weights.Weights, Lemma(edited, "bat").Weights.Weights);
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
        Assert.NotEqual(Lemma(original, "bank").Weights.Weights, Lemma(edited, "bank").Weights.Weights);
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
            "stub", ModelId, Hash, anchors, Inventory, new TrainingOptions(Parallelism: 1), Encoder(EmbeddingCache.Open(_dir.Root, Digest)), null, FitCache.Open(_dir.Root), output, Stopwatch.StartNew(), TestContext.Current.CancellationToken);

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
}
