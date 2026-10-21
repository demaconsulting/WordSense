using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The irregular forms of one lemma and part of speech, by kind, from one source.
/// </summary>
internal sealed class IrregularKinds
{
    /// <summary>
    ///     Gets the irregular past forms.
    /// </summary>
    public List<(string Form, string Level)> Past { get; } = [];

    /// <summary>
    ///     Gets the irregular participle forms.
    /// </summary>
    public List<(string Form, string Level)> Participle { get; } = [];

    /// <summary>
    ///     Gets the irregular gerund forms.
    /// </summary>
    public List<(string Form, string Level)> Gerund { get; } = [];

    /// <summary>
    ///     Gets the irregular third person forms.
    /// </summary>
    public List<(string Form, string Level)> Third { get; } = [];

    /// <summary>
    ///     Gets the irregular plural forms.
    /// </summary>
    public List<(string Form, string Level)> Plural { get; } = [];

    /// <summary>
    ///     Gets the irregular comparative forms.
    /// </summary>
    public List<(string Form, string Level)> Comparative { get; } = [];

    /// <summary>
    ///     Gets the irregular superlative forms.
    /// </summary>
    public List<(string Form, string Level)> Superlative { get; } = [];

    /// <summary>
    ///     Gets the variant forms, which add forms without replacing a regular form.
    /// </summary>
    public List<(string Form, string Level)> Variant { get; } = [];

    /// <summary>
    ///     Gets every form of every kind.
    /// </summary>
    /// <returns>The forms.</returns>
    public IEnumerable<string> All() => Past.Concat(Participle).Concat(Gerund).Concat(Third).Concat(Plural).Concat(Comparative).Concat(Superlative).Concat(Variant).Select(f => f.Form);

    /// <summary>
    ///     Gets the list of a rule.
    /// </summary>
    /// <param name="rule">The rule name.</param>
    /// <returns>The list, or null when the rule is not a kind of forms.</returns>
    public List<(string Form, string Level)>? Of(string rule) => rule switch
    {
        "Past" => Past,
        "Participle" => Participle,
        "Gerund" => Gerund,
        "Third" => Third,
        "Plural" => Plural,
        "Comparative" => Comparative,
        "Superlative" => Superlative,
        "Variant" => Variant,
        _ => null,
    };
}

/// <summary>
///     One contraction of the closed list.
/// </summary>
/// <param name="Spelling">The contraction.</param>
/// <param name="Head">The head word.</param>
/// <param name="Letters">The part-of-speech letters to try for the head word, in order.</param>
internal sealed record ContractionRow(string Spelling, string Head, string Letters);

/// <summary>
///     One analysis of a hand-written override of the reverse rules.
/// </summary>
/// <param name="Spelling">The spelling.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="Pos">The part-of-speech letter.</param>
/// <param name="Features">The features.</param>
internal sealed record OverrideRow(string Spelling, string Lemma, string Pos, string Features);

/// <summary>
///     The source lists of the form generator: the hand-written irregular forms, the closed lists of irregular forms,
///     the forms of the Automatically Generated Inflection Database, the contractions and the overrides.
/// </summary>
/// <remarks>
///     Every list is a plain file of the lexicon directory.
/// </remarks>
internal sealed class IrregularSources
{
    /// <summary>
    ///     The name of the closed irregular lists.
    /// </summary>
    public const string ClosedFile = "irregular-closed.tsv";

    /// <summary>
    ///     The name of the forms taken from the Automatically Generated Inflection Database.
    /// </summary>
    public const string AgidFile = "agid-irregular.tsv";

    /// <summary>
    ///     The name of the closed list of contractions.
    /// </summary>
    public const string ContractionsFile = "contractions.tsv";

    /// <summary>
    ///     The name of the hand-written overrides.
    /// </summary>
    public const string OverridesFile = "form-overrides.tsv";

    /// <summary>
    ///     Gets the hand-written irregular forms by lemma and part-of-speech letter.
    /// </summary>
    public Dictionary<(string Lemma, string Pos), IrregularKinds> Hand { get; } = [];

    /// <summary>
    ///     Gets the closed irregular lists by lemma and part-of-speech letter.
    /// </summary>
    public Dictionary<(string Lemma, string Pos), IrregularKinds> Closed { get; } = [];

