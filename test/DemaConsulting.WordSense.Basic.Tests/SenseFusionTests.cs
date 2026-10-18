namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="SenseFusion"/>.
/// </summary>
public class SenseFusionTests
{
    private static readonly WordDetails Fish = new("bass", PartOfSpeech.Noun, "fish", null);
    private static readonly WordDetails Music = new("bass", PartOfSpeech.Noun, "music", null);
    private static readonly WordDetails Verb = new("bass", PartOfSpeech.Verb, null, null);
    private static readonly WordDetails Auxiliary = new("bass", PartOfSpeech.Auxiliary, null, null);

    /// <summary>
    ///     The auxiliary tag has the numeric value of the auxiliary part of speech, so the mass of the auxiliary state
    ///     reaches the auxiliary sense and it wins when the tagger is sure of it.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_AuxiliaryPosterior_SelectsAuxiliarySense()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 0.001;
        posteriors[PosTag.Verb] = 0.001;
        posteriors[PosTag.Auxiliary] = 0.998;
        double[] cues = [double.NaN, double.NaN, double.NaN];
        var probabilities = new double[3];

        SenseFusion.Fuse(posteriors, [Fish, Verb, Auxiliary], cues, 1.5, 0.5, probabilities);

        Assert.Equal(PosTag.Auxiliary, PosTag.Of(PartOfSpeech.Auxiliary));
        Assert.Equal(2, SenseFusion.Winner(probabilities));
        Assert.True(probabilities[2] > 0.9);
    }

    /// <summary>
    ///     Fusion gives probabilities that sum to one and pick the sense of the likely part of speech.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_PosPosteriorDecides()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 0.9;
        posteriors[PosTag.Verb] = 0.1;
        double[] cues = [0, 0, double.NaN];
        var probabilities = new double[3];

        SenseFusion.Fuse(posteriors, [Fish, Music, Verb], cues, 1.5, 0.5, probabilities);

        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.True(probabilities[0] > probabilities[2]);
        Assert.Equal(0, SenseFusion.Winner(probabilities));
    }

    /// <summary>
    ///     The cue scores separate senses that share a part of speech.
    /// </summary>
    [Fact]
    public void SenseFusion_Fuse_CuesSeparateSamePartOfSpeech()
    {
        var posteriors = new double[PosTag.Count];
        posteriors[PosTag.Noun] = 1.0;
        double[] cues = [-10, -2];
        var probabilities = new double[2];

        SenseFusion.Fuse(posteriors, [Fish, Music], cues, 1.5, 0.5, probabilities);

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

        SenseFusion.Fuse(new double[PosTag.Count], [Fish, Music], [double.NaN, double.NaN], 1.5, 0.5, probabilities);

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
        posteriors[PosTag.Noun] = 1.0;
        var probabilities = new double[2];

        SenseFusion.Fuse(posteriors, [Fish, Music], [-1e6, 0], 1.5, 1.0, probabilities);

        Assert.All(probabilities, p => Assert.True(double.IsFinite(p)));
        Assert.Equal(1.0, probabilities.Sum(), 9);
        Assert.Equal(1, SenseFusion.Winner(probabilities));
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
        posteriors[PosTag.Noun] = 1.0;
        var withNaN = new double[2];
        var withMean = new double[2];

        SenseFusion.Fuse(posteriors, [Fish, Music], [-3.0, double.NaN], 1.5, 0.5, withNaN);
        SenseFusion.Fuse(posteriors, [Fish, Music], [-3.0, -3.0], 1.5, 0.5, withMean);

        Assert.Equal(withMean[0], withNaN[0], 9);
        Assert.Equal(withMean[1], withNaN[1], 9);
    }
}
