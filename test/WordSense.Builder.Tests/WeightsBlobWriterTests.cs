using DemaConsulting.WordSense.Onnx;
using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the writer of the weights blob, checked against the reader of the Onnx package.
/// </summary>
public sealed class WeightsBlobWriterTests
{
    private const int Dimension = 3;

    private static WeightedLemma Bank() => new(
        "bank",
        [new WeightedWord("bank", PartOfSpeech.Noun, null), new WeightedWord("bank", PartOfSpeech.Verb, null)],
        new LemmaWeights(LemmaModelKind.Binary, [0.5f, -0.25f, 1f], [0.125f], 0.75f, Dimension));

    private static WeightedLemma Bow() => new(
        "bow",
        [
            new WeightedWord("bow", PartOfSpeech.Noun, "bend"),
            new WeightedWord("bow", PartOfSpeech.Verb, "bend"),
            new WeightedWord("bow", PartOfSpeech.Noun, "weapon"),
        ],
        new LemmaWeights(
            LemmaModelKind.OneVersusRest,
            [1f, 0f, -1f, 0.5f, 0.25f, -0.5f, -1f, 2f, 0.125f],
            [0.5f, -0.5f, 0f],
            0f,
            Dimension));

    private static WeightsData Sample(params WeightedLemma[] lemmas) => new("test-model", Dimension, [0.1f, -0.2f, 0.3f], lemmas);

    /// <summary>
    ///     Binary and one-versus-rest lemmas are read back with equal content, in ordinal lemma order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_RoundTripsThroughTheReader()
    {
        var data = Sample(Bow(), Bank());

        var read = WeightsBlob.Read(WeightsBlobWriter.Write(data));

        Assert.Equal("test-model", read.ModelId);
        Assert.Equal(Dimension, read.Dimension);
        Assert.Equal(data.Mean, read.Mean);
        Assert.Equal(["bank", "bow"], read.Lemmas.Select(l => l.Lemma));

        var bank = read.Lemmas[0];
        Assert.Equal(Bank().Senses, bank.Senses);
        Assert.Equal(LemmaModelKind.Binary, bank.Weights.Kind);
        Assert.Equal(Bank().Weights.Weights, bank.Weights.Weights);
        Assert.Equal(Bank().Weights.Bias, bank.Weights.Bias);
        Assert.Equal(0.75f, bank.Weights.AbstainMargin);
        Assert.Equal(Dimension, bank.Weights.Dimension);

        var bow = read.Lemmas[1];
        Assert.Equal(Bow().Senses, bow.Senses);
        Assert.Equal(LemmaModelKind.OneVersusRest, bow.Weights.Kind);
        Assert.Equal(Bow().Weights.Weights, bow.Weights.Weights);
        Assert.Equal(Bow().Weights.Bias, bow.Weights.Bias);
        Assert.Equal(0f, bow.Weights.AbstainMargin);
        Assert.Equal(3, bow.Weights.SenseCount);
    }

    /// <summary>
    ///     A table without lemmas is a valid blob.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_NoLemmas_RoundTrips()
    {
        var read = WeightsBlob.Read(WeightsBlobWriter.Write(Sample()));

        Assert.Empty(read.Lemmas);
        Assert.Equal(Dimension, read.Mean.Length);
    }

    /// <summary>
    ///     Equal content gives equal bytes whatever the order of the lemmas.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_IsDeterministic()
    {
        Assert.Equal(
            WeightsBlobWriter.Write(Sample(Bank(), Bow())),
            WeightsBlobWriter.Write(Sample(Bow(), Bank())));
    }

    /// <summary>
    ///     The mean needs the model dimension and the data must not be null.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_RejectsInvalidInput()
    {
        var wrongMean = new WeightsData("test-model", Dimension, [0.1f], [Bank()]);

        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(wrongMean));
        Assert.Throws<ArgumentNullException>(() => WeightsBlobWriter.Write(null!));
    }
}
