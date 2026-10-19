// cspell:ignore gret
using System.Text.RegularExpressions;

namespace WordSense.Builder.Data;

/// <summary>
///     The regular suffix and spelling rules of English inflection: the plural of a noun, the third person, past tense and
///     gerund of a verb, the degrees of an adjective, and the reverse of these rules.
/// </summary>
/// <remarks>
///     Every rule works on one lower-case word and returns the forms it produces, the primary spelling first. A rule that
///     has two plausible spellings (<c>travelled</c> or <c>traveled</c>) returns the other as an alternative; the
///     alternative is used only when the word list attests it and not the primary spelling.
/// </remarks>
internal static partial class InflectionRules
{
    /// <summary>The UD feature string of a plural noun.</summary>
    internal const string FeaturePlural = "Number=Plur";

    /// <summary>The UD feature string of a past participle (the form shared with the past tense).</summary>
    internal const string FeaturePastParticiple = "Tense=Past+VerbForm=Part";

    /// <summary>The UD feature string of a gerund.</summary>
    internal const string FeatureGerund = "VerbForm=Ger";

    /// <summary>The UD feature string of a comparative adjective.</summary>
    internal const string FeatureComparative = "Degree=Cmp";

    /// <summary>The UD feature string of a superlative adjective.</summary>
    internal const string FeatureSuperlative = "Degree=Sup";

