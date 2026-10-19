using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the inventory bootstrap (<see cref="InventorySeeder"/>) and of the temporary working file of the corpus
///     relabeling (<see cref="LegacyClassMap"/>). They are plain unit checks of build-time helpers, not requirements.
/// </summary>
public sealed class InventorySeederTests : IDisposable
{
    private const string SensesHeader = "sense_id\toewn_sense_id\tpart_of_speech\tgloss\tsource\tgroup_id\n";
    private const string GroupsHeader = "group_id\tlemma\tpart_of_speech\tmember_sense_ids\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private string ClosedClassSlice()
    {
        var data = DataFixture.ImportSlice(_dir);
        DataFixture.Replace(data, "source.tsv", "version\n", "version\ndema\tDEMA Consulting\tMIT\thttps://example.org\t1\n");
        DataFixture.Replace(data, "entry/b.tsv", "source_id\n", "source_id\ndema-big-i\tbig\ti\t0\t1\t1\tdema\n");
        DataFixture.Write(data, "entry/t.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id\ndema-the-d\tthe\td\t0\t1\t1\tdema\n");
        return data;
    }

    private string Curation(string senses, string groups)
    {
        _dir.Write("curation/senses.tsv", SensesHeader + senses);
        _dir.Write("curation/groups.tsv", GroupsHeader + groups);
        return Path.Join(_dir.Root, "curation");
    }

