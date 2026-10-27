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
///     and several scored senses is pending: the words are joined by single spaces into one text, the encoder runs once
///     for the pending words, the pooled vector of each is centered on the global mean of the weights and scaled to unit
///     length, and the classifier scores the fine senses of every analysis of the word that has an open-class part of speech
///     (noun, verb, adjective, adverb) and no sense of another lemma; a closed-class analysis (auxiliary, determiner, pronoun,
///     preposition, conjunction, interjection) is listed with probability 0 and without senses, and the fallback model of a
///     composition gives it its probability.
///     The softmax of the scores gives the probability of every sense. The senses belong to a lemma and part of speech
///     (a <i>slot</i>), not to the analyses of a slot that differ only in features (<c>cut</c> as an infinitive and as a
///     past tense, <c>deer</c> as a singular and a plural), so the probability of a slot is the mass of its senses and
///     the probability of a sense given its slot is its mass renormalized in the slot; the analyses of a slot split the
///     probability of the slot (by the chooser of the form when it has a label for each of them, otherwise equally), and
///     that split never reaches the level or the confidence. The selected analysis is the best analysis of the most
///     probable slot, and the level is decided for it with the thresholds of the lemma: the sense when
///     the margin of the best fine sense reaches the fine threshold and the best sense lies in that slot, else the
///     group of the best sense when it has one and the margin of the group reaches the group threshold, else the part of
///     speech (the ranked senses of the analysis still show the likely senses and groups). The confidence is the
///     probability of the answer at its level (the probability of the slot, times the probability of the sense, or
///     of the group, within it).
///     </para>
///     <para>
///     A form whose analyses belong to several lemmas or tenses (<c>left</c>, <c>read</c>) is first resolved by the
///     chooser of the form, a small classifier over the lemmas or analyses of the form with its own threshold; a form of
///     that kind without a chooser is answered from the lexicon. A word whose senses are not scored (no weights, fewer
///     than two scored senses, no usable vector) is answered from the lexicon alone: every lemma and part of speech has
///     the same probability 1/s for s of them, the analyses of one of them split it equally (the lexicon order is kept,
///     so the first analysis is the selected one), at the <see cref="SenseLevel.PartOfSpeech"/> level, without scored
///     senses. Such a word is left to
///     the fallback model of a composition (see <see cref="FallbackWordSenseModel"/>). No fine sense is ever guessed.
///     </para>
///     <para>
///     The classifier holds no mutable state and is thread-safe.
///     </para>
/// </remarks>
internal sealed class OnnxClassifier
{
    private readonly WeightedLemmaTable _table;
    private readonly PooledEncode? _encode;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class that answers from the lexicon alone.
    /// </summary>
    public OnnxClassifier()
        : this(new WeightedLemmaTable(), null)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class.
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
    ///     Tells whether any word of a list is pending (has several scored senses, or a form chooser), which is the only
    ///     case that needs the encoder and therefore the model files.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="table">The trained weights.</param>
    /// <returns>True when the model files, the encoder and the weights are needed to classify the list.</returns>
    internal static bool NeedsModel(IReadOnlyList<string> words, WeightedLemmaTable table)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(table);
        return words.Any(w => Plan(w, table).Pending is not null);
    }

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
    public async Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var results = new ClassifyResult[list.Count];
        var pending = new List<(int Index, Pending Plan)>();
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = Plan(list[i], _encode is null ? null : _table);
            if (plan.Result is { } result)
            {
                results[i] = result;
            }
            else
            {
                pending.Add((i, plan.Pending!));
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        var pooled = await _encode!(list, pending.Select(p => p.Index).ToList(), cancellationToken).ConfigureAwait(false);
        var mean = _table.Data.Mean;
        for (var p = 0; p < pending.Count; p++)
        {
            var (index, plan) = pending[p];
            var feature = pooled[p] is { } vector && vector.Length == mean.Length ? FeatureExtractor.Center(vector, mean) : null;
            results[index] = feature is not null ? Decide(list[index], plan, feature) : LexiconOnly(list[index], plan.Details);
        }

        return results;
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

        var lemmas = slots.Select(s => s.Lemma).Distinct(StringComparer.Ordinal).ToList();
        var chooser = ChooserOf(table, word, details);

        // A form of several lemmas needs its chooser; without one the lexicon answers it
        if (lemmas.Count > 1 && chooser is null)
        {
            return (LexiconOnly(word, details), null);
        }

        var scored = lemmas.Any(lemma => table.Find(lemma) is { } resolved && CandidateColumns(resolved, slots.Where(s => s.Lemma == lemma)).Count >= 2);
        return scored || chooser is not null ? (null, new Pending(details, slots, table, chooser)) : (LexiconOnly(word, details), null);
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
    ///     WordNet senses) are not scored: the encoder has no evidence of its own for the part of speech of a word, and the
    ///     probability of such a slot comes from the fallback model (see <see cref="FallbackWordSenseModel"/>).
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
    ///     Answers a word from the lexicon alone.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="details">The analyses of the word, at least one.</param>
    /// <returns>The result at the part of speech level without scored senses.</returns>
    /// <remarks>
    ///     The probability is shared out by lemma and part of speech, not by analysis: every lemma and part of speech has
    ///     the same probability and the analyses that differ only in features share it equally. The analyses are listed
    ///     by slot in the order of the lexicon (a slot is placed where its first analysis is), so the selected analysis
    ///     is the first one of the lexicon and the confidence is the probability of its slot.
    /// </remarks>
    private static ClassifyResult LexiconOnly(string word, ImmutableArray<WordDetails> details)
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

        var slotProbability = 1.0 / slots.Count;
        var analyses = ImmutableArray.CreateBuilder<AnalysisCandidate>(details.Length);
        foreach (var slot in slots)
        {
            foreach (var i in slot)
            {
                analyses.Add(new AnalysisCandidate(details[i], slotProbability / slot.Count, []));
            }
        }

        return new ClassifyResult(word, true, analyses.MoveToImmutable(), details[slots[0][0]], SenseLevel.PartOfSpeech, slotProbability, null, null);
    }

    /// <summary>
    ///     Scores a pending word with its feature and decides the answer.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="plan">The pending word.</param>
    /// <param name="feature">The unit-length feature of the word in context.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    ///     The senses belong to a lemma and part of speech (a slot), not to the analyses that differ only in features, so
    ///     the probabilities are worked out per slot: the probability of a slot is the mass of its senses (or, for a form
    ///     with a chooser, the share of the chooser's labels that its analyses carry), the probability of a sense given
    ///     its slot is its mass renormalized in the slot, and the confidence is the probability of the slot times that.
    ///     The analyses of one slot split the probability of the slot: by the chooser when it has a label for each of
    ///     them, otherwise equally. The selected analysis is the best one of the most probable slot.
    /// </remarks>
    private static ClassifyResult Decide(string word, Pending plan, float[] feature)
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

        Normalize(weightOf);

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
        var analysisProbability = slotProbability[selectedSlot];
        if (!chooserConfident || !lemmaScores.TryGetValue(selectedSlot.Lemma, out var scores))
        {
            return PartOfSpeechAnswer(word, analyses.MoveToImmutable(), details[selected], analysisProbability);
        }

        // The level of the selected analysis, with the thresholds of its lemma
        return AnswerAtLevel(word, analyses.MoveToImmutable(), details[selected], selectedSlot, analysisProbability, scores);
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
    ///     shares it equally among its analyses that the label stands for.
    /// </remarks>
    private static void SpreadLabelProbability(Pending plan, List<int> indexes, double labelProbability, Dictionary<string, LemmaScores> lemmaScores, double[] weightOf)
    {
        var slotsOfLabel = indexes.Select(plan.SlotOf).Distinct().ToList();
        var masses = slotsOfLabel.Select(s => SlotMass(lemmaScores, s)).ToArray();
        var total = masses.Sum();
        for (var k = 0; k < slotsOfLabel.Count; k++)
        {
            var inSlot = indexes.Where(i => plan.SlotOf(i) == slotsOfLabel[k]).ToList();
            var share = total > 0 ? masses[k] / total : 1.0 / slotsOfLabel.Count;
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
        for (var s = 0; s < plan.Slots.Count; s++)
        {
            foreach (var i in plan.Slots[s].Analyses)
            {
                weightOf[i] = (total > 0 ? masses[s] / total : 1.0 / plan.Slots.Count) / plan.Slots[s].Analyses.Count;
            }
        }
    }

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
    ///     Gets the mass of the senses of a slot, or 1 when the senses are not scored.
    /// </summary>
    /// <param name="lemmaScores">The scored lemmas.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The mass.</returns>
    private static double SlotMass(Dictionary<string, LemmaScores> lemmaScores, Slot slot) =>
        lemmaScores.TryGetValue(slot.Lemma, out var scores) ? scores.MassOf(slot) : 1.0;

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

    private static void Normalize(double[] values)
    {
        var sum = values.Sum();
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = sum > 0 ? values[i] / sum : 1.0 / values.Length;
        }
    }

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
        }

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
