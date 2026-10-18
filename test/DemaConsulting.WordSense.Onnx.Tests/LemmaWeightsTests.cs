namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="LemmaWeights"/>: scoring, ranking and the confidence mapping.
/// </summary>
public class LemmaWeightsTests
{
    /// <summary>
    ///     A binary model scores its two senses as the negated and the plain activation.
    /// </summary>
    [Fact]
    public void LemmaWeights_Scores_Binary_ReturnsSymmetricScores()
    {
        var weights = new LemmaWeights(LemmaModelKind.Binary, [1f, 0f], [0.5f], 0.2f, 2);
        var scores = new float[2];

        weights.Scores([2f, 7f], scores);

        Assert.Equal([-2.5f, 2.5f], scores);
        Assert.Equal(2, weights.SenseCount);
    }

    /// <summary>
    ///     A one-versus-rest model scores one row per sense.
    /// </summary>
    [Fact]
    public void LemmaWeights_Scores_OneVersusRest_ReturnsOneScorePerRow()
    {
        var weights = new LemmaWeights(LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 1f, 1f, 1f], [0f, 0f, 1f], 0f, 2);
        var scores = new float[3];

        weights.Scores([3f, 4f], scores);

        Assert.Equal([3f, 4f, 8f], scores);
    }

    /// <summary>
    ///     Inconsistent shapes and non-finite values are rejected.
    /// </summary>
    [Fact]
    public void LemmaWeights_Constructor_InvalidShape_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LemmaWeights(LemmaModelKind.Binary, [1f], [0f], 0f, 2));
        Assert.Throws<ArgumentException>(() => new LemmaWeights(LemmaModelKind.Binary, [1f, float.NaN], [0f], 0f, 2));
        Assert.Throws<ArgumentException>(() => new LemmaWeights(LemmaModelKind.OneVersusRest, [1f, 1f], [0f], 0f, 2));
    }

    /// <summary>
    ///     Ranking returns the best index and its margin to the runner-up.
    /// </summary>
    [Fact]
    public void LemmaWeights_Rank_ReturnsBestAndMargin()
    {
        var (best, margin) = LemmaWeights.Rank([1f, 4f, 3f]);

        Assert.Equal(1, best);
        Assert.Equal(1f, margin);
    }

    /// <summary>
    ///     The confidence is 0.5 at the abstain margin, grows with the margin and stays within 0 and 1.
    /// </summary>
    [Fact]
    public void LemmaWeights_Confidence_IsMonotonicAndClamped()
    {
        Assert.Equal(0.5, LemmaWeights.Confidence(1.0, 1.0), 9);
        Assert.True(LemmaWeights.Confidence(2.0, 1.0) > LemmaWeights.Confidence(1.5, 1.0));
        Assert.True(LemmaWeights.Confidence(0.2, 1.0) < 0.5);
        Assert.Equal(1.0, LemmaWeights.Confidence(1000, 1.0));
        Assert.Equal(0.0, LemmaWeights.Confidence(-1000, 1.0));
        Assert.Equal(0.0, LemmaWeights.Confidence(double.NaN, 1.0));
    }
}
