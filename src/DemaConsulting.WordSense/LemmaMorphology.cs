using System.Numerics;

namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: the suffix rules a <see cref="WordSenseTable"/> may apply to a word that is absent from it, to find
///     the dictionary lemma the word is an inflection of. The enabled set is stored in the table.
/// </summary>
/// <remarks>
///     A rule never overrides a word the table contains and never applies to a lemma that has trained weights. Rules
///     that did not reach the accuracy gate on real text (<c>-ing</c>, <c>-er</c>/<c>-est</c>, possessives of
///     dictionary words) do not exist.
/// </remarks>
[Flags]
public enum LemmaMorphologyRules
{
    /// <summary>
    ///     No rule: only words the table contains are known.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Plural and third-person <c>-s</c>, <c>-es</c>, <c>-ies</c>: a base that is only a noun gives a noun, a base
    ///     that is only a verb gives a verb.
    /// </summary>
    Plural = 1,

    /// <summary>
    ///     Past tense <c>-ed</c>, <c>-ied</c> and doubled-consonant <c>-ed</c> of a base that can be a verb: a verb.
    /// </summary>
    PastTense = 2,

    /// <summary>
    ///     Adverb <c>-ly</c> of a base that is only an adjective: an adverb.
    /// </summary>
    Adverb = 4,

    /// <summary>
    ///     Every rule that passed the accuracy gate.
    /// </summary>
    Default = Plural | PastTense | Adverb,
}

/// <summary>
///     Derives the dictionary lemma of a word that the table does not contain.
/// </summary>
internal static class LemmaMorphology
{
    /// <summary>
    ///     The smallest base accepted for a derived word.
    /// </summary>
    private const int MinBaseLength = 3;

    /// <summary>
    ///     Tries to resolve a word absent from the table through the enabled rules.
    /// </summary>
    /// <param name="word">The lower-case word.</param>
    /// <param name="rules">The enabled rules.</param>
    /// <param name="table">The table that supplies the base lemmas.</param>
    /// <param name="resolution">Receives the base entry narrowed to the derived sense.</param>
    /// <returns>True when a rule applies and the base satisfies its gate.</returns>
    public static bool TryResolve(string word, LemmaMorphologyRules rules, WordSenseTable table, out LemmaResolution resolution)
    {
        resolution = default;
        if (rules == LemmaMorphologyRules.None || word.Length <= MinBaseLength || !LexicalIndex.IsValidWord(word))
        {
            return false;
        }

        return ((rules & LemmaMorphologyRules.Plural) != 0 && TryPlural(word, table, out resolution))
            || ((rules & LemmaMorphologyRules.PastTense) != 0 && TryPastTense(word, table, out resolution))
            || ((rules & LemmaMorphologyRules.Adverb) != 0 && TryAdverb(word, table, out resolution));
    }

    /// <summary>
    ///     Applies the plural rule.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="table">The table.</param>
    /// <param name="resolution">Receives the resolution.</param>
    /// <returns>True when the rule applies.</returns>
    private static bool TryPlural(string word, WordSenseTable table, out LemmaResolution resolution)
    {
        resolution = default;
        if (!word.EndsWith('s') || word.EndsWith("ss", StringComparison.Ordinal) ||
            word.EndsWith("us", StringComparison.Ordinal) || word.EndsWith("is", StringComparison.Ordinal))
        {
            return false;
        }

        // Plain -s, then -ies, then -es
        foreach (var candidate in PluralBases(word))
        {
            if (candidate.Length >= MinBaseLength && table.TryGetDictionaryMask(candidate, out var mask) &&
                (mask == Bit(PartOfSpeech.Noun) || mask == Bit(PartOfSpeech.Verb)))
            {
                resolution = new LemmaResolution(WordSenseTable.CreateDictionaryEntry(candidate, mask), 0);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Lists the base spellings a plural or third-person word may come from, most likely first.
    /// </summary>
    /// <param name="word">A word ending in <c>s</c>.</param>
    /// <returns>The candidate bases.</returns>
    private static IEnumerable<string> PluralBases(string word)
    {
        yield return word[..^1];
        if (word.EndsWith("ies", StringComparison.Ordinal))
        {
            yield return word[..^3] + "y";
        }

        if (word.EndsWith("es", StringComparison.Ordinal))
        {
            yield return word[..^2];
        }
    }

    /// <summary>
    ///     Applies the past-tense rule.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="table">The table.</param>
    /// <param name="resolution">Receives the resolution.</param>
    /// <returns>True when the rule applies.</returns>
    private static bool TryPastTense(string word, WordSenseTable table, out LemmaResolution resolution)
    {
        resolution = default;
        if (!word.EndsWith("ed", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var candidate in PastBases(word))
        {
            if (candidate.Length >= MinBaseLength && table.TryGetDictionaryMask(candidate, out var mask) &&
                (mask & Bit(PartOfSpeech.Verb)) != 0)
            {
                var entry = WordSenseTable.CreateDictionaryEntry(candidate, mask);
                resolution = new LemmaResolution(entry, BitOperations.PopCount((uint)(mask & (Bit(PartOfSpeech.Verb) - 1))));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Lists the base spellings a past-tense word may come from, most likely first.
    /// </summary>
    /// <param name="word">A word ending in <c>ed</c>.</param>
    /// <returns>The candidate bases.</returns>
    private static IEnumerable<string> PastBases(string word)
    {
        var stem = word[..^2];
        yield return stem;
        yield return word[..^1];
        if (stem.Length >= 2 && stem[^1] == stem[^2] && stem[^1] is not ('a' or 'e' or 'i' or 'o' or 'u'))
        {
            yield return stem[..^1];
        }

        if (stem.EndsWith('i'))
        {
            yield return stem[..^1] + "y";
        }
    }

    /// <summary>
    ///     Applies the adverb rule.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="table">The table.</param>
    /// <param name="resolution">Receives the resolution.</param>
    /// <returns>True when the rule applies.</returns>
    private static bool TryAdverb(string word, WordSenseTable table, out LemmaResolution resolution)
    {
        resolution = default;
        if (!word.EndsWith("ly", StringComparison.Ordinal))
        {
            return false;
        }

        var stem = word[..^2];
        var candidates = stem.EndsWith('i') ? new[] { stem, stem[..^1] + "y" } : [stem];
        foreach (var candidate in candidates)
        {
            if (candidate.Length >= MinBaseLength && table.TryGetDictionaryMask(candidate, out var mask) &&
                mask == Bit(PartOfSpeech.Adjective))
            {
                // The base has no adverb sense, so the answer is a one-sense entry for the base lemma
                var entry = new LemmaEntry(candidate, [new SenseDefinition(PartOfSpeech.Adverb)]);
                resolution = new LemmaResolution(entry, 0);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets the part-of-speech mask bit of a part of speech.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The bit.</returns>
    internal static int Bit(PartOfSpeech pos) => 1 << (int)pos;
}
