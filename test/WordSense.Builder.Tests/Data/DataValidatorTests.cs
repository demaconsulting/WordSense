using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="DataValidator"/>: the imported slice is valid, and every rule has a bad case.
/// </summary>
public sealed class DataValidatorTests : IDisposable
{
    private const string BassSense1 = "oewn-bass__1.05.00..";

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
        Assert.False(report.RowCounts.ContainsKey("coarse_class"));
    }

    /// <summary>
    ///     Features of an analysis of the form table are checked against the feature vocabulary.
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
        DataFixture.Write(data, "form/b.tsv", $"spelling\trank\tentry_id\tfeatures\tsource_id\treviewed\tlemma\tpos\tprovenance\nbig\t0\toewn-big-a\t{features}\toewn\t0\t\t\toewn_form\n");

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

    private string Labeled(string name = "data")
    {
        var data = DataFixture.ImportSlice(_dir, name);
        SenseLabelGenerator.Run(data);
        return data;
    }

    // The slice has two nouns of bass; a third one (bass 1.05.01, in the synset of the first one) makes a group of two of the three possible
    private string LabeledWithThreeBassNouns(string name = "data")
    {
        var data = DataFixture.ImportSlice(_dir, name);
        DataFixture.Replace(
            data,
            "sense/b.tsv",
            "oewn-bass__1.10.00..\t",
            "oewn-bass__1.05.01..\tbass%1:05:01::\toewn-bass-n\toewn-00000001-n\t2\noewn-bass__1.10.00..\t");
        SenseLabelGenerator.Run(data);
        return data;
    }

    // Writes one curated group of bass nouns with the listed members
    private static void WriteBassGroup(string data, string label, string pos, params string[] members) =>
        WriteGroup(data, "bass#group:" + label, label, pos, "related", members);

    // Writes one group of bass senses with the given id, label, part of speech and kind (the id is not checked here)
    private static void WriteGroup(string data, string groupId, string label, string pos, string kind, params string[] members)
    {
        DataFixture.Write(data, "sense_group/b.tsv", $"group_id\tlemma\tpos\tlabel\torigin\tkind\n{groupId}\tbass\t{pos}\t{label}\tcurated\t{kind}\n");
        DataFixture.Write(data, "group_sense/b.tsv", "group_id\tsense_id\n" + string.Concat(members.Select(m => $"{groupId}\t{m}\n")));
    }

    /// <summary>
    ///     Senses without any group are valid: a group is optional, and a curated group of some, but not all, senses of a
    ///     lemma and part of speech is valid.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_CuratedGroupOfSomeSenses_IsValid()
    {
        // Arrange
        var data = LabeledWithThreeBassNouns();

        // Act
        var withoutGroups = DataValidator.Validate(data);
        WriteBassGroup(data, "high", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var withGroup = DataValidator.Validate(data);

        // Assert
        Assert.True(withoutGroups.IsValid, Messages(withoutGroups));
        Assert.True(withGroup.IsValid, Messages(withGroup));
        Assert.Equal(13, withGroup.RowCounts["sense_label"]);
        Assert.Equal(2, withGroup.RowCounts["group_sense"]);
    }

    /// <summary>
    ///     A group of one sense is a violation: a sense needs no group of its own.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupOfOneSense_Violates()
    {
        // Arrange
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "high", "n", "oewn-bass__1.05.00..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("at least two senses", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group with every sense of its lemma and part of speech is a violation: the part of speech is the next level.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupOfAllSensesOfPartOfSpeech_Violates()
    {
        // Arrange
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "all", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..", "oewn-bass__1.10.00..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("every sense of its lemma and part of speech", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense in two groups breaks the unique constraint.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseInTwoGroups_ViolatesUnique()
    {
        // Arrange - two groups of bass share the sense 1.05.00
        var data = LabeledWithThreeBassNouns();
        DataFixture.Write(data, "sense_group/b.tsv", "group_id\tlemma\tpos\tlabel\torigin\tkind\nbass#group:high\tbass\tn\thigh\tcurated\trelated\nbass#group:low\tbass\tn\tlow\tcurated\trelated\n");
        DataFixture.Write(
            data,
            "group_sense/b.tsv",
            "group_id\tsense_id\nbass#group:high\toewn-bass__1.05.00..\nbass#group:high\toewn-bass__1.05.01..\nbass#group:low\toewn-bass__1.05.00..\nbass#group:low\toewn-bass__1.10.00..\n");

        // Act / Assert
        AssertViolation(data, "unique");
    }

    /// <summary>
    ///     A group whose part of speech is not the part of speech of its senses is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupAcrossPartsOfSpeech_Violates()
    {
        // Arrange - the group of two nouns of bass says it is a verb group
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "high", "v", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");

        // Act / Assert
        AssertViolation(data, "group");
    }

    /// <summary>
    ///     A group whose senses belong to two lemmas is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupAcrossLemmas_Violates()
    {
        // Arrange - a sense of fish is a member of a group of bass
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "high", "n", "oewn-bass__1.05.00..", "oewn-fish__1.05.00..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("belongs to the lemma 'fish'", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group that holds two respellings is a violation, whatever the order of its senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupWithTwoRespellings_Violates()
    {
        // Arrange - one curated group of two of the nouns of bass, with two pronunciations
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "all", "n", "oewn-bass__1.05.00..", "oewn-bass__1.10.00..");
        Assert.True(DataValidator.Validate(data).IsValid);
        DataFixture.Write(data, "sense_respelling/b.tsv", string.Join('\n', "sense_id\trespelling", "oewn-bass__1.05.00..\tbass", "oewn-bass__1.10.00..\tbase") + "\n");

        // Act / Assert
        AssertViolation(data, "group");
    }

    /// <summary>
    ///     The retired origin of the default groups is no longer a member of the group origins.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_DefaultGroupOrigin_Violates()
    {
        // Arrange
        var data = LabeledWithThreeBassNouns();
        WriteBassGroup(data, "high", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        DataFixture.Replace(data, "sense_group/b.tsv", "\tcurated\t", "\tdefault\t");

        // Act / Assert
        AssertViolation(data, "cell");
    }

    /// <summary>
    ///     The id of a group without the marker <c>group:</c> is a violation: it could be the id of a sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupIdWithoutMarker_Violates()
    {
        // Arrange
        var data = LabeledWithThreeBassNouns();
        WriteGroup(data, "bass#high", "high", "n", "related", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("must carry the marker 'group:'", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The id of a group whose lemma part is not the lemma of the group is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupIdOfAnotherLemma_Violates()
    {
        // Arrange - the group of bass is called fish#group:high
        var data = LabeledWithThreeBassNouns();
        WriteGroup(data, "fish#group:high", "high", "n", "related", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("the id of a group is its lemma", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group label that is not a slug of lower-case ASCII letters, digits and hyphens is a violation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupLabelNotASlug_Violates()
    {
        // Arrange - the label has an upper-case letter, which the slug rule rejects
        var data = LabeledWithThreeBassNouns();
        WriteGroup(data, "bass#group:High", "High", "n", "related", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("must be lower-case ASCII letters, digits and single hyphens", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group without a kind, or with a kind outside the vocabulary, is a violation; both kinds are valid.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_GroupKind_MustBeInTheVocabulary()
    {
        // Arrange / Act
        var data = LabeledWithThreeBassNouns();
        WriteGroup(data, "bass#group:high", "high", "n", "equivalent", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var equivalent = DataValidator.Validate(data);
        WriteGroup(data, "bass#group:high", "high", "n", string.Empty, "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var missing = DataValidator.Validate(data);
        WriteGroup(data, "bass#group:high", "high", "n", "similar", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var unknown = DataValidator.Validate(data);

        // Assert
        Assert.True(equivalent.IsValid, Messages(equivalent));
        Assert.Contains("[cell]", Messages(missing), StringComparison.Ordinal);
        Assert.Contains("'similar' is not in vocabulary 'group_kind'", Messages(unknown), StringComparison.Ordinal);
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
    ///     A sense id that is also the id of a group is a violation; the marker <c>group:</c> keeps the two apart, so only
    ///     a sense label with the marker can collide.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_SenseIdEqualToGroupId_Violates()
    {
        // Arrange - the first noun of bass is labeled group:high, which makes its id the id of the group of two nouns
        var data = LabeledWithThreeBassNouns();
        var first = DataFixture.Rows(data, "sense_label").Single(l => l[0] == "oewn-bass__1.05.00..");
        DataFixture.Replace(data, "sense_label/b.tsv", $"oewn-bass__1.05.00..\t{first[1]}\t", "oewn-bass__1.05.00..\tgroup:high\t");
        WriteBassGroup(data, "high", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Contains("is the id of a sense and of a group", Messages(report), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Group ids are versioned per data release and are not frozen: a baseline with another group id, or without
    ///     the group, is no stability violation, while the same change of a sense label still is.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataValidator_Validate_ChangedGroupId_IsNotAStabilityViolation()
    {
        // Arrange - the baseline has the group high, the current data has the group low
        var baseline = LabeledWithThreeBassNouns("baseline");
        WriteBassGroup(baseline, "high", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var current = LabeledWithThreeBassNouns("current");
        WriteBassGroup(current, "low", "n", "oewn-bass__1.05.00..", "oewn-bass__1.05.01..");
        var withoutGroup = LabeledWithThreeBassNouns("without-group");
        var first = DataFixture.Rows(current, "sense_label").Single(l => l[0] == "oewn-bass__1.05.00..");
        DataFixture.Replace(current, "sense_label/b.tsv", $"oewn-bass__1.05.00..\t{first[1]}\t", "oewn-bass__1.05.00..\tchanged\t");

        // Act
        var senseChanged = DataValidator.Validate(current, baseline);
        DataFixture.Replace(current, "sense_label/b.tsv", "oewn-bass__1.05.00..\tchanged\t", $"oewn-bass__1.05.00..\t{first[1]}\t");
        var groupRenamed = DataValidator.Validate(current, baseline);
        var groupDropped = DataValidator.Validate(withoutGroup, baseline);

        // Assert
        Assert.Contains("[stability]", Messages(senseChanged), StringComparison.Ordinal);
        Assert.True(groupRenamed.IsValid, Messages(groupRenamed));
        Assert.True(groupDropped.IsValid, Messages(groupDropped));
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
