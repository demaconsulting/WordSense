namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxClassifier"/>: it answers every word from the lexicon alone, without model files.
/// </summary>
public class OnnxClassifierTests
{
    private static readonly string[] Sentence = ["They", "will", "record", "the", "record", "."];

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
    ///     A word with several analyses and several senses in each (no weights in this version) is answered with every
    ///     analysis, equally likely, at the part of speech level, and no analysis lists scored senses.
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
    ///     When the first analysis has one sense, the answer is at the sense level with the probability of that
    ///     analysis, and an analysis with several senses lists none.
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
    ///     An unknown word gets the unknown result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxClassifier_ClassifyAsync_UnknownWord_IsAnsweredAsUnknown()
    {
        // Arrange
        var classifier = new OnnxClassifier();

        // Act
        var result = await classifier.ClassifyAsync([.. Sentence, "zzqxjw"], TestContext.Current.CancellationToken);

        // Assert: the period and the made-up word are unknown, the rest is known
        Assert.Equal(ClassifyResult.Unknown("zzqxjw"), result[^1]);
        Assert.Equal(ClassifyResult.Unknown("."), result[5]);
        Assert.All(result.Take(5), r => Assert.True(r.IsKnown));
    }
}
