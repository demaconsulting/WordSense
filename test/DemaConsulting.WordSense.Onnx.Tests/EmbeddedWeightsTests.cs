using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that the weights embedded in the model packages of this repository load and have the expected shape.
/// </summary>
public class EmbeddedWeightsTests
{
    /// <summary>
    ///     The MiniLM weights load, belong to the MiniLM encoder and train every lemma for at least two classes.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_MiniLM_LoadsWithEveryLemmaTrainedForSeveralClasses()
    {
        // Arrange / Act: load the embedded weights
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(MiniLMWordSenseModel).Assembly, "DemaConsulting.WordSense.MiniLM.weights.bin", "all-MiniLM-L6-v2-int8", 384);

        // Assert: model id, dimension, mean and the classes of every lemma
        Assert.Equal("all-MiniLM-L6-v2-int8", table.Data.ModelId);
        Assert.Equal(384, table.Data.Dimension);
        Assert.Equal(384, table.Data.Mean.Length);
        Assert.NotEmpty(table.Lemmas);
        Assert.All(table.Lemmas.Values, lemma => Assert.True(lemma.ClassIds.Count >= 2, $"'{lemma.Lemma}' has fewer than two classes."));
    }

    /// <summary>
    ///     The BertBaseCased weights load, belong to the BERT encoder and train every lemma for at least two classes.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_BertBaseCased_LoadsWithEveryLemmaTrainedForSeveralClasses()
    {
        // Arrange / Act: load the embedded weights
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(BertBaseCasedWordSenseModel).Assembly, "DemaConsulting.WordSense.BertBaseCased.weights.bin", "bert-base-cased-int8", 768);

        // Assert: model id, dimension, mean and the classes of every lemma
        Assert.Equal("bert-base-cased-int8", table.Data.ModelId);
        Assert.Equal(768, table.Data.Dimension);
        Assert.Equal(768, table.Data.Mean.Length);
        Assert.NotEmpty(table.Lemmas);
        Assert.All(table.Lemmas.Values, lemma => Assert.True(lemma.ClassIds.Count >= 2, $"'{lemma.Lemma}' has fewer than two classes."));
    }
}
