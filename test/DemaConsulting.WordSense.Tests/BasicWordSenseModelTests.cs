namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="BasicWordSenseModel"/>.
/// </summary>
public class BasicWordSenseModelTests
{
    /// <summary>
    ///     The model needs no files and no preparation step, so it does not offer the downloadable capability and has no
    ///     ensure member.
    /// </summary>
    [Fact]
    public void BasicWordSenseModel_IsNotDownloadable()
    {
        // Arrange
        using var model = new BasicWordSenseModel();

        // Act
        IWordSenseModel contract = model;

        // Assert: neither by type check nor by reflection
        Assert.False(contract is IDownloadableWordSenseModel);
        Assert.False(typeof(IDownloadableWordSenseModel).IsAssignableFrom(typeof(BasicWordSenseModel)));
        Assert.Null(typeof(BasicWordSenseModel).GetMethod("EnsureModelAsync"));
    }

    /// <summary>
    ///     The model classifies.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_ReturnsParallelResults()
    {
        var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(["Hello", "world"], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    /// <summary>
    ///     The embedded table tells the tense of a verb whose past and non-past share a spelling from its context, for the
    ///     textbook <c>read</c> and for verbs the table has no form column for: the selected analysis is a past tense or
    ///     participle after a form of <c>have</c> or a time word of the past, and not after a modal, <c>to</c> or an imperative.
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
    [InlineData("They will cut the budget next year .", 2, false)]
    [InlineData("He cut the cake into six equal pieces .", 1, true)]
    [InlineData("We usually set the table before dinner .", 2, false)]
    [InlineData("The workers had already set up the stage .", 4, true)]
    public async Task BasicWordSenseModel_ClassifyAsync_SameSpellingVerb_TenseFollowsTheContext(string sentence, int index, bool past)
    {
        // Arrange
        using var model = new BasicWordSenseModel();

        // Act
        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert
        var details = Assert.IsType<WordDetails>(results[index].Details);
        Assert.Equal(PartOfSpeech.Verb, details.PartOfSpeech);
        Assert.Equal(past, BasicClassifier.IsPast(details));
        Assert.Equal(1.0, results[index].Analyses.Sum(a => a.Probability), 9);
    }

    /// <summary>
    ///     The model classifies without any preparation call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_WithoutPreparation_Works()
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(["They", "record", "the", "record"], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A modal or helping verb in an auxiliary position is answered as an auxiliary with high confidence.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the auxiliary word.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("so we will record a record", 2)]
    [InlineData("they can see it", 1)]
    [InlineData("you may go now", 1)]
    [InlineData("we must go", 1)]
    [InlineData("she is running now", 1)]
    [InlineData("he has gone", 1)]
    [InlineData("she did not go", 1)]
    public async Task BasicWordSenseModel_ClassifyAsync_AuxiliaryContext_AnswersAuxiliary(string sentence, int index)
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Auxiliary, results[index].Details?.PartOfSpeech);
        Assert.True(results[index].Confidence > 0.9, $"The confidence of '{results[index].Word}' is {results[index].Confidence}.");
    }

    /// <summary>
    ///     A form of <c>be</c> that is the main verb of its clause (the copula) is a verb, as in the open corpus the
    ///     statistics come from; as a helper of another verb it is an auxiliary.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_CopulaBe_AnswersMainVerb()
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync("she is here".Split(' '), TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     The word <c>will</c> as a legal document, and <c>can</c> and <c>will</c> after an article, stay nouns.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the noun.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("his last will and testament", 2)]
    [InlineData("the can is empty", 1)]
    [InlineData("the will", 1)]
    public async Task BasicWordSenseModel_ClassifyAsync_NounContext_StaysNoun(string sentence, int index)
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Noun, results[index].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A possessive that is written as one token is answered with its own noun analysis, and the word after it is a
    ///     noun, as it is after an article: the tagger reads the stem and the possessive marker, the pattern of its
    ///     training counts.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the word after the possessive.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("Note the sergeant's rank .", 3)]
    [InlineData("Check the pilot's license .", 3)]
    [InlineData("We heard the captain's order .", 4)]
    public async Task BasicWordSenseModel_ClassifyAsync_PossessiveToken_ReadsTheNextWordAsNoun(string sentence, int index)
    {
        // Arrange: the embedded model and the words of the sentence, the possessive as one token
        using var model = new BasicWordSenseModel();
        var words = sentence.Split(' ');

        // Act: classify the whole sentence through the public model
        var results = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);

