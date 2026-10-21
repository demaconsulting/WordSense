// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the training of the form choosers (<see cref="WeightsTrainer.AttachChoosers"/>); no encoder is involved,
///     the features are small hand-made vectors and the forms are real forms of the word image.
/// </summary>
public sealed class ChooserTrainingTests
{
    private static readonly byte[] Hash = new byte[32];
    private static readonly TrainingOptions Options = new(Parallelism: 1);

    private static WeightedLemma Lemma(string lemma) =>
        new(lemma, [lemma + "#a", lemma + "#b"], new LemmaWeights(new float[6], new float[2], 3));

    private static WeightsData Weights(params string[] lemmas) =>
        new("m", Hash, 3, new float[3], [.. lemmas.Select(Lemma)]);

    private static (ChooserRow[] Rows, float[][] Features) Rows(string form, params (string Label, float[] Direction)[] labels)
    {
        var rows = new List<ChooserRow>();
        var features = new List<float[]>();
        var n = 0;
        foreach (var (label, direction) in labels)
        {
            for (var i = 0; i < 6; i++)
            {
                n++;
                rows.Add(new ChooserRow(form, label, $"The {{{form}}} {n}.", "a.tsv", ["The", form, n.ToString(System.Globalization.CultureInfo.InvariantCulture), "."], 1));
                features.Add([direction[0] + (0.03f * i), direction[1] - (0.02f * i), direction[2] + (0.01f * i)]);
            }
        }

        return ([.. rows], [.. features]);
    }

    private static readonly float[] A = [1, 0, 0];
    private static readonly float[] B = [0, 1, 0];
    private static readonly float[] C = [0, 0, 1];

    /// <summary>
    ///     A form of two lemmas gets a chooser with the labels in ordinal order that tells them apart, stored with the
    ///     first lemma of the form that has weights.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_AttachChoosers_FormOfTwoLemmas_StoresTheChooserWithTheFirstTrainedLemma()
    {
        // Arrange: used is the past of use and the adjective used
        var (rows, features) = Rows("used", ("used", B), ("use", A));

        // Act
        var (data, report) = WeightsTrainer.AttachChoosers(Weights("use", "used"), rows, features, Options);

        // Assert
        var chooser = Assert.Single(report.Trained);
        Assert.Empty(report.Skipped);
        Assert.Equal("used", chooser.Form);
        Assert.Equal(["use", "used"], chooser.Labels);
        Assert.Equal(2, chooser.Weights.SenseCount);
        Assert.True(chooser.Weights.FineThreshold >= 0);
        Assert.Same(chooser, Assert.Single(data.Lemmas.First(l => l.Lemma == "use").Choosers));
        Assert.Empty(data.Lemmas.First(l => l.Lemma == "used").Choosers);
        var scores = new float[2];
        chooser.Weights.Scores(A, scores);
        Assert.Equal(0, LemmaWeights.Rank(scores).Best);
        chooser.Weights.Scores(B, scores);
        Assert.Equal(1, LemmaWeights.Rank(scores).Best);
    }

    /// <summary>
    ///     When only the second lemma of the form has weights the chooser is stored with it, and the blob that holds it
    ///     is accepted by the writer and the reader.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_AttachChoosers_OnlyTheSecondLemmaTrained_StoresItThere_AndTheBlobRoundTrips()
    {
        // Arrange
        var (rows, features) = Rows("used", ("used", B), ("use", A));

        // Act
        var (data, _) = WeightsTrainer.AttachChoosers(Weights("used"), rows, features, Options);
        var read = WeightsBlob.Read(WeightsBlobWriter.Write(data));

        // Assert
        var chooser = Assert.Single(Assert.Single(read.Lemmas).Choosers);
        Assert.Equal("used", chooser.Form);
        Assert.Equal(["use", "used"], chooser.Labels);
    }

    /// <summary>
    ///     The analyses of one lemma that differ in tense get their own labels (lemma/features), and the other analyses of
    ///     the lemma share the label of the lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_AttachChoosers_TenseVariants_GetOneLabelEach()
    {
        // Arrange: read is a present verb, a past verb, a noun and an adjective
        var (rows, features) = Rows("read", ("read/Tense=Pres", A), ("read/Tense=Past+VerbForm=Part", B), ("read", C));

        // Act
        var (_, report) = WeightsTrainer.AttachChoosers(Weights("read"), rows, features, Options);

        // Assert
        var chooser = Assert.Single(report.Trained);
        Assert.Equal(["read", "read/Tense=Past+VerbForm=Part", "read/Tense=Pres"], chooser.Labels);
    }

    /// <summary>
    ///     A form gets no chooser, and says why, when its rows name one label, when an analysis has no label, or when no
    ///     lemma of the form has weights.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_AttachChoosers_UnusableForms_AreSkippedWithAReason()
    {
        // Arrange
        var (one, oneFeatures) = Rows("used", ("use", A));
        var (partial, partialFeatures) = Rows("read", ("read/Tense=Pres", A), ("read/Tense=Past+VerbForm=Part", B));
        var (orphan, orphanFeatures) = Rows("bases", ("base", A), ("basis", B));
        ChooserRow[] rows = [.. one, .. partial, .. orphan];
        float[][] features = [.. oneFeatures, .. partialFeatures, .. orphanFeatures];

        // Act
        var (data, report) = WeightsTrainer.AttachChoosers(Weights("use", "read"), rows, features, Options);

        // Assert
        Assert.Empty(report.Trained);
        Assert.Equal(["bases", "read", "used"], report.Skipped.Select(s => s.Form));
        Assert.Contains("no lemma of the form has weights", report.Skipped[0].Reason, StringComparison.Ordinal);
        Assert.Contains("no row has the label of the analysis", report.Skipped[1].Reason, StringComparison.Ordinal);
        Assert.Contains("a chooser needs two", report.Skipped[2].Reason, StringComparison.Ordinal);
        Assert.All(data.Lemmas, l => Assert.Empty(l.Choosers));
    }

    /// <summary>
    ///     The training is deterministic, and a feature missing for a row is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_AttachChoosers_IsDeterministic_AndNeedsOneFeaturePerRow()
    {
        // Arrange
        var (rows, features) = Rows("used", ("used", B), ("use", A));

        // Act
        var first = WeightsTrainer.AttachChoosers(Weights("use"), rows, features, Options).Data;
        var second = WeightsTrainer.AttachChoosers(Weights("use"), rows, features, Options with { Parallelism = 4 }).Data;

        // Assert
        Assert.Equal(WeightsBlobWriter.Write(first), WeightsBlobWriter.Write(second));
        Assert.Throws<ArgumentException>(() => WeightsTrainer.AttachChoosers(Weights("use"), rows, features[1..], Options));
    }
}
