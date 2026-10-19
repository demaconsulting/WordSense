using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One irregular inflection of a word: the forms a lemma takes for one rule, such as <c>go</c> with the past form <c>went</c>.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Pos">The part of speech of the forms.</param>
/// <param name="Rule">The rule: <c>Past</c>, <c>Participle</c>, <c>Plural</c>, <c>Comparative</c>, <c>Superlative</c>, <c>Variant</c>, <c>Auxiliary</c> or <c>Modal</c>.</param>
/// <param name="Forms">The forms.</param>
/// <param name="Evidence">The evidence tokens of the row (one variant level per form in an AGID row).</param>
internal sealed record IrregularEntry(string Lemma, PartOfSpeech Pos, string Rule, string[] Forms, string[] Evidence);

/// <summary>
///     The irregular inflected forms of the dictionary words: a hand-written closed list of facts of the language
///     (<c>corpus/lexicon/irregular-forms.tsv</c>), read by the form generator and by the tests.
/// </summary>
/// <remarks>
///     <para>
///     The files of this kind have the columns lemma, part-of-speech letter, rule, space-separated forms and evidence; the
///     evidence of an AGID row holds one variant level per form.
///     </para>
///     <para>
///     The list derived from the Universal Dependencies English-EWT treebank (<c>corpus/lexicon/irregular-ud.tsv</c>,
///     written by the <c>ud-irregular</c> command) is a test oracle only: its license (CC BY-SA 4.0) is not compatible with the
///     data, so no reader here and no generator ever opens it. It holds every form-and-lemma pair of the training and
///     development files (never the test file) that occurs at least twice with an inflectional feature, whose lemma is a
///     dictionary word of that part of speech, that the dictionary does not list, and that the regular rules do not produce.
///     </para>
/// </remarks>
internal static class IrregularForms
{
    /// <summary>
    ///     The name of the hand-written list.
    /// </summary>
    public const string HandFile = "irregular-forms.tsv";

    /// <summary>The rule name of a form that is none of the named inflection rules.</summary>
    private const string OtherRule = "Other";

    /// <summary>
    ///     The name of the list derived from the treebank (test oracle only).
    /// </summary>
    public const string DerivedFile = "irregular-ud.tsv";

    private const int MinCount = 2;

    private static readonly string[] Files = ["en_ewt-ud-train.conllu", "en_ewt-ud-dev.conllu"];

    /// <summary>
    ///     Reads the hand-written irregular list of a lexicon directory.
    /// </summary>
    /// <param name="directory">The lexicon directory.</param>
    /// <returns>The entries; empty when the list does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<IrregularEntry> Read(string directory) => ReadFile(Path.Combine(directory, HandFile));

    /// <summary>
    ///     Reads one irregular list.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The entries; empty when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<IrregularEntry> ReadFile(string path)
    {
        var entries = new List<IrregularEntry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        var name = Path.GetFileName(path);
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 4 || columns[1].Length != 1 || LexiconSource.Letters.IndexOf(columns[1][0]) < 0)
            {
                throw new InvalidDataException($"{name} line {number.ToString(CultureInfo.InvariantCulture)}: expected 'lemma<TAB>letter<TAB>rule<TAB>forms<TAB>evidence'.");
            }

