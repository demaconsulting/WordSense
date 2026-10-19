using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="DataSchema"/> and its YAML-subset parser, on the committed <c>data/schema.yaml</c> and on
///     damaged copies of it.
/// </summary>
public sealed class DataSchemaTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string SchemaText() => File.ReadAllText(Path.Join(DataFixture.RepositoryRoot(), "data", "schema.yaml"), Encoding.UTF8);

    private DataSchema LoadText(string text) => DataSchema.Load(_dir.Write("schema.yaml", text));

    /// <summary>
    ///     The committed schema loads and declares every table of the data model.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_Load_CommittedSchema_DeclaresTables()
    {
        // Arrange / Act
        var schema = LoadText(SchemaText());

        // Assert
        Assert.Equal(2, schema.Version);
        Assert.Equal("oewn-2025", schema.Release);
        Assert.Equal(21, schema.Tables.Count());
        foreach (var name in new[] { "source", "entry", "synset", "gloss", "example", "sense", "synset_relation", "sense_relation", "pronunciation", "id_map", "coarse_class", "class_sense", "class_entry", "form", "form_sense_scope", "sense_label", "sense_label_map", "sense_group", "group_sense", "sense_frequency", "sense_respelling" })
        {
            Assert.True(schema.HasTable(name), name);
        }

        var entry = schema.Table("entry");
        Assert.Equal(PartitionKind.Lemma, entry.Partition);
        Assert.Equal(["entry_id"], entry.PrimaryKey);
        Assert.Equal(ColumnKind.Enum, entry.Columns[entry.IndexOf("pos")].Kind);
        Assert.True(schema.Table("synset").Columns[schema.Table("synset").IndexOf("head_synset_id")].Nullable);
        Assert.Equal(PartitionKind.None, schema.Table("source").Partition);
    }

    /// <summary>
    ///     The sense inventory tables are declared with their keys, foreign keys and vocabularies.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_Load_CommittedSchema_DeclaresSenseGroupAndFrequencyTables()
    {
        // Arrange / Act
        var schema = LoadText(SchemaText());

        // Assert
        Assert.Equal(["sense_id"], schema.Table("sense_label").PrimaryKey);
        Assert.Equal("sense.sense_id", FormatKey(schema.Table("sense_label").ForeignKeys["sense_id"]));
        Assert.Equal(["old_id", "release"], schema.Table("sense_label_map").PrimaryKey);
        Assert.True(schema.Table("sense_label_map").Columns[schema.Table("sense_label_map").IndexOf("new_id")].Nullable);
        Assert.Equal(["group_id"], schema.Table("sense_group").PrimaryKey);
        Assert.Contains(schema.Table("sense_group").Unique, u => u.SequenceEqual(["lemma", "label"]));
        Assert.Equal(["group_id", "sense_id"], schema.Table("group_sense").PrimaryKey);
        Assert.Contains(schema.Table("group_sense").Unique, u => u.SequenceEqual(["sense_id"]));
        Assert.Equal("sense_group.group_id", FormatKey(schema.Table("group_sense").ForeignKeys["group_id"]));
        Assert.Equal(["sense_key"], schema.Table("sense_frequency").PrimaryKey);
        Assert.Equal(PartitionKind.Key, schema.Table("sense_frequency").Partition);
        Assert.Equal("sense.sense_key", FormatKey(schema.Table("sense_frequency").ForeignKeys["sense_key"]));
        Assert.Equal(["sense_id"], schema.Table("sense_respelling").PrimaryKey);
        Assert.True(schema.IsMember("label_origin", "curated"));
        Assert.True(schema.IsMember("label_origin", "generated"));
        Assert.True(schema.IsMember("label_origin", "project"));
        Assert.True(schema.IsMember("group_origin", "default"));
        Assert.True(schema.IsMember("frequency_source", "wn30"));
        Assert.True(schema.IsMember("synset_pos", "d"));
    }

    private static string FormatKey(ForeignKey key) => key.Table + "." + key.Column;

    /// <summary>
    ///     Vocabularies, relation types and features are available to the validator.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_Load_CommittedSchema_DeclaresVocabularies()
    {
        // Arrange / Act
        var schema = LoadText(SchemaText());
        var synsetRelations = schema.RelationTypes("synset_relation_type");

        // Assert
        Assert.True(schema.IsMember("pos", "n"));
        Assert.False(schema.IsMember("pos", "s"));
        Assert.True(schema.IsMember("synset_pos", "s"));
        Assert.True(schema.IsMember("synset_relation_type", "hypernym"));
        Assert.True(synsetRelations["hypernym"].Stored);
        Assert.False(synsetRelations["hyponym"].Stored);
        Assert.Equal("hyponym", synsetRelations["hypernym"].Inverse);
        Assert.True(synsetRelations["similar"].Symmetric);
        Assert.Null(schema.RelationTypes("sense_relation_type")["pertainym"].Inverse);
        Assert.Contains("Past", schema.Features["Tense"]);
    }

    /// <summary>
    ///     The file split line can be rewritten without touching the rest of the schema.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_WriteFileSplit_ReplacesOnlyThatLine()
    {
        // Arrange
        var path = _dir.Write("schema.yaml", SchemaText());

        // Act
        DataSchema.WriteFileSplit(path, ['s', 'a']);
        var reloaded = DataSchema.Load(path);

        // Assert
        Assert.Equal(['a', 's'], reloaded.FileSplit);
        var before = SchemaText().Split('\n');
        var after = File.ReadAllText(path).Split('\n');
        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), p => p.First != p.Second);
    }

    /// <summary>
    ///     An empty split plan round-trips.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_WriteFileSplit_EmptyPlan_RoundTrips()
    {
        // Arrange
        var path = _dir.Write("schema.yaml", SchemaText());

        // Act
        DataSchema.WriteFileSplit(path, []);

        // Assert
        Assert.Empty(DataSchema.Load(path).FileSplit);
    }

    /// <summary>
    ///     Damaged schemas are rejected with a line number or a reason.
    /// </summary>
    [Theory]
    [InlineData("schema_version: 2\n", "tab", "schema_version: 2\n\tx: 1\n")]
    [InlineData("data_release: oewn-2025\n", "duplicate", "data_release: oewn-2025\ndata_release: other\n")]
    [InlineData("enum:synset_pos", "unknown vocabulary", "enum:nope")]
    [InlineData("source_id: source.source_id", "foreign key", "source_id: nowhere.source_id")]
    [InlineData("primary_key: [entry_id]", "unknown column", "primary_key: [no_such_column]")]
    [InlineData("      inverse: hypernym\n      symmetric: false\n      stored: false", "one direction", "      inverse: hypernym\n      symmetric: false\n      stored: true")]
    public void WordSenseBuilder_DataSchema_Load_Damaged_Throws(string original, string reason, string changed)
    {
        // Arrange
        var text = SchemaText();
        Assert.Contains(original, text, StringComparison.Ordinal);
        var damaged = text.Replace(original, changed, StringComparison.Ordinal);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => LoadText(damaged));

        // Assert
        Assert.Contains(reason, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A missing schema file is an I/O error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataSchema_Load_MissingFile_Throws()
    {
        // Arrange / Act / Assert
        Assert.ThrowsAny<IOException>(() => DataSchema.Load(Path.Join(_dir.Root, "none.yaml")));
    }
}
