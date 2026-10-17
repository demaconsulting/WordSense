using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Generates the inflected forms of dictionary words, so that the word table holds every form it should know.
/// </summary>
/// <remarks>
///     <para>
///     The rules are the ones that reached the accuracy gate on real text, listed in
///     <c>corpus/lexicon/morphology.tsv</c> (a rule that is not <c>enabled</c> there is not applied):
///     </para>
///     <list type="bullet">
///     <item><description><c>Plural</c>: plural and third-person <c>-s</c>, <c>-es</c>, <c>-ies</c> of a base that is only a noun or only a verb, which gives that part of speech.</description></item>
///     <item><description><c>PastTense</c>: <c>-ed</c>, <c>-d</c>, <c>-ied</c> and doubled-consonant <c>-ed</c> of a base that can be a verb, which gives a verb.</description></item>
///     <item><description><c>Adverb</c>: <c>-ly</c> and <c>-ily</c> of a base that is only an adjective, which gives an adverb.</description></item>
///     </list>
///     <para>
///     A generated form never replaces a word that is already known, and the first base in ordinal order wins when two
///     bases produce the same form. Every generated form answers with the base as its lemma.
///     </para>
/// </remarks>
internal static class MorphologyExpander
{
    private const int MinBaseLength = 3;
    private const int MinFormLength = 4;

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
    /// <param name="bases">The dictionary words with their part-of-speech masks, in ordinal order; only these are bases.</param>
    /// <param name="known">Predicate telling whether a word is already known (a lemma, a form or a dictionary word).</param>
    /// <param name="enabled">The names of the enabled rules.</param>
    /// <param name="single">Gives the shared one-sense answer of a base and a part of speech.</param>
    /// <returns>The generated forms and their answers, in ordinal order.</returns>
    public static SortedDictionary<string, WordDetails[]> Expand(
        IReadOnlyList<(string Word, byte Mask)> bases,
        Func<string, bool> known,
        IReadOnlySet<string> enabled,
        Func<string, PartOfSpeech, WordDetails[]> single)
    {
        var result = new SortedDictionary<string, WordDetails[]>(StringComparer.Ordinal);
        void Add(string form, string lemma, PartOfSpeech pos)
        {
            if (form.Length >= MinFormLength && !known(form) && !result.ContainsKey(form))
            {
                result[form] = single(lemma, pos);
            }
        }

        var noun = LexiconSource.Bit(PartOfSpeech.Noun);
        var verb = LexiconSource.Bit(PartOfSpeech.Verb);
        var adjective = LexiconSource.Bit(PartOfSpeech.Adjective);
        foreach (var (word, mask) in bases.Where(b => b.Word.Length >= MinBaseLength))
        {
            if (enabled.Contains("Plural") && (mask == noun || mask == verb))
            {
                var pos = mask == noun ? PartOfSpeech.Noun : PartOfSpeech.Verb;
                foreach (var form in PluralForms(word).Where(f => !f.EndsWith("ss", StringComparison.Ordinal) && !f.EndsWith("us", StringComparison.Ordinal) && !f.EndsWith("is", StringComparison.Ordinal)))
                {
                    Add(form, word, pos);
                }
            }

            if (enabled.Contains("PastTense") && (mask & verb) != 0)
            {
                foreach (var form in PastForms(word))
                {
                    Add(form, word, PartOfSpeech.Verb);
                }
            }

            if (enabled.Contains("Adverb") && mask == adjective)
            {
                Add(word.EndsWith('y') ? word[..^1] + "ily" : word + "ly", word, PartOfSpeech.Adverb);
            }
        }

        return result;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static IEnumerable<string> PluralForms(string word)
    {
        yield return word + "s";
        if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            yield return word[..^1] + "ies";
        }

        if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith('o') ||
            word.EndsWith("ch", StringComparison.Ordinal) || word.EndsWith("sh", StringComparison.Ordinal))
        {
            yield return word + "es";
        }
    }

    private static IEnumerable<string> PastForms(string word)
    {
        yield return word.EndsWith('e') ? word + "d" : word + "ed";
        if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            yield return word[..^1] + "ied";
        }

        // A short consonant-vowel-consonant ending doubles the final consonant
        if (word.Length >= 3 && !IsVowel(word[^1]) && word[^1] is not ('w' or 'x' or 'y') && IsVowel(word[^2]) && !IsVowel(word[^3]))
        {
            yield return word + word[^1] + "ed";
        }
    }
}
