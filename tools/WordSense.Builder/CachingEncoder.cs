using System.Diagnostics;

namespace WordSense.Builder;

/// <summary>
///     Wraps an encoder call so that a sentence whose pooled vector is in the <see cref="EmbeddingCache"/> is not
///     encoded again, and new vectors are added to the cache.
/// </summary>
internal sealed class CachingEncoder
{
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<int>, CancellationToken, ValueTask<double[]?[]>> _inner;
    private readonly EmbeddingCache? _cache;
    private readonly Stopwatch _encodeTime = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="CachingEncoder"/> class.
    /// </summary>
    /// <param name="inner">The encoder call; it pools the target words of a sentence.</param>
    /// <param name="cache">The cache, or null to encode every sentence.</param>
    public CachingEncoder(Func<IReadOnlyList<string>, IReadOnlyList<int>, CancellationToken, ValueTask<double[]?[]>> inner, EmbeddingCache? cache)
    {
        _inner = inner;
        _cache = cache;
    }

    /// <summary>
    ///     Gets the number of sentences answered from the cache.
    /// </summary>
    public int Hits { get; private set; }

    /// <summary>
    ///     Gets the number of sentences that were encoded.
    /// </summary>
    public int Misses { get; private set; }

    /// <summary>
    ///     Gets the time spent in the inner encoder.
    /// </summary>
    public TimeSpan EncodeTime => _encodeTime.Elapsed;

    /// <summary>
    ///     Gets the cache, or null when caching is off.
    /// </summary>
    public EmbeddingCache? Cache => _cache;

    /// <summary>
    ///     Gets the pooled vector of one target word.
    /// </summary>
    /// <param name="words">The words of the sentence.</param>
    /// <param name="target">The index of the target word.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pooled vector, or null when the target word has no tokens (never cached).</returns>
    public async ValueTask<double[]?> EncodeAsync(IReadOnlyList<string> words, int target, CancellationToken cancellationToken)
    {
        var key = _cache is null ? null : EmbeddingCache.KeyOf(words, target);
        if (key is not null && _cache!.TryGet(key, out var cached))
        {
            Hits++;
            return cached;
        }

        _encodeTime.Start();
        double[]?[] vectors;
        try
        {
            vectors = await _inner(words, [target], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _encodeTime.Stop();
        }

        Misses++;
        var vector = vectors[0];
        if (key is not null && vector is not null)
        {
            _cache!.Add(key, vector);
        }

        return vector;
    }
}
