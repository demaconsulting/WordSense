namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     End-to-end test of the MiniLM model: the same test runs on every platform and runtime of the build matrix, which
///     makes it the evidence for the platform requirements and for the ONNX Runtime package.
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the"; each answer states its level and is
    ///     never a group, and the period is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ClassifiesRecord()
    {
        // Arrange: the model on the committed model files, verified up front
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert: one result per word, the two uses of "record" told apart, the period unknown
        Assert.Equal(6, results.Length);
        Assert.Equal(PartOfSpeech.Verb, results[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, results[4].Details?.PartOfSpeech);
        Assert.Equal(SenseLevel.None, results[5].Level);
        Assert.Null(results[5].Details);
        Assert.All(results, r => Assert.InRange(r.Confidence, 0.0, 1.0));
        Assert.All(results, r => Assert.NotEqual(SenseLevel.Group, r.Level));
    }

    /// <summary>
    ///     The auxiliary <c>have</c> stays an auxiliary and the verb <c>have</c> gets a verb sense: the closed-class part
    ///     of speech comes from the Basic model, and the encoder scores the senses inside the open-class part.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLM_Classifier_EndToEnd_Have_AuxiliaryStaysAuxiliaryAndVerbGetsAVerbSense()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var auxiliary = (await model.ClassifyAsync(["He", "has", "been", "there", "all", "day", "."], TestContext.Current.CancellationToken))[1];
        var verb = (await model.ClassifyAsync(["I", "have", "a", "car", "."], TestContext.Current.CancellationToken))[1];

        // Assert: the auxiliary is the selected analysis; the verb lists only verb senses, scored in context
        Assert.Equal(PartOfSpeech.Auxiliary, auxiliary.Details?.PartOfSpeech);
        Assert.Equal(1.0, auxiliary.Analyses.Sum(a => a.Probability), 9);
        Assert.Equal(PartOfSpeech.Verb, verb.Details?.PartOfSpeech);
        Assert.Equal(1.0, verb.Analyses.Sum(a => a.Probability), 9);
        var selected = verb.Analyses.First(a => a.Details.Equals(verb.Details));
        Assert.False(selected.Senses.IsEmpty);
        Assert.False(selected.SensesRankedByUsage);
        Assert.All(selected.Senses, s =>
        {
            Assert.True(WordLexicon.TryGetSense(s.SenseId, out var sense));
            Assert.Equal(PartOfSpeech.Verb, sense.PartOfSpeech);
        });
    }

    /// <summary>
    ///     The tense of <c>read</c> and <c>put</c> follows the context: the encoder cannot tell the variants of the slot
    ///     apart, so the Basic model splits them, and the slot keeps the probability the encoder gave it.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the verb.</param>
    /// <param name="past">Whether the verb is a past tense or a participle in the sentence.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("I will read that tomorrow .", 2, false)]
    [InlineData("I have read that already .", 2, true)]
    [InlineData("Please put the books on the shelf .", 1, false)]
    [InlineData("She had put the keys in her bag before she left .", 2, true)]
    public async Task MiniLM_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext(string sentence, int index, bool past)
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        var words = sentence.Split(' ');
        static double SlotMass(ClassifyResult r) => r.Analyses.Where(a => a.Details.PartOfSpeech == r.Details!.PartOfSpeech && a.Details.Lemma == r.Details.Lemma).Sum(a => a.Probability);

        // Act
        var results = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);
        var encoded = (await model.Encoder.ClassifyAsync(words, TestContext.Current.CancellationToken))[index];

        // Assert: the selected analysis has the tense of the context and the probabilities of the word sum to one
        var result = results[index];
        var details = Assert.IsType<WordDetails>(result.Details);
        Assert.Equal(PartOfSpeech.Verb, details.PartOfSpeech);
        Assert.Equal(past, details.TryGetFeature("Tense", out var tense) && tense == "Past" || details.TryGetFeature("VerbForm", out var form) && form == "Part");
        Assert.Equal(result.Details, result.Analyses[0].Details);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);

        // Assert: when the encoder named a sense or a group for the verb, the slot of the verb, the level, the confidence and the
        // ids are those of the encoder (only the split inside the slot moved); when it scored senses but committed to neither
        // (the part-of-speech level), the slot probabilities are combined with those of Basic, so the answer stays at that level
        // with the probability of the selected slot as the confidence and the senses of the encoder; otherwise the answer is the
        // complete result of Basic
        // (production eligibility: the selected analysis lists scored senses, or the level is Group or Sense)
        var scored = encoded.Level is SenseLevel.Group or SenseLevel.Sense || encoded.Analyses.Any(a => a.Details.Equals(encoded.Details) && !a.Senses.IsEmpty);
        if (encoded.Level is SenseLevel.Group or SenseLevel.Sense)
        {
            Assert.Equal((encoded.Level, encoded.Confidence, encoded.GroupId, encoded.SenseId), (result.Level, result.Confidence, result.GroupId, result.SenseId));
            Assert.Equal(SlotMass(encoded), SlotMass(result), 9);
        }
        else if (scored)
        {
            Assert.Equal((SenseLevel.PartOfSpeech, null, null), (result.Level, result.GroupId, result.SenseId));
            Assert.Equal(SlotMass(result), result.Confidence, 9);
            Assert.All(result.Analyses, a => Assert.True(encoded.Analyses.First(e => e.Details == a.Details).Senses.AsSpan().SequenceEqual(a.Senses.AsSpan())));
        }
        else
        {
            using var basic = new BasicWordSenseModel();
            var expected = (await basic.ClassifyAsync(words, TestContext.Current.CancellationToken))[index];
            Assert.Equal((expected.Level, expected.Confidence, expected.Details), (result.Level, result.Confidence, result.Details));
            Assert.Equal(expected.Analyses.Select(a => (a.Details, a.Probability)), result.Analyses.Select(a => (a.Details, a.Probability)));
        }
    }

    /// <summary>
    ///     Every result carries its candidate analyses, the selected analysis, a level and, at the sense level, the
    ///     sense id: the determiner "the" has one sense, so it is answered at the sense level.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ResultCarriesAnalysisLevelAndSense()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert: the one-sense determiner is a sense answer with its id, an unknown token has no analysis
        var the = results[3];
        Assert.True(the.IsKnown);
        Assert.Equal(SenseLevel.Sense, the.Level);
        Assert.Equal("the#determiner", the.SenseId);
        Assert.Null(the.GroupId);
        Assert.NotNull(the.Details);
        Assert.Equal(1.0, the.Confidence, 6);
        Assert.False(results[5].IsKnown);
        Assert.Empty(results[5].Analyses);
        Assert.All(results.Where(r => r.IsKnown), r => Assert.NotEmpty(r.Analyses));
    }

    /// <summary>
    ///     A word with several meanings that the model does not score is a known word, answered at the part-of-speech
    ///     level (never none) with its analysis and no sense or group id.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_UntrainedWord_AnswersPartOfSpeech()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert
        var record = results[4];
        Assert.True(record.IsKnown);
        Assert.NotNull(record.Details);
        Assert.Equal(SenseLevel.PartOfSpeech, record.Level);
        Assert.Null(record.SenseId);
        Assert.Null(record.GroupId);
        Assert.InRange(record.Confidence, 0.0, 1.0);
    }

    /// <summary>
    ///     A word with several meanings that the encoder was not trained for is answered at the part-of-speech level and
    ///     lists the senses of its selected analysis ranked by the usage prior (the fallback model's ranking, never an
    ///     answer), and its key carries the label of the top-ranked sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task MiniLM_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var record = (await model.ClassifyAsync(["They", "will", "garden", "the", "garden", "."], TestContext.Current.CancellationToken))[4];

        // Assert
        var details = Assert.IsType<WordDetails>(record.Details);
        var selected = record.Analyses.First(a => a.Details.Equals(details));
        Assert.Equal(SenseLevel.PartOfSpeech, record.Level);
        Assert.Null(record.SenseId);
        Assert.Equal(UsagePrior.RankOf(details.Lemma, details.PartOfSpeech), selected.Senses);
        Assert.True(selected.Senses.Length >= 2);
        Assert.True(selected.SensesRankedByUsage);
        Assert.EndsWith("#" + selected.Senses[0].SenseId.Split('#')[1], record.Key, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The analyses of a word are sorted by probability, largest first, and sum to one; the selected analysis is
    ///     the first of them.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_AnalysesAreRankedAndSumToOne()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert
        foreach (var result in results.Where(r => r.IsKnown))
        {
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 6);
            for (var i = 1; i < result.Analyses.Length; i++)
            {
                Assert.True(result.Analyses[i - 1].Probability >= result.Analyses[i].Probability);
            }

            Assert.Equal(result.Analyses[0].Details, result.Details);
        }
    }

    /// <summary>
    ///     Trained lemmas in clear sentences are answered at the finest level the model is confident in: at least one
    ///     answer reaches the sense level (the sentence of <c>dawn</c>, a trained lemma that no other sentence uses, is
    ///     the explicit witness), and every answer follows the level contract (a sense carries its id and the
    ///     group of the catalog, a group only its id, a part of speech neither).
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_AnswersAtTheFinestConfidentLevel()
    {
        // Arrange: trained lemmas of the first batch in sentences that make the sense clear
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;
        string[][] sentences =
        [
            ["I", "will", "go", "back", "to", "the", "house", "."],
            ["She", "turned", "right", "at", "the", "corner", "and", "walked", "home", "."],
            ["Please", "help", "me", "carry", "the", "test", "papers", "."],
            ["Neither", "side", "wanted", "to", "break", "the", "contract", "before", "it", "expired", "."],
            ["Muscles", "contract", "when", "they", "receive", "a", "signal", "from", "the", "nerves", "."],
            ["The", "farmers", "get", "up", "at", "dawn", "to", "milk", "the", "cows", "."],
        ];

        // Act
        var all = new List<ClassifyResult>();
        foreach (var sentence in sentences)
        {
            all.AddRange(await model.ClassifyAsync(sentence, TestContext.Current.CancellationToken));
        }

        // Assert: the levels follow the contract, and the most confident answers reach the sense level
        var trained = all.Where(r => r.Analyses.Any(a => !a.Senses.IsEmpty) && r.Level != SenseLevel.None && r.Analyses.Length > 0 && r.Details!.Lemma is "back" or "right" or "home" or "help" or "test" or "contract" or "dawn").ToList();
        Assert.NotEmpty(trained);
        Assert.Contains(trained, r => r.Level == SenseLevel.Sense);
        Assert.Contains(all, r => r.Word == "dawn" && r.Level == SenseLevel.Sense);
        foreach (var result in trained)
        {
            switch (result.Level)
            {
                case SenseLevel.Sense:
                    Assert.True(WordLexicon.TryGetSense(result.SenseId!, out var sense));
                    Assert.Equal(sense.GroupId, result.GroupId);
                    break;
                case SenseLevel.Group:
                    Assert.Null(result.SenseId);
                    Assert.True(WordLexicon.TryGetGroup(result.GroupId!, out _));
                    break;
                default:
                    Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
                    Assert.Null(result.SenseId);
                    Assert.Null(result.GroupId);
                    break;
            }
        }
    }

    /// <summary>
    ///     The senses of every analysis of a trained word are ranked by probability, largest first, and sum to one, and
    ///     belong to the lemma and the part of speech of the analysis.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_SensesAreRankedAndSumToOne()
    {
        // Arrange
        using var model = new MiniLMWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var results = await model.ClassifyAsync(["I", "will", "go", "back", "to", "the", "house", "."], TestContext.Current.CancellationToken);

        // Assert: the trained word lists scored senses in at least one analysis; each list is ranked and sums to one
        var back = results[3];
        Assert.Contains(back.Analyses, a => !a.Senses.IsEmpty);
        Assert.False(back.Analyses.First(a => a.Details.Equals(back.Details)).SensesRankedByUsage);
        foreach (var analysis in back.Analyses.Where(a => !a.Senses.IsEmpty))
        {
            Assert.Equal(1.0, analysis.Senses.Sum(s => s.Probability), 6);
            for (var i = 1; i < analysis.Senses.Length; i++)
            {
                Assert.True(analysis.Senses[i - 1].Probability >= analysis.Senses[i].Probability);
            }

            Assert.All(analysis.Senses, s =>
            {
                Assert.True(WordLexicon.TryGetSense(s.SenseId, out var info));
                Assert.Equal(analysis.Details.Lemma, info.Lemma);
                Assert.Equal(analysis.Details.PartOfSpeech, info.PartOfSpeech);
            });
        }
    }
}
