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
    ///     With strata of lemmas the shift is fitted per stratum: a stratum of poor precision gets its own shift, a stratum
    ///     whose answers already reach the target gets none, and the thresholds of a stratum are the same with or without
    ///     the other one (adding lemmas never moves the thresholds of the lemmas trained before).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_Strata_AreCalibratedByTheirOwnRows()
    {
        // Arrange: walk (base) is half wrong below margin 3 and right from 4; run (stage 1) is always right
        var (w, r) = (Walk, Jog);
        var walkRows = new List<(CorpusRow Row, float[] Feature)>();
        var runRows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 15; i++)
        {
            walkRows.Add(Observation(w[0], w[1], 1f + (i * 0.05f), i % 2 == 0 && i < 14, i));
            runRows.Add(Observation(r[0], r[1], 1f + (i * 0.05f), true, 100 + i));
        }

        for (var i = 0; i < 10; i++)
        {
            walkRows.Add(Observation(w[0], w[1], 4f + i, true, 200 + i));
            runRows.Add(Observation(r[0], r[1], 4f + i, true, 300 + i));
        }

        var data = Data(Lemma(w[0], w[1], 1f), Lemma(r[0], r[1], 1f));
        var options = new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 10, Stratum: lemma => lemma == "run" ? "stage 1" : "base");

        // Act
        var both = Run(data, [.. walkRows, .. runRows], options);
        var baseOnly = Run(Data(Lemma(w[0], w[1], 1f)), walkRows, options);

        // Assert: one shift per stratum, in the order of the names; walk is calibrated as if run were not there
        Assert.Equal(["base", "stage 1"], both.Report.Strata.Select(s => s.Name));
        Assert.Equal(3.0, both.Report.Strata[0].FineShift, 3);
        Assert.Equal(0.0, both.Report.Strata[1].FineShift, 3);
        Assert.InRange(both.Data.Lemmas.Single(l => l.Lemma == "walk").Weights.FineThreshold, 3.99f, 4.01f);
        Assert.Equal(1f, both.Data.Lemmas.Single(l => l.Lemma == "run").Weights.FineThreshold);
        Assert.Equal(baseOnly.Data.Lemmas.Single().Weights.FineThreshold, both.Data.Lemmas.Single(l => l.Lemma == "walk").Weights.FineThreshold);
        Assert.Equal(baseOnly.Report.Strata[0], both.Report.Strata[0] with { Rows = baseOnly.Report.Strata[0].Rows });
        Assert.Contains("stratum 'stage 1'", both.Report.Describe(), StringComparison.Ordinal);

        // A single pooled stratum (no function) calibrates both lemmas with the same shift
        var pooled = Run(data, [.. walkRows, .. runRows], new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 10));
        Assert.Single(pooled.Report.Strata);
        Assert.Equal(pooled.Data.Lemmas[0].Weights.FineThreshold, pooled.Data.Lemmas[1].Weights.FineThreshold);
    }

    /// <summary>
    ///     The stage of a lemma is the stage of the highest wave file that holds one of its anchors; files that are not
    ///     wave files do not count, and a lemma without a wave file is in the base stage.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_StagesOf_NamesTheStageOfTheHighestWaveFile()
    {
        // Arrange
        var walk = Walk;
        CorpusRow Row(SenseInfo sense, string file) => SenseFixtures.Row(sense, 1) with { Source = file };
        var rows = new[]
        {
            Row(walk[0], "quota-w4c-real.tsv"),
            Row(Jog[0], "quota-w49-written.tsv"),
            Row(Jog[1], "quota-w50-real.tsv"),
            Row(SenseFixtures.Senses("stand", PartOfSpeech.Noun, 2)[0], "quota-w51-written.tsv"),
            Row(SenseFixtures.Senses("stand", PartOfSpeech.Noun, 2)[1], "quota-w3-real.tsv"),
            Row(SenseFixtures.Senses("hold", PartOfSpeech.Noun, 1)[0], "batch-1.tsv"),
            Row(SenseFixtures.Senses("keep", PartOfSpeech.Noun, 1)[0], "quota-w100-real.tsv"),
        };

        // Act
        var stage = Corpus.StagesOf(rows);

        // Assert
        Assert.Equal("base", stage("walk"));
        Assert.Equal("stage 1", stage("run"));
        Assert.Equal("stage 1", stage("stand"));
        Assert.Equal("base", stage("hold"));
        Assert.Equal("stage 2", stage("keep"));
        Assert.Equal("base", stage("unknown"));
    }

    private static float ScoreOf(SenseInfo sense, SenseInfo best, SenseInfo determiner)
    {
        if (sense.SenseId == best.SenseId)
        {
            return 3f;
        }

        return sense.SenseId == determiner.SenseId ? 2.5f : 0f;
    }

    /// <summary>
    ///     The calibration scores a row as the classifier does: a closed-class sense is not a candidate, so the margin of
    ///     the best open-class sense is the gap to the next open-class sense, not to the closed-class one.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_ClosedClassSenses_AreNotCandidatesOfTheMargin()
    {
        // Arrange: little has open-class senses and the determiner; the best open sense scores 3, the determiner 2.5, the rest 0
        var senses = Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf("little", pos)).ToArray();
        var determiner = Assert.Single(senses, s => s.PartOfSpeech == PartOfSpeech.Determiner);
        var best = senses.First(s => s.PartOfSpeech == PartOfSpeech.Adjective);
        var weights = senses.Select(s => ScoreOf(s, best, determiner)).ToArray();
        var lemma = new WeightedLemma("little", [.. senses.Select(s => s.SenseId)], new LemmaWeights(weights, new float[senses.Length], 1, 1f));
        var rows = new List<(CorpusRow Row, float[] Feature)> { (SenseFixtures.Row(best, 1), [1f]) };

        // Act
        var (_, report) = Run(Data(lemma), rows, new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1));

        // Assert: with all senses the margin would be 0.5 (below the threshold 1); among the open-class candidates it is 3
        Assert.Equal(1, report.Rows);
        Assert.Equal(1, report.AnswersBefore);
        Assert.Equal(1, report.CorrectBefore);
    }

    /// <summary>
    ///     A row whose word has fewer than two open-class candidate senses for the lemma is not a calibration answer, as
    ///     the classifier does not score it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_FewerThanTwoCandidates_IsNotObserved()
    {
        // Arrange: the lemma has only the determiner and one adjective sense as columns
        var senses = Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf("little", pos)).ToArray();
        var determiner = Assert.Single(senses, s => s.PartOfSpeech == PartOfSpeech.Determiner);
        var adjective = senses.First(s => s.PartOfSpeech == PartOfSpeech.Adjective);
        var lemma = new WeightedLemma("little", [adjective.SenseId, determiner.SenseId], new LemmaWeights([3f, 2f], [0f, 0f], 1, 1f));

        // Act
        var (_, report) = Run(Data(lemma), [(SenseFixtures.Row(adjective, 1), [1f])], new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1));

        // Assert: one open-class candidate only
        Assert.Equal(0, report.Rows);
    }

    private static ClassifyResult BasicLittle(double determiner)
    {
        var details = WordLexicon.Lookup("little");
        var determinerDetails = details.First(d => d.PartOfSpeech == PartOfSpeech.Determiner);
        var adjectiveDetails = details.First(d => d.PartOfSpeech == PartOfSpeech.Adjective);
        return new ClassifyResult("little", true, [new(determinerDetails, determiner, []), new(adjectiveDetails, 1 - determiner, [])], determiner >= 0.5 ? determinerDetails : adjectiveDetails, SenseLevel.PartOfSpeech, Math.Max(determiner, 1 - determiner), null, null);
    }

    private static (WeightedLemma Lemma, SenseInfo Adjective, SenseInfo Determiner) LittleWithDeterminer()
    {
        // Two open-class adjective senses that score 3 and 0, and the determiner column that is never a candidate
        var senses = Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf("little", pos)).ToArray();
        var determiner = Assert.Single(senses, s => s.PartOfSpeech == PartOfSpeech.Determiner);
        var adjectives = senses.Where(s => s.PartOfSpeech == PartOfSpeech.Adjective).Take(2).ToArray();
        var lemma = new WeightedLemma("little", [adjectives[0].SenseId, adjectives[1].SenseId, determiner.SenseId], new LemmaWeights([3f, 0f, 2f], [0f, 0f, 0f], 1, 1f));
        return (lemma, adjectives[0], determiner);
    }

    /// <summary>
    ///     A row whose closed-class slot wins the closed-class split of the composite model (Basic says the determiner)
    ///     is left out: its answer is at the part-of-speech level at run time, so no threshold applies to it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_ClosedClassSlotWins_RowIsLeftOut()
    {
        // Arrange: the encoder puts all its mass on the adjective slot; Basic says determiner 0.9
        var (lemma, adjective, _) = LittleWithDeterminer();

        // Act
        var (_, report) = RealCalibrator.Apply(Data(lemma), [SenseFixtures.Row(adjective, 1)], [[1f]], new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1), [BasicLittle(0.9)]);

        // Assert
        Assert.Equal(0, report.Rows);
        Assert.Equal(1, report.ClosedWins);
    }

    /// <summary>
    ///     A row whose open-class slot wins the split is a sense answer: it is observed, and a row whose true sense is the
    ///     closed-class sense is wrong. Without the result of Basic the row is kept in the same way.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_OpenClassSlotWins_RowIsObserved_ClosedTrueSenseIsWrong()
    {
        // Arrange: Basic says determiner 0.1, so the adjective slot wins
        var (lemma, adjective, determiner) = LittleWithDeterminer();
        var options = new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1);

        // Act
        var (_, right) = RealCalibrator.Apply(Data(lemma), [SenseFixtures.Row(adjective, 1)], [[1f]], options, [BasicLittle(0.1)]);
        var (_, closedTruth) = RealCalibrator.Apply(Data(lemma), [SenseFixtures.Row(determiner, 2)], [[1f]], options, [BasicLittle(0.1)]);
        var (_, withoutBasic) = RealCalibrator.Apply(Data(lemma), [SenseFixtures.Row(determiner, 3)], [[1f]], options);

        // Assert
        Assert.Equal(1, right.Rows);
        Assert.Equal(1, right.CorrectBefore);
        Assert.Equal(0, right.ClosedWins);
        Assert.Equal(1, closedTruth.Rows);
        Assert.Equal(1, closedTruth.AnswersBefore);
        Assert.Equal(0, closedTruth.CorrectBefore);
        Assert.Equal(1, closedTruth.ClosedGoldRows);
        Assert.Equal(1, withoutBasic.Rows);
        Assert.Equal(0, withoutBasic.CorrectBefore);
        Assert.Equal(0, withoutBasic.ClosedWins);
    }

    /// <summary>
    ///     The Basic results must be one per row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_BasicResultsOfAnotherLength_Throws()
    {
        // Arrange
        var (lemma, adjective, _) = LittleWithDeterminer();

        // Act and Assert
        Assert.Throws<ArgumentException>(() => RealCalibrator.Apply(Data(lemma), [SenseFixtures.Row(adjective, 1)], [[1f]], new CalibrationOptions(), []));
    }

    /// <summary>
    ///     A row is scored with the plan that the classifier has for the form of its target word: a form of several lemmas
    ///     without a chooser is answered from the lexicon, so the encoder does not run and the row is not an observation;
    ///     with a chooser for the form the encoder runs and the row is observed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_FormOfSeveralLemmas_IsObservedOnlyWithAChooser()
    {
        // Arrange: "left" is a form of the lemmas left and leave; the lemma leave has two verb senses (columns 0 and 1)
        var leaves = SenseFixtures.Senses("leave", PartOfSpeech.Verb, 2);
        Assert.True(WordLexicon.Lookup("left").Select(d => d.Lemma).Distinct().Count() > 1, "left must be a form of several lemmas");
        var ids = leaves.Select(s => s.SenseId).ToArray();
        var weights = new LemmaWeights([1f, -1f], [0f, 0f], 1, 1f);
        var withoutChooser = new WeightedLemma("leave", ids, weights);
        var chooser = new FormChooser("left", ["left", "leave"], new LemmaWeights([-1f, 1f], [0f, 0f], 1, 1f));
        var withChooser = new WeightedLemma("leave", ids, weights, [chooser]);
        var row = new CorpusRow("leave", ids[0], PartOfSpeech.Verb, "They {left} early.", "a.tsv", ["They", "left", "early", "."], 1);
        var options = new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1);

        // Act
        var (_, none) = RealCalibrator.Apply(Data(withoutChooser), [row], [[1f]], options);
        var (_, chosen) = RealCalibrator.Apply(Data(withChooser), [row], [[1f]], options);

        // Assert
        Assert.Equal(0, none.Rows);
        Assert.Equal(1, chosen.Rows);
    }

    /// <summary>
    ///     A row whose form chooser is not confident, or whose selected analysis is of another lemma, is answered at the
    ///     part-of-speech level (or by the threshold of the other lemma) at run time, so no threshold of the lemma decides
    ///     it and it is not an observation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_ChooserNotConfidentOrOtherLemmaSelected_RowIsLeftOut()
    {
        // Arrange: the chooser of "left" has the labels left and leave (threshold 1); the scores are -x and +x
        var leaves = SenseFixtures.Senses("leave", PartOfSpeech.Verb, 2);
        var ids = leaves.Select(s => s.SenseId).ToArray();
        var chooser = new FormChooser("left", ["left", "leave"], new LemmaWeights([-1f, 1f], [0f, 0f], 1, 1f));
        var data = Data(new WeightedLemma("leave", ids, new LemmaWeights([1f, -1f], [0f, 0f], 1, 1f), [chooser]));
        var row = new CorpusRow("leave", ids[0], PartOfSpeech.Verb, "They {left} early.", "a.tsv", ["They", "left", "early", "."], 1);
        var options = new CalibrationOptions(MinOwnRows: 1000, MinPooledAnswered: 1);

        // Act: a margin of 0.4 is below the threshold, a margin of 2 for "left" selects the other lemma, a margin of 2 for "leave" decides
        var (_, unsure) = RealCalibrator.Apply(data, [row], [[0.2f]], options);
        var (_, elsewhere) = RealCalibrator.Apply(data, [row], [[-1f]], options);
        var (_, decided) = RealCalibrator.Apply(data, [row], [[1f]], options);

        // Assert
        Assert.Equal(0, unsure.Rows);
        Assert.Equal(1, unsure.ChooserUnsure);
        Assert.Equal(0, elsewhere.Rows);
        Assert.Equal(1, elsewhere.SelectedElsewhere);
        Assert.Equal(1, decided.Rows);
        Assert.Equal(0, decided.ChooserUnsure + decided.SelectedElsewhere + decided.NotScored + decided.OutsidePartOfSpeech);
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

    /// <summary>
    ///     A bulk of answers with a steady error rate of 5 percent never reaches the target 0.97 on a large share of the
    ///     answers: the shift is then an order statistic of the distances, and six more confident errors at the top of
    ///     the distance range do not move it. Without the share, the first crossing sits in the far tail and
    ///     depends on those few rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_ConfidentErrorsInTheTail_DoNotMoveTheShift()
    {
        // Arrange: 400 answers at the distances 0.0 to 39.9; every 20th is wrong, and the perturbed copy has six more
        // wrong answers at the very top
        var answers = Enumerable.Range(0, 400).Select(i => (i / 10.0, i % 20 != 0)).ToList();
        var perturbed = answers.Select((a, i) => (a.Item1, a.Item2 && i < 394)).ToList();

        // Act
        var steady = RealCalibrator.ChooseShift(answers, 0.97, 30, 0.3);
        var moved = RealCalibrator.ChooseShift(perturbed, 0.97, 30, 0.3);
        var legacy = RealCalibrator.ChooseShift(answers, 0.97, 30);

        // Assert: the share of 30 percent keeps the 120 most confident answers (shift 28.0) in both cases, and says that the
        // target is not reached; the legacy rule "reaches" it on 39 answers in the far tail
        Assert.Equal(28.0, steady.Shift, 6);
        Assert.False(steady.Reached);
        Assert.Equal(steady, moved);
        Assert.Equal(36.1, legacy.Shift, 6);
        Assert.True(legacy.Reached);
    }

    /// <summary>
    ///     When the target cannot be reached the result is defined: the largest shift that keeps the minimum share of
    ///     the answers, reported as not reached; with fewer answers than the minimum the shift is zero.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_TargetNotReachable_KeepsTheMinimumShareAndReportsIt()
    {
        // Arrange: 200 answers at the distances 0.0 to 19.9, 80 percent right everywhere
        var answers = Enumerable.Range(0, 200).Select(i => (i / 10.0, i % 5 != 0)).ToList();

        // Act
        var unreachable = RealCalibrator.ChooseShift(answers, 0.97, 10, 0.25);
        var tooFew = RealCalibrator.ChooseShift(answers.Take(9).ToList(), 0.97, 10, 0.25);

        // Assert: a quarter of 200 answers is 50, the 50 largest distances start at 15.0
        Assert.Equal(15.0, unreachable.Shift, 6);
        Assert.False(unreachable.Reached);
        Assert.Equal((0.0, false), tooFew);
    }

    /// <summary>
    ///     When the precision of the answers clearly rises above the target, the lowest shift that reaches it is chosen
    ///     (the candidate before it fails) and the answers it keeps do reach the target.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_TargetClearlyReachable_ReachesItOnEnoughAnswers()
    {
        // Arrange: the answers below the distance 5.0 are half wrong (the even ones), those from 5.0 are all right
        var answers = Enumerable.Range(0, 200).Select(i => (i / 10.0, i >= 50 || i % 2 == 1)).ToList();
        double Precision(double from) => answers.Where(a => a.Item1 >= from).Count(a => a.Item2) / (double)answers.Count(a => a.Item1 >= from);

        // Act
        var (shift, reached) = RealCalibrator.ChooseShift(answers, 0.97, 10, 0.25);

        // Assert: 4.1 is the lowest qualifying shift (the tail from 4.0 has five wrong answers of 160, 96.9%), and the
        // tail from 4.1 keeps 159 answers at 97.5%
        Assert.True(reached);
        Assert.Equal(4.1, shift, 6);
        Assert.True(Precision(4.0) < 0.97);
        Assert.True(Precision(shift) >= 0.97);
        Assert.True(answers.Count(a => a.Item1 >= shift) >= 50);
    }

    /// <summary>
    ///     With fewer than 100 pooled answers the minimum count of 30 decides, not the share: 30 percent of 60 answers
    ///     is only 18.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_FewPooledAnswers_TheMinimumCountDominatesTheShare()
    {
        // Arrange: 60 answers at the distances 0.0 to 5.9, 80 percent right everywhere
        var answers = Enumerable.Range(0, 60).Select(i => (i / 10.0, i % 5 != 0)).ToList();

        // Act
        var floor = RealCalibrator.ChooseShift(answers, 0.97, 30, 0.3);
        var share = RealCalibrator.ChooseShift(answers, 0.97, 10, 0.3);

        // Assert: the 30 most confident answers start at 3.0; with a floor of 10 the share keeps 18, from 4.2
        Assert.Equal(3.0, floor.Shift, 6);
        Assert.False(floor.Reached);
        Assert.Equal(4.2, share.Shift, 6);
        Assert.False(share.Reached);
    }

    /// <summary>
    ///     Fewer than 30 usable answers (a distance that is negative or not finite is no answer) give shift 0 and "target not
    ///     reached"; with 30 usable answers the shift is chosen.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_FewerThanThirtyUsableAnswers_GiveShiftZeroNotReached()
    {
        // Arrange: 29 usable answers, all right, and five that are no answers
        var usable = Enumerable.Range(0, 29).Select(i => (1.0 + i, true)).ToList();
        List<(double, bool)> unusable = [(-0.5, true), (double.NaN, true), (double.PositiveInfinity, true), (-3.0, false), (double.NegativeInfinity, true)];

        // Act
        var few = RealCalibrator.ChooseShift([.. usable, .. unusable], 0.97, 30, 0.3);
        var enough = RealCalibrator.ChooseShift([.. usable, (30.0, true), .. unusable], 0.97, 30, 0.3);

        // Assert
        Assert.Equal((0.0, false), few);
        Assert.Equal((0.0, true), enough);
    }

    /// <summary>
    ///     Answers with the same distance are never split: a shift keeps all of them or none, so the answers kept can
    ///     exceed the minimum when the boundary falls inside a tie.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_ChooseShift_TiesAtTheBoundary_AreKeptTogether()
    {
        // Arrange: ten distances (0 to 9) with five answers each, one of the five wrong
        var answers = Enumerable.Range(0, 50).Select(i => (i / 5 * 1.0, i % 5 != 0)).ToList();

        // Act
        var exact = RealCalibrator.ChooseShift(answers, 0.97, 5, 0.30);
        var inside = RealCalibrator.ChooseShift(answers, 0.97, 5, 0.32);

        // Assert: 15 answers are the distances 7 to 9; 16 answers would split the tie at 6, so all 20 from 6 are kept
        Assert.Equal((7.0, false), exact);
        Assert.Equal((6.0, false), inside);
    }

    /// <summary>
    ///     The defaults: the calibration share is 0.3 and the minimum count 30; the target of a weights run is 0.97, two
    ///     points above the 95 percent of the cross-validation (and of the test text).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Defaults_ShareThirtyPercentTargetNinetySevenPercent()
    {
        // Arrange / Act
        var options = new CalibrationOptions();
        var settings = new TrainSettings(null);
        var crossValidation = new TrainingOptions();

        // Assert
        Assert.Equal(0.3, options.MinPooledShare);
        Assert.Equal(30, options.MinPooledAnswered);
        Assert.Equal(0.97, RealCalibrator.DefaultLemmaTarget);
        Assert.Equal(RealCalibrator.DefaultLemmaTarget, settings.CalibrationTarget);
        Assert.Equal(0.95, crossValidation.TargetPrecision);
    }

    /// <summary>
    ///     With a target that the calibration rows cannot reach, <c>Apply</c> reports it and keeps the minimum share of
    ///     the pooled answers.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_TargetNotReachable_ReportsItAndKeepsTheMinimumShare()
    {
        // Arrange: two lemmas with 25 rows each (no threshold of their own), 80 percent right at every margin; the
        // distances of the 50 rows are 0.0, 0.2, ... 9.8
        var (w, r) = (Walk, Jog);
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 25; i++)
        {
            rows.Add(Observation(w[0], w[1], 1f + (0.4f * i), i % 5 != 0, i));
            rows.Add(Observation(r[0], r[1], 1.2f + (0.4f * i), i % 5 != 0, 100 + i));
        }

        var data = Data(Lemma(w[0], w[1], 1f), Lemma(r[0], r[1], 1f));

        // Act
        var (_, report) = Run(data, rows, new CalibrationOptions(TargetPrecision: 0.97, MinOwnRows: 1000, MinPooledAnswered: 5, MinPooledShare: 0.5));

        // Assert: half of the 50 answers are kept, from the distance 5.0, and the line of the run says so for all rows
        Assert.False(report.FineReached);
        Assert.Equal(5.0, report.FineShift, 3);
        Assert.Equal(25, report.AnswersAfter);
        Assert.Contains("fine pooled shift 5.000 (target not reached)", report.Describe(), StringComparison.Ordinal);
        Assert.Contains("(all calibration rows)", report.Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The group thresholds follow the same rule as the fine ones: with more than 100 group answers whose precision
    ///     stays at 90 percent, the share of 30 percent decides, the target is not reached, and the report line says so.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_GroupAnswersAboveOneHundred_UseTheShareAndReportTargetNotReached()
    {
        // Arrange: the fine level is never answered, so every row is a group answer; the group margin of the feature x is
        // tanh(x), the first threshold is 0.2, every 10th of the 120 rows is wrong, and the distances rise with the row
        var (a1, _, b, _) = SenseFixtures.Grouped("test");
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 120; i++)
        {
            rows.Add((SenseFixtures.Row(i % 10 == 0 ? b : a1, i), [0.21f + (0.0075f * i)]));
        }

        var data = Data(Lemma(a1, b, LemmaWeights.Never, 0.2f));

        // Act
        var (calibrated, report) = Run(data, rows, new CalibrationOptions(TargetPrecision: 0.97));

        // Assert: 30 percent of 120 answers is 36, so the shift is the distance of the 85th answer (tanh(0.84) - 0.2)
        Assert.Equal(120, report.GroupAnswers);
        Assert.True(report.GroupVerified);
        Assert.False(report.GroupReached);
        Assert.Equal(Math.Tanh(0.84) - 0.2, report.GroupShift, 3);
        Assert.True(calibrated.Lemmas[0].Weights.GroupThreshold > 0.6f);
        Assert.Contains("(verified, target not reached on 120 answers)", report.Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group answer whose distance is infinite (the lemma has no other group, so the group margin is infinite) is
    ///     no usable answer: it does not count towards the verification of the group thresholds, which stay as they
    ///     were, and the report counts none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealCalibrator_Apply_InfiniteGroupDistances_AreNotUsableAndDoNotVerify()
    {
        // Arrange: the two senses of the lemma are in one group, so the group margin of every row is infinite; the fine
        // level is never answered, so all 40 rows are group answers
        var (a1, a2, _, _) = SenseFixtures.Grouped("test");
        var rows = new List<(CorpusRow Row, float[] Feature)>();
        for (var i = 0; i < 40; i++)
        {
            rows.Add((SenseFixtures.Row(a1, i), [0.5f + (0.01f * i)]));
        }

        var data = Data(Lemma(a1, a2, LemmaWeights.Never, 0.2f));

        // Act
        var (calibrated, report) = Run(data, rows, new CalibrationOptions(TargetPrecision: 0.97));

        // Assert: no usable group answer, so the group threshold is kept and reported as unverified
        Assert.Equal(0, report.GroupAnswers);
        Assert.False(report.GroupVerified);
        Assert.Equal(0.2f, calibrated.Lemmas[0].Weights.GroupThreshold);
        Assert.Contains("unverified, kept on 0 answers", report.Describe(), StringComparison.Ordinal);
    }
}
