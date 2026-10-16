namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="SenseWeights"/>.
/// </summary>
public class SenseWeightsTests
{
    private static SenseWeights Binary() => new(LemmaModelKind.Binary, [1f, -2f, 0.5f], [0.25f], 0.75f, 3);

    private static SenseWeights OneVersusRest() =>
        new(LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0.1f, -0.1f], 0.5f, 3);

    /// <summary>
    ///     A binary model scores {-s, +s}; the margin is twice the activation.
    /// </summary>
    [Fact]
    public void SenseWeights_Score_Binary_UsesActivationSign()
    {
        var model = Binary();

        var (best, margin) = model.Score([1f, 0f, 0f]);
        var (best2, _) = model.Score([-1f, 0f, 0f]);

        Assert.Equal(1, best);
        Assert.Equal(2 * 1.25f, margin);
        Assert.Equal(0, best2);
    }

    /// <summary>
    ///     A one-versus-rest model picks the highest row and reports the gap to the runner-up.
    /// </summary>
    [Fact]
    public void SenseWeights_Score_OneVersusRest_PicksBestRow()
    {
        var (best, margin) = OneVersusRest().Score([0f, 1f, 0f]);

        Assert.Equal(1, best);
        Assert.Equal(1.1f - 0f, margin, 4);
    }

    /// <summary>
    ///     A binary model's per-sense scores are {-a, +a} for activation a.
    /// </summary>
    [Fact]
    public void SenseWeights_Scores_Binary_ReturnsNegatedAndActivation()
    {
        var model = Binary();
        var scores = new float[2];

        model.Scores([1f, 0f, 0f], scores);

        Assert.Equal(-1.25f, scores[0]);
        Assert.Equal(1.25f, scores[1]);
        Assert.Equal(2, model.SenseCount);
    }

    /// <summary>
    ///     A one-versus-rest model's per-sense scores are each row's activation.
    /// </summary>
    [Fact]
    public void SenseWeights_Scores_OneVersusRest_ReturnsPerRowActivation()
    {
        var model = OneVersusRest();
        var scores = new float[3];

        model.Scores([0f, 1f, 0f], scores);

        Assert.Equal([0f, 1.1f, -0.1f], scores);
        Assert.Equal(3, model.SenseCount);
    }

    /// <summary>
    ///     A feature or scores span of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void SenseWeights_Scores_LengthMismatch_Throws()
    {
        var model = Binary();

        Assert.Throws<ArgumentException>(() => model.Scores(new float[2], new float[2]));
        Assert.Throws<ArgumentException>(() => model.Scores(new float[3], new float[3]));
        Assert.Throws<ArgumentException>(() => model.Score([1f]));
    }

    /// <summary>
    ///     Invalid weights are rejected at construction.
    /// </summary>
    [Fact]
    public void SenseWeights_Constructor_BadShapes_Throw()
    {
        Assert.Throws<ArgumentException>(() => new SenseWeights(LemmaModelKind.Binary, [1f, 2f], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new SenseWeights(LemmaModelKind.Binary, [float.NaN], [0f], 0f, 1));
        Assert.Throws<ArgumentException>(() => new SenseWeights(LemmaModelKind.OneVersusRest, [1f], [0f], 0f, 1));
    }

    /// <summary>
    ///     A binary score of zero activation selects the second sense, and a one-versus-rest tie selects the first
    ///     highest row.
    /// </summary>
    [Fact]
    public void SenseWeights_Score_TiesAndZero_SelectDocumentedSense()
    {
        var (zeroBest, zeroMargin) = new SenseWeights(LemmaModelKind.Binary, [1f], [0f], 0f, 1).Score([0f]);
        var (tieBest, tieMargin) = new SenseWeights(LemmaModelKind.OneVersusRest, [1f, 1f], [0f, 0f], 0f, 1).Score([1f]);

        Assert.Equal(1, zeroBest);
        Assert.Equal(0f, zeroMargin);
        Assert.Equal(0, tieBest);
        Assert.Equal(0f, tieMargin);
    }

    /// <summary>
    ///     Odd dimensions give the same scores as a scalar reference.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(384)]
    public void SenseWeights_Scores_OddDimension_MatchesScalarReference(int dimension)
    {
        var weights = Enumerable.Range(0, dimension).Select(i => ((i % 7) - 3) * 0.1f).ToArray();
        var feature = Enumerable.Range(0, dimension).Select(i => ((i % 5) - 2) * 0.3f).ToArray();
        var model = new SenseWeights(LemmaModelKind.Binary, weights, [0.5f], 0.1f, dimension);
        var scores = new float[2];

        model.Scores(feature, scores);

        var expected = 0.5;
        for (var i = 0; i < dimension; i++)
        {
            expected += weights[i] * (double)feature[i];
        }

        Assert.Equal(expected, scores[1], 3);
        Assert.Equal(-expected, scores[0], 3);
    }
}
