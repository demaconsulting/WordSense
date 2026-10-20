using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the writer of the weights blob, checked against the reader of the Onnx package, and for the accuracy
///     gate of the half-precision storage.
/// </summary>
public sealed class WeightsBlobWriterTests
{
    private const int Dimension = 3;

    /// <summary>
    ///     The largest relative error of a value stored as a half-precision float (2 to the power of -11).
    /// </summary>
    private const double HalfRelativeError = 1.0 / 2048;

    private static readonly byte[] InventoryHash = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static WeightedLemma Bank() => new(
        "bank",
        ["bank#noun", "bank#verb"],
        new LemmaWeights([0.5f, -0.25f, 1f, -0.1f, 0.3f, 0.7f], [0.125f, -0.125f], Dimension, 0.75f, LemmaWeights.Never));

    private static WeightedLemma Bow() => new(
        "bow",
        ["bow#bend", "bow#bend-verb", "bow#weapon"],
        new LemmaWeights(
            [1f, 0f, -1f, 0.5f, 0.25f, -0.5f, -1f, 2f, 0.123456f],
            [0.5f, -0.5f, 0f],
            Dimension,
            0.4f,
            0.2f),
        [new FormChooser("left", ["leave", "left"], new LemmaWeights([0.3f, -0.6f, 0.9f, 0.11f, 0.22f, -0.33f], [0.25f, -0.25f], Dimension, 0.6f))]);

    private static WeightsData Sample(params WeightedLemma[] lemmas) =>
        new("test-model", InventoryHash, Dimension, [0.1f, -0.2f, 0.3f], lemmas);