    /// <summary>
    ///     Gets the AGID forms by lemma and part-of-speech letter.
    /// </summary>
    public Dictionary<(string Lemma, string Pos), IrregularKinds> Agid { get; } = [];

    /// <summary>
    ///     Gets the forms of the auxiliaries as spelling and features, by lemma.
    /// </summary>
    public Dictionary<string, List<(string Spelling, string Features)>> Auxiliaries { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the modal auxiliaries.
    /// </summary>
    public List<string> Modals { get; } = [];

    /// <summary>
    ///     Gets the contractions.
    /// </summary>
    public List<ContractionRow> Contractions { get; } = [];

    /// <summary>
    ///     Gets the overrides by spelling.
    /// </summary>
    public Dictionary<string, List<OverrideRow>> Overrides { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Reads the source lists of a lexicon directory; a missing file is an empty list.
    /// </summary>
    /// <param name="lexicon">The lexicon directory.</param>
    /// <returns>The sources.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static IrregularSources Read(string lexicon)
    {
        var sources = new IrregularSources();
        Fill(sources.Hand, IrregularForms.ReadFile(Path.Join(lexicon, IrregularForms.HandFile)), null);
        Fill(sources.Closed, IrregularForms.ReadFile(Path.Join(lexicon, ClosedFile)), sources);
        Fill(sources.Agid, IrregularForms.ReadFile(Path.Join(lexicon, AgidFile)), null);
        sources.ReadContractions(Path.Join(lexicon, ContractionsFile));
        sources.ReadOverrides(Path.Join(lexicon, OverridesFile));
        return sources;
    }

    /// <summary>
    ///     Gets the part-of-speech letter of the form table for a letter of the lexicon.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The lower-case letter.</returns>
    public static string Letter(DemaConsulting.WordSense.PartOfSpeech pos) => DataTables.PosLetter(pos);

    private static void Fill(Dictionary<(string, string), IrregularKinds> target, List<IrregularEntry> entries, IrregularSources? auxiliaryTarget)
    {
        foreach (var entry in entries)
        {
            if (auxiliaryTarget is not null && entry.Rule == "Auxiliary")
            {
                var forms = new List<(string, string)>();
                foreach (var token in entry.Forms)
                {
                    var colon = token.IndexOf(':', StringComparison.Ordinal);
                    if (colon <= 0)
                    {
                        throw new InvalidDataException($"The auxiliary form '{token}' of '{entry.Lemma}' is not 'form:Features'.");
                    }

                    forms.Add((token[..colon], token[(colon + 1)..]));
                }

                auxiliaryTarget.Auxiliaries[entry.Lemma] = forms;
                continue;
            }

            if (auxiliaryTarget is not null && entry.Rule == "Modal")
            {
                auxiliaryTarget.Modals.Add(entry.Lemma);
                continue;
            }

            var key = (entry.Lemma, Letter(entry.Pos));
            if (!target.TryGetValue(key, out var kinds))
            {
                target[key] = kinds = new IrregularKinds();
            }

            var list = kinds.Of(entry.Rule) ?? throw new InvalidDataException($"The rule '{entry.Rule}' of '{entry.Lemma}' is not known.");
            for (var i = 0; i < entry.Forms.Length; i++)
            {
                list.Add((entry.Forms[i], i < entry.Evidence.Length && entry.Evidence.Length == entry.Forms.Length && entry.Evidence[i] is "0" or "1" or "2" or "?" ? entry.Evidence[i] : "0"));
            }
        }
    }

    private void ReadContractions(string path)
    {
        foreach (var columns in ReadColumns(path, 3))
        {
            Contractions.Add(new ContractionRow(columns[0], columns[1], columns[2]));
        }
    }

    private void ReadOverrides(string path)
    {
        foreach (var columns in ReadColumns(path, 4))
        {
            if (!Overrides.TryGetValue(columns[0], out var list))
            {
                Overrides[columns[0]] = list = [];
            }

            list.Add(new OverrideRow(columns[0], columns[1], columns[2], columns[3]));
        }
    }

    private static IEnumerable<string[]> ReadColumns(string path, int minimum)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

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
            if (columns.Length < minimum)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} line {number.ToString(CultureInfo.InvariantCulture)}: expected at least {minimum.ToString(CultureInfo.InvariantCulture)} tab-separated columns.");
            }

            yield return columns;
        }
    }
}
