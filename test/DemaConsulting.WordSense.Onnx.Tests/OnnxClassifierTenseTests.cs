namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the tense evidence of <see cref="OnnxClassifier"/>: the encoder runs for every same-spelling verb of a
///     classifier with a tense head, the head gives the probability that the verb is past, and the answers themselves do
///     not depend on the tense head.
/// </summary>
public class OnnxClassifierTenseTests
{
    private const double Tolerance = 1e-9;

    // The tense head reads one component of the vector of a word (word layout, weight 1, no bias)
    private const int TenseComponent = 5;

    private static PosHead Head(bool withTense, int layout = PosHeadBlob.TenseWord)
    {
        var d = Fixtures.Dimension;
        var weights = new float[(layout == PosHeadBlob.TenseWord ? 1 : 3) * d];
        weights[(layout == PosHeadBlob.TenseWord ? 0 : d) + TenseComponent] = 1f;
        var spec = HeadSpec.Create(d);
        if (withTense)
        {
            spec = spec with { Sections = [(PosHeadBlob.TenseSection, false, HeadBlobWriter.TensePayload(layout, 0.03f, weights, 0f))] };
        }

        return new PosHead(PosHeadBlob.Read(HeadBlobWriter.Write(spec)));
    }

    private static double[] Vector(double logit)
    {
        var vector = new double[Fixtures.Dimension];
        vector[TenseComponent] = logit;
        return vector;
    }

    private static OnnxClassifier Classifier(FakeViewEncoder view, bool useTense, bool withTense = true, int layout = PosHeadBlob.TenseWord) =>
        new(new WeightedLemmaTable(), new FakeEncoder((double[]?)null).EncodeAsync, Head(withTense, layout), view.EncodeAsync, useTense);

    private static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

    /// <summary>
    ///     The tense head gives the probability of past of a same-spelling verb and nothing for any other word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyWithTenseAsync_SameSpellingVerb_GivesTheProbabilityOfPast()
    {
        // Arrange: "become" is a verb of one part of speech with a past and a non-past analysis; "quickly" is no verb
        Assert.True(TenseFusion.IsSameSpellingVerb(WordLexicon.Lookup("become")));
        var view = new FakeViewEncoder(_ => Vector(1.5));
        var classifier = Classifier(view, true);

        // Act
        var (results, past) = await classifier.ClassifyWithTenseAsync(["become", "quickly"], TestContext.Current.CancellationToken);

        // Assert: one probability, for "become", the sigmoid of the component
        Assert.Equal(2, results.Length);
        Assert.Equal(Sigmoid(1.5), past[0]!.Value, Tolerance);
        Assert.Null(past[1]);
        Assert.Equal(1, view.Calls);
    }

    /// <summary>
    ///     A classifier without a tense head still answers the same, gives no probability and does not encode a text that
    ///     has only same-spelling verbs of one part of speech.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyWithTenseAsync_WithoutTenseHead_GivesNoProbabilityAndTheSameResults()
    {
        // Arrange
        var withView = new FakeViewEncoder(_ => Vector(2.0));
        var withoutView = new FakeViewEncoder(_ => Vector(2.0));
        var with = Classifier(withView, true);
        var without = Classifier(withoutView, false, withTense: false);
        string[] words = ["become", "quickly"];

        // Act
        var (resultsWith, _) = await with.ClassifyWithTenseAsync(words, TestContext.Current.CancellationToken);
        var (resultsWithout, pastWithout) = await without.ClassifyWithTenseAsync(words, TestContext.Current.CancellationToken);

        // Assert: no encoder call for the classifier that does not judge tense; the answers are equal
        Assert.All(pastWithout, p => Assert.Null(p));
        Assert.Equal(0, withoutView.Calls);
        Assert.Equal(resultsWithout.Select(r => (r.Word, r.Level, r.Confidence, r.Details)), resultsWith.Select(r => (r.Word, r.Level, r.Confidence, r.Details)));
    }

    /// <summary>
    ///     The neighbor layout reads the vectors of the neighbors, and a word without a usable vector has no probability.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyWithTenseAsync_WordWithoutVector_HasNoProbability()
    {
        // Arrange: the view gives "become" no vector
        var view = new FakeViewEncoder(token => token == "become" ? null : Vector(1.0));
        var classifier = Classifier(view, true);

        // Act
        var (_, past) = await classifier.ClassifyWithTenseAsync(["quickly", "become"], TestContext.Current.CancellationToken);

        // Assert
        Assert.All(past, p => Assert.Null(p));
    }

    /// <summary>
    ///     A classifier that is to judge tense needs a head with a tense head.
    /// </summary>
    [Fact]
    public void OnnxClassifier_Constructor_TenseWithoutTenseHead_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => Classifier(new FakeViewEncoder(_ => null), true, withTense: false));
    }

    /// <summary>
    ///     A same-spelling verb needs the encoder only when the classifier judges tense.
    /// </summary>
    [Fact]
    public void OnnxClassifier_NeedsEncoder_SameSpellingVerb_NeedsTheEncoderOnlyWithTense()
    {
        // Arrange
        var table = new WeightedLemmaTable();

        // Act / Assert
        Assert.False(OnnxClassifier.NeedsEncoder(["become"], table, true));
        Assert.False(OnnxClassifier.NeedsEncoder(["become"], table, true, false));
        Assert.True(OnnxClassifier.NeedsEncoder(["become"], table, true, true));
        Assert.False(OnnxClassifier.NeedsEncoder(["quickly", "."], table, false, true));
        Assert.False(OnnxClassifier.NeedsEncoder(["become"], table, false, true));
    }
}
