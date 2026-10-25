using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="FallbackWordSenseModel"/>.
/// </summary>
public class FallbackWordSenseModelTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");

    /// <summary>
    ///     A word whose selected analysis has scored senses, or that is answered at the sense or group level, is answered
    ///     by the primary model, with its complete result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_PrimaryScoredSenses_UsesPrimary()
    {
        // Arrange
        var scored = Result("a", [new(Noun, 0.9, [new("record#a", 0.7), new("record#b", 0.3)]), new(Verb, 0.1, [])], Noun, SenseLevel.PartOfSpeech);
        var sense = Result("b", [new(Noun, 1, [new("record#a", 1)])], Noun, SenseLevel.Sense);
        var group = Result("c", [new(Noun, 1, [])], Noun, SenseLevel.Group);
        using var model = new FallbackWordSenseModel(new StubModel(scored, sense, group), new StubModel(Plain("a"), Plain("b"), Plain("c")));

        // Act
        var results = await model.ClassifyAsync(["a", "b", "c"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([scored, sense, group], results);
    }

    /// <summary>
    ///     A word for which the primary model only chose between analyses (or does not know it) is answered by the
    ///     fallback model; a primary result whose scored senses belong to another analysis than the selected one does not
    ///     count.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_PrimaryChoseOnlyAnalysis_UsesFallback()
    {
        // Arrange
        var onlyAnalysis = Result("a", [new(Noun, 0.5, []), new(Verb, 0.5, [])], Noun, SenseLevel.PartOfSpeech);
        var otherAnalysisScored = Result("b", [new(Noun, 0.6, []), new(Verb, 0.4, [new("record#x", 1)])], Noun, SenseLevel.PartOfSpeech);
        var unknown = ClassifyResult.Unknown("c");
        var fallback = new[] { Plain("a"), Plain("b"), Plain("c") };
        using var model = new FallbackWordSenseModel(new StubModel(onlyAnalysis, otherAnalysisScored, unknown), new StubModel(fallback));

        // Act
        var results = await model.ClassifyAsync(["a", "b", "c"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(fallback, results);
    }

    /// <summary>
    ///     The composite has the downloadable capability, so code that holds only the model contract can ask for it.
    /// </summary>
    [Fact]
    public void WordSenseContracts_FallbackWordSenseModel_IsDownloadable()
    {
        // Arrange
        using var model = new FallbackWordSenseModel(new PlainModel(), new PlainModel());

        // Act
        IWordSenseModel contract = model;

        // Assert
        Assert.IsType<IDownloadableWordSenseModel>(contract, exactMatch: false);
    }

    /// <summary>
    ///     Ensuring the composite ensures both capable models, with the same options.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_EnsuresBoth()
    {
        // Arrange
        var primary = new StubModel();
        var fallback = new StubModel();
        using var model = new FallbackWordSenseModel(primary, fallback);
        var options = new DownloadOptions(new Uri("https://models.example.com/"));

        // Act
        await model.EnsureModelAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(options, primary.EnsuredWith);
        Assert.Same(options, fallback.EnsuredWith);
        Assert.Equal(1, primary.EnsureCalls);
        Assert.Equal(1, fallback.EnsureCalls);
    }

    /// <summary>
    ///     Only the members that have the capability are ensured; a member without it is skipped, in either position.
    /// </summary>
    /// <param name="capablePrimary">Whether the primary model is the capable one.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_OnlyCapableMemberEnsured(bool capablePrimary)
    {
        // Arrange
        var capable = new StubModel();
        var plain = new PlainModel();
        using var model = capablePrimary ? new FallbackWordSenseModel(capable, plain) : new FallbackWordSenseModel(plain, capable);
        var options = new DownloadOptions(new Uri("https://models.example.com/"));
        using var cts = new CancellationTokenSource();

        // Act
        await model.EnsureModelAsync(options, cts.Token);

        // Assert: the capable member got the options and the token, the plain member was never asked
        Assert.Same(options, capable.EnsuredWith);
        Assert.Equal(cts.Token, capable.EnsuredToken);
        Assert.Equal(1, capable.EnsureCalls);
    }

    /// <summary>
    ///     When no member has the capability, the call completes at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_NoCapableMember_CompletesImmediately()
    {
        // Arrange
        using var model = new FallbackWordSenseModel(new PlainModel(), new PlainModel());

        // Act
        var task = model.EnsureModelAsync(new DownloadOptions(new Uri("https://models.example.com/")), TestContext.Current.CancellationToken);
        await task;

        // Assert
        Assert.True(task.IsCompletedSuccessfully);
    }

    /// <summary>
    ///     When no member has the capability, an already cancelled token gives a cancelled task, not an exception from
    ///     the call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_NoCapableMemberCancelled_ReturnsCanceledTask()
    {
        // Arrange
        using var model = new FallbackWordSenseModel(new PlainModel(), new PlainModel());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var task = model.EnsureModelAsync(null, cts.Token);

        // Assert
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    /// <summary>
    ///     When the ensure step of the primary model fails (synchronously or asynchronously), the fallback model still gets
    ///     its ensure step, and the failure of the primary model is reported after both ran.
    /// </summary>
    /// <param name="synchronous">Whether the primary model throws before it returns a task.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_PrimaryFails_StillEnsuresFallback(bool synchronous)
    {
        // Arrange
        var primary = new StubModel { EnsureFailure = synchronous ? EnsureFailureKind.Synchronous : EnsureFailureKind.Asynchronous };
        var fallback = new StubModel();
        using var model = new FallbackWordSenseModel(primary, fallback);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("Ensure failed.", failure.Message);
        Assert.Equal(1, primary.EnsureCalls);
        Assert.Equal(1, fallback.EnsureCalls);
    }

    /// <summary>
    ///     When both capable members fail with typed exceptions, both ran, and the typed exception of the primary member
    ///     reaches the caller unchanged.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_BothFail_ReportsAfterBothRan()
    {
        // Arrange
        var unavailable = new ModelUnavailableException("No source delivered the file.", new HttpRequestException("offline"));
        var primary = new StubModel { EnsureFailure = EnsureFailureKind.Asynchronous, Failure = unavailable };
        var fallback = new StubModel { EnsureFailure = EnsureFailureKind.Asynchronous, Failure = new ModelIntegrityException("Damaged.") };
        using var model = new FallbackWordSenseModel(primary, fallback);

        // Act
        var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

        // Assert: both ensure steps ran, and the original exception and its inner exception are preserved
        Assert.Same(unavailable, failure);
        Assert.IsType<HttpRequestException>(failure.InnerException);
        Assert.Equal(1, primary.EnsureCalls);
        Assert.Equal(1, fallback.EnsureCalls);
    }

    /// <summary>
    ///     Disposing the composite disposes each model once, however often it is called, and a model that throws on
    ///     disposal does not keep the other from being disposed.
    /// </summary>
    [Fact]
    public void WordSenseContracts_FallbackWordSenseModel_Dispose_DisposesBothOnce()
    {
        // Arrange
        var primary = new StubModel { ThrowOnDispose = true };
        var fallback = new StubModel();
        var model = new FallbackWordSenseModel(primary, fallback);

        // Act
        Assert.Throws<InvalidOperationException>(model.Dispose);
        model.Dispose();
        model.Dispose();

        // Assert
        Assert.Equal(1, primary.DisposeCalls);
        Assert.Equal(1, fallback.DisposeCalls);
    }

    /// <summary>
    ///     A null model, a null word list, a null word and a disposed model are rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_ClassifyAsync_InvalidArguments_Throw()
    {
        // Arrange
        Assert.Throws<ArgumentNullException>(() => new FallbackWordSenseModel(null!, new StubModel()));
        Assert.Throws<ArgumentNullException>(() => new FallbackWordSenseModel(new StubModel(), null!));
        var model = new FallbackWordSenseModel(new StubModel(), new StubModel());

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => model.ClassifyAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => model.ClassifyAsync(["a", null!], TestContext.Current.CancellationToken));
        model.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["a"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A model that returns an array of another length than the input breaks the contract and is reported.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_ClassifyAsync_ResultNotParallel_Throws()
    {
        using var model = new FallbackWordSenseModel(new StubModel(Plain("a")), new StubModel(Plain("a"), Plain("b")));

        await Assert.ThrowsAsync<InvalidDataException>(() => model.ClassifyAsync(["a", "b"], TestContext.Current.CancellationToken));
    }

    private static ClassifyResult Plain(string word) => Result(word, [new(Verb, 1, [])], Verb, SenseLevel.PartOfSpeech);

    private static readonly WordDetails PutPast = new("put", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part");
    private static readonly WordDetails PutInf = new("put", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails PutNoun = new("put", PartOfSpeech.Noun, "Number=Sing");

    // The senses of the verb put and of the noun put, as an encoder lists them: the same for every variant of a slot
    private static readonly ImmutableArray<SenseCandidate> PutVerbSenses = [new("put#place", 0.8), new("put#express", 0.2)];

    // The result of an encoder for "put" that scored the senses of the verb slot (mass 0.6) and gave its variants the same probability
    private static ClassifyResult EncoderPut(double past = 0.3, double inf = 0.3) =>
        new("put", true, [new(PutPast, past, PutVerbSenses), new(PutInf, inf, PutVerbSenses), new(PutNoun, 1 - past - inf, [])], PutPast, SenseLevel.Sense, 0.55, "put#group", "put#place");

    // The result of Basic for "put": a context that says the infinitive
    private static ClassifyResult BasicPut(double past = 0.1, double inf = 0.6) =>
        new("put", true, [new(PutInf, inf, []), new(PutPast, past, []), new(PutNoun, 1 - past - inf, [])], PutInf, SenseLevel.PartOfSpeech, inf + past, null, null);

    private static async Task<ClassifyResult> Combine(ClassifyResult primary, ClassifyResult fallback)
    {
        using var model = new FallbackWordSenseModel(new StubModel(primary), new StubModel(fallback));
        return (await model.ClassifyAsync([primary.Word], TestContext.Current.CancellationToken))[0];
    }

    /// <summary>
    ///     The variants of a slot that the primary model gave the same probability take the relative proportions of the
    ///     fallback model, the probability of the slot stays as the primary model gave it, the variants are sorted again
    ///     and the selected analysis is the best variant; the senses, the level, the confidence and the ids are untouched.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_TiedVariantsOfASlot_TakeTheProportionsOfTheFallback()
    {
        // Arrange: the encoder ties the past and the infinitive (0.3 each); Basic says 6 to 1 for the infinitive
        var primary = EncoderPut();
        var fallback = BasicPut(past: 0.1, inf: 0.6);

        // Act
        var result = await Combine(primary, fallback);

        // Assert: the infinitive first, the slot mass 0.6 split 6:1, the noun and everything else as the encoder had it
        Assert.Equal([PutInf, PutPast, PutNoun], result.Analyses.Select(a => a.Details));
        Assert.Equal(0.6 * 6 / 7, result.Analyses[0].Probability, 12);
        Assert.Equal(0.6 / 7, result.Analyses[1].Probability, 12);
        Assert.Equal(0.4, result.Analyses[2].Probability, 12);
        Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 12);
        Assert.Equal(PutInf, result.Details);
        Assert.Equal(PutVerbSenses, result.Analyses[0].Senses);
        Assert.Equal(PutVerbSenses, result.Analyses[1].Senses);
        Assert.Equal((primary.Level, primary.Confidence, primary.GroupId, primary.SenseId), (result.Level, result.Confidence, result.GroupId, result.SenseId));
        Assert.Equal(primary.Word, result.Word);
    }

    /// <summary>
    ///     When the fallback model says past, the selected analysis stays the past one and the proportions are applied.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_TiedVariants_SelectedAnalysisFollowsTheFallbackSplit()
    {
        // Arrange: Basic says past (0.5 to 0.1), the encoder lists the past first on its tie
        var primary = EncoderPut();
        var fallback = BasicPut(past: 0.5, inf: 0.1);

        // Act
        var result = await Combine(primary, fallback);

        // Assert
        Assert.Equal([PutPast, PutInf, PutNoun], result.Analyses.Select(a => a.Details));
        Assert.Equal(PutPast, result.Details);
        Assert.Equal(0.6 * 5 / 6, result.Analyses[0].Probability, 12);
    }

    /// <summary>
    ///     Variants that the fallback model gives the same probability keep the order of the primary result after the split,
    ///     behind a variant that it ranks higher.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_TiedVariants_EqualShares_KeepTheOrderOfThePrimaryResult()
    {
        // Arrange: three tied variants of the verb slot (0.2 each); Basic ranks the finite form first and ties the others
        var finite = new WordDetails("put", PartOfSpeech.Verb, "VerbForm=Fin");
        var primary = new ClassifyResult("put", true, [new(PutPast, 0.2, PutVerbSenses), new(PutInf, 0.2, PutVerbSenses), new(finite, 0.2, PutVerbSenses), new(PutNoun, 0.4, [])], PutPast, SenseLevel.Sense, 0.55, "put#group", "put#place");
        var fallback = new ClassifyResult("put", true, [new(finite, 0.4, []), new(PutInf, 0.1, []), new(PutPast, 0.1, []), new(PutNoun, 0.4, [])], finite, SenseLevel.PartOfSpeech, 0.6, null, null);

        // Act
        var result = await Combine(primary, fallback);

        // Assert: the finite form first, then the two equal variants in the order of the primary result (past, infinitive)
        Assert.Equal([finite, PutPast, PutInf, PutNoun], result.Analyses.Select(a => a.Details));
        Assert.Equal(0.4, result.Analyses[0].Probability, 12);
        Assert.Equal(0.1, result.Analyses[1].Probability, 12);
        Assert.Equal(0.1, result.Analyses[2].Probability, 12);
        Assert.Equal(finite, result.Details);
    }

    /// <summary>
    ///     A slot that the primary model split unequally (a trained form chooser decided it) is never touched.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_UnequalVariants_AreLeftAsThePrimaryModelSplitThem()
    {
        // Arrange: the encoder split 0.45 to 0.15 although Basic says the opposite
        var primary = EncoderPut(past: 0.45, inf: 0.15);

        // Act
        var result = await Combine(primary, BasicPut(past: 0.1, inf: 0.6));

        // Assert
        Assert.Equal(primary, result);
    }

    /// <summary>
    ///     Two probabilities within 1e-9 are a tie, two that differ by more are not.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_TieTolerance_IsOneNanoUnit()
    {
        // Arrange
        var within = EncoderPut(past: 0.3, inf: 0.3 + 5e-10);
        var beyond = EncoderPut(past: 0.3, inf: 0.3 + 2e-9);

        // Act
        var tied = await Combine(within, BasicPut());
        var split = await Combine(beyond, BasicPut());

        // Assert
        Assert.Equal(PutInf, tied.Details);
        Assert.Equal(beyond, split);
    }

    /// <summary>
    ///     A slot whose variants the fallback model also gives the same probability, or does not list, is left as it is, and
    ///     so is a word with a single analysis or a primary result without analyses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_FallbackWithoutDistinction_LeavesThePrimaryResult()
    {
        // Arrange
        var primary = EncoderPut();
        var tiedToo = BasicPut(past: 0.3, inf: 0.3);
        var missing = new ClassifyResult("put", true, [new(PutInf, 0.7, []), new(PutNoun, 0.3, [])], PutInf, SenseLevel.PartOfSpeech, 0.7, null, null);
        var single = new ClassifyResult("put", true, [new(PutPast, 1, PutVerbSenses)], PutPast, SenseLevel.Sense, 1, null, "put#place");

        // Act
        var a = await Combine(primary, tiedToo);
        var b = await Combine(primary, missing);
        var c = await Combine(single, BasicPut());
        var d = await Combine(single, single);

        // Assert
        Assert.Equal(primary, a);
        Assert.Equal(primary, b);
        Assert.Equal(single, c);
        Assert.Equal(single, d);
    }

    /// <summary>
    ///     A slot that is not the selected one is re-split too, and the selected analysis stays that of the primary model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_TiedSlotThatIsNotSelected_IsSplitAndLeavesTheSelectedAnalysis()
    {
        // Arrange: the noun slot (0.7) is selected; the verb slot (0.3) has a tied past and infinitive
        var primary = new ClassifyResult("put", true, [new(PutNoun, 0.7, [new("put#noun", 1)]), new(PutPast, 0.15, PutVerbSenses), new(PutInf, 0.15, PutVerbSenses)], PutNoun, SenseLevel.PartOfSpeech, 0.7, null, null);
        var fallback = BasicPut(past: 0.1, inf: 0.3);

        // Act
        var result = await Combine(primary, fallback);

        // Assert
        Assert.Equal([PutNoun, PutInf, PutPast], result.Analyses.Select(a => a.Details));
        Assert.Equal(PutNoun, result.Details);
        Assert.Equal(0.7, result.Analyses[0].Probability, 12);
        Assert.Equal(0.3 * 0.75, result.Analyses[1].Probability, 12);
    }

    /// <summary>
    ///     Whatever the two models say, the probability of every slot, the level, the confidence and the ids of the result
    ///     are those of the primary model, and the probabilities still sum to one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_ManyInputs_KeepSlotTotalsLevelAndConfidence()
    {
        // Arrange: a deterministic sequence of splits for the noun slot and for what Basic says
        var seed = 12345u;
        double Next()
        {
            seed = unchecked((seed * 1664525u) + 1013904223u);
            return 0.05 + (0.9 * ((seed >> 8) / (double)(1u << 24)));
        }

        for (var i = 0; i < 200; i++)
        {
            var verbMass = Next();
            var half = verbMass / 2;
            var primary = new ClassifyResult("put", true, [new(PutPast, half, PutVerbSenses), new(PutInf, half, PutVerbSenses), new(PutNoun, 1 - verbMass, [])], PutPast, SenseLevel.Sense, 0.5, null, "put#place");
            var inf = Next() * 0.5;
            var past = Next() * 0.5;

            // Act
            var result = await Combine(primary, BasicPut(past: past, inf: inf));

            // Assert
            Assert.Equal(primary.Level, result.Level);
            Assert.Equal(primary.Confidence, result.Confidence);
            Assert.Equal(primary.SenseId, result.SenseId);
            Assert.Equal(1.0, result.Analyses.Sum(a => a.Probability), 9);
            Assert.Equal(verbMass, result.Analyses.Where(a => a.Details.PartOfSpeech == PartOfSpeech.Verb).Sum(a => a.Probability), 9);
            Assert.Equal(1 - verbMass, result.Analyses.First(a => a.Details == PutNoun).Probability, 9);
            Assert.Equal(result.Analyses[0].Details, result.Details);
        }
    }

    private static ClassifyResult Result(string word, ImmutableArray<AnalysisCandidate> analyses, WordDetails details, SenseLevel level) =>
        new(word, true, analyses, details, level, 1, null, null);

    private enum EnsureFailureKind
    {
        None,
        Synchronous,
        Asynchronous,
    }

    private sealed class StubModel(params ClassifyResult[] results) : IDownloadableWordSenseModel
    {
        private int _disposeCalls;

        public DownloadOptions? EnsuredWith { get; private set; }

        public CancellationToken EnsuredToken { get; private set; }

        public int EnsureCalls { get; private set; }

        public int DisposeCalls => _disposeCalls;

        public bool ThrowOnDispose { get; init; }

        public EnsureFailureKind EnsureFailure { get; init; }

        public Exception Failure { get; init; } = new InvalidOperationException("Ensure failed.");

        public Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            EnsuredWith = options;
            EnsuredToken = cancellationToken;
            return EnsureFailure switch
            {
                EnsureFailureKind.Synchronous => throw Failure,
                EnsureFailureKind.Asynchronous => Task.FromException(Failure),
                _ => Task.CompletedTask,
            };
        }

        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCalls);
            if (ThrowOnDispose)
            {
                FailDisposal();
            }
        }

        private static void FailDisposal() => throw new InvalidOperationException("Disposal failed.");
    }

    private sealed class PlainModel(params ClassifyResult[] results) : IWordSenseModel
    {
        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public void Dispose()
        {
        }
    }
}
