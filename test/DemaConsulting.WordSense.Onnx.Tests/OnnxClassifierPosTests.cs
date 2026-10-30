using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     A fake of the view encoder delegate: it splits the possessives of a text like the real one and returns designed
///     vectors, and it records what it was asked.
/// </summary>
/// <param name="vectorOf">Gives the vector of a token of the view, or null for a token without word pieces.</param>
internal sealed class FakeViewEncoder(Func<string, double[]?> vectorOf)
{
    /// <summary>
    ///     Gets the words of every call.
    /// </summary>
    public List<string[]> Texts { get; } = [];

    /// <summary>
    ///     Gets the number of calls.
    /// </summary>
    public int Calls => Texts.Count;

    /// <summary>
    ///     Encodes every word in the possessive view.
    /// </summary>
    /// <param name="words">The words of the text.</param>
    /// <param name="_">The token, which the fake ignores; it is part of the signature of the encoder delegate.</param>
    /// <returns>The tokens of the view with their vectors.</returns>
    public ValueTask<WordVectors> EncodeAsync(IReadOnlyList<string> words, CancellationToken _)
    {
        Texts.Add([.. words]);
        var (tokens, start) = PossessiveView.Expand(words, PossessiveView.IsLexiconPossessive);
        return ValueTask.FromResult(new WordVectors(tokens, start, [.. tokens.Select(vectorOf)]));
    }
}

/// <summary>
///     Tests for the part-of-speech evidence of <see cref="OnnxClassifier"/>: the probability of a slot is the posterior
///     of its part of speech from the head times the share of the slot among the slots of that part of speech, the
///     senses inside a slot are the ones the weights score, and the encoder runs for every word of several parts of
///     speech. The head and the encoder are fakes with designed vectors.
/// </summary>
public class OnnxClassifierPosTests
{
    private const double Tolerance = 1e-6;

    // The head reads the previous word's components 18..27, the word's components 28..37 and the next word's components
    // 8..17 (class c of PartOfSpeech reads component base + c), each with weight 1 and with no bias; the last component of
    // a fixture vector (39) carries the rest of its length and is read by no class
    private const int PreviousBase = 18;
    private const int WordBase = 28;
    private const int NextBase = 8;

    private static readonly int ClassCount = Enum.GetValues<PartOfSpeech>().Length;

    private static PosHead Head()
    {
        var d = Fixtures.Dimension;
        var weights = new float[ClassCount * 3 * d];
        for (var c = 0; c < ClassCount; c++)
        {
            weights[(c * 3 * d) + PreviousBase + c] = 1f;
            weights[(c * 3 * d) + d + WordBase + c] = 1f;
            weights[(c * 3 * d) + (2 * d) + NextBase + c] = 1f;
        }

        var spec = HeadSpec.Create(d) with { Weights = weights, Biases = new float[ClassCount] };
        return new PosHead(PosHeadBlob.Read(HeadBlobWriter.Write(spec)));
    }

    private static (int Component, double Score)[] WordLogits(params (PartOfSpeech Part, double Logit)[] logits) =>
        [.. logits.Select(l => (WordBase + (int)l.Part, l.Logit))];

    private static double[] Vector(params (PartOfSpeech Part, double Logit)[] logits) => Fixtures.Feature(WordLogits(logits));

    // A vector with the given scores and no regard for the scale of the fixtures (for scores no encoder would give)
    private static double[] RawVector(params (PartOfSpeech Part, double Logit)[] logits)
    {
        var vector = new double[Fixtures.Dimension];
        foreach (var (component, score) in WordLogits(logits))
        {
            vector[component] = score;
        }

        return vector;
    }

    private static OnnxClassifier Classifier(WeightedLemmaTable table, FakeViewEncoder view, FakeEncoder? joined = null) =>
        new(table, (joined ?? new FakeEncoder((double[]?)null)).EncodeAsync, Head(), view.EncodeAsync);

    private static async Task<ClassifyResult[]> RunAsync(OnnxClassifier classifier, params string[] words) =>
        await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

