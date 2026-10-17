using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxClassifier"/> with the real MiniLM encoder and, for the scoring tests, weights built
///     from the embedded MiniLM weights. They are skipped when the model files are not available.
/// </summary>
public class OnnxClassifierTests
{
    private const string Resource = "DemaConsulting.WordSense.MiniLM.weights.bin";

    private static readonly string[] Sentence = ["They", "will", "record", "the", "record", "."];

    private static OnnxEncoder Open()
    {
        var directory = ModelAssets.RequireModelDirectory();
        return new OnnxEncoder(Path.Combine(directory, "model_int8.onnx"), Path.Combine(directory, "vocab.txt"), true, 128, 2);
    }

    private static WeightedLemmaTable Embedded() =>
        WeightedLemmaTable.LoadEmbedded(typeof(MiniLMWordSenseModel).Assembly, Resource, "all-MiniLM-L6-v2-int8", 384);

    /// <summary>
    ///     Builds a table holding only the weights of one lemma, scaled by a factor.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="scale">The factor applied to weights and bias; the abstain margin is kept.</param>
    /// <returns>The table.</returns>
    private static WeightedLemmaTable Scaled(string lemma, float scale)
    {
        var embedded = Embedded();
        var source = embedded.Find(lemma)!;
        var w = source.Weights;
        var scaled = new LemmaWeights(w.Kind, [.. w.Weights.Select(x => x * scale)], [.. w.Bias.Select(x => x * scale)], w.AbstainMargin, w.Dimension);
        var data = new WeightsData(embedded.Data.ModelId, embedded.Data.Dimension, embedded.Data.Mean, [new WeightedLemma(source.Lemma, source.Senses, scaled)]);
        return new WeightedLemmaTable(data, WordLexicon.Words);
    }

    private static string SingleSenseWord() => WordLexicon.Words.First(p => p.Value.Length == 1 && p.Key.All(char.IsAsciiLetterLower)).Key;

    /// <summary>
    ///     A word that is not in the lexicon gets no details and confidence 0.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        var result = await classifier.ClassifyAsync(["zzqxjw"], TestContext.Current.CancellationToken);

        Assert.Equal(new WordSense("zzqxjw", null, 0), result[0]);
    }

    /// <summary>
    ///     A word with exactly one sense gets that sense and confidence 1.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SingleSenseWord_ReturnsEntryWithFullConfidence()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());
        var word = SingleSenseWord();

        var result = await classifier.ClassifyAsync([word], TestContext.Current.CancellationToken);

        Assert.Equal(new WordSense(word, WordLexicon.Words[word][0], 1), result[0]);
    }

    /// <summary>
    ///     Without ambiguous words the encoder is not used: a disposed encoder does not matter.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NoAmbiguousWords_DoesNotEncode()
    {
        var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());
        encoder.Dispose();

        var result = await classifier.ClassifyAsync([SingleSenseWord(), "zzqxjw", "."], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Length);
    }

    /// <summary>
    ///     An ambiguous word needs the encoder: with a disposed encoder the call fails.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_AmbiguousWords_UseTheEncoder()
    {
        var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        encoder.Dispose();

        Assert.NotNull(result[2].Details);
        Assert.NotNull(result[4].Details);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Only the ambiguous words are decided by the encoder; the others are answered from the lexicon.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_OnlyAmbiguousWordsAreEncoded()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());
        var word = SingleSenseWord();

        var result = await classifier.ClassifyAsync([word, "record", "zzqxjw"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result[0].Confidence);
        Assert.NotNull(result[1].Details);
        Assert.Equal(new WordSense("zzqxjw", null, 0), result[2]);
    }

    /// <summary>
    ///     A form that has fewer senses than its lemma is ranked only among its own senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FormWithFewerSenses_RanksOnlyItsOwnSenses()
    {
        var table = Embedded();
        var form = WordLexicon.Words
            .Where(p => p.Value.Length > 1 && table.Find(p.Value[0].Lemma) is not null && WordLexicon.Words[p.Value[0].Lemma].Length > p.Value.Length)
            .Select(p => p.Key)
            .FirstOrDefault();
        if (form is null)
        {
            Assert.Skip("The lexicon has no ambiguous form with fewer senses than its lemma.");
        }

        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, table);

        var result = await classifier.ClassifyAsync(["the", form, "."], TestContext.Current.CancellationToken);

        Assert.Contains(result[1].Details, WordLexicon.Words[form]);
    }

    /// <summary>
    ///     Larger weights give a larger margin and so a higher confidence, for the same text.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LargerMargin_GivesHigherConfidence()
    {
        using var encoder = Open();
        var small = new OnnxClassifier(encoder, Scaled("record", 0.01f));
        var large = new OnnxClassifier(encoder, Scaled("record", 3f));

        var low = await small.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var high = await large.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.True(high[2].Confidence > low[2].Confidence);
    }

    /// <summary>
    ///     Every confidence is within 0 and 1.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ConfidenceIsWithinZeroAndOne()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        var result = await classifier.ClassifyAsync(["They", "will", "close", "the", "close", "record", "to", "set", "a", "record", "."], TestContext.Current.CancellationToken);

        Assert.All(result, r => Assert.InRange(r.Confidence, 0.0, 1.0));
    }

    /// <summary>
    ///     A low margin still returns the best candidate, with a low confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LowMargin_ReturnsBestCandidateWithLowConfidence()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Scaled("record", 0.0001f));

        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.NotNull(result[2].Details);
        Assert.True(result[2].Confidence <= 0.5);
    }

    /// <summary>
    ///     An ambiguous word whose lemma has no weights abstains.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_Abstains()
    {
        using var encoder = Open();
        var empty = new WeightedLemmaTable(new WeightsData("all-MiniLM-L6-v2-int8", 384, new float[384], []), WordLexicon.Words);
        var classifier = new OnnxClassifier(encoder, empty);

        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.Equal(new WordSense("record", null, 0), result[2]);
    }

    /// <summary>
    ///     Weights of another dimension than the encoder's make an ambiguous word abstain.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_DimensionMismatch_Abstains()
    {
        using var encoder = Open();
        var source = Embedded().Find("record")!;
        var weights = new LemmaWeights(LemmaModelKind.OneVersusRest, new float[source.Senses.Count * 3], new float[source.Senses.Count], 0.1f, 3);
        var table = new WeightedLemmaTable(
            new WeightsData("all-MiniLM-L6-v2-int8", 3, new float[3], [new WeightedLemma("record", source.Senses, weights)]),
            WordLexicon.Words);
        var classifier = new OnnxClassifier(encoder, table);

        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.Equal(new WordSense("record", null, 0), result[2]);
    }

    /// <summary>
    ///     An empty list gives an empty result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_EmptyList_ReturnsEmpty()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        Assert.Empty(await classifier.ClassifyAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NullWords_Throws()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null element is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NullElement_Throws()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        await Assert.ThrowsAsync<ArgumentException>(() => classifier.ClassifyAsync(["record", null!], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A cancelled call is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_Cancelled_Throws()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync(Sentence, cancelled.Token));
    }
}
