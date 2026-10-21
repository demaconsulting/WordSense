namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="SenseFusion"/>.
/// </summary>
public class SenseFusionTests
{
    private static readonly WordDetails Noun = TestTables.RecordNoun;
    private static readonly WordDetails Verb = TestTables.RecordVerb;
    private static readonly WordDetails Auxiliary = new("record", PartOfSpeech.Auxiliary, string.Empty);

    /// <summary>
    ///     The auxiliary tag has the numeric value of the auxiliary part of speech, so the mass of the auxiliary state
    ///     reaches the auxiliary analysis and it wins when the tagger is sure of it.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_AuxiliaryPosterior_SelectsAuxiliaryAnalysis()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 0.001;
        posteriors[PosTag.Verb] = 0.001;
        posteriors[PosTag.Auxiliary] = 0.998;
        double[] cues = [double.NaN, double.NaN, double.NaN];
        var probabilities = new double[3];

        SenseFusion.Fuse(posteriors, [Noun, Verb, Auxiliary], cues, 1.5, 0.5, probabilities);

        Assert.Equal(PosTag.Auxiliary, PosTag.Of(PartOfSpeech.Auxiliary));
        Assert.Equal(2, SenseFusion.Winner(probabilities));
        Assert.True(probabilities[2] > 0.9);
    }

    /// <summary>
    ///     Fusion gives probabilities that sum to one and pick the analysis of the likely part of speech.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_PosPosteriorDecides()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 0.9;
        posteriors[PosTag.Verb] = 0.1;
        double[] cues = [0, 0, double.NaN];
        var probabilities = new double[3];

        SenseFusion.Fuse(posteriors, [Noun, TestTables.CatNoun, Verb], cues, 1.5, 0.5, probabilities);

        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.True(probabilities[0] > probabilities[2]);
        Assert.Equal(0, SenseFusion.Winner(probabilities));
    }

    /// <summary>
    ///     The cue scores separate analyses that share a part of speech: they have the same posterior share, so only the
    ///     cues decide between the past and the present of <c>read</c>.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_CuesSeparateSamePartOfSpeechAnalyses()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        double[] cues = [-10, -2];
        var probabilities = new double[2];

        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres], cues, 1.5, 0.5, probabilities);

        Assert.Equal(1, SenseFusion.Winner(probabilities));
        Assert.InRange(probabilities[1], 0.5, 1.0);
    }

    /// <summary>
    ///     When the tagger gives no mass to any candidate, the candidates are equally likely before the cues.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_NoPosMass_IsUniform()
    {
        var probabilities = new double[2];

        SenseFusion.Fuse(new double[PosTag.Count], [TestTables.ReadPast, TestTables.ReadPres], [double.NaN, double.NaN], 1.5, 0.5, probabilities);

        Assert.Equal(0.5, probabilities[0], 9);
        Assert.Equal(0.5, probabilities[1], 9);
        Assert.Equal(0, SenseFusion.Winner(probabilities));
    }

    /// <summary>
    ///     Extreme scores do not produce non-finite probabilities.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_ExtremeScores_StayFinite()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        var probabilities = new double[2];

        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres], [-1e6, 0], 1.5, 1.0, probabilities);

        Assert.All(probabilities, p => Assert.True(double.IsFinite(p)));
        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.Equal(1, SenseFusion.Winner(probabilities));
    }

    /// <summary>
    ///     A candidate whose weight would underflow keeps a probability above zero, and the probabilities sum to one.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_ExtremeScores_StayAboveZeroAndSumToOne()
    {
        // Arrange
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        var probabilities = new double[3];

        // Act
        SenseFusion.Fuse(
            posteriors,
            [TestTables.ReadPast, TestTables.ReadPres, TestTables.ReadPastParticiple],
            [-1e6, 0, -1e9],
            1.5,
            1.0,
            probabilities);

        // Assert
        Assert.All(probabilities, p => Assert.True(p > 0 && double.IsFinite(p)));
        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.Equal(1, SenseFusion.Winner(probabilities));
    }

    /// <summary>
    ///     A default analysis array is rejected with an argument-null error and the cue scores are not modified.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_DefaultAnalyses_Throws()
    {
        // Arrange
        var posteriors = new double[PosTag.Count];
        var cues = new[] { 1.0 };
        var probabilities = new double[1];

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => SenseFusion.Fuse(posteriors, default, cues, 1.5, 0.5, probabilities));
        Assert.Equal(1.0, cues[0]);
    }

    /// <summary>
    ///     Equal probabilities pick the first candidate.
    /// </summary>
    [Fact]
    public void SenseFusion_Winner_Tie_PicksFirst()
    {
        Assert.Equal(0, SenseFusion.Winner([0.4, 0.4, 0.2]));
        Assert.Equal(1, SenseFusion.Winner([0.2, 0.4, 0.4]));
    }

    /// <summary>
    ///     A missing (NaN) cue is filled with the mean of the finite cues, so it neither helps nor hurts the candidate.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_MissingCue_IsFilledWithMeanOfOthers()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        var withNaN = new double[2];
        var withMean = new double[2];

        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres], [-3.0, double.NaN], 1.5, 0.5, withNaN);
        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres], [-3.0, -3.0], 1.5, 0.5, withMean);

        Assert.Equal(withMean[0], withNaN[0], 9);
        Assert.Equal(withMean[1], withNaN[1], 9);
    }

    /// <summary>
    ///     The candidates of one lemma and part of speech that have no cue score weigh as one candidate together, so a
    ///     part of speech is not favored because it has several feature variants: with the tagger and the cues
    ///     indifferent, a noun and a verb with two variants have the probability 1/2 each, not 1/3 and 2/3.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_VariantsWithoutCues_ShareTheWeightOfOneCandidate()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 0.5;
        posteriors[PosTag.Verb] = 0.5;
        var probabilities = new double[3];

        SenseFusion.Fuse(posteriors, [Noun, TestTables.ReadPast, TestTables.ReadPres], [double.NaN, double.NaN, double.NaN], 1.0, 0.5, probabilities);

        Assert.Equal(0.5, probabilities[0], 9);
        Assert.Equal(0.25, probabilities[1], 9);
        Assert.Equal(0.25, probabilities[2], 9);
    }

    /// <summary>
    ///     Variants that have cue scores keep their own weights, so the context separates them and their sum is what
    ///     the cues say about the slot: the same as when each variant is a separate candidate.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_VariantsWithCues_KeepTheirWeights()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        var probabilities = new double[2];

        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres], [-4.0, -2.0], 1.5, 0.5, probabilities);

        var ratio = Math.Exp(0.5 * 2.0);
        Assert.Equal(ratio / (1 + ratio), probabilities[1], 9);
        Assert.Equal(1.0, probabilities.Sum(), 9);
    }

    /// <summary>
    ///     Only the variants without a cue score are merged into one weight: a variant that has a cue keeps its own.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_VariantsOfWhichOneHasNoCue_ShareTheFillWeight()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Verb] = 1.0;
        var probabilities = new double[3];

        // The two variants without a cue take the cue of the first as their fill, and split one weight between them
        SenseFusion.Fuse(posteriors, [TestTables.ReadPast, TestTables.ReadPres, TestTables.ReadPastParticiple], [-2.0, double.NaN, double.NaN], 1.0, 1.0, probabilities);

        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.Equal(0.5, probabilities[0], 9);
        Assert.Equal(0.25, probabilities[1], 9);
        Assert.Equal(0.25, probabilities[2], 9);
    }
}
