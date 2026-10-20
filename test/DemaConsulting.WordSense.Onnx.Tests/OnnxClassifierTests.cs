namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxClassifier"/>: lexicon answers without a model, and the scoring of the senses of a
///     word with a fake encoder and weights over the real senses of the word image (a sense wins by a chosen margin).
/// </summary>
public class OnnxClassifierTests
{
    private const double Tolerance = 1e-4;

    private static readonly string[] Sentence = ["They", "will", "record", "the", "record", "."];

    private static readonly string[] JustIds = Fixtures.SenseIds("just");

    private static string[] AdverbIds => [.. WordLexicon.SensesOf("just", PartOfSpeech.Adverb).Select(s => s.SenseId)];

    private static string[] AdjectiveIds => [.. WordLexicon.SensesOf("just", PartOfSpeech.Adjective).Select(s => s.SenseId)];

    private static WeightedLemmaTable JustTable(float fine = LemmaWeights.Never, float group = LemmaWeights.Never) =>
        Fixtures.Table(Fixtures.Lemma("just", fine, group));

    private static async Task<ClassifyResult> RunAsync(WeightedLemmaTable table, double[]? vector, string word = "just")
    {
        var classifier = new OnnxClassifier(table, new FakeEncoder(vector).EncodeAsync);
        return (await classifier.ClassifyAsync([word], TestContext.Current.CancellationToken))[0];
    }

    private static double[] JustFeature(params (string Id, double Score)[] scores) => Fixtures.Feature("just", scores);

    private static double[] JustProbabilities(params (string Id, double Score)[] scores)
    {
        var values = new double[JustIds.Length];
        foreach (var (id, score) in scores)
        {
            values[Array.IndexOf(JustIds, id)] = score;
        }

        return Fixtures.Softmax(values);
    }

    private static double P(double[] probabilities, string id) => probabilities[Array.IndexOf(JustIds, id)];

    private static double Mass(double[] probabilities, PartOfSpeech partOfSpeech) =>
        WordLexicon.SensesOf("just", partOfSpeech).Sum(s => P(probabilities, s.SenseId));

    private static AnalysisCandidate Analysis(ClassifyResult result, PartOfSpeech partOfSpeech) =>
        result.Analyses.Single(a => a.Details.PartOfSpeech == partOfSpeech);

    private static (string[] Members, string GroupId) AdverbGroup()
    {
        var group = WordLexicon.SensesOf("just", PartOfSpeech.Adverb)
            .Where(s => s.GroupId is not null)
            .GroupBy(s => s.GroupId)
            .First(g => g.Count() >= 2);
        return ([.. group.Select(s => s.SenseId)], group.Key!);
    }

    private static double GroupMargin(double[] probabilities, string[] members) =>
        members.Sum(m => P(probabilities, m)) - JustIds.Where(id => !members.Contains(id)).Max(id => P(probabilities, id));

    private static float FineMarginOf(double[] vector)
    {
        var feature = FeatureExtractor.Center(vector, new float[Fixtures.Dimension])!;
        var scores = new float[JustIds.Length];
        Fixtures.Lemma("just").Weights.Scores(feature, scores);
        return LemmaWeights.Rank(scores).Margin;
    }