    private static readonly HashSet<string> NounEsO = new(
        ["potato", "tomato", "hero", "echo", "veto", "torpedo", "embargo", "tornado", "volcano", "buffalo", "domino", "mosquito", "cargo", "mango"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> NounEsOAlso = new(
        ["mango", "cargo", "domino", "mosquito", "embargo", "volcano", "tornado", "torpedo", "buffalo", "echo", "veto"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> Ves = new(
        ["knife", "wife", "life", "leaf", "loaf", "half", "wolf", "calf", "shelf", "self", "elf", "thief", "sheaf"],
        StringComparer.Ordinal);

    private static readonly string[] Mono = (
        "fit hit hop nap pat plug rub run shop ship skip slip step stop strip trim trip wrap zip dip cut tap trap bar bat beg bug clap drop flip "
        + "grab grin hug jam knit mop pin plan pop prop rig rob scan sin slam snap spot star stir swap tag tan thin tip top whip win quip quit "
        + "rap sob sap nod pad pit pot put rot rid sit set tug wed ban bed bid fan fog gag gin hem hum jog lap map nab net pip pun ram sag sum "
        + "sun tab tin tub veto bud cap dub fib fit gap gun jig kid lug mat mud peg pet rag rib rim rot rub sag sip sob sod spin spur stab "
        + "stub stun swim swot tar tip tot tub wag rip zap nip nag jot jut lop lob mob mug dim din fin gab gum jab jar mar pan pen rev rut sub sup vet wad web wet yap").Split(' ');

    private static readonly string[] StressPrefixes =
        ["re", "un", "out", "over", "under", "up", "de", "dis", "mis", "pre", "retro", "sub", "inter", "be", "counter", "off"];

    private static readonly string[] UnstressedEndings =
        ["enter", "offer", "suffer", "differ", "cover", "order", "gather", "wonder", "visit", "benefit", "limit", "edit", "profit", "audit", "merit", "orbit", "exhibit", "inhibit", "prohibit", "vomit", "format"];

    /// <summary>
    ///     The prefixes that an irregular verb can take (<c>overtake</c>, <c>mistake</c>).
    /// </summary>
    public static readonly string[] VerbPrefixes =
        ["be", "de", "dis", "fore", "for", "inter", "mis", "out", "over", "pre", "re", "under", "up", "with", "un", "sub", "counter", "off", "down"];

    /// <summary>
    ///     The irregular verbs that never take a prefix (<c>saw</c> is a noun too, <c>fall</c> and <c>swim</c> have no prefixed
    ///     forms in use); every other irregular verb of three or more letters, and <c>go</c> and <c>do</c>, does:
    ///     <c>retake</c> and <c>overdo</c> inflect like <c>take</c> and <c>do</c>.
    /// </summary>
    public static readonly HashSet<string> NotPrefixable = new(
        ["saw", "bid", "string", "spring", "rid", "fall", "spit", "swim"],
        StringComparer.Ordinal);

    /// <summary>
    ///     The adverbs that have a degree of their own (the others take no <c>-er</c> or <c>-est</c>).
    /// </summary>
    public static readonly HashSet<string> GradableAdverbs = new(
        ["well", "badly", "far", "much", "little", "late", "near", "soon", "fast", "hard", "long", "early", "often", "high", "low", "wide", "deep", "close", "loud", "slow", "quick", "cheap", "tight"],
        StringComparer.Ordinal);

    /// <summary>
    ///     The adverbs whose degree forms are irregular and come from the closed list.
    /// </summary>
    public static readonly HashSet<string> IrregularAdverbs = new(
        ["well", "badly", "far", "much", "little", "late", "often", "soon"],
        StringComparer.Ordinal);

    /// <summary>
    ///     The regular forms of a verb.
    /// </summary>
    /// <param name="Past">The primary past and participle spellings.</param>
    /// <param name="Gerund">The primary gerund spellings.</param>
    /// <param name="PastAlternative">The alternative past spellings.</param>
    /// <param name="GerundAlternative">The alternative gerund spellings.</param>
    internal sealed record VerbForms(string[] Past, string[] Gerund, string[] PastAlternative, string[] GerundAlternative);

    /// <summary>
    ///     Counts the groups of vowel letters of a word, as an estimate of its syllables.
    /// </summary>
    /// <param name="word">The lower-case word.</param>
    /// <returns>The number of groups.</returns>
    public static int VowelGroups(string word) => VowelRun().Count(word);

    /// <summary>
    ///     Tells whether a word ends in consonant, vowel, consonant (not w, x or y) without a vowel pair before the end.
    /// </summary>
    /// <param name="word">The lower-case word.</param>
    /// <returns>True when the final consonant would double before a suffix of a short word.</returns>
    public static bool EndsConsonantVowelConsonant(string word) => CvcEnd().IsMatch(word) && !VowelPairEnd().IsMatch(word);

    /// <summary>
    ///     Gets the plural spellings of a noun.
    /// </summary>
    /// <param name="word">The lower-case singular.</param>
    /// <returns>The spellings, the primary one first.</returns>
    public static string[] NounPlural(string word)
    {
        if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith("ch", StringComparison.Ordinal) || word.EndsWith("sh", StringComparison.Ordinal))
        {
            return [word + "es"];
        }

        if (ConsonantY().IsMatch(word))
        {
            return [word[..^1] + "ies"];
        }

        if (word.EndsWith('o') && NounEsO.Contains(word))
        {
            return NounEsOAlso.Contains(word) ? [word + "es", word + "s"] : [word + "es"];
        }

        if (Ves.Contains(word))
        {
            return [word.EndsWith("fe", StringComparison.Ordinal) ? word[..^2] + "ves" : word[..^1] + "ves"];
        }

        return [word + "s"];
    }

    /// <summary>
    ///     Gets the third person singular present of a verb.
    /// </summary>
    /// <param name="word">The lower-case base.</param>
    /// <returns>The spelling.</returns>
    public static string VerbThirdPerson(string word)
    {
        if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith('o') || word.EndsWith("ch", StringComparison.Ordinal) || word.EndsWith("sh", StringComparison.Ordinal))
        {
            return word + "es";
        }

        return ConsonantY().IsMatch(word) ? word[..^1] + "ies" : word + "s";
    }

    /// <summary>
    ///     Gets the regular past and gerund of a verb, with the alternative spellings.
    /// </summary>
    /// <param name="word">The lower-case base.</param>
    /// <param name="listed">Tells whether the word list holds a word; it swaps a primary spelling for an alternative that only the list holds.</param>
    /// <returns>The forms.</returns>
    public static VerbForms VerbRegular(string word, Func<string, bool> listed)
    {
        var r = VerbRegularPlain(word);
        if (r.PastAlternative.Length > 0 && !listed(r.Past[0]) && listed(r.PastAlternative[0]))
        {
            r = r with { Past = r.PastAlternative, PastAlternative = r.Past };
        }

        if (r.GerundAlternative.Length > 0 && !listed(r.Gerund[0]) && listed(r.GerundAlternative[0]))
        {
            r = r with { Gerund = r.GerundAlternative, GerundAlternative = r.Gerund };
        }

        return r;
    }

    /// <summary>
    ///     Gets the comparative and superlative of a one- or two-syllable adjective.
    /// </summary>
    /// <param name="word">The lower-case adjective.</param>
    /// <returns>The comparative and superlative, or null when the word takes no <c>-er</c> and <c>-est</c>.</returns>
    public static (string Comparative, string Superlative)? AdjectiveDegrees(string word)
    {
        var syllables = VowelGroups(word);
        if (!(syllables == 1 || (syllables == 2 && TwoSyllableEnding().IsMatch(word)))
            || HeavyEnding.Any(e => word.EndsWith(e, StringComparison.Ordinal)))
        {
            return null;
        }

        if (word.EndsWith('e'))
        {
            return (word + "r", word + "st");
        }

        if (ConsonantY().IsMatch(word))
        {
            return (word[..^1] + "ier", word[..^1] + "iest");
        }

        if (EndsConsonantVowelConsonant(word))
        {
            return (word + word[^1] + "er", word + word[^1] + "est");
        }

        return (word + "er", word + "est");
    }

    /// <summary>
    ///     Gets the candidate bases of a word that could be the inflected form of one part of speech.
    /// </summary>
    /// <param name="word">The lower-case word.</param>
    /// <param name="pos">The part-of-speech letter: <c>n</c>, <c>v</c>, <c>a</c> or <c>r</c>.</param>
    /// <returns>The candidate bases with the features the word would have.</returns>
    public static List<(string Base, string Features)> ReverseCandidates(string word, string pos)
    {
        var w = word;
        var result = new List<(string, string)>();
        switch (pos)
        {
            case "n":
                if (w.EndsWith("ies", StringComparison.Ordinal))
                {
                    result.Add((w[..^3] + "y", FeaturePlural));
                }

                if (w.EndsWith("es", StringComparison.Ordinal))
                {
                    result.Add((w[..^2], FeaturePlural));
                }

                if (w.EndsWith("ves", StringComparison.Ordinal))
                {
                    result.Add((w[..^3] + "f", FeaturePlural));
                    result.Add((w[..^3] + "fe", FeaturePlural));
                }

                if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal))
                {
                    result.Add((w[..^1], FeaturePlural));
                }

                break;
            case "v":
                if (w.EndsWith("ies", StringComparison.Ordinal))
                {
                    result.Add((w[..^3] + "y", "Person=3+Tense=Pres"));
                }

                if (w.EndsWith("es", StringComparison.Ordinal))
                {
                    result.Add((w[..^2], "Person=3+Tense=Pres"));
                }

                if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal))
                {
                    result.Add((w[..^1], "Person=3+Tense=Pres"));
                }

