using System.Collections.Immutable;
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
    ///     An untrained trailing Auxiliary sense is not counted: the encoder models never answer it, so the fallback
    ///     confidence of a word that has it stays what it was before the sense existed.
    /// </summary>
    [Fact]
    public void OnnxClassifier_ResolveWithoutModel_TrailingAuxiliarySense_IsIgnored()
    {
        var results = OnnxClassifier.ResolveWithoutModel(["is", "will", "can", "have", "record"], CancellationToken.None);

        Assert.Equal(new WordSense("is", WordLexicon.Words["is"][0], 1), results[0]);
        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
        Assert.Equal(0.5, results[1].Confidence);
        Assert.Equal(PartOfSpeech.Verb, results[2].Details?.PartOfSpeech);
        Assert.Equal(0.5, results[2].Confidence);
        Assert.Equal(0.5, results[3].Confidence);
        Assert.All(results, r => Assert.NotEqual(PartOfSpeech.Auxiliary, r.Details?.PartOfSpeech));
    }

    /// <summary>
    ///     The auxiliary verbs have no trained weights, so a text with only auxiliaries needs no model and the cheap
    ///     pre-check does not count their auxiliary sense as an ambiguity.
    /// </summary>
    [Fact]
    public void OnnxClassifier_HasMultiSenseWord_OnlyAuxiliaryAmbiguity_IsFalse()
    {
        Assert.False(OnnxClassifier.HasMultiSenseWord(["is", "been", "the"]));
        Assert.True(OnnxClassifier.HasMultiSenseWord(["they", "will", "go"]));
    }

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
    ///     A form that has fewer senses than its lemma is ranked only among its own senses: the candidates of the
    ///     form decide the result whatever the weights of the other senses of the lemma score.
    /// </summary>
    [Fact]
    public void OnnxClassifier_ClassifyAsync_FormWithFewerSenses_RanksOnlyItsOwnSenses()
    {
        var lemma = Embedded().Find("record")!;
        var all = WordLexicon.Words["record"];
        Assert.True(all.Length > 1, "The lemma needs several senses.");
        var feature = new float[lemma.Weights.Dimension];
        feature[0] = 1f;

        foreach (var own in all)
        {
            var (details, confidence) = OnnxClassifier.Pick(lemma, [own], feature);

            Assert.Equal(own, details);
            Assert.InRange(confidence, 0.0, 1.0);
        }
    }
    /// <summary>
    ///     A form whose senses are a non-prefix subset of the senses of its lemma is ranked only among those senses,
    ///     matched by identity, and never answers a sense it does not list; a single listed sense answers with full
    ///     confidence.
    /// </summary>
    [Fact]
    public void OnnxClassifier_Pick_NonPrefixSubset_IsRankedByIdentity()
    {
        var lemma = Embedded().Find("bow")!;
        var all = WordLexicon.Words["bows"];
        Assert.Equal(3, all.Length);
        ImmutableArray<WordDetails> subset = [all[0], all[2]];
        var random = new Random(7);

        for (var round = 0; round < 20; round++)
        {
            var feature = new float[lemma.Weights.Dimension];
            for (var i = 0; i < feature.Length; i++)
            {
                feature[i] = (float)(random.NextDouble() - 0.5);
            }

            var (details, confidence) = OnnxClassifier.Pick(lemma, subset, feature);

            Assert.Contains(details, subset);
            Assert.NotEqual(all[1], details);
            Assert.InRange(confidence, 0.0, 1.0);

            var (single, certain) = OnnxClassifier.Pick(lemma, [all[2]], feature);
            Assert.Equal(all[2], single);
            Assert.Equal(1.0, certain);
        }
    }

    /// <summary>
    ///     A listed form with a single sense needs no model: <c>bowed</c> answers its one sense with confidence 1 even
    ///     though its lemma has weights.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FormWithSingleListedSense_ReturnsItWithFullConfidence()
    {
        var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());
        encoder.Dispose();

        var result = await classifier.ClassifyAsync(["bowed"], TestContext.Current.CancellationToken);

        Assert.Equal(new WordSense("bowed", new WordDetails("bow", PartOfSpeech.Verb, "bend", "bow"), 1), result[0]);
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
    ///     An ambiguous word whose lemma has no weights gets its first dictionary sense with a confidence of one over
    ///     the number of senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_FallsBackToFirstSense()
    {
        using var encoder = Open();
        var empty = new WeightedLemmaTable(new WeightsData("all-MiniLM-L6-v2-int8", 384, new float[384], []), WordLexicon.Words);
        var classifier = new OnnxClassifier(encoder, empty);

        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        var senses = WordLexicon.Words["record"];
        Assert.Equal(new WordSense("record", senses[0], 1.0 / senses.Length), result[2]);
    }

    /// <summary>
    ///     A word with trained weights and more dictionary senses than trained ones is ranked among the trained senses
    ///     only, and the extra sense is never the answer.
    /// </summary>
    [Fact]
    public void OnnxClassifier_Pick_ExtraDictionarySenses_AreNeverRanked()
    {
        var lemma = Embedded().Find("record")!;
        var extra = new WordDetails("record", PartOfSpeech.Determiner, null, null);
        ImmutableArray<WordDetails> candidates = [.. WordLexicon.Words["record"], extra];
        var feature = new float[lemma.Weights.Dimension];
        feature[0] = 1f;

        var (details, confidence) = OnnxClassifier.Pick(lemma, candidates, feature);

        Assert.NotNull(details);
        Assert.NotEqual(extra, details);
        Assert.InRange(confidence, 0.0, 1.0);
    }

    /// <summary>
    ///     A capitalized word resolves like its lower-case form, and a determiner needs no model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_CapitalizedWord_ResolvesLikeLowerCase()
    {
        using var encoder = Open();
        var classifier = new OnnxClassifier(encoder, Embedded());

        var result = await classifier.ClassifyAsync(["The", "the"], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Determiner, result[0].Details!.PartOfSpeech);
        Assert.Equal(result[0].Details, result[1].Details);
        Assert.Equal(1.0, result[0].Confidence);
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
