using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One irregular inflection of a word: the forms a lemma takes for one rule, such as <c>go</c> with the past form <c>went</c>.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Pos">The part of speech of the forms.</param>
/// <param name="Rule">The rule: <c>Past</c>, <c>Participle</c>, <c>Plural</c>, <c>Comparative</c>, <c>Superlative</c>, <c>Variant</c> or <c>Other</c>.</param>
/// <param name="Forms">The forms.</param>
internal sealed record IrregularEntry(string Lemma, PartOfSpeech Pos, string Rule, string[] Forms);

/// <summary>
///     Generates the inflected forms of dictionary words, so that the word table holds every form it should know.
/// </summary>
/// <remarks>
///     <para>
///     The rules are listed in <c>corpus/lexicon/morphology.tsv</c> (a rule that is not <c>enabled</c> there is not applied):
///     </para>
///     <list type="bullet">
///     <item><description><c>Plural</c>: <c>-s</c>, <c>-es</c> and <c>-ies</c> of a base that can be a noun, which gives a noun.</description></item>
///     <item><description><c>ThirdPerson</c>: the same endings of a base that can be a verb, which gives a verb.</description></item>
///     <item><description><c>PastTense</c>: <c>-ed</c>, <c>-d</c>, <c>-ied</c> and doubled-consonant <c>-ed</c> of a base that can be a verb, which gives a verb.</description></item>
///     <item><description><c>Gerund</c>: <c>-ing</c> (with the dropped <c>e</c>, <c>ying</c> and doubled consonant) of a base that can be a verb, which gives a verb.</description></item>
///     <item><description><c>Comparative</c> and <c>Superlative</c>: <c>-er</c> and <c>-est</c> of a short adjective, which give an adjective (and an adverb when the base is one too).</description></item>
///     <item><description><c>Adverb</c>: <c>-ly</c>, <c>-ily</c> and <c>-ically</c> of an adjective that is not an adverb, which gives an adverb.</description></item>
///     <item><description><c>Irregular</c>: the forms of the irregular lists (<see cref="IrregularForms"/>), and the forms of a verb that is an irregular verb with a prefix.</description></item>
///     </list>
///     <para>
///     A word that is itself an inflected form of another dictionary word (<c>looked</c>, <c>bigger</c>) is not inflected
///     again, apart from the plural of an <c>-ing</c> noun. A regular form that an irregular entry replaces (<c>goed</c>,
///     <c>childs</c>) is not generated. A generated form never replaces a word that is already known, and the first
///     base in ordinal order wins when two bases produce the same form. Every generated form answers with its base as
///     its lemma and one sense for each part of speech it can have.
///     </para>
/// </remarks>
internal static class MorphologyExpander
{
    private const int MinBaseLength = 3;
    private const int MinFormLength = 3;

    private static readonly string[] Prefixes =
        ["be", "for", "fore", "mis", "out", "over", "re", "under", "up", "with", "un", "off", "inter", "counter", "down"];

    // Verbs of two or more syllables whose last syllable is stressed, so that the final consonant doubles before a suffix
    private static readonly string[] StressedFinal =
    [
        "admit", "commit", "emit", "omit", "permit", "remit", "submit", "transmit", "begin", "refer", "prefer", "defer", "infer", "confer",
        "transfer", "occur", "recur", "incur", "concur", "deter", "demur", "regret", "forget", "beget", "upset", "reset", "offset", "outfit",
        "control", "patrol", "enrol", "extol", "compel", "dispel", "expel", "impel", "propel", "repel", "rebel", "excel", "annul", "equip",
        "forbid", "outbid", "kidnap", "handicap", "wiretap", "outwit", "bootleg", "hobnob", "humbug", "zigzag", "overrun", "outrun",
        "rerun", "unwrap", "entrap", "enwrap", "unplug", "unzip", "debug", "refit", "recap", "remap", "outstrip", "overstep", "inset",
        "unfit", "uproot", "backslap", "blackball", "bobsled", "overlap", "preset", "unclog", "unrig", "unbar", "unstop", "unpin",
        "unknit", "unclip", "unstrap", "upend", "inlay", "worship", "format", "program", "bias",
    ];

