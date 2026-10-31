using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the tense of same-spelling verbs in <see cref="FallbackWordSenseModel"/>: a primary model that provides
///     tense evidence gives the probability of past, which the composition combines with the past and non-past split of
///     the fallback result.
/// </summary>
public class FallbackWordSenseModelTenseTests
{
    private const double Tolerance = 1e-9;

    private static readonly WordDetails PutPast = new("put", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");
    private static readonly WordDetails PutInf = new("put", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails PutPres = new("put", PartOfSpeech.Verb, "Tense=Pres+VerbForm=Fin");

    private static ClassifyResult Put(double past, double inf, double pres) =>
        new("put", true, [new(PutInf, inf, []), new(PutPres, pres, []), new(PutPast, past, [])], PutInf, SenseLevel.PartOfSpeech, inf + pres, null, null);

    private static double Past(ClassifyResult result) =>
        result.Analyses.Where(a => a.Details == PutPast).Sum(a => a.Probability) / result.Analyses.Sum(a => a.Probability);

    private static async Task<ClassifyResult> RunAsync(double? pastProbability, double? tenseWeight, bool tenseEvidence = true)
    {
        var primary = new TenseModel(tenseEvidence, pastProbability, Put(0.34, 0.33, 0.33));
        var fallback = Put(0.2, 0.5, 0.3);
        using var model = tenseWeight is { } w
            ? new FallbackWordSenseModel(primary, new PlainModel(fallback), 0.8, w)
            : new FallbackWordSenseModel(primary, new PlainModel(fallback), 0.8);
        return (await model.ClassifyAsync(["put"], TestContext.Current.CancellationToken))[0];
    }

    private static double Back(double z) => 1.0 / (1.0 + Math.Exp(-z));

    private static double Odds(double p) => Math.Log(p / (1 - p));

    /// <summary>
    ///     With the tense weight 1 the share of the past analyses is the probability of the head; with another weight it is
    ///     the combination in the odds with the share of the fallback result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task FallbackWordSenseModel_TenseEvidence_SetsThePastShareFromTheHead()
    {
        // Arrange / Act
        var headOnly = await RunAsync(0.9, 1.0);
        var mixed = await RunAsync(0.9, 0.6);
        var fallbackOnly = await RunAsync(0.9, 0.0);

        // Assert: the fallback result has a past share of 0.2
        Assert.Equal(0.9, Past(headOnly), Tolerance);
        Assert.Equal(Back((0.6 * Odds(0.9)) + (0.4 * Odds(0.2))), Past(mixed), Tolerance);
        Assert.Equal(0.2, Past(fallbackOnly), Tolerance);
        Assert.Equal(PutPast, headOnly.Details);
        Assert.Equal(1.0, headOnly.Analyses.Sum(a => a.Probability), Tolerance);
    }

    /// <summary>
    ///     The constructor without a tense weight takes the head alone.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task FallbackWordSenseModel_TenseEvidence_DefaultWeight_TakesTheHeadAlone()
    {
        // Arrange / Act
        var result = await RunAsync(0.7, null);

        // Assert
        Assert.Equal(0.7, Past(result), Tolerance);
    }

    /// <summary>
    ///     A word for which the head gave no probability, and a primary model that provides no tense evidence, leave the
    ///     result as the fallback result gives it.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task FallbackWordSenseModel_TenseEvidence_NoProbabilityOrNoEvidence_KeepsTheFallbackShare()
    {
        // Arrange / Act
        var none = await RunAsync(null, 0.9);
        var notProvided = await RunAsync(0.9, 0.9, tenseEvidence: false);

        // Assert
        Assert.Equal(0.2, Past(none), Tolerance);
        Assert.Equal(0.2, Past(notProvided), Tolerance);
    }

    /// <summary>
    ///     A tense weight outside 0 to 1 is rejected.
    /// </summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void FallbackWordSenseModel_Constructor_TenseWeightOutOfRange_Throws(double weight)
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new FallbackWordSenseModel(new PlainModel(Put(0.2, 0.5, 0.3)), new PlainModel(Put(0.2, 0.5, 0.3)), 0.5, weight));
    }

    private sealed class TenseModel(bool tenseEvidence, double? past, params ClassifyResult[] results) : IWordSenseModel, IPartOfSpeechEvidenceModel
    {
        public bool ProvidesPartOfSpeechEvidence => true;

        public bool ProvidesTenseEvidence => tenseEvidence;

        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public Task<(ClassifyResult[] Results, double?[] PastProbability)> ClassifyWithTenseAsync(IEnumerable<string> words, CancellationToken cancellationToken) =>
            Task.FromResult((results, new double?[] { past }));

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
