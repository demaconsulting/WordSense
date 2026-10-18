using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="DataValidator"/>: the imported slice is valid, and every rule has a bad case.
/// </summary>
public sealed class DataValidatorTests : IDisposable
{
    private const string BassSense1 = "oewn-bass__1.05.00..";
    private const string BassSense2 = "oewn-bass__1.10.00..";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private string Data() => DataFixture.ImportSlice(_dir);

    private static string Messages(ValidationReport report) => string.Join("\n", report.Violations);

    private static void AssertViolation(string data, string rule)
    {
        var report = DataValidator.Validate(data);
        Assert.False(report.IsValid, "The data should be invalid.");
        Assert.True(Messages(report).Contains($"[{rule}]", StringComparison.Ordinal), $"Expected [{rule}] in: " + Messages(report));
    }

    private static void AddBassClass(string data, bool coverBoth)
    {
        DataFixture.Write(data, "coarse_class/b.tsv", "class_id\tlemma\tlabel\tpos\tkind\nbass#1\tbass\tfish\tn\tsense_group\n");
        var rows = $"class_id\tsense_id\nbass#1\t{BassSense1}\n" + (coverBoth ? $"bass#1\t{BassSense2}\n" : string.Empty);
        DataFixture.Write(data, "class_sense/b.tsv", rows);
    }

