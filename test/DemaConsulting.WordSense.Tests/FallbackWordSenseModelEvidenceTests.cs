using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the composition of <see cref="FallbackWordSenseModel"/> with a primary model that provides
///     part-of-speech evidence (the marker interface): the slot probabilities of a word of several parts of speech are
///     reweighed against the fallback model at every level of the answer.
/// </summary>
public class FallbackWordSenseModelEvidenceTests
{
    private static readonly WordDetails UseNoun = new("use", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails UseInf = new("use", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails UsePres = new("use", PartOfSpeech.Verb, "Tense=Pres+VerbForm=Fin");
    private static readonly WordDetails WillAux = new("will", PartOfSpeech.Auxiliary, "VerbForm=Fin");
    private static readonly WordDetails WillNoun = new("will", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails FoundFind = new("find", PartOfSpeech.Verb, "Tense=Past");
    private static readonly WordDetails FoundFound = new("found", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly ImmutableArray<SenseCandidate> NounSenses = [new("use#utility", 0.6), new("use#habit", 0.4)];
    private static readonly ImmutableArray<SenseCandidate> VerbSenses = [new("use#employ", 0.9), new("use#consume", 0.1)];

    private static ClassifyResult Primary(double noun, double inf, double pres, SenseLevel level = SenseLevel.PartOfSpeech, bool scored = false)
    {
        var analyses = ImmutableArray.Create(
            new AnalysisCandidate(UseNoun, noun, scored ? NounSenses : []),
            new AnalysisCandidate(UseInf, inf, scored ? VerbSenses : []),
            new AnalysisCandidate(UsePres, pres, scored ? VerbSenses : []));
        return level switch
        {
            SenseLevel.Sense => new ClassifyResult("use", true, analyses, UseNoun, level, noun * 0.6, "use#g", "use#utility"),
            _ => new ClassifyResult("use", true, analyses, UseNoun, level, noun, null, null),
        };
    }

    private static ClassifyResult Basic(double inf, double pres, double noun) =>
        new("use", true, [new(UseInf, inf, VerbSenses, true), new(UsePres, pres, VerbSenses, true), new(UseNoun, noun, NounSenses, true)], UseInf, SenseLevel.PartOfSpeech, inf + pres, null, null);

    private static async Task<ClassifyResult> RunAsync(ClassifyResult primary, ClassifyResult fallback, double weight, bool evidence = true)
    {
        using var model = new FallbackWordSenseModel(new EvidenceModel(evidence, primary), new EvidenceModel(false, fallback), weight);
        return (await model.ClassifyAsync([primary.Word], TestContext.Current.CancellationToken))[0];
    }

    private static double Slot(ClassifyResult result, PartOfSpeech part) =>
        result.Analyses.Where(a => a.Details.PartOfSpeech == part).Sum(a => a.Probability);

    private static double[] Fuse(double[] primary, double[] fallback, double weight)
    {
        var scores = primary.Select((p, k) => Math.Pow(Math.Max(p, 1e-6), weight) * Math.Pow(Math.Max(fallback[k], 1e-6), 1 - weight)).ToArray();
        return [.. scores.Select(s => s / scores.Sum())];
    }

    /// <summary>
    ///     A primary result without scored senses gets the slot probabilities of the weighted combination, keeps the
    ///     senses ranked by usage and the variants of the fallback result, and answers at the part-of-speech level on the
    ///     best variant of the most probable slot.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceUnscoredPrimary_ReweighsTheFallbackResult()
    {
        // Arrange: the head says noun 0.55, verb 0.45; Basic says verb 0.95 (infinitive 0.6), noun 0.05
        var primary = Primary(0.55, 0.225, 0.225);
        var fallback = Basic(0.6, 0.35, 0.05);

        // Act
        var result = await RunAsync(primary, fallback, 0.8);

        // Assert
        var fused = Fuse([0.55, 0.45], [0.05, 0.95], 0.8);
        Assert.Equal(fused[1], Slot(result, PartOfSpeech.Verb), 9);
        Assert.Equal(fused[0], Slot(result, PartOfSpeech.Noun), 9);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
        Assert.Equal(PartOfSpeech.Verb, result.Details!.PartOfSpeech);
        Assert.Equal(UseInf, result.Details);
        Assert.Equal(UseInf, result.Analyses[0].Details);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(fused[1], result.Confidence, 9);
        Assert.All(result.Analyses, a => Assert.True(a.SensesRankedByUsage));
        Assert.Equal(0.6 / 0.95, result.Analyses[0].Probability / fused[1], 9);
    }

    /// <summary>
    ///     A fallback result at the sense level keeps the level when its slot stays the most probable one and the fused
    ///     confidence reaches the single-sense target; below the target the same analysis is answered at the
    ///     part-of-speech level.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceFallbackSenseLevel_KeepsOrStepsDownByTheFusedConfidence()
    {
        // Arrange: Basic names the one sense of the verb slot at the sense level
        ClassifyResult Single(double verb) => new("use", true, [new(UseInf, verb, [new("use#employ", 1.0)], true), new(UseNoun, 1 - verb, [new("use#utility", 1.0)], true)], UseInf, SenseLevel.Sense, verb, "use#g", "use#employ");

        // Act
        var sure = await RunAsync(Primary(0.01, 0.495, 0.495), Single(0.999), 0.8);
        var unsure = await RunAsync(Primary(0.2, 0.4, 0.4), Single(0.9), 0.8);

        // Assert: the first stays at the sense level, the second is below 0.97
        Assert.Equal(SenseLevel.Sense, sure.Level);
        Assert.Equal("use#employ", sure.SenseId);
        Assert.True(sure.Confidence >= 0.97);
        Assert.Equal(SenseLevel.PartOfSpeech, unsure.Level);
        Assert.Null(unsure.SenseId);
        Assert.Null(unsure.GroupId);
        Assert.Equal(PartOfSpeech.Verb, unsure.Details!.PartOfSpeech);
        Assert.Equal(Slot(unsure, PartOfSpeech.Verb), unsure.Confidence, 9);
    }

    /// <summary>
    ///     A scored primary result at the sense level keeps its level, group and sense when its slot stays the most
    ///     probable one; the confidence is the new probability of the slot times the probability of the sense within it.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceScoredPrimary_TopSlotUnchanged_KeepsTheSense()
    {
        // Arrange: the encoder answers the noun sense with the head's noun 0.8; Basic leans to the noun too
        var primary = Primary(0.8, 0.1, 0.1, SenseLevel.Sense, scored: true);
        var fallback = Basic(0.2, 0.1, 0.7);

        // Act
        var result = await RunAsync(primary, fallback, 0.8);

        // Assert
        var fused = Fuse([0.8, 0.2], [0.7, 0.3], 0.8);
        Assert.Equal(SenseLevel.Sense, result.Level);
        Assert.Equal("use#utility", result.SenseId);
        Assert.Equal("use#g", result.GroupId);
        Assert.Equal(fused[0] * 0.6, result.Confidence, 9);
        Assert.Equal(fused[0], Slot(result, PartOfSpeech.Noun), 9);
        Assert.Equal(UseNoun, result.Details);
        Assert.Equal(NounSenses, result.Analyses[0].Senses);
        Assert.All(result.Analyses, a => Assert.False(a.SensesRankedByUsage));
    }

    /// <summary>
    ///     When the fusion makes another slot the most probable one, the answer steps down to the part-of-speech level on
    ///     the best analysis of that slot, whatever the level of the base result, and keeps the senses of the analyses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceScoredPrimary_TopSlotChanges_StepsDownToThePartOfSpeech()
    {
        // Arrange: the encoder answers the noun sense at 0.55; Basic says verb with 0.95
        var primary = Primary(0.55, 0.225, 0.225, SenseLevel.Sense, scored: true);
        var fallback = Basic(0.6, 0.35, 0.05);

        // Act
        var result = await RunAsync(primary, fallback, 0.4);

        // Assert
        var fused = Fuse([0.55, 0.45], [0.05, 0.95], 0.4);
        Assert.True(fused[1] > fused[0]);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.SenseId);
        Assert.Null(result.GroupId);
        Assert.Equal(PartOfSpeech.Verb, result.Details!.PartOfSpeech);
        Assert.Equal(result.Analyses[0].Details, result.Details);
        Assert.Equal(fused[1], result.Confidence, 9);
        Assert.Equal(VerbSenses, result.Analyses[0].Senses);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
    }

    /// <summary>
    ///     The weight 1 takes the primary evidence alone and the weight 0 the fallback alone, for a result of either kind.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceWeight_OneTakesThePrimaryZeroTheFallback()
    {
        // Arrange
        var primary = Primary(0.7, 0.15, 0.15);
        var fallback = Basic(0.6, 0.35, 0.05);

        // Act
        var head = await RunAsync(primary, fallback, 1.0);
        var basic = await RunAsync(primary, fallback, 0.0);

        // Assert
        Assert.Equal(0.7, Slot(head, PartOfSpeech.Noun), 9);
        Assert.Equal(PartOfSpeech.Noun, head.Details!.PartOfSpeech);
        Assert.Equal(0.95, Slot(basic, PartOfSpeech.Verb), 9);
        Assert.Equal(PartOfSpeech.Verb, basic.Details!.PartOfSpeech);
    }

    /// <summary>
    ///     A primary model that provides the evidence has its closed-class slots weighed like every other slot: the split
    ///     of the closed-class slots from the fallback result is not applied.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidencePrimary_DoesNotSplitTheClosedClass()
    {
        // Arrange: the primary gives the auxiliary a probability of its own, Basic nearly none
        var primary = new ClassifyResult("will", true, [new(WillAux, 0.6, [], false), new(WillNoun, 0.4, [new("will#testament", 1.0)], false)], WillAux, SenseLevel.PartOfSpeech, 0.6, null, null);
        var fallback = new ClassifyResult("will", true, [new(WillNoun, 0.7, [new("will#testament", 1.0)], true), new(WillAux, 0.3, [], false)], WillNoun, SenseLevel.PartOfSpeech, 0.7, null, null);

        // Act
        var result = await RunAsync(primary, fallback, 0.8);

        // Assert
        var fused = Fuse([0.6, 0.4], [0.3, 0.7], 0.8);
        Assert.Equal(fused[0], Slot(result, PartOfSpeech.Auxiliary), 9);
        Assert.Equal(PartOfSpeech.Auxiliary, result.Details!.PartOfSpeech);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
    }

    /// <summary>
    ///     A primary model that is marked but does not provide the evidence is composed like an unmarked model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_MarkerWithoutEvidence_IsComposedLikeAPlainModel()
    {
        // Arrange
        var primary = Primary(0.7, 0.15, 0.15, SenseLevel.Sense, scored: true);
        var fallback = Basic(0.6, 0.35, 0.05);
        using var plain = new FallbackWordSenseModel(new PlainModel(primary), new PlainModel(fallback), 0.8);

        // Act
        var marked = await RunAsync(primary, fallback, 0.8, evidence: false);
        var expected = (await plain.ClassifyAsync(["use"], TestContext.Current.CancellationToken))[0];

        // Assert
        Assert.Equal(expected, marked);
    }

    /// <summary>
    ///     A word whose slots share one part of speech has no part-of-speech evidence: a scored primary result is only
    ///     refined, never reweighed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidencePrimary_OnePartOfSpeech_KeepsThePrimarySlots()
    {
        // Arrange: two verb lemmas for "found"; the encoder (a chooser) prefers one, Basic the other
        var primary = new ClassifyResult("found", true, [new(FoundFind, 0.8, [new("find#locate", 1.0)]), new(FoundFound, 0.2, [new("found#set-up", 1.0)])], FoundFind, SenseLevel.Sense, 0.8, "find#g", "find#locate");
        var fallback = new ClassifyResult("found", true, [new(FoundFound, 0.9, [], false), new(FoundFind, 0.1, [], false)], FoundFound, SenseLevel.PartOfSpeech, 0.9, null, null);

        // Act
        var result = await RunAsync(primary, fallback, 0.4);

        // Assert
        Assert.Equal(primary, result);
    }

    /// <summary>
    ///     The tied variants of a slot of a scored primary result are re-split by the fallback result, as for every
    ///     primary model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidenceScoredPrimary_RefinesTiedVariants()
    {
        // Arrange: the verb slot of the encoder has two tied variants; Basic separates them 3 to 1
        var primary = Primary(0.1, 0.45, 0.45, SenseLevel.PartOfSpeech, scored: true);
        var fallback = Basic(0.675, 0.225, 0.1);

        // Act
        var result = await RunAsync(primary, fallback, 1.0);

        // Assert: the slot probabilities are the primary's, the variants are re-split by the fallback
        Assert.Equal(0.9, Slot(result, PartOfSpeech.Verb), 9);
        Assert.Equal(UseInf, result.Analyses[0].Details);
        Assert.Equal(0.675, result.Analyses[0].Probability, 9);
        Assert.Equal(0.225, result.Analyses[1].Probability, 9);
    }

    /// <summary>
    ///     A word that the fallback model does not list is answered as without evidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EvidencePrimary_FallbackWithoutAnalyses_UsesTheNormalRules()
    {
        // Arrange
        var primary = Primary(0.7, 0.15, 0.15);
        var fallback = ClassifyResult.Unknown("use");

        // Act
        var result = await RunAsync(primary, fallback, 0.8);

        // Assert: the primary did not score senses, so the fallback result is the answer
        Assert.Equal(fallback, result);
    }

    private sealed class EvidenceModel(bool evidence, params ClassifyResult[] results) : IWordSenseModel, IPartOfSpeechEvidenceModel
    {
        public bool ProvidesPartOfSpeechEvidence => evidence;

        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public void Dispose()
        {
        }
    }

    private sealed class PlainModel(params ClassifyResult[] results) : IWordSenseModel
    {
        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public void Dispose()
        {
        }
    }
}
