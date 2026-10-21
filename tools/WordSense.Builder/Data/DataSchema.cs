using System.Globalization;

namespace WordSense.Builder.Data;

/// <summary>
///     The kind of a column type declared in <c>data/schema.yaml</c>.
/// </summary>
internal enum ColumnKind
{
    /// <summary>A free text value.</summary>
    Text,

    /// <summary>A stable identifier without white space.</summary>
    Id,

    /// <summary>A canonical non-negative decimal integer.</summary>
    Int,

    /// <summary>A member of a closed vocabulary.</summary>
    Enum,

    /// <summary>A set of closed-vocabulary features joined by <c>+</c>.</summary>
    Features,
}

/// <summary>
///     How the rows of a table are assigned to the files of the table folder.
/// </summary>
internal enum PartitionKind
{
    /// <summary>One file <c>data/&lt;table&gt;.tsv</c>.</summary>
    None,

    /// <summary>By the lemma held in the named column.</summary>
    Lemma,

    /// <summary>By the lemma of the entry named in the column.</summary>
    Entry,

    /// <summary>By the lemma of the entry of the sense named in the column.</summary>
    Sense,

    /// <summary>By the lemma of the first sense of the synset named in the column.</summary>
    Synset,

    /// <summary>By the lemma of the entry of the sense whose sense key is named in the column.</summary>
    Key,
}

/// <summary>
///     A column of a table.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="Kind">The kind of the column type.</param>
/// <param name="Vocabulary">The vocabulary name for an <see cref="ColumnKind.Enum"/> column, otherwise null.</param>
/// <param name="Nullable">True when an empty cell (NULL) is allowed.</param>
internal sealed record ColumnDef(string Name, ColumnKind Kind, string? Vocabulary, bool Nullable);

/// <summary>
///     A foreign key target.
/// </summary>
/// <param name="Table">The referenced table.</param>
/// <param name="Column">The referenced column.</param>
internal sealed record ForeignKey(string Table, string Column);

/// <summary>
///     A relation type of a relation vocabulary.
/// </summary>
/// <param name="Name">The relation name.</param>
/// <param name="Inverse">The name of the inverse relation, or null when there is none.</param>
/// <param name="Symmetric">True when the relation is its own inverse.</param>
/// <param name="Stored">True when rows of this direction are stored (the inverse direction is derived).</param>
internal sealed record RelationType(string Name, string? Inverse, bool Symmetric, bool Stored);

/// <summary>
///     A table of the schema.
/// </summary>
/// <param name="Name">The table name.</param>
/// <param name="Columns">The columns in file order.</param>
/// <param name="PrimaryKey">The primary key column names in sort order.</param>
/// <param name="Unique">The additional unique constraints (non-empty cells only).</param>
/// <param name="ForeignKeys">The foreign keys by column name.</param>
/// <param name="Partition">The partition kind.</param>
/// <param name="PartitionColumn">The column the partition is derived from (null for <see cref="PartitionKind.None"/>).</param>
internal sealed record TableDef(
    string Name,
    IReadOnlyList<ColumnDef> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<IReadOnlyList<string>> Unique,
    IReadOnlyDictionary<string, ForeignKey> ForeignKeys,
    PartitionKind Partition,
    string? PartitionColumn)
{
    /// <summary>
    ///     Gets the index of a column.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The zero-based index.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the table has no such column.</exception>
    public int IndexOf(string name)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Name == name)
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"Table '{Name}' has no column '{name}'.");
    }
}

/// <summary>
///     The typed model of <c>data/schema.yaml</c>: tables, columns, constraints, partitioning, closed vocabularies and
///     the file-split plan, read from a small strict subset of YAML.
/// </summary>
/// <remarks>
///     The schema is machine-checked data, so the reader supports only what the file needs (block mappings, block
///     sequences, flow sequences of scalars, plain or quoted scalars, comments) and fails with the file, line and reason
///     for anything else. This avoids a third-party YAML dependency, which would need its own OTS item. Instances are
///     immutable and thread-safe.
/// </remarks>
internal sealed class DataSchema
{
    /// <summary>
    ///     The schema format version this tool reads and writes.
    /// </summary>
    public const int SupportedVersion = 4;

    private readonly Dictionary<string, TableDef> _tables;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _vocabularies;
    private readonly Dictionary<string, IReadOnlyDictionary<string, RelationType>> _relationTypes;

    private DataSchema(
        int version,
        string release,
        IReadOnlyList<char> fileSplit,
        Dictionary<string, TableDef> tables,
        Dictionary<string, IReadOnlyDictionary<string, string>> vocabularies,
        Dictionary<string, IReadOnlyDictionary<string, RelationType>> relationTypes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> features)
    {
        Version = version;
        Release = release;
        FileSplit = fileSplit;
        _tables = tables;
        _vocabularies = vocabularies;
        _relationTypes = relationTypes;
        Features = features;
    }

