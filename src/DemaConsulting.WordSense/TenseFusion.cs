using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     The tense of a verb whose past and non-past share a spelling (<c>read</c>, <c>put</c>, <c>found</c>): the
///     combination of the tense head of an encoder with the past and non-past split of another model, and the
///     application of the result to the analyses of a word.
/// </summary>
/// <remarks>
///     <para>
///     The tense of a word is one number, the probability that the verb is past (a past tense or a participle, the class
///     <see cref="BasicClassifier.IsPast"/> defines; the infinitive, the present and the imperative are the other class).
///     It lives inside the verb: <see cref="PastShare"/> is the share of the past analyses in the probability of all verb
///     analyses of a result, whatever the lemma (the past <c>found</c> is an analysis of <c>find</c>, the infinitive of
///     <c>found</c>). The combination <see cref="Fuse"/> is a weighted sum of the log odds of two such shares (a
///     geometric mean of the odds), and <see cref="Apply"/> sets the share of a result to the combined value without
///     changing the probability of the verb as a whole, the shares inside the past analyses and inside the other
///     analyses, or the analyses of any other part of speech.
///     </para>
/// </remarks>
internal static class TenseFusion
{
    /// <summary>
    ///     The probability below which a share counts as this value, and above whose complement it does too, so that its
    ///     log odds are finite.
    /// </summary>
    private const double Floor = 1e-6;

    /// <summary>
    ///     Tells whether the analyses of a word are those of a same-spelling verb: a verb analysis that is past and a verb
    ///     analysis that is not.
    /// </summary>
    /// <param name="analyses">The analyses of the word.</param>
    /// <returns>True when both kinds of verb analysis are listed.</returns>
    public static bool IsSameSpellingVerb(IEnumerable<WordDetails> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        var past = false;
        var other = false;
        foreach (var analysis in analyses)
        {
            if (analysis.PartOfSpeech == PartOfSpeech.Verb)
            {
                past |= BasicClassifier.IsPast(analysis);
                other |= !BasicClassifier.IsPast(analysis);
            }
        }

        return past && other;
    }

    /// <summary>
    ///     Gets the share of the past analyses in the probability of the verb analyses of a result.
    /// </summary>
    /// <param name="analyses">The analyses of the result.</param>
    /// <returns>The share, or null when the analyses are not those of a same-spelling verb or the verb analyses have no probability.</returns>
    public static double? PastShare(ImmutableArray<AnalysisCandidate> analyses)
    {
        if (analyses.IsDefaultOrEmpty || !IsSameSpellingVerb(analyses.Select(a => a.Details)))
        {
            return null;
        }

        var (past, other) = Masses(analyses);
        var total = past + other;
        return total > 0 && double.IsFinite(total) ? past / total : null;
    }

    /// <summary>
    ///     Combines the probability of past that the tense head gives with a share from another model.
    /// </summary>
    /// <param name="head">The probability of past of the tense head.</param>
    /// <param name="other">The share of the past analyses of the other model, or null when it has none (it then counts as even odds).</param>
    /// <param name="weight">The weight of the head, from 0 to 1: 1 takes the head alone, 0 the other model alone.</param>
    /// <returns>The probability of past: <c>head^w × other^(1−w)</c> in the odds, between 0 and 1.</returns>
    public static double Fuse(double head, double? other, double weight)
    {
        var z = (weight * LogOdds(head)) + ((1.0 - weight) * (other is { } share ? LogOdds(share) : 0.0));
        return 1.0 / (1.0 + Math.Exp(-z));
    }

