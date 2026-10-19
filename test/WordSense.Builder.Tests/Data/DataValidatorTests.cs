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
    public void WordSenseBuilder_DataValidator_Validate_UncoveredClassSense_ViolatesPartition()
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
    ///     Features of the table that links a class to an entry are checked against the feature vocabulary.
    /// </summary>
    [Theory]
    [InlineData("Tense=Past+VerbForm=Part", true)]
    [InlineData("Tense=Fut", false)]
    [InlineData("VerbForm=Part+Tense=Past", false)]
    [InlineData("Tense", false)]
    public void WordSenseBuilder_DataValidator_Validate_SenseTableFeatures_AreChecked(string features, bool valid)
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
        var report = DataValidator.Validate(Path.Join(_dir.Root, "nothing"));

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
        File.Delete(Path.Join(data, "source.tsv"));

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
        var path = Path.Join(data, "entry", "f.tsv");
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
        DataFixture.Replace(data, Path.Join("entry", file), original, changed);

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
        var path = Path.Join(data, "entry", "b.tsv");
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

    // ---- the sense inventory: labels, groups and the published ids ----

    private const string Bass1Group = "bass#noun-group\toewn-bass__1.05.00..\n";
    private const string Bass2Group = "bass#noun-group\toewn-bass__1.10.00..\n";
    private const string DefaultBassGroup = "bass#noun-group\tbass\tn\tnoun-group\tdefault\n";

    private string Labeled(string name = "data")
    {
        var data = DataFixture.ImportSlice(_dir, name);
        SenseLabelGenerator.Run(data);
        return data;
    }

    // Replaces the default group of the nouns of bass by curated groups
    private static void CurateBass(string data, bool coverBoth = true)
    {
        DataFixture.Replace(data, "sense_group/b.tsv", DefaultBassGroup, "bass#high\tbass\tn\thigh\tcurated\nbass#low\tbass\tn\tlow\tcurated\n");
        DataFixture.Replace(
            data,
            "group_sense/b.tsv",
            Bass1Group + Bass2Group,
            "bass#high\toewn-bass__1.05.00..\n" + (coverBoth ? "bass#low\toewn-bass__1.10.00..\n" : string.Empty));
    }

    /// <summary>
    ///     Labels and default groups made by the generator are valid, and so are curated groups that partition the lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_CompleteGroups_IsValid()
    {
        // Arrange
        var data = Labeled();

        // Act
        var generated = DataValidator.Validate(data);
        CurateBass(data);
        var curated = DataValidator.Validate(data);

        // Assert
        Assert.True(generated.IsValid, Messages(generated));
        Assert.True(curated.IsValid, Messages(curated));
        Assert.Equal(12, curated.RowCounts["sense_label"]);
        Assert.Equal(12, curated.RowCounts["group_sense"]);
    }

    /// <summary>
    ///     A sense of a curated lemma that is in no group breaks the partition rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_UncoveredSense_ViolatesPartition()
    {
        // Arrange
        var data = Labeled();
        CurateBass(data, coverBoth: false);

        // Act / Assert
        AssertViolation(data, "partition");
    }

    /// <summary>
    ///     A sense in two groups breaks the unique constraint.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseInTwoGroups_ViolatesUnique()
    {
        // Arrange
        var data = Labeled();
        CurateBass(data);
        DataFixture.Replace(data, "group_sense/b.tsv", "bass#high\toewn-bass__1.05.00..\n", "bass#high\toewn-bass__1.05.00..\nbass#high\toewn-bass__1.10.00..\n");

        // Act / Assert
        AssertViolation(data, "unique");
    }

    /// <summary>
    ///     A group whose part of speech is not the part of speech of its senses is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupAcrossPartsOfSpeech_Violates()
    {
        // Arrange - the group of the nouns of bass says it is a verb group
        var data = Labeled();
        DataFixture.Replace(data, "sense_group/b.tsv", DefaultBassGroup, "bass#noun-group\tbass\tv\tnoun-group\tdefault\n");

        // Act / Assert
        AssertViolation(data, "group");
    }

    /// <summary>
    ///     A group that holds two respellings is a violation, whatever the order of its senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupWithTwoRespellings_Violates()
    {
        // Arrange - one curated group of both nouns of bass, with two pronunciations
        var data = Labeled();
        DataFixture.Replace(data, "sense_group/b.tsv", DefaultBassGroup, "bass#all\tbass\tn\tall\tcurated\n");
        DataFixture.Replace(data, "group_sense/b.tsv", Bass1Group + Bass2Group, "bass#all\toewn-bass__1.05.00..\nbass#all\toewn-bass__1.10.00..\n");
        Assert.True(DataValidator.Validate(data).IsValid);
        DataFixture.Write(data, "sense_respelling/b.tsv", string.Join('\n', "sense_id\trespelling", "oewn-bass__1.05.00..\tbass", "oewn-bass__1.10.00..\tbase") + "\n");

        // Act / Assert
        AssertViolation(data, "group");
    }

    /// <summary>
    ///     A lemma that is not curated needs one default group for each part of speech and its senses are in it.
    /// </summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("other-group")]
    public void WordSenseBuilder_DataValidator_Validate_OtherLemma_NeedsOneDefaultGroupPerPartOfSpeech(string defect)
    {
        // Arrange
        var data = Labeled();
        if (defect == "missing")
        {
            DataFixture.Replace(data, "sense_group/b.tsv", "big#adjective-group\tbig\ta\tadjective-group\tdefault\n", string.Empty);
            DataFixture.Replace(data, "group_sense/b.tsv", "big#adjective-group\toewn-big__3.00.00..\n", string.Empty);
        }
        else
        {
            // The second noun of bass is moved to a second default group of the same part of speech
            DataFixture.Replace(data, "sense_group/b.tsv", DefaultBassGroup, DefaultBassGroup + "bass#noun-group-extra\tbass\tn\tnoun-group-extra\tdefault\n");
            DataFixture.Replace(data, "group_sense/b.tsv", Bass2Group, "bass#noun-group-extra\toewn-bass__1.10.00..\n");
        }

        // Act / Assert
        AssertViolation(data, defect == "missing" ? "partition" : "group");
    }

    /// <summary>
    ///     A sense of a one-token lemma without a label is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseWithoutLabel_Violates()
    {
        // Arrange
        var data = Labeled();
        DataFixture.Replace(data, "sense_label/b.tsv", "oewn-bassist__1.18.00..\tnoun\tgenerated\n", string.Empty);

        // Act / Assert
        AssertViolation(data, "label");
    }

    /// <summary>
    ///     Two senses of one lemma with the same label break the unique constraint of lemma and label.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_DuplicateLabelInLemma_ViolatesUnique()
    {
        // Arrange - both nouns of bass are now called fish
        var data = Labeled();
        DataFixture.Replace(data, "sense_label/b.tsv", "oewn-bass__1.10.00..\tnoun\tgenerated", "oewn-bass__1.10.00..\tfish\tgenerated");

        // Act / Assert
        AssertViolation(data, "unique");
    }

    /// <summary>
    ///     A malformed label is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_MalformedLabel_Violates()
    {
        // Arrange
        var data = Labeled();
        DataFixture.Replace(data, "sense_label/b.tsv", "oewn-bass__1.10.00..\tnoun\tgenerated", "oewn-bass__1.10.00..\tLow-Voice\tgenerated");

        // Act / Assert
        AssertViolation(data, "label");
    }

    /// <summary>
    ///     A sense id that is also the id of a group is a violation, because the two share one namespace.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseIdEqualToGroupId_Violates()
    {
        // Arrange - bass#fish is the id of the first noun of bass; a group gets the same id
        var data = Labeled();
        DataFixture.Replace(data, "sense_group/b.tsv", DefaultBassGroup, "bass#fish\tbass\tn\tfish\tcurated\n");
        DataFixture.Replace(data, "group_sense/b.tsv", Bass1Group + Bass2Group, "bass#fish\toewn-bass__1.05.00..\nbass#fish\toewn-bass__1.10.00..\n");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("is the id of a sense and of a group", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A label that differs from the published one is a violation unless the id map records the change.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ChangedPublishedLabel_ViolatesStability()
    {
        // Arrange - the baseline is the labeled slice; the current data renames bassist#noun
        var baseline = Labeled("baseline");
        var current = Labeled("current");
        DataFixture.Replace(current, "sense_label/b.tsv", "oewn-bassist__1.18.00..\tnoun\tgenerated", "oewn-bassist__1.18.00..\tplayer\tgenerated");

        // Act
        var unchanged = DataValidator.Validate(baseline, baseline);
        var changed = DataValidator.Validate(current, baseline);
        File.WriteAllText(Path.Join(current, "sense_label_map.tsv"), "old_id\tnew_id\trelease\nbassist#noun\tbassist#player\ttest\n", new UTF8Encoding(false));
        var recorded = DataValidator.Validate(current, baseline);

        // Assert
        Assert.True(unchanged.IsValid, Messages(unchanged));
        Assert.Contains("[stability]", Messages(changed), StringComparison.Ordinal);
        Assert.Contains("bassist#noun", Messages(changed), StringComparison.Ordinal);
        Assert.True(recorded.IsValid, Messages(recorded));
    }

    /// <summary>
    ///     A published id that another sense uses now is a violation even when the id map names it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_PublishedIdTakenByAnotherSense_ViolatesStability()
    {
        // Arrange - in the current data the two nouns of bass swapped their labels
        var baseline = Labeled("baseline");
        var current = Labeled("current");
        DataFixture.Replace(current, "sense_label/b.tsv", "oewn-bass__1.05.00..\tfish\tgenerated\noewn-bass__1.10.00..\tnoun\tgenerated", "oewn-bass__1.05.00..\tnoun\tgenerated\noewn-bass__1.10.00..\tfish\tgenerated");
        File.WriteAllText(Path.Join(current, "sense_label_map.tsv"), "old_id\tnew_id\trelease\nbass#fish\tbass#noun\ttest\n", new UTF8Encoding(false));

        // Act
        var report = DataValidator.Validate(current, baseline);

        // Assert
        Assert.Contains("never reused", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A baseline that does not exist is reported as a stability violation, not thrown.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_MissingBaseline_ReportsStability()
    {
        // Arrange
        var data = Labeled();

        // Act
        var report = DataValidator.Validate(data, Path.Join(_dir.Root, "nothing"));

        // Assert
        Assert.Contains("[stability]", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     An id that the map retires must not be the id of a current sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ReusedRetiredId_Violates()
    {
        // Arrange - bass#fish is retired but the first noun of bass has it
        var data = Labeled();
        File.WriteAllText(Path.Join(data, "sense_label_map.tsv"), "old_id\tnew_id\trelease\nbass#fish\t\ttest\n", new UTF8Encoding(false));

        // Act / Assert
        AssertViolation(data, "stability");
    }

    /// <summary>
    ///     A project sense that has a sense key is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ProjectSenseWithKey_Violates()
    {
        // Arrange - the first noun of bass has a sense key but is labeled as a project sense
        var data = Labeled();
        DataFixture.Replace(data, "sense_label/b.tsv", "oewn-bass__1.05.00..\tfish\tgenerated", "oewn-bass__1.05.00..\tfish\tproject");

        // Act / Assert
        AssertViolation(data, "sense");
    }

    /// <summary>
    ///     A project sense needs an authored gloss, and a sense without a key must be a project sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ProjectSenseWithoutGloss_Violates()
    {
        // Arrange - a keyless sense of a new entry with a synset that has no gloss row
        var data = Labeled();
        DataFixture.Write(data, "entry/t.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id\ndema-the-d\tthe\td\t0\t1\t1\toewn\n");
        DataFixture.Write(data, "synset/t.tsv", "synset_id\tpos\thead_synset_id\tlexfile\ndema-syn-the-d-1\td\t\t\n");
        DataFixture.Write(data, "sense/t.tsv", "sense_id\tsense_key\tentry_id\tsynset_id\tposition\ndema-the-d-1\t\tdema-the-d\tdema-syn-the-d-1\t1\n");
        DataFixture.Write(data, "sense_label/t.tsv", "sense_id\tlabel\torigin\ndema-the-d-1\tdeterminer\tproject\n");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("needs an authored gloss", Messages(report), StringComparison.Ordinal);
    }
}