    /// <summary>
    ///     Gets the schema format version.
    /// </summary>
    public int Version { get; }

    /// <summary>
    ///     Gets the data release the committed tables are pinned to (for example <c>oewn-2025</c>).
    /// </summary>
    public string Release { get; }

    /// <summary>
    ///     Gets the first letters whose files are split by the first two letters of the lemma.
    /// </summary>
    public IReadOnlyList<char> FileSplit { get; }

    /// <summary>
    ///     Gets the feature vocabulary: feature name to its allowed values.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; }

    /// <summary>
    ///     Gets the tables in declaration order.
    /// </summary>
    public IEnumerable<TableDef> Tables => _tables.Values;

    /// <summary>
    ///     Gets the names of all plain vocabularies.
    /// </summary>
    public IEnumerable<string> VocabularyNames => _vocabularies.Keys;

    /// <summary>
    ///     Gets the names of the relation vocabularies (the two relation levels).
    /// </summary>
    public IEnumerable<string> RelationVocabularyNames => _relationTypes.Keys;

    /// <summary>
    ///     Gets a table by name.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the schema has no such table.</exception>
    public TableDef Table(string name) =>
        _tables.TryGetValue(name, out var table) ? table : throw new KeyNotFoundException($"The schema has no table '{name}'.");

    /// <summary>
    ///     Tests whether the schema declares a table.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>True when the schema declares the table.</returns>
    public bool HasTable(string name) => _tables.ContainsKey(name);

    /// <summary>
    ///     Gets a plain vocabulary (value to description).
    /// </summary>
    /// <param name="name">The vocabulary name, for example <c>pos</c>.</param>
    /// <returns>The vocabulary.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the schema has no such vocabulary.</exception>
    public IReadOnlyDictionary<string, string> Vocabulary(string name) =>
        _vocabularies.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException($"The schema has no vocabulary '{name}'.");

    /// <summary>
    ///     Gets a relation vocabulary.
    /// </summary>
    /// <param name="name">The vocabulary name, <c>synset_relation_type</c> or <c>sense_relation_type</c>.</param>
    /// <returns>The relation types by name.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the schema has no such vocabulary.</exception>
    public IReadOnlyDictionary<string, RelationType> RelationTypes(string name) =>
        _relationTypes.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException($"The schema has no relation vocabulary '{name}'.");

    /// <summary>
    ///     Tests whether a value belongs to the vocabulary an enum column refers to.
    /// </summary>
    /// <param name="vocabulary">The vocabulary name; a relation vocabulary matches its relation names.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the value is a member.</returns>
    public bool IsMember(string vocabulary, string value)
    {
        if (_vocabularies.TryGetValue(vocabulary, out var plain))
        {
            return plain.ContainsKey(value);
        }

        return _relationTypes.TryGetValue(vocabulary, out var relations) && relations.ContainsKey(value);
    }

