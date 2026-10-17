using System.Collections.Frozen;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The outcome of a rule that fired.
/// </summary>
/// <param name="Rule">The name of the rule.</param>
/// <param name="PartOfSpeech">The part of speech the rule selects.</param>
/// <param name="Confidence">The fixed confidence of the rule, between 0 and 1.</param>
internal readonly record struct RuleMatch(string Rule, PartOfSpeech PartOfSpeech, double Confidence);

/// <summary>
///     The hand-written rules of the basic model: closed-class word lists and deterministic rules over the parts of
///     speech the neighboring words can have.
/// </summary>
/// <remarks>
///     <para>
///     The rules run in the order of <see cref="RuleNames"/>; the first rule that fires wins. A rule fires only when
///     the part of speech it selects is one the target word can have. Each rule carries a fixed confidence. The values
///     below are initial estimates; the builder command <c>basic-precision</c> measures the precision of every rule on
///     the training anchors so the values can be calibrated to it. Nothing here is trained or learned at run time.
///     </para>
///     <para>
///     The rules use only the public word lexicon: neighbor part-of-speech sets come from
///     <see cref="WordLexicon.Words"/>.
///     </para>
/// </remarks>
internal static class Rules
{
    /// <summary>The rule for a determiner or possessive directly before the word.</summary>
    internal const string AfterDeterminer = "after-determiner";

    /// <summary>The rule for a modal or auxiliary verb directly before the word.</summary>
    internal const string AfterModal = "after-modal";

    /// <summary>The rule for the word <c>to</c> directly before the word.</summary>
    internal const string AfterTo = "after-to";

    /// <summary>The rule for a subject pronoun directly before the word.</summary>
    internal const string AfterSubjectPronoun = "after-subject-pronoun";

    /// <summary>The rule for a preposition directly before the word.</summary>
    internal const string AfterPreposition = "after-preposition";

    /// <summary>The rule for a degree adverb directly before the word.</summary>
    internal const string AfterDegreeAdverb = "after-degree-adverb";

    /// <summary>The rule for an auxiliary verb directly after the word.</summary>
    internal const string BeforeAuxiliary = "before-auxiliary";

    /// <summary>The rule for a word that can only be a noun directly after the word.</summary>
    internal const string BeforeNoun = "before-noun";

    /// <summary>The initial confidence of <see cref="AfterDeterminer"/>.</summary>
    internal const double AfterDeterminerConfidence = 0.85;

    /// <summary>The initial confidence of <see cref="AfterModal"/>.</summary>
    internal const double AfterModalConfidence = 0.9;

    /// <summary>The initial confidence of <see cref="AfterTo"/>.</summary>
    internal const double AfterToConfidence = 0.7;

    /// <summary>The initial confidence of <see cref="AfterSubjectPronoun"/>.</summary>
    internal const double AfterSubjectPronounConfidence = 0.8;

    /// <summary>The initial confidence of <see cref="AfterPreposition"/>.</summary>
    internal const double AfterPrepositionConfidence = 0.7;

    /// <summary>The initial confidence of <see cref="AfterDegreeAdverb"/>.</summary>
    internal const double AfterDegreeAdverbConfidence = 0.7;

    /// <summary>The initial confidence of <see cref="BeforeAuxiliary"/>.</summary>
    internal const double BeforeAuxiliaryConfidence = 0.7;

    /// <summary>The initial confidence of <see cref="BeforeNoun"/>.</summary>
    internal const double BeforeNounConfidence = 0.6;

    private static readonly FrozenSet<string> Determiners = Set(
        "the", "a", "an", "this", "these", "those", "my", "your", "his", "her", "its", "our", "their",
        "some", "any", "no", "every", "each", "another", "either", "neither");

    private static readonly FrozenSet<string> Modals = Set(
        "will", "would", "can", "could", "shall", "should", "may", "might", "must", "do", "does", "did",
        "don't", "doesn't", "didn't", "won't", "can't", "cannot", "couldn't", "wouldn't", "shouldn't");

    private static readonly FrozenSet<string> SubjectPronouns = Set("i", "you", "he", "she", "it", "we", "they", "who");

    private static readonly FrozenSet<string> Prepositions = Set(
        "in", "on", "at", "of", "for", "with", "by", "from", "about", "into", "over", "under", "through",
        "during", "without", "between", "among", "against", "toward", "towards", "upon", "within");

    private static readonly FrozenSet<string> DegreeAdverbs = Set("very", "too", "so", "quite", "more", "most", "less", "least", "rather", "extremely");

    private static readonly FrozenSet<string> Auxiliaries = Set(
        "is", "are", "was", "were", "has", "have", "had", "will", "would", "can", "could", "shall", "should",
        "may", "might", "must", "does", "did");

