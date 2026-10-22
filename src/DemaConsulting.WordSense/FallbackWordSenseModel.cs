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
///     The composite owns both models: <see cref="EnsureModelAsync"/> ensures the members that implement
///     <see cref="IDownloadableWordSenseModel"/> (concurrently, with the same options) and skips the others, and
///     <see cref="Dispose"/> disposes both. Disposal is idempotent and thread-safe; after it, both methods throw
///     <see cref="ObjectDisposedException"/>. If either model throws, the call throws; <see cref="EnsureModelAsync"/>
///     always starts the ensure step of every capable member first, even when one of them fails, and then reports the
///     failure (the exception of the first failed member, unchanged: a <see cref="WordSenseException"/> of a member
///     reaches the caller as it is). <see cref="ClassifyAsync"/> needs no preceding ensure: a member that needs files
///     ensures them lazily when its own <see cref="IWordSenseModel.ClassifyAsync"/> needs them. The composite
///     implements <see cref="IDownloadableWordSenseModel"/> also when no member has the capability (the call then
///     completes at once). The result array is parallel to the input, as for every model.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///
///     // The composition that the MiniLM and BertBaseCased packages build for you, written out for any primary model
///     static IDownloadableWordSenseModel Compose(IWordSenseModel primary) => new FallbackWordSenseModel(primary, new BasicWordSenseModel());
///     </code>
/// </example>
public sealed class FallbackWordSenseModel : IDownloadableWordSenseModel
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
    ///     Makes sure the members that can be prepared have what they need; members without the
    ///     <see cref="IDownloadableWordSenseModel"/> capability are skipped.
    /// </summary>
    /// <param name="options">The download options, passed to every member that has the capability.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when every capable member is ready; when no member has the capability it is a completed task, or a cancelled task when <paramref name="cancellationToken"/> is already cancelled.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="ModelUnavailableException">Reported through the returned task when a member could not obtain its files.</exception>
    /// <exception cref="ModelIntegrityException">Reported through the returned task when the files of a member are damaged.</exception>
    /// <exception cref="DownloadConfigurationException">Reported through the returned task when the download settings are invalid.</exception>
    public Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_primary is not IDownloadableWordSenseModel && _fallback is not IDownloadableWordSenseModel)
        {
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
        }

        return EnsureMembersAsync(options, cancellationToken);
    }

    private async Task EnsureMembersAsync(DownloadOptions? options, CancellationToken cancellationToken)
    {
        // Every capable member always gets to run its ensure step, even when another one fails synchronously or asynchronously
        var primaryTask = StartEnsure(_primary, options, cancellationToken);
        var fallbackTask = StartEnsure(_fallback, options, cancellationToken);
        await Task.WhenAll(primaryTask, fallbackTask).ConfigureAwait(false);
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
    /// <exception cref="WordSenseException">Thrown (as <see cref="ModelUnavailableException"/>, <see cref="ModelIntegrityException"/> or <see cref="DownloadConfigurationException"/>) when a member cannot obtain, verify or configure what it needs, unchanged from the member.</exception>
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

    private static Task StartEnsure(IWordSenseModel model, DownloadOptions? options, CancellationToken cancellationToken)
    {
        if (model is not IDownloadableWordSenseModel downloadable)
        {
            return Task.CompletedTask;
        }

        try
        {
            return downloadable.EnsureModelAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
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