    /// <summary>
    ///     Sets the share of the past analyses among the verb analyses of a result.
    /// </summary>
    /// <param name="result">The result of the word.</param>
    /// <param name="past">The share of the past analyses among the verb analyses, from 0 to 1.</param>
    /// <returns>The result with the past analyses scaled to <c>V × past</c> and the other verb analyses to <c>V × (1 − past)</c>, where V is the probability of the verb analyses together; or <paramref name="result"/> itself when it is not a same-spelling verb or its verb analyses have no probability.</returns>
    /// <remarks>
    ///     The analyses are listed by slot again (the most probable slot first, ties in the order of the input) and in a
    ///     slot the most probable analysis first, and the selected analysis is the first one. When the selected slot is the
    ///     slot that the result answered, its level, group and sense stay, and the confidence is the new probability of the
    ///     slot times the probability of the sense (or the group) within it; the slot of a same-lemma verb keeps its
    ///     probability, so only a form whose past and non-past belong to different lemmas (<c>found</c>) can change it.
    ///     When another slot becomes the most probable one, the answer is at the <see cref="SenseLevel.PartOfSpeech"/>
    ///     level on the best analysis of that slot, with the probability of the slot as its confidence.
    /// </remarks>
    public static ClassifyResult Apply(ClassifyResult result, double past)
    {
        var analyses = result.Analyses;
        if (analyses.IsDefaultOrEmpty || !IsSameSpellingVerb(analyses.Select(a => a.Details)) || !(past >= 0.0 && past <= 1.0))
        {
            return result;
        }

        var (pastMass, otherMass) = Masses(analyses);
        var verbMass = pastMass + otherMass;
        if (!(verbMass > 0) || !double.IsFinite(verbMass))
        {
            return result;
        }

        var pastCount = analyses.Count(a => a.Details.PartOfSpeech == PartOfSpeech.Verb && BasicClassifier.IsPast(a.Details));
        var otherCount = analyses.Count(a => a.Details.PartOfSpeech == PartOfSpeech.Verb && !BasicClassifier.IsPast(a.Details));
        var scaled = new AnalysisCandidate[analyses.Length];
        for (var i = 0; i < scaled.Length; i++)
        {
            var analysis = analyses[i];
            if (analysis.Details.PartOfSpeech != PartOfSpeech.Verb)
            {
                scaled[i] = analysis;
            }
            else if (BasicClassifier.IsPast(analysis.Details))
            {
                scaled[i] = analysis.WithProbability(verbMass * past * (pastMass > 0 ? analysis.Probability / pastMass : 1.0 / pastCount));
            }
            else
            {
                scaled[i] = analysis.WithProbability(verbMass * (1.0 - past) * (otherMass > 0 ? analysis.Probability / otherMass : 1.0 / otherCount));
            }
        }

        // The slots in the order of the input, their probabilities, and the new order: the most probable slot first
        var slotOf = new int[scaled.Length];
        var slots = new List<WordDetails>();
        for (var i = 0; i < scaled.Length; i++)
        {
            var slot = slots.FindIndex(s => SenseFusion.SameSlot(s, scaled[i].Details));
            if (slot < 0)
            {
                slots.Add(scaled[i].Details);
                slot = slots.Count - 1;
            }

            slotOf[i] = slot;
        }

        var slotProbability = new double[slots.Count];
        for (var i = 0; i < scaled.Length; i++)
        {
            slotProbability[slotOf[i]] += scaled[i].Probability;
        }

        var order = Enumerable.Range(0, scaled.Length)
            .OrderByDescending(i => slotProbability[slotOf[i]])
            .ThenBy(i => slotOf[i])
            .ThenByDescending(i => scaled[i].Probability)
            .ThenBy(i => i)
            .ToArray();
        var ordered = ImmutableArray.CreateRange(order.Select(i => scaled[i]));
        var top = slotOf[order[0]];
        var answered = result.Details is { } selected ? slots.FindIndex(s => SenseFusion.SameSlot(s, selected)) : -1;
        if (answered == top && result.Level is SenseLevel.Sense or SenseLevel.Group)
        {
            // The sense (or the group) within the slot keeps its probability: only the probability of the slot is new
            var oldSlotProbability = analyses.Where(a => SenseFusion.SameSlot(a.Details, slots[top])).Sum(a => a.Probability);
            var inSlot = oldSlotProbability > 0 ? Math.Min(1.0, result.Confidence / oldSlotProbability) : 1.0;
            return new ClassifyResult(result.Word, result.IsKnown, ordered, ordered[0].Details, result.Level, slotProbability[top] * inSlot, result.GroupId, result.SenseId);
        }

        return new ClassifyResult(result.Word, result.IsKnown, ordered, ordered[0].Details, SenseLevel.PartOfSpeech, slotProbability[top], null, null);
    }

    /// <summary>
    ///     Adds up the probability of the past and of the other verb analyses of a result.
    /// </summary>
    /// <param name="analyses">The analyses of the result.</param>
    /// <returns>The summed probability of the past verb analyses and of the other verb analyses; analyses of other parts of speech do not count.</returns>
    private static (double Past, double Other) Masses(ImmutableArray<AnalysisCandidate> analyses)
    {
        double past = 0;
        double other = 0;
        foreach (var analysis in analyses)
        {
            if (analysis.Details.PartOfSpeech != PartOfSpeech.Verb)
            {
                continue;
            }

            if (BasicClassifier.IsPast(analysis.Details))
            {
                past += analysis.Probability;
            }
            else
            {
                other += analysis.Probability;
            }
        }

        return (past, other);
    }

    /// <summary>
    ///     Gets the log odds of a probability, the scale on which two estimates of the tense are combined.
    /// </summary>
    /// <param name="probability">The probability, clamped to the floor so that the result is finite.</param>
    /// <returns>The natural logarithm of the odds.</returns>
    private static double LogOdds(double probability)
    {
        var p = Math.Clamp(probability, Floor, 1.0 - Floor);
        return Math.Log(p / (1.0 - p));
    }
}
