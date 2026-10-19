using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="OewnImporter"/> on the hand-written OEWN slice.
/// </summary>
public sealed class OewnImporterTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private string SchemaCopy(string name)
    {
        var data = Path.Join(_dir.Root, name);
        Directory.CreateDirectory(data);
        File.Copy(Path.Join(DataFixture.RepositoryRoot(), "data", "schema.yaml"), Path.Join(data, "schema.yaml"));
        return data;
    }

    private string ImportText(string xml, string name = "bad")
    {
        var input = _dir.Write(name + ".xml", xml);
        var data = SchemaCopy(name + "-data");
        OewnImporter.Import(input, data, Path.Join(data, "schema.yaml"), null);
        return data;
    }

    /// <summary>
    ///     The import counts every table of the slice.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_Slice_CountsRows()
    {
        // Arrange
        var data = SchemaCopy("data");

        // Act
        var result = OewnImporter.Import(DataFixture.SlicePath, data, Path.Join(data, "schema.yaml"), null);

        // Assert
        Assert.Equal(1, result.Rows["source"]);
        Assert.Equal(14, result.Rows["entry"]);
        Assert.Equal(14, result.Rows["synset"]);
        Assert.Equal(15, result.Rows["gloss"]);
        Assert.Equal(3, result.Rows["example"]);
        Assert.Equal(14, result.Rows["sense"]);
        Assert.Equal(4, result.Rows["synset_relation"]);
        Assert.Equal(4, result.Rows["sense_relation"]);
        Assert.Equal(3, result.Rows["pronunciation"]);
        Assert.Equal(0, result.Rows["id_map"]);
        Assert.Equal(1, result.MissingInverse);
        Assert.Equal(0, result.SelfRelations);
        Assert.Contains("entry: 14 rows", result.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The imported slice satisfies every validator rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_Slice_IsValid()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     Homograph numbers are 0 for unique pairs and numbered by the id tail otherwise; satellite entries become
    ///     adjectives.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_Entries_DeriveHomographsAndPos()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var entries = DataFixture.Rows(data, "entry").ToDictionary(r => r[0], r => r, StringComparer.Ordinal);

        // Assert
        Assert.Equal("1", entries["oewn-bass-n"][3]);
        Assert.Equal("2", entries["oewn-bass-n-1"][3]);
        Assert.Equal("0", entries["oewn-fish-n"][3]);
        Assert.Equal("a", entries["oewn-large-s"][2]);
        Assert.Equal("2", entries["oewn-run_up-v"][4]);
        Assert.Equal("1", entries["oewn-run_up-v"][5]);
        Assert.Equal("oewn", entries["oewn-fish-n"][6]);
    }

    /// <summary>
    ///     A satellite gets the head from its similar relation; the sense key is derived from the id tail.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_SynsetsAndSenses_DeriveHeadAndKey()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var synsets = DataFixture.Rows(data, "synset").ToDictionary(r => r[0], r => r, StringComparer.Ordinal);
        var senses = DataFixture.Rows(data, "sense").ToDictionary(r => r[0], r => r, StringComparer.Ordinal);

        // Assert
        Assert.Equal("oewn-00000010-a", synsets["oewn-00000012-s"][2]);
        Assert.Equal(string.Empty, synsets["oewn-00000010-a"][2]);
        Assert.Equal("adj.all", synsets["oewn-00000012-s"][3]);
        Assert.Equal("bass%1:05:00::", senses["oewn-bass__1.05.00.."][1]);
        Assert.Equal("large%5:00:00:big:00", senses["oewn-large__5.00.00.big.00"][1]);
        Assert.Equal("run_up%2:38:00::", senses["oewn-run_up__2.38.00.."][1]);
        Assert.Equal("1", senses["oewn-bass__1.05.00.."][4]);
    }

    /// <summary>
    ///     Relations are stored in one direction only, symmetric ones once with the smaller id first.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_Relations_StoredOnce()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var synsetRelations = DataFixture.Rows(data, "synset_relation").Select(r => string.Join(' ', r)).ToList();
        var senseRelations = DataFixture.Rows(data, "sense_relation").Select(r => string.Join('|', r)).ToList();

        // Assert
        Assert.Contains("oewn-00000001-n hypernym oewn-00000002-n", synsetRelations);
        Assert.DoesNotContain(synsetRelations, r => r.Contains("hyponym", StringComparison.Ordinal));
        Assert.Contains("oewn-00000010-a similar oewn-00000012-s", synsetRelations);
        Assert.Contains("oewn-00000010-a also oewn-00000011-a", synsetRelations);
        Assert.Contains("oewn-00000004-n domain_topic oewn-00000043-n", synsetRelations);
        Assert.Contains("oewn-bass__1.10.00..|derivation||oewn-bassist__1.18.00..", senseRelations);
        Assert.Contains("oewn-bassist__1.18.00..|other|agent|oewn-bass__1.10.00..", senseRelations);
        Assert.Contains("oewn-big__3.00.00..|antonym||oewn-small__3.00.00..", senseRelations);
        Assert.Contains("oewn-musical__3.00.00..|pertainym||oewn-music__1.10.00..", senseRelations);
    }

    /// <summary>
    ///     Glosses, examples and pronunciations keep their order as sequence numbers.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_TextTables_KeepOrder()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var glosses = DataFixture.Rows(data, "gloss").Where(r => r[0] == "oewn-00000003-n").ToList();
        var pronunciations = DataFixture.Rows(data, "pronunciation").Where(r => r[0] == "oewn-bass-n-1").ToList();

        // Assert
        Assert.Equal(["1", "2"], glosses.Select(r => r[1]));
        Assert.Equal("a singer who has a bass voice", glosses[1][2]);
        Assert.Equal(["GB", "US"], pronunciations.Select(r => r[2]));
        Assert.Equal("beɪs", pronunciations[1][3]);
        Assert.Equal(3, DataFixture.Rows(data, "example").Count);
    }

    /// <summary>
    ///     Lemmas that do not start with an ASCII letter are written to the <c>_</c> files.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_NonAsciiLemmas_GoToUnderscoreFile()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir);
        var text = File.ReadAllText(Path.Join(data, "entry", "_.tsv"), Encoding.UTF8);

        // Assert
        Assert.Contains("Ångström", text, StringComparison.Ordinal);
        Assert.Contains("\uD835\uDC9C matrix", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(data, "sense", "_.tsv")));
        Assert.True(File.Exists(Path.Join(data, "synset", "_.tsv")));
    }

    /// <summary>
    ///     A low threshold splits the busy letters and records the plan in the schema; the result still validates.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_LowThreshold_SplitsAndUpdatesSchema()
    {
        // Arrange / Act
        var data = DataFixture.ImportSlice(_dir, "split", 3);

        // Assert
        Assert.Contains("file_split: [b]", File.ReadAllText(Path.Join(data, "schema.yaml")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(data, "entry", "ba.tsv")));
        Assert.True(File.Exists(Path.Join(data, "entry", "bi.tsv")));
        Assert.False(File.Exists(Path.Join(data, "entry", "b.tsv")));
        var report = DataValidator.Validate(data);
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     Importing twice produces byte-identical files.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_Twice_IsByteIdentical()
    {
        // Arrange
        var first = DataFixture.ImportSlice(_dir, "one");
        var second = SchemaCopy("two");

        // Act
        OewnImporter.Import(DataFixture.SlicePath, second, Path.Join(second, "schema.yaml"), null);

        // Assert
        var files = Directory.GetFiles(first, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var other = Path.Join(second, Path.GetRelativePath(first, file));
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(other));
        }

        Assert.Equal(files.Length, Directory.GetFiles(second, "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>
    ///     A gzip-compressed input is read like a plain one.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_GzipInput_Works()
    {
        // Arrange
        var gz = Path.Join(_dir.Root, "slice.xml.gz");
        using (var target = File.Create(gz))
        using (var zip = new GZipStream(target, CompressionLevel.Fastest))
        {
            zip.Write(File.ReadAllBytes(DataFixture.SlicePath));
        }

        var data = SchemaCopy("gz");

        // Act
        var result = OewnImporter.Import(gz, data, Path.Join(data, "schema.yaml"), null);

        // Assert
        Assert.Equal(14, result.Rows["entry"]);
    }

    /// <summary>
    ///     The SHA-256 pin accepts the right hash and rejects a wrong one before anything is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_HashPin_IsChecked()
    {
        // Arrange
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DataFixture.SlicePath)));
        var good = SchemaCopy("good");
        var bad = SchemaCopy("bad-hash");

        // Act
        var result = OewnImporter.Import(DataFixture.SlicePath, good, Path.Join(good, "schema.yaml"), hash.ToLowerInvariant());
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.Import(DataFixture.SlicePath, bad, Path.Join(bad, "schema.yaml")));

        // Assert
        Assert.Equal(14, result.Rows["entry"]);
        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Join(bad, "source.tsv")));
    }

    /// <summary>
    ///     Existing tables are authoritative: the import refuses to replace them unless told to.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_ExistingTables_RefusesWithoutOverwrite()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var schema = Path.Join(data, "schema.yaml");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.Import(DataFixture.SlicePath, data, schema, null));
        var result = OewnImporter.Import(DataFixture.SlicePath, data, schema, null, overwrite: true);

        // Assert
        Assert.Contains("--overwrite", ex.Message, StringComparison.Ordinal);
        Assert.Equal(14, result.Rows["entry"]);
    }

    /// <summary>
    ///     The identifier map import fails when two current senses have the same sense key, instead of letting one
    ///     overwrite the other.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportPrevious_DuplicateSenseKey_Throws()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var file = Directory.GetFiles(Path.Join(data, "sense"), "*.tsv").First(f => File.ReadAllText(f).Contains("fish%1:05:00::", StringComparison.Ordinal));
        DataFixture.Replace(data, Path.GetRelativePath(data, file), "fish%1:05:00::", "bass%1:05:00::");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.ImportPrevious(DataFixture.SlicePath, data, "test", null));

        // Assert
        Assert.Contains("bass%1:05:00::", ex.Message, StringComparison.Ordinal);
        Assert.Contains("exactly one", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The identifier map import accepts data whose sense keys are unique.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportPrevious_UniqueSenseKeys_Imports()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);

        // Act
        var result = OewnImporter.ImportPrevious(DataFixture.SlicePath, data, "test", null);

        // Assert
        Assert.Equal(0, result.SenseRows);
        Assert.True(result.Unchanged > 0);
    }

    /// <summary>
    ///     Malformed XML is reported as invalid data.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_MalformedXml_Throws()
    {
        // Arrange / Act
        var ex = Assert.Throws<InvalidDataException>(() => ImportText("<LexicalResource><Lexicon id=\"x\">"));

        // Assert
        Assert.Contains("well-formed", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A file without a Lexicon element is not an OEWN file.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_NoLexicon_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => ImportText("<LexicalResource/>"));
    }

    /// <summary>
    ///     An unknown relation name is not silently dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_UnknownRelation_Throws()
    {
        // Arrange
        var xml = File.ReadAllText(DataFixture.SlicePath).Replace("relType=\"hypernym\"", "relType=\"unheard_of\"", StringComparison.Ordinal);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => ImportText(xml));

        // Assert
        Assert.Contains("unheard_of", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A satellite synset without a head is an error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_SatelliteWithoutHead_Throws()
    {
        // Arrange - the satellite loses its similar relation
        var xml = File.ReadAllText(DataFixture.SlicePath).Replace("<SynsetRelation relType=\"similar\" target=\"oewn-00000010-a\"/>", string.Empty, StringComparison.Ordinal);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => ImportText(xml));

        // Assert
        Assert.Contains("head", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense that points to a missing synset is an error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_MissingSynset_Throws()
    {
        // Arrange
        var xml = File.ReadAllText(DataFixture.SlicePath).Replace("synset=\"oewn-00000002-n\"", "synset=\"oewn-99999999-n\"", StringComparison.Ordinal);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => ImportText(xml));

        // Assert
        Assert.Contains("missing synset", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tabs and line breaks in text are replaced by spaces and counted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_TabInText_IsCleaned()
    {
        // Arrange
        var xml = File.ReadAllText(DataFixture.SlicePath).Replace("a small restaurant", "a small\trestaurant", StringComparison.Ordinal);
        var input = _dir.Write("tab.xml", xml);
        var data = SchemaCopy("tab-data");

        // Act
        var result = OewnImporter.Import(input, data, Path.Join(data, "schema.yaml"), null);

        // Assert
        Assert.Equal(1, result.CleanedCells);
        Assert.Contains(DataFixture.Rows(data, "gloss"), r => r[2] == "a small restaurant");
    }
}