    // Verbs and nouns that are the irregular form of another word and also a word of their own, which inflect regularly
    private static readonly HashSet<string> RegularToo = new(
        ["found", "saw", "bound", "ground", "wound", "bore", "fell", "felt", "rose", "left", "lay", "bit", "bent", "shed", "lit", "hung", "bid", "stuck", "spat", "wove", "thought", "spoke", "drove", "rung"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> CompoundLemmas = new(["man", "woman", "child", "foot", "tooth", "mouse", "louse"], StringComparer.Ordinal);

    // Adverbs of one syllable that have no comparative
    private static readonly HashSet<string> ClosedAdverbs = new(
        ["now", "then", "here", "there", "thus", "too", "how", "why", "when", "where", "once", "twice", "yet", "not", "also", "very", "just", "only", "ever", "else", "hence", "thence", "whence", "quite", "both", "away", "back", "off", "out", "down", "up", "in", "on", "over", "under", "forth", "ago", "even", "such", "so", "as", "than", "though", "since", "still", "much", "more", "most", "less", "least", "well", "best", "better"],
        StringComparer.Ordinal);

    private static readonly string[] HeavySuffixes =
        ["ed", "ing", "ful", "less", "ous", "ive", "al", "ic", "ist", "ish", "ar", "or", "able", "ible", "ern", "ese", "ian", "an", "ory", "ary", "ate", "ual", "ose", "ment", "ness", "ity"];

    [Flags]
    private enum Derived
    {
        None = 0,
        Gerund = 1,
        Past = 2,
        Plural = 4,
        Comparative = 8,
        Adverb = 16,
    }

    /// <summary>
    ///     Reads the enabled rule names of a morphology file (<c>rule</c>, <c>state</c>, <c>evidence</c>).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The names of the enabled rules; empty when the file does not exist.</returns>
    public static HashSet<string> ReadEnabled(string path)
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return enabled;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length >= 2 && columns[1] == "enabled")
            {
                enabled.Add(columns[0]);
            }
        }

