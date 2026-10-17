namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="SenseFusion"/>.
/// </summary>
public class SenseFusionTests
{
    private static readonly WordDetails Fish = new("bass", PartOfSpeech.Noun, "fish", null);
    private static readonly WordDetails Music = new("bass", PartOfSpeech.Noun, "music", null);
    private static readonly WordDetails Verb = new("bass", PartOfSpeech.Verb, null, null);

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
}
