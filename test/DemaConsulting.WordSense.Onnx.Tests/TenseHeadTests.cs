namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="TenseHead"/>: the probability of past of a verb from the pooled vectors of the word, and from
///     the vectors of its neighbors for the neighbor layout.
/// </summary>
public class TenseHeadTests
{
    private const int Dimension = 4;
    private const double Tolerance = 1e-9;

    private static TenseHead Head(int layout, float[] weights, float bias)
    {
        var spec = HeadSpec.Create(Dimension) with { Sections = [(PosHeadBlob.TenseSection, false, HeadBlobWriter.TensePayload(layout, 0.03f, weights, bias))] };
        return new PosHead(PosHeadBlob.Read(HeadBlobWriter.Write(spec))).Tense!;
    }

    private static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

    /// <summary>
    ///     The word layout takes the dot product of the weights and the vector of the word plus the bias, through the logistic
    ///     function, and ignores the neighbors.
    /// </summary>
    [Fact]
    public void TenseHead_PastProbability_WordLayout_IsTheLogisticOfTheDotProduct()
    {
        // Arrange: exact half-precision weights
        var head = Head(PosHeadBlob.TenseWord, [0.5f, -0.25f, 1f, 0f], 0.25f);
        double[] word = [2, 4, 1, 100];

        // Act
        var alone = head.PastProbability(null, word, null);
        var withNeighbors = head.PastProbability([9, 9, 9, 9], word, [7, 7, 7, 7]);

        // Assert: 0.25 + 1 - 1 + 1 + 0
        Assert.Equal(Sigmoid(1.25), alone, Tolerance);
        Assert.Equal(alone, withNeighbors, Tolerance);
    }

    /// <summary>
    ///     The neighbor layout reads the previous word, the word and the next word in that order, and a missing neighbor
    ///     counts as zeros.
    /// </summary>
    [Fact]
    public void TenseHead_PastProbability_NeighborLayout_ReadsPreviousWordAndNext()
    {
        // Arrange: each block reads a different component
        float[] weights = [1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 4];
        var head = Head(PosHeadBlob.TenseWordAndNeighbors, weights, 0f);
        double[] previous = [1, 1, 1, 1];
        double[] word = [1, 1, 1, 1];
        double[] next = [1, 1, 1, 1];

        // Act / Assert
        Assert.Equal(Sigmoid(7), head.PastProbability(previous, word, next), Tolerance);
        Assert.Equal(Sigmoid(6), head.PastProbability(null, word, next), Tolerance);
        Assert.Equal(Sigmoid(3), head.PastProbability(previous, word, null), Tolerance);
        Assert.Equal(Sigmoid(2), head.PastProbability(null, word, null), Tolerance);
    }

    /// <summary>
    ///     The scores are bounded and a vector of another size or a missing word is rejected.
    /// </summary>
    [Fact]
    public void TenseHead_PastProbability_ExtremeScoresAndBadVectors_AreHandled()
    {
        // Arrange
        var head = Head(PosHeadBlob.TenseWord, [1f, 0f, 0f, 0f], 0f);

        // Act / Assert
        Assert.Equal(1.0, head.PastProbability(null, [1000, 0, 0, 0], null), Tolerance);
        Assert.Equal(0.0, head.PastProbability(null, [-1000, 0, 0, 0], null), Tolerance);
        Assert.Throws<ArgumentException>(() => head.PastProbability(null, [1, 2, 3], null));
        Assert.Throws<ArgumentException>(() => head.PastProbability([1, 2], [1, 2, 3, 4], null));
        Assert.Throws<ArgumentException>(() => head.PastProbability(null, [1, 2, 3, 4], [1]));
        Assert.Throws<ArgumentNullException>(() => head.PastProbability(null, null!, null));
    }
}
