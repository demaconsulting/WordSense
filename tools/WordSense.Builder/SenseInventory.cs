using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One row of the sense inventory: a sense of a lemma with its part of speech and pronunciation meaning.
/// </summary>
/// <param name="Lemma">The lower-case lemma.</param>
/// <param name="Label">The internal sense label that joins the row to the anchor, holdout and forms files; it is not public.</param>
/// <param name="Pos">The part of speech.</param>
/// <param name="Meaning">The pronunciation meaning, or null for a lemma that is not a heteronym.</param>
/// <param name="Respelling">The plain respelling of the meaning, or null when none is authored.</param>
/// <param name="Source">The file name of the row, for messages and grouping.</param>
internal sealed record InventoryRow(
    string Lemma,
    string Label,
    PartOfSpeech Pos,
    string? Meaning,
    string? Respelling,
    string Source)
{
    /// <summary>
    ///     Creates a row from the legacy plan fields (a tag list and a group) used by the one-shot importers.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="label">The sense label.</param>
    /// <param name="tags">The legacy tags; one of them is a part of speech (noun, verb, adj, adv).</param>
    /// <param name="group">The legacy group, or null.</param>
    /// <param name="source">The file name.</param>
    /// <returns>The row; its meaning is the group, else the label (adj and adv spelled out).</returns>
    public static InventoryRow FromPlan(string lemma, string label, IReadOnlyList<string> tags, string? group, string source)
    {
        var pos = tags.Select(PosOfTag).FirstOrDefault(p => p is not null)
            ?? throw new InvalidDataException($"Sense '{lemma}/{label}' has no part-of-speech tag.");
        var meaning = SpellOut(string.IsNullOrEmpty(group) ? label : group);
        return new InventoryRow(lemma, label, pos, meaning, null, source);
    }

    /// <summary>
    ///     Maps a legacy part-of-speech tag to a part of speech.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The part of speech, or null when the tag is not one.</returns>
    public static PartOfSpeech? PosOfTag(string tag) => tag switch
    {
        "noun" => PartOfSpeech.Noun,
        "verb" => PartOfSpeech.Verb,
        "adj" => PartOfSpeech.Adjective,
        "adv" => PartOfSpeech.Adverb,
        _ => null,
    };

    /// <summary>
    ///     Spells out the abbreviated part-of-speech words used as meaning ids.
    /// </summary>
    /// <param name="text">The label or group.</param>
    /// <returns>The meaning id.</returns>
    public static string SpellOut(string text) => text switch
    {
        "adj" => "adjective",
        "adv" => "adverb",
        _ => text,
    };
}

/// <summary>
///     The sense inventory: every sense of every trained lemma with its part of speech and pronunciation meaning.
/// </summary>
/// <remarks>
///     The inventory is the authority on which senses exist and what they mean. It lives in
///     <c>corpus/senses/*.tsv</c> (header <c>lemma</c>, <c>label</c>, <c>pos</c>, <c>meaning</c>, <c>respelling</c>;
///     the meaning and respelling may be empty). The label is the internal join key to the corpus files. The corpus
///     must agree with it in both directions: every sense that has anchor sentences needs an entry and every entry
///     needs at least one anchor sentence.
/// </remarks>
internal sealed class SenseInventory
{
    private const string Header = "lemma\tlabel\tpos\tmeaning\trespelling";

    private readonly Dictionary<(string Lemma, string Label), InventoryRow> _rows;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SenseInventory"/> class.
    /// </summary>
    /// <param name="rows">The rows, already free of duplicates.</param>
    public SenseInventory(IEnumerable<InventoryRow> rows)
    {
        _rows = rows.ToDictionary(r => (r.Lemma, r.Label));
    }

