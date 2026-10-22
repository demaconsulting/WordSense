// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the usage prior: the offsets, their wiring into the training, the thresholds and the fit cache key,
///     and the options of the builder. No encoder and no model files are involved.
/// </summary>
public sealed class SensePriorTests
{
    private static readonly byte[] Hash = new byte[32];

    private static readonly TrainingOptions Options = new(Parallelism: 1);

    private static readonly PriorSettings Defaults = new();

    /// <summary>
    ///     Each offset is <c>-ln q + alpha ln pi</c> with the shares of the documented formula, and adding the offsets
    ///     to the scores gives the probabilities <c>p / q * pi^alpha</c>, normalized.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SensePrior_Offsets_FollowTheDocumentedFormula()
    {
        // Arrange: three senses with 10, 30 and 60 anchors and the tag counts 5, 0 and 15
        int[] anchorCounts = [10, 30, 60];
        int[] tagCounts = [5, 0, 15];

        // Act
        var offsets = SensePrior.Offsets(anchorCounts, tagCounts, Defaults);

        // Assert: hand-computed, the usage shares are (count + 0.5) / 21.5
        Assert.Equal(3, offsets.Length);
        Assert.Equal(-Math.Log(0.1) + (0.5 * Math.Log(5.5 / 21.5)), offsets[0], 12);
        Assert.Equal(-Math.Log(0.3) + (0.5 * Math.Log(0.5 / 21.5)), offsets[1], 12);
        Assert.Equal(-Math.Log(0.6) + (0.5 * Math.Log(15.5 / 21.5)), offsets[2], 12);

        // The softmax of the corrected scores is p / q * pi^alpha, normalized
        double[] scores = [0.3, -0.2, 0.9];
        var p = Softmax(scores);
        double[] expected = [p[0] / 0.1 * Math.Sqrt(5.5 / 21.5), p[1] / 0.3 * Math.Sqrt(0.5 / 21.5), p[2] / 0.6 * Math.Sqrt(15.5 / 21.5)];
        var total = expected.Sum();
        var corrected = Softmax([.. scores.Select((l, i) => l + offsets[i])]);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(expected[i] / total, corrected[i], 12);
        }
    }

    /// <summary>
    ///     A project sense (no sense key) and a sense that the table does not list get the smoothing constant alone,
    ///     which is what a sense with the count 0 gets.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SensePrior_Offsets_SenseWithoutCount_UsesTheSmoothingAlone()
    {
        // Arrange
        var counted = new SenseInfo("w#counted", null, "w", PartOfSpeech.Noun, "w%1:00:00::");
        var zero = new SenseInfo("w#zero", null, "w", PartOfSpeech.Noun, "w%1:00:01::");
        var unlisted = new SenseInfo("w#unlisted", null, "w", PartOfSpeech.Noun, "w%1:00:02::");
        var project = new SenseInfo("w#project", null, "w", PartOfSpeech.Noun, null);
        var usage = new SenseUsage(new Dictionary<string, int> { ["w%1:00:00::"] = 8, ["w%1:00:01::"] = 0 }, Defaults);

        // Act
        var offsets = SensePrior.Offsets([counted, zero, unlisted, project], [5, 5, 5, 5], usage);

        // Assert
        Assert.Equal(offsets[1], offsets[2]);
        Assert.Equal(offsets[1], offsets[3]);
        Assert.Equal(-Math.Log(0.25) + (0.5 * Math.Log(0.5 / 10.0)), offsets[3], 12);
        Assert.True(offsets[0] > offsets[3]);
    }

    /// <summary>
    ///     Without the exponent only the share of the sense among the anchors is divided out, whatever the counts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SensePrior_Offsets_AlphaZero_RemovesOnlyTheTrainingShare()
    {
        // Act
        var offsets = SensePrior.Offsets([10, 30], [100, 0], new PriorSettings(0, 0.5));

        // Assert
        Assert.Equal(-Math.Log(0.25), offsets[0], 12);
        Assert.Equal(-Math.Log(0.75), offsets[1], 12);
    }

    /// <summary>
    ///     The offsets are the same bits on every call, and unusable input is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SensePrior_Offsets_AreDeterministic()
    {
        // Act
        var first = SensePrior.Offsets([7, 3, 11, 1], [4, 0, 9, 2], Defaults);
        var second = SensePrior.Offsets([7, 3, 11, 1], [4, 0, 9, 2], Defaults);

        // Assert
        Assert.Equal(first.Select(BitConverter.DoubleToInt64Bits), second.Select(BitConverter.DoubleToInt64Bits));
        Assert.Throws<ArgumentException>(() => SensePrior.Offsets([1, 2], [1], Defaults));
        Assert.Throws<ArgumentException>(() => SensePrior.Offsets([0, 2], [1, 1], Defaults));
        Assert.Throws<ArgumentException>(() => SensePrior.Offsets([1, 2], [1, -1], Defaults));
        Assert.Throws<ArgumentException>(() => SensePrior.Offsets([1, 2], [1, 1], new PriorSettings(-1, 0.5)));
        Assert.Throws<ArgumentException>(() => SensePrior.Offsets([1, 2], [1, 1], new PriorSettings(0.5, 0)));
    }

    /// <summary>
    ///     The tag counts are read from the partitioned frequency table by sense key; a missing table is refused rather
    ///     than silently giving a prior without counts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SensePrior_ReadCounts_ReadsTheFrequencyTable()
    {
        // Arrange
        using var dir = new TestDirectory();
        dir.Write("sense_frequency/ab.tsv", "sense_key\ttag_count\tsource\nab%1:08:00::\t0\twn30\nab%1:10:00::\t7\twn30\n");
        dir.Write("sense_frequency/ba.tsv", "sense_key\ttag_count\tsource\nba%1:10:00::\t3\twn30\n");

        // Act
        var counts = SensePrior.ReadCounts(dir.Root);

        // Assert
        Assert.Equal(3, counts.Count);
        Assert.Equal(7, counts["ab%1:10:00::"]);
        Assert.Equal(0, counts["ab%1:08:00::"]);
        Assert.Equal(3, counts["ba%1:10:00::"]);
        using var empty = new TestDirectory();
        Assert.Throws<InvalidDataException>(() => SensePrior.ReadCounts(empty.Root));
    }

    /// <summary>
    ///     A lemma trained with the prior has the weights of the lemma trained without it, and each bias is shifted by
    ///     exactly the offset of its sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_UsagePrior_ShiftsTheBiasesByTheOffsets()
    {
        // Arrange
        var (rows, pooled, usage) = Walk();

        // Act
        var plain = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options).Data.Lemmas[0];
        var prior = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options, usage: usage).Data.Lemmas[0];

        // Assert
        var senses = prior.SenseIds.Select(id => WordLexicon.TryGetSense(id, out var s) ? s : throw new InvalidOperationException(id)).ToList();
        var offsets = SensePrior.Offsets(senses, [4, 6], usage);
        Assert.Equal(plain.Weights.Weights, prior.Weights.Weights);
        Assert.Equal(2, prior.Weights.Bias.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal((float)(plain.Weights.Bias[i] + offsets[i]), prior.Weights.Bias[i]);
        }

        Assert.NotEqual(plain.Weights.Bias, prior.Weights.Bias);
    }

    /// <summary>
    ///     The trained prior is the same bits with one thread and with many and keeps the number of weights and
    ///     biases, so the weights do not grow.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_TrainDense_UsagePrior_IsDeterministicAndKeepsTheSize()
    {
        // Arrange
        var (rows, pooled, usage) = Walk();

        // Act
        var one = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options, usage: usage).Data;
        var many = WeightsTrainer.TrainDense("m", Hash, pooled, rows, new TrainingOptions(Parallelism: 4), usage: usage).Data;
        var plain = WeightsTrainer.TrainDense("m", Hash, pooled, rows, Options).Data;

        // Assert
        Assert.Equal(one.Lemmas[0].Weights.Bias, many.Lemmas[0].Weights.Bias);
        Assert.Equal(one.Lemmas[0].Weights.Weights, many.Lemmas[0].Weights.Weights);
        Assert.Equal(one.Lemmas[0].Weights.FineThreshold, many.Lemmas[0].Weights.FineThreshold);
        Assert.Equal(plain.Lemmas[0].Weights.Bias.Length, one.Lemmas[0].Weights.Bias.Length);
        Assert.Equal(plain.Lemmas[0].Weights.Weights.Length, one.Lemmas[0].Weights.Weights.Length);
        Assert.NotEmpty(WeightsBlobWriter.Write(one));
    }

    /// <summary>
    ///     Offsets that favor one sense change the out-of-fold margins and so the thresholds; zero offsets change
    ///     nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_Offsets_AreAppliedToTheOutOfFoldScores()
    {
        // Arrange: two overlapping senses
        var features = new List<float[]>();
        var labels = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            foreach (var sense in new[] { 0, 1 })
            {
                var wobble = (float)Math.Sin((sense * 31.0) + (i * 12.9898));
                var vector = new[] { sense == 0 ? 1f : 0.55f, sense == 0 ? 0.55f : 1f, 0.6f * wobble };
                var norm = MathF.Sqrt(vector.Sum(v => v * v));
                features.Add([.. vector.Select(v => v / norm)]);
                labels.Add(sense);
            }
        }

        int[] groups = [-1, -1];

        // Act
        var none = Thresholds.Choose("walk", [.. features], [.. labels], 2, groups, Options);
        var zero = Thresholds.Choose("walk", [.. features], [.. labels], 2, groups, Options, null, [0.0, 0.0]);
        var favored = Thresholds.Choose("walk", [.. features], [.. labels], 2, groups, Options, null, [3.0, -3.0]);

        // Assert
        Assert.Equal(none, zero);
        Assert.NotEqual(none.Fine, favored.Fine);
    }

    /// <summary>
    ///     Another offset gives another key, so a cached fit is never reused for a different prior.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_KeyOf_DependsOnThePriorOffsets()
    {
        // Arrange
        string[] senses = ["bank#noun", "bank#verb"];
        int[] groups = [-1, -1];
        int[] labels = [0, 1];
        float[][] features = [[1f, 0f], [0f, 1f]];
        var key = FitCache.KeyOf(Options, "bank", senses, groups, labels, features);

        // Act and Assert
        Assert.Equal(key, FitCache.KeyOf(Options, "bank", senses, groups, labels, features, null));
        var withOffsets = FitCache.KeyOf(Options, "bank", senses, groups, labels, features, [0.1, -0.1]);
        Assert.NotEqual(key, withOffsets);
        Assert.NotEqual(withOffsets, FitCache.KeyOf(Options, "bank", senses, groups, labels, features, [0.1, -0.2]));
        Assert.Equal(withOffsets, FitCache.KeyOf(Options, "bank", senses, groups, labels, features, [0.1, -0.1]));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bank", senses, groups, labels, features, [0.0, 0.0]));
    }

    /// <summary>
    ///     The prior is on by default with the documented settings; the options switch it off and change the settings,
    ///     and a value out of range is an error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Program_TrainSettingsOf_ParsesThePriorOptions()
    {
        // Act
        var defaults = Program.TrainSettingsOf([], Path.GetTempPath());
        var off = Program.TrainSettingsOf(new() { ["no-prior"] = "true" }, Path.GetTempPath());
        var custom = Program.TrainSettingsOf(new() { ["prior-alpha"] = "1", ["prior-smoothing"] = "0.25" }, Path.GetTempPath());

        // Assert
        Assert.True(defaults.UsagePrior);
        Assert.Equal(0.5, defaults.PriorAlpha);
        Assert.Equal(0.5, defaults.PriorSmoothing);
        Assert.False(off.UsagePrior);
        Assert.True(custom.UsagePrior);
        Assert.Equal(1.0, custom.PriorAlpha);
        Assert.Equal(0.25, custom.PriorSmoothing);
        Assert.Throws<ArgumentException>(() => Program.TrainSettingsOf(new() { ["prior-alpha"] = "-1" }, Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => Program.TrainSettingsOf(new() { ["prior-alpha"] = "x" }, Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => Program.TrainSettingsOf(new() { ["prior-smoothing"] = "0" }, Path.GetTempPath()));
    }

    private static (CorpusRow[] Rows, double[][] Pooled, SenseUsage Usage) Walk()
    {
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var rows = new List<CorpusRow>();
        var pooled = new List<double[]>();
        var number = 0;
        foreach (var (sense, count, direction) in new[] { (noun, 4, new double[] { 1, 0, 0 }), (verb, 6, new double[] { 0, 1, 0 }) })
        {
            for (var i = 0; i < count; i++)
            {
                number++;
                rows.Add(SenseFixtures.Row(sense, number));
                pooled.Add([direction[0] + (0.02 * i), direction[1] - (0.01 * i), direction[2] + (0.015 * i)]);
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [noun.SenseKey ?? "none-noun"] = 40,
            [verb.SenseKey ?? "none-verb"] = 2,
        };
        return ([.. rows], [.. pooled], new SenseUsage(counts, Defaults));
    }

    private static double[] Softmax(double[] scores)
    {
        var exp = scores.Select(Math.Exp).ToArray();
        var sum = exp.Sum();
        return [.. exp.Select(e => e / sum)];
    }
}
