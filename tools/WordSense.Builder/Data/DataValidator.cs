using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder.Data;

/// <summary>
///     The outcome of validating a data folder.
/// </summary>
/// <param name="Violations">The violations found, at most <see cref="DataValidator.MaxPerRule"/> per rule, in discovery order.</param>
/// <param name="TotalViolations">The total number of violations, including those that were not listed.</param>
/// <param name="RowCounts">The number of valid rows read per table.</param>
internal sealed record ValidationReport(IReadOnlyList<string> Violations, int TotalViolations, IReadOnlyDictionary<string, int> RowCounts)
{
    /// <summary>
    ///     Gets a value indicating whether the data has no violation.
    /// </summary>
    public bool IsValid => TotalViolations == 0;
}

/// <summary>
///     Validates the TSV tables of a <c>data</c> folder against <c>schema.yaml</c>: the file set, the file format, the
///     cell types, keys, references, sort order, file placement, relation conventions and the cross-table consistency
///     rules.
/// </summary>
/// <remarks>
///     <para>
///     Every violation names the file, the line (when it has one), the rule and the offending value, so that a data
///     edit can be fixed without reading the validator. At most <see cref="MaxPerRule"/> violations are listed per
///     rule; the total is still counted. The validator reads the whole folder (about 50 MB for the full release) and
///     holds the rows in memory; it never changes anything. It is stateless between calls.
///     </para>
/// </remarks>
internal sealed partial class DataValidator
{
    /// <summary>
    ///     The maximum number of listed violations per rule.
    /// </summary>
    public const int MaxPerRule = 20;

    /// <summary>The name of the entry table, also the name of its validation rule.</summary>
    private const string TableEntry = "entry";

    /// <summary>The name of the synset table, also the name of its validation rule.</summary>
    private const string TableSynset = "synset";

    /// <summary>The name of the sense table, also the name of its validation rule.</summary>
    private const string TableSense = "sense";

    /// <summary>The name of the coarse-class table.</summary>
    private const string TableCoarseClass = "coarse_class";

    /// <summary>The name of the form-sense-scope table, also the name of its validation rule.</summary>
    private const string TableFormSenseScope = "form_sense_scope";

    /// <summary>The name of the rule checking the set of files in the data directory.</summary>
    private const string RuleFileSet = "file-set";

    /// <summary>The name of the rule checking the byte-level format of a file.</summary>
    private const string RuleFormat = "format";

    private static readonly string[] LoadOrder = ["source", TableEntry, TableSynset, TableSense];

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _root;
    private readonly string? _baseline;
    private readonly DataSchema _schema;
    private readonly Dictionary<string, int> _ruleCounts = new(StringComparer.Ordinal);
    private readonly List<string> _violations = [];
    private readonly Dictionary<string, LoadedTable> _tables = new(StringComparer.Ordinal);

    private DataValidator(string root, DataSchema schema, string? baseline)
    {
        _root = root;
        _schema = schema;
        _baseline = baseline;
    }