    /// <summary>
    ///     An empty list gives an empty result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_EmptyList_ReturnsEmpty()
    {
        // Arrange
        var classifier = new OnnxClassifier();

        // Act / Assert
        Assert.Empty(await classifier.ClassifyAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NullWords_Throws()
    {
        // Arrange
        var classifier = new OnnxClassifier();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null element is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NullElement_Throws()
    {
        // Arrange
        var classifier = new OnnxClassifier();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => classifier.ClassifyAsync(["record", null!], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     An already cancelled token cancels the call, even for an empty list.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_Cancelled_Throws()
    {
        // Arrange: a token that is already cancelled
        var classifier = new OnnxClassifier();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync(Sentence, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync([], cancelled.Token));
    }

    /// <summary>
    ///     A null table is rejected.
    /// </summary>
    [Fact]
    public void OnnxClassifier_Constructor_NullTable_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => new OnnxClassifier(null!, null));
    }

    /// <summary>
    ///     An unknown word gets the unknown result, and the encoder is not asked.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UnknownWord_IsAnsweredAsUnknown()
    {
        // Arrange
        var encoder = new FakeEncoder(JustFeature());
        var classifier = new OnnxClassifier(JustTable(), encoder.EncodeAsync);

        // Act
        var result = await classifier.ClassifyAsync([.. Sentence, "zzqxjw"], TestContext.Current.CancellationToken);

        // Assert: the period and the made-up word are unknown, the rest is known
        Assert.Equal(ClassifyResult.Unknown("zzqxjw"), result[^1]);
        Assert.Equal(ClassifyResult.Unknown("."), result[5]);
        Assert.All(result.Take(5), r => Assert.True(r.IsKnown));
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A word is looked up without regard to its case and keeps its spelling in the result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_CapitalizedWord_ResolvesLikeLowerCase()
    {
        // Arrange: a lexicon word and a word that is scored by the encoder
        var vector = JustFeature((AdverbIds[0], 3));
        var classifier = new OnnxClassifier(JustTable(1f), new FakeEncoder(vector).EncodeAsync);

        // Act
        var results = await classifier.ClassifyAsync(["the", "The", "just", "Just"], TestContext.Current.CancellationToken);

        // Assert: same answers, each with its own spelling
        Assert.Equal("The", results[1].Word);
        Assert.Equal(results[0].Analyses, results[1].Analyses);
        Assert.Equal((results[0].Level, results[0].SenseId, results[0].Confidence), (results[1].Level, results[1].SenseId, results[1].Confidence));
        Assert.Equal("Just", results[3].Word);
        Assert.Equal(results[2].Analyses, results[3].Analyses);
        Assert.Equal((results[2].Level, results[2].SenseId, results[2].Confidence), (results[3].Level, results[3].SenseId, results[3].Confidence));
    }

    /// <summary>
    ///     A word with one analysis and one sense is answered at the sense level with probability 1.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SingleAnalysisSingleSense_AnswersTheSense()
    {
        // Arrange
        var classifier = new OnnxClassifier();
        var sense = Assert.Single(WordLexicon.SensesOf("the", PartOfSpeech.Determiner));

        // Act
        var result = (await classifier.ClassifyAsync(["the"], TestContext.Current.CancellationToken))[0];

        // Assert
        var analysis = Assert.Single(result.Analyses);
        Assert.Equal(1.0, analysis.Probability);
        Assert.Equal([new SenseCandidate(sense.SenseId, 1.0)], analysis.Senses);
        Assert.Equal(SenseLevel.Sense, result.Level);
        Assert.Equal(sense.SenseId, result.SenseId);
        Assert.Equal(sense.GroupId, result.GroupId);
        Assert.Equal(1.0, result.Confidence);
        Assert.Equal(analysis.Details, result.Details);
    }

    /// <summary>
    ///     A word with one analysis and one sense is answered without running the encoder, and it lists its single
    ///     sense unscored with probability 1 instead of ranked candidates.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SingleSenseWord_DoesNotEncode()
    {
        // Arrange
        var encoder = new FakeEncoder(JustFeature());
        var classifier = new OnnxClassifier(JustTable(), encoder.EncodeAsync);

        // Act
        var result = (await classifier.ClassifyAsync(["the"], TestContext.Current.CancellationToken))[0];

        // Assert
        Assert.Equal(0, encoder.Calls);
        Assert.Equal(SenseLevel.Sense, result.Level);
    }

    /// <summary>
    ///     A single-sense word has nothing to rank: its only sense is listed with probability 1, never scored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SingleSenseWord_ListsNoCandidates()
    {
        // Arrange: the table does not matter for a word that has one sense
        var encoder = new FakeEncoder(JustFeature());
        var classifier = new OnnxClassifier(JustTable(), encoder.EncodeAsync);
        var sense = Assert.Single(WordLexicon.SensesOf("the", PartOfSpeech.Determiner));

        // Act
        var result = (await classifier.ClassifyAsync(["the"], TestContext.Current.CancellationToken))[0];

        // Assert: the one sense is not a ranked candidate list; the encoder never scored anything
        var senses = Assert.Single(result.Analyses).Senses;
        Assert.Equal([new SenseCandidate(sense.SenseId, 1.0)], senses);
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A list in which no word is ambiguous (single-sense, unknown and untrained words) never runs the encoder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_NoAmbiguousWords_DoesNotEncode()
    {
        // Arrange: "record" is ambiguous in the lexicon but the table has no weights for it
        var encoder = new FakeEncoder(JustFeature());
        var classifier = new OnnxClassifier(JustTable(), encoder.EncodeAsync);

        // Act
        var result = await classifier.ClassifyAsync(["The", "zzqxjw", ".", "record"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, encoder.Calls);
        Assert.Equal(4, result.Length);
    }

    /// <summary>
    ///     A word whose lemma has weights and several senses runs the encoder once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_AmbiguousWords_UseTheEncoder()
    {
        // Arrange
        var encoder = new FakeEncoder(JustFeature((AdverbIds[0], 3)));
        var classifier = new OnnxClassifier(JustTable(1f), encoder.EncodeAsync);

        // Act
        var result = await classifier.ClassifyAsync(["It", "is", "just", "right"], TestContext.Current.CancellationToken);

        // Assert: one call with the whole text, and the answer comes from the designed vector
        Assert.Equal(1, encoder.Calls);
        Assert.Equal(["It", "is", "just", "right"], encoder.Texts[0]);
        Assert.Equal(AdverbIds[0], result[2].SenseId);
    }

    /// <summary>
    ///     The encoder is asked only for the indexes of the ambiguous words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_OnlyAmbiguousWordsAreEncoded()
    {
        // Arrange
        string[] words = ["the", "just", "zzqxjw", "just", "."];
        var encoder = new FakeEncoder(JustFeature((AdverbIds[0], 3)));
        var classifier = new OnnxClassifier(JustTable(1f), encoder.EncodeAsync);

        // Act
        var result = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        // Assert: the delegate saw the whole text but only the targets 1 and 3
        Assert.Equal(1, encoder.Calls);
        Assert.Equal(words, encoder.Texts[0]);
        Assert.Equal([1, 3], encoder.Targets[0]);
        Assert.Equal(AdverbIds[0], result[1].SenseId);
        Assert.Equal(AdverbIds[0], result[3].SenseId);
        Assert.Equal(SenseLevel.Sense, result[0].Level);
        Assert.Equal(SenseLevel.None, result[2].Level);
    }

    /// <summary>
    ///     The senses of every analysis of a word are scored, each part of speech with its own senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_WordWithSeveralAnalyses_RanksTheSensesOfEveryAnalysis()
    {
        // Arrange: "just" is an adjective and an adverb, and the encoder favors an adjective sense a little
        var vector = JustFeature((AdjectiveIds[1], 1.5));

        // Act
        var result = await RunAsync(JustTable(), vector);

        // Assert: both analyses list exactly the senses of their own part of speech
        Assert.Equal(2, result.Analyses.Length);
        Assert.Equal(new HashSet<string>(AdjectiveIds), Analysis(result, PartOfSpeech.Adjective).Senses.Select(s => s.SenseId).ToHashSet());
        Assert.Equal(new HashSet<string>(AdverbIds), Analysis(result, PartOfSpeech.Adverb).Senses.Select(s => s.SenseId).ToHashSet());
        Assert.Equal(AdjectiveIds[1], Analysis(result, PartOfSpeech.Adjective).Senses[0].SenseId);
    }

    /// <summary>
    ///     A preposition use of a word that is also a verb and a noun is reachable: its sense can win and its analysis
    ///     is then the selected one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_PrepositionUseOfLike_IsReachable()
    {
        // Arrange: weights over all senses of "like"; the preposition sense scores highest
        var preposition = Assert.Single(WordLexicon.SensesOf("like", PartOfSpeech.Preposition));
        var table = Fixtures.Table(Fixtures.Lemma("like", fine: 1f));
        var vector = Fixtures.Feature("like", (preposition.SenseId, 6));

        // Act
        var result = await RunAsync(table, vector, "like");

        // Assert: the preposition analysis is selected and answered at the sense level
        Assert.Equal(PartOfSpeech.Preposition, result.Details!.PartOfSpeech);
        Assert.Equal(SenseLevel.Sense, result.Level);
        Assert.Equal(preposition.SenseId, result.SenseId);
        var mass = Math.Exp(6) / (Math.Exp(6) + Fixtures.SenseIds("like").Length - 1);
        Assert.Equal(mass, Analysis(result, PartOfSpeech.Preposition).Probability, Tolerance);
        Assert.Equal(mass, result.Confidence, Tolerance);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
    }

    /// <summary>
    ///     An inflected form ranks the senses of its lemma, only those of its own parts of speech and no sense of
    ///     another lemma.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_InflectedForm_RanksTheSensesOfItsLemma()
    {
        // Arrange: "likes" is a plural noun and a third-person verb of "like"; a verb sense wins
        var verb = WordLexicon.SensesOf("like", PartOfSpeech.Verb);
        var nouns = WordLexicon.SensesOf("like", PartOfSpeech.Noun);
        var table = Fixtures.Table(Fixtures.Lemma("like", fine: 1f), Fixtures.Lemma("just", fine: 1f));
        var vector = Fixtures.Feature("like", (verb[0].SenseId, 3));

        // Act
        var result = await RunAsync(table, vector, "likes");

        // Assert: noun and verb senses of "like" only
        Assert.Equal(2, result.Analyses.Length);
        var senses = result.Analyses.SelectMany(a => a.Senses).Select(s => WordLexicon.TryGetSense(s.SenseId, out var info) ? info : null).ToList();
        Assert.Equal(verb.Length + nouns.Length, senses.Count);
        Assert.All(senses, s => Assert.Equal("like", s!.Lemma));
        Assert.All(Analysis(result, PartOfSpeech.Verb).Senses, s => Assert.Contains(verb, v => v.SenseId == s.SenseId));
        Assert.All(Analysis(result, PartOfSpeech.Noun).Senses, s => Assert.Contains(nouns, n => n.SenseId == s.SenseId));
        Assert.Equal(PartOfSpeech.Verb, result.Details!.PartOfSpeech);
        Assert.Equal(verb[0].SenseId, result.SenseId);
    }

    /// <summary>
    ///     A trained lemma lists every analysis with its probability and, inside each, its senses ranked by their
    ///     probability given the analysis, summing to one, in inventory order on ties, never truncated and whatever
    ///     level is answered.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_TrainedLemma_ListsRankedCandidatesSummingToOne()
    {
        // Arrange: two adverb senses and one adjective sense stand out; every other score is equal (a tie)
        var vector = JustFeature((AdverbIds[3], 2), (AdverbIds[1], 1), (AdjectiveIds[2], 1.5));

        // Act: once with no threshold met, once with the fine threshold met
        var below = await RunAsync(JustTable(), vector);
        var met = await RunAsync(JustTable(0.1f), vector);

        // Assert
        Assert.Equal(SenseLevel.PartOfSpeech, below.Level);
        Assert.Equal(SenseLevel.Sense, met.Level);
        foreach (var result in new[] { below, met })
        {
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
            Assert.Equal(result.Analyses.OrderByDescending(a => a.Probability), result.Analyses);
            foreach (var analysis in result.Analyses)
            {
                var expected = WordLexicon.SensesOf("just", analysis.Details.PartOfSpeech).Select(s => s.SenseId).ToHashSet();
                Assert.Equal(expected, analysis.Senses.Select(s => s.SenseId).ToHashSet());
                Assert.Equal(analysis.Senses.Length, analysis.Senses.Select(s => s.SenseId).Distinct().Count());
                Assert.Equal(1.0, analysis.Senses.Sum(s => s.Probability), 9);
                for (var i = 1; i < analysis.Senses.Length; i++)
                {
                    var (before, after) = (analysis.Senses[i - 1], analysis.Senses[i]);
                    Assert.True(
                        before.Probability > after.Probability
                        || (Math.Abs(before.Probability - after.Probability) < 1e-12 && Array.IndexOf(JustIds, before.SenseId) < Array.IndexOf(JustIds, after.SenseId)),
                        $"{before.SenseId} is ranked before {after.SenseId}.");
                }
            }
        }

        // The adverb that scores highest leads its analysis, then the runner-up, then the tie in inventory order
        var adverbs = Analysis(below, PartOfSpeech.Adverb).Senses.Select(s => s.SenseId).ToArray();
        Assert.Equal([AdverbIds[3], AdverbIds[1]], adverbs[..2]);
        Assert.Equal(AdverbIds.Where(id => id != AdverbIds[3] && id != AdverbIds[1]), adverbs[2..]);
    }

    /// <summary>
    ///     A lemma without weights lists no scored senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_ListsNoCandidates()
    {
        // Arrange: "record" is not in the table
        var encoder = new FakeEncoder(JustFeature());
        var classifier = new OnnxClassifier(JustTable(), encoder.EncodeAsync);

        // Act
        var result = (await classifier.ClassifyAsync(["record"], TestContext.Current.CancellationToken))[0];

        // Assert
        Assert.NotEmpty(result.Analyses);
        Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A larger margin of the best sense gives a higher confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LargerMargin_GivesHigherConfidence()
    {
        // Arrange: the fine threshold is met by every margin
        var table = JustTable(0.5f);

        // Act
        var confidences = new List<double>();
        foreach (var margin in new[] { 1.0, 2.0, 4.0, 6.0 })
        {
            var result = await RunAsync(table, JustFeature((AdverbIds[0], margin)));
            Assert.Equal(SenseLevel.Sense, result.Level);
            confidences.Add(result.Confidence);
        }

        // Assert
        Assert.Equal(confidences.Order(), confidences);
        Assert.Equal(confidences.Count, confidences.Distinct().Count());
    }

    /// <summary>
    ///     The confidence is a probability: within zero and one at every level.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ConfidenceIsWithinZeroAndOne()
    {
        // Arrange: tables that answer every level (sense, group, part of speech) and margins from none to huge
        var (members, _) = AdverbGroup();
        var tables = new[] { JustTable(), JustTable(0.5f), JustTable(5f, 0.1f), JustTable(5f, 0.99f), JustTable(0f, 0f) };
        var vectors = new[]
        {
            JustFeature(),
            JustFeature((AdverbIds[0], 0.2)),
            JustFeature((members[0], 3), (members[1], 2.6)),
            JustFeature((AdjectiveIds[0], 6)),
            JustFeature((AdverbIds[0], 9)),
        };

        // Act / Assert
        foreach (var table in tables)
        {
            foreach (var vector in vectors)
            {
                var result = await RunAsync(table, vector);
                Assert.InRange(result.Confidence, 0.0, 1.0);
                Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
            }
        }
    }

    /// <summary>
    ///     A margin above the fine threshold answers the best sense at the sense level, with the group of the sense
    ///     and the probability of the sense as confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_MarginAboveFineThreshold_AnswersSense()
    {
        // Arrange: a group member leads by a margin of 3 and the fine threshold is 1
        var (members, groupId) = AdverbGroup();
        var vector = JustFeature((members[0], 3));
        var probabilities = JustProbabilities((members[0], 3));

        // Act
        var result = await RunAsync(JustTable(1f, 0.5f), vector);

        // Assert
        Assert.Equal(SenseLevel.Sense, result.Level);
        Assert.Equal(members[0], result.SenseId);
        Assert.Equal(groupId, result.GroupId);
        Assert.Equal(PartOfSpeech.Adverb, result.Details!.PartOfSpeech);
        Assert.Equal(P(probabilities, members[0]), result.Confidence, Tolerance);
        Assert.Equal(Mass(probabilities, PartOfSpeech.Adverb), result.Analyses[0].Probability, Tolerance);
    }

    /// <summary>
    ///     A margin exactly at the fine threshold answers the sense; the smallest step above the margin does not.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_MarginAtFineThreshold_AnswersSense()
    {
        // Arrange: the margin of the best sense, computed with the same arithmetic as the classifier
        var vector = JustFeature((AdverbIds[0], 2));
        var margin = FineMarginOf(vector);

        // Act
        var at = await RunAsync(JustTable(margin), vector);
        var above = await RunAsync(JustTable(MathF.BitIncrement(margin)), vector);

        // Assert
        Assert.Equal(SenseLevel.Sense, at.Level);
        Assert.Equal(AdverbIds[0], at.SenseId);
        Assert.Equal(SenseLevel.PartOfSpeech, above.Level);
        Assert.Null(above.SenseId);
    }

    /// <summary>
    ///     When the fine threshold is not met but the group of the best sense reaches the group threshold, the group is
    ///     answered with its id only.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FineBelowAndGroupAboveThreshold_AnswersGroup()
    {
        // Arrange: two senses of one group lead closely, so the fine margin is small and the group margin is large
        var (members, groupId) = AdverbGroup();
        var probabilities = JustProbabilities((members[0], 3), (members[1], 2.6));
        Assert.True(GroupMargin(probabilities, members) > 0.5);

        // Act
        var result = await RunAsync(JustTable(1f, 0.5f), JustFeature((members[0], 3), (members[1], 2.6)));

        // Assert: the group, not a sense; the probability of the group is the confidence and the members stay listed
        Assert.Equal(SenseLevel.Group, result.Level);
        Assert.Equal(groupId, result.GroupId);
        Assert.Null(result.SenseId);
        Assert.Equal(members.Sum(m => P(probabilities, m)), result.Confidence, Tolerance);
        var listed = result.Analyses[0].Senses.Select(s => s.SenseId).ToArray();
        Assert.Equal(members, listed[..members.Length].OrderBy(id => Array.IndexOf(JustIds, id)));
    }

    /// <summary>
    ///     When neither threshold is met, the answer is the part of speech of the selected analysis with the
    ///     probability of that analysis as confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_BothBelowThreshold_AnswersPartOfSpeech()
    {
        // Arrange: the same scores as the group test, but the group threshold is out of reach
        var (members, _) = AdverbGroup();
        var probabilities = JustProbabilities((members[0], 3), (members[1], 2.6));
        Assert.True(GroupMargin(probabilities, members) < 0.95);

        // Act
        var result = await RunAsync(JustTable(1f, 0.95f), JustFeature((members[0], 3), (members[1], 2.6)));

        // Assert
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(PartOfSpeech.Adverb, result.Details!.PartOfSpeech);
        Assert.Null(result.SenseId);
        Assert.Null(result.GroupId);
        Assert.Equal(Mass(probabilities, PartOfSpeech.Adverb), result.Confidence, Tolerance);
        Assert.Equal(result.Analyses[0].Probability, result.Confidence, 9);
    }

    /// <summary>
    ///     A low margin still shows the best sense and its group, ranked first in the senses of the selected analysis.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LowMargin_KeepsTheBestSenseAndGroupVisibleInTheRankedSenses()
    {
        // Arrange: the answer steps up to the part of speech
        var (members, groupId) = AdverbGroup();
        var vector = JustFeature((members[0], 3), (members[1], 2.6));

        // Act
        var result = await RunAsync(JustTable(1f, 0.95f), vector);

        // Assert: the best sense leads the ranked senses, the next one is its group mate
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        var ranked = Analysis(result, PartOfSpeech.Adverb).Senses;
        Assert.Equal(members[0], ranked[0].SenseId);
        Assert.Equal(members[1], ranked[1].SenseId);
        Assert.True(ranked[0].Probability > ranked[1].Probability);
        Assert.True(WordLexicon.TryGetSense(ranked[0].SenseId, out var best));
        Assert.Equal(groupId, best.GroupId);
        Assert.True(WordLexicon.TryGetSense(ranked[1].SenseId, out var mate));
        Assert.Equal(groupId, mate.GroupId);
    }

    /// <summary>
    ///     A lemma without groups steps from the sense straight to the part of speech, even with a group threshold that
    ///     any margin passes.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutGroups_StepsFromSenseToPartOfSpeech()
    {
        // Arrange: "pleasant" is an adjective with two senses and no group
        var ids = Fixtures.SenseIds("pleasant");
        Assert.All(Fixtures.Senses("pleasant"), s => Assert.Null(s.GroupId));
        var table = Fixtures.Table(Fixtures.Lemma("pleasant", fine: 1f, group: 0f));
        var vector = Fixtures.Feature("pleasant", (ids[0], 0.5));

        // Act
        var result = await RunAsync(table, vector, "pleasant");

        // Assert: never a group, the confidence is the probability of the one analysis
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.GroupId);
        Assert.Null(result.SenseId);
        Assert.Equal(1.0, result.Confidence, 9);
        Assert.Equal(2, Assert.Single(result.Analyses).Senses.Length);
    }

    /// <summary>
    ///     A best sense that is in no group does not step to a group, even when the lemma has groups.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_BestSenseInNoGroup_StaysAtPartOfSpeech()
    {
        // Arrange: the leading adverb sense has no group and the fine margin is below the threshold
        Assert.Null(WordLexicon.SensesOf("just", PartOfSpeech.Adverb)[0].GroupId);

        // Act
        var result = await RunAsync(JustTable(1f, 0f), JustFeature((AdverbIds[0], 0.5)));

        // Assert
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.GroupId);
        Assert.Equal(PartOfSpeech.Adverb, result.Details!.PartOfSpeech);
    }

    /// <summary>
    ///     A best sense that lies in another analysis than the most probable one is not answered.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_BestSenseOutsideTheSelectedAnalysis_AnswersPartOfSpeech()
    {
        // Arrange: an adjective sense leads by 1, but the eight adverb senses together are more probable
        var probabilities = JustProbabilities((AdjectiveIds[0], 1));
        Assert.True(Mass(probabilities, PartOfSpeech.Adverb) > Mass(probabilities, PartOfSpeech.Adjective));

        // Act
        var result = await RunAsync(JustTable(0.5f, 0f), JustFeature((AdjectiveIds[0], 1)));

        // Assert: the adverb analysis is selected at the part of speech level, and the adjective sense stays ranked
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(PartOfSpeech.Adverb, result.Details!.PartOfSpeech);
        Assert.Equal(Mass(probabilities, PartOfSpeech.Adverb), result.Confidence, Tolerance);
        Assert.Equal(AdjectiveIds[0], Analysis(result, PartOfSpeech.Adjective).Senses[0].SenseId);
    }

    /// <summary>
    ///     A word with several analyses and several senses in each (no weights) is answered with every analysis, equally
    ///     likely, at the part of speech level, and no analysis lists scored senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_AnswersAnalysesWithoutScoredSenses()
    {
        // Arrange: "record" has a noun and a verb analysis, each with several senses
        var classifier = new OnnxClassifier();
        var lexicon = WordLexicon.Lookup("record");

        // Act
        var result = await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert: both "record" words have the analyses of the lexicon in its order, uniform, with no scored sense
        foreach (var index in new[] { 2, 4 })
        {
            var record = result[index];
            Assert.True(record.IsKnown);
            Assert.Equal(SenseLevel.PartOfSpeech, record.Level);
            Assert.Equal(lexicon[0], record.Details);
            Assert.Equal(lexicon, record.Analyses.Select(a => a.Details));
            Assert.All(record.Analyses, a => Assert.Equal(1.0 / lexicon.Length, a.Probability, 12));
            Assert.Equal(1.0, record.Analyses.Sum(a => a.Probability), 12);
            Assert.All(record.Analyses, a => Assert.True(a.Senses.IsEmpty));
            Assert.Equal(1.0 / lexicon.Length, record.Confidence, 12);
            Assert.Null(record.GroupId);
            Assert.Null(record.SenseId);
        }
    }

    /// <summary>
    ///     A word without weights is never answered with a fine sense, whatever the encoder and the table are.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_NeverAnswersAFineSense()
    {
        // Arrange: a table that trains "just" only, asked about ambiguous words of other lemmas
        var encoder = new FakeEncoder(JustFeature((AdverbIds[0], 9)));
        var classifier = new OnnxClassifier(JustTable(0f, 0f), encoder.EncodeAsync);

        // Act
        var results = await classifier.ClassifyAsync(["record", "like", "back", "pleasant"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, encoder.Calls);
        Assert.All(results, r => Assert.Equal(SenseLevel.PartOfSpeech, r.Level));
        Assert.All(results, r => Assert.Null(r.SenseId));
        Assert.All(results, r => Assert.Null(r.GroupId));
    }

    /// <summary>
    ///     A word with one analysis and several senses is answered at the part of speech level with probability 1.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SingleAnalysisSeveralSenses_AnswersPartOfSpeech()
    {
        // Arrange: "pleasant" is an adjective only, with two senses
        var classifier = new OnnxClassifier();
        var word = "pleasant";
        var analysisOfWord = Assert.Single(WordLexicon.Lookup(word));
        Assert.True(WordLexicon.SensesOf(analysisOfWord.Lemma, analysisOfWord.PartOfSpeech).Length > 1);

        // Act
        var result = (await classifier.ClassifyAsync([word], TestContext.Current.CancellationToken))[0];

        // Assert
        var analysis = Assert.Single(result.Analyses);
        Assert.Equal(1.0, analysis.Probability);
        Assert.True(analysis.Senses.IsEmpty);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(1.0, result.Confidence);
        Assert.Null(result.SenseId);
    }

    /// <summary>
    ///     When the first analysis has one sense, the answer still stays at the part of speech level with the uniform
    ///     probability, and no analysis lists scored senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SeveralAnalysesFirstWithOneSense_StaysAtPartOfSpeech()
    {
        // Arrange: "that" has several analyses; the first is the determiner with one sense, but nothing is scored
        var classifier = new OnnxClassifier();
        var lexicon = WordLexicon.Lookup("that");
        Assert.Single(WordLexicon.SensesOf(lexicon[0].Lemma, lexicon[0].PartOfSpeech));

        // Act
        var result = (await classifier.ClassifyAsync(["that"], TestContext.Current.CancellationToken))[0];

        // Assert: uniform analyses, the selected one is the first, no sense is named or scored
        Assert.Equal(lexicon, result.Analyses.Select(a => a.Details));
        Assert.All(result.Analyses, a => Assert.Equal(1.0 / lexicon.Length, a.Probability, 12));
        Assert.Equal(lexicon[0], result.Details);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.SenseId);
        Assert.Equal(1.0 / lexicon.Length, result.Confidence, 12);
        Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
    }

    /// <summary>
    ///     When the first analysis has several senses but a later one has exactly one, the level stays the part of speech
    ///     and no analysis lists scored senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_SeveralAnalysesLaterWithOneSense_StaysAtPartOfSpeech()
    {
        // Arrange: "no" has an adverb analysis with several senses first and a noun analysis with one sense
        var classifier = new OnnxClassifier();
        var lexicon = WordLexicon.Lookup("no");
        var noun = lexicon.Single(d => d.PartOfSpeech == PartOfSpeech.Noun);
        Assert.Single(WordLexicon.SensesOf(noun.Lemma, noun.PartOfSpeech));
        Assert.True(WordLexicon.SensesOf(lexicon[0].Lemma, lexicon[0].PartOfSpeech).Length > 1);

        // Act
        var result = (await classifier.ClassifyAsync(["no"], TestContext.Current.CancellationToken))[0];

        // Assert
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Null(result.SenseId);
        Assert.Equal(lexicon[0], result.Details);
        Assert.Equal(1.0 / lexicon.Length, result.Confidence, 12);
        Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
    }

    /// <summary>
    ///     Weights of another dimension than the vector, a vector of the wrong length and a target without a vector all
    ///     give the lexicon-only answer at the part of speech level, never unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_DimensionMismatch_FallsBackToPartOfSpeech()
    {
        // Arrange: a vector of 7 values and none; the table dimension is 40
        var lexicon = WordLexicon.Lookup("just");

        // Act
        var shortVector = await RunAsync(JustTable(1f, 0f), new double[7]);
        var noVector = await RunAsync(JustTable(1f, 0f), null);

        // Assert: the analyses of the lexicon, uniform, no scored sense, at the part of speech level
        foreach (var result in new[] { shortVector, noVector })
        {
            Assert.True(result.IsKnown);
            Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
            Assert.Equal(lexicon, result.Analyses.Select(a => a.Details));
            Assert.All(result.Analyses, a => Assert.Equal(1.0 / lexicon.Length, a.Probability, 12));
            Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
            Assert.Equal(lexicon[0], result.Details);
            Assert.Equal(1.0 / lexicon.Length, result.Confidence, 12);
            Assert.Null(result.SenseId);
            Assert.Null(result.GroupId);
        }
    }

    /// <summary>
    ///     A form of two lemmas takes the lemma that its chooser prefers from the context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FormOfTwoLemmas_ChoosesTheLemmaFromContext()
    {
        // Arrange: "left" is a past participle of "leave" and an adjective, adverb and noun of "left"
        var table = LeftTable(0.5f);
        var verb = WordLexicon.SensesOf("leave", PartOfSpeech.Verb);
        var adjective = WordLexicon.SensesOf("left", PartOfSpeech.Adjective)[0];
        var leftSenses = Fixtures.SenseIds("left");
        var leaveVector = Fixtures.Feature((Fixtures.Column("leave", verb[0].SenseId), 3), (Fixtures.Label(0), 4));
        var leftVector = Fixtures.Feature((Fixtures.Column("left", adjective.SenseId), 3), (Fixtures.Label(1), 4));

        // Act
        var asLeave = await RunAsync(table, leaveVector, "left");
        var asLeft = await RunAsync(table, leftVector, "left");

        // Assert: the chooser decides the lemma, then the senses of that lemma decide the sense
        var chooserLeave = Math.Exp(4) / (Math.Exp(4) + 1);
        Assert.Equal("leave", asLeave.Details!.Lemma);
        Assert.Equal(SenseLevel.Sense, asLeave.Level);
        Assert.Equal(verb[0].SenseId, asLeave.SenseId);
        Assert.Equal(chooserLeave, asLeave.Analyses[0].Probability, Tolerance);
        Assert.Equal(chooserLeave * Math.Exp(3) / (Math.Exp(3) + verb.Length - 1), asLeave.Confidence, Tolerance);

        Assert.Equal("left", asLeft.Details!.Lemma);
        Assert.Equal(PartOfSpeech.Adjective, asLeft.Details.PartOfSpeech);
        Assert.Equal(adjective.SenseId, asLeft.SenseId);
        var chooserLeft = Math.Exp(4) / (Math.Exp(4) + 1);
        Assert.Equal(chooserLeft * Math.Exp(3) / (Math.Exp(3) + leftSenses.Length - 1), asLeft.Confidence, Tolerance);

        // Every analysis is listed, with probabilities that sum to one; the verb analysis lists the senses of "leave"
        foreach (var result in new[] { asLeave, asLeft })
        {
            Assert.Equal(4, result.Analyses.Length);
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
        }

        Assert.All(AnalysisOfLemma(asLeave, "leave").Senses, s => Assert.StartsWith("leave#", s.SenseId, StringComparison.Ordinal));
        Assert.All(asLeft.Analyses.Where(a => a.Details.Lemma == "left").SelectMany(a => a.Senses), s => Assert.StartsWith("left#", s.SenseId, StringComparison.Ordinal));
    }

    /// <summary>
    ///     The tense of a verb form is chosen from the context by the chooser of the form.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_ReadPastVersusPresent_ChoosesTheTenseFeatures()
    {
        // Arrange: "read" is a verb in the past and the present, a noun and an adjective; the labels name the tenses
        var lexicon = WordLexicon.Lookup("read");
        Assert.Contains(lexicon, d => d.Features == "Tense=Past+VerbForm=Part");
        Assert.Contains(lexicon, d => d.Features == "Tense=Pres");
        var table = Fixtures.Table(Fixtures.Lemma(
            "read",
            fine: 1f,
            choosers: [Fixtures.Chooser("read", ["read", "read/Tense=Past+VerbForm=Part", "read/Tense=Pres"], 0.5f)]));
        var verb = WordLexicon.SensesOf("read", PartOfSpeech.Verb);
        var senseScore = (Fixtures.Column("read", verb[0].SenseId), 3.0);

        // Act
        var past = await RunAsync(table, Fixtures.Feature(senseScore, (Fixtures.Label(1), 4)), "read");
        var present = await RunAsync(table, Fixtures.Feature(senseScore, (Fixtures.Label(2), 4)), "read");

        // Assert: the preferred tense is selected and answered with the sense, with the probability of its label
        Assert.Equal("Tense=Past+VerbForm=Part", past.Details!.Features);
        Assert.Equal("Tense=Pres", present.Details!.Features);
        var label = Math.Exp(4) / (Math.Exp(4) + 2);
        foreach (var result in new[] { past, present })
        {
            Assert.Equal(SenseLevel.Sense, result.Level);
            Assert.Equal(verb[0].SenseId, result.SenseId);
            Assert.Equal(label * Math.Exp(3) / (Math.Exp(3) + verb.Length - 1), result.Confidence, Tolerance);
            Assert.Equal(4, result.Analyses.Length);
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
        }

        // The verb analyses list the verb senses, the noun analysis its own sense
        Assert.Equal(verb.Length, past.Analyses[0].Senses.Length);
        Assert.Equal(label, past.Analyses[0].Probability, Tolerance);
    }

    /// <summary>
    ///     The analyses of one lemma and part of speech that differ only in features share the mass equally when no
    ///     chooser separates them.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FeaturesOfOneLemmaAndPartOfSpeech_SplitTheMassEqually()
    {
        // Arrange: the two verb analyses of "read" have the same senses; a verb sense leads
        var table = Fixtures.Table(Fixtures.Lemma("read", fine: 1f));
        var verb = WordLexicon.SensesOf("read", PartOfSpeech.Verb);
        var vector = Fixtures.Feature("read", (verb[0].SenseId, 3));

        // Act
        var result = await RunAsync(table, vector, "read");

        // Assert: equal halves of the verb mass, the first analysis of the lexicon is selected
        var verbs = result.Analyses.Where(a => a.Details.PartOfSpeech == PartOfSpeech.Verb).ToArray();
        Assert.Equal(2, verbs.Length);
        Assert.Equal(verbs[0].Probability, verbs[1].Probability, 12);
        Assert.Equal("Tense=Past+VerbForm=Part", result.Details!.Features);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
    }

    /// <summary>
    ///     A chooser that is not confident gives the part of speech of the most probable analysis, with the
    ///     probability of that analysis as confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FormChooserBelowThreshold_AnswersPartOfSpeech()
    {
        // Arrange: the chooser prefers "leave" by a margin of 0.5, which a threshold of 2 refuses and 0.4 accepts
        var verb = WordLexicon.SensesOf("leave", PartOfSpeech.Verb);
        var vector = Fixtures.Feature((Fixtures.Column("leave", verb[0].SenseId), 3), (Fixtures.Label(0), 0.5));

        // Act
        var unsure = await RunAsync(LeftTable(2f), vector, "left");
        var sure = await RunAsync(LeftTable(0.4f), vector, "left");

        // Assert: the sense is not named although the sense scores are clear, yet the senses stay ranked
        var chooser = Math.Exp(0.5) / (Math.Exp(0.5) + 1);
        Assert.Equal(SenseLevel.PartOfSpeech, unsure.Level);
        Assert.Equal("leave", unsure.Details!.Lemma);
        Assert.Null(unsure.SenseId);
        Assert.Null(unsure.GroupId);
        Assert.Equal(chooser, unsure.Confidence, Tolerance);
        Assert.Equal(verb[0].SenseId, AnalysisOfLemma(unsure, "leave").Senses[0].SenseId);
        Assert.Equal(SenseLevel.Sense, sure.Level);
        Assert.Equal(verb[0].SenseId, sure.SenseId);
    }

    /// <summary>
    ///     A form of several lemmas that has no chooser is answered from the lexicon without running the encoder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_FormOfSeveralLemmasWithoutChooser_IsAnsweredFromTheLexicon()
    {
        // Arrange: both lemmas of "left" have weights, but the form has no chooser
        var table = Fixtures.Table(Fixtures.Lemma("leave", fine: 1f), Fixtures.Lemma("left", fine: 1f));
        var encoder = new FakeEncoder(Fixtures.Feature());
        var classifier = new OnnxClassifier(table, encoder.EncodeAsync);
        var lexicon = WordLexicon.Lookup("left");

        // Act
        var result = (await classifier.ClassifyAsync(["left"], TestContext.Current.CancellationToken))[0];

        // Assert
        Assert.Equal(0, encoder.Calls);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(lexicon, result.Analyses.Select(a => a.Details));
        Assert.All(result.Analyses, a => Assert.True(a.Senses.IsEmpty));
        Assert.Equal(lexicon[0], result.Details);
    }

    /// <summary>
    ///     Only a word that is pending (several scored senses, or a chooser) needs the model.
    /// </summary>
    [Fact]
    public void OnnxClassifier_NeedsModel_OnlyPendingWords_AreTrue()
    {
        // Arrange
        var table = JustTable();

        // Act / Assert: trained and ambiguous needs it; unknown, single-sense and untrained words do not
        Assert.True(OnnxClassifier.NeedsModel(["the", "just"], table));
        Assert.False(OnnxClassifier.NeedsModel(["the", "zzqxjw", ".", "record"], table));
        Assert.False(OnnxClassifier.NeedsModel(["just"], new WeightedLemmaTable()));
        Assert.False(OnnxClassifier.NeedsModel([], table));
        Assert.Throws<ArgumentNullException>(() => OnnxClassifier.NeedsModel(null!, table));
        Assert.Throws<ArgumentNullException>(() => OnnxClassifier.NeedsModel(["just"], null!));
    }

    /// <summary>
    ///     A list without a pending word is answered by the static lexicon path exactly like the classifier does.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ResolveWithoutModel_LexiconWords_MatchTheClassifier()
    {
        // Arrange
        var classifier = new OnnxClassifier();

        // Act
        var direct = OnnxClassifier.ResolveWithoutModel([.. Sentence, "zzqxjw"], CancellationToken.None);
        var answered = await classifier.ClassifyAsync([.. Sentence, "zzqxjw"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(answered, direct);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.Throws<OperationCanceledException>(() => OnnxClassifier.ResolveWithoutModel(Sentence, cancelled.Token));
        Assert.Throws<ArgumentNullException>(() => OnnxClassifier.ResolveWithoutModel(null!, CancellationToken.None));
    }

    private static WeightedLemmaTable LeftTable(float chooserThreshold) => Fixtures.Table(
        Fixtures.Lemma("leave", fine: 1f, choosers: [Fixtures.Chooser("left", ["leave", "left"], chooserThreshold)]),
        Fixtures.Lemma("left", fine: 1f));

    private static AnalysisCandidate AnalysisOfLemma(ClassifyResult result, string lemma) =>
        result.Analyses.First(a => a.Details.Lemma == lemma);
}
