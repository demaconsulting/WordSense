using System.Text.RegularExpressions;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     End-to-end tests of the data commands: import a slice through the command line, validate it, and validate the
///     committed <c>data</c> folder of the repository.
/// </summary>
public sealed partial class DataEndToEndTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await Program.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    private string EmptyDataFolder()
    {
        var data = Path.Join(_dir.Root, "data");
        Directory.CreateDirectory(data);
        File.Copy(Path.Join(DataFixture.RepositoryRoot(), "data", "schema.yaml"), Path.Join(data, "schema.yaml"));
        return data;
    }

    /// <summary>
    ///     The two commands import the slice and validate the result.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_ImportThenValidate_Succeeds()
    {
        // Arrange
        var data = EmptyDataFolder();

        // Act
        var import = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        var validate = await RunAsync("validate-data", "--data", data);

        // Assert
        Assert.Equal(0, import.Code);
        Assert.Contains("entry: 14 rows", import.Output, StringComparison.Ordinal);
        Assert.Equal(0, validate.Code);
        Assert.Contains("is valid", validate.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Without the pin option the pinned release hash is enforced.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_PinnedHash_RejectsOtherFile()
    {
        // Arrange
        var data = EmptyDataFolder();

        // Act
        var import = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data);

        // Assert
        Assert.Equal(1, import.Code);
        Assert.Contains("SHA-256", import.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The import refuses to overwrite existing tables unless asked.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_ExistingTables_NeedsOverwrite()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");

        // Act
        var refused = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        var allowed = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none", "--overwrite", "true");

        // Assert
        Assert.Equal(1, refused.Code);
        Assert.Equal(0, allowed.Code);
    }

    /// <summary>
    ///     The import needs an input file.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_NoInput_ReportsUsage()
    {
        // Arrange / Act
        var result = await RunAsync("import-oewn");

        // Assert
        Assert.Equal(1, result.Code);
        Assert.Contains("--input", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Invalid data makes the validator exit with 1 and list the violation.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Validate_InvalidData_ListsViolations()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        File.Delete(Path.Join(data, "source.tsv"));

        // Act
        var result = await RunAsync("validate-data", "--data", data);

        // Assert
        Assert.Equal(1, result.Code);
        Assert.Contains("[file-set]", result.Error, StringComparison.Ordinal);
        Assert.Contains("violation(s)", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The usage lists the data commands.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Usage_ListsCommands()
    {
        // Arrange / Act
        var result = await RunAsync();

        // Assert
        Assert.Contains("import-oewn", result.Error, StringComparison.Ordinal);
        Assert.Contains("validate-data", result.Error, StringComparison.Ordinal);
    }

    private static readonly string CommittedRoot = Path.Join(DataFixture.RepositoryRoot(), "data");

    // Validating the committed data reads about 70 MB of tables, so every test of it shares one report and one read
    private static readonly Lazy<ValidationReport> CommittedReport = new(() => DataValidator.Validate(CommittedRoot));

    private static readonly Lazy<CommittedInventory> Inventory = new(() => new CommittedInventory(CommittedRoot));

    /// <summary>
    ///     The committed <c>data</c> folder of the repository passes every validator rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_IsValid()
    {
        // Arrange / Act
        var report = CommittedReport.Value;

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.True(report.RowCounts["entry"] > 100_000);
    }

    /// <summary>
    ///     The class tables and the form scopes of the retired inventory are gone from the committed data.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_HasNoRetiredClassTables()
    {
        // Arrange / Act
        var report = CommittedReport.Value;

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.Equal(2, report.RowCounts["source"]);
        foreach (var table in new[] { "coarse_class", "class_sense", "class_entry", "form_sense_scope" })
        {
            Assert.False(report.RowCounts.ContainsKey(table), table);
            Assert.False(Directory.Exists(Path.Join(CommittedRoot, table)), table);
        }
    }

    /// <summary>
    ///     The committed data carries the labels of the sense inventory and its few curated groups.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_HasSenseLabelsAndGroups()
    {
        // Arrange / Act
        var report = CommittedReport.Value;
        var inventory = Inventory.Value;

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.True(report.RowCounts["sense_label"] > 129_000);
        Assert.InRange(report.RowCounts["sense_group"], 50, 1_000);
        Assert.True(report.RowCounts["group_sense"] >= 2 * report.RowCounts["sense_group"]);
        Assert.All(inventory.Groups.Values, g => Assert.Equal("curated", g[4]));
        Assert.Equal(["equivalent", "related"], inventory.Groups.Values.Select(g => g[5]).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains("curated", inventory.Labels.Values.Select(l => l[2]));
        Assert.Contains(inventory.Labels, l => inventory.PublicId(l.Key) == "go#attempt");
    }

    /// <summary>
    ///     Every sense of a one-token lemma and every project sense has one label, unique in its lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_EverySenseHasOneUniqueLabel()
    {
        // Arrange
        var inventory = Inventory.Value;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Act
        var unlabeled = inventory.Senses.Values.Where(s => inventory.InInventory(s) && !inventory.Labels.ContainsKey(s[0])).Select(s => s[0]).Take(5).ToList();
        var duplicates = inventory.Labels.Keys.Select(inventory.PublicId).Where(id => !seen.Add(id)).Take(5).ToList();
        var malformed = inventory.Labels.Values.Where(l => !LabelShape().IsMatch(l[1])).Select(l => l[0]).Take(5).ToList();

        // Assert
        Assert.Empty(unlabeled);
        Assert.Empty(duplicates);
        Assert.Empty(malformed);
    }

    /// <summary>
    ///     No release has published sense labels yet, so the data is its own baseline: nothing changes and no retired
    ///     id of the id map is the id of a current sense. Group ids are versioned per release and not compared.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_PublishedLabelsAreNeverChangedOrReused()
    {
        // Arrange
        var inventory = Inventory.Value;

        // Act
        var report = DataValidator.Validate(CommittedRoot, CommittedRoot);
        var current = inventory.Labels.Keys.Select(inventory.PublicId).ToHashSet(StringComparer.Ordinal);
        var reused = inventory.LabelMap.Select(m => m[0]).Where(current.Contains).ToList();

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.Empty(reused);
    }

    /// <summary>
    ///     Every group id is <c>lemma#group:label</c> with a slug label, so it is never the id of a sense, and every group
    ///     has a kind of the vocabulary; the curation of the group carries the same kind and non-empty evidence.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_GroupsHaveAMarkerIdAKindAndCuratedEvidence()
    {
        // Arrange
        var inventory = Inventory.Value;
        var senseIds = inventory.Labels.Keys.Select(inventory.PublicId).ToHashSet(StringComparer.Ordinal);
        var curation = File.ReadAllLines(Path.Join(DataFixture.RepositoryRoot(), "corpus", "curation", "groups.tsv"));
        var header = curation[0].Split('\t');
        var curated = curation.Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')).Select(c => (Id: c[Array.IndexOf(header, "group_id")], Kind: c[Array.IndexOf(header, "kind")], Evidence: c[Array.IndexOf(header, "evidence")])).ToList();

        // Act - a group split by part of speech has the curated slug plus a part of speech suffix
        static string SlugOf(string id) => id[(id.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var badId = inventory.Groups.Values.Where(g => g[0] != g[1] + "#group:" + g[3] || !LabelShape().IsMatch(g[3]) || senseIds.Contains(g[0])).Select(g => g[0]).Take(5).ToList();
        var badKind = inventory.Groups.Values.Where(g => g[5] is not ("equivalent" or "related")).Select(g => g[0]).Take(5).ToList();
        var noEvidence = inventory.Groups.Values
            .Where(g => !curated.Any(c => c.Id.StartsWith(g[1] + "#", StringComparison.Ordinal)
                && (SlugOf(c.Id) == g[3] || g[3].StartsWith(SlugOf(c.Id) + "-", StringComparison.Ordinal))
                && c.Kind == g[5]
                && c.Evidence.Trim().Length > 0))
            .Select(g => g[0]).Take(5).ToList();

        // Assert
        Assert.Empty(badId);
        Assert.Empty(badKind);
        Assert.Empty(noEvidence);
    }

    /// <summary>
    ///     Every group is a set of at least two, but not all, senses of one lemma and part of speech, a sense is in at
    ///     most one group.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_GroupsAreSetsOfSomeSensesOfOnePartOfSpeech()
    {
        // Arrange
        var inventory = Inventory.Value;
        var sensesOfPartOfSpeech = inventory.Senses.Values.GroupBy(s => (inventory.Lemma(s), inventory.Entries[s[2]][2])).ToDictionary(g => g.Key, g => g.Count());
        var byGroup = inventory.Members.GroupBy(m => m[0]).ToDictionary(g => g.Key, g => g.Select(m => m[1]).ToList(), StringComparer.Ordinal);

        // Act
        var problems = new List<string>();
        foreach (var group in inventory.Groups.Values)
        {
            var members = byGroup.GetValueOrDefault(group[0], []);
            var key = (group[1], group[2]);
            if (members.Count < 2)
            {
                problems.Add($"{group[0]} has fewer than two senses");
            }

            if (members.Count >= sensesOfPartOfSpeech.GetValueOrDefault(key))
            {
                problems.Add($"{group[0]} has every sense of its lemma and part of speech");
            }

            if (members.Any(m => (inventory.Lemma(inventory.Senses[m]), inventory.Entries[inventory.Senses[m][2]][2]) != key))
            {
                problems.Add($"{group[0]} has a sense of another lemma or part of speech");
            }
        }

        var inTwoGroups = inventory.Members.GroupBy(m => m[1]).Where(g => g.Count() > 1).Select(g => g.Key).Take(5).ToList();

        // Assert
        Assert.Empty(problems.Take(5));
        Assert.Empty(inTwoGroups);
    }

    /// <summary>
    ///     A group is optional: almost every sense is in no group, only the lemmas of the curation have groups, and no
    ///     group has the name of a retired default group.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_SensesNeedNoGroup()
    {
        // Arrange
        var inventory = Inventory.Value;
        var inventorySenses = inventory.Senses.Values.Where(inventory.InInventory).ToList();
        var curation = File.ReadAllLines(Path.Join(DataFixture.RepositoryRoot(), "corpus", "curation", "groups.tsv")).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')[1]).ToHashSet(StringComparer.Ordinal);

        // Act
        var withoutGroup = inventorySenses.Count(s => !inventory.GroupOf.ContainsKey(s[0]));
        var oldNames = inventory.Groups.Keys.Where(id => id.EndsWith("-group", StringComparison.Ordinal)).Take(5).ToList();
        var foreign = inventory.Groups.Values.Where(g => !curation.Contains(g[1])).Select(g => g[0]).Take(5).ToList();

        // Assert
        Assert.True(withoutGroup > inventorySenses.Count - 500, "the groups cover only a few senses");
        Assert.Empty(oldNames);
        Assert.Empty(foreign);
    }

    /// <summary>
    ///     Every sense that has a sense key has a count of the WordNet 3.0 source, 0 where the counts file has none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_EveryKeyedSenseHasAFrequency()
    {
        // Arrange
        var inventory = Inventory.Value;

        // Act
        var keyed = inventory.Senses.Values.Where(s => s[1].Length > 0).Select(s => s[1]).ToList();
        var missing = keyed.Where(k => !inventory.Frequency.ContainsKey(k)).Take(5).ToList();

        // Assert
        Assert.Empty(missing);
        Assert.Equal(keyed.Count, inventory.Frequency.Count);
        Assert.All(inventory.Frequency.Values, f => Assert.Equal("wn30", f[2]));
        Assert.True(inventory.Frequency.Values.Count(f => f[1] != "0") > 30_000, "most counted senses keep their count");
    }

    /// <summary>
    ///     A project sense has no sense key and an authored gloss; the four discourse and function senses are there.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_ProjectSensesHaveNoKeyAndAGloss()
    {
        // Arrange
        var inventory = Inventory.Value;

        // Act
        var project = inventory.Labels.Values.Where(l => l[2] == "project").Select(l => inventory.Senses[l[0]]).ToList();
        var keyless = inventory.Senses.Values.Where(s => s[1].Length == 0).Select(s => s[0]).ToHashSet(StringComparer.Ordinal);
        var withoutGloss = project.Where(s => !inventory.Glosses.Contains(s[3])).Select(s => s[0]).Take(5).ToList();
        var ids = project.Select(s => inventory.PublicId(s[0])).ToHashSet(StringComparer.Ordinal);

        // Assert
        Assert.All(project, s => Assert.Equal(string.Empty, s[1]));
        Assert.Equal(keyless.Count, project.Count);
        Assert.Empty(withoutGloss);
        Assert.True(project.Count >= 939);
        Assert.Subset(ids, new HashSet<string>(["right#discourse-marker", "well#discourse-marker", "help#cry-for-aid", "however#conjunction", "the#determiner"]));
    }

    /// <summary>
    ///     Every determiner, pronoun, preposition, conjunction, interjection and auxiliary analysis of the word table has
    ///     a project sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_ClosedClassAnalysesHaveAProjectSense()
    {
        // Arrange
        var inventory = Inventory.Value;
        var projectEntries = inventory.Senses.Values.Where(s => s[1].Length == 0 && inventory.Labels.TryGetValue(s[0], out var l) && l[2] == "project").Select(s => s[2]).ToHashSet(StringComparer.Ordinal);

        // Act
        var closed = inventory.Entries.Values.Where(e => "upcidx".Contains(e[2], StringComparison.Ordinal)).ToList();
        var analyses = inventory.FormEntries.Where(e => "upcidx".Contains(inventory.Entries[e][2], StringComparison.Ordinal)).ToList();
        var without = closed.Where(e => !projectEntries.Contains(e[0])).Select(e => e[0]).Take(5).ToList();

        // Assert
        Assert.True(closed.Count >= 800);
        Assert.NotEmpty(analyses);
        Assert.Empty(without);
        Assert.All(analyses, e => Assert.Contains(e, projectEntries));
    }

    /// <summary>
    ///     The conjunction <c>or</c> and every auxiliary verb of the closed list have an analysis with a project sense
    ///     (<c>or#conjunction</c>, <c>can#auxiliary</c>).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_OrAndTheAuxiliariesHaveClosedClassAnalyses()
    {
        // Arrange
        var inventory = Inventory.Value;
        var listed = File.ReadAllLines(Path.Join(DataFixture.RepositoryRoot(), "corpus", "lexicon", "auxiliaries.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')[0]).ToList();
        var senseOf = inventory.Senses.Values.Where(s => s[1].Length == 0).ToDictionary(s => s[2], s => inventory.PublicId(s[0]), StringComparer.Ordinal);

        // Act
        var noEntry = listed.Where(w => !senseOf.GetValueOrDefault($"dema-{w}-x", string.Empty).Equals($"{w}#auxiliary", StringComparison.Ordinal)).ToList();
        var noAnalysis = listed.Where(w => !inventory.FormsOf.GetValueOrDefault(w, []).Any(e => inventory.Entries[e][2] == "x")).ToList();
        var orAnalyses = inventory.FormsOf.GetValueOrDefault("or", []);

        // Assert
        Assert.NotEmpty(listed);
        Assert.Empty(noEntry);
        Assert.Empty(noAnalysis);
        Assert.Equal("dema-or-c", orAnalyses[0]);
        Assert.Equal("or#conjunction", senseOf["dema-or-c"]);
    }

    /// <summary>
    ///     The count import command writes the table from a counts file.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_ImportWn30Counts_WritesTheTable()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        var counts = _dir.Write("index.sense", "bass%1:05:00:: 00000001 1 7\n");

        // Act
        var result = await RunAsync("import-wn30-counts", "--input", counts, "--data", data, "--sha256", "none");
        var pinned = await RunAsync("import-wn30-counts", "--input", counts, "--data", data);

        // Assert
        Assert.Equal(0, result.Code);
        Assert.Contains("sense_frequency: 14 rows, 1 with a count", result.Output, StringComparison.Ordinal);
        Assert.Contains(DataFixture.Rows(data, "sense_frequency"), r => r.SequenceEqual(["bass%1:05:00::", "7", "wn30"]));
        Assert.Equal(1, pinned.Code);
        Assert.Contains("SHA-256", pinned.Error, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("validate-data", "--data", data)).Code);
    }

    /// <summary>
    ///     The label command labels the senses and the validator accepts the result; the baseline option is read.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_LabelSenses_WritesLabelsAndNoGroups()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");

        // Act
        var label = await RunAsync("label-senses", "--data", data);
        var validate = await RunAsync("validate-data", "--data", data, "--baseline", data);

        // Assert
        Assert.Equal(0, label.Code);
        Assert.Contains("12 generated", label.Output, StringComparison.Ordinal);
        Assert.Equal(0, validate.Code);
        Assert.Contains("sense_label 12", validate.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_group")));
    }

    /// <summary>
    ///     The README files and the provenance record credit WordNet 3.0 as the source of the counts and name SemCor as
    ///     an evaluation source only.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_Attribution_NamesWordNetCountsAndSemCorUse()
    {
        // Arrange
        var root = DataFixture.RepositoryRoot();
        var files = new[]
        {
            Path.Join(root, "README.md"),
            Path.Join(root, "data", "README.md"),
            Path.Join(root, "data", "PROVENANCE.md")
        };

        // Act / Assert
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.Contains("WordNet 3.0", text, StringComparison.Ordinal);
            Assert.Contains("Princeton University", text, StringComparison.Ordinal);
            Assert.Contains("SemCor", text, StringComparison.Ordinal);
            Assert.Contains("evaluation", text, StringComparison.Ordinal);
        }

        Assert.Contains("THIS SOFTWARE AND DATABASE IS PROVIDED \"AS IS\"", File.ReadAllText(files[2]), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelShape();

    /// <summary>
    ///     The inventory tables of the committed data, read once for the tests that check properties of the whole data.
    /// </summary>
    private sealed class CommittedInventory
    {
        public CommittedInventory(string root)
        {
            var schema = DataSchema.Load(Path.Join(root, "schema.yaml"));
            List<string[]> Read(string table) => DataTables.Read(root, schema.Table(table)).ToList();
            Entries = Read("entry").ToDictionary(r => r[0], StringComparer.Ordinal);
            Senses = Read("sense").ToDictionary(r => r[0], StringComparer.Ordinal);
            Labels = Read("sense_label").ToDictionary(r => r[0], StringComparer.Ordinal);
            LabelMap = Read("sense_label_map");
            Groups = Read("sense_group").ToDictionary(r => r[0], StringComparer.Ordinal);
            Members = Read("group_sense");
            GroupOf = Members.ToDictionary(r => r[1], r => r[0], StringComparer.Ordinal);
            Frequency = Read("sense_frequency").ToDictionary(r => r[0], StringComparer.Ordinal);
            Glosses = Read("gloss").Where(g => g[2].Length > 0).Select(g => g[0]).ToHashSet(StringComparer.Ordinal);
            FormEntries = Read("form").Where(f => f[2].Length > 0).Select(f => f[2]).ToHashSet(StringComparer.Ordinal);
            FormsOf = Read("form").Where(f => f[2].Length > 0).GroupBy(f => f[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(f => f[2]).ToList(), StringComparer.Ordinal);
        }

        public Dictionary<string, string[]> Entries { get; }

        public Dictionary<string, string[]> Senses { get; }

        public Dictionary<string, string[]> Labels { get; }

        public List<string[]> LabelMap { get; }

        public Dictionary<string, string[]> Groups { get; }

        public List<string[]> Members { get; }

        public Dictionary<string, string> GroupOf { get; }

        public Dictionary<string, string[]> Frequency { get; }

        public HashSet<string> Glosses { get; }

        public HashSet<string> FormEntries { get; }

        public Dictionary<string, List<string>> FormsOf { get; }

        public string Lemma(string[] sense) => Entries[sense[2]][1];

        public bool InInventory(string[] sense) => Entries[sense[2]][4] == "1" || sense[1].Length == 0;

        public string PublicId(string senseId) => Lemma(Senses[senseId]) + "#" + Labels[senseId][1];
    }

    /// <summary>
    ///     The README files and the provenance record credit Open English WordNet and its license.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_Attribution_NamesLicenseAndSource()
    {
        // Arrange
        var root = DataFixture.RepositoryRoot();
        var files = new[]
        {
            Path.Join(root, "README.md"),
            Path.Join(root, "data", "README.md"),
            Path.Join(root, "data", "PROVENANCE.md")
        };

        // Act / Assert
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.Contains("Open English WordNet", text, StringComparison.Ordinal);
            Assert.Contains("CC BY 4.0", text, StringComparison.Ordinal);
        }
    }
}
