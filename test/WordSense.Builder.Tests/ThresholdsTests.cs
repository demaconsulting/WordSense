using System.Diagnostics;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the choice of the fine and group thresholds of a lemma by cross-validation on its anchors.
/// </summary>
public sealed class ThresholdsTests
{
    private static readonly TrainingOptions Options = new(Parallelism: 1);

    /// <summary>
    ///     Builds anchors in six dimensions: senses that share a base direction are confusable, a sense of its own
    ///     direction is separable; the spread is a deterministic hash of the sense and the index.
    /// </summary>
    /// <param name="baseOf">The base direction of each sense.</param>
    /// <param name="perSense">The number of anchors of each sense.</param>
    /// <param name="spread">The size of the spread added to the base.</param>
    /// <returns>The unit-length features and the column of each.</returns>
    private static (float[][] Features, int[] Labels) Anchors(int[] baseOf, int perSense, double spread)
    {
        var features = new List<float[]>();
        var labels = new List<int>();
        for (var sense = 0; sense < baseOf.Length; sense++)
        {
            for (var i = 0; i < perSense; i++)
            {
                var vector = new double[6];
                vector[baseOf[sense]] = 1;
                for (var d = 0; d < vector.Length; d++)
                {
                    vector[d] += spread * Math.Sin((sense * 101.0) + (i * 12.9898) + (d * 78.233));
                }

                var norm = Math.Sqrt(vector.Sum(v => v * v));
                features.Add([.. vector.Select(v => (float)(v / norm))]);
                labels.Add(sense);
            }
        }

        return ([.. features], [.. labels]);
    }

    /// <summary>
    ///     The rule picks the lowest margin whose answers reach the target with enough answers: the handcrafted answers
    ///     give the expected margin, every answer above it reaches the target, the next lower candidate does not, and
    ///     the same margin is found by brute force.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_MeetPrecisionTargetOnOutOfFoldAnswers()
    {
        // Arrange: errors at the low margins, then correct answers; 95 percent needs at most one error in twenty
        var answers = new List<(double Margin, bool Correct)> { (0.1, false), (0.2, false), (0.3, false) };
        answers.AddRange(Enumerable.Range(0, 19).Select(i => (0.4 + (0.05 * i), true)));
        answers.Add((0.35, false));

        // Act
        var threshold = Thresholds.ChooseThreshold(answers, Options);

        // Assert: the 20 answers from 0.35 up contain one error (95.0 percent), 0.3 would add another error
        Assert.Equal(0.35f, threshold);
        var above = answers.Where(a => a.Margin >= threshold).ToList();
        Assert.True(above.Count(a => a.Correct) >= Options.TargetPrecision * above.Count);
        var lower = answers.Where(a => a.Margin >= 0.3).ToList();
        Assert.True(lower.Count(a => a.Correct) < Options.TargetPrecision * lower.Count);
        Assert.Equal(BruteForce(answers, Options), threshold);
    }

    /// <summary>
    ///     Equal margins are taken together, a threshold needs the minimum number of answers, and negative or infinite
    ///     margins can never be thresholds.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_TiesMinimumAnswersAndUnusableMargins()
    {
        // Ties: the error shares its margin with a correct answer, so both are in or out together
        Assert.Equal(0.6f, Thresholds.ChooseThreshold([(0.5, true), (0.5, false), (0.6, true), (0.7, true), (0.8, true)], Options), 5);

        // Minimum number of answers
        Assert.Equal(LemmaWeights.Never, Thresholds.ChooseThreshold([(0.9, true), (0.95, true)], Options));
        Assert.Equal(0.9f, Thresholds.ChooseThreshold([(0.9, true), (0.95, true)], Options with { MinAnswered = 2 }), 5);

        // Negative and infinite margins are not candidates
        Assert.Equal(LemmaWeights.Never, Thresholds.ChooseThreshold(Enumerable.Repeat((-0.5, true), 5).ToList(), Options));
        Assert.Equal(LemmaWeights.Never, Thresholds.ChooseThreshold(Enumerable.Repeat((double.PositiveInfinity, true), 5).ToList(), Options));
        Assert.Equal(0.1f, Thresholds.ChooseThreshold([(-0.5, false), (0.1, true), (0.2, true), (0.3, true)], Options), 5);

        // The stored float never exceeds the margin it was taken from, so the answer that defined it still reaches it
        var rounded = Thresholds.ChooseThreshold([(0.1, true), (0.2, true), (0.3, true)], Options);
        Assert.True(rounded <= 0.1);
        Assert.True(rounded > 0.0999);
    }

