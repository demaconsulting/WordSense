namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="LemmaWeights"/>: scoring, ranking, normalization, groups, the confidence mapping and the
///     level decision with the thresholds of the lemma.
/// </summary>
public class LemmaWeightsTests
{
    private const float Never = LemmaWeights.Never;

    private static void AssertClose(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 12);
        }
    }

    /// <summary>
    ///     A one-versus-rest model scores one row per sense.
    /// </summary>
    [Fact]
    public void LemmaWeights_Scores_OneVersusRest_ReturnsOneScorePerRow()
    {
        // Arrange: three rows over two components, with a bias on the third
        var weights = new LemmaWeights([1f, 0f, 0f, 1f, 1f, 1f], [0f, 0f, 1f], 2);
        var scores = new float[3];

        // Act
        weights.Scores([3f, 4f], scores);

        // Assert: row dot feature plus bias
        Assert.Equal([3f, 4f, 8f], scores);
        Assert.Equal(3, weights.SenseCount);
        Assert.Equal(2, weights.Dimension);
    }

    /// <summary>
    ///     A feature or a score buffer of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void LemmaWeights_Scores_WrongLength_Throws()
    {
        // Arrange
        var weights = new LemmaWeights([1f, 0f, 0f, 1f], [0f, 0f], 2);

        // Act / Assert
        Assert.Throws<ArgumentException>(() => weights.Scores(new float[3], new float[2]));
        Assert.Throws<ArgumentException>(() => weights.Scores(new float[2], new float[3]));
    }

    /// <summary>
    ///     Inconsistent shapes and non-finite values are rejected.
    /// </summary>
    [Fact]
    public void LemmaWeights_Constructor_InvalidShape_Throws()
    {
        // Arrange / Act / Assert: one row only, a wrong weight count, a zero or negative dimension, NaN and infinite values
        Assert.Throws<ArgumentException>(() => new LemmaWeights([1f], [0f], 1));
        Assert.Throws<ArgumentException>(() => new LemmaWeights([1f, 1f, 1f], [0f, 0f], 2));
        Assert.Throws<ArgumentException>(() => new LemmaWeights([], [0f, 0f], -1));
        Assert.Throws<ArgumentException>(() => new LemmaWeights([], [0f, 0f], 0));
        Assert.Throws<ArgumentException>(() => new LemmaWeights([1f, float.NaN], [0f, 0f], 1));
        Assert.Throws<ArgumentException>(() => new LemmaWeights([1f, 1f], [0f, float.PositiveInfinity], 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaWeights(null!, [0f, 0f], 1));
        Assert.Throws<ArgumentNullException>(() => new LemmaWeights([1f, 1f], null!, 1));
    }

    /// <summary>
    ///     A threshold must be a finite margin that is not negative, or the sentinel that is never reached.
    /// </summary>
    /// <param name="fine">The fine threshold.</param>
    /// <param name="group">The group threshold.</param>
    /// <param name="valid">Whether the thresholds are accepted.</param>
    [Theory]
    [InlineData(-0.1f, 1f, false)]
    [InlineData(1f, -0.1f, false)]
    [InlineData(float.NaN, 1f, false)]
    [InlineData(1f, float.PositiveInfinity, false)]
    [InlineData(float.NegativeInfinity, 1f, false)]
    [InlineData(0f, 0f, true)]
    [InlineData(0.75f, Never, true)]
    [InlineData(Never, 0.25f, true)]
    public void LemmaWeights_Constructor_Thresholds_AreValidated(float fine, float group, bool valid)
    {
        // Arrange / Act
        var create = () => new LemmaWeights([1f, 1f], [0f, 0f], 1, fine, group);

        // Assert
        if (valid)
        {
            var weights = create();
            Assert.Equal(fine, weights.FineThreshold);
            Assert.Equal(group, weights.GroupThreshold);
        }
        else
        {
            Assert.Throws<ArgumentException>(create);
        }
    }

    /// <summary>
    ///     Thresholds that are not given are never reached.
    /// </summary>
    [Fact]
    public void LemmaWeights_Constructor_DefaultThresholds_AreNever()
    {
        // Arrange / Act
        var weights = new LemmaWeights([1f, 1f], [0f, 0f], 1);

        // Assert
        Assert.Equal(Never, weights.FineThreshold);
        Assert.Equal(Never, weights.GroupThreshold);
    }

    /// <summary>
    ///     Ranking returns the best index and its margin to the runner-up.
    /// </summary>
    [Fact]
    public void LemmaWeights_Rank_ReturnsBestAndMargin()
    {
        // Arrange / Act
        var (best, margin) = LemmaWeights.Rank([1f, 4f, 3f]);

        // Assert
        Assert.Equal(1, best);
        Assert.Equal(1f, margin);
    }

    /// <summary>
    ///     On equal scores the first sense wins and the margin is zero; a single score has an infinite margin.
    /// </summary>
    [Fact]
    public void LemmaWeights_Rank_TiesAndSingleScore_KeepInventoryOrderAndInfiniteMargin()
    {
        // Arrange / Act
        var tie = LemmaWeights.Rank([2f, 2f, 1f]);
        var single = LemmaWeights.Rank([5f]);

        // Assert
        Assert.Equal((0, 0f), tie);
        Assert.Equal(0, single.Best);
        Assert.True(float.IsPositiveInfinity(single.Margin));
        Assert.Throws<ArgumentException>(() => LemmaWeights.Rank([]));
    }

    /// <summary>
    ///     The softmax gives positive probabilities that sum to one and keep the order of the scores.
    /// </summary>
    [Fact]
    public void LemmaWeights_Softmax_SumsToOne()
    {
        // Arrange
        float[] scores = [1f, 2f, 0f, -3f];

        // Act
        var probabilities = LemmaWeights.Softmax(scores);

        // Assert: normalized, positive and equal to the plain formula
        Assert.Equal(1.0, probabilities.Sum(), 12);
        Assert.All(probabilities, p => Assert.True(p > 0));
        AssertClose(Fixtures.Softmax([1, 2, 0, -3]), probabilities);
        Assert.True(probabilities[1] > probabilities[0] && probabilities[0] > probabilities[2] && probabilities[2] > probabilities[3]);
        Assert.Throws<ArgumentException>(() => LemmaWeights.Softmax([]));
    }

    /// <summary>
    ///     Very large scores do not overflow: the softmax is shifted by the maximum.
    /// </summary>
    [Fact]
    public void LemmaWeights_Softmax_LargeScores_AreStable()
    {
        // Arrange / Act
        var probabilities = LemmaWeights.Softmax([1000f, 1000f, 0f]);

        // Assert
        Assert.Equal(0.5, probabilities[0], 12);
        Assert.Equal(0.5, probabilities[1], 12);
        Assert.Equal(0.0, probabilities[2], 12);
    }

    /// <summary>
    ///     The score of a group is the sum of the fine probabilities of its senses; a sense in no group stands alone.
    /// </summary>
    [Fact]
    public void LemmaWeights_GroupScores_SumTheFineProbabilitiesOfTheGroup()
    {
        // Arrange: senses 0 and 2 form group 0, sense 1 is in group 1, sense 3 is in no group
        double[] probabilities = [0.4, 0.3, 0.2, 0.1];
        int[] groups = [0, 1, 0, -1];

        // Act
        var scores = LemmaWeights.GroupScores(probabilities, groups);

        // Assert
        AssertClose([0.6, 0.3, 0.6, 0.1], scores);
        Assert.Throws<ArgumentException>(() => LemmaWeights.GroupScores(probabilities, new[] { 0 }));
    }

    /// <summary>
    ///     The margin of a group is its score minus the best score of any other group; senses of one group never
    ///     compete with each other, and a lone group has an infinite margin.
    /// </summary>
    [Fact]
    public void LemmaWeights_GroupMargin_IsTheScoreOverTheBestOtherGroup()
    {
        // Arrange: groups [0, 1, 0, -1] with the scores of the previous test
        double[] scores = [0.6, 0.3, 0.6, 0.1];
        int[] groups = [0, 1, 0, -1];

        // Act
        var best = LemmaWeights.GroupMargin(scores, groups, 0);
        var second = LemmaWeights.GroupMargin(scores, groups, 1);
        var alone = LemmaWeights.GroupMargin([1.0], [-1], 0);
        var oneGroup = LemmaWeights.GroupMargin([1.0, 1.0], [0, 0], 0);

        // Assert: group 0 leads group 1 by 0.3; group 1 trails group 0 by 0.3; no other group means infinity
        Assert.Equal(0.3, best, 12);
        Assert.Equal(-0.3, second, 12);
        Assert.True(double.IsPositiveInfinity(alone));
        Assert.True(double.IsPositiveInfinity(oneGroup));
    }

    /// <summary>
    ///     The confidence is 0.5 at the threshold, grows with the margin and stays within 0 and 1.
    /// </summary>
    [Fact]
    public void LemmaWeights_Confidence_IsMonotonicAndClamped()
    {
        // Arrange / Act / Assert: exactly one half at the threshold, whatever the threshold
        Assert.Equal(0.5, LemmaWeights.Confidence(1.0, 1.0), 9);
        Assert.Equal(0.5, LemmaWeights.Confidence(0.0, 0.0), 9);
        Assert.Equal(0.5, LemmaWeights.Confidence(0.3, 0.3), 9);

        // Monotonic in the margin
        Assert.True(LemmaWeights.Confidence(2.0, 1.0) > LemmaWeights.Confidence(1.5, 1.0));
        Assert.True(LemmaWeights.Confidence(0.2, 1.0) < 0.5);
        Assert.True(LemmaWeights.Confidence(0.1, 0.0) > LemmaWeights.Confidence(0.05, 0.0));

        // Clamped, and zero for a NaN margin
        Assert.Equal(1.0, LemmaWeights.Confidence(1000, 1.0));
        Assert.Equal(0.0, LemmaWeights.Confidence(-1000, 1.0));
        Assert.Equal(0.0, LemmaWeights.Confidence(double.NaN, 1.0));
        Assert.Equal(1.0, LemmaWeights.Confidence(double.PositiveInfinity, 1.0));
    }

    /// <summary>
    ///     The level is the sense at or above the fine threshold, else the group at or above the group threshold when
    ///     the best sense has one, else the part of speech.
    /// </summary>
    [Fact]
    public void LemmaWeights_Level_StepsFromSenseToGroupToPartOfSpeech()
    {
        // Arrange: fine threshold 1.0, group threshold 0.5
        var weights = new LemmaWeights([1f, 1f], [0f, 0f], 1, 1.0f, 0.5f);

        // Act / Assert
        Assert.Equal(AnswerKind.Sense, weights.Level(1.0, true, true, 0.0));
        Assert.Equal(AnswerKind.Sense, weights.Level(2.0, true, false, 0.0));
        Assert.Equal(AnswerKind.Group, weights.Level(0.9, true, true, 0.5));
        Assert.Equal(AnswerKind.PartOfSpeech, weights.Level(0.9, true, true, 0.49));
        Assert.Equal(AnswerKind.PartOfSpeech, weights.Level(0.9, true, false, 5.0));
    }

    /// <summary>
    ///     A best sense that lies outside the selected analysis is never answered, whatever the margins.
    /// </summary>
    [Fact]
    public void LemmaWeights_Level_BestOutsideSelectedAnalysis_IsPartOfSpeech()
    {
        // Arrange
        var weights = new LemmaWeights([1f, 1f], [0f, 0f], 1, 0.0f, 0.0f);

        // Act / Assert
        Assert.Equal(AnswerKind.PartOfSpeech, weights.Level(9.0, false, true, 9.0));
    }

    /// <summary>
    ///     The level uses the thresholds of the lemma it is asked on: two lemmas decide differently for the same
    ///     margins, and a threshold that is <see cref="LemmaWeights.Never"/> is never met by a finite margin.
    /// </summary>
    [Fact]
    public void LemmaWeights_Level_UsesTheLemmasOwnThresholds()
    {
        // Arrange: an easy lemma, a hard lemma and a lemma that never reached its targets
        var easy = new LemmaWeights([1f, 1f], [0f, 0f], 1, 0.5f, 0.2f);
        var hard = new LemmaWeights([1f, 1f], [0f, 0f], 1, 3.0f, 0.9f);
        var never = new LemmaWeights([1f, 1f], [0f, 0f], 1, Never, Never);

        // Act / Assert: a fine margin of 1.0 with a group margin of 0.5
        Assert.Equal(AnswerKind.Sense, easy.Level(1.0, true, true, 0.5));
        Assert.Equal(AnswerKind.PartOfSpeech, hard.Level(1.0, true, true, 0.5));
        Assert.Equal(AnswerKind.PartOfSpeech, never.Level(1.0, true, true, 0.5));

        // A group margin of 0.5 passes the easy group threshold only; a margin of 0.95 passes the hard one too
        Assert.Equal(AnswerKind.Group, new LemmaWeights([1f, 1f], [0f, 0f], 1, 3.0f, 0.2f).Level(1.0, true, true, 0.5));
        Assert.Equal(AnswerKind.Group, hard.Level(1.0, true, true, 0.95));

        // Never is never met, not even by a huge margin; a lemma with only the fine level skips the group level
        Assert.Equal(AnswerKind.PartOfSpeech, never.Level(1e30, true, true, 1e30));
        Assert.Equal(AnswerKind.Sense, new LemmaWeights([1f, 1f], [0f, 0f], 1, 0.5f, Never).Level(1.0, true, true, 0.9));
        Assert.Equal(AnswerKind.PartOfSpeech, new LemmaWeights([1f, 1f], [0f, 0f], 1, 3.0f, Never).Level(1.0, true, true, 0.9));
    }
}