    /// <summary>
    ///     Validates a data folder.
    /// </summary>
    /// <param name="dataDirectory">The data folder that holds <c>schema.yaml</c> and the tables.</param>
    /// <param name="baselineDirectory">The data folder of the last release, against which the published sense labels are checked, or null.</param>
    /// <returns>The report of the validation.</returns>
    public static ValidationReport Validate(string dataDirectory, string? baselineDirectory = null)
    {
        var schemaPath = Path.Join(dataDirectory, "schema.yaml");
        DataSchema schema;
        try
        {
            schema = DataSchema.Load(schemaPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return new ValidationReport([$"{schemaPath}: [schema] {ex.Message}"], 1, new Dictionary<string, int>());
        }

        var validator = new DataValidator(dataDirectory, schema, baselineDirectory);
        validator.Run();
        var counts = validator._tables.ToDictionary(p => p.Key, p => p.Value.RowCount, StringComparer.Ordinal);
        return new ValidationReport(validator._violations, validator._ruleCounts.Values.Sum(), counts);
    }

    private sealed record FileRows(string Path, string Key, List<string[]> Rows);

    private sealed class LoadedTable(TableDef definition)
    {
        public TableDef Definition { get; } = definition;

        public List<FileRows> Files { get; } = [];

        public int RowCount => Files.Sum(f => f.Rows.Count);

        public IEnumerable<string[]> Rows => Files.SelectMany(f => f.Rows);
    }

    private void Report(string file, int line, string rule, string message)
    {
        var count = _ruleCounts.GetValueOrDefault(rule) + 1;
        _ruleCounts[rule] = count;
        if (count <= MaxPerRule)
        {
            var location = line > 0 ? $"{file}:{line.ToString(CultureInfo.InvariantCulture)}" : file;
            _violations.Add($"{location}: [{rule}] {message}");
        }
    }

    private void Run()
    {
        CheckFileSet();
        var ordered = LoadOrder.Concat(_schema.Tables.Select(t => t.Name).Where(n => !LoadOrder.Contains(n))).Where(_schema.HasTable);
        foreach (var name in ordered)
        {
            LoadTable(_schema.Table(name));
        }

        var resolver = BuildResolver();
        foreach (var table in _tables.Values)
        {
            CheckPlacement(table, resolver);
            CheckUnique(table);
            CheckForeignKeys(table);
        }

        CheckRelations();
        CheckConsistency();
        CheckPartitionRule();
        CheckInventory();
    }

    // ---- file set ----

    private void CheckFileSet()
    {
        if (!Directory.Exists(_root))
        {
            Report(_root, 0, RuleFileSet, "The data directory does not exist.");
            return;
        }

        var tableNames = _schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(_root))
        {
            var name = Path.GetFileName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            var isTableFile = path.EndsWith(DataLayout.Extension, StringComparison.Ordinal) && tableNames.Contains(stem) && File.Exists(path) && _schema.Table(stem).Partition == PartitionKind.None;
            var isTableDirectory = Directory.Exists(path) && tableNames.Contains(name) && _schema.Table(name).Partition != PartitionKind.None;
            var isDocument = File.Exists(path) && (name == "schema.yaml" || name.EndsWith(".md", StringComparison.Ordinal));
            if (!isTableFile && !isTableDirectory && !isDocument)
            {
                Report(path, 0, RuleFileSet, "Unexpected file or folder in the data directory.");
            }
        }
    }

    private IEnumerable<(string Path, string Key)> TableFiles(TableDef table)
    {
        if (table.Partition == PartitionKind.None)
        {
            yield return (Path.Join(_root, table.Name + DataLayout.Extension), string.Empty);
            yield break;
        }

        var directory = Path.Join(_root, table.Name);
        if (!Directory.Exists(directory))
        {
            // A partitioned table without rows has no files, so a missing folder is an empty table
            yield break;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (!File.Exists(path) || !path.EndsWith(DataLayout.Extension, StringComparison.Ordinal) || !DataLayout.IsValidKey(key, _schema.FileSplit))
            {
                Report(path, 0, RuleFileSet, $"'{Path.GetFileName(path)}' is not a valid file of table '{table.Name}' for the file split plan.");
                continue;
            }

            yield return (path, key);
        }
    }

    // ---- reading ----

    private void LoadTable(TableDef table)
    {
        var loaded = new LoadedTable(table);
        _tables[table.Name] = loaded;
        foreach (var (path, key) in TableFiles(table))
        {
            if (!File.Exists(path))
            {
                Report(path, 0, RuleFileSet, $"The file of table '{table.Name}' is missing.");
                continue;
            }

            var rows = ReadFile(table, path, key);
            if (rows is not null)
            {
                loaded.Files.Add(new FileRows(path, key, rows));
            }
        }
    }

