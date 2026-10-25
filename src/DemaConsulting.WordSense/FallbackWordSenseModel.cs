using System.Collections.Immutable;

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
///     the result of one of the two models, never a mixture of the probabilities of both: its analyses, selected analysis,
///     level, confidence and ids belong together, and the probabilities of one result are not comparable with those of
///     the other model. The composition adds no probability of its own, so the rules of the models hold unchanged inside
///     the result that is used: the senses belong to a lemma and part of speech (a slot), the feature variants of a slot
///     share the probability of their slot, the selected analysis is the best variant of the most probable slot, and the
///     confidence is built on the probability of the slot.
///     </para>
///     <para>
///     One refinement keeps the result of the primary model honest about what it did not decide: the feature variants of a
///     slot (<c>read</c> as a past and as a present tense) share the senses, so an encoder has no evidence of its own for
///     the split of the slot and gives its variants the same probability. When the primary result gives all the variants
///     of a slot exactly the same probability (within 1e-9), the variants take the relative proportions that the fallback
///     model gives them (the tense model of the Basic model, which reads the context of the word), and the probability of
///     the slot stays as the primary model gave it. The level, the confidence, the group and the sense of the result
///     never change; the order of the variants inside the slot, their probabilities and, when it is the selected slot,
///     the selected analysis can. A slot that the primary model split unequally is left as it is.
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
    /// <summary>
    ///     The largest difference of two probabilities that still counts as a tie: the equal split of an encoder is exact
    ///     up to rounding, so this only has to absorb floating-point noise.
    /// </summary>
    private const double TieTolerance = 1e-9;

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
            results[i] = ScoredSenses(primary[i]) ? Refine(primary[i], fallback[i]) : fallback[i];
        }

        return results;
    }

    /// <summary>
    ///     Gives the analyses of one slot that the primary model could not tell apart the relative probabilities of the
    ///     fallback model, keeping the probability of every slot as the primary model gave it.
    /// </summary>
    /// <remarks>
    ///     A slot (a lemma and a part of speech) whose analyses differ only in their features (<c>read</c> as a past
    ///     tense and as a present tense) shares its senses, so an encoder that scores senses has no evidence of its own for
    ///     the split and gives every analysis of the slot the same probability. The fallback model may have evidence for
    ///     the split (the context of the word: its tagger and its tense model), and the split does not change the slot
    ///     probability, so the level, the confidence, the group and the sense of the primary result are untouched by
    ///     construction; only the order of the analyses inside the slot, the probabilities inside the slot and, when the
    ///     slot is the selected one, the selected analysis (<see cref="ClassifyResult.Details"/>) can change. A slot whose
    ///     analyses the primary model split unequally (a trained form chooser decided it) is never touched, and neither is
    ///     a slot whose analyses the fallback model gives the same probability, or does not list at all.
    /// </remarks>
    /// <param name="primary">The result of the primary model, which scored senses for the word.</param>
    /// <param name="fallback">The result of the fallback model for the same word.</param>
    /// <returns>The result of the primary model with the analyses of its tied slots re-split, or <paramref name="primary"/> itself when no slot changes.</returns>
    private static ClassifyResult Refine(ClassifyResult primary, ClassifyResult fallback)
    {
        var analyses = primary.Analyses;
        if (analyses.Length < 2 || fallback.Analyses.Length < 2)
        {
            return primary;
        }

        // The slots of the primary result, each as the positions of its analyses; the analyses of a slot are listed together
        var slots = new List<List<int>>();
        for (var i = 0; i < analyses.Length; i++)
        {
            var slot = slots.Find(s => SenseFusion.SameSlot(analyses[s[0]].Details, analyses[i].Details));
            if (slot is null)
            {
                slots.Add([i]);
            }
            else
            {
                slot.Add(i);
            }
        }

        var changed = false;
        var refined = analyses.ToArray();
        foreach (var slot in slots.Where(s => s.Count >= 2))
        {
            changed |= RefineSlot(slot, analyses, fallback.Analyses, refined);
        }

        if (!changed)
        {
            return primary;
        }

        var details = Equals(primary.Details, analyses[0].Details) ? refined[0].Details : primary.Details;
        return new ClassifyResult(primary.Word, primary.IsKnown, ImmutableArray.Create(refined), details, primary.Level, primary.Confidence, primary.GroupId, primary.SenseId);
    }

    /// <summary>
    ///     Re-splits one slot of the primary result when its analyses are tied and the fallback model separates them.
    /// </summary>
    /// <param name="slot">The positions of the analyses of the slot in <paramref name="analyses"/>, ascending.</param>
    /// <param name="analyses">The analyses of the primary result.</param>
    /// <param name="fallbackAnalyses">The analyses of the fallback result.</param>
    /// <param name="refined">The analyses of the new result, a copy of <paramref name="analyses"/>; the positions of the slot are rewritten.</param>
    /// <returns>True when the slot was re-split.</returns>
    private static bool RefineSlot(List<int> slot, ImmutableArray<AnalysisCandidate> analyses, ImmutableArray<AnalysisCandidate> fallbackAnalyses, AnalysisCandidate[] refined)
    {
        // Tied: the primary model made no distinction among the analyses of the slot
        var mass = 0.0;
        foreach (var position in slot)
        {
            mass += analyses[position].Probability;
            if (Math.Abs(analyses[position].Probability - analyses[slot[0]].Probability) > TieTolerance)
            {
                return false;
            }
        }

        // The fallback model must list every analysis of the slot and separate them
        var shares = new double[slot.Count];
        for (var s = 0; s < slot.Count; s++)
        {
            var target = analyses[slot[s]].Details;
            var share = fallbackAnalyses.Where(c => c.Details.Equals(target)).Select(c => (double?)c.Probability).FirstOrDefault();
            if (share is null)
            {
                return false;
            }

            shares[s] = share.Value;
        }

        var total = shares.Sum();
        if (total <= 0 || shares.Max() - shares.Min() <= TieTolerance)
        {
            return false;
        }

        // Re-split the unchanged mass of the slot by the shares of the fallback, then sort the slot: the most probable
        // analysis first, a tie keeping the order of the primary result
        var split = new List<AnalysisCandidate>(slot.Count);
        for (var s = 0; s < slot.Count; s++)
        {
            var original = analyses[slot[s]];
            split.Add(new AnalysisCandidate(original.Details, mass * shares[s] / total, original.Senses));
        }

        var order = Enumerable.Range(0, split.Count).OrderByDescending(s => split[s].Probability).ThenBy(s => s).ToArray();
        for (var s = 0; s < slot.Count; s++)
        {
            refined[slot[s]] = split[order[s]];
        }

        return true;
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
