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
    ///     A rejected input leaves the existing tables byte-identical, also with overwrite: the import stages its output
    ///     and replaces the tables only when the whole input was accepted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_Import_OverwriteWithInvalidInput_KeepsExistingTables()
    {
        // Arrange - imported data, and an input whose sense points to a missing synset
        var data = DataFixture.ImportSlice(_dir);
        var before = Snapshot(data);
        var xml = File.ReadAllText(DataFixture.SlicePath).Replace("synset=\"oewn-00000002-n\"", "synset=\"oewn-99999999-n\"", StringComparison.Ordinal);
        var input = _dir.Write("invalid.xml", xml);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.Import(input, data, Path.Join(data, "schema.yaml"), null, overwrite: true));

        // Assert
        Assert.Contains("missing synset", ex.Message, StringComparison.Ordinal);
        var after = Snapshot(data);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (file, bytes) in before)
        {
            Assert.Equal(bytes, after[file]);
        }

        Assert.DoesNotContain(Directory.GetDirectories(_dir.Root), d => d.Contains(".import-", StringComparison.Ordinal));
    }

    private static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes, StringComparer.Ordinal);

    /// <summary>
    ///     A move that fails in the middle of the replacement (a locked file) undoes every move, including the schema split
    ///     update and the id map of the sense labels: the original files are byte-identical afterwards, no table of the new
    ///     import is left behind and no staging or backup folder remains.
    /// </summary>
    /// <param name="removeLabelMap">True to start without the id map file, so that the import creates it.</param>
    /// <param name="failAt">The move that fails: a positive number counts from the first move, a negative one from the last.</param>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 8)]
    [InlineData(false, -2)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    public void WordSenseBuilder_OewnImporter_Import_FailingMoveMidReplace_RestoresEveryOriginalFile(bool removeLabelMap, int failAt)
    {
        // Arrange - count the moves of a successful import of the same shape (a split threshold of 1 changes the split
        // plan in the schema, so a schema that is not restored shows), then fail one of them
        var total = 0;
        var counted = DataFixture.ImportSlice(_dir, "counted");
        if (removeLabelMap)
        {
            File.Delete(Path.Join(counted, "sense_label_map.tsv"));
        }

        OewnImporter.Import(DataFixture.SlicePath, counted, Path.Join(counted, "schema.yaml"), null, splitThreshold: 1, overwrite: true, beforeMove: (_, _) => total++);
        var data = DataFixture.ImportSlice(_dir, "data");
        if (removeLabelMap)
        {
            File.Delete(Path.Join(data, "sense_label_map.tsv"));
        }

        var before = Snapshot(data);
        var moves = 0;
        var failing = failAt > 0 ? failAt : total + failAt + 1;

        // Act
        var ex = Assert.Throws<IOException>(() => OewnImporter.Import(
            DataFixture.SlicePath,
            data,
            Path.Join(data, "schema.yaml"),
            null,
            splitThreshold: 1,
            overwrite: true,
            beforeMove: (_, _) =>
            {
                if (++moves == failing)
                {
                    throw new IOException("locked");
                }
            }));

        // Assert
        Assert.Equal("locked", ex.Message);
        var after = Snapshot(data);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (file, bytes) in before)
        {
            Assert.Equal(bytes, after[file]);
        }

        Assert.DoesNotContain(Directory.GetDirectories(_dir.Root), d => d.Contains(".import-", StringComparison.Ordinal) || d.EndsWith(".backup", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A relation to a synset or a sense that the file does not hold is an error that names the relation, not a
    ///     dangling reference in the data.
    /// </summary>
    [Theory]
    [InlineData("<SynsetRelation relType=\"hypernym\" target=\"oewn-00000002-n\"/>", "<SynsetRelation relType=\"hypernym\" target=\"oewn-99999999-n\"/>", "missing synset")]
    [InlineData("relType=\"pertainym\" target=\"oewn-music__1.10.00..\"", "relType=\"pertainym\" target=\"oewn-nothing__1.10.00..\"", "missing sense")]
    public void WordSenseBuilder_OewnImporter_Import_RelationToMissingEndpoint_Throws(string original, string changed, string expected)
    {
        // Arrange
        var text = File.ReadAllText(DataFixture.SlicePath);
        Assert.Contains(original, text, StringComparison.Ordinal);
        var xml = text.Replace(original, changed, StringComparison.Ordinal);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => ImportText(xml));

        // Assert
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    private const string FormsXml = "<LexicalResource><Lexicon id=\"x\" label=\"l\" language=\"en\" version=\"1\">"
        + "<LexicalEntry id=\"e-color\"><Lemma writtenForm=\"color\" partOfSpeech=\"n\"/><Form writtenForm=\"colour\"/><Form writtenForm=\" col\tour \"/></LexicalEntry>"
        + "<LexicalEntry id=\"e-plain\"><Lemma writtenForm=\"plain\" partOfSpeech=\"a\"/></LexicalEntry>"
        + "</Lexicon></LexicalResource>";

    /// <summary>
    ///     The written forms are read per entry in file order and cleaned like every other text; an entry without a form
    ///     is absent.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ReadForms_ReturnsCleanedFormsPerEntry()
    {
        // Arrange
        var input = _dir.Write("forms.xml", FormsXml);

        // Act
        var forms = OewnImporter.ReadForms(input, null);

        // Assert
        Assert.Equal(["e-color"], forms.Keys);
        Assert.Equal(["colour", "col our"], forms["e-color"]);
    }

    /// <summary>
    ///     The forms of a file whose SHA-256 differs from the pin, or of malformed XML, are refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ReadForms_WrongHashOrMalformedXml_Throws()
    {
        // Arrange
        var input = _dir.Write("forms.xml", FormsXml);
        var broken = _dir.Write("broken.xml", "<LexicalResource><Lexicon id=\"x\">");

        // Act
        var hash = Assert.Throws<InvalidDataException>(() => OewnImporter.ReadForms(input));
        var xml = Assert.Throws<InvalidDataException>(() => OewnImporter.ReadForms(broken, null));

        // Assert
        Assert.Contains("SHA-256", hash.Message, StringComparison.Ordinal);
        Assert.Contains("well-formed", xml.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A changed sense id maps by its sense key, a changed entry id maps to the entry of its mapped senses, an id the
    ///     data still has gets no row and an id without a successor is counted as removed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportPrevious_ChangedIds_MapBySenseKeyAndMajority()
    {
        // Arrange - the earlier release called the fish sense and entry differently and had another sense of big
        var data = DataFixture.ImportSlice(_dir);
        var previousXml = File.ReadAllText(DataFixture.SlicePath)
            .Replace("oewn-fish__1.05.00..", "oewn-fishy__1.05.00..", StringComparison.Ordinal)
            .Replace("oewn-fish-n", "oewn-fishy-n", StringComparison.Ordinal)
            .Replace("oewn-big__3.00.00..", "oewn-gone__9.99.00..", StringComparison.Ordinal);
        var previous = _dir.Write("previous.xml", previousXml);

        // Act
        var result = OewnImporter.ImportPrevious(previous, data, "test", null);

        // Assert
        Assert.Equal(1, result.SenseRows);
        Assert.Equal(1, result.EntryRows);
        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Ambiguous);
        var rows = DataFixture.Rows(data, "id_map");
        Assert.Contains(rows, r => r.SequenceEqual(["oewn-fishy__1.05.00..", "oewn-fish__1.05.00..", "test"]));
        Assert.Contains(rows, r => r.SequenceEqual(["oewn-fishy-n", "oewn-fish-n", "test"]));
        Assert.Equal(2, rows.Count);
        Assert.True(DataValidator.Validate(data).IsValid);
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

    private const string CountsText = "bass%1:05:00:: 00000001 1 7\nbass%1:10:00:: 00000003 2 3\nunknown%1:00:00:: 00000999 1 4\n";

    /// <summary>
    ///     The count of a line arrives on the sense with that key; a line of no sense is counted and not written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportCounts_JoinsOnSenseKey()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var counts = _dir.Write("index.sense", CountsText);

        // Act
        var result = OewnImporter.ImportCounts(counts, data, null);
        var rows = DataFixture.Rows(data, "sense_frequency");

        // Assert
        Assert.Equal(2, result.Counted);
        Assert.Equal(1, result.Unmatched);
        Assert.Contains(rows, r => r.SequenceEqual(["bass%1:05:00::", "7", "wn30"]));
        Assert.Contains(rows, r => r.SequenceEqual(["bass%1:10:00::", "3", "wn30"]));
        Assert.DoesNotContain(rows, r => r[0].StartsWith("unknown", StringComparison.Ordinal));
        Assert.True(DataValidator.Validate(data).IsValid);
    }

    /// <summary>
    ///     A sense that the counts file does not list has the count 0, never a missing row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportCounts_SenseWithoutEntry_HasCountZero()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var counts = _dir.Write("index.sense", CountsText);
        var keys = DataFixture.Rows(data, "sense").Select(r => r[1]).Where(k => k.Length > 0).ToList();

        // Act
        var result = OewnImporter.ImportCounts(counts, data, null);
        var rows = DataFixture.Rows(data, "sense_frequency");

        // Assert
        Assert.Equal(keys.Count, rows.Count);
        Assert.Equal(keys.Count - 2, result.Zero);
        Assert.Contains(rows, r => r.SequenceEqual(["fish%1:05:00::", "0", "wn30"]));
        Assert.Equal(keys.Order(StringComparer.Ordinal), rows.Select(r => r[0]).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     A counts file whose SHA-256 is not the pin is rejected before anything is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OewnImporter_ImportCounts_HashPin_IsChecked()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var counts = _dir.Write("index.sense", CountsText);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(counts)));

        // Act
        var accepted = OewnImporter.ImportCounts(counts, data, hash.ToLowerInvariant());
        Directory.Delete(Path.Join(data, "sense_frequency"), true);
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.ImportCounts(counts, data));

        // Assert
        Assert.Equal(2, accepted.Counted);
        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_frequency")));
    }

    /// <summary>
    ///     A line that has not four fields, a count that is not a non-negative integer or a key that appears twice
    ///     is rejected and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("bass%1:05:00:: 00000001 1\n")]
    [InlineData("bass%1:05:00:: 00000001 1 many\n")]
    [InlineData("bass%1:05:00:: 00000001 1 -3\n")]
    [InlineData("bass%1:05:00:: 00000001 1 7\nbass%1:05:00:: 00000001 1 8\n")]
    [InlineData("bass%1:05:00:: 00000001 1 7\n\n")]
    public void WordSenseBuilder_OewnImporter_ImportCounts_MalformedLine_Throws(string text)
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        var counts = _dir.Write("index.sense", text);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OewnImporter.ImportCounts(counts, data, null));

        // Assert
        Assert.Contains("index.sense:", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_frequency")));
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
