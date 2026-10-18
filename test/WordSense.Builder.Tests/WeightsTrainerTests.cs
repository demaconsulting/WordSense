// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense.Onnx;
using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the weights trainer and the model catalog; no encoder and no model files are involved.
/// </summary>
public sealed class WeightsTrainerTests
{
    private static readonly byte[] Hash = new byte[32];
    private static CorpusRow Row(string lemma, string sense, int number) =>
        new(lemma, sense, $"The {{{lemma}}} {number}.", "a.tsv", ["The", lemma, number.ToString(System.Globalization.CultureInfo.InvariantCulture), "."], 1);

    /// <summary>
    ///     A binary fit scores the second sense with a single row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_Binary_UsesOneRowForTheSecondSense()
    {
        float[][] x = [[1f, 0f], [0.9f, 0.1f], [-1f, 0f], [-0.9f, -0.1f]];
        int[] labels = [1, 1, 0, 0];

        var (weights, bias) = WeightsTrainer.Fit(x, labels, 2, LemmaModelKind.Binary, 100);

        Assert.Equal(2, weights.Length);
        Assert.Single(bias);
        Assert.True(weights[0] > 0);
    }

    /// <summary>
    ///     A one-versus-rest fit has one row per sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_OneVersusRest_UsesOneRowPerSense()
    {
        float[][] x = [[1f, 0f], [0.9f, 0.1f], [-1f, 0f], [-0.9f, -0.1f], [0f, 1f], [0.1f, 0.9f]];
        int[] labels = [0, 0, 1, 1, 2, 2];

        var (weights, bias) = WeightsTrainer.Fit(x, labels, 3, LemmaModelKind.OneVersusRest, 100);

        Assert.Equal(6, weights.Length);
        Assert.Equal(3, bias.Length);
        Assert.True(weights[0] > 0);
        Assert.True(weights[2] < 0);
        Assert.True(weights[5] > 0);
    }

    /// <summary>
    ///     Dense training yields one binary lemma that classifies its own training vectors and is deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_TrainsBinaryLemma()
    {
        var inventory = new SenseInventory(
        [
            new InventoryRow("bank", "noun", PartOfSpeech.Noun, null, null, "s.tsv"),
            new InventoryRow("bank", "verb", PartOfSpeech.Verb, null, null, "s.tsv"),
        ]);
        CorpusRow[] anchors = [Row("bank", "noun", 1), Row("bank", "noun", 2), Row("bank", "noun", 3), Row("bank", "verb", 4), Row("bank", "verb", 5), Row("bank", "verb", 6)];
        double[][] pooled = [[1, 0.1], [0.9, 0], [1.1, 0.2], [0, 1], [0.1, 0.9], [0, 1.1]];
        var options = new TrainingOptions(Parallelism: 1);

        var data = WeightsTrainer.TrainDense("test-model", Hash, pooled, anchors, inventory, options);
        var again = WeightsTrainer.TrainDense("test-model", Hash, pooled, anchors, inventory, options);

        Assert.Equal("test-model", data.ModelId);
        Assert.Equal(2, data.Dimension);
        Assert.Equal(2, data.Mean.Length);
        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal("bank", lemma.Lemma);
        Assert.Equal(["bank#noun", "bank#verb"], lemma.ClassIds);
        Assert.Equal(LemmaModelKind.Binary, lemma.Weights.Kind);
        Assert.Equal(lemma.Weights.Weights, again.Lemmas[0].Weights.Weights);

        var scores = new float[2];
        for (var i = 0; i < pooled.Length; i++)
        {
            var feature = FeatureExtractor.Center(pooled[i], data.Mean);
            Assert.NotNull(feature);
            lemma.Weights.Scores(feature, scores);
            Assert.Equal(i < 3 ? 0 : 1, LemmaWeights.Rank(scores).Best);
        }
    }

    /// <summary>
    ///     A lemma with anchors for fewer than two senses cannot be trained.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_SingleSenseLemma_Throws()
    {
        var inventory = new SenseInventory([new InventoryRow("bank", "noun", PartOfSpeech.Noun, null, null, "s.tsv")]);
        CorpusRow[] anchors = [Row("bank", "noun", 1), Row("bank", "noun", 2)];
        double[][] pooled = [[1, 0], [0, 1]];

        // The failure of a lemma is rethrown unwrapped
        Assert.Throws<InvalidDataException>(() => WeightsTrainer.TrainDense("m", Hash, pooled, anchors, inventory, new TrainingOptions(Parallelism: 1)));
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
            Path.Combine("repo", "src", "DemaConsulting.WordSense.MiniLM", "Resources", "weights.bin"),
            minilm.WeightsPath("repo"));
        Assert.Throws<ArgumentException>(() => ModelCatalog.Find("unknown"));
    }
}