    /// <summary>
    ///     Reads a schema file.
    /// </summary>
    /// <param name="path">The path of <c>schema.yaml</c>.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="InvalidDataException">Thrown with file and line when the file is malformed or inconsistent.</exception>
    public static DataSchema Load(string path)
    {
        // Read the whole file; the schema is small and the parser needs random access to line numbers
        var lines = File.ReadAllLines(path);
        try
        {
            var root = SchemaYaml.Parse(lines);
            return Build(root);
        }
        catch (SchemaException ex)
        {
            throw new InvalidDataException($"{path}:{ex.Line.ToString(CultureInfo.InvariantCulture)}: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Replaces the <c>file_split</c> line of a schema file with a new plan, keeping everything else.
    /// </summary>
    /// <param name="path">The path of <c>schema.yaml</c>.</param>
    /// <param name="letters">The letters whose files are split.</param>
    /// <exception cref="InvalidDataException">Thrown when the file has no <c>file_split</c> line.</exception>
    public static void WriteFileSplit(string path, IEnumerable<char> letters)
    {
        var lines = File.ReadAllLines(path);
        var index = Array.FindIndex(lines, l => l.StartsWith("file_split:", StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidDataException($"{path}: no 'file_split:' line to update.");
        }

        lines[index] = "file_split: [" + string.Join(", ", letters.OrderBy(c => c)) + "]";
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(false));
    }

    /// <summary>
    ///     Converts the parsed tree to the typed model and checks its internal consistency.
    /// </summary>
    /// <param name="root">The root mapping.</param>
    /// <returns>The schema.</returns>
    private static DataSchema Build(SchemaYaml.Node root)
    {
        var map = root.AsMap();
        var versionNode = map.Require("schema_version");
        if (!int.TryParse(versionNode.AsScalar(), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw new SchemaException(versionNode.Line, $"schema_version '{versionNode.AsScalar()}' is not a non-negative integer.");
        }

        if (version != SupportedVersion)
        {
            throw new SchemaException(versionNode.Line, $"schema_version {version.ToString(CultureInfo.InvariantCulture)} is not supported; this tool reads version {SupportedVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        var release = map.Require("data_release").AsScalar();
        var split = ReadSplit(map.Require("file_split"));
        var vocabularies = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var relations = new Dictionary<string, IReadOnlyDictionary<string, RelationType>>(StringComparer.Ordinal);
        ReadVocabularies(map.Require("vocabularies"), vocabularies, relations);
        var features = ReadFeatures(map.Require("features"));
        var tables = new Dictionary<string, TableDef>(StringComparer.Ordinal);
        foreach (var (name, node) in map.Require("tables").AsMap().Entries)
        {
            tables[name] = ReadTable(name, node);
        }

        var schema = new DataSchema(version, release, split, tables, vocabularies, relations, features);
        schema.CheckConsistency(map.Require("tables"), map.Require("vocabularies"));
        return schema;
    }

    private static List<char> ReadSplit(SchemaYaml.Node node)
    {
        var letters = new List<char>();
        foreach (var item in node.AsSequence().Items)
        {
            var text = item.AsScalar();
            if (text.Length != 1 || text[0] is < 'a' or > 'z')
            {
                throw new SchemaException(item.Line, $"file_split entry '{text}' is not a single letter a-z.");
            }

            letters.Add(text[0]);
        }

        return letters;
    }

    private static void ReadVocabularies(
        SchemaYaml.Node node,
        Dictionary<string, IReadOnlyDictionary<string, string>> plain,
        Dictionary<string, IReadOnlyDictionary<string, RelationType>> relations)
    {
        foreach (var (name, body) in node.AsMap().Entries)
        {
            if (name.EndsWith("_relation_type", StringComparison.Ordinal))
            {
                relations[name] = ReadRelationTypes(body);
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (value, description) in body.AsMap().Entries)
            {
                values[value] = description.AsScalar();
            }

            plain[name] = values;
        }
    }

    private static Dictionary<string, RelationType> ReadRelationTypes(SchemaYaml.Node node)
    {
        var result = new Dictionary<string, RelationType>(StringComparer.Ordinal);
        foreach (var (name, body) in node.AsMap().Entries)
        {
            var fields = body.AsMap();
            var inverse = fields.Require("inverse").AsScalar();
            result[name] = new RelationType(
                name,
                inverse.Length == 0 ? null : inverse,
                ReadYesNo(fields.Require("symmetric")),
                ReadYesNo(fields.Require("stored")));
        }

        return result;
    }

    private static bool ReadYesNo(SchemaYaml.Node node) => node.AsScalar() switch
    {
        "yes" or "true" => true,
        "no" or "false" => false,
        var other => throw new SchemaException(node.Line, $"Expected 'true' or 'false' but found '{other}'."),
    };

    private static Dictionary<string, IReadOnlyList<string>> ReadFeatures(SchemaYaml.Node node)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in node.AsMap().Entries)
        {
            result[name] = values.AsSequence().Items.Select(i => i.AsScalar()).ToList();
        }

        return result;
    }

    private static TableDef ReadTable(string name, SchemaYaml.Node node)
    {
        var map = node.AsMap();
        var columns = new List<ColumnDef>();
        foreach (var (column, type) in map.Require("columns").AsMap().Entries)
        {
            columns.Add(ParseColumn(column, type));
        }

        var primaryKey = map.Require("primary_key").AsSequence().Items.Select(i => i.AsScalar()).ToList();
        var unique = new List<IReadOnlyList<string>>();
        if (map.TryGet("unique", out var uniqueNode))
        {
            unique.AddRange(uniqueNode.AsSequence().Items.Select(i => (IReadOnlyList<string>)i.AsSequence().Items.Select(c => c.AsScalar()).ToList()));
        }

        var foreignKeys = new Dictionary<string, ForeignKey>(StringComparer.Ordinal);
        if (map.TryGet("foreign_keys", out var fkNode))
        {
            foreach (var (column, target) in fkNode.AsMap().Entries)
            {
                foreignKeys[column] = ParseForeignKey(target);
            }
        }

        var (kind, partitionColumn) = ReadPartition(map.Require("partition"));
        return new TableDef(name, columns, primaryKey, unique, foreignKeys, kind, partitionColumn);
    }

    private static ForeignKey ParseForeignKey(SchemaYaml.Node target)
    {
        var parts = target.AsScalar().Split('.');
        return parts.Length == 2
            ? new ForeignKey(parts[0], parts[1])
            : throw new SchemaException(target.Line, $"Foreign key target '{target.AsScalar()}' must be 'table.column'.");
    }

    private static (PartitionKind Kind, string? Column) ReadPartition(SchemaYaml.Node node)
    {
        var partition = node.AsMap();
        var by = partition.Require("by");
        var kind = by.AsScalar() switch
        {
            "none" => PartitionKind.None,
            "lemma" => PartitionKind.Lemma,
            "entry" => PartitionKind.Entry,
            "sense" => PartitionKind.Sense,
            "synset" => PartitionKind.Synset,
            "key" => PartitionKind.Key,
            var other => throw new SchemaException(by.Line, $"Unknown partition kind '{other}'."),
        };
        return (kind, kind == PartitionKind.None ? null : partition.Require("column").AsScalar());
    }

    private static ColumnDef ParseColumn(string name, SchemaYaml.Node typeNode)
    {
        var text = typeNode.AsScalar();
        var nullable = text.EndsWith('?');
        if (nullable)
        {
            text = text[..^1];
        }

        if (text.StartsWith("enum:", StringComparison.Ordinal))
        {
            return new ColumnDef(name, ColumnKind.Enum, text["enum:".Length..], nullable);
        }

        var kind = text switch
        {
            "text" => ColumnKind.Text,
            "id" => ColumnKind.Id,
            "int" => ColumnKind.Int,
            "features" => ColumnKind.Features,
            _ => throw new SchemaException(typeNode.Line, $"Unknown column type '{text}'."),
        };
        return new ColumnDef(name, kind, null, nullable);
    }

    /// <summary>
    ///     Checks that every key, foreign key, partition column, vocabulary and relation inverse refers to something
    ///     that exists, so a typo in the schema fails when it is loaded and not when data is validated.
    /// </summary>
    /// <param name="tablesNode">The tables node, for line numbers.</param>
    /// <param name="vocabulariesNode">The vocabularies node, for the line numbers of the relation types.</param>
    private void CheckConsistency(SchemaYaml.Node tablesNode, SchemaYaml.Node vocabulariesNode)
    {
        var lines = tablesNode.AsMap();
        foreach (var table in _tables.Values)
        {
            CheckTable(table, lines.Require(table.Name).Line);
        }

        var vocabularyLines = vocabulariesNode.AsMap();
        foreach (var (level, types) in _relationTypes)
        {
            var typeLines = vocabularyLines.Require(level).AsMap();
            foreach (var type in types.Values)
            {
                CheckRelationType(level, types, type, typeLines.Require(type.Name).Line);
            }
        }
    }

    private void CheckTable(TableDef table, int line)
    {
        var names = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var referenced = table.PrimaryKey.Concat(table.Unique.SelectMany(u => u)).Concat(table.ForeignKeys.Keys);
        if (table.PartitionColumn is not null)
        {
            referenced = referenced.Append(table.PartitionColumn);
        }

        var unknown = referenced.FirstOrDefault(c => !names.Contains(c));
        if (unknown is not null)
        {
            throw new SchemaException(line, $"Table '{table.Name}' refers to unknown column '{unknown}'.");
        }

        var badColumn = table.Columns.FirstOrDefault(c => c.Kind == ColumnKind.Enum && !_vocabularies.ContainsKey(c.Vocabulary!) && !_relationTypes.ContainsKey(c.Vocabulary!));
        if (badColumn is not null)
        {
            throw new SchemaException(line, $"Column '{table.Name}.{badColumn.Name}' refers to unknown vocabulary '{badColumn.Vocabulary}'.");
        }

        foreach (var fk in table.ForeignKeys.Values)
        {
            if (!_tables.TryGetValue(fk.Table, out var target) || target.Columns.All(c => c.Name != fk.Column))
            {
                throw new SchemaException(line, $"Table '{table.Name}' has a foreign key to unknown '{fk.Table}.{fk.Column}'.");
            }
        }
    }

    private static void CheckRelationType(string level, IReadOnlyDictionary<string, RelationType> types, RelationType type, int line)
    {
        if (type.Inverse is null)
        {
            if (type.Symmetric || !type.Stored)
            {
                throw new SchemaException(line, $"{level} '{type.Name}' has no inverse, so it must be stored and not symmetric.");
            }

            return;
        }

        if (!types.TryGetValue(type.Inverse, out var inverse) || inverse.Inverse != type.Name)
        {
            throw new SchemaException(line, $"{level} '{type.Name}' has inverse '{type.Inverse}', which does not point back to it.");
        }

        var consistent = type.Symmetric ? type.Stored && inverse == type : type.Stored != inverse.Stored;
        if (!consistent)
        {
            throw new SchemaException(line, $"{level} '{type.Name}' and its inverse '{type.Inverse}' must store exactly one direction (a symmetric type stores itself).");
        }
    }
}
