using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One row of the sense inventory: a sense of a lemma and its tags.
/// </summary>
/// <param name="Lemma">The lower-case lemma.</param>
/// <param name="Sense">The sense label.</param>
/// <param name="Tags">The tags, sorted ordinally.</param>
/// <param name="Source">The file name of the row, for messages and grouping.</param>
internal sealed record InventoryRow(string Lemma, string Sense, IReadOnlyList<string> Tags, string Source);

/// <summary>
///     The sense inventory: every sense of every trained lemma with its tags.
/// </summary>
/// <remarks>
///     The inventory is the authority on which senses exist and what they mean. It lives in
///     <c>corpus/senses/*.tsv</c> (header <c>lemma</c>, <c>sense</c>, <c>tags</c>; tags separated by a
///     pipe). The corpus must agree with it in both directions: every sense that has anchor sentences
///     needs an entry and every entry needs at least one anchor sentence.
/// </remarks>
internal sealed class SenseInventory
{
    private const string Header = "lemma\tsense\ttags";

    private readonly Dictionary<(string Lemma, string Sense), InventoryRow> _rows;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SenseInventory"/> class.
    /// </summary>
    /// <param name="rows">The rows, already free of duplicates.</param>
    public SenseInventory(IEnumerable<InventoryRow> rows)
    {
        _rows = rows.ToDictionary(r => (r.Lemma, r.Sense));
    }

    /// <summary>
    ///     Gets every row, ordered by lemma and sense.
    /// </summary>
    public IEnumerable<InventoryRow> Rows => _rows.Values
        .OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal);

    /// <summary>
    ///     Gets the number of lemmas in the inventory.
    /// </summary>
    public int LemmaCount => _rows.Keys.Select(k => k.Lemma).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    ///     Determines whether the inventory has a sense.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sense">The sense label.</param>
    /// <returns>True when listed.</returns>
    public bool Contains(string lemma, string sense) => _rows.ContainsKey((lemma, sense));

    /// <summary>
    ///     Determines whether the inventory has any sense of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>True when listed.</returns>
    public bool ContainsLemma(string lemma) => _rows.Keys.Any(k => k.Lemma == lemma);

    /// <summary>
    ///     Gets the sense definitions of a lemma in ordinal order of label.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The definitions.</returns>
    public SenseDefinition[] DefinitionsOf(string lemma)
    {
        return _rows.Values.Where(r => r.Lemma == lemma).OrderBy(r => r.Sense, StringComparer.Ordinal)
            .Select(r => new SenseDefinition(r.Sense, r.Tags)).ToArray();
    }

    /// <summary>
    ///     Reads every <c>*.tsv</c> file of a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The <c>senses</c> directory.</param>
    /// <returns>The inventory.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found, one per line.</exception>
    public static SenseInventory ReadDirectory(string directory)
    {
        var rows = new List<InventoryRow>();
        var errors = new List<string>();
        var seen = new Dictionary<(string, string), string>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
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

        foreach (var row in Rows.Where(r => !anchorPairs.ContainsKey((r.Lemma, r.Sense))))
        {
            errors.Add($"senses/{row.Source}: sense '{row.Lemma}/{row.Sense}' has no anchor sentences.");
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
            text.Append(row.Lemma).Append('\t').Append(row.Sense).Append('\t').Append(string.Join('|', row.Tags)).Append('\n');
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
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != Header)
        {
            errors.Add($"senses/{name}:1: the header must be 'lemma<TAB>sense<TAB>tags'.");
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
            if (columns.Length != 3)
            {
                errors.Add($"senses/{name}:{number}: expected 3 tab-separated columns but found {columns.Length}.");
                continue;
            }

            var lemma = columns[0].Trim();
            var sense = columns[1].Trim();
            var tags = columns[2].Trim().Split('|');
            if (lemma.Length == 0 || sense.Length == 0 || lemma != lemma.ToLowerInvariant())
            {
                errors.Add($"senses/{name}:{number}: lemma must be lower-case and sense must be non-empty.");
            }
            else if (tags.Any(t => !SenseDefinition.IsValidTag(t)) || tags.Length > SenseDefinition.MaxTagsPerSense)
            {
                errors.Add($"senses/{name}:{number}: tags must be 1 to {SenseDefinition.MaxTagsPerSense} lower-case words ([a-z][a-z0-9-]*, at most {SenseDefinition.MaxTagLength} characters) separated by '|'.");
            }
            else if (tags.Where((t, k) => k > 0 && string.CompareOrdinal(tags[k - 1], t) >= 0).Any())
            {
                errors.Add($"senses/{name}:{number}: tags must be sorted ordinally and unique.");
            }
            else if (!seen.TryAdd((lemma, sense), name))
            {
                errors.Add($"senses/{name}:{number}: sense '{lemma}/{sense}' is already listed in {seen[(lemma, sense)]}.");
            }
            else
            {
                rows.Add(new InventoryRow(lemma, sense, tags, name));
            }
        }
    }
}

