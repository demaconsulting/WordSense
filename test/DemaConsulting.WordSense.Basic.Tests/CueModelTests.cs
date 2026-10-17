using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="CueModel"/>.
/// </summary>
public class CueModelTests
{
    private static readonly WordDetails Fish = new("bass", PartOfSpeech.Noun, "fish", null);
    private static readonly WordDetails Music = new("bass", PartOfSpeech.Noun, "music", null);
    private static readonly WordDetails Verb = new("bass", PartOfSpeech.Verb, null, null);

    /// <summary>
    ///     The features are the distinct letter-bearing window tokens plus the two immediate neighbors.
    /// </summary>
    [Fact]
    public void CueModel_Extract_WindowAndNeighbors()
    {
        string[] tokens = ["the", "river", "bass", ".", "the"];
        var features = new FeatureRef[CueModel.MaxFeatures];

        var count = CueModel.Extract(tokens, 2, features);

        FeatureRef[] expected = [new FeatureRef(CueModel.Window, "the"), new FeatureRef(CueModel.Window, "river"), new FeatureRef(CueModel.Left, "river"), new FeatureRef(CueModel.Right, ".")];
        Assert.Equal(4, count);
        Assert.Equal(expected, features[..count]);
    }

    /// <summary>
    ///     A repeated window word counts once and the window is limited to five words on each side.
    /// </summary>
    [Fact]
    public void CueModel_Extract_DedupesAndLimitsWindow()
    {
        string[] tokens = ["far", "a", "b", "c", "d", "e", "x", "a", "f", "g", "h", "i", "j", "k"];
        var features = new FeatureRef[CueModel.MaxFeatures];

        var count = CueModel.Extract(tokens, 6, features);
        var window = features[..count].Where(f => f.Kind == CueModel.Window).Select(f => f.Token).ToArray();

        string[] expected = ["a", "b", "c", "d", "e", "f", "g", "h", "i"];
        Assert.Equal(expected, window.Order().ToArray());
        Assert.DoesNotContain("far", window);
        Assert.DoesNotContain("j", window);
    }

    /// <summary>
    ///     The first and last word of a segment have no left or right feature.
    /// </summary>
    [Fact]
    public void CueModel_Extract_Edges()
    {
        var features = new FeatureRef[CueModel.MaxFeatures];

        var first = CueModel.Extract(["bass", "x"], 0, features);
        var firstKinds = features[..first].Select(f => f.Kind).ToArray();
        var alone = CueModel.Extract(["bass"], 0, features);
        var last = CueModel.Extract(["x", "bass"], 1, features);
        var lastKinds = features[..last].Select(f => f.Kind).ToArray();

        Assert.DoesNotContain(CueModel.Left, firstKinds);
        Assert.Contains(CueModel.Right, firstKinds);
        Assert.Equal(0, alone);
        Assert.Contains(CueModel.Left, lastKinds);
        Assert.DoesNotContain(CueModel.Right, lastKinds);
    }

    /// <summary>
    ///     The scores follow the quantized weights; a sense the table lacks scores NaN.
    /// </summary>
    [Fact]
    public void CueModel_Score_UsesWeightsAndMarksMissingSenses()
    {
        var tables = BasicTables.Load(TestTables.Default());
        ImmutableArray<WordDetails> senses = [Fish, Music, Verb];
        var scores = new double[3];

        CueModel.Score(tables.Cues["bass"], [new FeatureRef(CueModel.Window, "river")], senses, scores);

        Assert.Equal((-8 - 2) * CueModel.Quantum, scores[0], 9);
        Assert.Equal((-8 - 40) * CueModel.Quantum, scores[1], 9);
        Assert.True(double.IsNaN(scores[2]));
    }

    /// <summary>
    ///     Features without a table entry add nothing: the score is the prior.
    /// </summary>
    [Fact]
    public void CueModel_Score_UnknownFeature_GivesPrior()
    {
        var tables = BasicTables.Load(TestTables.Default());
        var scores = new double[2];

        CueModel.Score(tables.Cues["bass"], [new FeatureRef(CueModel.Window, "zzzz-no-such")], [Fish, Music], scores);

        Assert.Equal(-8 * CueModel.Quantum, scores[0], 9);
        Assert.Equal(-8 * CueModel.Quantum, scores[1], 9);
    }
}