    /// <summary>
    ///     Gets every row, ordered by lemma and label.
    /// </summary>
    public IEnumerable<InventoryRow> Rows => _rows.Values
        .OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Label, StringComparer.Ordinal);

    /// <summary>
    ///     Gets the number of lemmas in the inventory.
    /// </summary>
    public int LemmaCount => _rows.Keys.Select(k => k.Lemma).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    ///     Determines whether the inventory has a sense.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="label">The sense label.</param>
    /// <returns>True when listed.</returns>
    public bool Contains(string lemma, string label) => _rows.ContainsKey((lemma, label));

    /// <summary>
    ///     Determines whether the inventory has any sense of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>True when listed.</returns>
    public bool ContainsLemma(string lemma) => _rows.Keys.Any(k => k.Lemma == lemma);

    /// <summary>
    ///     Gets the rows of a lemma in ordinal order of label.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The rows.</returns>
    public InventoryRow[] RowsOf(string lemma)
    {
        return _rows.Values.Where(r => r.Lemma == lemma).OrderBy(r => r.Label, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    ///     Gets the sense labels of a lemma in ordinal order; the order of the trained classes.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The labels.</returns>
    public string[] LabelsOf(string lemma) => RowsOf(lemma).Select(r => r.Label).ToArray();

    /// <summary>
    ///     Gets the sense definitions of a lemma in ordinal order of label, parallel to <see cref="LabelsOf"/>.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The definitions.</returns>
    public SenseDefinition[] DefinitionsOf(string lemma)
    {
        return RowsOf(lemma).Select(r => new SenseDefinition(r.Pos, r.Meaning, r.Respelling)).ToArray();
    }

    /// <summary>
    ///     Reads every <c>*.tsv</c> file of a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The <c>senses</c> directory.</param>
    /// <param name="skipFile">The name of a file to leave out, or null to read all files.</param>
    /// <returns>The inventory.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found, one per line.</exception>
    public static SenseInventory ReadDirectory(string directory, string? skipFile = null)
    {
        var rows = new List<InventoryRow>();
        var errors = new List<string>();
        var seen = new Dictionary<(string, string), string>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").Where(f => Path.GetFileName(f) != skipFile).OrderBy(f => f, StringComparer.Ordinal))
            {
                ReadFile(file, rows, errors, seen);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        return new SenseInventory(rows);
    }

    /// <summary>
    ///     Checks the part-of-speech and pronunciation rules of every lemma: meanings are all-or-none, a heteronym has at
    ///     least two distinct meanings, (part of speech, meaning) pairs are unique, and respellings are consistent.
    /// </summary>
    /// <returns>One message per violation.</returns>
    public List<string> ValidateRules()
    {
        var errors = new List<string>();
        foreach (var lemma in Rows.Select(r => r.Lemma).Distinct(StringComparer.Ordinal))
        {
            var rows = RowsOf(lemma);
            try
            {
                var definitions = rows.Select(r => new SenseDefinition(r.Pos, r.Meaning, r.Respelling)).ToArray();
                foreach (var definition in definitions)
                {
                    definition.Validate();
                }

                if (definitions.Length < 2)
                {
                    throw new ArgumentException("A lemma needs at least two senses.");
                }

                LemmaModel.ValidateSet(definitions);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"senses/{rows[0].Source}: lemma '{lemma}': {ex.Message.Split(" (Parameter", StringSplitOptions.None)[0]}");
            }
        }

        return errors;
    }

    /// <summary>
    ///     Checks the corpus against the inventory in both directions.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <param name="forms">The forms rows.</param>
    /// <exception cref="InvalidDataException">Thrown with every disagreement found, one per line.</exception>
    public void Validate(
        IReadOnlyList<CorpusRow> anchors,
        IReadOnlyList<CorpusRow> holdout,
        IReadOnlyList<(string Form, string Lemma, string[] Labels)> forms)
    {
        var errors = new List<string>();
        var anchorPairs = anchors.GroupBy(r => (r.Lemma, r.Sense)).ToDictionary(g => g.Key, g => g.First().Source);
        foreach (var ((lemma, sense), source) in anchorPairs.OrderBy(p => p.Key.Lemma, StringComparer.Ordinal).ThenBy(p => p.Key.Sense, StringComparer.Ordinal))
        {
            if (!Contains(lemma, sense))
            {
                errors.Add($"anchors/{source}: sense '{lemma}/{sense}' has anchor sentences but is not in the sense inventory.");
            }
        }

        foreach (var row in Rows.Where(r => !anchorPairs.ContainsKey((r.Lemma, r.Label))))
        {
            errors.Add($"senses/{row.Source}: sense '{row.Lemma}/{row.Label}' has no anchor sentences.");
        }

        foreach (var group in holdout.GroupBy(r => (r.Lemma, r.Sense)).Where(g => !Contains(g.Key.Lemma, g.Key.Sense)))
        {
            errors.Add($"holdout/{group.First().Source}: sense '{group.Key.Lemma}/{group.Key.Sense}' is not in the sense inventory.");
        }

        // Forms of untrained lemmas are ignored by the build, so only trained lemmas are checked
        foreach (var (form, lemma, labels) in forms.Where(f => ContainsLemma(f.Lemma)))
        {
            foreach (var label in labels.Where(l => !Contains(lemma, l)))
            {
                errors.Add($"forms: form '{form}' of lemma '{lemma}' lists label '{label}', which is not a sense of the lemma.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>
    ///     Writes the inventory rows as a file: UTF-8 without a byte-order mark, LF line endings.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="rows">The rows, written in the given order.</param>
    public static void WriteFile(string path, IEnumerable<InventoryRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var row in rows)
        {
            text.Append(row.Lemma).Append('\t').Append(row.Label).Append('\t').Append(row.Pos.ToString().ToLowerInvariant())
                .Append('\t').Append(row.Meaning ?? string.Empty).Append('\t').Append(row.Respelling ?? string.Empty).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Reads one inventory file, collecting every problem.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="rows">Receives the valid rows.</param>
    /// <param name="errors">Receives one message per problem, naming the file and line.</param>
    /// <param name="seen">Tracks the senses already listed, with the file of the first listing.</param>
    private static void ReadFile(string path, List<InventoryRow> rows, List<string> errors, Dictionary<(string, string), string> seen)
    {
        var name = Path.GetFileName(path);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        var header = lines.Length == 0 ? string.Empty : lines[0].TrimEnd('\r');
        if (header != Header)
        {
            errors.Add($"senses/{name}:1: the header must be 'lemma<TAB>label<TAB>pos<TAB>meaning<TAB>respelling'.");
            return;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var number = i + 1;
            var columns = line.Split('\t');
            if (columns.Length != 5)
            {
                errors.Add($"senses/{name}:{number}: expected 5 tab-separated columns but found {columns.Length}.");
                continue;
            }

            var lemma = columns[0].Trim();
            var label = columns[1].Trim();
            var posText = columns[2].Trim();
            var meaning = columns[3].Trim();
            var respelling = columns[4].Trim();
            if (lemma.Length == 0 || label.Length == 0 || lemma != lemma.ToLowerInvariant())
            {
                errors.Add($"senses/{name}:{number}: lemma must be lower-case and label must be non-empty.");
            }
            else if (!Enum.TryParse<PartOfSpeech>(posText, true, out var pos) || posText != pos.ToString().ToLowerInvariant())
            {
                errors.Add($"senses/{name}:{number}: pos must be one of noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection.");
            }
            else if (meaning.Length > 0 && !SenseDefinition.IsValidMeaning(meaning))
            {
                errors.Add($"senses/{name}:{number}: meaning must be empty or lower-case letters, digits and hyphens starting with a letter.");
            }
            else if (respelling.Length > 0 && (meaning.Length == 0 || !SenseDefinition.IsValidRespelling(respelling)))
            {
                errors.Add($"senses/{name}:{number}: respelling must be empty or letters, hyphens and apostrophes, and needs a meaning.");
            }
            else if (!seen.TryAdd((lemma, label), name))
            {
                errors.Add($"senses/{name}:{number}: sense '{lemma}/{label}' is already listed in {seen[(lemma, label)]}.");
            }
            else
            {
                rows.Add(new InventoryRow(lemma, label, pos, meaning.Length == 0 ? null : meaning, respelling.Length == 0 ? null : respelling, name));
            }
        }
    }
}