/// <summary>
///     Derives the tags of a sense from its label; the documented rules of the inventory.
/// </summary>
/// <remarks>
///     Part-of-speech labels map to the same tag; <c>past</c> and <c>present</c> are verb forms and map
///     to <c>past|verb</c> and <c>present|verb</c>. Descriptive labels use an explicit table; a label
///     missing from it is an error so a new meaning is never tagged by accident. Two lemma-specific
///     overrides apply where the label alone would mislead.
/// </remarks>
internal static class SenseTagDerivation
{
    private static readonly Dictionary<string, string[]> Rules = new(StringComparer.Ordinal)
    {
        ["noun"] = ["noun"],
        ["verb"] = ["verb"],
        ["adj"] = ["adj"],
        ["adv"] = ["adv"],
        ["past"] = ["past", "verb"],
        ["present"] = ["present", "verb"],
        ["argument"] = ["noun"],
        ["line"] = ["noun"],
        ["metal"] = ["noun"],
        ["music"] = ["noun"],
        ["fish"] = ["noun"],
        ["drop"] = ["noun"],
        ["injury"] = ["noun"],
        ["weapon"] = ["noun"],
        ["paddle"] = ["verb"],

        // Mixed senses: the anchors contain both nouns and verbs
        ["guide"] = ["noun", "verb"],
        ["bend"] = ["noun", "verb"],
        ["rip"] = ["noun", "verb"],
    };

    private static readonly Dictionary<(string Lemma, string Sense), string[]> Overrides = new()
    {
        // "used" with a plain verb label is the simple past tense ("She used the hammer")
        [("used", "verb")] = ["past", "verb"],
    };

    /// <summary>
    ///     Gets the tags of a sense.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sense">The sense label.</param>
    /// <returns>The tags, sorted ordinally.</returns>
    /// <exception cref="InvalidDataException">Thrown when the label has no derivation rule.</exception>
    public static string[] Derive(string lemma, string sense)
    {
        if (Overrides.TryGetValue((lemma, sense), out var tags) || Rules.TryGetValue(sense, out tags))
        {
            return tags;
        }

        throw new InvalidDataException(
            $"Sense label '{sense}' (lemma '{lemma}') has no tag derivation rule; add it to the sense inventory rules.");
    }

    /// <summary>
    ///     Derives inventory files from anchor rows, one file per anchor file.
    /// </summary>
    /// <param name="anchors">The anchor rows, in file order.</param>
    /// <returns>The inventory rows per anchor file name.</returns>
    /// <exception cref="InvalidDataException">Thrown when a label has no rule.</exception>
    public static SortedDictionary<string, List<InventoryRow>> DeriveFiles(IEnumerable<CorpusRow> anchors)
    {
        var files = new SortedDictionary<string, List<InventoryRow>>(StringComparer.Ordinal);
        var emitted = new HashSet<(string, string)>();
        foreach (var row in anchors)
        {
            if (!emitted.Add((row.Lemma, row.Sense)))
            {
                continue;
            }

            if (!files.TryGetValue(row.Source, out var list))
            {
                files[row.Source] = list = [];
            }

            list.Add(new InventoryRow(row.Lemma, row.Sense, Derive(row.Lemma, row.Sense), row.Source));
        }

        foreach (var list in files.Values)
        {
            list.Sort((a, b) => a.Lemma == b.Lemma ? string.CompareOrdinal(a.Sense, b.Sense) : string.CompareOrdinal(a.Lemma, b.Lemma));
        }

        return files;
    }
}
