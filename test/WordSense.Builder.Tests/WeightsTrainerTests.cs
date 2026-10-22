// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the weights trainer and the model catalog; no encoder and no model files are involved. The senses are
///     real senses of the catalog of the word image.
/// </summary>
public sealed class WeightsTrainerTests
{
    private static readonly byte[] Hash = new byte[32];

    private static readonly double[] DirectionA = [1, 0, 0];
    private static readonly double[] DirectionB = [0, 1, 0];
    private static readonly double[] DirectionC = [0, 0, 1];

    private static (CorpusRow[] Rows, double[][] Pooled) Data(params (SenseInfo Sense, double[] Direction, int Count)[] parts)
    {
        var rows = new List<CorpusRow>();
        var pooled = new List<double[]>();
        var number = 0;
        foreach (var (sense, direction, count) in parts)
        {
            for (var i = 0; i < count; i++)
            {
                number++;
                rows.Add(SenseFixtures.Row(sense, number));
                pooled.Add([direction[0] + (0.02 * i), direction[1] - (0.01 * i), direction[2] + (0.015 * i)]);
            }
        }

        return ([.. rows], [.. pooled]);
    }

    private static readonly TrainingOptions Options = new(Parallelism: 1);

    /// <summary>
    ///     A fit of two senses has one row per sense; there is no binary shortcut.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_TwoSenses_HasTwoRows()
    {
        float[][] x = [[1f, 0f], [0.9f, 0.1f], [-1f, 0f], [-0.9f, -0.1f]];
        int[] labels = [1, 1, 0, 0];

        var (weights, bias) = WeightsTrainer.Fit(x, labels, 2, 100);

        Assert.Equal(4, weights.Length);
        Assert.Equal(2, bias.Length);
        Assert.True(weights[0] < 0);
        Assert.True(weights[2] > 0);
    }

    /// <summary>
    ///     A fit has one row per sense that scores its sense above the others.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_OneVersusRest_UsesOneRowPerSense()
    {
        float[][] x = [[1f, 0f], [0.9f, 0.1f], [-1f, 0f], [-0.9f, -0.1f], [0f, 1f], [0.1f, 0.9f]];
        int[] labels = [0, 0, 1, 1, 2, 2];

        var (weights, bias) = WeightsTrainer.Fit(x, labels, 3, 100);

        Assert.Equal(6, weights.Length);
        Assert.Equal(3, bias.Length);
        Assert.True(weights[0] > 0);
        Assert.True(weights[2] < 0);
        Assert.True(weights[5] > 0);
    }

    /// <summary>
    ///     A lemma with two anchored senses gets two rows, classifies its own training vectors and is deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_TwoSenseLemma_HasTwoRowsAndIsDeterministic()
    {
        // Arrange
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var (rows, pooled) = Data((noun, DirectionA, 4), (verb, DirectionB, 4));

        // Act
        var trained = WeightsTrainer.TrainDense("test-model", Hash, pooled, rows, Options);
        var again = WeightsTrainer.TrainDense("test-model", Hash, pooled, rows, Options);

        // Assert
        var data = trained.Data;
        Assert.Equal("test-model", data.ModelId);
        Assert.Equal(Hash, data.InventoryHash);
        Assert.Equal(3, data.Dimension);
        Assert.Equal(3, data.Mean.Length);
        Assert.Empty(trained.Untrained);
        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal("walk", lemma.Lemma);
        Assert.Equal([noun.SenseId, verb.SenseId], lemma.SenseIds);
        Assert.Equal(2, lemma.Weights.SenseCount);
        Assert.Empty(lemma.Choosers);
        Assert.Equal(lemma.Weights.Weights, again.Data.Lemmas[0].Weights.Weights);
        Assert.Equal(lemma.Weights.FineThreshold, again.Data.Lemmas[0].Weights.FineThreshold);
        var scores = new float[2];
        for (var i = 0; i < pooled.Length; i++)
        {
            var feature = FeatureExtractor.Center(pooled[i], data.Mean);
            Assert.NotNull(feature);
            lemma.Weights.Scores(feature, scores);
            Assert.Equal(i < 4 ? 0 : 1, LemmaWeights.Rank(scores).Best);
        }
    }

    /// <summary>
    ///     The columns are the anchored senses in inventory order (the parts of speech in enumeration order, each in
    ///     catalog order), whatever the order of the anchors, and a sense of the catalog without an anchor has no column.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_ColumnsAreTheAnchoredSensesInInventoryOrder()
    {
        // Arrange: anchors for the second noun sense, the first verb sense and the first noun sense, in that order
        var nouns = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 3);
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var (rows, pooled) = Data((nouns[1], DirectionB, 3), (verb, DirectionC, 3), (nouns[0], DirectionA, 3));

