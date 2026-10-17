namespace DemaConsulting.WordSense;

/// <summary>
///     The context feature of one target word: either a dense vector or a set of sparse feature ids.
/// </summary>
/// <param name="Dense">The unit-length dense feature, or null for a sparse feature.</param>
/// <param name="Ids">The active sparse feature ids, or null for a dense feature.</param>
/// <param name="Value">The value every active sparse feature carries.</param>
internal readonly record struct ContextFeature(float[]? Dense, int[]? Ids, float Value);

/// <summary>
///     Turns the words of one sentence into the context features of selected target words.
/// </summary>
internal interface IFeatureSource
{
    /// <summary>
    ///     Computes the features of the target words.
    /// </summary>
    /// <param name="words">The words of the sentence as written.</param>
    /// <param name="targets">The indexes into <paramref name="words"/> that need a feature.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One entry per target, in order; null when no feature could be computed.</returns>
    Task<ContextFeature?[]> GetFeaturesAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken);
}