    /// <summary>
    ///     Gets the names of the rules in the order they are tried.
    /// </summary>
    internal static IReadOnlyList<string> RuleNames { get; } =
    [
        AfterModal,
        AfterTo,
        AfterSubjectPronoun,
        AfterDeterminer,
        AfterDegreeAdverb,
        AfterPreposition,
        BeforeAuxiliary,
        BeforeNoun,
    ];

    /// <summary>
    ///     Gets the current confidence of every rule by name.
    /// </summary>
    internal static IReadOnlyDictionary<string, double> Confidences { get; } = new Dictionary<string, double>
    {
        [AfterModal] = AfterModalConfidence,
        [AfterTo] = AfterToConfidence,
        [AfterSubjectPronoun] = AfterSubjectPronounConfidence,
        [AfterDeterminer] = AfterDeterminerConfidence,
        [AfterDegreeAdverb] = AfterDegreeAdverbConfidence,
        [AfterPreposition] = AfterPrepositionConfidence,
        [BeforeAuxiliary] = BeforeAuxiliaryConfidence,
        [BeforeNoun] = BeforeNounConfidence,
    }.ToFrozenDictionary();

    /// <summary>
    ///     Applies the rules to one word.
    /// </summary>
    /// <param name="previous">The normalized word before the target, or null at the start.</param>
    /// <param name="next">The normalized word after the target, or null at the end.</param>
    /// <param name="candidates">The parts of speech the target word can have.</param>
    /// <returns>The first rule that fires, or null when none does.</returns>
    internal static RuleMatch? Evaluate(string? previous, string? next, IReadOnlySet<PartOfSpeech> candidates)
    {
        // Closed-class context to the left decides first, then context to the right
        if (previous is not null)
        {
            if (Modals.Contains(previous) && candidates.Contains(PartOfSpeech.Verb))
            {
                return new RuleMatch(AfterModal, PartOfSpeech.Verb, AfterModalConfidence);
            }

            if (previous == "to" && candidates.Contains(PartOfSpeech.Verb))
            {
                return new RuleMatch(AfterTo, PartOfSpeech.Verb, AfterToConfidence);
            }

            if (SubjectPronouns.Contains(previous) && candidates.Contains(PartOfSpeech.Verb))
            {
                return new RuleMatch(AfterSubjectPronoun, PartOfSpeech.Verb, AfterSubjectPronounConfidence);
            }

            if (Determiners.Contains(previous))
            {
                // A determiner starts a noun phrase: prefer the noun, otherwise the adjective
                if (candidates.Contains(PartOfSpeech.Noun))
                {
                    return new RuleMatch(AfterDeterminer, PartOfSpeech.Noun, AfterDeterminerConfidence);
                }

                if (candidates.Contains(PartOfSpeech.Adjective))
                {
                    return new RuleMatch(AfterDeterminer, PartOfSpeech.Adjective, AfterDeterminerConfidence);
                }
            }

            if (DegreeAdverbs.Contains(previous) && candidates.Contains(PartOfSpeech.Adjective))
            {
                return new RuleMatch(AfterDegreeAdverb, PartOfSpeech.Adjective, AfterDegreeAdverbConfidence);
            }

            if (Prepositions.Contains(previous) && candidates.Contains(PartOfSpeech.Noun))
            {
                return new RuleMatch(AfterPreposition, PartOfSpeech.Noun, AfterPrepositionConfidence);
            }
        }

        if (next is not null)
        {
            if (Auxiliaries.Contains(next) && candidates.Contains(PartOfSpeech.Noun))
            {
                return new RuleMatch(BeforeAuxiliary, PartOfSpeech.Noun, BeforeAuxiliaryConfidence);
            }

            if (candidates.Contains(PartOfSpeech.Adjective) && OnlyNoun(next))
            {
                return new RuleMatch(BeforeNoun, PartOfSpeech.Adjective, BeforeNounConfidence);
            }
        }

        return null;
    }

    /// <summary>
    ///     Checks whether a normalized word can only be a noun according to the word lexicon.
    /// </summary>
    /// <param name="word">The normalized word.</param>
    /// <returns>True when the lexicon knows the word and every sense is a noun.</returns>
    private static bool OnlyNoun(string word) =>
        WordLexicon.Words.TryGetValue(word, out var senses) && senses.All(s => s.PartOfSpeech == PartOfSpeech.Noun);

    /// <summary>
    ///     Builds a set of lower-case words.
    /// </summary>
    /// <param name="words">The words, already lower case.</param>
    /// <returns>The set.</returns>
    private static FrozenSet<string> Set(params string[] words) => words.ToFrozenSet(StringComparer.Ordinal);
}