    private static void AssertHalf(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0, (Math.Abs(expected[i]) * HalfRelativeError) + 1e-7);
        }
    }

    /// <summary>
    ///     The thresholds, the sense ids, the biases and a form chooser are read back exactly, the weights within the
    ///     rounding error of half precision, and the lemmas in ordinal order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_RoundTripsThroughTheReader()
    {
        // Arrange
        var data = Sample(Bow(), Bank());

        // Act
        var read = WeightsBlob.Read(WeightsBlobWriter.Write(data));

        // Assert
        Assert.Equal("test-model", read.ModelId);
        Assert.Equal(InventoryHash, read.InventoryHash);
        Assert.Equal(Dimension, read.Dimension);
        Assert.Equal(data.Mean, read.Mean);
        Assert.Equal(["bank", "bow"], read.Lemmas.Select(l => l.Lemma));

        var bank = read.Lemmas[0];
        Assert.Equal(Bank().SenseIds, bank.SenseIds);
        AssertHalf(Bank().Weights.Weights, bank.Weights.Weights);
        Assert.Equal(Bank().Weights.Bias, bank.Weights.Bias);
        Assert.Equal(0.75f, bank.Weights.FineThreshold);
        Assert.Equal(LemmaWeights.Never, bank.Weights.GroupThreshold);
        Assert.Equal(Dimension, bank.Weights.Dimension);
        Assert.Empty(bank.Choosers);

        var bow = read.Lemmas[1];
        Assert.Equal(Bow().SenseIds, bow.SenseIds);
        AssertHalf(Bow().Weights.Weights, bow.Weights.Weights);
        Assert.Equal(Bow().Weights.Bias, bow.Weights.Bias);
        Assert.Equal(0.4f, bow.Weights.FineThreshold);
        Assert.Equal(0.2f, bow.Weights.GroupThreshold);
        Assert.Equal(3, bow.Weights.SenseCount);
        var chooser = Assert.Single(bow.Choosers);
        Assert.Equal("left", chooser.Form);
        Assert.Equal(["leave", "left"], chooser.Labels);
        Assert.Equal(0.6f, chooser.Weights.FineThreshold);
        AssertHalf(Bow().Choosers[0].Weights.Weights, chooser.Weights.Weights);
        Assert.Equal(Bow().Choosers[0].Weights.Bias, chooser.Weights.Bias);
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
        Assert.Equal(InventoryHash, read.InventoryHash);
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
    ///     The mean needs the model dimension, the hash needs 32 bytes and the data must not be null.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_RejectsInvalidInput()
    {
        var wrongMean = new WeightsData("test-model", InventoryHash, Dimension, [0.1f], [Bank()]);
        var wrongHash = new WeightsData("test-model", [1, 2, 3], Dimension, [0.1f, 0.2f, 0.3f], [Bank()]);

        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(wrongMean));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(wrongHash));
        Assert.Throws<ArgumentNullException>(() => WeightsBlobWriter.Write(null!));
    }

    /// <summary>
    ///     A lemma whose sense id count differs from its row count is refused, and so is a chooser whose label count
    ///     differs from its row count or a weight that does not fit half precision.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_SenseCountMismatch_Throws()
    {
        var tooMany = new WeightedLemma("bank", ["bank#a", "bank#b", "bank#c"], Bank().Weights);
        var tooFew = new WeightedLemma("bank", ["bank#a"], Bank().Weights);
        var badChooser = new WeightedLemma("bank", Bank().SenseIds, Bank().Weights, [new FormChooser("left", ["leave", "left", "other"], Bow().Choosers[0].Weights)]);
        var huge = new WeightedLemma("bank", Bank().SenseIds, new LemmaWeights([1e6f, 0f, 0f, 0f, 0f, 0f], [0f, 0f], Dimension));

        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(tooMany)));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(tooFew)));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(badChooser)));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(huge)));
        Assert.NotEmpty(WeightsBlobWriter.Write(Sample(Bank())));
    }

    /// <summary>
    ///     Storing the weights in half precision keeps the fine accuracy within the gate: a lemma trained on synthetic
    ///     data is scored on rows that were never trained on with the full-precision weights and with the weights read
    ///     back from the blob, and the two accuracies differ by no more than 0.2 percentage point.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_ReducedPrecision_StaysWithinTheAccuracyGate()
    {
        // Arrange: three senses of a lemma in three overlapping directions of a 6-dimensional space
        var senses = new[] { SenseFixtures.Senses("walk", PartOfSpeech.Noun, 2), SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1) }.SelectMany(s => s).ToArray();
        (CorpusRow[] Rows, double[][] Pooled) Make(int firstNumber, int perSense)
        {
            var rows = new List<CorpusRow>();
            var pooled = new List<double[]>();
            for (var s = 0; s < senses.Length; s++)
            {
                for (var i = 0; i < perSense; i++)
                {
                    var vector = new double[6];
                    vector[s] = 1;
                    for (var d = 0; d < vector.Length; d++)
                    {
                        vector[d] += 0.6 * Math.Sin((s * 31.0) + ((firstNumber + i) * 12.9898) + (d * 78.233));
                    }

                    rows.Add(SenseFixtures.Row(senses[s], firstNumber + i));
                    pooled.Add(vector);
                }
            }

            return ([.. rows], [.. pooled]);
        }

        var (anchors, anchorVectors) = Make(0, 30);
        var (holdout, holdoutVectors) = Make(1000, 100);
        var full = WeightsTrainer.TrainDense("m", new byte[32], anchorVectors, anchors, new TrainingOptions(Parallelism: 1)).Data;
        var reduced = WeightsBlob.Read(WeightsBlobWriter.Write(full));

        // Act
        var result = PrecisionGate.Measure(full, reduced, holdout, holdoutVectors);

        // Assert
        Assert.Equal(holdout.Length, result.Rows);
        Assert.InRange(result.FullAccuracy, 40, 100);
        Assert.True(Math.Abs(result.LossPoints) <= PrecisionGate.MaxDifferencePoints, PrecisionGate.Describe(result));
        PrecisionGate.Enforce(result);
    }

    /// <summary>
    ///     The gate counts only the rows of lemmas that have weights, describes both accuracies, and fails when they
    ///     differ by more than 0.2 point.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_PrecisionGate_MeasuresDescribesAndEnforces()
    {
        // Arrange
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var other = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        var weights = new WeightsData("m", InventoryHash, 2, [0f, 0f], [new WeightedLemma("walk", [noun.SenseId, "walk#other"], new LemmaWeights([1f, 0f, -1f, 0f], [0f, 0f], 2))]);
        CorpusRow[] rows = [SenseFixtures.Row(noun, 1), SenseFixtures.Row(other, 2)];
        double[][] pooled = [[1, 0.1], [1, 0.1]];

        // Act
        var result = PrecisionGate.Measure(weights, weights, rows, pooled);
        var bad = new GateResult(1000, 900, 890);

        // Assert
        Assert.Equal(new GateResult(1, 1, 1), result);
        Assert.Contains("100.00%", PrecisionGate.Describe(result), StringComparison.Ordinal);
        PrecisionGate.Enforce(result);
        var failure = Assert.Throws<InvalidDataException>(() => PrecisionGate.Enforce(bad));
        Assert.Contains("90.00%", failure.Message, StringComparison.Ordinal);
        Assert.Contains("89.00%", failure.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PrecisionGate.Measure(weights, weights, rows, [[1, 0]]));
    }
}