                if (w.EndsWith("ied", StringComparison.Ordinal))
                {
                    result.Add((w[..^3] + "y", FeaturePastParticiple));
                }

                if (w.EndsWith("ed", StringComparison.Ordinal))
                {
                    result.Add((w[..^2], FeaturePastParticiple));
                    result.Add((w[..^1], FeaturePastParticiple));
                    if (w.Length > 4 && w[^3] == w[^4])
                    {
                        result.Add((w[..^3], FeaturePastParticiple));
                    }
                }

                if (w.EndsWith("ing", StringComparison.Ordinal))
                {
                    result.Add((w[..^3], FeatureGerund));
                    result.Add((w[..^3] + "e", FeatureGerund));
                    if (w.Length > 5 && w[^4] == w[^5])
                    {
                        result.Add((w[..^4], FeatureGerund));
                    }

                    if (w.EndsWith("ying", StringComparison.Ordinal))
                    {
                        result.Add((w[..^4] + "ie", FeatureGerund));
                    }
                }

                break;
            case "a":
            case "r":
                if (w.EndsWith("ier", StringComparison.Ordinal))
                {
                    result.Add((w[..^3] + "y", FeatureComparative));
                }

                if (w.EndsWith("iest", StringComparison.Ordinal))
                {
                    result.Add((w[..^4] + "y", FeatureSuperlative));
                }

                if (w.EndsWith("er", StringComparison.Ordinal))
                {
                    result.Add((w[..^2], FeatureComparative));
                    result.Add((w[..^1], FeatureComparative));
                    if (w.Length > 4 && w[^3] == w[^4])
                    {
                        result.Add((w[..^3], FeatureComparative));
                    }
                }

