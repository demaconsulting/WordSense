using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the inventory bootstrap (<see cref="InventorySeeder"/>). They are plain unit checks of a build-time
///     helper, not requirements.
/// </summary>
public sealed class InventorySeederTests : IDisposable
{
    private const string SensesHeader = "sense_id\toewn_sense_id\tpart_of_speech\tgloss\tsource\tgroup_id\n";
    private const string GroupsHeader = "group_id\tlemma\tpart_of_speech\tmember_sense_ids\tkind\tevidence\n";

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

    // The slice has two nouns of bass; a third one (1.05.01) lets a group have some, but not all, of them
    private string ThreeBassNounsSlice()
    {
        var data = ClosedClassSlice();
        DataFixture.Replace(
            data,
            "sense/b.tsv",
            "oewn-bass__1.10.00..\t",
            "oewn-bass__1.05.01..\tbass%1:05:01::\toewn-bass-n\toewn-00000001-n\t2\noewn-bass__1.10.00..\t");
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
    ///     The curated labels and groups are imported: a group of some senses is kept with the id <c>lemma#group:slug</c>
    ///     (also when its slug is the label of a sense, and without any suffix), a group of one sense is left out (also
    ///     each part of a group that mixes two parts of speech), and a project sense gets its authored gloss.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_Curation_ImportsLabelsAndKeepsOnlyGroupsOfSomeSenses()
    {
        // Arrange - bass has three curated nouns (a group of two and a single one); big has an adjective and an interjection in one group
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#fish-food\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\tbass#fish-food\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#sound\n"
            + "big#large\toewn-big__3.00.00..\tAdjective\tabove average in size\toewn\tbig#size\n"
            + "big#wow\t\tInterjection\tan exclamation of wonder\tproject\tbig#size\n",
            "bass#fish-food\tbass\tNoun\tbass#fish-food,bass#fish-meat\tequivalent\tgloss:near-identical\n"
            + "bass#sound\tbass\tNoun\tbass#voice\n"
            + "big#size\tbig\tAdjective/Interjection\tbig#large,big#wow\n");

        // Act
        var run = InventorySeeder.Run(data, curation);
        SenseLabelGenerator.Run(data);
        var report = DataValidator.Validate(data);
        var groups = DataFixture.Rows(data, "sense_group").Select(g => string.Join('|', g)).ToList();
        var members = DataFixture.Rows(data, "group_sense");

        // Assert
        Assert.Equal(5, run.CuratedLabels);
        Assert.Equal(1, run.CuratedGroups);
        Assert.Equal(1, run.SplitGroups);
        Assert.Equal(3, run.DroppedSingletons);
        Assert.Equal(0, run.DroppedWholePartOfSpeech);
        Assert.Equal(["bass#group:fish-food|bass|n|fish-food|curated|equivalent"], groups);
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.Equal("bass#group:fish-food", m[0]));
        Assert.Contains(DataFixture.Rows(data, "sense_label"), l => l[0] == "dema-big-i-1" && l[1] == "wow" && l[2] == "project");
        Assert.Contains(DataFixture.Rows(data, "gloss"), g => g[0] == "dema-syn-big-i-1" && g[2] == "an exclamation of wonder");
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     A curated project sense of a word and part of speech that the dictionary has (a use Open English WordNet
    ///     lacks) becomes a sense of the existing entry, with a synset and the authored gloss; a second run adds nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_ProjectSenseOfADictionaryEntry_IsAddedToTheEntry()
    {
        // Arrange - bass has two nouns in the slice; the curated project sense is a third, without a sense key
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\t\n"
            + "bass#instrument\t\tNoun\tan instrument that plays the low notes\tproject\t\n",
            string.Empty);

        // Act
        var run = InventorySeeder.Run(data, curation);
        SenseLabelGenerator.Run(data);
        var again = InventorySeeder.Run(data, curation);
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(3, run.ProjectSenses); // two closed-class entries and the dictionary entry
        Assert.Equal(0, again.ProjectSenses);
        Assert.Contains(DataFixture.Rows(data, "sense"), s => s[0] == "dema-bass-n-1" && s[1].Length == 0 && s[2] == "oewn-bass-n" && s[3] == "dema-syn-bass-n-1" && s[4] == "3");
        Assert.Contains(DataFixture.Rows(data, "synset"), s => s[0] == "dema-syn-bass-n-1" && s[1] == "n");
        Assert.Contains(DataFixture.Rows(data, "gloss"), g => g[0] == "dema-syn-bass-n-1" && g[2] == "an instrument that plays the low notes");
        Assert.Contains(DataFixture.Rows(data, "sense_label"), l => l[0] == "dema-bass-n-1" && l[1] == "instrument" && l[2] == "project");
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     A second run changes nothing, and a curated label that would change a published one is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_IsIdempotentAndNeverChangesALabel()
    {
        // Arrange
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#fish-food\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\tbass#fish-food\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#voice\n",
            "bass#fish-food\tbass\tNoun\tbass#fish-food,bass#fish-meat\trelated\tjudge: 3/20 in-group confusion\nbass#voice\tbass\tNoun\tbass#voice\n");
        InventorySeeder.Run(data, curation);
        var labels = Directory.GetFiles(Path.Join(data, "sense_label")).Select(File.ReadAllText).ToList();
        var groups = Directory.GetFiles(Path.Join(data, "sense_group")).Select(File.ReadAllText).ToList();
        var members = Directory.GetFiles(Path.Join(data, "group_sense")).Select(File.ReadAllText).ToList();

        // Act
        var again = InventorySeeder.Run(data, curation);
        var other = Curation(
            "bass#fish\toewn-bass__1.05.00..\tNoun\tx\toewn\tbass#fish\nbass#voice\toewn-bass__1.10.00..\tNoun\tx\toewn\tbass#voice\n",
            "bass#fish\tbass\tNoun\tbass#fish\nbass#voice\tbass\tNoun\tbass#voice\n");
        var ex = Assert.Throws<InvalidDataException>(() => InventorySeeder.Run(data, other));

        // Assert
        Assert.Equal(0, again.ProjectSenses);
        Assert.Equal(0, again.CuratedLabels);
        Assert.Equal(1, again.CuratedGroups);
        Assert.Equal(labels, Directory.GetFiles(Path.Join(data, "sense_label")).Select(File.ReadAllText).ToList());
        Assert.Equal(groups, Directory.GetFiles(Path.Join(data, "sense_group")).Select(File.ReadAllText).ToList());
        Assert.Equal(members, Directory.GetFiles(Path.Join(data, "group_sense")).Select(File.ReadAllText).ToList());
        Assert.Contains("published id", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A generated label was never published: the curation replaces it (and takes the sense over with the origin
    ///     <c>curated</c>), a second run changes nothing and the data stays valid.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_GeneratedLabel_IsReplacedByTheCuratedLabel()
    {
        // Arrange - the labels of the three bass nouns are generated first
        var data = ThreeBassNounsSlice();
        InventorySeeder.Run(data, null);
        SenseLabelGenerator.Run(data);
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\t\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\t\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\t\n",
            string.Empty);

        // Act
        var run = InventorySeeder.Run(data, curation);
        var labels = DataFixture.Rows(data, "sense_label").Select(l => string.Join('|', l)).ToList();
        var again = InventorySeeder.Run(data, curation);
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(3, run.CuratedLabels);
        Assert.Contains("oewn-bass__1.05.00..|fish-food|curated", labels);
        Assert.Contains("oewn-bass__1.05.01..|fish-meat|curated", labels);
        Assert.Contains("oewn-bass__1.10.00..|voice|curated", labels);
        Assert.Equal(0, again.CuratedLabels);
        Assert.Equal(labels, DataFixture.Rows(data, "sense_label").Select(l => string.Join('|', l)).ToList());
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     A curated sense whose part of speech is not the one of its sense in the data is refused and nothing is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_CuratedPartOfSpeechOfOtherSense_IsRefused()
    {
        // Arrange - the first noun of bass is curated as an adjective
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tAdjective\tthe flesh of a fish\toewn\t\n",
            string.Empty);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => InventorySeeder.Run(data, curation));

        // Assert
        Assert.Contains("part of speech 'Adjective'", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_label")));
    }

    /// <summary>
    ///     A group that would be written needs a kind of the <c>group_kind</c> vocabulary, non-empty evidence and a slug;
    ///     otherwise the run is refused and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("", "gloss:near-identical", "kind")]
    [InlineData("similar", "gloss:near-identical", "kind")]
    [InlineData("related", "", "no evidence")]
    [InlineData("related", "   ", "no evidence")]
    public void WordSenseBuilder_InventorySeeder_Run_GroupWithoutKindOrEvidence_IsRefused(string kind, string evidence, string expected)
    {
        // Arrange
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#fish-food\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\tbass#fish-food\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#voice\n",
            $"bass#fish-food\tbass\tNoun\tbass#fish-food,bass#fish-meat\t{kind}\t{evidence}\n");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => InventorySeeder.Run(data, curation));

        // Assert
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_group")));
    }

