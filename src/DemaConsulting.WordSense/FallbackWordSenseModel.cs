namespace DemaConsulting.WordSense;

/// <summary>
///     A model that combines two models: it answers each word with the result of the primary model when the primary model
///     scored the senses of the word, and otherwise with the result of the fallback model.
/// </summary>
/// <remarks>
///     <para>
///     Both models classify the whole list of words (so each one sees the same sentence and context) and the results are
///     combined word by word. The result of the primary model is used for a word when the primary model scored senses for
///     it: its selected analysis lists scored senses (a non-empty <see cref="AnalysisCandidate.Senses"/>), or its level is
///     <see cref="SenseLevel.Group"/> or <see cref="SenseLevel.Sense"/>. In every other case (the word is unknown to the
///     primary model, or it only chose between analyses) the result of the fallback model is used. The result of a word is
///     always the complete result of one of the two models, never a mixture: its analyses, selected analysis, level,
///     confidence and ids belong together, and the probabilities of one result are not comparable with those of the other
///     model. The composition adds no probability of its own, so the rules of the models hold unchanged inside the result
///     that is used: the senses belong to a lemma and part of speech (a slot), the feature variants of a slot share its
///     probability, the selected analysis is the best variant of the most probable slot, and the confidence is built on
///     the probability of the slot.
///     </para>
///     <para>
///     A typical composition is an encoder model as the primary model, which scores the fine senses of the lemmas it was
///     trained for, and the Basic model as the fallback, which chooses the analysis (part of speech) of every other
///     ambiguous word. The packages <c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c>
///     compose exactly this by default.
///     </para>
///     <para>
///     The composite owns both models: <see cref="EnsureModelAsync"/> ensures both (concurrently, with the same mirror),
///     and <see cref="Dispose"/> disposes both. Disposal is idempotent and thread-safe; after it, both methods throw
///     <see cref="ObjectDisposedException"/>. If either model throws, the call throws. The result array is parallel to the
///     input, as for every model.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///
///     // The composition that the MiniLM and BertBaseCased packages build for you, written out for any primary model
///     static IWordSenseModel Compose(IWordSenseModel primary) => new FallbackWordSenseModel(primary, new BasicWordSenseModel());
///     </code>
/// </example>
public sealed class FallbackWordSenseModel : IWordSenseModel
{
    private readonly IWordSenseModel _primary;
    private readonly IWordSenseModel _fallback;
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FallbackWordSenseModel"/> class.
    /// </summary>
    /// <param name="primary">The model whose result is used for a word when it scored the senses of the word; it is owned by this model.</param>
    /// <param name="fallback">The model whose result is used for every other word; it is owned by this model.</param>
    /// <exception cref="ArgumentNullException">Thrown when a model is null.</exception>
    public FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <summary>
    ///     Makes sure both models have what they need.
    /// </summary>
    /// <param name="mirror">A mirror to try before the public source, passed to both models.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when both models are ready.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Task.WhenAll(_primary.EnsureModelAsync(mirror, cancellationToken), _fallback.EnsureModelAsync(mirror, cancellationToken));
    }

    /// <summary>
    ///     Classifies every word with both models and combines the results word by word.
    /// </summary>
    /// <param name="words">The words of one tokenized text, with punctuation as separate elements.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An array parallel to <paramref name="words"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="InvalidDataException">Thrown when a model returns an array of another length than the input.</exception>
    public async Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        var primaryTask = _primary.ClassifyAsync(list, cancellationToken);
        var fallbackTask = _fallback.ClassifyAsync(list, cancellationToken);
        await Task.WhenAll(primaryTask, fallbackTask).ConfigureAwait(false);
        var primary = primaryTask.Result;
        var fallback = fallbackTask.Result;
        if (primary.Length != list.Count || fallback.Length != list.Count)
        {
            throw new InvalidDataException("A model returned a result array that is not parallel to the words.");
        }

        var results = new ClassifyResult[list.Count];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = ScoredSenses(primary[i]) ? primary[i] : fallback[i];
        }

        return results;
    }

    /// <summary>
    ///     Disposes both models; it is idempotent and thread-safe.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _primary.Dispose();
        }
        finally
        {
            _fallback.Dispose();
        }
    }

    private static bool ScoredSenses(ClassifyResult result)
    {
        if (result.Level is SenseLevel.Group or SenseLevel.Sense)
        {
            return true;
        }

        foreach (var analysis in result.Analyses)
        {
            if (analysis.Details.Equals(result.Details) && !analysis.Senses.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }
}