        return enabled;
    }

    /// <summary>
    ///     Generates the inflected forms of dictionary words.
    /// </summary>
    /// <param name="bases">The dictionary words with their part-of-speech masks and sense-ordered letters, in ordinal order; only these are bases.</param>
    /// <param name="known">Predicate telling whether a word is already known (a lemma, a form or a dictionary word).</param>
    /// <param name="enabled">The names of the enabled rules.</param>
    /// <param name="irregular">The irregular entries.</param>
    /// <param name="answer">Gives the answer of a generated form from its lemma and its parts of speech.</param>
    /// <returns>The generated forms and their answers, in ordinal order.</returns>
    public static SortedDictionary<string, LegacyWord[]> Expand(
        IReadOnlyList<(string Word, ushort Mask, string Letters)> bases,
        Func<string, bool> known,
        IReadOnlySet<string> enabled,
        IReadOnlyList<IrregularEntry> irregular,
        Func<string, IReadOnlyList<PartOfSpeech>, LegacyWord[]> answer)
    {
        var masks = new Dictionary<string, ushort>(bases.Count, StringComparer.Ordinal);
        var letters = new Dictionary<string, string>(bases.Count, StringComparer.Ordinal);
        foreach (var (word, mask, order) in bases)
        {
            masks[word] = mask;
            letters[word] = order;
        }

        var candidates = new Dictionary<string, (string Lemma, List<PartOfSpeech> Pos)>(StringComparer.Ordinal);
        void Add(string form, string lemma, PartOfSpeech pos)
        {
            if (candidates.TryGetValue(form, out var existing))
            {
                if (string.Equals(existing.Lemma, lemma, StringComparison.Ordinal) && !existing.Pos.Contains(pos))
                {
                    existing.Pos.Add(pos);
                }
            }
            else
            {
                candidates[form] = (lemma, [pos]);
            }
        }

        // The irregular entries come first: their lemma wins, and they say which regular forms are not generated
        var irregularForms = new HashSet<string>(StringComparer.Ordinal);
        var suppressed = new HashSet<(string Lemma, string Rule)>();
        if (enabled.Contains("Irregular"))
        {
            var irregularVerbs = new Dictionary<string, List<IrregularEntry>>(StringComparer.Ordinal);
            foreach (var entry in irregular.Where(e => masks.TryGetValue(e.Lemma, out var m) && (m & LexiconSource.Bit(e.Pos)) != 0))
            {
                foreach (var form in entry.Forms)
                {
                    Add(form, entry.Lemma, entry.Pos);
                    if (entry.Rule != "Variant" && !string.Equals(form, entry.Lemma, StringComparison.Ordinal))
                    {
                        irregularForms.Add(form);
                    }
                }

                if (entry.Rule != "Variant")
                {
                    suppressed.Add((entry.Lemma, Group(entry.Rule)));
                }

                if (entry.Pos == PartOfSpeech.Verb && entry.Rule is "Past" or "Participle")
                {
                    if (!irregularVerbs.TryGetValue(entry.Lemma, out var list))
                    {
                        irregularVerbs[entry.Lemma] = list = [];
                    }

                    list.Add(entry);
                }
            }

            // A verb that is an irregular verb after a prefix takes the prefixed forms (undo, undid, undone)
            var verbBit = LexiconSource.Bit(PartOfSpeech.Verb);
            foreach (var (word, _, _) in bases.Where(b => (b.Mask & verbBit) != 0 && !irregularVerbs.ContainsKey(b.Word)))
            {
                foreach (var prefix in Prefixes.Where(p => word.StartsWith(p, StringComparison.Ordinal) && word.Length > p.Length + 1))
                {
                    if (irregularVerbs.TryGetValue(word[prefix.Length..], out var entries))
                    {
                        foreach (var form in entries.SelectMany(e => e.Forms))
                        {
                            Add(prefix + form, word, PartOfSpeech.Verb);
                        }

                        break;
                    }
                }
            }
        }

        // A noun that ends in an irregular noun after a word takes the compound plural (workman, workmen)
        var compoundNouns = irregular
            .Where(e => e.Pos == PartOfSpeech.Noun && e.Rule == "Plural" && enabled.Contains("Irregular") && masks.ContainsKey(e.Lemma) && CompoundLemmas.Contains(e.Lemma))
            .ToList();
        var nounBit = LexiconSource.Bit(PartOfSpeech.Noun);
        foreach (var (word, mask, _) in bases.Where(b => b.Word.Length >= MinBaseLength))
        {
            var noPlural = suppressed.Contains((word, "Plural"));
            if ((mask & nounBit) != 0 && !noPlural && enabled.Contains("Plural"))
            {
                foreach (var entry in compoundNouns.Where(e => word.Length >= e.Lemma.Length + 3 && word.EndsWith(e.Lemma, StringComparison.Ordinal)))
                {
                    var stem = word[..^entry.Lemma.Length];
                    if (masks.ContainsKey(stem))
                    {
                        foreach (var form in entry.Forms)
                        {
                            Add(stem + form, word, PartOfSpeech.Noun);
                        }

                        noPlural = true;
                    }
                }
            }

            var flags = new Flags(
                enabled,
                Classify(word, masks),
                irregularForms.Contains(word),
                noPlural,
                suppressed.Contains((word, "Past")),
                suppressed.Contains((word, "Comparative")));
            foreach (var (form, pos) in Generate(word, mask, flags))
            {
                Add(form, word, pos);
            }
        }

        var result = new SortedDictionary<string, LegacyWord[]>(StringComparer.Ordinal);
        foreach (var (form, (lemma, pos)) in candidates)
        {
            if (form.Length < MinFormLength || known(form))
            {
                continue;
            }

            var order = letters.GetValueOrDefault(lemma, string.Empty);
            var sorted = pos
                .OrderBy(p =>
                {
                    var index = order.IndexOf(LexiconSource.Letters[(int)p], StringComparison.Ordinal);
                    return index < 0 ? int.MaxValue : index;
                })
                .ToArray();
            result[form] = answer(lemma, sorted);
        }

        return result;
    }

    /// <summary>
    ///     Generates the regular forms of one word, without the checks that need the whole dictionary.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="mask">The part-of-speech mask of the word.</param>
    /// <returns>The forms with their parts of speech.</returns>
    public static IEnumerable<(string Form, PartOfSpeech Pos)> RegularForms(string word, ushort mask)
    {
        var all = new HashSet<string>(["Plural", "ThirdPerson", "PastTense", "Gerund", "Comparative", "Superlative", "Adverb"], StringComparer.Ordinal);
        return Generate(word, mask, new Flags(all, Derived.None, false, false, false, false));
    }

    private static string Group(string rule) => rule is "Comparative" or "Superlative" ? "Comparative" : rule;

    private sealed record Flags(
        IReadOnlySet<string> Enabled,
        Derived Derived,
        bool IrregularForm,
        bool NoPlural,
        bool NoPast,
        bool NoComparative);

    private static IEnumerable<(string Form, PartOfSpeech Pos)> Generate(string word, ushort mask, Flags flags)
    {
        var noun = LexiconSource.Bit(PartOfSpeech.Noun);
        var verb = LexiconSource.Bit(PartOfSpeech.Verb);
        var adjective = LexiconSource.Bit(PartOfSpeech.Adjective);
        var adverb = LexiconSource.Bit(PartOfSpeech.Adverb);
        var derived = flags.Derived;
        var enabled = flags.Enabled;

        // The plural of a noun is not generated for an inflected form, except for an -ing noun
        var inflectedForm = (derived & (Derived.Plural | Derived.Past | Derived.Adverb)) != 0;
        var pureIrregular = flags.IrregularForm && !RegularToo.Contains(word);
        if ((mask & noun) != 0 && enabled.Contains("Plural") && !flags.NoPlural && !inflectedForm && !pureIrregular && !word.EndsWith("is", StringComparison.Ordinal))
        {
            foreach (var form in SForms(word))
            {
                yield return (form, PartOfSpeech.Noun);
            }
        }

        if ((mask & verb) != 0 && (derived & (Derived.Gerund | Derived.Past | Derived.Plural)) == 0 && !pureIrregular)
        {
            if (enabled.Contains("ThirdPerson"))
            {
                foreach (var form in SForms(word))
                {
                    yield return (form, PartOfSpeech.Verb);
                }
            }

            if (enabled.Contains("PastTense") && !flags.NoPast)
            {
                foreach (var form in Suffixed(word, "ed"))
                {
                    yield return (form, PartOfSpeech.Verb);
                }
            }

            if (enabled.Contains("Gerund"))
            {
                foreach (var form in Suffixed(word, "ing"))
                {
                    yield return (form, PartOfSpeech.Verb);
                    if (enabled.Contains("Manner") && Syllables(word) <= 3 && !form.EndsWith("ying", StringComparison.Ordinal))
                    {
                        yield return (form + "ly", PartOfSpeech.Adverb);
                    }
                }
            }

            if (enabled.Contains("Agent") && Syllables(word) <= 3 && !word.EndsWith("er", StringComparison.Ordinal))
            {
                foreach (var form in Suffixed(word, "er"))
                {
                    yield return (form, PartOfSpeech.Noun);
                    foreach (var plural in SForms(form))
                    {
                        yield return (plural, PartOfSpeech.Noun);
                    }
                }
            }
        }

        var inflectedAdjective = (derived & (Derived.Comparative | Derived.Adverb | Derived.Past | Derived.Gerund)) != 0;
        var comparable = (mask & adjective) != 0 || ((mask & adverb) != 0 && Syllables(word) == 1 && !ClosedAdverbs.Contains(word));
        if (comparable && !inflectedAdjective && !flags.IrregularForm && !flags.NoComparative && Plausible(word))
        {
            var pos = new List<PartOfSpeech>(2);
            if ((mask & adjective) != 0)
            {
                pos.Add(PartOfSpeech.Adjective);
            }

            if ((mask & adverb) != 0)
            {
                pos.Add(PartOfSpeech.Adverb);
            }

            foreach (var (suffix, rule) in new[] { ("er", "Comparative"), ("est", "Superlative") })
            {
                if (!enabled.Contains(rule))
                {
                    continue;
                }

                foreach (var form in Suffixed(word, suffix))
                {
                    foreach (var p in pos)
                    {
                        yield return (form, p);
                    }
                }
            }
        }

        if ((mask & adjective) != 0 && enabled.Contains("Adverb") && (derived & Derived.Adverb) == 0 && !flags.IrregularForm &&
            !word.EndsWith("ly", StringComparison.Ordinal))
        {
            yield return (AdverbOf(word), PartOfSpeech.Adverb);
        }

        if ((mask & adjective) != 0 && enabled.Contains("Quality") && (derived & (Derived.Adverb | Derived.Past | Derived.Gerund | Derived.Comparative)) == 0 &&
            !flags.IrregularForm && Syllables(word) <= 3 && !word.EndsWith("ness", StringComparison.Ordinal) && !word.EndsWith("ly", StringComparison.Ordinal))
        {
            yield return (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]) ? word[..^1] + "iness" : word + "ness", PartOfSpeech.Noun);
        }
    }

    private static string AdverbOf(string word)
    {
        if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            return word[..^1] + "ily";
        }

        if (word.EndsWith("ic", StringComparison.Ordinal))
        {
            return word + "ally";
        }

        if (word.EndsWith("ll", StringComparison.Ordinal))
        {
            return word + "y";
        }

        if (word.EndsWith("le", StringComparison.Ordinal) && word.Length > 2 && !IsVowel(word[^3]))
        {
            return word[..^1] + "y";
        }

        return word.EndsWith("ue", StringComparison.Ordinal) ? word[..^1] + "ly" : word + "ly";
    }

    private static bool Plausible(string word)
    {
        var syllables = Syllables(word);
        return syllables == 1 || (syllables == 2 && word.Length <= 9 && !HeavySuffixes.Any(s => word.EndsWith(s, StringComparison.Ordinal)));
    }

    private static int Syllables(string word)
    {
        var count = 0;
        var inVowel = false;
        for (var i = 0; i < word.Length; i++)
        {
            var vowel = IsVowel(word[i]) || (word[i] == 'y' && i > 0);
            if (vowel && !inVowel)
            {
                count++;
            }

            inVowel = vowel;
        }

        var silentE = word.EndsWith('e') && word.Length > 2 && !(word.EndsWith("le", StringComparison.Ordinal) && !IsVowel(word[^3]));
        return silentE && count > 1 ? count - 1 : Math.Max(count, 1);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    // The u of qu is a consonant
    private static bool VowelAt(string word, int index) => IsVowel(word[index]) && !(word[index] == 'u' && index > 0 && word[index - 1] == 'q');

    private static bool EndsConsonantVowelConsonant(string word) =>
        word.Length >= 3 && !IsVowel(word[^1]) && word[^1] is not ('w' or 'x' or 'y') && VowelAt(word, word.Length - 2) &&
        !VowelAt(word, word.Length - 3);

    private static bool FinalStressed(string word)
    {
        if (Syllables(word) == 1)
        {
            return true;
        }

        return StressedFinal.Any(item => word.EndsWith(item, StringComparison.Ordinal) &&
            (word.Length == item.Length || Array.IndexOf(Prefixes, word[..^item.Length]) >= 0));
    }

    private static IEnumerable<string> SForms(string word)
    {
        if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            yield return word[..^1] + "ies";
        }
        else if (word.EndsWith("ch", StringComparison.Ordinal))
        {
            // church, churches; but monarch, monarchs
            yield return word + "es";
            yield return word + "s";
        }
        else if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith("sh", StringComparison.Ordinal))
        {
            yield return word + "es";
        }
        else if (word.EndsWith('o') && word.Length > 1 && !IsVowel(word[^2]))
        {
            yield return word + "s";
            yield return word + "es";
        }
        else
        {
            yield return word + "s";
        }
    }

    // Adds a vowel-initial suffix (ed, ing, er, est) with the spelling changes of English
    private static IEnumerable<string> Suffixed(string word, string suffix)
    {
        var ing = suffix == "ing";
        if (ing && word.EndsWith("ie", StringComparison.Ordinal))
        {
            yield return word[..^2] + "ying";
            yield break;
        }

        if (word.EndsWith('e'))
        {
            if (!ing)
            {
                yield return word + suffix[1..];
            }
            else
            {
                yield return word.EndsWith("ee", StringComparison.Ordinal) || word.EndsWith("ye", StringComparison.Ordinal) || word.EndsWith("oe", StringComparison.Ordinal)
                    ? word + "ing"
                    : word[..^1] + "ing";
            }

            yield break;
        }

        if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            yield return ing ? word + "ing" : word[..^1] + "i" + suffix;
            yield break;
        }

        if (word.EndsWith("ic", StringComparison.Ordinal) && (ing || suffix == "ed"))
        {
            yield return word + "k" + suffix;
            yield break;
        }

        if (EndsConsonantVowelConsonant(word))
        {
            if (FinalStressed(word))
            {
                yield return word + word[^1] + suffix;
                yield break;
            }

            yield return word + suffix;
            if (word[^1] == 'l' && (ing || suffix == "ed"))
            {
                yield return word + "l" + suffix;
            }

            yield break;
        }

        yield return word + suffix;
    }

    private static Derived Classify(string word, IReadOnlyDictionary<string, ushort> masks)
    {
        var verb = LexiconSource.Bit(PartOfSpeech.Verb);
        var noun = LexiconSource.Bit(PartOfSpeech.Noun);
        var adjective = LexiconSource.Bit(PartOfSpeech.Adjective);
        bool Has(string stem, ushort bits) => stem.Length >= 3 && masks.TryGetValue(stem, out var m) && (m & bits) != 0;
        bool HasUndoubled(string stem, ushort bits) => stem.Length >= 4 && stem[^1] == stem[^2] && Has(stem[..^1], bits);
        var d = Derived.None;
        if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length >= 5)
        {
            var stem = word[..^3];
            if (Has(stem, verb) || Has(stem + "e", verb) || HasUndoubled(stem, verb) || (word.EndsWith("ying", StringComparison.Ordinal) && Has(word[..^4] + "ie", verb)))
            {
                d |= Derived.Gerund;
            }
        }

        if (word.EndsWith("ed", StringComparison.Ordinal) && !word.EndsWith("eed", StringComparison.Ordinal) && word.Length >= 5)
        {
            var stem = word[..^2];
            if (Has(stem, verb) || Has(stem + "e", verb) || HasUndoubled(stem, verb) || (word.EndsWith("ied", StringComparison.Ordinal) && Has(word[..^3] + "y", verb)))
            {
                d |= Derived.Past;
            }
        }

        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) && word.Length >= 4)
        {
            var both = (ushort)(noun | verb);
            if (Has(word[..^1], both) || (word.EndsWith("es", StringComparison.Ordinal) && Has(word[..^2], both)) ||
                (word.EndsWith("ies", StringComparison.Ordinal) && Has(word[..^3] + "y", both)))
            {
                d |= Derived.Plural;
            }
        }

        if ((word.EndsWith("er", StringComparison.Ordinal) || word.EndsWith("est", StringComparison.Ordinal)) && word.Length >= 5)
        {
            var stem = word[..^(word.EndsWith("er", StringComparison.Ordinal) ? 2 : 3)];
            if (Has(stem, adjective) || Has(stem + "e", adjective) || HasUndoubled(stem, adjective) || (stem.EndsWith('i') && Has(stem[..^1] + "y", adjective)))
            {
                d |= Derived.Comparative;
            }
        }

        if (word.EndsWith("ly", StringComparison.Ordinal) && word.Length >= 5)
        {
            var stem = word[..^2];
            if (Has(stem, adjective) || Has(stem + "e", adjective) || Has(word[..^1], adjective) || (stem.EndsWith('i') && Has(stem[..^1] + "y", adjective)) ||
                (stem.EndsWith("ical", StringComparison.Ordinal) && Has(stem[..^2], adjective)) || Has(stem[..^1] + "le", adjective))
            {
                d |= Derived.Adverb;
            }
        }

        return d;
    }
}
