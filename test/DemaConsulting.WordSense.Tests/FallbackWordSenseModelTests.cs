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
    ///     Ensuring the composite ensures both models, with the same mirror.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_EnsuresBoth()
    {
        // Arrange
        var primary = new StubModel();
        var fallback = new StubModel();
        using var model = new FallbackWordSenseModel(primary, fallback);
        var mirror = new MirrorOptions(new Uri("https://models.example.com/"));

        // Act
        await model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(mirror, primary.EnsuredWith);
        Assert.Same(mirror, fallback.EnsuredWith);
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

    private sealed class StubModel(params ClassifyResult[] results) : IWordSenseModel
    {
        private int _disposeCalls;

        public MirrorOptions? EnsuredWith { get; private set; }

        public int EnsureCalls { get; private set; }

        public int DisposeCalls => _disposeCalls;

        public bool ThrowOnDispose { get; init; }

        public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            EnsuredWith = mirror;
            return Task.CompletedTask;
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
}
