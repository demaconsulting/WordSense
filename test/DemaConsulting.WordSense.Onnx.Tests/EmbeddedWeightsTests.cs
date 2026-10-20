using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that the weights embedded in the model packages of this repository belong to the shipped word image.
/// </summary>
public class EmbeddedWeightsTests
{
    private static void AssertMatchesWordImage(WeightedLemmaTable table, string modelId, int dimension)
    {
        // The table was built by LoadEmbedded, which resolves every sense against the word image; check it explicitly too
        Assert.Equal(modelId, table.Data.ModelId);
        Assert.Equal(dimension, table.Data.Dimension);
        Assert.Equal(dimension, table.Data.Mean.Length);
        Assert.Equal(WordLexicon.Current.InventoryHash.ToArray(), table.Data.InventoryHash);
        Assert.NotEmpty(table.Lemmas);
        foreach (var lemma in table.Lemmas.Values)
        {
            Assert.True(lemma.Weights.SenseIds.Count >= 2, $"'{lemma.Weights.Lemma}' has fewer than two senses.");
            foreach (var id in lemma.Weights.SenseIds)
            {
                Assert.True(WordLexicon.TryGetSense(id, out var sense), $"The sense '{id}' is unknown to the word image.");
                Assert.Equal(lemma.Weights.Lemma, sense.Lemma);
            }
        }
    }

    /// <summary>
    ///     The MiniLM weights load, belong to the MiniLM encoder, match the inventory of the word image, know every
    ///     sense of every lemma and train every lemma for at least two senses.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_MiniLM_MatchesWordImage()
    {
        // Arrange / Act: load the embedded weights
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(MiniLMWordSenseModel).Assembly, "DemaConsulting.WordSense.MiniLM.weights.bin", "all-MiniLM-L6-v2-int8", 384);

        // Assert
        AssertMatchesWordImage(table, "all-MiniLM-L6-v2-int8", 384);
    }

    /// <summary>
    ///     The BertBaseCased weights load, belong to the BERT encoder, match the inventory of the word image, know every
    ///     sense of every lemma and train every lemma for at least two senses.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_BertBaseCased_MatchesWordImage()
    {
        // Arrange / Act: load the embedded weights
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(BertBaseCasedWordSenseModel).Assembly, "DemaConsulting.WordSense.BertBaseCased.weights.bin", "bert-base-cased-int8", 768);

        // Assert
        AssertMatchesWordImage(table, "bert-base-cased-int8", 768);
    }
}
