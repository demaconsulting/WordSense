namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     A deterministic <see cref="IWordSenseModel"/> for tests: the first component of the vector of a target depends on
///     whether the word before it is "the", the second is always 1 and the third is 0. A word that is empty or is
///     <c>\u0001</c> has no embedding, like a word that produces no tokens.
/// </summary>
internal sealed class FakeWordSenseModel : IWordSenseModel
{
    /// <summary>
    ///     The identifier of the fake model.
    /// </summary>
    public const string Id = "fake-model";

    /// <summary>
    ///     The SHA-256 of the fake model.
    /// </summary>
    public static readonly string Sha = new('a', 64);

    private int _calls;
    private int _disposed;

    /// <inheritdoc />
    public string ModelId { get; init; } = Id;

    /// <inheritdoc />
    public string ModelSha256 { get; init; } = Sha;

    /// <inheritdoc />
    public int HiddenSize { get; init; } = 3;

    /// <summary>
    ///     Gets the number of calls to <see cref="EncodeAsync"/> that reached the model.
    /// </summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>
    ///     Gets a value indicating whether the model has been disposed.
    /// </summary>
    public bool Disposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    ///     Gets a source that is cancelled while the model encodes, or null.
    /// </summary>
    public CancellationTokenSource? CancelDuringEncode { get; init; }

    /// <inheritdoc />
    public ValueTask<double[]?[]> EncodeAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _calls);
        CancelDuringEncode?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();

        var result = new double[]?[targets.Count];
        for (var t = 0; t < targets.Count; t++)
        {
            var index = targets[t];
            if (words[index].Length == 0 || words[index] == "\u0001")
            {
                continue;
            }

            var afterThe = index > 0 && string.Equals(words[index - 1], "the", StringComparison.OrdinalIgnoreCase);
            var vector = new double[HiddenSize];
            vector[0] = afterThe ? 1.0 : -1.0;
            vector[1] = 1.0;
            result[t] = vector;
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }
}