                if (w.EndsWith("est", StringComparison.Ordinal))
                {
                    result.Add((w[..^3], FeatureSuperlative));
                    result.Add((w[..^2], FeatureSuperlative));
                    if (w.Length > 5 && w[^4] == w[^5])
                    {
                        result.Add((w[..^4], FeatureSuperlative));
                    }
                }

                break;
            default:
                break;
        }

        return result;
    }

    private static readonly string[] HeavyEnding = ["ed", "ing", "ful", "less", "ous", "ish", "ive", "al"];

    private static VerbForms VerbRegularPlain(string w)
    {
        if (w.EndsWith('e') && !w.EndsWith("ee", StringComparison.Ordinal) && !w.EndsWith("ye", StringComparison.Ordinal) && !w.EndsWith("oe", StringComparison.Ordinal))
        {
            return new VerbForms([w + "d"], [w[..^1] + "ing"], [], []);
        }

        if (w.EndsWith("ee", StringComparison.Ordinal) || w.EndsWith("ye", StringComparison.Ordinal) || w.EndsWith("oe", StringComparison.Ordinal))
        {
            return new VerbForms([w + "d"], [w + "ing"], [], []);
        }

        if (ConsonantY().IsMatch(w))
        {
            return new VerbForms([w[..^1] + "ied"], [w + "ing"], [], []);
        }

        if (w.EndsWith("ie", StringComparison.Ordinal))
        {
            return new VerbForms([w + "d"], [w[..^2] + "ying"], [], []);
        }

        if (w.EndsWith('c') && w.Length > 1 && "aeiouy".Contains(w[^2], StringComparison.Ordinal))
        {
            return new VerbForms([w + "ked"], [w + "king"], [], []);
        }

        if (w.EndsWith('c'))
        {
            return new VerbForms([w + "ed"], [w + "ing"], [], []);
        }

        if (EndsConsonantVowelConsonant(w) && (VowelGroups(w) == 1 || StressFinal(w)))
        {
            return new VerbForms([w + w[^1] + "ed"], [w + w[^1] + "ing"], [], []);
        }

        if (EndsConsonantVowelConsonant(w))
        {
            return new VerbForms([w + "ed"], [w + "ing"], [w + w[^1] + "ed"], [w + w[^1] + "ing"]);
        }

        if (w.EndsWith('l') && LiquidEnd().IsMatch(w) && VowelGroups(w) >= 2)
        {
            return new VerbForms([w + "ed"], [w + "ing"], [w + "led"], [w + "ling"]);
        }

        return new VerbForms([w + "ed"], [w + "ing"], [], []);
    }

    private static bool StressFinal(string w)
    {
        if (StressedEnding().IsMatch(w) && VowelGroups(w) >= 2 && EndsConsonantVowelConsonant(w) && !UnstressedEndings.Any(e => w.EndsWith(e, StringComparison.Ordinal)))
        {
            return true;
        }

        return Mono.Any(m => w.EndsWith(m, StringComparison.Ordinal) && Array.IndexOf(StressPrefixes, w[..^m.Length]) >= 0);
    }

    [GeneratedRegex("[aeiouy]+", RegexOptions.CultureInvariant)]
    private static partial Regex VowelRun();

    [GeneratedRegex("[^aeiou][aeiou][^aeiouwxy]$", RegexOptions.CultureInvariant)]
    private static partial Regex CvcEnd();

    [GeneratedRegex("[aeiou]{2}[^aeiouwxy]$", RegexOptions.CultureInvariant)]
    private static partial Regex VowelPairEnd();

    [GeneratedRegex("[^aeiou]y$", RegexOptions.CultureInvariant)]
    private static partial Regex ConsonantY();

    [GeneratedRegex("[aeiou]l$", RegexOptions.CultureInvariant)]
    private static partial Regex LiquidEnd();

    [GeneratedRegex("(y|ow|le|er|et|ure)$", RegexOptions.CultureInvariant)]
    private static partial Regex TwoSyllableEnding();

    [GeneratedRegex("(mit|fer|pel|cur|gin|bel|trol|quip|quit|gret)$", RegexOptions.CultureInvariant)]
    private static partial Regex StressedEnding();
}
