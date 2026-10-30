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
///     it: its selected analysis lists scored senses (a non-empty <see cref="AnalysisCandidate.Senses"/> that are not only
///     ranked by usage, see <see cref="AnalysisCandidate.SensesRankedByUsage"/>), or its level is
///     <see cref="SenseLevel.Group"/> or <see cref="SenseLevel.Sense"/>. In every other case (the word is unknown to the
///     primary model, or it only chose between analyses, or it only ranked the senses by usage) the result of the fallback
///     model is used. The result of a word is
///     the result of one of the two models, never a mixture of the probabilities of both (the two refinements below are the
///     only exceptions): its analyses, selected analysis,
///     level, confidence and ids belong together, and the probabilities of one result are not comparable with those of
///     the other model. The rules of the models hold unchanged inside
///     the result that is used: the senses belong to a lemma and part of speech (a slot), the feature variants of a slot
///     share the probability of their slot, the selected analysis is the best variant of the most probable slot, and the
///     confidence is built on the probability of the slot. The first refinement below only re-splits the variants of a slot
///     that the primary result lists; the second one reweighs the slots of the primary result and can add the slots that
///     only the fallback result lists.
///     </para>
///     <para>
///     The first refinement keeps the result of the primary model honest about what it did not decide: the feature variants of a
///     slot (<c>read</c> as a past and as a present tense) share the senses, so an encoder has no evidence of its own for
///     the split of the slot and gives its variants the same probability. When the primary result gives all the variants
///     of a slot exactly the same probability (within 1e-9), the variants take the relative proportions that the fallback
///     model gives them (the tense model of the Basic model, which reads the context of the word), and the probability of
///     the slot stays as the primary model gave it. The level, the confidence, the group and the sense of the result
///     never change; the order of the variants inside the slot, their probabilities and, when it is the selected slot,
///     the selected analysis can. A slot that the primary model split unequally is left as it is.
///     </para>
///     <para>
///     The second refinement concerns the part of speech. The probability that an encoder gives a slot (a lemma and a
///     part of speech) is the mass of its senses, not an estimate of the part of speech, and when the encoder commits to
///     no sense and no group (its result is at the <see cref="SenseLevel.PartOfSpeech"/> level) its top slot can be the wrong
///     one where the tagger of the fallback model is right (the verb <c>use</c> that an encoder calls a noun). With a
///     primary weight <c>w</c> below 1 (the constructor argument <c>primaryWeight</c>; 1 when it is not given, which turns the
///     refinement off) the probability of every slot, over the union of the slots of both results, becomes proportional to
///     <c>p_primary^w × p_fallback^(1−w)</c>, where the probability of a slot is the sum of the probabilities of its analyses
///     and a slot that a result does not list counts with the small floor 1e-6. A slot that only the fallback result lists
///     is added to the result with the analyses of the fallback result (with the senses and the ranking kind that the
///     fallback result lists for them: for the Basic model the senses ranked by usage of a polysemous slot, see
///     <see cref="AnalysisCandidate.SensesRankedByUsage"/>), so the result can list
///     slots that the primary result did not. The analyses of a slot keep the shares they have in their own result
///     and the analyses of the primary result keep their senses, so the combination decides the slot only; the result is listed by
///     slot again, its selected analysis is the first analysis and its confidence is the probability of that slot (the
///     rule of every answer at the part-of-speech level). A result at the <see cref="SenseLevel.Sense"/> or
///     <see cref="SenseLevel.Group"/> level is never touched by this refinement, and neither is a result of one slot.
///     </para>
///     <para>
///     A word whose primary result did not score senses gets the fallback result, with one guard: the Basic model answers
///     at the <see cref="SenseLevel.Sense"/> level whenever its most probable analysis has exactly one sense, at any
///     confidence, but the encoder models promise sense answers calibrated to a precision of 0.97. So for a word with more
///     than one slot (lemma and part of speech) a fallback result at the sense level whose selected analysis lists exactly
///     one sense is kept only when its confidence is at least 0.97; below it the same analysis is answered at the
///     <see cref="SenseLevel.PartOfSpeech"/> level with the same confidence and no group or sense. The Basic model alone
///     is not affected.
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

    /// <summary>
    ///     The probability that a slot gets from a model that does not list it (and the least probability that a slot of
    ///     either model counts with), so that the logarithm of the combination is always defined.
    /// </summary>
    private const double SlotFloor = 1e-6;

    /// <summary>
    ///     The confidence that a sense-level answer of the fallback model needs for a word with several analyses: the
    ///     precision target (0.97) that the encoder models are calibrated to on real text.
    /// </summary>
    private const double SenseTarget = 0.97;

    private readonly IWordSenseModel _primary;
    private readonly IWordSenseModel _fallback;
    private readonly double _primaryWeight;
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FallbackWordSenseModel"/> class that keeps the result of the
    ///     primary model whole (the primary weight is 1).
    /// </summary>
    /// <param name="primary">The model whose result is used for a word when it scored the senses of the word; it is owned by this model.</param>
    /// <param name="fallback">The model whose result is used for every other word; it is owned by this model.</param>
    /// <exception cref="ArgumentNullException">Thrown when a model is null.</exception>
    public FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback)
        : this(primary, fallback, 1.0)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FallbackWordSenseModel"/> class with a primary weight for the
    ///     words that the primary model answers at the part-of-speech level.
    /// </summary>
    /// <param name="primary">The model whose result is used for a word when it scored the senses of the word; it is owned by this model.</param>
    /// <param name="fallback">The model whose result is used for every other word; it is owned by this model.</param>
    /// <param name="primaryWeight">
    ///     The weight of the primary model when the probabilities of the slots (lemma and part of speech) of the two models
    ///     are combined for a word that the primary model answers at the <see cref="SenseLevel.PartOfSpeech"/> level, from 0
    ///     to 1: 1 keeps the primary result as it is, 0 takes the slot probabilities of the fallback model.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a model is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="primaryWeight"/> is not a number from 0 to 1.</exception>
    public FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback, double primaryWeight)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        if (!(primaryWeight >= 0.0 && primaryWeight <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryWeight), primaryWeight, "The primary weight must be a number from 0 to 1.");
        }

        _primaryWeight = primaryWeight;
    }

    /// <summary>
    ///     Gets the weight of the primary model in the combination of the slot probabilities of a word that the primary
    ///     model answers at the <see cref="SenseLevel.PartOfSpeech"/> level; 1 means that the primary result is kept as it is.
    /// </summary>
    public double PrimaryWeight => _primaryWeight;

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

        var evidence = _primary is IPartOfSpeechEvidenceModel { ProvidesPartOfSpeechEvidence: true };
        var results = new ClassifyResult[list.Count];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = evidence && HasSeveralPartsOfSpeech(primary[i], fallback[i])
                ? Reweigh(primary[i], fallback[i], _primaryWeight)
                : Resolve(primary[i], fallback[i], evidence);
        }

        return results;
    }

    /// <summary>
    ///     Resolves a word for which the primary model has no part-of-speech evidence to reweigh: the primary result when
    ///     it scored senses, otherwise the fallback result.
    /// </summary>
    /// <param name="primary">The result of the primary model.</param>
    /// <param name="fallback">The result of the fallback model.</param>
    /// <param name="evidence">Whether the primary model provides part-of-speech evidence (then only the variants of a slot are refined).</param>
    /// <returns>The result for the word.</returns>
    private ClassifyResult Resolve(ClassifyResult primary, ClassifyResult fallback, bool evidence)
    {
        if (!ScoredSenses(primary))
        {
            return GuardSingleSense(fallback);
        }

        return evidence ? Refine(primary, fallback) : Combine(primary, fallback, _primaryWeight);
    }

    /// <summary>
    ///     Tells whether a word has two or more parts of speech, which is when the part-of-speech evidence of the primary
    ///     model applies.
    /// </summary>
    /// <param name="primary">The result of the primary model.</param>
    /// <param name="fallback">The result of the fallback model.</param>
    /// <returns>True when the analyses of the word that the primary model lists have two or more parts of speech and the fallback model has an answer for the word.</returns>
    private static bool HasSeveralPartsOfSpeech(ClassifyResult primary, ClassifyResult fallback) =>
        !fallback.Analyses.IsEmpty && primary.Analyses.Select(a => a.Details.PartOfSpeech).Distinct().Skip(1).Any();

    /// <summary>
    ///     Reweighs the slot probabilities of a primary model that estimates the part of speech against the fallback model
    ///     for a word of two or more parts of speech, at every level of the answer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The slot probability over the slots of both results becomes proportional to
    ///     <c>p_primary^w × p_fallback^(1−w)</c> (a slot that a result does not list counts with <see cref="SlotFloor"/>), where
    ///     <c>w</c> is the primary weight. The base result keeps the senses, the level, the group and the identifiers: it
    ///     is the primary result when the primary model scored senses (with its tied variants refined by the fallback
    ///     model), otherwise the fallback result (the senses ranked by usage and the tense variants of the Basic model). The analyses
    ///     of a slot keep the shares they have in their own result, and a slot that the base result does not list takes
    ///     the analyses of the other result.
    ///     </para>
    ///     <para>
    ///     When the most probable slot is the slot that the base result answered, the level, the group and the sense stay
    ///     and the confidence is the new probability of the slot times the probability of the sense (or the group) within it.
    ///     When another slot became the most probable one, the answer is at the <see cref="SenseLevel.PartOfSpeech"/> level on the
    ///     best analysis of that slot, with the probability of the slot as its confidence: the composition decided a
    ///     part of speech, not a sense. A base result from the fallback model at the sense level for a word of several slots is
    ///     kept only at a confidence of <see cref="SenseTarget"/> or more (see <see cref="GuardSingleSense"/>).
    ///     </para>
    /// </remarks>
    /// <param name="primary">The result of the primary model.</param>
    /// <param name="fallback">The result of the fallback model for the same word.</param>
    /// <param name="weight">The primary weight, from 0 to 1.</param>
    /// <returns>The reweighed result.</returns>
    private static ClassifyResult Reweigh(ClassifyResult primary, ClassifyResult fallback, double weight)
    {
        var scored = ScoredSenses(primary);
        var baseResult = scored ? Refine(primary, fallback) : fallback;
        var primarySlots = GroupBySlot(scored ? baseResult.Analyses : primary.Analyses);
        var fallbackSlots = GroupBySlot(fallback.Analyses);
        var slots = new List<(List<AnalysisCandidate> Members, double Primary, double Fallback)>();
        foreach (var members in primarySlots)
        {
            var other = fallbackSlots.Find(s => SenseFusion.SameSlot(s[0].Details, members[0].Details));
            slots.Add((scored || other is null ? members : other, SlotProbability(members), other is null ? 0.0 : SlotProbability(other)));
        }

        foreach (var members in fallbackSlots.Where(s => !primarySlots.Exists(p => SenseFusion.SameSlot(p[0].Details, s[0].Details))))
        {
            slots.Add((members, 0.0, SlotProbability(members)));
        }

        // The combination in the log domain, normalized with the largest score subtracted
        var scores = slots.Select(s => (weight * Math.Log(Math.Max(s.Primary, SlotFloor))) + ((1.0 - weight) * Math.Log(Math.Max(s.Fallback, SlotFloor)))).ToArray();
        var best = scores.Max();
        var exponents = scores.Select(x => Math.Exp(x - best)).ToArray();
        var total = exponents.Sum();
        var probability = exponents.Select(e => e / total).ToArray();

        // The most probable slot first (a tie keeps the order of the slots), and in a slot the most probable analysis first
        var order = Enumerable.Range(0, slots.Count).OrderByDescending(k => probability[k]).ThenBy(k => k).ToArray();
        var combined = ImmutableArray.CreateBuilder<AnalysisCandidate>(slots.Sum(s => s.Members.Count));
        foreach (var k in order)
        {
            var mass = SlotProbability(slots[k].Members);
            foreach (var member in slots[k].Members.OrderByDescending(a => a.Probability))
            {
                combined.Add(member.WithProbability(probability[k] * (mass > 0.0 ? member.Probability / mass : 1.0 / slots[k].Members.Count)));
            }
        }

        var analyses = combined.ToImmutable();
        var top = order[0];
        var answered = baseResult.Details is { } selected ? slots.FindIndex(s => SenseFusion.SameSlot(s.Members[0].Details, selected)) : -1;
        ClassifyResult result;
        if (answered == top && baseResult.Level is SenseLevel.Sense or SenseLevel.Group)
        {
            // The probability of the sense (or the group) within the slot does not change; the slot's probability does
            var baseSlot = GroupBySlot(baseResult.Analyses).Find(s => SenseFusion.SameSlot(s[0].Details, baseResult.Details!));
            var inSlot = baseSlot is null || SlotProbability(baseSlot) <= 0.0 ? 1.0 : baseResult.Confidence / SlotProbability(baseSlot);
            result = new ClassifyResult(baseResult.Word, baseResult.IsKnown, analyses, baseResult.Details, baseResult.Level, probability[top] * Math.Min(1.0, inSlot), baseResult.GroupId, baseResult.SenseId);
        }
        else
        {
            result = new ClassifyResult(baseResult.Word, baseResult.IsKnown, analyses, analyses[0].Details, SenseLevel.PartOfSpeech, probability[top], null, null);
        }

        return scored ? result : GuardSingleSense(result);
    }

    /// <summary>
    ///     Keeps a sense-level answer of the fallback model for a word with several analyses only when its confidence
    ///     reaches <see cref="SenseTarget"/>; otherwise answers at the part-of-speech level with the same analysis.
    /// </summary>
    /// <remarks>
    ///     The Basic model answers at the sense level whenever its most probable analysis has exactly one sense, at any
    ///     confidence: that is the right behavior for a model that makes no promise about its sense answers. The encoder
    ///     models do promise sense answers that are calibrated to the target precision, and a word whose own lemma the
    ///     encoder did not score (a form of an untrained one-sense lemma, <c>growing</c> as an adjective, <c>more</c> as
    ///     <c>much</c>) is answered by the fallback result as it is, so that answer would carry no such promise. For a
    ///     word with more than one analysis, a fallback result at the sense level whose selected analysis lists exactly
    ///     one sense is therefore kept only when its confidence is at least <see cref="SenseTarget"/>; below it the
    ///     result is the same analysis, at the part-of-speech level, with the same confidence (the probability of its
    ///     slot) and no group or sense. A word with a single analysis (the probability of the slot is 1) and every other
    ///     result are returned as they are.
    /// </remarks>
    /// <param name="fallback">The result of the fallback model for a word that the primary model did not score.</param>
    /// <returns>The result, or <paramref name="fallback"/> itself when nothing applies.</returns>
    private static ClassifyResult GuardSingleSense(ClassifyResult fallback)
    {
        if (fallback.Level != SenseLevel.Sense || fallback.Confidence >= SenseTarget || GroupBySlot(fallback.Analyses).Count < 2)
        {
            return fallback;
        }

        var senseCount = fallback.Analyses.Where(a => a.Details.Equals(fallback.Details)).Select(a => a.Senses.IsDefaultOrEmpty ? 0 : a.Senses.Length).FirstOrDefault();
        return senseCount == 1
            ? new ClassifyResult(fallback.Word, fallback.IsKnown, fallback.Analyses, fallback.Details, SenseLevel.PartOfSpeech, fallback.Confidence, null, null)
            : fallback;
    }

    /// <summary>
    ///     Combines a primary result that scored senses with the fallback result of the same word.
    /// </summary>
    /// <param name="primary">The result of the primary model.</param>
    /// <param name="fallback">The result of the fallback model.</param>
    /// <param name="weight">The primary weight.</param>
    /// <returns>The result for the word.</returns>
    private static ClassifyResult Combine(ClassifyResult primary, ClassifyResult fallback, double weight)
    {
        var split = SplitClosedClass(primary, fallback, out var applied);
        return Reconcile(Refine(split, fallback), fallback, weight, applied);
    }

    /// <summary>
    ///     Takes the probability of the closed-class slots of a primary result from the fallback result, and lets the
    ///     primary result decide only inside the open-class part.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A closed-class slot (an auxiliary, determiner, pronoun, preposition, conjunction or interjection of a word: no
    ///     WordNet senses, see <see cref="SenseFusion.IsClosedClass"/>) is a part of speech that a sense classifier has
    ///     no evidence for: the mass of its senses says nothing about whether the word is used in that way. So when the
    ///     primary result lists closed-class slots without probability (the encoder does not score them), the
    ///     probability of every closed-class slot becomes the probability that the fallback result
    ///     gives it (normalized over the slots of the primary result), and the open-class slots share the rest, in the
    ///     proportions of the primary result. The analyses of a closed-class slot are those of the fallback result, with
    ///     its senses and the ranking kind of its senses. A primary result that gives a closed-class slot a probability
    ///     has an estimate of its own and is left as it is (the Basic model as the primary model, for example).
    ///     </para>
    ///     <para>
    ///     When an open-class slot is still the most probable one, the primary result keeps its level, group and sense and
    ///     its confidence is scaled by the new probability of the slot (the confidence is the probability of the slot times
    ///     the probability of the sense within it). When a closed-class slot is the most probable one the answer is at the
    ///     part-of-speech level with the slot probabilities worked out here, its first analysis selected and no sense or
    ///     group: the composite decided a part of speech, not a sense. A primary result without a closed-class slot or
    ///     without an open-class slot, a fallback result without analyses, or a fallback result that lists none of the
    ///     slots is returned as it is. A primary result whose open-class slots have no probability at all has no evidence
    ///     for them either, so every slot takes the probability of the fallback result (the analyses keep their senses and
    ///     share the probability of their slot equally) and the answer is at the part-of-speech level.
    ///     </para>
    /// </remarks>
    /// <param name="primary">The result of the primary model, which scored senses for the word.</param>
    /// <param name="fallback">The result of the fallback model for the same word.</param>
    /// <param name="applied">True when the probabilities were reassigned, false when the primary result is returned as it is.</param>
    /// <returns>The primary result with the probabilities reassigned, or the primary result itself.</returns>
    internal static ClassifyResult SplitClosedClass(ClassifyResult primary, ClassifyResult fallback, out bool applied)
    {
        applied = false;
        var slots = GroupBySlot(primary.Analyses);
        var isClosed = slots.Select(s => SenseFusion.IsClosedClass(s[0].Details.PartOfSpeech)).ToArray();
        var closedCount = isClosed.Count(c => c);
        if (fallback.Analyses.IsEmpty || closedCount == 0 || closedCount == slots.Count)
        {
            return primary;
        }

        // The probability of every slot of the primary result in the fallback result, normalized over those slots
        var fallbackSlots = GroupBySlot(fallback.Analyses);
        var fallbackOf = slots
            .Select(s => fallbackSlots.Find(f => SenseFusion.SameSlot(f[0].Details, s[0].Details)))
            .ToArray();
        var fallbackTotal = fallbackOf.Sum(f => f is null ? 0.0 : SlotProbability(f));
        if (fallbackTotal <= 0.0)
        {
            return primary;
        }

        var primaryProbability = slots.Select(SlotProbability).ToArray();
        if (Enumerable.Range(0, slots.Count).Any(k => isClosed[k] && primaryProbability[k] > 0.0))
        {
            // The primary model has an estimate of its own for a closed-class slot (a tagger, or a classifier that scored it)
            return primary;
        }

        // A primary result without open-class mass (a label of the chooser that stands for closed-class slots only took all of
        // it) has no evidence for the open-class slots either, so every slot takes the probability of the fallback result
        var openMass = Enumerable.Range(0, slots.Count).Where(k => !isClosed[k]).Sum(k => primaryProbability[k]);
        var noOpenEvidence = openMass <= 0.0;
        var fallbackProbability = fallbackOf.Select(f => f is null ? 0.0 : SlotProbability(f) / fallbackTotal).ToArray();
        var closedMass = Enumerable.Range(0, slots.Count).Where(k => isClosed[k]).Sum(k => fallbackProbability[k]);
        var probability = Enumerable.Range(0, slots.Count)
            .Select(k => isClosed[k] || noOpenEvidence ? fallbackProbability[k] : (1.0 - closedMass) * primaryProbability[k] / openMass)
            .ToArray();

        // The most probable slot first (a tie keeps the order of the slots)
        var order = Enumerable.Range(0, slots.Count).OrderByDescending(k => probability[k]).ThenBy(k => k).ToArray();
        var best = order[0];
        var combined = ImmutableArray.CreateBuilder<AnalysisCandidate>(primary.Analyses.Length + fallback.Analyses.Length);
        foreach (var k in order)
        {
            if (isClosed[k])
            {
                foreach (var analysis in (fallbackOf[k] ?? slots[k]).OrderByDescending(a => a.Probability))
                {
                    combined.Add(analysis.WithProbability(fallbackOf[k] is null ? 0.0 : analysis.Probability / fallbackTotal));
                }
            }
            else if (noOpenEvidence)
            {
                // The analyses keep the senses they list and share the probability of their slot equally
                foreach (var analysis in slots[k])
                {
                    combined.Add(analysis.WithProbability(probability[k] / slots[k].Count));
                }
            }
            else
            {
                var scale = primaryProbability[k] > 0.0 ? probability[k] / primaryProbability[k] : 0.0;
                foreach (var analysis in slots[k])
                {
                    combined.Add(analysis.WithProbability(analysis.Probability * scale));
                }
            }
        }

        var analyses = combined.ToImmutable();
        applied = true;
        var keepsLevel = !isClosed[best]
            && primary.Details is { } primarySelected
            && SenseFusion.SameSlot(slots[best][0].Details, primarySelected)
            && primary.Level is SenseLevel.Sense or SenseLevel.Group
            && primaryProbability[best] > 0.0;
        return keepsLevel
            ? new ClassifyResult(primary.Word, primary.IsKnown, analyses, primary.Details, primary.Level, primary.Confidence * probability[best] / primaryProbability[best], primary.GroupId, primary.SenseId)
            : new ClassifyResult(primary.Word, primary.IsKnown, analyses, analyses[0].Details, SenseLevel.PartOfSpeech, probability[best], null, null);
    }

    /// <summary>
    ///     Combines the probabilities of the slots of a primary result that committed to no sense and no group with those of
    ///     the fallback result.
    /// </summary>
    /// <remarks>
    ///     The probability of a slot of an encoder is the mass of its senses, which says what the senses of the slot are
    ///     worth in this context but is not an estimate of the part of speech: a word whose slots are all weakly supported
    ///     (the answer stays at the part-of-speech level) can have the wrong slot on top, where the tagger of the
    ///     fallback model is right. So the probability of every slot, over the slots of both results, becomes
    ///     proportional to <c>p_primary^w × p_fallback^(1−w)</c> (a slot that a result does not list, or gives less than
    ///     <see cref="SlotFloor"/>, counts with <see cref="SlotFloor"/>), where <c>w</c> is the primary weight. The
    ///     variants of a slot keep the shares they have in their own result (equally when the slot has no probability),
    ///     and the analyses keep their senses. The result is re-ordered by slot, the most probable first; its selected
    ///     analysis is the first one, and its confidence is the probability of the selected slot, as for every answer at the
    ///     part-of-speech level. A result at the sense or group level, a weight of 1, a fallback result without analyses
    ///     and a result of a single slot are returned as they are.
    ///     <para>
    ///     When the closed-class split has been applied to the primary result (<paramref name="closedFixed"/>), the probability of every closed-class slot of the primary result is
    ///     the probability of the fallback result by construction (0 when Basic gives the slot none or omits it) and is never weighted again: combining it again with the
    ///     fallback result would count the fallback twice and move the closed mass. The combination is then made inside the
    ///     open-class part only: the other slots share the probability that the closed-class slots leave, in the proportions
    ///     of the combination, so the closed-class slots keep exactly the probability of the fallback and the open-class
    ///     slots keep the encoder evidence combined with the fallback evidence.
    ///     </para>
    /// </remarks>
    /// <param name="primary">The result of the primary model, which scored senses for the word.</param>
    /// <param name="fallback">The result of the fallback model for the same word.</param>
    /// <param name="weight">The primary weight, from 0 to 1.</param>
    /// <param name="closedFixed">Whether the closed-class slots of the primary result carry the probability of the fallback result (the split was applied) and keep it.</param>
    /// <returns>The reconciled result, or <paramref name="primary"/> itself when nothing applies.</returns>
    private static ClassifyResult Reconcile(ClassifyResult primary, ClassifyResult fallback, double weight, bool closedFixed)
    {
        if (weight >= 1.0 || primary.Level != SenseLevel.PartOfSpeech || fallback.Analyses.IsEmpty)
        {
            return primary;
        }

        // The slots of both results (the slots of the primary result first, in its order) with the probability of each
        var primarySlots = GroupBySlot(primary.Analyses);
        var fallbackSlots = GroupBySlot(fallback.Analyses);
        var slots = new List<(List<AnalysisCandidate> Analyses, double Primary, double Fallback)>();
        foreach (var analyses in primarySlots)
        {
            var other = fallbackSlots.Find(s => SenseFusion.SameSlot(s[0].Details, analyses[0].Details));
            slots.Add((analyses, SlotProbability(analyses), other is null ? 0.0 : SlotProbability(other)));
        }

        foreach (var analyses in fallbackSlots.Where(s => !primarySlots.Exists(p => SenseFusion.SameSlot(p[0].Details, s[0].Details))))
        {
            slots.Add((analyses, 0.0, SlotProbability(analyses)));
        }

        if (slots.Count < 2)
        {
            return primary;
        }

        // The closed-class slots of the primary result that the split reassigned keep their probability, a probability of 0 too (Basic
        // gave the slot none, or omitted it); the other slots share the rest. A closed-class slot that only the fallback result lists
        // was not part of the split and is combined like any other
        var isFixed = slots.Select((s, k) => closedFixed && k < primarySlots.Count && SenseFusion.IsClosedClass(s.Analyses[0].Details.PartOfSpeech)).ToArray();
        var fixedMass = Enumerable.Range(0, slots.Count).Where(k => isFixed[k]).Sum(k => slots[k].Primary);
        if (Enumerable.Range(0, slots.Count).All(k => isFixed[k]))
        {
            return primary;
        }

        // The combination in the log domain over the other slots, normalized with the largest score subtracted
        var scores = slots.Select(s => (weight * Math.Log(Math.Max(s.Primary, SlotFloor))) + ((1.0 - weight) * Math.Log(Math.Max(s.Fallback, SlotFloor)))).ToArray();
        var best = Enumerable.Range(0, slots.Count).Where(k => !isFixed[k]).Max(k => scores[k]);
        var exponents = scores.Select(x => Math.Exp(x - best)).ToArray();
        var total = Enumerable.Range(0, slots.Count).Where(k => !isFixed[k]).Sum(k => exponents[k]);
        var probability = Enumerable.Range(0, slots.Count).Select(k => isFixed[k] ? slots[k].Primary : (1.0 - fixedMass) * exponents[k] / total).ToArray();

        // The most probable slot first (a tie keeps the order of the slots), and in a slot the most probable analysis
        // first (a tie keeps the order of the result): the variants keep their share of their own slot
        var order = Enumerable.Range(0, slots.Count).OrderByDescending(k => probability[k]).ThenBy(k => k).ToArray();
        var combined = ImmutableArray.CreateBuilder<AnalysisCandidate>(primary.Analyses.Length);
        foreach (var k in order)
        {
            var slotProbability = probability[k];
            var members = slots[k].Analyses;
            var mass = SlotProbability(members);
            foreach (var member in members.OrderByDescending(a => a.Probability))
            {
                var share = mass > 0.0 ? member.Probability / mass : 1.0 / members.Count;
                combined.Add(member.WithProbability(slotProbability * share));
            }
        }

        var analysesOfResult = combined.ToImmutable();
        return new ClassifyResult(primary.Word, primary.IsKnown, analysesOfResult, analysesOfResult[0].Details, SenseLevel.PartOfSpeech, probability[order[0]], null, null);
    }

    /// <summary>
    ///     Groups analyses by slot (lemma and part of speech), the slots and the analyses of a slot in the order of the input.
    /// </summary>
    /// <param name="analyses">The analyses.</param>
    /// <returns>The slots, each as the list of its analyses.</returns>
    private static List<List<AnalysisCandidate>> GroupBySlot(ImmutableArray<AnalysisCandidate> analyses)
    {
        var slots = new List<List<AnalysisCandidate>>();
        foreach (var analysis in analyses)
        {
            var slot = slots.Find(s => SenseFusion.SameSlot(s[0].Details, analysis.Details));
            if (slot is null)
            {
                slots.Add([analysis]);
            }
            else
            {
                slot.Add(analysis);
            }
        }

        return slots;
    }

    private static double SlotProbability(List<AnalysisCandidate> analyses) => analyses.Sum(a => a.Probability);

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
            split.Add(original.WithProbability(mass * shares[s] / total));
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

    /// <summary>
    ///     Tells whether the primary model scored the senses of a word: it answered at the group or sense level, or its
    ///     selected analysis lists senses that were not merely ranked by usage.
    /// </summary>
    /// <param name="result">The result of the primary model.</param>
    /// <returns>True when the result of the primary model is used for the word.</returns>
    private static bool ScoredSenses(ClassifyResult result)
    {
        if (result.Level is SenseLevel.Group or SenseLevel.Sense)
        {
            return true;
        }

        foreach (var analysis in result.Analyses)
        {
            if (analysis.Details.Equals(result.Details) && !analysis.Senses.IsEmpty && !analysis.SensesRankedByUsage)
            {
                return true;
            }
        }

        return false;
    }
}