    /// <summary>
    ///     Every closed-class entry without a sense gets a project sense with a synset and a gloss, and the data validates
    ///     once the labels are written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_ClosedClassEntry_GetsAProjectSense()
    {
        // Arrange
        var data = ClosedClassSlice();

        // Act
        var run = InventorySeeder.Run(data, null);
        SenseLabelGenerator.Run(data);
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(2, run.ProjectSenses);
        Assert.Contains(DataFixture.Rows(data, "sense"), s => s[0] == "dema-the-d-1" && s[1].Length == 0 && s[2] == "dema-the-d");
        Assert.Contains(DataFixture.Rows(data, "synset"), s => s[0] == "dema-syn-the-d-1" && s[1] == "d");
        Assert.Contains(DataFixture.Rows(data, "gloss"), g => g[0] == "dema-syn-the-d-1" && g[2].Contains("determiner \"the\"", StringComparison.Ordinal));
        Assert.Contains(DataFixture.Rows(data, "sense_label"), l => l[0] == "dema-the-d-1" && l[1] == "determiner" && l[2] == "project");
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     The curated labels and groups are imported: a one-sense group whose id is a sense id is renamed, a group that
    ///     mixes two parts of speech is split, and a project sense gets its authored gloss.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_Curation_ImportsLabelsAndSplitsAndRenamesGroups()
    {
        // Arrange - bass has two curated nouns in two groups; big has an adjective and an interjection in one group
        var data = ClosedClassSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#fish-food\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#sound\n"
            + "big#large\toewn-big__3.00.00..\tAdjective\tabove average in size\toewn\tbig#size\n"
            + "big#wow\t\tInterjection\tan exclamation of wonder\tproject\tbig#size\n",
            "bass#fish-food\tbass\tNoun\tbass#fish-food\n"
            + "bass#sound\tbass\tNoun\tbass#voice\n"
            + "big#size\tbig\tAdjective/Interjection\tbig#large,big#wow\n");

        // Act
        var run = InventorySeeder.Run(data, curation);
        SenseLabelGenerator.Run(data);
        var report = DataValidator.Validate(data);
        var groups = DataFixture.Rows(data, "sense_group").Select(g => g[0] + "|" + g[2] + "|" + g[4]).ToList();

        // Assert
        Assert.Equal(4, run.CuratedLabels);
        Assert.Equal(4, run.CuratedGroups);
        Assert.Equal(1, run.SplitGroups);
        Assert.Equal(1, run.RenamedGroups);
        Assert.Contains("bass#fish-food-group|n|curated", groups);
        Assert.Contains("bass#sound|n|curated", groups);
        Assert.Contains("big#size|a|curated", groups);
        Assert.Contains("big#size-interjection|i|curated", groups);
        Assert.Contains(DataFixture.Rows(data, "sense_label"), l => l[0] == "dema-big-i-1" && l[1] == "wow" && l[2] == "project");
        Assert.Contains(DataFixture.Rows(data, "gloss"), g => g[0] == "dema-syn-big-i-1" && g[2] == "an exclamation of wonder");
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     A second run changes nothing, and a curated label that would change a published one is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_IsIdempotentAndNeverChangesALabel()
    {
        // Arrange
        var data = ClosedClassSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#fish-food\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#voice\n",
            "bass#fish-food\tbass\tNoun\tbass#fish-food\nbass#voice\tbass\tNoun\tbass#voice\n");
        InventorySeeder.Run(data, curation);
        var before = Directory.GetFiles(Path.Join(data, "sense_label")).Select(File.ReadAllText).ToList();

        // Act
        var again = InventorySeeder.Run(data, curation);
        var other = Curation(
            "bass#fish\toewn-bass__1.05.00..\tNoun\tx\toewn\tbass#fish\nbass#voice\toewn-bass__1.10.00..\tNoun\tx\toewn\tbass#voice\n",
            "bass#fish\tbass\tNoun\tbass#fish\nbass#voice\tbass\tNoun\tbass#voice\n");
        var ex = Assert.Throws<InvalidDataException>(() => InventorySeeder.Run(data, other));

        // Assert
        Assert.Equal(0, again.ProjectSenses);
        Assert.Equal(0, again.CuratedLabels);
        Assert.Equal(before, Directory.GetFiles(Path.Join(data, "sense_label")).Select(File.ReadAllText).ToList());
        Assert.Contains("published id", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The working file maps every class to its new senses and groups, and a class that stands for an entry to the
    ///     entry; a class whose sense has no label is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LegacyClassMap_Write_MapsClassesToNewSensesAndGroups()
    {
        // Arrange - one class with both nouns of bass, one lemma-split class that stands for the entry of big
        var data = DataFixture.ImportSlice(_dir);
        DataFixture.Write(data, "coarse_class/b.tsv", "class_id\tlemma\tlabel\tpos\tkind\nbass#fish\tbass\tfish\tn\tsense_group\nbigger#f\tbigger\tform\ta\tlemma_split\n");
        DataFixture.Write(data, "class_sense/b.tsv", "class_id\tsense_id\nbass#fish\toewn-bass__1.05.00..\nbass#fish\toewn-bass__1.10.00..\n");
        DataFixture.Write(data, "class_entry/b.tsv", "class_id\tentry_id\tfeatures\nbigger#f\toewn-big-a\tDegree=Cmp\n");
        _dir.Write("corpus/senses/x.tsv", TestDirectory.SensesHeader + "bass\tfish\tnoun\tfish\tbas\nbigger\tform\tadjective\t\t\n");
        var output = Path.Join(_dir.Root, "corpus", "migration", "legacy-class-map.tsv");
        var corpus = Path.Join(_dir.Root, "corpus");

        // Act
        var refused = Assert.Throws<InvalidDataException>(() => LegacyClassMap.Write(data, corpus, output));
        SenseLabelGenerator.Run(data);
        var (classes, rows) = LegacyClassMap.Write(data, corpus, output);
        var lines = File.ReadAllLines(output, Encoding.UTF8);

        // Assert
        Assert.Contains("run label-senses", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, classes);
        Assert.Equal(3, rows);
        Assert.Equal("class_id\tkind\tlemma\tpos\tmeaning\trespelling\tsense_count\tsense_id\tgroup_id\tentry_id\tentry_lemma\tentry_pos\tfeatures", lines[0]);
        Assert.Contains("bass#fish\tsense_group\tbass\tn\tfish\tbas\t2\tbass#fish\tbass#noun-group\t\t\t\t", lines);
        Assert.Contains("bass#fish\tsense_group\tbass\tn\tfish\tbas\t2\tbass#noun\tbass#noun-group\t\t\t\t", lines);
        Assert.Contains("bigger#f\tlemma_split\tbigger\ta\t\t\t0\t\t\toewn-big-a\tbig\ta\tDegree=Cmp", lines);
    }
}
