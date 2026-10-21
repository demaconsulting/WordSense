namespace DemaConsulting.WordSense.Tests;

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
