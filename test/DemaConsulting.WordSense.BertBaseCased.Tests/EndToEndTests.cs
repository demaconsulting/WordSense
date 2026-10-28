namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     End-to-end test of the BertBaseCased model. It runs on the committed model files and is skipped when they are
///     absent (for example a checkout without Git LFS content).
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the"; each answer states its level and is
    ///     never a group, and the period is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_ClassifiesRecord()
    {
        // Arrange: the model on the committed model files, verified up front
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
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
    public async Task BertBaseCased_Classifier_EndToEnd_Have_AuxiliaryStaysAuxiliaryAndVerbGetsAVerbSense()
    {
        // Arrange
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
        model.Encoder.IntraOpThreads = 2;

        // Act
        var auxiliary = (await model.ClassifyAsync(["He", "has", "been", "there", "all", "day", "."], TestContext.Current.CancellationToken))[1];
        var verb = (await model.ClassifyAsync(["I", "have", "a", "car", "."], TestContext.Current.CancellationToken))[1];

        // Assert: the auxiliary is the selected analysis; the verb is answered with one of its own senses
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
    public async Task BertBaseCased_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext(string sentence, int index, bool past)
    {
        // Arrange
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
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

        // Assert: when the encoder scored senses for the verb, the slot of the verb, the level, the confidence and the ids are
        // those of the encoder (only the split inside the slot moved); otherwise the answer is the complete result of Basic
        var scored = encoded.Level is SenseLevel.Group or SenseLevel.Sense || encoded.Analyses.Any(a => a.Senses.Length > 0);
        if (scored)
        {
            Assert.Equal((encoded.Level, encoded.Confidence, encoded.GroupId, encoded.SenseId), (result.Level, result.Confidence, result.GroupId, result.SenseId));
            Assert.Equal(SlotMass(encoded), SlotMass(result), 9);
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
    ///     A word with several meanings that the encoder was not trained for is answered at the part-of-speech level and
    ///     lists the senses of its selected analysis ranked by the usage prior (the fallback model's ranking, never an
    ///     answer), and its key carries the label of the top-ranked sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage()
    {
        // Arrange
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require());
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
}
