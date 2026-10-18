using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that the weights embedded in the model packages of this repository match the word lexicon.
/// </summary>
public class EmbeddedWeightsTests
{
    /// <summary>
    ///     The MiniLM weights belong to the shipped word image: the table builds (strict load) and has the image's class hash.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_MiniLM_MatchesWordImage()
    {
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(MiniLMWordSenseModel).Assembly, "DemaConsulting.WordSense.MiniLM.weights.bin", "all-MiniLM-L6-v2-int8", 384);

        Assert.Equal(WordLexicon.Current.ClassHash.ToArray(), table.Data.ClassHash);
        Assert.NotEmpty(table.Lemmas);
    }

    /// <summary>
    ///     The BertBaseCased weights belong to the shipped word image: the table builds (strict load) and has the image's class hash.
    /// </summary>
    [Fact]
    public void EmbeddedWeights_BertBaseCased_MatchesWordImage()
    {
        var table = WeightedLemmaTable.LoadEmbedded(
            typeof(BertBaseCasedWordSenseModel).Assembly, "DemaConsulting.WordSense.BertBaseCased.weights.bin", "bert-base-cased-int8", 768);

        Assert.Equal(WordLexicon.Current.ClassHash.ToArray(), table.Data.ClassHash);
        Assert.NotEmpty(table.Lemmas);
    }
}