            entries.Add(new IrregularEntry(
                columns[0],
                (PartOfSpeech)LexiconSource.Letters.IndexOf(columns[1][0]),
                columns[2],
                columns[3].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                columns.Length > 4 ? columns[4].Split(' ', StringSplitOptions.RemoveEmptyEntries) : []));
        }

        return entries;
    }

    /// <summary>
    ///     Derives the irregular forms from the treebank and writes them.
    /// </summary>
    /// <param name="udDirectory">The directory with the treebank files.</param>
    /// <param name="corpus">The corpus directory with the dictionary.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>A summary line.</returns>
    /// <exception cref="InvalidDataException">Thrown when a treebank file is missing.</exception>
    public static string Derive(string udDirectory, string corpus, string output)
    {
        var lexicon = LexiconSource.ReadWords(Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile))
            .ToDictionary(w => w.Word, w => w.Mask, StringComparer.Ordinal);
        var counts = new Dictionary<(string Lemma, char Letter, string Rule, string Form), int>();
        foreach (var name in Files)
        {
            var path = Path.Combine(udDirectory, name);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"The treebank file '{path}' is missing; run ud-counts first.");
            }

            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var columns = line.Split('\t');
                if (columns.Length < 6 || columns[0].Contains('-') || columns[0].Contains('.'))
                {
                    continue;
                }

                var letter = columns[3] switch
                {
                    "NOUN" => 'N',
                    "VERB" or "AUX" => 'V',
                    "ADJ" => 'J',
                    "ADV" => 'B',
                    _ => ' ',
                };
                var form = columns[1].ToLowerInvariant();
                var lemma = columns[2].ToLowerInvariant();
                if (letter == ' ' || form == lemma || !IsWord(form) || !IsWord(lemma) || !lexicon.TryGetValue(lemma, out var mask) ||
                    (mask & (1 << LexiconSource.Letters.IndexOf(letter, StringComparison.Ordinal))) == 0)
                {
                    continue;
                }

                var key = (lemma, letter, RuleOf(columns[5], letter), form);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var regular = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var groups = new SortedDictionary<(string Lemma, char Letter, string Rule), List<(string Form, int Count)>>();
        foreach (var ((lemma, letter, rule, form), count) in counts.Where(c => c.Value >= MinCount && c.Key.Rule != OtherRule && !lexicon.ContainsKey(c.Key.Form)))
        {
            if (!regular.TryGetValue(lemma, out var forms))
            {
                regular[lemma] = forms = RegularForms(lemma, lexicon[lemma]);
            }

            if (forms.Contains(form) || forms.Any(f => Distance(f, form) <= 2))
            {
                continue;
            }

            if (!groups.TryGetValue((lemma, letter, rule), out var list))
            {
                groups[(lemma, letter, rule)] = list = [];
            }

            list.Add((form, count));
        }

        var text = new StringBuilder();
        text.AppendLine("# Irregular forms derived from the Universal Dependencies English-EWT treebank (CC BY-SA 4.0, training and development files only).");
        text.AppendLine("# Columns: lemma <TAB> part-of-speech letter <TAB> rule <TAB> forms (space separated) <TAB> evidence (occurrences)");
        text.AppendLine("# Written by the ud-irregular command of WordSense.Builder; see corpus/ud/PROVENANCE.md.");
        var pairs = 0;
        foreach (var ((lemma, letter, _), list) in groups)
        {
            var ordered = list.OrderBy(f => f.Form, StringComparer.Ordinal).ToList();
            pairs += ordered.Count;
            text.Append(lemma).Append('\t').Append(letter).Append('\t').Append("Variant")
                .Append('\t').Append(string.Join(' ', ordered.Select(f => f.Form)))
                .Append('\t').Append(string.Join(' ', ordered.Select(f => f.Count.ToString(CultureInfo.InvariantCulture)))).Append('\n');
        }

        File.WriteAllText(output, text.ToString(), new UTF8Encoding(false));
        return $"Wrote {pairs.ToString(CultureInfo.InvariantCulture)} irregular forms of {groups.Count.ToString(CultureInfo.InvariantCulture)} lemma entries to {output}.";
    }

    private static bool IsWord(string text) => text.Length >= 2 && text.All(c => c is >= 'a' and <= 'z');

    // The forms the regular rules produce for a word of the parts of speech in its mask, with their alternative spellings
    private static HashSet<string> RegularForms(string word, ushort mask)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        if ((mask & LexiconSource.Bit(PartOfSpeech.Noun)) != 0)
        {
            forms.UnionWith(Data.InflectionRules.NounPlural(word));
        }

        if ((mask & LexiconSource.Bit(PartOfSpeech.Verb)) != 0)
        {
            forms.Add(Data.InflectionRules.VerbThirdPerson(word));
            var verb = Data.InflectionRules.VerbRegular(word, _ => false);
            forms.UnionWith(verb.Past);
            forms.UnionWith(verb.Gerund);
            forms.UnionWith(verb.PastAlternative);
            forms.UnionWith(verb.GerundAlternative);
        }

        if ((mask & (LexiconSource.Bit(PartOfSpeech.Adjective) | LexiconSource.Bit(PartOfSpeech.Adverb))) != 0 && Data.InflectionRules.AdjectiveDegrees(word) is { } degrees)
        {
            forms.Add(degrees.Comparative);
            forms.Add(degrees.Superlative);
        }

        return forms;
    }

    // The edit distance of two words; a form this close to a regular form of its lemma is a misspelling, not an irregular form
    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }

    private static string RuleOf(string features, char letter)
    {
        if (letter == 'N')
        {
            return features.Contains("Number=Plur", StringComparison.Ordinal) ? "Plural" : OtherRule;
        }

        if (letter == 'V')
        {
            var past = features.Contains("Tense=Past", StringComparison.Ordinal);
            if (past && features.Contains("VerbForm=Part", StringComparison.Ordinal))
            {
                return "Participle";
            }

            return past ? "Past" : OtherRule;
        }

        if (features.Contains("Degree=Cmp", StringComparison.Ordinal))
        {
            return "Comparative";
        }

        return features.Contains("Degree=Sup", StringComparison.Ordinal) ? "Superlative" : OtherRule;
    }
}
