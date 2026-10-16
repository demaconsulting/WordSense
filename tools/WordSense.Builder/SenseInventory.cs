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
/// <param name="Group">The optional group; null or empty means the sense is its own group.</param>
internal sealed record InventoryRow(string Lemma, string Sense, IReadOnlyList<string> Tags, string Source, string? Group = null);

/// <summary>
///     The sense inventory: every sense of every trained lemma with its tags.
/// </summary>
/// <remarks>
///     The inventory is the authority on which senses exist and what they mean. It lives in
///     <c>corpus/senses/*.tsv</c> (header <c>lemma</c>, <c>sense</c>, <c>tags</c>, <c>group</c>; tags separated by a
///     pipe; the group is optional and defaults to the sense label). The corpus must agree with it in both directions: every sense that has anchor sentences
///     needs an entry and every entry needs at least one anchor sentence.
/// </remarks>
internal sealed class SenseInventory
{
    private const string Header = "lemma\tsense\ttags\tgroup";

    private const string LegacyHeader = "lemma\tsense\ttags";

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
            .Select(r => new SenseDefinition(r.Sense, r.Tags, string.IsNullOrEmpty(r.Group) ? null : r.Group)).ToArray();
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
            text.Append(row.Lemma).Append('\t').Append(row.Sense).Append('\t').Append(string.Join('|', row.Tags))
                .Append('\t').Append(row.Group ?? string.Empty).Append('\n');
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
        if (header != Header && header != LegacyHeader)
        {
            errors.Add($"senses/{name}:1: the header must be 'lemma<TAB>sense<TAB>tags<TAB>group'.");
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
            if (columns.Length is not (3 or 4))
            {
                errors.Add($"senses/{name}:{number}: expected 3 or 4 tab-separated columns but found {columns.Length}.");
                continue;
            }

            var lemma = columns[0].Trim();
            var sense = columns[1].Trim();
            var tags = columns[2].Trim().Split('|');
            var group = columns.Length == 4 ? columns[3].Trim() : string.Empty;
            if (lemma.Length == 0 || sense.Length == 0 || lemma != lemma.ToLowerInvariant())
            {
                errors.Add($"senses/{name}:{number}: lemma must be lower-case and sense must be non-empty.");
            }
            else if (group.Length > 0 && !SenseDefinition.IsValidTag(group))
            {
                errors.Add($"senses/{name}:{number}: group must be empty or lower-case letters, digits and hyphens starting with a letter.");
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
                rows.Add(new InventoryRow(lemma, sense, tags, name, group.Length == 0 ? null : group));
            }
        }
    }
}
