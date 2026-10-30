using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     Encodes the target words of a tokenized text: one pooled vector per target, or null for a target without word pieces.
/// </summary>
/// <param name="words">The words of the text.</param>
/// <param name="targets">The indexes of the target words.</param>
/// <param name="cancellationToken">A token that cancels the operation.</param>
/// <returns>An array parallel to <paramref name="targets"/>.</returns>
internal delegate ValueTask<double[]?[]> PooledEncode(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken);

/// <summary>
///     Encodes every word of a tokenized text in the possessive view that the part-of-speech head reads (see <see cref="WordVectors"/>).
/// </summary>
/// <param name="words">The words of the text.</param>
/// <param name="cancellationToken">A token that cancels the operation.</param>
/// <returns>The tokens of the view and the pooled vector of each.</returns>
internal delegate ValueTask<WordVectors> PooledViewEncode(IReadOnlyList<string> words, CancellationToken cancellationToken);

/// <summary>
///     The classifier of the encoder models: the unit that decides the answer for every word of a tokenized text.
/// </summary>
/// <remarks>
///     <para>
///     Every word is looked up in the lexicon. An unknown word gets <see cref="ClassifyResult.Unknown"/>. A word with
///     one analysis whose lemma and part of speech have exactly one sense is answered at the
///     <see cref="SenseLevel.Sense"/> level with confidence 1 and no encoder run. A word with a single lemma and part of
///     speech whose senses are several is at the <see cref="SenseLevel.PartOfSpeech"/> level with the probability of its
///     slot, 1, whenever no fine sense or group is answered (the slot has no alternative; the level, not the confidence,
///     tells that the sense is unresolved). A word whose lemma has trained weights
///     and several scored senses is pending: the pooled vector of each pending word is centered on the global mean of the
///     weights and scaled to unit length, and the classifier scores the fine senses of every analysis of the word that
///     has an open-class part of speech (noun, verb, adjective, adverb) and no sense of another lemma; a closed-class
///     analysis (auxiliary, determiner, pronoun, preposition, conjunction, interjection) lists no senses. The softmax of
///     the scores gives the probability of every sense. The senses belong to a lemma and part of speech
///     (a <i>slot</i>), not to the analyses of a slot that differ only in features (<c>cut</c> as an infinitive and as a
///     past tense, <c>deer</c> as a singular and a plural), so the probability of a sense given its slot is its mass
///     renormalized in the slot, and the analyses of a slot split the probability of the slot (by the chooser of the
///     form when it has a label for each of them, otherwise equally); that split never reaches the level or the
///     confidence. The selected analysis is the best analysis of the most probable slot, and the level is decided for it
///     with the thresholds of the lemma: the sense when the margin of the best fine sense reaches the fine threshold and
///     the best sense lies in that slot, else the group of the best sense when it has one and the margin of the group
///     reaches the group threshold, else the part of speech (the ranked senses of the analysis still show the likely
///     senses and groups). The confidence is the probability of the answer at its level (the probability of the slot,
///     times the probability of the sense, or of the group, within it).
///     </para>
///     <para>
///     With a head (the classifier of BertBaseCased; the MiniLM classifier, the tests of senses and the build tool have
///     none, and there the probability of a slot is the mass of its senses) the part-of-speech head decides the
///     probability of a slot. A known word of two or more parts of speech needs
///     the encoder whatever its lemma: the text is encoded once (in sliding windows when it is longer than the encoder
///     window, with a possessive written as one token split into its stem and its marker, see
///     <see cref="PossessiveView"/>), and the head reads the vectors of the word and of its previous and next token.
///     The head's posterior over the parts of speech that the lexicon lists for the word, renormalized, is the
///     probability of each part of speech. The probability of a slot is that probability times the share of the slot
///     among the slots of its part of speech: the share of its senses' mass (or of its chooser labels) when its lemma is
///     scored, else equal. A closed-class slot therefore has a probability of its own and no senses. The scores of the
///     senses, and so the sense and the group of an answer and the ranking inside a slot, do not depend on the head. When
///     the best sense lies in a slot that the head does not rank first, the answer is at the
///     <see cref="SenseLevel.PartOfSpeech"/> level.
///     </para>
///     <para>
///     A form whose analyses belong to several lemmas or tenses (<c>left</c>, <c>read</c>) is first resolved by the
///     chooser of the form, a small classifier over the lemmas or analyses of the form with its own threshold; a form of
///     that kind without a chooser is answered from the lexicon. A word of several parts of speech whose senses are not
///     scored (no weights, fewer than two scored senses, no usable vector) has the head's probability for each part of
///     speech, shared equally by its lemmas, the analyses of a slot split it equally (the lexicon order is kept), at the
///     <see cref="SenseLevel.PartOfSpeech"/> level, without scored senses. Such a word is left to the fallback model of a
///     composition for its senses (see <see cref="FallbackWordSenseModel"/>). A word of one part of speech whose senses are
///     not scored, a word without
///     a usable vector, and a classifier without a head (tests and the build tool) are answered from the lexicon alone:
///     every lemma and part of speech has the same probability 1/s for s of them. No fine sense is ever guessed.
///     </para>
///     <para>
///     The classifier holds no mutable state and is thread-safe.
///     </para>
/// </remarks>
internal sealed class OnnxClassifier
{
    private readonly WeightedLemmaTable _table;
    private readonly PooledEncode? _encode;
    private readonly PosHead? _head;
    private readonly PooledViewEncode? _encodeView;
    private readonly TenseHead? _tense;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class that answers from the lexicon alone.
    /// </summary>
    public OnnxClassifier()
        : this(new WeightedLemmaTable(), null)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class that scores senses and has no
    ///     part-of-speech head.
    /// </summary>
    /// <param name="table">The trained weights.</param>
    /// <param name="encode">The encoder of the target words, or null to answer every word from the lexicon alone; its owner is the caller.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="table"/> is null.</exception>
    public OnnxClassifier(WeightedLemmaTable table, PooledEncode? encode)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _encode = encode;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class with a part-of-speech head.
    /// </summary>
    /// <param name="table">The trained weights.</param>
    /// <param name="encode">The encoder of target words, which gives the vector of a possessive that is written as one token; its owner is the caller.</param>
    /// <param name="head">The part-of-speech head of the encoder.</param>
    /// <param name="encodeView">The encoder of every word of a text in the possessive view; its owner is the caller.</param>
    /// <param name="useTense">Whether to judge the tense of every same-spelling verb with the tense head of <paramref name="head"/> (see <see cref="ClassifyWithTenseAsync"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="useTense"/> is true and the head has no tense head.</exception>
    public OnnxClassifier(WeightedLemmaTable table, PooledEncode encode, PosHead head, PooledViewEncode encodeView, bool useTense = false)
        : this(table, encode ?? throw new ArgumentNullException(nameof(encode)))
    {
        _head = head ?? throw new ArgumentNullException(nameof(head));
        _encodeView = encodeView ?? throw new ArgumentNullException(nameof(encodeView));
        _tense = useTense ? head.Tense ?? throw new ArgumentException("The head has no tense head.", nameof(useTense)) : null;
    }

