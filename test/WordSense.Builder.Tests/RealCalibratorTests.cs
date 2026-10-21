using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the real-text calibration of the thresholds, on one-dimensional lemmas whose margin is twice the
///     absolute value of the feature.
/// </summary>
public sealed class RealCalibratorTests
{
    private static readonly byte[] Hash = new byte[32];

    private static SenseInfo[] Walk => SenseFixtures.Senses("walk", PartOfSpeech.Noun, 2);

    private static SenseInfo[] Jog => SenseFixtures.Senses("run", PartOfSpeech.Noun, 2);

    /// <summary>
    ///     Builds a lemma with two senses that score <c>x</c> and <c>-x</c>, so the best sense is the first for a
    ///     positive feature and the margin is <c>2|x|</c>.
    /// </summary>
    private static WeightedLemma Lemma(SenseInfo first, SenseInfo second, float fine, float group = LemmaWeights.Never) =>
        new(first.Lemma, [first.SenseId, second.SenseId], new LemmaWeights([1f, -1f], [0f, 0f], 1, fine, group));

    private static WeightsData Data(params WeightedLemma[] lemmas) => new("stub", Hash, 1, [0f], lemmas);

    /// <summary>
    ///     A calibration row whose best sense is the first and whose margin is the given one; it is answered right when
    ///     <paramref name="correct"/> is true.
    /// </summary>
    private static (CorpusRow Row, float[] Feature) Observation(SenseInfo first, SenseInfo second, float margin, bool correct, int number) =>
        (SenseFixtures.Row(correct ? first : second, number), [margin / 2f]);

    private static (WeightsData Data, CalibrationReport Report) Run(WeightsData data, IEnumerable<(CorpusRow Row, float[] Feature)> rows, CalibrationOptions options)
    {
        var list = rows.ToList();
        return RealCalibrator.Apply(data, list.Select(r => r.Row).ToList(), list.Select(r => r.Feature).ToList(), options);
    }

    /// <summary>
    ///     Rows below the margin 2 are half wrong and rows from the margin 4 are right: the first threshold 1 gives
    ///     poor precision, and the pooled shift moves the thresholds until the answers reach the target.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_RaisesThresholdsUntilThePooledPrecisionReachesTheTarget()
    {
        // Arrange: two lemmas with 25 rows each (no threshold of their own)
        var (w, r) = (Walk, Jog);
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 15; i++)
        {
            var low = 1f + (i * 0.05f);
            rows.Add(Observation(w[0], w[1], low, i % 2 == 0 && i < 14, i));
            rows.Add(Observation(r[0], r[1], low, i % 2 == 0 && i < 14, 100 + i));
        }

        for (var i = 0; i < 10; i++)
        {
            rows.Add(Observation(w[0], w[1], 4f + i, true, 200 + i));
            rows.Add(Observation(r[0], r[1], 4f + i, true, 300 + i));
        }

        var data = Data(Lemma(w[0], w[1], 1f), Lemma(r[0], r[1], 1f));

