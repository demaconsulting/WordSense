using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that the weights embedded in the model packages of this repository match the word lexicon.
/// </summary>
public class EmbeddedWeightsTests
{
    /// <summary>
    ///     No lemma of the MiniLM weights is dropped because the lexicon disagrees.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_MiniLM_DropsNoLemma()
    {
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(MiniLMWordSenseModel).Assembly, "DemaConsulting.WordSense.MiniLM.weights.bin", "all-MiniLM-L6-v2-int8", 384);

        Assert.Empty(table.DroppedLemmaNames);
        Assert.NotEmpty(table.Lemmas);
    }

    /// <summary>
    ///     No lemma of the BertBaseCased weights is dropped because the lexicon disagrees.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_BertBaseCased_DropsNoLemma()
    {
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(BertBaseCasedWordSenseModel).Assembly, "DemaConsulting.WordSense.BertBaseCased.weights.bin", "bert-base-cased-int8", 768);

        Assert.Empty(table.DroppedLemmaNames);
        Assert.NotEmpty(table.Lemmas);
    }
}