        // Act
        var data = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options).Data;

        // Assert
        var inventory = SenseInventory.SensesOf("walk").Select(s => s.SenseId).ToList();
        var columns = Assert.Single(data.Lemmas).SenseIds;
        Assert.Equal([nouns[0].SenseId, nouns[1].SenseId, verb.SenseId], columns);
        Assert.Equal(columns.OrderBy(inventory.IndexOf), columns);
        Assert.DoesNotContain(nouns[2].SenseId, columns);
    }

    /// <summary>
    ///     A lemma with anchors for a single sense has nothing to choose between: it gets no weights, and that is
    ///     reported rather than failing the run; the other lemmas are trained.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_LemmaWithOneAnchoredSense_GetsNoWeights()
    {
        // Arrange: walk has two anchored senses, run only one (with several anchors)
        var walkNoun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var walkVerb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var runNoun = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        var (walkRows, walkPooled) = Data((walkNoun, DirectionA, 3), (walkVerb, DirectionB, 3));
        var (runRows, runPooled) = Data((runNoun, DirectionC, 4));
        CorpusRow[] rows = [.. walkRows, .. runRows];
        double[][] pooled = [.. walkPooled, .. runPooled];

        // Act
        var trained = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options);

        // Assert
        Assert.Equal(["walk"], trained.Data.Lemmas.Select(l => l.Lemma));
        var untrained = Assert.Single(trained.Untrained);
        Assert.Equal(("run", 1), untrained);
    }

    /// <summary>
    ///     A sense with a single anchor stays in the training: it has a column, and its anchor is classified as that sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_SenseWithOneAnchor_StaysInTraining()
    {
        // Arrange
        var nouns = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 2);
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var (rows, pooled) = Data((nouns[0], DirectionA, 5), (nouns[1], DirectionC, 1), (verb, DirectionB, 5));

        // Act
        var data = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options).Data;

        // Assert
        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal(3, lemma.SenseIds.Count);
        Assert.Contains(nouns[1].SenseId, lemma.SenseIds);
        var single = FeatureExtractor.Center(pooled[5], data.Mean);
        Assert.NotNull(single);
        var scores = new float[3];
        lemma.Weights.Scores(single, scores);
        Assert.Equal(nouns[1].SenseId, lemma.SenseIds[LemmaWeights.Rank(scores).Best]);
    }

    /// <summary>
    ///     An anchor whose sense is not a sense of its lemma in the catalog is a data error, reported unwrapped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_AnchorOfSenseOfAnotherLemma_Throws()
    {
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var other = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        CorpusRow[] rows = [SenseFixtures.Row(noun, 1), SenseFixtures.Row(other, 2) with { Lemma = "walk" }];

        var error = Assert.Throws<InvalidDataException>(() => WeightsTrainer.TrainDense("m", Hash, [[1, 0], [0, 1]], rows, Options));

        Assert.Contains(other.SenseId, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Training needs anchors, and a frozen mean must have the dimension of the vectors.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_NoAnchorsOrWrongMean_Throws()
    {
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        CorpusRow[] rows = [SenseFixtures.Row(noun, 1)];

        Assert.Throws<InvalidDataException>(() => WeightsTrainer.TrainDense("m", Hash, [], [], Options));
        Assert.Throws<ArgumentException>(() => WeightsTrainer.TrainDense("m", Hash, [[1, 0]], rows, Options, [1f, 2f, 3f]));
    }

    /// <summary>
    ///     The number of threads that train the lemmas does not change the weights.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_Parallelism_DoesNotChangeTheWeights()
    {
        // Arrange: two lemmas
        var walkNoun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var walkVerb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var runNoun = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        var runVerb = SenseFixtures.Senses("run", PartOfSpeech.Verb, 1)[0];
        var (walkRows, walkPooled) = Data((walkNoun, DirectionA, 4), (walkVerb, DirectionB, 4));
        var (runRows, runPooled) = Data((runNoun, DirectionA, 4), (runVerb, DirectionC, 4));
        CorpusRow[] rows = [.. walkRows, .. runRows];
        double[][] pooled = [.. walkPooled, .. runPooled];

        // Act
        var one = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options).Data;
        var four = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options with { Parallelism = 4 }).Data;

        // Assert
        Assert.Equal(WeightsBlobWriter.Write(one), WeightsBlobWriter.Write(four));
        Assert.Equal(["run", "walk"], one.Lemmas.Select(l => l.Lemma));
    }

    /// <summary>
    ///     The catalog finds the three models and knows which ones have weights.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ModelCatalog_Find_KnowsTheModels()
    {
        var minilm = ModelCatalog.Find("minilm");
        var basic = ModelCatalog.Find("basic");

        Assert.Equal(["minilm", "bert-base-cased", "basic"], ModelCatalog.All.Select(m => m.Name));
        Assert.True(minilm.IsEncoder);
        Assert.False(basic.IsEncoder);
        Assert.True(ModelCatalog.Find("bert-base-cased").IsEncoder);
        Assert.Equal(
            Path.Join("repo", "src", "DemaConsulting.WordSense.MiniLM", "Resources", "weights.bin"),
            minilm.WeightsPath("repo"));
        Assert.Throws<ArgumentException>(() => ModelCatalog.Find("unknown"));
    }

    /// <summary>
    ///     The ensure helper prepares a downloadable model and skips a model without files.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelCatalog_EnsureAsync_EnsuresOnlyDownloadableModels()
    {
        // Arrange: a model with the capability and the Basic model without it
        using var downloadable = new EnsureCountingModel();
        using var basic = new BasicWordSenseModel();

        // Act
        await ModelCatalog.EnsureAsync(downloadable, TestContext.Current.CancellationToken);
        await ModelCatalog.EnsureAsync(basic, TestContext.Current.CancellationToken);

        // Assert: the capable model was ensured once, the Basic model needed nothing
        Assert.Equal(1, downloadable.EnsureCalls);
        await Assert.ThrowsAsync<ArgumentNullException>(() => ModelCatalog.EnsureAsync(null!, TestContext.Current.CancellationToken));
    }

    private sealed class EnsureCountingModel : IDownloadableWordSenseModel
    {
        public int EnsureCalls { get; private set; }

        public Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Task.CompletedTask;
        }

        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(words.Select(ClassifyResult.Unknown).ToArray());

        public void Dispose()
        {
        }
    }
}
