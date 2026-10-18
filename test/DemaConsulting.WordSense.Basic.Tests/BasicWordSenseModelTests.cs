namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicWordSenseModel"/>.
/// </summary>
public class BasicWordSenseModelTests
{
    /// <summary>
    ///     Making the model ready needs no resources and completes immediately.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_CompletesWithoutFiles()
    {
        var model = new BasicWordSenseModel();

        var task = model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    /// <summary>
    ///     A mirror is ignored: nothing is requested from it.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_IgnoresMirror()
    {
        var model = new BasicWordSenseModel();
        var mirror = new MirrorOptions(new Uri("http://127.0.0.1:1/unreachable"));

        var task = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    /// <summary>
    ///     A token cancelled before the call gives a cancelled task.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_Cancelled_ReturnsCanceledTask()
    {
        var model = new BasicWordSenseModel();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var task = model.EnsureModelAsync(null, cancelled.Token);

        Assert.True(task.IsCanceled);
    }

    /// <summary>
    ///     The model classifies after the ensure call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_ReturnsParallelResults()
    {
        IWordSenseModel model = new BasicWordSenseModel();
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var results = await model.ClassifyAsync(["Hello", "world"], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    /// <summary>
    ///     The model classifies without a preparation call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_WithoutEnsure_Works()
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
    [InlineData("she is here", 1)]
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
        Assert.Equal(1.0, results[0].Confidence);
        Assert.Null(results[1].Details);
        Assert.Null(results[2].Details);
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
    ///     Preparing after disposal is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = new BasicWordSenseModel();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
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
    ///     A form that names a single sense answers it with full confidence: <c>bowed</c> is only the bend verb.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_BowedIsOnlyTheBendVerb()
    {
        using var model = new BasicWordSenseModel();

        var senses = await model.ClassifyAsync(["He", "bowed", "to", "the", "queen", "."], TestContext.Current.CancellationToken);

        var bowed = Assert.Single(WordLexicon.Lookup("bowed"));
        Assert.Equal(bowed, senses[1].Details);
        Assert.Equal(PartOfSpeech.Verb, bowed.PartOfSpeech);
        Assert.Equal("bow#bend-verb", bowed.SenseId);
        Assert.Equal(1.0, senses[1].Confidence);
    }

    /// <summary>
    ///     <c>bows</c> keeps all three senses of <c>bow</c> and the context picks among them.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_ClassifyAsync_BowsIsResolvedAmongItsThreeSenses()
    {
        using var model = new BasicWordSenseModel();

        var actors = await model.ClassifyAsync(["The", "actors", "took", "their", "bows", "."], TestContext.Current.CancellationToken);
        var queen = await model.ClassifyAsync(["He", "bows", "to", "the", "queen", "."], TestContext.Current.CancellationToken);
        var archer = await model.ClassifyAsync(["She", "drew", "back", "the", "bows", "."], TestContext.Current.CancellationToken);

        var senses = WordLexicon.Lookup("bows");
        Assert.Equal(3, senses.Length);
        Assert.Contains(actors[4].Details, senses);
        Assert.Contains(queen[1].Details, senses);
        Assert.Contains(archer[4].Details, senses);
        Assert.Equal(PartOfSpeech.Verb, queen[1].Details?.PartOfSpeech);
        Assert.Equal("bow#weapon", archer[4].Details!.SenseId);
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
        Assert.Equal("read#present", readsDetails.SenseId);
        Assert.Equal("read#past", past[2].Details!.SenseId);
        Assert.Equal("read#present", present[3].Details!.SenseId);
    }
}
