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