        // Assert: one result for each word, the possessive answered with its own noun analysis, the next word a noun
        Assert.Equal(words.Length, results.Length);
        Assert.Equal(words[index - 1], results[index - 1].Word);
        Assert.Equal(PartOfSpeech.Noun, results[index - 1].Details?.PartOfSpeech);
        Assert.Contains("Poss=Yes", results[index - 1].Details?.Features, StringComparison.Ordinal);
        Assert.Equal(PartOfSpeech.Noun, results[index].Details?.PartOfSpeech);
        Assert.True(results[index].Confidence > 0.8);
    }

    /// <summary>
    ///     A word after a determiner and a noun is a noun in a clause that already has a verb, although the word could
    ///     also be a verb. The test guards the answer (a noun) and a confidence floor of 0.75 (the weakest sentence,
    ///     the door handle, is answered at 0.79: the verb handle has far more anchors than the noun).
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("Please open the door handle .", 4)]
    [InlineData("Replace the oil filter .", 3)]
    [InlineData("Inspect the brake pad .", 3)]
    public async Task BasicWordSenseModel_ClassifyAsync_NounAfterNounInClauseWithVerb_IsNoun(string sentence, int index)
    {
        // Arrange: the embedded model; the clause already has a verb before the determiner
        using var model = new BasicWordSenseModel();

        // Act: classify the whole sentence through the public model
        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert: the word after the determiner and the noun is a noun, with a confidence above the floor
        Assert.Equal(PartOfSpeech.Noun, results[index].Details?.PartOfSpeech);
        Assert.True(results[index].Confidence > 0.75);
    }

    /// <summary>
    ///     A punctuation token or a conjunction ends the clause for the tagger: the word after a determiner and a noun
    ///     in the clause that follows is a verb again, although the first clause already has a verb.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("He nodded , the pump runs .", 5)]
    [InlineData("He nodded and the pump runs .", 5)]
    public async Task BasicWordSenseModel_ClassifyAsync_VerbAfterClauseBreak_IsVerb(string sentence, int index)
    {
        // Arrange: the embedded model; the first clause has a verb, then a break
        using var model = new BasicWordSenseModel();

        // Act: classify the whole sentence through the public model
        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert: the word after the determiner and the noun of the second clause is its verb
        Assert.Equal(PartOfSpeech.Verb, results[index].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A sentence-initial gerund does not give its clause a verb, so it is still read as a verb when a determiner
    ///     follows, while the verb of the clause comes later.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("Resolving this issue requires care .")]
    [InlineData("Checking this valve takes time .")]
    public async Task BasicWordSenseModel_ClassifyAsync_GerundAtTheStart_IsStillAVerb(string sentence)
    {
        // Arrange: the embedded model
        using var model = new BasicWordSenseModel();

        // Act: classify the whole sentence through the public model
        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert: the first word is a verb
        Assert.Equal(PartOfSpeech.Verb, results[0].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     Through the public model, the written case chooses between the capitalized and the lower-case lemma of one
    ///     spelling: <c>Polish</c> and <c>August</c> inside a sentence are the language and the month, <c>polish</c> and
    ///     <c>august</c> the shine and the adjective.
    /// </summary>
    /// <param name="sentence">The sentence, words separated by spaces.</param>
    /// <param name="index">The index of the word.</param>
    /// <param name="lemma">The lemma of the selected analysis.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("My neighbor speaks fluent Polish and a little German .", 4, "Polish")]
    [InlineData("The shop sells silver polish in small tins .", 4, "polish")]
    [InlineData("We usually take our vacation in August .", 6, "August")]
    [InlineData("The old professor had an august presence that quieted the room .", 5, "august")]
    public async Task BasicWordSenseModel_ClassifyAsync_CaseOfWord_ChoosesTheLemma(string sentence, int index, string lemma)
    {
        // Arrange: the built-in model with its embedded statistics
        using var model = new BasicWordSenseModel();

        // Act
        var results = await model.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(lemma, results[index].Details?.Lemma);
    }

    /// <summary>
    ///     The word <c>need</c> as a noun or a main verb stays what it was before the auxiliary sense existed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_NeedAsMainVerb_IsNotAuxiliary()
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(["we", "need", "rest"], TestContext.Current.CancellationToken);

        Assert.NotEqual(PartOfSpeech.Auxiliary, results[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     A lexicon-only list is answered without any model resource: the model needs none.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_LexiconOnlyWords_NeverNeedEnsure()
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(["The", ".", "zzqxjw"], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Determiner, results[0].Details!.PartOfSpeech);
        Assert.Equal(SenseLevel.Sense, results[0].Level);
        Assert.Equal(1.0, results[0].Confidence);
        Assert.Null(results[1].Details);
        Assert.Null(results[2].Details);
        Assert.Equal(SenseLevel.None, results[2].Level);
    }

    /// <summary>
    ///     One model gives the same answers when used from many threads at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_IsThreadSafe()
    {
        using var model = new BasicWordSenseModel();
        string[] words = ["They", "will", "record", "the", "record"];
        var expected = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var runs = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => model.ClassifyAsync(words, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(runs, run => Assert.Equal(expected, run));
    }

    /// <summary>
    ///     Disposing twice is harmless.
    /// </summary>
    [Fact]
    public void BasicWordSenseModel_Dispose_CalledTwice_IsHarmless()
    {
        var model = new BasicWordSenseModel();

        model.Dispose();

        Assert.Null(Record.Exception(model.Dispose));
    }

    /// <summary>
    ///     Disposing from many threads at once never throws.
    /// </summary>
    [Fact]
    public async Task BasicWordSenseModel_Dispose_Concurrent_IsHarmless()
    {
        var model = new BasicWordSenseModel();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ =>
        {
            try
            {
                model.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["a"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Classifying is rejected once the model is disposed, whatever the words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = new BasicWordSenseModel();
        var before = await model.ClassifyAsync(["They", "record"], TestContext.Current.CancellationToken);
        model.Dispose();

        Assert.Equal(2, before.Length);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["They", "record"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A classification that has started completes normally when the model is disposed afterwards: the Basic model
    ///     releases nothing, so a running call is never disturbed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Dispose_DuringClassification_RunningCallCompletes()
    {
        var model = new BasicWordSenseModel();
        string[] words = ["They", "will", "record", "the", "record", "."];
        var expected = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var running = model.ClassifyAsync(words, TestContext.Current.CancellationToken);
        model.Dispose();

        Assert.Equal(expected, await running);
    }

    /// <summary>
    ///     A null word list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_NullWords_Throws()
    {
        using var model = new BasicWordSenseModel();

        await Assert.ThrowsAnyAsync<ArgumentNullException>(() => model.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A token that is already cancelled stops the classification.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_Cancelled_Throws()
    {
        using var model = new BasicWordSenseModel();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.ClassifyAsync(["record"], cts.Token));
    }

    /// <summary>
    ///     A form with one analysis is answered with it and full confidence, at the part-of-speech level when its
    ///     lemma has several senses: <c>bowed</c> is a form of the verb <c>bow</c>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_FormWithOneAnalysis_IsAnsweredWithConfidenceOne()
    {
        using var model = new BasicWordSenseModel();

        var results = await model.ClassifyAsync(["He", "bowed", "to", "the", "queen", "."], TestContext.Current.CancellationToken);

        var analyses = WordLexicon.Lookup("bowed");
        Assert.Contains(results[1].Details!, analyses);
        Assert.Equal("bow", results[1].Details!.Lemma);
        if (analyses.Length == 1)
        {
            Assert.Equal(1.0, results[1].Confidence);
            Assert.Equal(SenseLevel.PartOfSpeech, results[1].Level);
        }
    }

    /// <summary>
    ///     <c>bows</c> keeps all its analyses, the context picks among them at the part-of-speech level and the answer
    ///     never names a sense or a group.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_BowsIsResolvedAmongItsAnalyses()
    {
        using var model = new BasicWordSenseModel();

        var actors = await model.ClassifyAsync(["The", "actors", "took", "their", "bows", "."], TestContext.Current.CancellationToken);
        var queen = await model.ClassifyAsync(["He", "bows", "to", "the", "queen", "."], TestContext.Current.CancellationToken);

        var analyses = WordLexicon.Lookup("bows");
        Assert.True(analyses.Length >= 2);
        Assert.Contains(actors[4].Details!, analyses);
        Assert.Contains(queen[1].Details!, analyses);
        Assert.Equal(PartOfSpeech.Noun, actors[4].Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Verb, queen[1].Details!.PartOfSpeech);
        Assert.All([actors[4], queen[1]], r => Assert.Equal(SenseLevel.PartOfSpeech, r.Level));
        Assert.All([actors[4], queen[1]], r => Assert.Null(r.SenseId));
    }

    /// <summary>
    ///     <c>reads</c> is always the present tense, while <c>read</c> is told apart by its context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_ReadsIsPresentAndReadSplitsByContext()
    {
        using var model = new BasicWordSenseModel();

        var reads = await model.ClassifyAsync(["She", "reads", "a", "book", "every", "night", "."], TestContext.Current.CancellationToken);
        var past = await model.ClassifyAsync(["Yesterday", "he", "read", "the", "report", "and", "sent", "comments", "."], TestContext.Current.CancellationToken);
        var present = await model.ClassifyAsync(["She", "loves", "to", "read", "before", "bed", "."], TestContext.Current.CancellationToken);

        var readsDetails = reads[1].Details!;
        Assert.Equal("read", readsDetails.Lemma);
        Assert.Equal(PartOfSpeech.Verb, readsDetails.PartOfSpeech);
        Assert.Contains("Tense=Pres", readsDetails.Features, StringComparison.Ordinal);
        Assert.Contains("Tense=Past", past[2].Details!.Features, StringComparison.Ordinal);
        Assert.Contains("Tense=Pres", present[3].Details!.Features, StringComparison.Ordinal);
    }
}