    /// <summary>
    ///     The same anchors and lemma always give the same thresholds, on any number of calls and threads, and the
    ///     result is a finite non-negative margin for separable data.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_IsDeterministicForSeed()
    {
        // Arrange
        var (features, labels) = Anchors([0, 1, 2], 10, 0.35);
        int[] groups = [-1, -1, -1];

        // Act
        var first = Thresholds.Choose("walk", features, labels, 3, groups, Options);
        var second = Thresholds.Choose("walk", features, labels, 3, groups, Options);
        var parallel = new LemmaThresholds[4];
        Parallel.For(0, parallel.Length, i => parallel[i] = Thresholds.Choose("walk", features, labels, 3, groups, Options));

        // Assert
        Assert.Equal(first, second);
        Assert.All(parallel, p => Assert.Equal(first, p));
        Assert.True(first.Fine < LemmaWeights.Never);
        Assert.True(first.Fine >= 0);
        Assert.Equal(LemmaWeights.Never, first.Group);
    }

    /// <summary>
    ///     The thresholds read the anchors of the lemma only: the function takes no held-out sentence, and changing the
    ///     held-out rows of a whole training run changes neither a weight nor a threshold.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Thresholds_Choose_NeverReadsHoldoutOrEvaluationSentences()
    {
        // Arrange: no parameter of the function is a corpus row, so a held-out sentence cannot be passed in
        var parameters = typeof(Thresholds).GetMethod(nameof(Thresholds.Choose))!.GetParameters();
        Assert.DoesNotContain(parameters, p => p.ParameterType.ToString().Contains("CorpusRow", StringComparison.Ordinal));
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var anchors = Enumerable.Range(0, 8).SelectMany(i => new[] { StubEmbedding.Row(noun, "noun", i), StubEmbedding.Row(verb, "verb", i) }).ToList();
        CorpusRow[] holdoutA = [StubEmbedding.Row(noun, "noun", 100), StubEmbedding.Row(verb, "verb", 101)];
        CorpusRow[] holdoutB = [StubEmbedding.Row(noun, "verb", 200), StubEmbedding.Row(verb, "noun", 201), StubEmbedding.Row(noun, "noun", 202)];
        var encoder = new CachingEncoder((words, targets, _) => ValueTask.FromResult<double[]?[]>(targets.Select(_ => (double[]?)StubEmbedding.Embed(words)).ToArray()), null);

        // Act
        var none = await ModelTrainer.TrainDataAsync("stub", "m", new byte[32], anchors, Options, encoder, null, null, TextWriter.Null, Stopwatch.StartNew(), TestContext.Current.CancellationToken);
        var withA = await ModelTrainer.TrainDataAsync("stub", "m", new byte[32], anchors, Options, encoder, null, null, TextWriter.Null, Stopwatch.StartNew(), TestContext.Current.CancellationToken, holdoutA);
        var withB = await ModelTrainer.TrainDataAsync("stub", "m", new byte[32], anchors, Options, encoder, null, null, TextWriter.Null, Stopwatch.StartNew(), TestContext.Current.CancellationToken, holdoutB);

        // Assert
        var bytes = WeightsBlobWriter.Write(none);
        Assert.Equal(bytes, WeightsBlobWriter.Write(withA));
        Assert.Equal(bytes, WeightsBlobWriter.Write(withB));
    }

