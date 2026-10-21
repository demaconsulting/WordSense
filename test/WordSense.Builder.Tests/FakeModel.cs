using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     A model that answers every sentence from a function, for the tests of the evaluation commands.
/// </summary>
internal sealed class FakeModel : IWordSenseModel
{
    private readonly Func<IReadOnlyList<string>, ClassifyResult[]> _classify;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeModel"/> class.
    /// </summary>
    /// <param name="classify">The function that classifies a sentence.</param>
    public FakeModel(Func<IReadOnlyList<string>, ClassifyResult[]> classify)
    {
        _classify = classify;
    }

    /// <inheritdoc />
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
        Task.FromResult(_classify([.. words]));

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
