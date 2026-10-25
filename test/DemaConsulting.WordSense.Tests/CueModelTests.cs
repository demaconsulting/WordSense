using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="CueModel"/>.
/// </summary>
public class CueModelTests
{
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
    ///     The scores follow the quantized weights of the column of each analysis, an analysis whose features match the
    ///     empty column scores with it, and an analysis the table has no column for scores NaN.
    /// </summary>
    [Fact]
    public void CueModel_Score_UsesWeightsAndMarksMissingAnalyses()
    {
        // Arrange: the toy table has a past and a present column of the verb "read" and none for the verb "record"
        var tables = TestTables.Load(TestTables.Default());
        ImmutableArray<WordDetails> analyses = [TestTables.ReadPast, TestTables.ReadPres, TestTables.RecordVerb];
        var scores = new double[3];

        // Act
        CueModel.Score(tables.Cues["read"], [new FeatureRef(CueModel.Window, "yesterday")], analyses, scores);

        // Assert
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
        var tables = TestTables.Load(TestTables.Default());
        var scores = new double[2];

        CueModel.Score(tables.Cues["read"], [new FeatureRef(CueModel.Window, "zzzz-no-such")], [TestTables.ReadPast, TestTables.ReadPres], scores);

        Assert.Equal(-8 * CueModel.Quantum, scores[0], 9);
        Assert.Equal(-8 * CueModel.Quantum, scores[1], 9);
    }