    /// <summary>
    ///     Tells whether any word of a list needs the encoder, and therefore the model files: a word whose senses are
    ///     pending (a trained lemma with several scored senses, or a form chooser), for a classifier with a
    ///     part-of-speech head a known word of two or more parts of speech, and for a classifier with a tense head a
    ///     same-spelling verb.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="table">The trained weights.</param>
    /// <param name="hasHead">Whether the classifier has a part-of-speech head.</param>
    /// <param name="hasTense">Whether the classifier judges the tense of same-spelling verbs.</param>
    /// <returns>True when the model files, the encoder, the weights and the head are needed to classify the list.</returns>
    internal static bool NeedsEncoder(IReadOnlyList<string> words, WeightedLemmaTable table, bool hasHead, bool hasTense = false)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(table);
        return words.Any(w =>
        {
            var plan = Plan(w, table);
            return plan.Pending is not null
                || (plan.Result is { } result
                    && ((hasHead && HasSeveralPartsOfSpeech(result.Analyses.Select(a => a.Details.PartOfSpeech)))
                        || (hasHead && hasTense && TenseFusion.IsSameSpellingVerb(result.Analyses.Select(a => a.Details)))));
        });
    }

    /// <summary>
    ///     Tells whether a list of parts of speech holds two or more distinct ones.
    /// </summary>
    /// <param name="partsOfSpeech">The parts of speech.</param>
    /// <returns>True when at least two of them differ.</returns>
    private static bool HasSeveralPartsOfSpeech(IEnumerable<PartOfSpeech> partsOfSpeech) =>
        partsOfSpeech.Distinct().Skip(1).Any();

    /// <summary>
    ///     Classifies a list that needs no model, using the lexicon alone: no model file, encoder or weights.
    /// </summary>
    /// <param name="words">The words; no word may be pending.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The results, parallel to the words.</returns>
    internal static ClassifyResult[] ResolveWithoutModel(IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        var results = new ClassifyResult[words.Count];
        for (var i = 0; i < results.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = Plan(words[i], null).Result!.Value;
        }

        return results;
    }

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task with an array parallel to <paramref name="words"/>.</returns>
    /// <remarks>
    ///     The words are validated here (a null list or element is rejected with the exception that the model
    ///     reports too) so that the unit keeps its contract when it is used on its own. The token is checked before the
    ///     work starts, so an already canceled token cancels the call even for an empty list.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
        (await ClassifyWithTenseAsync(words, cancellationToken).ConfigureAwait(false)).Results;

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text and judges the tense of the same-spelling verbs.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     The results, parallel to <paramref name="words"/> as for <see cref="ClassifyAsync"/>, and the probability of past
    ///     of every word that is a same-spelling verb (a verb with a past and a non-past analysis) when the classifier has a
    ///     tense head and the word has a usable vector; null for every other word and for every word of a classifier without
    ///     a tense head. The results do not depend on the tense head: the probability is reported beside them.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<(ClassifyResult[] Results, double?[] PastProbability)> ClassifyWithTenseAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var results = new ClassifyResult[list.Count];
        var past = new double?[list.Count];
        var pending = new List<(int Index, Pending Plan)>();
        var headWords = new List<int>();
        var tenseWords = new List<int>();
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = Plan(list[i], _encode is null ? null : _table);
            if (plan.Result is { } result)
            {
                results[i] = result;

                // A known word of several parts of speech, whose senses are not scored, still gets the head's evidence
                if (_head is not null && HasSeveralPartsOfSpeech(result.Analyses.Select(a => a.Details.PartOfSpeech)))
                {
                    headWords.Add(i);
                }

                if (_tense is not null && TenseFusion.IsSameSpellingVerb(result.Analyses.Select(a => a.Details)))
                {
                    tenseWords.Add(i);
                }
            }
            else
            {
                pending.Add((i, plan.Pending!));
                if (_tense is not null && TenseFusion.IsSameSpellingVerb(plan.Pending!.Details))
                {
                    tenseWords.Add(i);
                }
            }
        }

        if (pending.Count == 0 && headWords.Count == 0 && tenseWords.Count == 0)
        {
            return (results, past);
        }

        if (_head is not null)
        {
            await ResolveWithHeadAsync(list, results, past, pending, headWords, tenseWords, cancellationToken).ConfigureAwait(false);
            return (results, past);
        }

        var pooled = await _encode!(list, pending.Select(p => p.Index).ToList(), cancellationToken).ConfigureAwait(false);
        var mean = _table.Data.Mean;
        for (var p = 0; p < pending.Count; p++)
        {
            var (index, plan) = pending[p];
            var feature = pooled[p] is { } vector && vector.Length == mean.Length ? FeatureExtractor.Center(vector, mean) : null;
            results[index] = feature is not null ? Decide(list[index], plan, feature, null) : LexiconOnly(list[index], plan.Details, null);
        }

        return (results, past);
    }

    /// <summary>
    ///     Encodes a text once in the possessive view and answers the pending words and the words of several parts of
    ///     speech with the head's evidence, and judges the tense of the same-spelling verbs.
    /// </summary>
    /// <param name="list">The words.</param>
    /// <param name="results">The results; filled in here for the words that are answered with the head.</param>
    /// <param name="past">The probability of past of every word; filled in here for the same-spelling verbs.</param>
    /// <param name="pending">The words whose senses are scored.</param>
    /// <param name="headWords">The other words of several parts of speech.</param>
    /// <param name="tenseWords">The words that are same-spelling verbs.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    private async Task ResolveWithHeadAsync(List<string> list, ClassifyResult[] results, double?[] past, List<(int Index, Pending Plan)> pending, List<int> headWords, List<int> tenseWords, CancellationToken cancellationToken)
    {
        var head = _head!;
        var view = await _encodeView!(list, cancellationToken).ConfigureAwait(false);
        var features = await FeaturesAsync(list, pending, view, cancellationToken).ConfigureAwait(false);
        for (var p = 0; p < pending.Count; p++)
        {
            var (index, plan) = pending[p];
            var posterior = HasSeveralPartsOfSpeech(plan.Slots.Select(s => s.PartOfSpeech)) ? PosteriorOf(head, view, index, plan.Details) : null;
            if (features[p] is { } feature)
            {
                results[index] = Decide(list[index], plan, feature, posterior);
            }
            else
            {
                results[index] = LexiconOnly(list[index], plan.Details, posterior);
            }
        }

        foreach (var index in headWords)
        {
            var details = results[index].Analyses.Select(a => a.Details).ToImmutableArray();
            if (PosteriorOf(head, view, index, details) is { } posterior)
            {
                results[index] = LexiconOnly(list[index], details, posterior);
            }
        }

        foreach (var index in tenseWords)
        {
            past[index] = PastProbabilityOf(_tense!, head.HiddenSize, view, index);
        }
    }

    /// <summary>
    ///     Computes the probability that a same-spelling verb is past.
    /// </summary>
    /// <param name="tense">The tense head.</param>
    /// <param name="hiddenSize">The hidden size of the encoder.</param>
    /// <param name="view">The text in the possessive view.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>The probability of past, or null when the word has no usable vector (the neighbors count only for the neighbor layout).</returns>
    private static double? PastProbabilityOf(TenseHead tense, int hiddenSize, WordVectors view, int index)
    {
        var at = view.Start[index];
        var word = view.Vectors[at];
        var previous = at > 0 ? view.Vectors[at - 1] : null;
        var next = at + 1 < view.Vectors.Length ? view.Vectors[at + 1] : null;
        if (word is null || word.Length != hiddenSize || (previous is not null && previous.Length != hiddenSize) || (next is not null && next.Length != hiddenSize))
        {
            return null;
        }

        var probability = tense.PastProbability(previous, word, next);
        return double.IsFinite(probability) ? probability : null;
    }

    /// <summary>
    ///     Gets the centered, unit-length feature of every pending word: the vector of the word as it is pooled over
    ///     all its word pieces (what the weights were trained on).
    /// </summary>
    /// <param name="list">The words.</param>
    /// <param name="pending">The pending words.</param>
    /// <param name="view">The text in the possessive view.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One feature per pending word, or null for a word without a usable vector.</returns>
    /// <remarks>
    ///     A word that the view keeps whole has the vector of the view. A possessive that is written as one token is split
    ///     in the view (its stem and its marker have vectors of their own, for the head), so its vector, pooled over all
    ///     its pieces, comes from a second pass of the encoder.
    /// </remarks>
    private async Task<float[]?[]> FeaturesAsync(List<string> list, List<(int Index, Pending Plan)> pending, WordVectors view, CancellationToken cancellationToken)
    {
        var vectors = new double[]?[pending.Count];
        var split = new List<int>();
        for (var p = 0; p < pending.Count; p++)
        {
            var index = pending[p].Index;
            var end = index + 1 < view.Start.Length ? view.Start[index + 1] : view.Tokens.Count;
            if (end - view.Start[index] > 1)
            {
                split.Add(p);
            }
            else
            {
                vectors[p] = view.Vectors[view.Start[index]];
            }
        }

        if (split.Count > 0)
        {
            var joined = await _encode!(list, split.Select(p => pending[p].Index).ToList(), cancellationToken).ConfigureAwait(false);
            for (var s = 0; s < split.Count; s++)
            {
                vectors[split[s]] = joined[s];
            }
        }

        var mean = _table.Data.Mean;
        return [.. vectors.Select(v => v is not null && v.Length == mean.Length ? FeatureExtractor.Center(v, mean) : null)];
    }

    /// <summary>
    ///     Computes the posterior of the parts of speech of a word that the lexicon lists for it.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="view">The text in the possessive view.</param>
    /// <param name="index">The index of the word.</param>
    /// <param name="details">The analyses of the word.</param>
    /// <returns>The posterior restricted to the parts of speech of the analyses and renormalized (indexed by <see cref="PartOfSpeech"/>), or null when the word has no usable vector or the head gives no usable distribution over those parts of speech (the word is then answered without the head).</returns>
    private static double[]? PosteriorOf(PosHead head, WordVectors view, int index, ImmutableArray<WordDetails> details)
    {
        var at = view.Start[index];
        var word = view.Vectors[at];
        var previous = at > 0 ? view.Vectors[at - 1] : null;
        var next = at + 1 < view.Vectors.Length ? view.Vectors[at + 1] : null;
        if (word is null || word.Length != head.HiddenSize || (previous is not null && previous.Length != head.HiddenSize) || (next is not null && next.Length != head.HiddenSize))
        {
            return null;
        }

        var mask = 0;
        foreach (var analysis in details)
        {
            mask |= 1 << (int)analysis.PartOfSpeech;
        }

        return head.RestrictedPosterior(previous, word, next, mask);
    }

    /// <summary>
    ///     Decides how a word is answered: from the lexicon at once, or by the encoder.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="table">The trained weights, or null when the caller knows that no word needs the encoder.</param>
    /// <returns>The final result, or the pending state of a word that needs a vector (or the lexicon-only analyses).</returns>
    private static (ClassifyResult? Result, Pending? Pending) Plan(string word, WeightedLemmaTable? table)
    {
        if (!WordLexicon.TryLookup(word, out var details) || details.IsEmpty)
        {
            return (ClassifyResult.Unknown(word), null);
        }

        // Group the analyses by lemma and part of speech: they share their senses
        var slots = GroupIntoSlots(details);

        // A word with one analysis is certain about it; its sense is named only when its lemma and part of speech have
        // exactly one sense (nothing was scored, nothing is guessed)
        if (details.Length == 1 && slots[0].Senses.Length == 1)
        {
            var sense = slots[0].Senses[0];
            return (new ClassifyResult(word, true, [new AnalysisCandidate(details[0], 1.0, [new SenseCandidate(sense.SenseId, 1.0)])], details[0], SenseLevel.Sense, 1.0, sense.GroupId, sense.SenseId), null);
        }

        if (table is null)
        {
            return (LexiconOnly(word, details), null);
        }

        var chooser = ChooserOf(table, word, details);
        var runs = RunsEncoder(slots, chooser is not null, lemma => table.Find(lemma) is { } resolved ? CandidateColumns(resolved, slots.Where(s => s.Lemma == lemma)).Count : 0);
        return runs ? (null, new Pending(details, slots, table, chooser)) : (LexiconOnly(word, details), null);
    }

    /// <summary>
    ///     Decides whether the encoder runs for a word that has several analyses or several senses: the one rule that
    ///     inference and the build tool share.
    /// </summary>
    /// <param name="slots">The slots of the word.</param>
    /// <param name="hasChooser">Whether the form of the word has a chooser.</param>
    /// <param name="candidateCountOf">Gives the number of candidate senses that the classifier scores for a lemma of the word (0 for a lemma without weights).</param>
    /// <returns>True when the encoder runs: a form of several lemmas needs its chooser (the lexicon answers it otherwise), and a form of one lemma needs a chooser or a lemma with at least two candidates.</returns>
    private static bool RunsEncoder(List<Slot> slots, bool hasChooser, Func<string, int> candidateCountOf)
    {
        var lemmas = slots.Select(s => s.Lemma).Distinct(StringComparer.Ordinal).ToList();
        if (lemmas.Count > 1 && !hasChooser)
        {
            return false;
        }

        return hasChooser || lemmas.Any(lemma => candidateCountOf(lemma) >= 2);
    }

    /// <summary>
    ///     Tells whether the encoder runs for the form of a word, before any weights exist for the other lemmas of the form:
    ///     the build tool uses it to leave out the anchors that inference never scores (see <see cref="RunsEncoder"/>).
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="lemma">The lemma that is being trained.</param>
    /// <param name="candidateCount">The number of candidate columns of the lemma for the form of the word (see <see cref="FormColumns"/>).</param>
    /// <param name="formHasChooser">Tells whether a normalized form gets a chooser.</param>
    /// <returns>True when inference scores the lemma for the word: the lexicon has it, it is not a one-analysis, one-sense word, and the encoder runs.</returns>
    /// <remarks>
    ///     A chooser counts for the word when its form has exactly the analyses of the word, as at run time. The lemma
    ///     being trained is the only one whose weights are consulted: a form of several lemmas runs the encoder with a
    ///     chooser whatever the weights of the other lemmas, and a form of one lemma has no other lemma.
    /// </remarks>
    internal static bool EncoderRuns(string word, string lemma, int candidateCount, Func<string, bool> formHasChooser)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(formHasChooser);
        if (!WordLexicon.TryLookup(word, out var details) || details.IsEmpty)
        {
            return false;
        }

        var slots = GroupIntoSlots(details);
        if (details.Length == 1 && slots[0].Senses.Length == 1)
        {
            return false;
        }

        var key = word.ToLowerInvariant().Replace('\u2019', '\'');
        var hasChooser = formHasChooser(key) && WordLexicon.TryLookup(key, out var keyDetails) && keyDetails.SequenceEqual(details);
        return RunsEncoder(slots, hasChooser, other => string.Equals(other, lemma, StringComparison.Ordinal) ? candidateCount : 0);
    }

    /// <summary>
    ///     Groups the analyses of a word by lemma and part of speech, in the order of their first analysis.
    /// </summary>
    /// <param name="details">The analyses of the word.</param>
    /// <returns>The slots, each with the indexes of its analyses.</returns>
    private static List<Slot> GroupIntoSlots(ImmutableArray<WordDetails> details)
    {
        var slots = new List<Slot>();
        for (var i = 0; i < details.Length; i++)
        {
            var slot = slots.Find(s => s.Lemma == details[i].Lemma && s.PartOfSpeech == details[i].PartOfSpeech);
            if (slot is null)
            {
                slot = new Slot(details[i].Lemma, details[i].PartOfSpeech, WordLexicon.SensesOf(details[i].Lemma, details[i].PartOfSpeech));
                slots.Add(slot);
            }

            slot.Analyses.Add(i);
        }

        return slots;
    }

    /// <summary>
    ///     Finds the chooser of a word: the chooser of its normalized form when that form has exactly the analyses of the word.
    /// </summary>
    /// <param name="table">The trained weights.</param>
    /// <param name="word">The word.</param>
    /// <param name="details">The analyses of the word.</param>
    /// <returns>The chooser, or null when the word has none.</returns>
    private static FormChooser? ChooserOf(WeightedLemmaTable table, string word, ImmutableArray<WordDetails> details)
    {
        var key = word.ToLowerInvariant().Replace('\u2019', '\'');
        return table.FindChooser(key) is { } found && WordLexicon.TryLookup(key, out var keyDetails) && keyDetails.SequenceEqual(details) ? found : null;
    }

    /// <summary>
    ///     Lists the columns of a trained lemma that score the senses of the analyses of a word.
    /// </summary>
    /// <param name="lemma">The trained lemma.</param>
    /// <param name="slots">The lemma and part of speech slots of the word that belong to the lemma.</param>
    /// <returns>The column indexes in ascending (inventory) order.</returns>
    /// <remarks>
    ///     The slots of a closed class (an auxiliary, determiner, pronoun, preposition, conjunction or interjection: no
    ///     WordNet senses) are not scored: they have no senses to rank, and the probability of such a slot is the
    ///     part-of-speech probability of the head.
    /// </remarks>
    private static List<int> CandidateColumns(ResolvedLemma lemma, IEnumerable<Slot> slots) =>
        OpenClassColumns(lemma.ColumnOf, lemma.Weights.Lemma, slots.Select(s => s.PartOfSpeech));

    /// <summary>
    ///     Lists the columns of a trained lemma that are scored for a word: the senses of the open-class parts of speech
    ///     that the word has for the lemma, and no sense of a closed class.
    /// </summary>
    /// <param name="columnOf">Gives the column of a sense id of the lemma, or a negative number for a sense without a column.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="partsOfSpeech">The parts of speech that the word has for the lemma.</param>
    /// <returns>The column indexes in ascending (inventory) order; the build tool uses the same rule to calibrate the thresholds.</returns>
    internal static List<int> OpenClassColumns(Func<string, int> columnOf, string lemma, IEnumerable<PartOfSpeech> partsOfSpeech)
    {
        ArgumentNullException.ThrowIfNull(columnOf);
        ArgumentNullException.ThrowIfNull(partsOfSpeech);
        var columns = new List<int>();
        foreach (var partOfSpeech in partsOfSpeech.Distinct().Where(p => !SenseFusion.IsClosedClass(p)))
        {
            foreach (var sense in WordLexicon.SensesOf(lemma, partOfSpeech))
            {
                if (columnOf(sense.SenseId) is var column and >= 0)
                {
                    columns.Add(column);
                }
            }
        }

        columns.Sort();
        return columns;
    }

    /// <summary>
    ///     Lists the columns of a trained lemma that the classifier scores for the form of a word, with no table: the
    ///     open-class senses of the parts of speech that the form has for the lemma (see <see cref="OpenClassColumns"/>).
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="columnOf">Gives the column of a sense id of the lemma, or a negative number for a sense without a column.</param>
    /// <returns>The columns in ascending order (fewer than two when the classifier would not score the form), or null when the word is not in the lexicon.</returns>
    /// <remarks>
    ///     The build tool uses this rule, and <see cref="CandidatesOf"/> and <see cref="ScoreForm"/> for the parts that
    ///     need the weights, for everything that it measures about the scores of a form (the thresholds, the calibration
    ///     and the half-precision gate), so training scores a row with the candidates that inference ranks.
    /// </remarks>
    internal static List<int>? FormColumns(string word, string lemma, Func<string, int> columnOf)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(columnOf);
        if (!WordLexicon.TryLookup(word, out var details) || details.IsEmpty)
        {
            return null;
        }

        return OpenClassColumns(columnOf, lemma, details.Where(d => string.Equals(d.Lemma, lemma, StringComparison.Ordinal)).Select(d => d.PartOfSpeech));
    }

    /// <summary>
    ///     Tells what the classifier does with the form of a word for a trained lemma: whether the encoder runs for the form,
    ///     and the columns of the lemma that it scores.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="table">The trained weights.</param>
    /// <returns>The plan: the encoder runs when the form has a chooser or a scored lemma (a form of several lemmas without a chooser, a form of one analysis and one sense and a form that no lemma scores are answered from the lexicon); the columns are those of the lemma (fewer than two when the lemma is not scored for the form).</returns>
    internal static (bool EncoderRuns, IReadOnlyList<int> Columns) CandidatesOf(string word, string lemma, WeightedLemmaTable table)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(table);
        var columns = table.Find(lemma) is { } resolved ? FormColumns(word, lemma, resolved.ColumnOf) ?? [] : [];
        return (Plan(word, table).Pending is not null, columns);
    }

    /// <summary>
    ///     Whether the threshold of a lemma decides the level of the answer for a word, and if not, why not: every way
    ///     in which inference answers a scored word without consulting the threshold of the lemma.
    /// </summary>
    internal enum FormOutcome
    {
        /// <summary>The classifier does not run the encoder for the form (an unknown word, a word of one analysis and one sense, a form of several lemmas without a chooser, a form that no lemma scores): the lexicon answers.</summary>
        EncoderDoesNotRun,

        /// <summary>The encoder runs but does not score the lemma for the form (fewer than two candidate senses): its senses are not ranked.</summary>
        LemmaNotScored,

        /// <summary>The margin of the form chooser is below its threshold: the answer is at the part-of-speech level.</summary>
        ChooserNotConfident,

        /// <summary>The selected analysis belongs to another lemma: the threshold of that lemma decides, not this one.</summary>
        SelectedElsewhere,

        /// <summary>The best sense is of another part of speech than the selected analysis: the answer is at the part-of-speech level.</summary>
        BestOutsideSelectedPartOfSpeech,

        /// <summary>The thresholds of the lemma decide between the sense, the group and the part-of-speech level.</summary>
        ThresholdDecides,
    }

    /// <summary>
    ///     What the classifier works out for a word and a lemma before any threshold decides the level.
    /// </summary>
    /// <param name="Outcome">Whether the threshold of the lemma decides the level of the answer, or why it does not. The other members are meaningful only for <see cref="FormOutcome.ThresholdDecides"/>; otherwise they are empty or not a number.</param>
    /// <param name="Result">The result as the classifier builds it from the plan of the word (every analysis of the form, the form chooser, the slot masses), at the part-of-speech level: the analyses, their probabilities and the selected analysis are those of the real answer.</param>
    /// <param name="Columns">The columns of the lemma that are scored for the form.</param>
    /// <param name="BestColumn">The column of the best sense.</param>
    /// <param name="FineMargin">The margin of the best score over the second best among the candidates.</param>
    /// <param name="GroupMargin">The group margin of the best sense among the candidates.</param>
    internal sealed record FormScoring(FormOutcome Outcome, ClassifyResult Result, IReadOnlyList<int> Columns, int BestColumn, float FineMargin, double GroupMargin)
    {
        /// <summary>
        ///     Gets a value that tells whether the threshold of the lemma decides the level of the answer.
        /// </summary>
        public bool ThresholdDecides => Outcome == FormOutcome.ThresholdDecides;

        /// <summary>
        ///     Builds the scoring of a word that inference does not answer by the thresholds of the lemma, for a reason that precedes the thresholds.
        /// </summary>
        /// <param name="outcome">The outcome, not <see cref="FormOutcome.ThresholdDecides"/>.</param>
        /// <returns>The scoring without a result.</returns>
        public static FormScoring Without(FormOutcome outcome) => new(outcome, default, [], -1, float.NaN, double.NaN);
    }

    /// <summary>
    ///     Scores a word for a trained lemma exactly as <see cref="ClassifyAsync"/> does, without the thresholds, and tells
    ///     whether the threshold of the lemma decides the level of the answer.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="lemma">The lemma whose classifier is read.</param>
    /// <param name="table">The trained weights.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <returns>The scoring, with the outcome that inference reaches for the word (see <see cref="FormOutcome"/>).</returns>
    /// <remarks>
    ///     Inference and the build tool share the decision: <c>Decide</c> answers at the part-of-speech level for every
    ///     outcome but <see cref="FormOutcome.ThresholdDecides"/>, and the calibration observes only the rows of that outcome.
    /// </remarks>
    internal static FormScoring ScoreForm(string word, string lemma, WeightedLemmaTable table, float[] feature)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(feature);
        if (Plan(word, table).Pending is not { } plan)
        {
            return FormScoring.Without(FormOutcome.EncoderDoesNotRun);
        }

        var weighed = Weigh(plan, feature);
        if (!weighed.LemmaScores.TryGetValue(lemma, out var scores))
        {
            return FormScoring.Without(FormOutcome.LemmaNotScored);
        }

        var outcome = Judge(weighed, lemma);
        if (outcome != FormOutcome.ThresholdDecides)
        {
            return FormScoring.Without(outcome);
        }

        var result = PartOfSpeechAnswer(word, weighed.Analyses, weighed.Selected, weighed.SelectedProbability);
        return new FormScoring(outcome, result, [.. scores.Columns], scores.Columns[scores.Best], scores.FineMargin, scores.GroupMargin);
    }

    /// <summary>
    ///     Says whether the threshold of a lemma decides the level of the answer of a weighed word: the one decision that
    ///     <see cref="Decide"/> and <see cref="ScoreForm"/> share.
    /// </summary>
    /// <param name="weighed">The weighed word.</param>
    /// <param name="lemma">The lemma whose threshold is asked about.</param>
    /// <returns>The outcome, in the order in which inference checks it.</returns>
    private static FormOutcome Judge(Weighed weighed, string lemma)
    {
        if (!weighed.ChooserConfident)
        {
            return FormOutcome.ChooserNotConfident;
        }

        if (!string.Equals(weighed.SelectedSlot.Lemma, lemma, StringComparison.Ordinal))
        {
            return FormOutcome.SelectedElsewhere;
        }

        if (!weighed.LemmaScores.TryGetValue(lemma, out var scores))
        {
            return FormOutcome.LemmaNotScored;
        }

        return scores.Senses[scores.Best].PartOfSpeech == weighed.SelectedSlot.PartOfSpeech
            ? FormOutcome.ThresholdDecides
            : FormOutcome.BestOutsideSelectedPartOfSpeech;
    }

    /// <summary>
    ///     Tells whether a posterior gives the listed parts of speech a positive, finite total probability.
    /// </summary>
    /// <param name="posterior">The posterior, indexed by <see cref="PartOfSpeech"/>.</param>
    /// <param name="partsOfSpeech">The parts of speech of the word.</param>
    /// <returns>True when the posterior can be used for the word.</returns>
    private static bool HasMass(double[] posterior, IEnumerable<PartOfSpeech> partsOfSpeech)
    {
        var total = partsOfSpeech.Distinct().Sum(p => posterior[(int)p]);
        return double.IsFinite(total) && total > 0;
    }

    /// <summary>
    ///     Answers a word without scored senses: from the lexicon alone, or from the part-of-speech head.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="details">The analyses of the word, at least one.</param>
    /// <param name="posterior">The head's posterior of the parts of speech of the word (restricted to them, indexed by <see cref="PartOfSpeech"/>), or null for the lexicon alone.</param>
    /// <returns>The result at the part of speech level without scored senses.</returns>
    /// <remarks>
    ///     The probability is shared out by lemma and part of speech, not by analysis. Without a posterior every lemma
    ///     and part of speech has the same probability. With one, a part of speech has the probability of the head and
    ///     its lemmas share it equally. The analyses that differ only in features share the probability of their slot
    ///     equally. The analyses are listed by slot, the most probable slot first, in the order of the lexicon for equal
    ///     probabilities (a slot is placed where its first analysis is), so the selected analysis is the first one of the
    ///     most probable slot and the confidence is the probability of its slot.
    /// </remarks>
    private static ClassifyResult LexiconOnly(string word, ImmutableArray<WordDetails> details, double[]? posterior = null)
    {
        var slots = new List<List<int>>();
        for (var i = 0; i < details.Length; i++)
        {
            var slot = slots.Find(s => details[s[0]].Lemma == details[i].Lemma && details[s[0]].PartOfSpeech == details[i].PartOfSpeech);
            if (slot is null)
            {
                slot = [];
                slots.Add(slot);
            }

            slot.Add(i);
        }

        // A posterior that gives none of the listed parts of speech any probability cannot be used: the lexicon alone decides
        if (posterior is not null && !HasMass(posterior, details.Select(d => d.PartOfSpeech)))
        {
            posterior = null;
        }

        var probability = slots
            .Select(s => posterior is null ? 1.0 / slots.Count : posterior[(int)details[s[0]].PartOfSpeech] / slots.Count(o => details[o[0]].PartOfSpeech == details[s[0]].PartOfSpeech))
            .ToArray();
        var order = Enumerable.Range(0, slots.Count).OrderByDescending(k => probability[k]).ThenBy(k => k).ToArray();
        var analyses = ImmutableArray.CreateBuilder<AnalysisCandidate>(details.Length);
        foreach (var k in order)
        {
            foreach (var i in slots[k])
            {
                analyses.Add(new AnalysisCandidate(details[i], probability[k] / slots[k].Count, []));
            }
        }

        return new ClassifyResult(word, true, analyses.MoveToImmutable(), details[slots[order[0]][0]], SenseLevel.PartOfSpeech, probability[order[0]], null, null);
    }

    /// <summary>
    ///     Scores a pending word with its feature and decides the answer.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="plan">The pending word.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <param name="posterior">The head's posterior of the parts of speech of the word, or null when no head decides them.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    ///     The senses belong to a lemma and part of speech (a slot), not to the analyses that differ only in features, so
    ///     the probabilities are worked out per slot: the probability of a slot is the mass of its senses (or, for a form
    ///     with a chooser, the share of the chooser's labels that its analyses carry), the probability of a sense given
    ///     its slot is its mass renormalized in the slot, and the confidence is the probability of the slot times that.
    ///     With a head, the probability of a part of speech is the head's and the slots of one part of speech share it by
    ///     those masses (see <see cref="Weigh"/>).
    ///     The analyses of one slot split the probability of the slot: by the chooser when it has a label for each of
    ///     them, otherwise equally. The selected analysis is the best one of the most probable slot.
    /// </remarks>
    private static ClassifyResult Decide(string word, Pending plan, float[] feature, double[]? posterior)
    {
        var weighed = Weigh(plan, feature, posterior);
        var selectedSlot = weighed.SelectedSlot;
        var analysisProbability = weighed.SelectedProbability;

        // Only when the threshold of the lemma of the selected analysis decides does a level above the part of speech
        // exist (the same decision as the calibration observes: see ScoreForm)
        if (Judge(weighed, selectedSlot.Lemma) != FormOutcome.ThresholdDecides)
        {
            return PartOfSpeechAnswer(word, weighed.Analyses, weighed.Selected, analysisProbability);
        }

        // The level of the selected analysis, with the thresholds of its lemma
        return AnswerAtLevel(word, weighed.Analyses, weighed.Selected, selectedSlot, analysisProbability, weighed.LemmaScores[selectedSlot.Lemma]);
    }

    /// <summary>
    ///     Works out everything that does not depend on a threshold for a pending word: the scored lemmas, the probability of
    ///     every analysis, the order of the analyses and the selected analysis.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <param name="posterior">The head's posterior of the parts of speech of the word (restricted to them, indexed by <see cref="PartOfSpeech"/>), or null when no head decides them.</param>
    /// <returns>The weighed word.</returns>
    /// <remarks>
    ///     With a posterior the probability of a part of speech is the head's, <c>P(slot) = P_pos(part of speech) ×
    ///     share(slot | part of speech)</c>: the share is the probability that the senses (or the chooser) give the slot,
    ///     renormalized among the slots of its part of speech (equal when they give those slots none). The scores of the
    ///     senses, and so the sense and group of an answer and their probabilities within a slot, do not change. The
    ///     build tool passes no posterior, so training and calibration see the sense masses alone.
    /// </remarks>
    private static Weighed Weigh(Pending plan, float[] feature, double[]? posterior = null)
    {
        var details = plan.Details;
        var lemmaScores = ScoreLemmas(plan, feature);

        // Probability of every analysis: the chooser splits the mass among the lemmas or tenses of a form, the senses
        // split the mass of one lemma among its parts of speech (its slots); the analyses of a slot split its mass
        var weightOf = new double[details.Length];
        var chooserConfident = true;
        if (plan.Chooser is { } chooser)
        {
            chooserConfident = WeighByChooser(plan, chooser, feature, lemmaScores, weightOf);
        }
        else
        {
            WeighBySlots(plan, lemmaScores, weightOf);
        }

        Normalize(plan, weightOf);
        if (posterior is not null && HasMass(posterior, plan.Slots.Select(s => s.PartOfSpeech)))
        {
            ApplyPartOfSpeech(plan, weightOf, posterior);
            Normalize(plan, weightOf);
        }

        // The probability of every slot, and the order: the most probable slot first (ties keep the lexicon order of the
        // first analysis of the slot), and in a slot the most probable analysis first (ties keep the lexicon order)
        var slotProbability = plan.Slots.ToDictionary(s => s, s => s.Analyses.Sum(i => weightOf[i]));
        var order = Enumerable.Range(0, details.Length)
            .OrderByDescending(i => slotProbability[plan.SlotOf(i)])
            .ThenBy(i => plan.Slots.IndexOf(plan.SlotOf(i)))
            .ThenByDescending(i => weightOf[i])
            .ThenBy(i => i)
            .ToArray();
        var analyses = ImmutableArray.CreateBuilder<AnalysisCandidate>(details.Length);
        foreach (var i in order)
        {
            analyses.Add(new AnalysisCandidate(details[i], weightOf[i], RankedSenses(plan.SlotOf(i), lemmaScores)));
        }

        var selected = order[0];
        var selectedSlot = plan.SlotOf(selected);
        return new Weighed(analyses.MoveToImmutable(), details[selected], selectedSlot, slotProbability[selectedSlot], chooserConfident, lemmaScores);
    }

    /// <summary>
    ///     Scores the senses of every weighted lemma of a pending word: a softmax over the senses of its analyses only.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <returns>The scores of the lemmas that have at least two candidate senses, by lemma.</returns>
    private static Dictionary<string, LemmaScores> ScoreLemmas(Pending plan, float[] feature)
    {
        var lemmaScores = new Dictionary<string, LemmaScores>(StringComparer.Ordinal);
        foreach (var lemma in plan.Slots.Select(s => s.Lemma).Distinct(StringComparer.Ordinal))
        {
            // The dimension check is a defensive guard: through the model the encoder is checked against the weights'
            // dimension when it is opened, so a mismatch is reported there as ModelIntegrityException, never silently here
            if (plan.Table.Find(lemma) is { } resolved && resolved.Weights.Weights.Dimension == feature.Length)
            {
                var columns = CandidateColumns(resolved, plan.Slots.Where(s => s.Lemma == lemma));
                if (columns.Count >= 2)
                {
                    lemmaScores[lemma] = LemmaScores.Compute(resolved, columns, feature);
                }
            }
        }

        return lemmaScores;
    }

    /// <summary>
    ///     Works out the probability of every analysis of a form that has a chooser.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="chooser">The chooser of the form.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <param name="weightOf">The probability of every analysis; filled in here.</param>
    /// <returns>True when the margin of the chooser reaches its threshold.</returns>
    private static bool WeighByChooser(Pending plan, FormChooser chooser, float[] feature, Dictionary<string, LemmaScores> lemmaScores, double[] weightOf)
    {
        var details = plan.Details;
        var labelScores = new float[chooser.Weights.SenseCount];
        chooser.Weights.Scores(feature, labelScores);
        var labelProbability = LemmaWeights.Softmax(labelScores);
        var confident = LemmaWeights.Rank(labelScores).Margin >= chooser.Weights.FineThreshold;
        var members = new List<int>?[labelProbability.Length];
        for (var i = 0; i < details.Length; i++)
        {
            var label = LabelOf(chooser, details[i]);
            (members[label] ??= []).Add(i);
        }

        for (var label = 0; label < members.Length; label++)
        {
            if (members[label] is { } indexes)
            {
                SpreadLabelProbability(plan, indexes, labelProbability[label], lemmaScores, weightOf);
            }
        }

        return confident;
    }

    /// <summary>
    ///     Shares the probability of one label of a chooser among the analyses that the label stands for.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="indexes">The analyses that the label stands for.</param>
    /// <param name="labelProbability">The probability of the label.</param>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <param name="weightOf">The probability of every analysis; filled in here.</param>
    /// <remarks>
    ///     The label shares its probability among the slots of its analyses by the mass of their senses, and a slot
    ///     shares it equally among its analyses that the label stands for. A closed-class slot has no mass and never
    ///     receives any, not even when no slot of the label has mass: the label then shares its probability equally among
    ///     its open-class slots, and a label that stands for closed-class slots only is dropped (its probability goes
    ///     to no slot, and the other labels are renormalized with the analyses).
    /// </remarks>
    private static void SpreadLabelProbability(Pending plan, List<int> indexes, double labelProbability, Dictionary<string, LemmaScores> lemmaScores, double[] weightOf)
    {
        var slotsOfLabel = indexes.Select(plan.SlotOf).Distinct().ToList();
        var masses = slotsOfLabel.Select(s => SlotMass(lemmaScores, s)).ToArray();
        var total = masses.Sum();
        var openSlots = slotsOfLabel.Count(s => !SenseFusion.IsClosedClass(s.PartOfSpeech));
        for (var k = 0; k < slotsOfLabel.Count; k++)
        {
            var inSlot = indexes.Where(i => plan.SlotOf(i) == slotsOfLabel[k]).ToList();
            var share = total > 0 ? masses[k] / total : EqualShare(slotsOfLabel[k], openSlots);
            foreach (var i in inSlot)
            {
                weightOf[i] = labelProbability * share / inSlot.Count;
            }
        }
    }

    /// <summary>
    ///     Works out the probability of every analysis of a word without a chooser: the senses split the mass among the slots.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <param name="weightOf">The probability of every analysis; filled in here.</param>
    private static void WeighBySlots(Pending plan, Dictionary<string, LemmaScores> lemmaScores, double[] weightOf)
    {
        var masses = plan.Slots.Select(s => SlotMass(lemmaScores, s)).ToArray();
        var total = masses.Sum();
        var openSlots = plan.Slots.Count(s => !SenseFusion.IsClosedClass(s.PartOfSpeech));
        for (var s = 0; s < plan.Slots.Count; s++)
        {
            foreach (var i in plan.Slots[s].Analyses)
            {
                weightOf[i] = (total > 0 ? masses[s] / total : EqualShare(plan.Slots[s], openSlots)) / plan.Slots[s].Analyses.Count;
            }
        }
    }

    /// <summary>
    ///     Replaces the probability of every part of speech of a weighed word by the head's, keeping the shares of its slots.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="weightOf">The probability of every analysis, summing to one; rewritten here.</param>
    /// <param name="posterior">The head's posterior, indexed by <see cref="PartOfSpeech"/>.</param>
    /// <remarks>
    ///     A slot gets <c>P_pos × share</c>, where the share is the slot's probability among the slots of its part of
    ///     speech (equal when those slots have none: a closed-class slot has no senses to give it any). The analyses of a
    ///     slot keep their shares of it.
    /// </remarks>
    private static void ApplyPartOfSpeech(Pending plan, double[] weightOf, double[] posterior)
    {
        var slots = plan.Slots;
        var slotWeight = slots.Select(s => s.Analyses.Sum(i => weightOf[i])).ToArray();
        foreach (var partOfSpeech in slots.Select(s => s.PartOfSpeech).Distinct())
        {
            var members = Enumerable.Range(0, slots.Count).Where(k => slots[k].PartOfSpeech == partOfSpeech).ToArray();
            var total = members.Sum(k => slotWeight[k]);
            foreach (var k in members)
            {
                var share = total > 0 ? slotWeight[k] / total : 1.0 / members.Length;
                var slotProbability = posterior[(int)partOfSpeech] * share;
                foreach (var i in slots[k].Analyses)
                {
                    weightOf[i] = slotProbability * (slotWeight[k] > 0 ? weightOf[i] / slotWeight[k] : 1.0 / slots[k].Analyses.Count);
                }
            }
        }
    }

    /// <summary>
    ///     Gets the share of a slot when no slot has mass: equal among the open-class slots, none for a closed-class slot
    ///     (it has no senses to give it mass; with a head its part-of-speech probability is applied afterwards).
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="openSlots">The number of open-class slots that share the probability.</param>
    /// <returns>The share.</returns>
    private static double EqualShare(Slot slot, int openSlots) =>
        SenseFusion.IsClosedClass(slot.PartOfSpeech) || openSlots == 0 ? 0.0 : 1.0 / openSlots;

    /// <summary>
    ///     Decides the level of the answer for the selected analysis, with the thresholds of its lemma.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="analyses">The analyses with their probabilities, the most probable first.</param>
    /// <param name="selected">The selected analysis.</param>
    /// <param name="selectedSlot">The slot of the selected analysis.</param>
    /// <param name="analysisProbability">The probability of the slot of the selected analysis.</param>
    /// <param name="scores">The scores of the lemma of the selected analysis.</param>
    /// <returns>The result.</returns>
    private static ClassifyResult AnswerAtLevel(string word, ImmutableArray<AnalysisCandidate> analyses, WordDetails selected, Slot selectedSlot, double analysisProbability, LemmaScores scores)
    {
        var best = scores.Best;
        var bestSense = scores.Senses[best];
        var inSelected = bestSense.PartOfSpeech == selectedSlot.PartOfSpeech;
        var kind = scores.Lemma.Weights.Weights.Level(scores.FineMargin, inSelected, bestSense.GroupId is not null, scores.GroupMargin);
        switch (kind)
        {
            case AnswerKind.Sense:
                return new ClassifyResult(word, true, analyses, selected, SenseLevel.Sense, analysisProbability * scores.WithinSlot(best, selectedSlot), bestSense.GroupId, bestSense.SenseId);
            case AnswerKind.Group:
                return new ClassifyResult(word, true, analyses, selected, SenseLevel.Group, analysisProbability * scores.GroupWithinSlot(best, selectedSlot), bestSense.GroupId, null);
            default:
                return PartOfSpeechAnswer(word, analyses, selected, analysisProbability);
        }
    }

    private static ClassifyResult PartOfSpeechAnswer(string word, ImmutableArray<AnalysisCandidate> analyses, WordDetails selected, double probability) =>
        new(word, true, analyses, selected, SenseLevel.PartOfSpeech, probability, null, null);

    /// <summary>
    ///     Gets the mass of the senses of a slot: 0 for a slot of a closed class (its senses are never scored, so it has no
    ///     mass of its own, whether or not its lemma is scored; with a head its part-of-speech probability is applied
    ///     afterwards), and 1 for an open-class slot whose senses are not scored (an untrained lemma).
    /// </summary>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The mass.</returns>
    private static double SlotMass(Dictionary<string, LemmaScores> lemmaScores, Slot slot)
    {
        if (SenseFusion.IsClosedClass(slot.PartOfSpeech))
        {
            return 0.0;
        }

        return lemmaScores.TryGetValue(slot.Lemma, out var scores) ? scores.MassOf(slot) : 1.0;
    }

    /// <summary>
    ///     Lists the senses of an analysis ranked by their probability given the analysis.
    /// </summary>
    /// <param name="slot">The lemma and part of speech of the analysis.</param>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <returns>The ranked senses, empty when the lemma was not scored.</returns>
    private static ImmutableArray<SenseCandidate> RankedSenses(Slot slot, Dictionary<string, LemmaScores> lemmaScores) =>
        lemmaScores.TryGetValue(slot.Lemma, out var scores) ? scores.Ranked(slot) : [];

    /// <summary>
    ///     Finds the label of a chooser that stands for an analysis: the label of its lemma and features, else of its lemma.
    /// </summary>
    /// <param name="chooser">The chooser.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The index of the label.</returns>
    /// <exception cref="InvalidDataException">Thrown when no label stands for the analysis.</exception>
    private static int LabelOf(FormChooser chooser, WordDetails analysis)
    {
        var exact = chooser.Labels.ToList().FindIndex(l => string.Equals(l, analysis.Lemma + "/" + analysis.Features, StringComparison.Ordinal));
        var label = exact >= 0 ? exact : chooser.Labels.ToList().FindIndex(l => string.Equals(l, analysis.Lemma, StringComparison.Ordinal));
        return label >= 0 ? label : throw new InvalidDataException($"The chooser of '{chooser.Form}' has no label for the analysis {analysis.Lemma}/{analysis.Features}.");
    }

    /// <summary>
    ///     Scales the probabilities of the analyses to sum to one. When nothing has mass (a label that stands for
    ///     closed-class slots only took all of it), the open-class analyses share it equally; a form without an open-class
    ///     analysis shares it equally among all, as there is nothing else to answer.
    /// </summary>
    /// <param name="plan">The pending word.</param>
    /// <param name="values">The probability of every analysis; rewritten here.</param>
    private static void Normalize(Pending plan, double[] values)
    {
        var sum = values.Sum();
        if (sum > 0)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] /= sum;
            }

            return;
        }

        var open = Enumerable.Range(0, values.Length).Where(i => !SenseFusion.IsClosedClass(plan.SlotOf(i).PartOfSpeech)).ToArray();
        var sharers = open.Length > 0 ? open : Enumerable.Range(0, values.Length).ToArray();
        foreach (var i in sharers)
        {
            values[i] = 1.0 / sharers.Length;
        }
    }

    /// <summary>
    ///     A pending word weighed with its feature, before any threshold decides a level.
    /// </summary>
    private sealed record Weighed(ImmutableArray<AnalysisCandidate> Analyses, WordDetails Selected, Slot SelectedSlot, double SelectedProbability, bool ChooserConfident, Dictionary<string, LemmaScores> LemmaScores);

    /// <summary>
    ///     The analyses of a word that share a lemma and a part of speech, and so their senses.
    /// </summary>
    private sealed class Slot(string lemma, PartOfSpeech partOfSpeech, ImmutableArray<SenseInfo> senses)
    {
        public string Lemma { get; } = lemma;

        public PartOfSpeech PartOfSpeech { get; } = partOfSpeech;

        public ImmutableArray<SenseInfo> Senses { get; } = senses;

        public List<int> Analyses { get; } = [];
    }

    /// <summary>
    ///     A word that is not yet answered: its analyses and what is needed to score its senses.
    /// </summary>
    private sealed record Pending(ImmutableArray<WordDetails> Details, List<Slot> Slots, WeightedLemmaTable Table, FormChooser? Chooser)
    {
        public Slot SlotOf(int analysis) => Slots.First(s => s.Analyses.Contains(analysis));
    }

    /// <summary>
    ///     The scored candidate senses of one weighted lemma for one word.
    /// </summary>
    private sealed class LemmaScores
    {
        private readonly double[] _probabilities;

        private LemmaScores(ResolvedLemma lemma, int[] columns, double[] probabilities, int best, float fineMargin, double groupMargin)
        {
            Lemma = lemma;
            _probabilities = probabilities;
            Best = best;
            FineMargin = fineMargin;
            GroupMargin = groupMargin;
            Senses = columns.Select(c => lemma.Senses[c]).ToImmutableArray();
            Columns = columns;
        }

        /// <summary>
        ///     Gets the scored columns of the lemma, in column order.
        /// </summary>
        public int[] Columns { get; }

        public ResolvedLemma Lemma { get; }

        /// <summary>
        ///     Gets the candidate senses, in column order.
        /// </summary>
        public ImmutableArray<SenseInfo> Senses { get; }

        /// <summary>
        ///     Gets the index (into <see cref="Senses"/>) of the best sense.
        /// </summary>
        public int Best { get; }

        public float FineMargin { get; }

        public double GroupMargin { get; }

        public static LemmaScores Compute(ResolvedLemma lemma, List<int> columns, float[] feature)
        {
            var all = new float[lemma.Weights.Weights.SenseCount];
            lemma.Weights.Weights.Scores(feature, all);
            var scores = columns.Select(c => all[c]).ToArray();
            var probabilities = LemmaWeights.Softmax(scores);
            var (best, margin) = LemmaWeights.Rank(scores);

            // Index every distinct group of the candidates; a sense in no group stands alone
            var groupIds = new List<string>();
            var groups = new int[columns.Count];
            for (var i = 0; i < groups.Length; i++)
            {
                var id = lemma.Senses[columns[i]].GroupId;
                if (id is null)
                {
                    groups[i] = -1;
                    continue;
                }

                var known = groupIds.IndexOf(id);
                if (known < 0)
                {
                    groupIds.Add(id);
                    known = groupIds.Count - 1;
                }

                groups[i] = known;
            }

            var groupScores = LemmaWeights.GroupScores(probabilities, groups);
            return new LemmaScores(lemma, [.. columns], probabilities, best, margin, LemmaWeights.GroupMargin(groupScores, groups, best));
        }

        public double MassOf(Slot slot)
        {
            double mass = 0;
            for (var i = 0; i < Senses.Length; i++)
            {
                if (InSlot(i, slot))
                {
                    mass += _probabilities[i];
                }
            }

            return mass;
        }

        public double WithinSlot(int sense, Slot slot) => Share(_probabilities[sense], slot);

        public double GroupWithinSlot(int sense, Slot slot)
        {
            double sum = 0;
            var group = Senses[sense].GroupId;
            for (var i = 0; i < Senses.Length; i++)
            {
                if (InSlot(i, slot) && string.Equals(Senses[i].GroupId, group, StringComparison.Ordinal))
                {
                    sum += _probabilities[i];
                }
            }

            return Share(sum, slot);
        }

        public ImmutableArray<SenseCandidate> Ranked(Slot slot)
        {
            var inSlot = Enumerable.Range(0, Senses.Length).Where(i => InSlot(i, slot)).ToArray();
            return [.. inSlot.OrderByDescending(i => _probabilities[i]).ThenBy(i => i).Select(i => new SenseCandidate(Senses[i].SenseId, Share(_probabilities[i], slot)))];
        }

        private bool InSlot(int sense, Slot slot) =>
            Senses[sense].PartOfSpeech == slot.PartOfSpeech && string.Equals(Senses[sense].Lemma, slot.Lemma, StringComparison.Ordinal);

        private double Share(double value, Slot slot)
        {
            var mass = MassOf(slot);
            if (mass > 0)
            {
                return value / mass;
            }

            return 1.0 / Math.Max(1, Enumerable.Range(0, Senses.Length).Count(i => InSlot(i, slot)));
        }
    }
}