    private static double[] Posterior(IReadOnlyList<PartOfSpeech> allowed, params (PartOfSpeech Part, double Logit)[] logits)
    {
        var exp = allowed.Select(p => Math.Exp(logits.Where(l => l.Part == p).Select(l => l.Logit).DefaultIfEmpty(0).Sum())).ToArray();
        var sum = exp.Sum();
        return [.. exp.Select(e => e / sum)];
    }

    private static double PartMass(ClassifyResult result, PartOfSpeech part) =>
        result.Analyses.Where(a => a.Details.PartOfSpeech == part).Sum(a => a.Probability);

    private static PartOfSpeech[] PartsOf(string word) => [.. WordLexicon.Lookup(word).Select(d => d.PartOfSpeech).Distinct()];

    /// <summary>
    ///     When the head gives every part of speech that the lexicon lists a probability that underflows (an excluded class
    ///     with a score 1000 higher), the restriction is computed from the scores, so the probabilities still sum to one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ExtremeLogitOfAnExcludedClass_KeepsAUsableDistribution()
    {
        // Arrange: "record" is a noun and a verb; the head's adjective score is 1000 higher than theirs
        var logits = new[] { (PartOfSpeech.Noun, 1.0), (PartOfSpeech.Verb, 0.0), (PartOfSpeech.Adjective, 1000.0) };
        var view = new FakeViewEncoder(_ => RawVector(logits));
        var classifier = Classifier(new WeightedLemmaTable(), view);

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert: the restriction is the softmax of the scores of the listed parts of speech alone
        var expected = Posterior([PartOfSpeech.Noun, PartOfSpeech.Verb], logits);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), Tolerance);
        Assert.Equal(expected[0], PartMass(result, PartOfSpeech.Noun), Tolerance);
        Assert.Equal(expected[1], PartMass(result, PartOfSpeech.Verb), Tolerance);
        Assert.Equal(expected[0], result.Confidence, Tolerance);
        Assert.Equal(PartOfSpeech.Noun, result.Details!.PartOfSpeech);
    }

    /// <summary>
    ///     A word of several parts of speech whose analyses and head posterior cannot agree (the allowed scores are not
    ///     numbers) is answered from the lexicon, with probabilities that sum to one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UnusableHeadScores_AnswersFromTheLexicon()
    {
        // Arrange: a vector whose component for the noun score is not finite
        var view = new FakeViewEncoder(_ => RawVector((PartOfSpeech.Noun, double.PositiveInfinity), (PartOfSpeech.Verb, 0.0)));
        var classifier = Classifier(new WeightedLemmaTable(), view);

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert: both parts of speech have the same probability and they sum to one
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), Tolerance);
        Assert.Equal(0.5, PartMass(result, PartOfSpeech.Noun), Tolerance);
        Assert.Equal(0.5, PartMass(result, PartOfSpeech.Verb), Tolerance);
        Assert.Equal(0.5, result.Confidence, Tolerance);
    }

    /// <summary>
    ///     A word of several parts of speech whose lemma has no weights gets the restricted posterior of the head as the
    ///     probability of its parts of speech, no scored senses and the part-of-speech level.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UntrainedWordOfSeveralPartsOfSpeech_TakesTheRestrictedPosterior()
    {
        // Arrange: "record" is a noun and a verb; the head would say adjective most of all, which the lexicon excludes
        var logits = new[] { (PartOfSpeech.Noun, 1.0), (PartOfSpeech.Verb, 0.0), (PartOfSpeech.Adjective, 5.0) };
        var view = new FakeViewEncoder(_ => Vector(logits));
        var joined = new FakeEncoder((double[]?)null);
        var classifier = Classifier(new WeightedLemmaTable(), view, joined);

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert
        var expected = Posterior([PartOfSpeech.Noun, PartOfSpeech.Verb], logits);
        Assert.Equal(1, view.Calls);
        Assert.Equal(0, joined.Calls);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(PartOfSpeech.Noun, result.Details!.PartOfSpeech);
        Assert.Equal(expected[0], PartMass(result, PartOfSpeech.Noun), Tolerance);
        Assert.Equal(expected[1], PartMass(result, PartOfSpeech.Verb), Tolerance);
        Assert.Equal(expected[0], result.Confidence, Tolerance);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), Tolerance);
        Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
        Assert.Null(result.SenseId);
        Assert.Null(result.GroupId);
    }

    /// <summary>
    ///     The head decides between the parts of speech of a word whichever way it points: the other part of speech wins
    ///     with the same word when the vector says so.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UntrainedWord_FollowsTheHead()
    {
        // Arrange
        var view = new FakeViewEncoder(_ => Vector((PartOfSpeech.Noun, 0.0), (PartOfSpeech.Verb, 3.0)));
        var classifier = Classifier(new WeightedLemmaTable(), view);

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert
        Assert.Equal(PartOfSpeech.Verb, result.Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Verb, result.Analyses[0].Details.PartOfSpeech);
        Assert.Equal(Math.Exp(3) / (Math.Exp(3) + 1), result.Confidence, Tolerance);
    }

    /// <summary>
    ///     A closed-class slot has probability from the head and no senses, like every other slot of an untrained word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ClosedClassSlots_GetPartOfSpeechMassAndNoSenses()
    {
        // Arrange: "that" is a determiner, a pronoun, a conjunction and an adverb (at least two closed classes)
        var allowed = PartsOf("that");
        Assert.True(allowed.Count(p => SenseFusion.IsClosedClass(p)) >= 2);
        var logits = allowed.Select((p, i) => (p, i * 0.8)).ToArray();
        var view = new FakeViewEncoder(_ => Vector(logits));
        var classifier = Classifier(new WeightedLemmaTable(), view);

        // Act
        var result = (await RunAsync(classifier, "that"))[0];

        // Assert
        var expected = Posterior(allowed, logits);
        for (var k = 0; k < allowed.Length; k++)
        {
            Assert.Equal(expected[k], PartMass(result, allowed[k]), Tolerance);
        }

        Assert.Equal(allowed[^1], result.Details!.PartOfSpeech);
        Assert.All(result.Analyses.Where(a => SenseFusion.IsClosedClass(a.Details.PartOfSpeech)), a => Assert.True(a.Senses.IsEmpty));
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), Tolerance);
    }

    /// <summary>
    ///     The slots of one part of speech share its probability equally when no senses are scored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SeveralSlotsOfOnePartOfSpeech_ShareItsProbabilityEqually()
    {
        // Arrange: find a word with two slots (lemmas) of one part of speech and one more part of speech
        var word = new[] { "found", "left", "saw", "lay", "ground", "wound" }.First(w =>
        {
            var slots = WordLexicon.Lookup(w).Select(d => (d.Lemma, d.PartOfSpeech)).Distinct().ToArray();
            return slots.GroupBy(s => s.PartOfSpeech).Any(g => g.Count() >= 2) && slots.Select(s => s.PartOfSpeech).Distinct().Count() >= 2;
        });
        var allowed = PartsOf(word);
        var logits = allowed.Select((p, i) => (p, i * 0.5)).ToArray();
        var classifier = Classifier(new WeightedLemmaTable(), new FakeViewEncoder(_ => Vector(logits)));

        // Act
        var result = (await RunAsync(classifier, word))[0];

        // Assert: every slot of a part of speech has its posterior divided by the number of its slots
        var expected = Posterior(allowed, logits);
        var slotsOfPart = result.Analyses.Select(a => (a.Details.Lemma, a.Details.PartOfSpeech)).Distinct().GroupBy(s => s.PartOfSpeech).ToDictionary(g => g.Key, g => g.Count());
        for (var k = 0; k < allowed.Length; k++)
        {
            Assert.Equal(expected[k], PartMass(result, allowed[k]), Tolerance);
            foreach (var lemma in result.Analyses.Where(a => a.Details.PartOfSpeech == allowed[k]).Select(a => a.Details.Lemma).Distinct())
            {
                var slot = result.Analyses.Where(a => a.Details.PartOfSpeech == allowed[k] && a.Details.Lemma == lemma).Sum(a => a.Probability);
                Assert.Equal(expected[k] / slotsOfPart[allowed[k]], slot, Tolerance);
            }
        }
    }

    /// <summary>
    ///     When the head and the senses agree on the part of speech, the answer keeps the sense level; its confidence is
    ///     the probability of the part of speech times the probability of the sense within the slot, and the senses of
    ///     the slots are the ones the weights score.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_TrainedLemma_HeadAgrees_KeepsTheSenseLevelWithTheHeadProbability()
    {
        // Arrange: the senses favor the first adverb sense by a wide margin, and the head says adverb
        var adverbs = Fixtures.SenseIds("just").Where(id => WordLexicon.SensesOf("just", PartOfSpeech.Adverb).Any(s => s.SenseId == id)).ToArray();
        var table = Fixtures.Table(Fixtures.Lemma("just", fine: 0.5f));
        var senseScores = new[] { (Fixtures.Column("just", adverbs[0]), 6.0) };
        var logits = new[] { (PartOfSpeech.Adverb, 2.0), (PartOfSpeech.Adjective, 0.0) };
        var vector = Fixtures.Feature([.. senseScores, .. WordLogits(logits)]);
        var classifier = Classifier(table, new FakeViewEncoder(_ => vector), new FakeEncoder(vector));

        // Act
        var result = (await RunAsync(classifier, "just"))[0];

        // Assert
        var senses = Fixtures.Softmax([.. Fixtures.SenseIds("just").Select(id => id == adverbs[0] ? 6.0 : 0.0)]);
        var adverbMass = WordLexicon.SensesOf("just", PartOfSpeech.Adverb).Sum(s => senses[Array.IndexOf(Fixtures.SenseIds("just"), s.SenseId)]);
        var pAdverb = Math.Exp(2) / (Math.Exp(2) + 1);
        Assert.Equal(SenseLevel.Sense, result.Level);
        Assert.Equal(adverbs[0], result.SenseId);
        Assert.Equal(PartOfSpeech.Adverb, result.Details!.PartOfSpeech);
        Assert.Equal(pAdverb, PartMass(result, PartOfSpeech.Adverb), Tolerance);
        Assert.Equal(pAdverb * senses[Array.IndexOf(Fixtures.SenseIds("just"), adverbs[0])] / adverbMass, result.Confidence, Tolerance);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), Tolerance);
        Assert.Equal(1.0, result.Analyses.Single(a => a.Details.PartOfSpeech == PartOfSpeech.Adverb).Senses.Sum(s => s.Probability), Tolerance);
    }

    /// <summary>
    ///     When the head and the senses disagree on the part of speech, the head decides the slot and the answer steps
    ///     down to the part-of-speech level with the head's probability; the senses of each slot are still listed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_TrainedLemma_HeadDisagrees_StepsDownToThePartOfSpeech()
    {
        // Arrange: the senses favor an adverb sense, the head says adjective
        var adverb = WordLexicon.SensesOf("just", PartOfSpeech.Adverb)[0].SenseId;
        var table = Fixtures.Table(Fixtures.Lemma("just", fine: 0.5f));
        var vector = Fixtures.Feature([(Fixtures.Column("just", adverb), 6.0), .. WordLogits((PartOfSpeech.Adverb, 0.0), (PartOfSpeech.Adjective, 2.0))]);
        var classifier = Classifier(table, new FakeViewEncoder(_ => vector), new FakeEncoder(vector));

        // Act
        var result = (await RunAsync(classifier, "just"))[0];

        // Assert
        var pAdjective = Math.Exp(2) / (Math.Exp(2) + 1);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.SenseId);
        Assert.Equal(PartOfSpeech.Adjective, result.Details!.PartOfSpeech);
        Assert.Equal(pAdjective, result.Confidence, Tolerance);
        Assert.Equal(pAdjective, PartMass(result, PartOfSpeech.Adjective), Tolerance);
        Assert.False(result.Analyses.Single(a => a.Details.PartOfSpeech == PartOfSpeech.Adverb).Senses.IsEmpty);
        Assert.False(result.Analyses.Single(a => a.Details.PartOfSpeech == PartOfSpeech.Adjective).Senses.IsEmpty);
    }

    /// <summary>
    ///     The head changes the probability of a slot and nothing about the senses inside it: the ranked senses of every
    ///     analysis equal those of a classifier without a head.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_TrainedLemma_SensesInsideASlotDoNotDependOnTheHead()
    {
        // Arrange
        var table = Fixtures.Table(Fixtures.Lemma("just", fine: 0.5f));
        var vector = Fixtures.Feature([(0, 3.0), (1, 2.0), (2, 1.0), .. WordLogits((PartOfSpeech.Adverb, 1.0), (PartOfSpeech.Adjective, 1.5))]);
        var withHead = Classifier(table, new FakeViewEncoder(_ => vector), new FakeEncoder(vector));
        var withoutHead = new OnnxClassifier(table, new FakeEncoder(vector).EncodeAsync);

        // Act
        var a = (await RunAsync(withHead, "just"))[0];
        var b = (await RunAsync(withoutHead, "just"))[0];

        // Assert
        foreach (var part in new[] { PartOfSpeech.Adverb, PartOfSpeech.Adjective })
        {
            Assert.Equal(b.Analyses.Single(x => x.Details.PartOfSpeech == part).Senses, a.Analyses.Single(x => x.Details.PartOfSpeech == part).Senses);
        }
    }

    /// <summary>
    ///     A text of words that have one part of speech, are unknown or have one sense is answered without running the
    ///     encoder, whatever the head.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_WordsOfOnePartOfSpeech_DoNotRunTheEncoder()
    {
        // Arrange
        var view = new FakeViewEncoder(_ => Vector((PartOfSpeech.Noun, 1.0)));
        var classifier = Classifier(new WeightedLemmaTable(), view);

        // Act
        var results = await RunAsync(classifier, "knowledge", "zzqxjw", ".", "the");

        // Assert
        Assert.Equal(0, view.Calls);
        Assert.Equal("knowledge", results[0].Details!.Lemma);
        Assert.False(results[1].IsKnown);
    }

    /// <summary>
    ///     A word without a vector (no word pieces) keeps the equal split of the lexicon.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_WordWithoutVector_IsAnsweredFromTheLexicon()
    {
        // Arrange
        var classifier = Classifier(new WeightedLemmaTable(), new FakeViewEncoder(_ => null));

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert
        Assert.All(result.Analyses, a => Assert.Equal(0.5, a.Probability));
        Assert.Equal(WordLexicon.Lookup("record")[0], result.Details);
    }

    /// <summary>
    ///     The head reads the vectors of the previous and the next word; across a possessive that is written as one token
    ///     the previous word of the next one is the marker, and the next word of the stem is the marker.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_Neighbors_AreThoseOfThePossessiveView()
    {
        // Arrange: "tool" is a noun and a verb; only the marker's vector pushes its previous-word block toward the verb
        Assert.True(PossessiveView.IsLexiconPossessive("technician's"));
        double[]? VectorOf(string token) => token switch
        {
            "'s" => Fixtures.Feature((PreviousBase + (int)PartOfSpeech.Verb, 4.0)),
            _ => Fixtures.Feature((WordBase + (int)PartOfSpeech.Noun, 0.0)),
        };
        var classifier = Classifier(new WeightedLemmaTable(), new FakeViewEncoder(VectorOf));

        // Act
        var splitMarker = (await RunAsync(classifier, "The", "technician's", "tool"))[2];
        var plain = (await RunAsync(classifier, "The", "technician", "tool"))[2];

        // Assert: after the possessive the marker is the previous token, so the verb is favored; after a plain word it is not
        Assert.Equal(PartOfSpeech.Verb, splitMarker.Details!.PartOfSpeech);
        Assert.Equal(Math.Exp(4) / (Math.Exp(4) + 1), splitMarker.Confidence, Tolerance);
        Assert.Equal(0.5, plain.Confidence, Tolerance);
    }

    /// <summary>
    ///     The next word's vector reaches the head, and the first word of a text has no previous vector.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NextWord_ReachesTheHead()
    {
        // Arrange: the vector of "now" pushes the next block of the word before it toward the verb
        double[]? VectorOf(string token) => token == "now"
            ? Fixtures.Feature((NextBase + (int)PartOfSpeech.Verb, 4.0))
            : Fixtures.Feature((WordBase + (int)PartOfSpeech.Noun, 0.0));
        var classifier = Classifier(new WeightedLemmaTable(), new FakeViewEncoder(VectorOf));

        // Act
        var results = await RunAsync(classifier, "record", "now");

        // Assert
        Assert.Equal(PartOfSpeech.Verb, results[0].Details!.PartOfSpeech);
        Assert.Equal(Math.Exp(4) / (Math.Exp(4) + 1), results[0].Confidence, Tolerance);
    }

    /// <summary>
    ///     A possessive of a trained lemma that is written as one token is scored with its vector pooled over all its
    ///     pieces (a second pass for that target), not with the vector of its stem.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_PendingPossessive_UsesTheJoinedVector()
    {
        // Arrange: "company's" is a possessive noun of a lemma with senses; the stem vector says nothing, the joined one favors a sense
        Assert.True(PossessiveView.IsLexiconPossessive("company's"));
        var table = Fixtures.Table(Fixtures.Lemma("company", fine: 0.5f));
        var senseIds = Fixtures.SenseIds("company");
        var joinedVector = Fixtures.Feature((Fixtures.Column("company", senseIds[1]), 6.0));
        var joined = new FakeEncoder(joinedVector);
        var classifier = Classifier(table, new FakeViewEncoder(_ => Fixtures.Feature()), joined);

        // Act
        var result = (await RunAsync(classifier, "The", "company's", "plan"))[1];

        // Assert
        Assert.Equal([1], Assert.Single(joined.Targets));
        Assert.Equal(senseIds[1], result.SenseId);
    }

    /// <summary>
    ///     A word that the view keeps whole is scored with the view's vector and needs no second pass.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_PendingWordKeptWhole_UsesTheViewVector()
    {
        // Arrange
        var table = Fixtures.Table(Fixtures.Lemma("just", fine: 0.5f));
        var adverb = WordLexicon.SensesOf("just", PartOfSpeech.Adverb)[0].SenseId;
        var vector = Fixtures.Feature([(Fixtures.Column("just", adverb), 6.0), .. WordLogits((PartOfSpeech.Adverb, 2.0), (PartOfSpeech.Adjective, 0.0))]);
        var joined = new FakeEncoder(Fixtures.Feature());
        var classifier = Classifier(table, new FakeViewEncoder(_ => vector), joined);

        // Act
        var result = (await RunAsync(classifier, "It", "was", "just", "fine"))[2];

        // Assert
        Assert.Equal(0, joined.Calls);
        Assert.Equal(adverb, result.SenseId);
    }

    /// <summary>
    ///     Over many words and designed vectors, every result keeps its invariants: the probabilities sum to one, the
    ///     analyses are ordered by their slot, the selected analysis is the first one, a closed-class slot lists no
    ///     senses, an answer at the part-of-speech level has the probability of its slot as confidence, and an answer at
    ///     a finer level lies in the selected slot with a confidence not above the probability of the slot.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ManyWords_KeepTheInvariants()
    {
        // Arrange: words of several parts of speech; vectors with random head logits and sense scores
        var words = new[] { "record", "that", "just", "found", "left", "saw", "back", "well", "light", "close", "like", "will", "can", "round", "present", "address", "second", "right", "act", "fine" };
        var random = new Random(20261030);
        var table = Fixtures.Table(Fixtures.Lemma("just", fine: 0.5f, group: 0.5f));
        double[] RandomVector() => Fixtures.Feature([.. Enumerable.Range(0, 10).Select(c => (WordBase + c, (random.NextDouble() * 4) - 2)), (0, random.NextDouble() * 3), (3, random.NextDouble() * 3)]);
        var vectors = words.ToDictionary(w => w, _ => RandomVector());
        var classifier = Classifier(table, new FakeViewEncoder(w => vectors[w]), new FakeEncoder(w => vectors[w]));

        // Act
        var results = await RunAsync(classifier, words);

        // Assert
        for (var i = 0; i < words.Length; i++)
        {
            var r = results[i];
            var slotOf = r.Analyses.Select(a => (a.Details.Lemma, a.Details.PartOfSpeech)).ToList();
            double SlotProbability((string Lemma, PartOfSpeech Part) slot) => r.Analyses.Where(a => (a.Details.Lemma, a.Details.PartOfSpeech) == slot).Sum(a => a.Probability);
            var slotProbabilities = slotOf.Distinct().Select(SlotProbability).ToArray();
            Assert.Equal(1.0, r.Analyses.Sum(a => a.Probability), Tolerance);
            Assert.Equal(r.Details, r.Analyses[0].Details);
            Assert.True(slotProbabilities.SequenceEqual(slotProbabilities.OrderByDescending(p => p)), words[i]);
            Assert.All(r.Analyses.Where(a => SenseFusion.IsClosedClass(a.Details.PartOfSpeech)), a => Assert.True(a.Senses.IsEmpty, words[i]));
            var selectedSlot = SlotProbability((r.Details!.Lemma, r.Details.PartOfSpeech));
            if (r.Level == SenseLevel.PartOfSpeech)
            {
                Assert.Equal(selectedSlot, r.Confidence, Tolerance);
            }
            else
            {
                Assert.True(r.Confidence <= selectedSlot + Tolerance, words[i]);
                var selected = r.Analyses[0];
                Assert.True(r.SenseId is null || selected.Senses.Any(s => s.SenseId == r.SenseId), words[i]);
            }

            foreach (var analysis in r.Analyses.Where(a => !a.Senses.IsEmpty))
            {
                Assert.Equal(1.0, analysis.Senses.Sum(s => s.Probability), Tolerance);
            }
        }
    }

    /// <summary>
    ///     A classifier needs both the head and its view encoder.
    /// </summary>
    [Fact]
    public void OnnxClassifier_Constructor_HeadWithoutEncoders_Throws()
    {
        // Arrange / Act / Assert
        var table = new WeightedLemmaTable();
        var view = new FakeViewEncoder(_ => null);
        var joined = new FakeEncoder((double[]?)null);
        Assert.Throws<ArgumentNullException>(() => new OnnxClassifier(table, null!, Head(), view.EncodeAsync));
        Assert.Throws<ArgumentNullException>(() => new OnnxClassifier(table, joined.EncodeAsync, null!, view.EncodeAsync));
        Assert.Throws<ArgumentNullException>(() => new OnnxClassifier(table, joined.EncodeAsync, Head(), null!));
    }

    /// <summary>
    ///     A vector whose length is not the hidden size of the head is not used: the word is answered from the lexicon.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_VectorOfAnotherSize_IsAnsweredFromTheLexicon()
    {
        // Arrange
        var classifier = Classifier(new WeightedLemmaTable(), new FakeViewEncoder(_ => new double[Fixtures.Dimension + 1]));

        // Act
        var result = (await RunAsync(classifier, "record"))[0];

        // Assert
        Assert.All(result.Analyses, a => Assert.Equal(0.5, a.Probability));
        Assert.Equal(ImmutableArray<SenseCandidate>.Empty, result.Analyses[0].Senses);
    }
}