    /// <summary>
    ///     A null table and a default analysis array are rejected with an argument-null error.
    /// </summary>
    [Fact]
    public void CueModel_Score_NullTableOrDefaultAnalyses_Throws()
    {
        // Arrange
        var tables = TestTables.Load(TestTables.Default());
        var cue = tables.Cues["read"];
        var scores = new double[2];

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => CueModel.Score(null!, [], [TestTables.ReadPast], scores));
        Assert.Throws<ArgumentNullException>(() => CueModel.Score(cue, [], default, scores));
    }

    /// <summary>
    ///     The tense features include the cue features of the target (its neighbors on each side) and the target itself is
    ///     never a feature, so the model cannot tell one verb from another.
    /// </summary>
    [Fact]
    public void CueModel_ExtractTense_IncludesTheCueFeaturesAndNeverTheTarget()
    {
        // Arrange
        string[] tokens = ["i", "have", "already", "read", "the", "book"];
        var features = new FeatureRef[CueModel.MaxTenseFeatures];

        // Act
        var count = CueModel.ExtractTense(tokens, 3, features);
        var found = features[..count];

        // Assert
        Assert.Contains(new FeatureRef(CueModel.Left, "already"), found);
        Assert.Contains(new FeatureRef(CueModel.Right, "the"), found);
        Assert.Contains(new FeatureRef(CueModel.Window, "have"), found);
        Assert.DoesNotContain(found, f => f.Token == "read");
        Assert.Equal(found.Length, found.Distinct().Count());
    }

    /// <summary>
    ///     The tokens of the window are told apart by the side of the target they stand on: a left-window feature for the
    ///     tokens before it and a right-window feature for the tokens after it, each token once.
    /// </summary>
    [Fact]
    public void CueModel_ExtractTense_WindowTokensAreToldApartBySide()
    {
        // Arrange: "the" stands on both sides of the target and "have" only on the left
        string[] tokens = ["the", "have", "read", "the", "book"];
        var features = new FeatureRef[CueModel.MaxTenseFeatures];

        // Act
        var count = CueModel.ExtractTense(tokens, 2, features);
        var found = features[..count];

        // Assert
        Assert.Contains(new FeatureRef(CueModel.LeftWindow, "have"), found);
        Assert.Contains(new FeatureRef(CueModel.LeftWindow, "the"), found);
        Assert.Contains(new FeatureRef(CueModel.RightWindow, "the"), found);
        Assert.Contains(new FeatureRef(CueModel.RightWindow, "book"), found);
        Assert.DoesNotContain(new FeatureRef(CueModel.RightWindow, "have"), found);
        Assert.Equal(1, found.Count(f => f is { Kind: CueModel.LeftWindow, Token: "the" }));
    }

    /// <summary>
    ///     The governing function word is the nearest one to the left within six tokens, and none is found beyond that
    ///     or when only a verb stands there.
    /// </summary>
    [Fact]
    public void CueModel_ExtractTense_GovernorIsTheNearestFunctionWordWithinTheRadius()
    {
        // Arrange
        string[] near = ["will", "he", "not", "read", "it"];
        string[] far = ["will", "a", "b", "c", "d", "e", "f", "read"];
        var features = new FeatureRef[CueModel.MaxTenseFeatures];

        // Act
        var nearCount = CueModel.ExtractTense(near, 3, features);
        var nearGovernors = features[..nearCount].Where(f => f.Kind == CueModel.Governor).ToArray();
        var farCount = CueModel.ExtractTense(far, 7, features);
        var farGovernors = features[..farCount].Where(f => f.Kind == CueModel.Governor).ToArray();

        // Assert: "not" is nearer than "will"; "will" is seven tokens away
        Assert.Equal([new FeatureRef(CueModel.Governor, "not")], nearGovernors);
        Assert.Empty(farGovernors);
    }

    /// <summary>
    ///     The edge of the segment is a feature: the first word (the first token with a letter) has the edge as its left
    ///     neighbor and the last word as its right one, whatever punctuation stands before or after them, and a word with a
    ///     word on both sides has no edge.
    /// </summary>
    [Fact]
    public void CueModel_ExtractTense_SegmentEdgesAreNeighbors()
    {
        // Arrange
        var features = new FeatureRef[CueModel.MaxTenseFeatures];
        var edgeLeft = new FeatureRef(CueModel.Left, CueModel.SegmentEdge);
        var edgeRight = new FeatureRef(CueModel.Right, CueModel.SegmentEdge);

        // Act
        var first = CueModel.ExtractTense(["run", "away"], 0, features);
        var firstFeatures = features[..first].ToArray();
        var last = CueModel.ExtractTense(["to", "run"], 1, features);
        var lastFeatures = features[..last].ToArray();
        var quoted = CueModel.ExtractTense(["\"", "put", "it", "back", ".", "\""], 1, features);
        var quotedFeatures = features[..quoted].ToArray();
        var closed = CueModel.ExtractTense(["we", "will", "put", ".", "\""], 2, features);
        var closedFeatures = features[..closed].ToArray();
        var inside = CueModel.ExtractTense(["we", "put", "it", "."], 1, features);
        var insideFeatures = features[..inside].ToArray();

        // Assert
        Assert.Contains(edgeLeft, firstFeatures);
        Assert.DoesNotContain(edgeRight, firstFeatures);
        Assert.Contains(edgeRight, lastFeatures);
        Assert.Contains(edgeLeft, quotedFeatures);
        Assert.Contains(new FeatureRef(CueModel.Left, "\""), quotedFeatures);
        Assert.DoesNotContain(edgeRight, quotedFeatures);
        Assert.Contains(edgeRight, closedFeatures);
        Assert.DoesNotContain(edgeLeft, closedFeatures);
        Assert.DoesNotContain(edgeLeft, insideFeatures);
        Assert.DoesNotContain(edgeRight, insideFeatures);
    }

    /// <summary>
    ///     No tense feature list is longer than the buffer size: a target with a full window on both sides fits.
    /// </summary>
    [Fact]
    public void CueModel_ExtractTense_FullWindow_FitsTheBuffer()
    {
        // Arrange: eleven distinct tokens around the target, a function word among them
        string[] tokens = ["a", "b", "c", "d", "to", "run", "e", "f", "g", "h", "i"];
        var features = new FeatureRef[CueModel.MaxTenseFeatures];

        // Act
        var count = CueModel.ExtractTense(tokens, 5, features);

        // Assert
        Assert.InRange(count, 1, CueModel.MaxTenseFeatures);
    }

    /// <summary>
    ///     The tense score is the prior plus the weights of the known features in eighths of a nat, positive for past.
    /// </summary>
    [Fact]
    public void CueModel_ScoreTense_SumsPriorAndKnownWeights()
    {
        // Arrange
        var yesterday = new FeatureRef(CueModel.Window, "yesterday");
        var tomorrow = new FeatureRef(CueModel.Window, "tomorrow");
        ushort[] hashes = [.. new[] { yesterday.Hash, tomorrow.Hash }.Order()];
        sbyte[] weights = hashes[0] == yesterday.Hash ? [16, -24] : [-24, 16];
        var cue = new TenseCue(-2, [.. hashes], [.. weights]);

        // Act
        var past = CueModel.ScoreTense(cue, [yesterday]);
        var future = CueModel.ScoreTense(cue, [tomorrow]);
        var both = CueModel.ScoreTense(cue, [yesterday, tomorrow, new FeatureRef(CueModel.Window, "zzzz-no-such")]);
        var none = CueModel.ScoreTense(cue, []);

        // Assert
        Assert.Equal((-2 + 16) * CueModel.Quantum, past, 9);
        Assert.Equal((-2 - 24) * CueModel.Quantum, future, 9);
        Assert.Equal((-2 + 16 - 24) * CueModel.Quantum, both, 9);
        Assert.Equal(-2 * CueModel.Quantum, none, 9);
    }

    /// <summary>
    ///     The tense score is a whole number of eighths of a nat: the stored weights are the quantized values and one stored
    ///     unit is 1/8 nat.
    /// </summary>
    [Fact]
    public void CueModel_ScoreTense_ResultIsInEighthsOfANat()
    {
        // Arrange
        var feature = new FeatureRef(CueModel.Window, "yesterday");
        var cue = new TenseCue(3, [feature.Hash], [5]);

        // Act
        var score = CueModel.ScoreTense(cue, [feature]);

        // Assert
        Assert.Equal(0.125, CueModel.Quantum);
        Assert.Equal(1.0, score, 12);
        Assert.Equal(score * 8, Math.Round(score * 8), 12);
    }

    /// <summary>
    ///     A null tense table is rejected with an argument-null error.
    /// </summary>
    [Fact]
    public void CueModel_ScoreTense_NullTable_Throws()
    {
        // Arrange
        TenseCue? cue = null;

        // Act
        var error = Record.Exception(() => CueModel.ScoreTense(cue!, []));

        // Assert
        var thrown = Assert.IsType<ArgumentNullException>(error);
        Assert.Equal("cue", thrown.ParamName);
    }

    /// <summary>
    ///     Only the closed class of function words that carry the tense of a verb phrase is a governor.
    /// </summary>
    [Theory]
    [InlineData("have", true)]
    [InlineData("will", true)]
    [InlineData("to", true)]
    [InlineData("didn't", true)]
    [InlineData("read", false)]
    [InlineData("yesterday", false)]
    public void CueModel_IsGovernor_ClosedClassOfFunctionWords(string token, bool expected)
    {
        // Arrange: the token comes from the theory data

        // Act
        var actual = CueModel.IsGovernor(token);

        // Assert
        Assert.Equal(expected, actual);
    }
}
