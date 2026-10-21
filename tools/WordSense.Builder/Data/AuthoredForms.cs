using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The inflected forms that were written for this project (<c>corpus/lexicon/forms-authored.tsv</c>, CC0 1.0): the source of
///     irregular forms and the attestation of generated forms that replaced the Automatically Generated Inflection Database.
/// </summary>
/// <remarks>
///     <para>
///     The file has the columns lemma, part-of-speech letter (<c>N</c>, <c>V</c>, <c>J</c>, <c>B</c>), rule, space-separated forms
///     and the level of each form (0 primary, 1 variant). It holds every authored form, regular or irregular, and serves two
///     roles. As an irregular source it gives the primary forms of an entry that no hand, closed or written list names
///     (provenance <c>llm</c> for level 0, <c>rule_variant</c> for level 1), and it is authoritative like those lists: the regular
///     spelling of that kind stays only when the authors name it. As an attestation map it says which spelling is a form of which
///     lemma and part of speech, so that a generated form (a rule or reverse-rule analysis, a prefixed irregular verb) ships when the
///     authors wrote it.
///     </para>
///     <para>
///     The provenance sidecar (<c>forms-authored-provenance.tsv</c>) holds one row per decision, including the answer that the
///     form does not exist. A decision on which both authors agreed that there is no such form is read as a veto: the regular rules
///     do not generate that form for the lemma.
///     </para>
/// </remarks>
internal sealed class AuthoredForms
{
    /// <summary>
    ///     The name of the authored forms.
    /// </summary>
    public const string FormsFile = "forms-authored.tsv";

    /// <summary>
    ///     The name of the provenance of the authored forms.
    /// </summary>
    public const string ProvenanceFile = "forms-authored-provenance.tsv";

    /// <summary>
    ///     Gets the authored forms by lower-case lemma and part-of-speech letter.
    /// </summary>
    public Dictionary<(string Lemma, string Pos), IrregularKinds> Forms { get; } = [];

    /// <summary>
    ///     Gets the decisions of both authors that a form does not exist, by lower-case lemma, part-of-speech letter and rule.
    /// </summary>
    public HashSet<(string Lemma, string Pos, string Rule)> NoForm { get; } = [];

    /// <summary>
    ///     Gets the lemma and the lemma with its part of speech (<c>bag/n</c>; adverbs file under <c>a</c>) of every authored spelling.
    /// </summary>
    public Dictionary<string, HashSet<string>> Attested { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Reads the authored forms and their provenance of a lexicon directory; a missing file is an empty list.
    /// </summary>
    /// <param name="lexicon">The lexicon directory.</param>
    /// <returns>The authored forms.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static AuthoredForms Read(string lexicon)
    {
        var authored = new AuthoredForms();
        foreach (var entry in IrregularForms.ReadFile(Path.Join(lexicon, FormsFile)))
        {
            var pos = IrregularSources.Letter(entry.Pos);
            var lemma = entry.Lemma.ToLowerInvariant();
            if (!authored.Forms.TryGetValue((lemma, pos), out var kinds))
            {
                authored.Forms[(lemma, pos)] = kinds = new IrregularKinds();
            }

            var list = kinds.Of(entry.Rule) ?? throw new InvalidDataException($"{FormsFile}: the rule '{entry.Rule}' of '{entry.Lemma}' is not known.");
            if (entry.Evidence.Length != entry.Forms.Length || entry.Evidence.Any(l => l is not ("0" or "1")))
            {
                throw new InvalidDataException($"{FormsFile}: the levels of '{entry.Lemma}' ({entry.Rule}) are not one 0 or 1 for each form.");
            }

            var tag = pos is "n" or "v" ? pos : "a";
            for (var i = 0; i < entry.Forms.Length; i++)
            {
                list.Add((entry.Forms[i], entry.Evidence[i]));
                var low = entry.Forms[i].ToLowerInvariant();
                if (!authored.Attested.TryGetValue(low, out var lemmas))
                {
                    authored.Attested[low] = lemmas = new HashSet<string>(StringComparer.Ordinal);
                }

                lemmas.Add(lemma);
                lemmas.Add(lemma + "/" + tag);
            }
        }

        authored.ReadDecisions(Path.Join(lexicon, ProvenanceFile));
        return authored;
    }

    /// <summary>
    ///     Gets the lemma and part of speech of every authored entry with a form that the regular rules do not give.
    /// </summary>
    /// <returns>The keys.</returns>
    public HashSet<(string Lemma, string Pos)> IrregularKeys()
    {
        var keys = new HashSet<(string, string)>();
        foreach (var ((lemma, pos), kinds) in Forms)
        {
            if (Differs(lemma, pos, kinds))
            {
                keys.Add((lemma, pos));
            }
        }

        return keys;
    }

    private static bool Differs(string lemma, string pos, IrregularKinds kinds)
    {
        var verb = pos == "v" ? InflectionRules.VerbRegular(lemma, _ => false) : null;
        var degrees = pos is "a" or "r" ? InflectionRules.AdjectiveDegrees(lemma) : null;
        return kinds.Plural.Any(f => !InflectionRules.NounPlural(lemma).Contains(f.Form, StringComparer.Ordinal))
            || kinds.Third.Any(f => f.Form != InflectionRules.VerbThirdPerson(lemma))
            || kinds.Past.Concat(kinds.Participle).Any(f => verb is null || !verb.Past.Concat(verb.PastAlternative).Contains(f.Form, StringComparer.Ordinal))
            || kinds.Gerund.Any(f => verb is null || !verb.Gerund.Concat(verb.GerundAlternative).Contains(f.Form, StringComparer.Ordinal))
            || kinds.Comparative.Any(f => degrees is null || f.Form != degrees.Value.Comparative)
            || kinds.Superlative.Any(f => degrees is null || f.Form != degrees.Value.Superlative);
    }

    private void ReadDecisions(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var number = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("lemma\t", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 8 || columns[1].Length != 1 || LexiconSource.Letters.IndexOf(columns[1][0]) < 0)
            {
                throw new InvalidDataException($"{ProvenanceFile} line {number.ToString(CultureInfo.InvariantCulture)}: expected 'lemma<TAB>letter<TAB>rule<TAB>batch<TAB>writer<TAB>verifier<TAB>verdict<TAB>result<TAB>...'.");
            }

            if (columns[6] == "agree" && columns[7] == "none")
            {
                var pos = IrregularSources.Letter((DemaConsulting.WordSense.PartOfSpeech)LexiconSource.Letters.IndexOf(columns[1][0]));
                NoForm.Add((columns[0].ToLowerInvariant(), pos, columns[2]));
            }
        }
    }
}