    /// <summary>
    ///     A level whose target is never reached is stored as "never": answers that cannot reach the precision target
    ///     give no fine threshold, a lemma without groups gets no group threshold, and the stored value survives the blob
    ///     and is never met by a finite margin.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_TargetNeverReached_NeverAnswersThatLevel()
    {
        // Arrange: identical features with alternating labels cannot be told apart, so no margin reaches 95 percent
        var features = Enumerable.Repeat(new float[] { 0.6f, 0.8f, 0f, 0f, 0f, 0f }, 20).ToArray();
        var labels = Enumerable.Range(0, 20).Select(i => i % 2).ToArray();

        // Act
        var thresholds = Thresholds.Choose("never", features, labels, 2, [-1, -1], Options);
        var neverWeights = new LemmaWeights(new float[12], [0f, 0f], 6, thresholds.Fine, thresholds.Group);
        var blob = WeightsBlobWriter.Write(new WeightsData("m", new byte[32], 6, new float[6], [new WeightedLemma("never", ["never#a", "never#b"], neverWeights)]));
        var read = WeightsBlob.Read(blob).Lemmas[0].Weights;

        // Assert
        Assert.Equal(LemmaWeights.Never, thresholds.Fine);
        Assert.Equal(LemmaWeights.Never, thresholds.Group);
        Assert.Equal(LemmaWeights.Never, read.FineThreshold);
        Assert.Equal(LemmaWeights.Never, read.GroupThreshold);
        Assert.Equal(AnswerKind.PartOfSpeech, read.Level(1e30, true, true, 1e30));
    }

    /// <summary>
    ///     A lemma with groups gets a group threshold from the anchors that fall through the fine threshold: senses of
    ///     one group that the classifier cannot tell apart are answered by their group; when every anchor is answered at
    ///     the fine level nothing falls through and the group level is never answered.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_GroupThresholdComesFromTheAnchorsThatFallThrough()
    {
        // Arrange: senses 0 and 1 share a direction (confusable, one group), sense 2 is separable and in no group
        var (confusable, labels) = Anchors([0, 0, 1], 12, 0.35);
        int[] groups = [0, 0, -1];

        // Act
        var thresholds = Thresholds.Choose("test", confusable, labels, 3, groups, Options);
        var (separable, separableLabels) = Anchors([0, 1, 2], 12, 0.1);
        var clean = Thresholds.Choose("test", separable, separableLabels, 3, [0, 0, -1], Options);

        // Assert
        Assert.True(thresholds.Fine < LemmaWeights.Never, "the separable sense must reach the target");
        Assert.True(thresholds.Group < LemmaWeights.Never, "the confusable senses must be answered by their group");
        Assert.True(thresholds.Group >= 0);
        Assert.Equal(LemmaWeights.Never, clean.Group);
        Assert.True(clean.Fine < LemmaWeights.Never);
    }

    /// <summary>
    ///     The arrays must agree in length.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_MismatchedArrays_Throw()
    {
        var (features, labels) = Anchors([0, 1], 4, 0.1);

        Assert.Throws<ArgumentException>(() => Thresholds.Choose("x", features, labels[..^1], 2, [-1, -1], Options));
        Assert.Throws<ArgumentException>(() => Thresholds.Choose("x", features, labels, 2, [-1], Options));
    }

    /// <summary>
    ///     The reference of the rule: for every candidate margin, in ascending order, the first whose tail meets the target.
    /// </summary>
    /// <param name="answers">The answers.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The threshold, or never.</returns>
    private static float BruteForce(List<(double Margin, bool Correct)> answers, TrainingOptions options)
    {
        foreach (var candidate in answers.Select(a => a.Margin).Where(m => double.IsFinite(m) && m >= 0).Distinct().Order())
        {
            var tail = answers.Where(a => a.Margin >= candidate).ToList();
            if (tail.Count >= options.MinAnswered && tail.Count(a => a.Correct) >= (options.TargetPrecision * tail.Count) - 1e-9)
            {
                return (float)candidate;
            }
        }

        return LemmaWeights.Never;
    }
}
