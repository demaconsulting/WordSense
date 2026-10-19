using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder.Data;

/// <summary>
///     Derives the irregular forms of the Automatically Generated Inflection Database (<c>infl.txt</c>) for the dictionary words.
/// </summary>
/// <remarks>
///     Only the forms that the regular rules do not produce are kept, for dictionary words with the part of speech, at the
///     variant levels 0 and 1; forms marked as unlikely or as a different word (<c>!</c>, <c>~</c>) and level 2 are dropped.
///     The database is gap-fill data: it ranks below the written forms of the Open English WordNet.
/// </remarks>
internal static partial class AgidImporter
{
    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex Word();

    /// <summary>
    ///     Reads the database file and writes the list of irregular forms.
    /// </summary>
    /// <param name="input">The <c>infl.txt</c> file.</param>
    /// <param name="corpus">The corpus directory with the dictionary.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>A summary line.</returns>
    /// <exception cref="InvalidDataException">Thrown when the input is missing.</exception>
    public static string Import(string input, string corpus, string output)
    {
        if (!File.Exists(input))
        {
            throw new InvalidDataException($"The inflection database file '{input}' does not exist.");
        }

        var moby = LexiconSource.ReadWords(Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile))
            .ToDictionary(w => w.Word, w => w.Letters, StringComparer.Ordinal);
        var lines = new SortedDictionary<(string Lemma, string Letter, string Rule), List<(string Form, string Level)>>();
        foreach (var raw in File.ReadLines(input, Encoding.Latin1))
        {
            var colon = raw.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var head = raw[..colon].Split(' ');
            if (head.Length != 2 || !moby.TryGetValue(head[0], out var letters) || !Word().IsMatch(head[0]))
            {
                continue;
            }

            var lemma = head[0];
            var slots = raw[(colon + 2)..].Split(" | ").Select(Slot).ToList();
            var targets = head[1] switch
            {
                "N" => [('N', "Plural", 0)],
                "A" => new[] { ('J', "Comparative", 0), ('J', "Superlative", 1), ('B', "Comparative", 0), ('B', "Superlative", 1) },
                "V" when slots.Count >= 4 => [('V', "Past", 0), ('V', "Participle", 1), ('V', "Gerund", 2), ('V', "Third", 3)],
                "V" when slots.Count == 3 => [('V', "Past", 0), ('V', "Participle", 0), ('V', "Gerund", 1), ('V', "Third", 2)],
                _ => Array.Empty<(char, string, int)>(),
            };
            foreach (var (letter, rule, slot) in targets)
            {
                if (!letters.Contains(letter, StringComparison.Ordinal) || slot >= slots.Count)
                {
                    continue;
                }

                var regular = Regular(lemma, letter, moby);
                foreach (var (form, level) in slots[slot])
                {
                    if (regular.Contains(form) || !Word().IsMatch(form) || level is "2" or "?")
                    {
                        continue;
                    }

                    if (!lines.TryGetValue((lemma, letter.ToString(), rule), out var list))
                    {
                        lines[(lemma, letter.ToString(), rule)] = list = [];
                    }

                    if (list.All(l => l.Form != form))
                    {
                        list.Add((form, level));
                    }
                }
            }
        }

        var text = new StringBuilder();
        text.Append("# Irregular forms taken from the Automatically Generated Inflection Database (AGID 2016.01.19) by Kevin Atkinson; see LICENSE-AGID.txt.\n");
        text.Append("# Columns: lemma <TAB> part-of-speech letter <TAB> rule <TAB> forms (space separated) <TAB> variant level of each form (0 primary, 1 variant, ? unlikely)\n");
        text.Append("# Written by the import-agid command of WordSense.Builder; only forms that the regular rules do not produce, for dictionary words.\n");
        var forms = 0;
        foreach (var ((lemma, letter, rule), list) in lines)
        {
            forms += list.Count;
            text.Append(lemma).Append('\t').Append(letter).Append('\t').Append(rule).Append('\t')
                .AppendJoin(' ', list.Select(l => l.Form)).Append('\t').AppendJoin(' ', list.Select(l => l.Level)).Append('\n');
        }

        File.WriteAllText(output, text.ToString(), new UTF8Encoding(false));
        return $"Wrote {forms.ToString(CultureInfo.InvariantCulture)} forms of {lines.Count.ToString(CultureInfo.InvariantCulture)} lemma rules to {output}.";
    }

    /// <summary>
    ///     Reads the spellings that the database attests as forms of a lemma (any slot, without the marked and unlikely forms).
    /// </summary>
    /// <param name="input">The <c>infl.txt</c> file, or null.</param>
    /// <returns>The lemmas by attested spelling.</returns>
    public static Dictionary<string, HashSet<string>> ReadAttested(string? input)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (input is null || !File.Exists(input))
        {
            return result;
        }

        foreach (var raw in File.ReadLines(input, Encoding.Latin1))
        {
            var colon = raw.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var head = raw[..colon].Split(' ');
            if (head.Length != 2 || !Word().IsMatch(head[0]))
            {
                continue;
            }

            foreach (var slot in raw[(colon + 2)..].Split(" | ").Select(Slot))
            {
                foreach (var form in slot.Where(s => s.Level is "0" or "1" && Word().IsMatch(s.Form)).Select(s => s.Form))
                {
                    if (!result.TryGetValue(form, out var lemmas))
                    {
                        result[form] = lemmas = new HashSet<string>(StringComparer.Ordinal);
                    }

                    lemmas.Add(head[0]);
                    lemmas.Add(head[0] + "/" + head[1].ToLowerInvariant());
                }
            }
        }

        return result;
    }

    // The forms of one slot with their variant levels; marked forms are dropped
    private static List<(string Form, string Level)> Slot(string text)
    {
        var result = new List<(string, string)>();
        foreach (var part in Regex.Replace(text, @"\{[^}]*\}", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5)).Split(','))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var form = tokens[0];
            if (form.Contains('!', StringComparison.Ordinal) || form.Contains('~', StringComparison.Ordinal) || form.Contains('<', StringComparison.Ordinal) || form.Contains('>', StringComparison.Ordinal))
            {
                continue;
            }

            string level;
            if (form.EndsWith('?'))
            {
                level = "?";
            }
            else
            {
                level = tokens.Length > 1 && tokens[1] is "0" or "1" or "2" ? tokens[1] : "0";
            }

            result.Add((form.TrimEnd('?'), level));
        }

        return result;
    }

    private static HashSet<string> Regular(string lemma, char letter, Dictionary<string, string> moby)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        switch (letter)
        {
            case 'N':
                forms.UnionWith(InflectionRules.NounPlural(lemma));
                break;
            case 'V':
                var regular = InflectionRules.VerbRegular(lemma, moby.ContainsKey);
                forms.Add(InflectionRules.VerbThirdPerson(lemma));
                forms.UnionWith(regular.Past);
                forms.UnionWith(regular.PastAlternative);
                forms.UnionWith(regular.Gerund);
                forms.UnionWith(regular.GerundAlternative);
                break;
            default:
                if (InflectionRules.AdjectiveDegrees(lemma) is { } d)
                {
                    forms.Add(d.Comparative);
                    forms.Add(d.Superlative);
                }

                break;
        }

        return forms;
    }
}
