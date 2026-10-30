using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="TenseFusion"/>: the test for a same-spelling verb, the share of the past analyses, the
///     combination of two probabilities of past and the application of the result to a word.
/// </summary>
public class TenseFusionTests
{
    private const double Tolerance = 1e-9;

    private static readonly WordDetails PutPast = new("put", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");
    private static readonly WordDetails PutInf = new("put", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails PutPres = new("put", PartOfSpeech.Verb, "Tense=Pres+VerbForm=Fin");
    private static readonly WordDetails PutNoun = new("put", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails FoundFind = new("find", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");
    private static readonly WordDetails FoundFound = new("found", PartOfSpeech.Verb, "VerbForm=Inf");

    private static ClassifyResult Put(double past, double inf, double pres, double noun = 0, SenseLevel level = SenseLevel.PartOfSpeech)
    {
        var analyses = ImmutableArray.CreateBuilder<AnalysisCandidate>();
        analyses.Add(new AnalysisCandidate(PutPast, past, []));
        analyses.Add(new AnalysisCandidate(PutInf, inf, []));
        analyses.Add(new AnalysisCandidate(PutPres, pres, []));
        if (noun > 0)
        {
            analyses.Add(new AnalysisCandidate(PutNoun, noun, []));
        }

        var ordered = analyses.OrderByDescending(a => a.Probability).ToImmutableArray();
        var slot = ordered[0].Details;
        return level switch
        {
            SenseLevel.Sense => new ClassifyResult("put", true, ordered, slot, level, ordered[0].Probability * 0.5, "put#g", "put#s"),
            _ => new ClassifyResult("put", true, ordered, slot, SenseLevel.PartOfSpeech, ordered[0].Probability, null, null),
        };
    }

    private static double Of(ClassifyResult result, WordDetails details) =>
        result.Analyses.Where(a => a.Details == details).Sum(a => a.Probability);

    /// <summary>
    ///     A word is a same-spelling verb when it has a past and a non-past verb analysis, whatever the lemma.
    /// </summary>
    [Fact]
    public void TenseFusion_IsSameSpellingVerb_NeedsAPastAndANonPastVerbAnalysis()
    {
        // Arrange / Act / Assert
        Assert.True(TenseFusion.IsSameSpellingVerb([PutPast, PutInf]));
        Assert.True(TenseFusion.IsSameSpellingVerb([FoundFind, FoundFound, PutNoun]));
        Assert.False(TenseFusion.IsSameSpellingVerb([PutInf, PutPres]));
        Assert.False(TenseFusion.IsSameSpellingVerb([PutPast, PutNoun]));
        Assert.False(TenseFusion.IsSameSpellingVerb([]));
        Assert.Throws<ArgumentNullException>(() => TenseFusion.IsSameSpellingVerb(null!));
    }

    /// <summary>
    ///     The past share is the probability of the past verb analyses in that of all verb analyses; other parts of speech do
    ///     not count and a word that is not a same-spelling verb has none.
    /// </summary>
    [Fact]
    public void TenseFusion_PastShare_IsThePastShareOfTheVerbAnalyses()
    {
        // Arrange
        var result = Put(0.1, 0.2, 0.3, 0.4);

        // Act / Assert
        Assert.Equal(0.1 / 0.6, TenseFusion.PastShare(result.Analyses)!.Value, Tolerance);
        Assert.Null(TenseFusion.PastShare([new AnalysisCandidate(PutInf, 0.5, []), new AnalysisCandidate(PutPres, 0.5, [])]));
        Assert.Null(TenseFusion.PastShare([]));
        Assert.Null(TenseFusion.PastShare(default));
        Assert.Null(TenseFusion.PastShare([new AnalysisCandidate(PutPast, 0, []), new AnalysisCandidate(PutInf, 0, [])]));
    }

    /// <summary>
    ///     The combination is a weighted sum of the log odds: the weight 1 takes the head, the weight 0 the other share, and
    ///     no other share counts as even odds.
    /// </summary>
    [Fact]
    public void TenseFusion_Fuse_IsAWeightedSumOfTheLogOdds()
    {
        // Arrange
        static double Odds(double p) => Math.Log(p / (1 - p));
        static double Back(double z) => 1.0 / (1.0 + Math.Exp(-z));

        // Act / Assert
        Assert.Equal(0.8, TenseFusion.Fuse(0.8, 0.3, 1.0), Tolerance);
        Assert.Equal(0.3, TenseFusion.Fuse(0.8, 0.3, 0.0), Tolerance);
        Assert.Equal(Back((0.9 * Odds(0.8)) + (0.1 * Odds(0.3))), TenseFusion.Fuse(0.8, 0.3, 0.9), Tolerance);
        Assert.Equal(Back(0.9 * Odds(0.8)), TenseFusion.Fuse(0.8, null, 0.9), Tolerance);
        Assert.Equal(0.5, TenseFusion.Fuse(0.5, null, 0.9), Tolerance);
        Assert.InRange(TenseFusion.Fuse(1.0, 0.0, 0.5), 0.0, 1.0);
        Assert.True(double.IsFinite(TenseFusion.Fuse(0.0, 1.0, 0.5)));
    }

    /// <summary>
    ///     The share of the past analyses is set to the probability, the probability of the verbs as a whole and of the other
    ///     parts of speech does not change, and the shares inside the past and the other analyses stay.
    /// </summary>
    [Fact]
    public void TenseFusion_Apply_SetsThePastShareOfTheVerb()
    {
        // Arrange: verbs 0.6 (past 0.1, infinitive 0.3, present 0.2), noun 0.4
        var result = Put(0.1, 0.3, 0.2, 0.4);

        // Act
        var applied = TenseFusion.Apply(result, 0.5);

        // Assert: past 0.3, other verbs 0.3 split 3:2
        Assert.Equal(0.3, Of(applied, PutPast), Tolerance);
        Assert.Equal(0.18, Of(applied, PutInf), Tolerance);
        Assert.Equal(0.12, Of(applied, PutPres), Tolerance);
        Assert.Equal(0.4, Of(applied, PutNoun), Tolerance);
        Assert.Equal(1.0, applied.Analyses.Sum(a => a.Probability), Tolerance);
        Assert.Equal(applied.Analyses[0].Details, applied.Details);
        Assert.Equal(PartOfSpeech.Verb, applied.Analyses.Select(a => a.Details.PartOfSpeech).First());
    }

    /// <summary>
    ///     A past probability that moves the best verb analysis to the past one makes that analysis the selected one.
    /// </summary>
    [Fact]
    public void TenseFusion_Apply_PastProbabilityHigh_SelectsThePastAnalysis()
    {
        // Arrange: the infinitive leads
        var result = Put(0.1, 0.5, 0.4);

        // Act
        var applied = TenseFusion.Apply(result, 0.9);

        // Assert
        Assert.Equal(PutPast, applied.Details);
        Assert.Equal(0.9, Of(applied, PutPast), Tolerance);
        Assert.Equal(SenseLevel.PartOfSpeech, applied.Level);
        Assert.Equal(1.0, applied.Confidence, Tolerance);
    }

    /// <summary>
    ///     An answer at the sense level keeps its level, group and sense when its slot stays the most probable one; the
    ///     probability of the sense within the slot is kept.
    /// </summary>
    [Fact]
    public void TenseFusion_Apply_SenseLevelAnswerWithSameSlot_KeepsTheSense()
    {
        // Arrange: a verb of one lemma and a sense answer of the verb slot at confidence 0.5 x 0.6
        var result = Put(0.1, 0.5, 0.4, level: SenseLevel.Sense);

        // Act
        var applied = TenseFusion.Apply(result, 0.7);

        // Assert: the slot of the lemma keeps its probability, so the confidence is the same
        Assert.Equal(SenseLevel.Sense, applied.Level);
        Assert.Equal("put#s", applied.SenseId);
        Assert.Equal("put#g", applied.GroupId);
        Assert.Equal(result.Confidence, applied.Confidence, Tolerance);
    }

    /// <summary>
    ///     When the past and the non-past analyses belong to different lemmas and another slot becomes the most probable one,
    ///     the answer steps down to the part-of-speech level on the best analysis of the new slot.
    /// </summary>
    [Fact]
    public void TenseFusion_Apply_DifferentLemmas_OtherSlotWins_StepsDownToThePartOfSpeech()
    {
        // Arrange: the infinitive of "found" answers at the sense level
        var result = new ClassifyResult("found", true, [new(FoundFound, 0.6, []), new(FoundFind, 0.4, [])], FoundFound, SenseLevel.Sense, 0.6, "found#g", "found#s");

        // Act
        var applied = TenseFusion.Apply(result, 0.9);

        // Assert
        Assert.Equal(FoundFind, applied.Details);
        Assert.Equal(SenseLevel.PartOfSpeech, applied.Level);
        Assert.Null(applied.SenseId);
        Assert.Null(applied.GroupId);
        Assert.Equal(0.9, applied.Confidence, Tolerance);
        Assert.Equal(FoundFind, applied.Analyses[0].Details);
    }

    /// <summary>
    ///     A result that is not a same-spelling verb, or a probability that is not between 0 and 1, leaves the result as it is.
    /// </summary>
    [Fact]
    public void TenseFusion_Apply_NotApplicable_ReturnsTheResult()
    {
        // Arrange
        var noun = new ClassifyResult("put", true, [new(PutNoun, 1.0, [])], PutNoun, SenseLevel.PartOfSpeech, 1.0, null, null);
        var verb = Put(0.1, 0.5, 0.4);

        // Act / Assert
        Assert.Equal(noun, TenseFusion.Apply(noun, 0.5));
        Assert.Equal(verb, TenseFusion.Apply(verb, double.NaN));
        Assert.Equal(verb, TenseFusion.Apply(verb, 1.5));
        Assert.Equal(verb, TenseFusion.Apply(verb, -0.1));
    }
}
