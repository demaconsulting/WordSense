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
///     do not generate that form for the lemma. The sidecar is required whenever authored forms exist, and it is read strictly: the
///     exact header, ten non-empty columns, the CC0 licence, a valid verdict and result, and one decision for each lemma, part of
///     speech and rule.
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

    private const string Licence = "CC0 1.0 (project-authored)";

    private const string ProvenanceHeader = "lemma\tpos\trule\tbatch_id\twriter\tverifier\tverdict\tresult\tlicence\tevidence";

    private const int ProvenanceColumns = 10;

    private static readonly string[] Rules = ["Plural", "Third", "Past", "Participle", "Gerund", "Comparative", "Superlative"];

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
            // The sidecar is the evidence of the authored forms: forms without it are refused
            if (Forms.Count > 0)
            {
                throw new InvalidDataException($"{path}: the provenance of the authored forms is missing.");
            }

            return;
        }

        var number = 0;
        var headerSeen = false;
        var seen = new Dictionary<(string Lemma, string Pos, string Rule), int>();
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var where = $"{path}:{number.ToString(CultureInfo.InvariantCulture)}";
            if (!headerSeen)
            {
                if (line != ProvenanceHeader)
                {
                    throw new InvalidDataException($"{where}: the header must be exactly '{ProvenanceHeader.Replace("\t", "<TAB>", StringComparison.Ordinal)}'.");
                }

                headerSeen = true;
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length != ProvenanceColumns || columns.Any(c => c.Length == 0) || columns[1].Length != 1 || LexiconSource.Letters.IndexOf(columns[1][0]) < 0)
            {
                throw new InvalidDataException($"{where}: expected the {ProvenanceColumns.ToString(CultureInfo.InvariantCulture)} non-empty columns 'lemma<TAB>letter<TAB>rule<TAB>batch_id<TAB>writer<TAB>verifier<TAB>verdict<TAB>result<TAB>licence<TAB>evidence'.");
            }

            if (columns[8] != Licence)
            {
                throw new InvalidDataException($"{where}: the licence must be '{Licence}', not '{columns[8]}'.");
            }

            if (!Rules.Contains(columns[2], StringComparer.Ordinal))
            {
                throw new InvalidDataException($"{where}: the rule '{columns[2]}' is not known.");
            }

            // The verdict and the result go together: both authors agree or an adjudication decides on forms or on none; a regular
            // spelling that the verifier doubted is accepted as the form
            if (!(columns[6] is "agree" or "adjudicated" && columns[7] is "forms" or "none") && !(columns[6] == "regular-accepted" && columns[7] == "forms"))
            {
                throw new InvalidDataException($"{where}: the verdict '{columns[6]}' with the result '{columns[7]}' is not a valid decision.");
            }

            var pos = IrregularSources.Letter((DemaConsulting.WordSense.PartOfSpeech)LexiconSource.Letters.IndexOf(columns[1][0]));
            var key = (columns[0].ToLowerInvariant(), pos, columns[2]);
            if (!seen.TryAdd(key, number))
            {
                throw new InvalidDataException($"{where}: the decision on '{columns[0]}' ({columns[2]}) repeats the one on line {seen[key].ToString(CultureInfo.InvariantCulture)}.");
            }

            if (columns[6] == "agree" && columns[7] == "none")
            {
                NoForm.Add(key);
            }
        }

        if (!headerSeen)
        {
            throw new InvalidDataException($"{path}: the header line is missing.");
        }
    }
}