    /// <summary>
    ///     The slice imported from OEWN is valid and the report counts its rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ImportedSlice_IsValid()
    {
        // Arrange
        var data = Data();

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.IsValid, Messages(report));
        Assert.Equal(14, report.RowCounts["entry"]);
        Assert.Equal(0, report.RowCounts["coarse_class"]);
    }

    /// <summary>
    ///     A class mapping that covers all senses of its lemma and part of speech is valid.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_CompletePartition_IsValid()
    {
        // Arrange
        var data = Data();
        AddBassClass(data, true);

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.IsValid, Messages(report));
    }

    /// <summary>
    ///     A sense of a mapped lemma that is in no class breaks the partition rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_UncoveredSense_ViolatesPartition()
    {
        // Arrange
        var data = Data();
        AddBassClass(data, false);

        // Act / Assert
        AssertViolation(data, "partition");
    }

    /// <summary>
    ///     A sense in two classes breaks the unique constraint.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseInTwoClasses_ViolatesUnique()
    {
        // Arrange
        var data = Data();
        DataFixture.Write(data, "coarse_class/b.tsv", "class_id\tlemma\tlabel\tpos\tkind\nbass#1\tbass\tfish\tn\tsense_group\nbass#2\tbass\tvoice\tn\tsense_group\n");
        DataFixture.Write(data, "class_sense/b.tsv", $"class_id\tsense_id\nbass#1\t{BassSense1}\nbass#1\t{BassSense2}\nbass#2\t{BassSense2}\n");

        // Act / Assert
        AssertViolation(data, "unique");
    }

    /// <summary>
    ///     Class entry features are checked against the feature vocabulary.
    /// </summary>
    [Theory]
    [InlineData("Tense=Past+VerbForm=Part", true)]
    [InlineData("Tense=Fut", false)]
    [InlineData("VerbForm=Part+Tense=Past", false)]
    [InlineData("Tense", false)]
    public void WordSenseBuilder_DataValidator_Validate_ClassEntryFeatures_AreChecked(string features, bool valid)
    {
        // Arrange
        var data = Data();
        DataFixture.Write(data, "coarse_class/b.tsv", "class_id\tlemma\tlabel\tpos\tkind\nbig#f\tbig\tform\ta\tlemma_split\n");
        DataFixture.Write(data, "class_entry/b.tsv", $"class_id\tentry_id\tfeatures\nbig#f\toewn-big-a\t{features}\n");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(valid, report.IsValid);
        if (!valid)
        {
            Assert.Contains("[cell]", Messages(report), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A missing data directory is a violation, not an exception.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_MissingDirectory_ReportsSchema()
    {
        // Arrange / Act
        var report = DataValidator.Validate(Path.Combine(_dir.Root, "nothing"));

        // Assert
        Assert.False(report.IsValid);
        Assert.Contains("[schema]", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A schema that cannot be parsed is reported with its rule name.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_BrokenSchema_ReportsSchema()
    {
        // Arrange
        var data = Data();
        DataFixture.Write(data, "schema.yaml", "schema_version: 1\n\tbad: tab\n");

        // Act / Assert
        AssertViolation(data, "schema");
    }

    /// <summary>
    ///     Files and folders the schema does not know are violations.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_UnexpectedFiles_ViolateFileSet()
    {
        // Arrange
        var data = Data();
        DataFixture.Write(data, "junk.txt", "x");
        DataFixture.Write(data, "entry/zz.tsv", "x\n");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(2, report.Violations.Count(v => v.Contains("[file-set]", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     A table file of a none-partitioned table must exist.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_MissingSourceFile_ViolatesFileSet()
    {
        // Arrange
        var data = Data();
        File.Delete(Path.Combine(data, "source.tsv"));

        // Act / Assert
        AssertViolation(data, "file-set");
    }

    /// <summary>
    ///     A partition file with only a header is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_EmptyPartitionFile_Violates()
    {
        // Arrange
        var data = Data();
        DataFixture.Write(data, "entry/z.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id\n");

        // Act / Assert
        AssertViolation(data, "empty-file");
    }

    /// <summary>
    ///     Rows in the wrong file violate the placement rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_RowInWrongFile_ViolatesPlacement()
    {
        // Arrange - music is in m.tsv; put a copy of its row into s.tsv at its sorted position
        var data = Data();
        DataFixture.Replace(data, "entry/s.tsv", "source_id\n", "source_id\noewn-music-n\tmusic\tn\t0\t1\t1\toewn\n");

        // Act / Assert
        AssertViolation(data, "placement");
    }

    /// <summary>
    ///     A single-letter file for a split letter is not a valid file.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SplitPlanMismatch_ViolatesFileSet()
    {
        // Arrange - the plan says b is split but the files are single-letter
        var data = Data();
        DataFixture.Replace(data, "schema.yaml", "file_split: []", "file_split: [b]");

        // Act / Assert
        AssertViolation(data, "file-set");
    }

    /// <summary>
    ///     Format rules: header, cell count, byte-order mark, CR, final LF, blank line and UTF-8.
    /// </summary>
    [Theory]
    [InlineData("header")]
    [InlineData("cell-count")]
    [InlineData("bom")]
    [InlineData("cr")]
    [InlineData("final-lf")]
    [InlineData("blank")]
    [InlineData("utf8")]
    public void WordSenseBuilder_DataValidator_Validate_FormatDefects_AreReported(string defect)
    {
        // Arrange
        var data = Data();
        var path = Path.Combine(data, "entry", "f.tsv");
        var text = File.ReadAllText(path, Encoding.UTF8);
        var bytes = defect switch
        {
            "header" => Encoding.UTF8.GetBytes(text.Replace("entry_id\tlemma", "entry\tlemma", StringComparison.Ordinal)),
            "cell-count" => Encoding.UTF8.GetBytes(text.Replace("\tfish\t", "fish\t", StringComparison.Ordinal)),
            "bom" => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)],
            "cr" => Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n", StringComparison.Ordinal)),
            "final-lf" => Encoding.UTF8.GetBytes(text.TrimEnd('\n')),
            "blank" => Encoding.UTF8.GetBytes(text + "\n"),
            _ => [.. Encoding.UTF8.GetBytes(text), 0xFF, 0xFE, 0x0A],
        };
        File.WriteAllBytes(path, bytes);
        var rule = defect is "header" or "cell-count" ? defect : "format";

        // Act / Assert
        AssertViolation(data, rule);
    }

    /// <summary>
    ///     Cell rules: NFC, edge spaces, enumerations and canonical integers.
    /// </summary>
    [Theory]
    [InlineData("cafe\u0301", "\tcafé\t")]
    [InlineData("big ", "\tbig\t")]
    [InlineData("q", "\tn\t")]
    [InlineData("00", "\t0\t")]
    public void WordSenseBuilder_DataValidator_Validate_CellDefects_AreReported(string replacement, string target)
    {
        // Arrange - entry files c.tsv (café) and b.tsv (big)
        var data = Data();
        var file = target == "\tcafé\t" ? "c.tsv" : "b.tsv";
        var changed = target switch
        {
            "\tcafé\t" => "\t" + replacement + "\t",
            "\tbig\t" => "\t" + replacement + "\t",
            "\tn\t" => "\tbass\t" + replacement + "\t",
            _ => "\tbig\ta\t" + replacement + "\t",
        };
        var original = target switch
        {
            "\tn\t" => "\tbass\tn\t",
            "\t0\t" => "\tbig\ta\t0\t",
            _ => target,
        };

        // Act
        DataFixture.Replace(data, Path.Combine("entry", file), original, changed);

        // Assert
        AssertViolation(data, "cell");
    }

    /// <summary>
    ///     An empty cell in a column that is not nullable is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_EmptyRequiredCell_Violates()
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, "gloss/f.tsv", "any of various mostly cold-blooded aquatic vertebrates", string.Empty);

        // Act / Assert
        AssertViolation(data, "cell");
    }

    /// <summary>
    ///     Rows that are not in strictly ascending key order violate the sort rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_UnsortedRows_ViolateSort()
    {
        // Arrange - swap the two bass entries
        var data = Data();
        var path = Path.Combine(data, "entry", "b.tsv");
        var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
        (lines[1], lines[2]) = (lines[2], lines[1]);
        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));

        // Act / Assert
        AssertViolation(data, "sort");
    }

    /// <summary>
    ///     A repeated lemma, part of speech and homograph number violates the unique constraint.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_DuplicateHomograph_ViolatesUnique()
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, "entry/b.tsv", "bass\tn\t2\t", "bass\tn\t1\t");

        // Act / Assert
        AssertViolation(data, "unique");
    }

    /// <summary>
    ///     A reference to a missing row violates the foreign key rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_DanglingReference_ViolatesForeignKey()
    {
        // Arrange - the gloss of a missing synset (the file is also the file of the lemma bass)
        var data = Data();
        DataFixture.Replace(data, "synset_relation/b.tsv", "oewn-00000001-n\thypernym\toewn-00000002-n", "oewn-00000001-n\thypernym\toewn-00000999-n");

        // Act / Assert
        AssertViolation(data, "foreign-key");
    }

    /// <summary>
    ///     Relation conventions: derived direction, symmetric order and the subtype rule.
    /// </summary>
    [Theory]
    [InlineData("synset_relation/b.tsv", "oewn-00000001-n\thypernym\toewn-00000002-n", "oewn-00000001-n\thyponym\toewn-00000002-n")]
    [InlineData("synset_relation/b.tsv", "oewn-00000010-a\tsimilar\toewn-00000012-s", "oewn-00000012-s\tsimilar\toewn-00000010-a")]
    [InlineData("synset_relation/b.tsv", "oewn-00000001-n\thypernym\toewn-00000002-n", "oewn-00000001-n\thypernym\toewn-00000001-n")]
    [InlineData("sense_relation/b.tsv", "other\tagent\t", "other\t\t")]
    [InlineData("sense_relation/b.tsv", "antonym\t\t", "antonym\tagent\t")]
    public void WordSenseBuilder_DataValidator_Validate_RelationDefects_AreReported(string file, string original, string changed)
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, file, original, changed);

        // Act / Assert
        AssertViolation(data, "relation");
    }

    /// <summary>
    ///     A satellite synset without a head, or a head on another synset, is a violation.
    /// </summary>
    [Theory]
    [InlineData("oewn-00000012-s\ts\toewn-00000010-a\t", "oewn-00000012-s\ts\t\t")]
    [InlineData("oewn-00000010-a\ta\t\t", "oewn-00000010-a\ta\toewn-00000011-a\t")]
    public void WordSenseBuilder_DataValidator_Validate_HeadDefects_ViolateSynset(string original, string changed)
    {
        // Arrange
        var data = Data();
        var file = original.Contains("12-s", StringComparison.Ordinal) ? "synset/l.tsv" : "synset/b.tsv";
        DataFixture.Replace(data, file, original, changed);

        // Act / Assert
        AssertViolation(data, "synset");
    }

    /// <summary>
    ///     The token count and head index of an entry must fit its lemma.
    /// </summary>
    [Theory]
    [InlineData("run up\tv\t0\t2\t1", "run up\tv\t0\t3\t1")]
    [InlineData("run up\tv\t0\t2\t1", "run up\tv\t0\t2\t5")]
    public void WordSenseBuilder_DataValidator_Validate_TokenDefects_ViolateEntry(string original, string changed)
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, "entry/r.tsv", original, changed);

        // Act / Assert
        AssertViolation(data, "entry");
    }

    /// <summary>
    ///     Sense positions must be 1..n and the entry and synset parts of speech must fit.
    /// </summary>
    [Theory]
    [InlineData("sense/f.tsv", "oewn-fish-n\toewn-00000002-n\t1", "oewn-fish-n\toewn-00000002-n\t3")]
    [InlineData("entry/f.tsv", "fish\tn\t0", "fish\tv\t0")]
    public void WordSenseBuilder_DataValidator_Validate_SenseDefects_ViolateSense(string file, string original, string changed)
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, file, original, changed);

        // Act / Assert
        AssertViolation(data, "sense");
    }

    /// <summary>
    ///     An entry without any sense is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_EntryWithoutSense_Violates()
    {
        // Arrange - add an entry for a lemma that has no sense row
        var data = Data();
        DataFixture.Replace(data, "entry/f.tsv", "oewn-fish-n\tfish\tn\t0\t1\t1\toewn\n", "oewn-fish-n\tfish\tn\t0\t1\t1\toewn\noewn-fish-v\tfish\tv\t0\t1\t1\toewn\n");

        // Act / Assert
        AssertViolation(data, "sense");
    }

    /// <summary>
    ///     An example that names a sense of another synset is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ExampleOfOtherSynset_Violates()
    {
        // Arrange
        var data = Data();
        DataFixture.Replace(data, "example/b.tsv", "oewn-00000003-n\t2\t\ta deep bass", $"oewn-00000003-n\t2\t{BassSense1}\ta deep bass");

        // Act / Assert
        AssertViolation(data, "example");
    }

    /// <summary>
    ///     A synset without a gloss is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SynsetWithoutGloss_Violates()
    {
        // Arrange - delete the gloss rows of one synset by replacing its file
        var data = Data();
        DataFixture.Replace(data, "gloss/f.tsv", "oewn-00000002-n\t1\tany of various mostly cold-blooded aquatic vertebrates\n", string.Empty);
        DataFixture.Write(data, "gloss/f.tsv", "synset_id\tseq\ttext\noewn-00000001-n\t1\tx\n");

        // Act / Assert
        AssertViolation(data, "synset");
    }

    /// <summary>
    ///     Violations are capped per rule while the total is still counted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ManyViolations_AreCappedPerRule()
    {
        // Arrange - 30 rows with an empty required cell
        var data = Data();
        var rows = Enumerable.Range(0, 30).Select(i => $"oewn-bad{i:D2}-n\t\tn\t0\t1\t1\toewn");
        DataFixture.Write(data, "entry/z.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id\n" + string.Join('\n', rows) + "\n");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.TotalViolations >= 30);
        Assert.Equal(DataValidator.MaxPerRule, report.Violations.Count(v => v.Contains("[cell]", StringComparison.Ordinal)));
    }
}