        // Act
        var (calibrated, report) = Run(data, rows, new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 10));

        // Assert: the pooled shift is the distance of the first sure row (3), and the answers that remain are all right
        Assert.True(report.FineReached);
        Assert.Equal(3.0, report.FineShift, 3);
        Assert.All(calibrated.Lemmas, l => Assert.InRange(l.Weights.FineThreshold, 3.99f, 4.01f));
        Assert.Equal(20, report.AnswersAfter);
        Assert.Equal(20, report.CorrectAfter);
        Assert.True(report.AnswersBefore > report.AnswersAfter);

        // Thresholds that already reach the target do not move
        var sure = Run(data, rows.Where(x => x.Feature[0] >= 2f), new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 10));
        Assert.Equal(0.0, sure.Report.FineShift);
        Assert.All(sure.Data.Lemmas, l => Assert.Equal(1f, l.Weights.FineThreshold));
    }

    /// <summary>
    ///     A lemma with enough calibration rows keeps the higher of its own threshold and the adjusted one; a lemma
    ///     with fewer rows gets the pooled shift.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_LemmaWithEnoughRows_KeepsTheHigherOfItsOwnAndTheAdjustedThreshold()
    {
        // Arrange: walk has 25 rows (10 wrong below margin 3, 15 right from 5), run has 6 sure rows
        var (w, r) = (Walk, Jog);
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 10; i++)
        {
            rows.Add(Observation(w[0], w[1], 1f + (i * 0.1f), false, i));
        }

        for (var i = 0; i < 15; i++)
        {
            rows.Add(Observation(w[0], w[1], 5f + i, true, 100 + i));
        }

        for (var i = 0; i < 6; i++)
        {
            rows.Add(Observation(r[0], r[1], 2f + i, true, 200 + i));
        }

        var data = Data(Lemma(w[0], w[1], 1f), Lemma(r[0], r[1], 1f));

        // Act
        var (calibrated, report) = Run(data, rows, new CalibrationOptions(MinOwnRows: 20, MinPooledAnswered: 3));

        // Assert: walk takes its own threshold (5), run keeps the result of its sure rows (no shift needed)
        Assert.Equal(1, report.OwnThresholds);
        Assert.InRange(calibrated.Lemmas.Single(l => l.Lemma == "walk").Weights.FineThreshold, 4.99f, 5.01f);
        Assert.Equal(1f, calibrated.Lemmas.Single(l => l.Lemma == "run").Weights.FineThreshold);
    }

    /// <summary>
    ///     Without calibration rows every threshold is unchanged, and rows without features are refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_NoCalibrationRows_KeepsTheThresholds()
    {
        // Arrange
        var w = Walk;
        var data = Data(Lemma(w[0], w[1], 2.5f, 0.25f));

        // Act
        var (calibrated, report) = RealCalibrator.Apply(data, [], [], new CalibrationOptions());

        // Assert
        Assert.Equal(2.5f, calibrated.Lemmas[0].Weights.FineThreshold);
        Assert.Equal(0.25f, calibrated.Lemmas[0].Weights.GroupThreshold);
        Assert.Equal(0, report.Rows);
        Assert.Throws<ArgumentException>(() => RealCalibrator.Apply(data, [SenseFixtures.Row(w[0], 1)], [], new CalibrationOptions()));
    }

    /// <summary>
    ///     A lemma that was never answered stays never answered whatever the rows say, and two runs give the same
    ///     thresholds.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_NeverStaysNever_AndIsDeterministic()
    {
        // Arrange
        var (w, r) = (Walk, Jog);
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 12; i++)
        {
            rows.Add(Observation(w[0], w[1], 2f + i, false, i));
            rows.Add(Observation(r[0], r[1], 2f + i, i % 3 != 0, 100 + i));
        }

        var data = Data(Lemma(w[0], w[1], LemmaWeights.Never), Lemma(r[0], r[1], 1f));
        var options = new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 5);

        // Act
        var first = Run(data, rows, options);
        var second = Run(data, rows, options);

        // Assert
        Assert.Equal(LemmaWeights.Never, first.Data.Lemmas.Single(l => l.Lemma == "walk").Weights.FineThreshold);
        Assert.Equal(first.Data.Lemmas.Select(l => l.Weights.FineThreshold), second.Data.Lemmas.Select(l => l.Weights.FineThreshold));
        Assert.Equal(first.Report, second.Report);
        Assert.True(first.Data.Lemmas.Single(l => l.Lemma == "run").Weights.FineThreshold >= 1f);
    }

    /// <summary>
    ///     The group threshold moves when enough group answers exist to verify it, and is kept (and reported as
    ///     unverified) when there are fewer.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_GroupThreshold_MovesOnlyWithEnoughAnswers()
    {
        // Arrange: two senses of different groups; the fine level is never answered, so every row is a group answer.
        // The group margin of the feature x is tanh(x): rows near 0.3 are wrong and rows from 1.5 are right.
        var (a1, _, b, _) = SenseFixtures.Grouped("test");
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 12; i++)
        {
            rows.Add((SenseFixtures.Row(b, i), [0.3f + (i * 0.005f)]));
            rows.Add((SenseFixtures.Row(a1, 100 + i), [1.5f + (i * 0.05f)]));
        }

        var data = Data(Lemma(a1, b, LemmaWeights.Never, 0.2f));

        // Act
        var moved = Run(data, rows, new CalibrationOptions(MinPooledAnswered: 10));
        var kept = Run(data, rows, new CalibrationOptions(MinPooledAnswered: 100));

        // Assert
        Assert.True(moved.Report.GroupVerified);
        Assert.True(moved.Data.Lemmas[0].Weights.GroupThreshold > 0.2f);
        Assert.False(kept.Report.GroupVerified);
        Assert.Equal(0.2f, kept.Data.Lemmas[0].Weights.GroupThreshold);
    }
}