    /// <summary>
    ///     A group slug that is not made of lower-case ASCII letters, digits and hyphens, such as one that carries the
    ///     marker itself, is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_GroupSlugWithMarker_IsRefused()
    {
        // Arrange
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#group:fish\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\tbass#group:fish\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#voice\n",
            "bass#group:fish\tbass\tNoun\tbass#fish-food,bass#fish-meat\trelated\tgloss:near-identical\n");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => InventorySeeder.Run(data, curation));

        // Assert
        Assert.Contains("slug", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group that has every sense of its lemma and part of speech is left out, because the part of speech is the
    ///     next level.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_GroupOfAllSensesOfPartOfSpeech_IsLeftOut()
    {
        // Arrange
        var data = ThreeBassNounsSlice();
        var curation = Curation(
            "bass#fish-food\toewn-bass__1.05.00..\tNoun\tthe flesh of a fish\toewn\tbass#all\n"
            + "bass#fish-meat\toewn-bass__1.05.01..\tNoun\tthe meat of a fish\toewn\tbass#all\n"
            + "bass#voice\toewn-bass__1.10.00..\tNoun\tthe lowest voice\toewn\tbass#all\n",
            "bass#all\tbass\tNoun\tbass#fish-food,bass#fish-meat,bass#voice\n");

        // Act
        var run = InventorySeeder.Run(data, curation);
        SenseLabelGenerator.Run(data);
        var report = DataValidator.Validate(data);

        // Assert
        Assert.Equal(3, run.CuratedLabels);
        Assert.Equal(0, run.CuratedGroups);
        Assert.Equal(1, run.DroppedWholePartOfSpeech);
        Assert.Equal(0, run.DroppedSingletons);
        Assert.False(Directory.Exists(Path.Join(data, "sense_group")));
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
    }

    /// <summary>
    ///     A group row of any origin other than curated, such as a retired default group, is removed with its members.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventorySeeder_Run_RetiredDefaultGroups_AreRemoved()
    {
        // Arrange
        var data = ClosedClassSlice();
        DataFixture.Write(data, "sense_group/b.tsv", "group_id\tlemma\tpos\tlabel\torigin\nbass#noun-group\tbass\tn\tnoun-group\tdefault\n");
        DataFixture.Write(data, "group_sense/b.tsv", "group_id\tsense_id\nbass#noun-group\toewn-bass__1.05.00..\nbass#noun-group\toewn-bass__1.10.00..\n");

        // Act
        var run = InventorySeeder.Run(data, null);

        // Assert
        Assert.Equal(1, run.RemovedStale);
        Assert.False(Directory.Exists(Path.Join(data, "sense_group")));
        Assert.False(Directory.Exists(Path.Join(data, "group_sense")));
    }
}