    private List<string[]>? ReadFile(TableDef table, string path, string key)
    {
        var lines = ReadLines(path);
        if (lines is null)
        {
            return null;
        }

        var header = string.Join('\t', table.Columns.Select(c => c.Name));
        if (lines.Count == 0 || lines[0] != header)
        {
            Report(path, 1, "header", $"The header must be '{header}'.");
            return null;
        }

        if (lines.Count == 1 && table.Partition != PartitionKind.None && key.Length > 0)
        {
            Report(path, 0, "empty-file", "A partition file must hold at least one row.");
        }

        var rows = new List<string[]>(lines.Count - 1);
        var comparer = DataLayout.RowComparer(table);
        for (var i = 1; i < lines.Count; i++)
        {
            var row = ParseRow(table, path, i + 1, lines[i]);
            if (row is null)
            {
                continue;
            }

            if (rows.Count > 0 && comparer.Compare(rows[^1], row) >= 0)
            {
                Report(path, i + 1, "sort", $"The row is not strictly after the previous row (key {string.Join(", ", table.PrimaryKey.Select(c => row[table.IndexOf(c)]))}).");
            }

            rows.Add(row);
        }

        return rows;
    }

    private List<string>? ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            Report(path, 1, RuleFormat, "The file starts with a byte-order mark.");
            return null;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Report(path, 0, RuleFormat, "The file is not valid UTF-8.");
            return null;
        }

        if (text.Contains('\r'))
        {
            Report(path, 0, RuleFormat, "The file contains a carriage return; line ends must be LF.");
            return null;
        }

        if (text.Length == 0 || text[^1] != '\n')
        {
            Report(path, 0, RuleFormat, "The file must end with a line feed.");
            return null;
        }

        return [.. text[..^1].Split('\n')];
    }

    private string[]? ParseRow(TableDef table, string path, int line, string text)
    {
        if (text.Length == 0)
        {
            Report(path, line, RuleFormat, "Blank line.");
            return null;
        }

        var cells = text.Split('\t');
        if (cells.Length != table.Columns.Count)
        {
            Report(path, line, "cell-count", $"Expected {table.Columns.Count.ToString(CultureInfo.InvariantCulture)} cells but found {cells.Length.ToString(CultureInfo.InvariantCulture)}.");
            return null;
        }

        var ok = true;
        for (var c = 0; c < cells.Length; c++)
        {
            var problem = CheckCellValue(table.Columns[c], cells[c]);
            if (problem is not null)
            {
                Report(path, line, "cell", $"Column '{table.Columns[c].Name}': {problem}");
                ok = false;
            }
        }

        return ok ? cells : null;
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalInt();

    private string? CheckCellValue(ColumnDef column, string cell)
    {
        if (cell.Length == 0)
        {
            return column.Nullable ? null : "The value is empty but the column is not nullable.";
        }

        if (!cell.IsNormalized(NormalizationForm.FormC))
        {
            return "The value is not in Unicode normalization form C.";
        }

        if (cell[0] == ' ' || cell[^1] == ' ')
        {
            return "The value has a leading or trailing space.";
        }

        var problem = DataLayout.CheckCell(cell);
        return problem ?? column.Kind switch
        {
            ColumnKind.Id => cell.Any(char.IsWhiteSpace) ? "An identifier must not contain white space." : null,
            ColumnKind.Int => CanonicalInt().IsMatch(cell) ? null : "The value is not a canonical non-negative decimal integer.",
            ColumnKind.Enum => _schema.IsMember(column.Vocabulary!, cell) ? null : $"'{cell}' is not in vocabulary '{column.Vocabulary}'.",
            ColumnKind.Features => CheckFeatures(cell),
            _ => null,
        };
    }

    private string? CheckFeatures(string cell)
    {
        var items = cell.Split('+');
        for (var i = 0; i < items.Length; i++)
        {
            var parts = items[i].Split('=');
            var known = parts.Length == 2 && _schema.Features.TryGetValue(parts[0], out var values) && values.Contains(parts[1]);
            if (!known)
            {
                return $"'{items[i]}' is not a Feature=Value item of the feature vocabulary.";
            }

            if (i > 0 && DataLayout.CompareUtf8(items[i - 1], items[i]) >= 0)
            {
                return "Feature items must be sorted and unique.";
            }
        }

        return null;
    }

    // ---- placement, keys, references ----

    private PartitionResolver BuildResolver()
    {
        var resolver = new PartitionResolver(_schema.FileSplit);
        foreach (var row in Rows(TableEntry))
        {
            resolver.AddEntry(row[0], row[1]);
        }

        foreach (var row in Rows(TableSense))
        {
            resolver.AddSense(row[0], row[2], row[3], row[1]);
        }

        foreach (var row in Rows(TableCoarseClass))
        {
            resolver.AddClass(row[0], row[1]);
        }

        return resolver;
    }

    private IEnumerable<string[]> Rows(string table) => _tables.TryGetValue(table, out var loaded) ? loaded.Rows : [];

    // A lookup that tolerates duplicate keys (a duplicate is reported by the unique rule, not by a crash here)
    private static Dictionary<string, string> Index(IEnumerable<string[]> rows, int keyColumn, int valueColumn)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result[row[keyColumn]] = row[valueColumn];
        }

        return result;
    }

    private void CheckPlacement(LoadedTable table, PartitionResolver resolver)
    {
        if (table.Definition.Partition == PartitionKind.None)
        {
            return;
        }

        foreach (var file in table.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var expected = resolver.KeyFor(table.Definition, file.Rows[i]);
                if (expected is not null && expected != file.Key)
                {
                    Report(file.Path, i + 2, "placement", $"The row belongs in file '{expected}{DataLayout.Extension}'.");
                }
            }
        }
    }

    private void CheckUnique(LoadedTable table)
    {
        var def = table.Definition;
        var constraints = new List<IReadOnlyList<string>> { def.PrimaryKey };
        constraints.AddRange(def.Unique);
        foreach (var columns in constraints)
        {
            var indexes = columns.Select(def.IndexOf).ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in table.Files)
            {
                for (var i = 0; i < file.Rows.Count; i++)
                {
                    var values = indexes.Select(x => file.Rows[i][x]).ToArray();
                    var isPrimary = ReferenceEquals(columns, def.PrimaryKey);
                    if ((isPrimary || values.All(v => v.Length > 0)) && !seen.Add(string.Join('\t', values)))
                    {
                        Report(file.Path, i + 2, "unique", $"Duplicate value for ({string.Join(", ", columns)}): {string.Join(", ", values)}.");
                    }
                }
            }
        }
    }

    private void CheckForeignKeys(LoadedTable table)
    {
        var def = table.Definition;
        foreach (var (column, target) in def.ForeignKeys)
        {
            var targets = _tables.TryGetValue(target.Table, out var parent) ? ValueSet(parent, target.Column) : [];
            var index = def.IndexOf(column);
            foreach (var file in table.Files)
            {
                for (var i = 0; i < file.Rows.Count; i++)
                {
                    var value = file.Rows[i][index];
                    if (value.Length > 0 && !targets.Contains(value))
                    {
                        Report(file.Path, i + 2, "foreign-key", $"{column} '{value}' is not in {target.Table}.{target.Column}.");
                    }
                }
            }
        }
    }

    private static HashSet<string> ValueSet(LoadedTable table, string column)
    {
        var index = table.Definition.IndexOf(column);
        return table.Rows.Select(r => r[index]).ToHashSet(StringComparer.Ordinal);
    }

    // ---- relations ----

    private void CheckRelations()
    {
        CheckRelationTable("synset_relation", "synset_relation_type", 0, 1, -1, 2);
        CheckRelationTable("sense_relation", "sense_relation_type", 0, 1, 2, 3);
    }

    private void CheckRelationTable(string name, string vocabulary, int from, int rel, int subtype, int to)
    {
        if (!_tables.TryGetValue(name, out var table))
        {
            return;
        }

        var types = _schema.RelationTypes(vocabulary);
        foreach (var file in table.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                var type = types[row[rel]];
                var problem = RelationProblem(type, row[from], row[to], subtype >= 0 ? row[subtype] : string.Empty);
                if (problem is not null)
                {
                    Report(file.Path, i + 2, "relation", $"{row[from]} {row[rel]} {row[to]}: {problem}");
                }
            }
        }
    }

    private static string? RelationProblem(RelationType type, string from, string to, string subtype)
    {
        if (from == to)
        {
            return "A relation must not connect a node to itself.";
        }

        if (!type.Stored)
        {
            return $"'{type.Name}' is a derived direction; store the relation as '{type.Inverse}' in the other direction.";
        }

        if (type.Symmetric && DataLayout.CompareUtf8(from, to) >= 0)
        {
            return "A symmetric relation is stored once, with the smaller id first.";
        }

        if (type.Name == "other" != (subtype.Length > 0) && (type.Name == "other" || subtype.Length > 0))
        {
            return type.Name == "other" ? "The 'other' relation requires a subtype." : "Only the 'other' relation may have a subtype.";
        }

        return null;
    }

    // ---- consistency ----

    private void CheckConsistency()
    {
        CheckEntries();
        CheckSynsets();
        CheckSenses();
        CheckExamples();
        CheckForms();
    }

    private void CheckEntries()
    {
        if (!_tables.TryGetValue(TableEntry, out var entries))
        {
            return;
        }

        foreach (var file in entries.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var problem = EntryProblem(file.Rows[i]);
                if (problem is not null)
                {
                    Report(file.Path, i + 2, TableEntry, $"{file.Rows[i][0]}: {problem}");
                }
            }
        }
    }

    private static string? EntryProblem(string[] row)
    {
        var tokens = row[1].Split(' ').Length;
        var count = int.Parse(row[4], CultureInfo.InvariantCulture);
        var head = int.Parse(row[5], CultureInfo.InvariantCulture);
        if (count != tokens)
        {
            return $"token_count is {count.ToString(CultureInfo.InvariantCulture)} but the lemma has {tokens.ToString(CultureInfo.InvariantCulture)} space-separated tokens.";
        }

        return head < 1 || head > count ? "head_idx must be between 1 and token_count." : null;
    }

    private void CheckSynsets()
    {
        if (!_tables.TryGetValue(TableSynset, out var synsets))
        {
            return;
        }

        var pos = Index(synsets.Rows, 0, 1);
        var withSense = Rows(TableSense).Select(r => r[3]).ToHashSet(StringComparer.Ordinal);
        var withGloss = Rows("gloss").Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        foreach (var file in synsets.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                var head = row[2];
                var problem = SynsetProblem(row, head.Length > 0 ? pos.GetValueOrDefault(head) : null, withSense.Contains(row[0]), withGloss.Contains(row[0]));
                if (problem is not null)
                {
                    Report(file.Path, i + 2, TableSynset, $"{row[0]}: {problem}");
                }
            }
        }
    }

    private static string? SynsetProblem(string[] row, string? headPos, bool hasSense, bool hasGloss)
    {
        if (!hasSense)
        {
            return "A synset needs at least one sense.";
        }

        if (!hasGloss)
        {
            return "A synset needs at least one gloss.";
        }

        if (row[1] == "s")
        {
            return row[2].Length == 0 || headPos != "a" ? "A satellite synset needs a head synset of part of speech 'a'." : null;
        }

        return row[2].Length > 0 ? "Only a satellite synset has a head synset." : null;
    }

    private void CheckSenses()
    {
        if (!_tables.TryGetValue(TableSense, out var senses))
        {
            return;
        }

        var synsetPos = Index(Rows(TableSynset), 0, 1);
        var entryPos = Index(Rows(TableEntry), 0, 2);
        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var file in senses.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                if (!positions.TryGetValue(row[2], out var list))
                {
                    positions[row[2]] = list = [];
                }

                list.Add(int.Parse(row[4], CultureInfo.InvariantCulture));
                if (entryPos.TryGetValue(row[2], out var ep) && synsetPos.TryGetValue(row[3], out var sp) && !PosCompatible(ep, sp))
                {
                    Report(file.Path, i + 2, TableSense, $"{row[0]}: entry part of speech '{ep}' does not fit synset part of speech '{sp}'.");
                }
            }
        }

        foreach (var (entry, list) in positions)
        {
            list.Sort();
            if (!list.SequenceEqual(Enumerable.Range(1, list.Count)))
            {
                Report(Path.Join(_root, TableSense), 0, TableSense, $"The positions of the senses of entry '{entry}' are not 1..{list.Count.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        // An entry without a sense exists only for a closed-class word (pronoun, preposition, conjunction, interjection,
        // determiner, auxiliary) or because a class or a carried analysis of the earlier table refers to it; any other
        // lemma and part of speech without senses is a surface-only analysis of the form table, never an entry
        var referenced = Rows("class_entry").Select(r => r[1]).ToHashSet(StringComparer.Ordinal);
        foreach (var row in Rows("form").Where(r => r[2].Length > 0 && r[8] == CarriedProvenance))
        {
            referenced.Add(row[2]);
        }

        foreach (var entry in Rows(TableEntry).Where(r => !positions.ContainsKey(r[0]) && !ClosedClass.Contains(r[2]) && !referenced.Contains(r[0])))
        {
            Report(Path.Join(_root, TableEntry), 0, TableSense, $"Entry '{entry[0]}' has no sense and no class or carried analysis refers to it.");
        }
    }

    private const string CarriedProvenance = "carried";

    private static readonly HashSet<string> ClosedClass = new(["u", "p", "c", "i", "d", "x"], StringComparer.Ordinal);

    private static readonly HashSet<string> SurfaceProvenance = new(["moby_reverse", "rule", "rule_variant", "agid", "irregular_list", "hand", "llm"], StringComparer.Ordinal);

    // ---- forms ----

    private void CheckForms()
    {
        if (!_tables.TryGetValue("form", out var forms))
        {
            return;
        }

        var entryPos = Index(Rows(TableEntry), 0, 2);
        var entryKeys = Rows(TableEntry).Select(r => (r[1], r[2])).ToHashSet();
        var classKind = Index(Rows(TableCoarseClass), 0, 4);
        var classPos = Index(Rows(TableCoarseClass), 0, 3);
        var ranks = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var file in forms.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                var rank = int.Parse(row[1], CultureInfo.InvariantCulture);
                if (!ranks.TryGetValue(row[0], out var set))
                {
                    ranks[row[0]] = set = [];
                }

                set.Add(rank);
                if (row[5] is not ("0" or "1"))
                {
                    Report(file.Path, i + 2, "form", $"{row[0]}#{row[1]}: reviewed must be 0 or 1.");
                }

                var problem = FormProblem(row, entryKeys);
                if (problem is not null)
                {
                    Report(file.Path, i + 2, "form", $"{row[0]}#{row[1]}: {problem}");
                }
            }
        }

        foreach (var (spelling, set) in ranks.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!set.OrderBy(r => r).SequenceEqual(Enumerable.Range(0, set.Count)))
            {
                Report(Path.Join(_root, "form"), 0, "form", $"The ranks of the analyses of '{spelling}' are not 0..{(set.Count - 1).ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        if (!_tables.TryGetValue(TableFormSenseScope, out var scopes))
        {
            return;
        }

        var formEntry = new Dictionary<(string, int), string>();
        foreach (var row in Rows("form"))
        {
            formEntry[(row[0], int.Parse(row[1], CultureInfo.InvariantCulture))] = row[2];
        }

        foreach (var file in scopes.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                if (!formEntry.TryGetValue((row[0], int.Parse(row[1], CultureInfo.InvariantCulture)), out var entry))
                {
                    Report(file.Path, i + 2, TableFormSenseScope, $"{row[0]}#{row[1]}: there is no such analysis in the form table.");
                    continue;
                }

                if (entry.Length == 0)
                {
                    Report(file.Path, i + 2, TableFormSenseScope, $"{row[0]}#{row[1]}: a surface-only analysis has no entry and cannot have a scope.");
                    continue;
                }

                if (classKind.GetValueOrDefault(row[2]) == "unmapped")
                {
                    Report(file.Path, i + 2, TableFormSenseScope, $"{row[0]}#{row[1]}: class '{row[2]}' is an unmapped bucket and cannot be an answer.");
                }
                else if (entryPos.TryGetValue(entry, out var ep) && classPos.TryGetValue(row[2], out var cp) && ep != cp)
                {
                    Report(file.Path, i + 2, TableFormSenseScope, $"{row[0]}#{row[1]}: class '{row[2]}' has part of speech '{cp}' but entry '{entry}' has '{ep}'.");
                }
            }
        }
    }

    private static string? FormProblem(string[] row, HashSet<(string, string)> entryKeys)
    {
        var hasEntry = row[2].Length > 0;
        var hasLemma = row[6].Length > 0;
        var hasPos = row[7].Length > 0;
        if (hasEntry)
        {
            return hasLemma || hasPos ? "An entry-backed analysis must not have a lemma or a part of speech of its own." : null;
        }

        if (!hasLemma || !hasPos)
        {
            return "A surface-only analysis needs a lemma and a part of speech (or an entry).";
        }

        if (entryKeys.Contains((row[6], row[7])))
        {
            return $"The lemma '{row[6]}' with part of speech '{row[7]}' is an entry; the analysis must use the entry.";
        }

        if (row[5] != "0")
        {
            return "A surface-only analysis must be unreviewed.";
        }

        return SurfaceProvenance.Contains(row[8]) ? null : $"Provenance '{row[8]}' is not allowed for a surface-only analysis.";
    }

    private static bool PosCompatible(string entryPos, string synsetPos) =>
        entryPos == "a" ? synsetPos is "a" or "s" : entryPos == synsetPos;

    private void CheckExamples()
    {
        if (!_tables.TryGetValue("example", out var examples))
        {
            return;
        }

        var senseSynset = Index(Rows(TableSense), 0, 3);
        foreach (var file in examples.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                var row = file.Rows[i];
                if (row[2].Length > 0 && senseSynset.TryGetValue(row[2], out var synset) && synset != row[0])
                {
                    Report(file.Path, i + 2, "example", $"Sense '{row[2]}' is not a sense of synset '{row[0]}'.");
                }
            }
        }
    }

    // ---- partition rule ----

    private void CheckPartitionRule()
    {
        var classes = Rows(TableCoarseClass).ToList();
        if (classes.Count == 0)
        {
            return;
        }

        var covered = Rows("class_sense").Select(r => r[1]).ToHashSet(StringComparer.Ordinal);
        var mapped = classes.Where(c => c[4] is "sense_group" or "no_oewn" or "unmapped").Select(c => (Lemma: c[1], Pos: c[3])).ToHashSet();
        var entries = Index(Rows(TableEntry).Where(e => mapped.Contains((e[1], e[2]))), 0, 1);
        var path = Path.Join(_root, "class_sense");
        foreach (var sense in Rows(TableSense))
        {
            if (entries.TryGetValue(sense[2], out var lemma) && !covered.Contains(sense[0]))
            {
                Report(path, 0, "partition", $"Sense '{sense[0]}' of mapped lemma '{lemma}' is in no class; put it in a class or in an 'unmapped' class.");
            }
        }
    }
}
