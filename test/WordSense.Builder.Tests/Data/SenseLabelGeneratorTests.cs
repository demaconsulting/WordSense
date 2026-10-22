using System.Text;
using System.Text.RegularExpressions;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="SenseLabelGenerator"/>: the labels of the senses, on small in-memory tables for the rules and
///     on the imported slice for the files. The generator never creates a group.
/// </summary>
public sealed partial class SenseLabelGeneratorTests : IDisposable
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["n"] = "noun",
        ["v"] = "verb",
        ["a"] = "adjective",
        ["r"] = "adverb",
        ["u"] = "pronoun",
        ["p"] = "preposition",
        ["c"] = "conjunction",
        ["i"] = "interjection",
        ["d"] = "determiner",
        ["x"] = "auxiliary",
    };

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormed();

    private static LabelSense Sense(string id, string lemma, string pos, string synset, int tokens = 1) =>
        new(id, id + "%", lemma, pos, tokens, synset);

    private static LabelInputs Inputs(
        IEnumerable<LabelSense> senses,
        Dictionary<string, IReadOnlyList<string>>? hypernyms = null,
        IEnumerable<string[]>? labels = null,
        IEnumerable<string>? retired = null,
        Dictionary<string, int>? counts = null) =>
        new(
            [.. senses],
            hypernyms ?? new Dictionary<string, IReadOnlyList<string>>(),
            [.. labels ?? []],
            [.. retired ?? []],
            counts ?? [],
            Names);

    private static Dictionary<string, string> LabelsOf(LabelOutput output) => output.Rows.ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);

    private string LabeledSlice(string name = "data")
    {
        var data = DataFixture.ImportSlice(_dir, name);
        SenseLabelGenerator.Run(data);
        return data;
    }

    private static string FolderText(string data, params string[] tables) =>
        string.Join('\n', tables.SelectMany(t => Directory.GetFiles(Path.Join(data, t), "*.tsv").OrderBy(f => f, StringComparer.Ordinal).Select(f => Path.GetFileName(f) + "\n" + File.ReadAllText(f, Encoding.UTF8))));

    /// <summary>
    ///     The only sense of a lemma and part of speech is labeled with the part of speech name.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_OnlySenseOfPartOfSpeech_UsesThePartOfSpeechName()
    {
        // Arrange
        var inputs = Inputs([Sense("s1", "bank", "n", "y1"), Sense("s2", "bank", "v", "y2"), Sense("s3", "the", "d", "y3")]);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert
        Assert.Equal("noun", labels["s1"]);
        Assert.Equal("verb", labels["s2"]);
        Assert.Equal("determiner", labels["s3"]);
    }

    /// <summary>
    ///     A sense of a synset with a synonym takes the slug of the first synonym; a synonym that slugs to the lemma
    ///     itself is skipped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_UsesTheFirstSynonym()
    {
        // Arrange - Bass shares a synset with bass (same slug) and fish; the second sense of the lemma has no synonym
        var inputs = Inputs(
        [
            Sense("b1", "Bass", "n", "z1"),
            Sense("b2", "bass", "n", "z1"),
            Sense("b3", "fish", "n", "z1"),
            Sense("b9", "Bass", "n", "z9"),
            Sense("c1", "car", "n", "y1"),
            Sense("c2", "automobile", "n", "y1"),
            Sense("c3", "machine", "n", "y1"),
            Sense("c9", "car", "n", "y9"),
        ]);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert
        Assert.Equal("fish", labels["b1"]);
        Assert.Equal("automobile", labels["c1"]);
        Assert.Equal("noun", labels["b9"]);
        Assert.Equal("noun", labels["c9"]);
    }

    /// <summary>
    ///     A sense with no usable synonym takes the head lemma of its first hypernym.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_NoSynonym_UsesTheHypernymHead()
    {
        // Arrange - the hypernym synsets are tried in the order of their ids; h-a has the senses "fish" and "animal"
        var hypernyms = new Dictionary<string, IReadOnlyList<string>> { ["y1"] = ["h-b", "h-a"], ["y2"] = [] };
        var inputs = Inputs(
            [
                Sense("t1", "tuna", "n", "y1"),
                Sense("t2", "tuna", "n", "y2"),
                Sense("f2", "animal", "n", "h-a"),
                Sense("f1", "fish", "n", "h-a"),
                Sense("g1", "food", "n", "h-b"),
            ],
            hypernyms);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert - the first sense by id of the first hypernym by id
        Assert.Equal("fish", labels["t1"]);
        Assert.Equal("noun", labels["t2"]);
    }

    /// <summary>
    ///     When the first member of the first hypernym synset has the slug of the lemma itself, the next usable member of
    ///     that same synset gives the label, not the head of another hypernym.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_FirstHypernymMemberIsSelf_UsesNextMemberOfThatHypernym()
    {
        // Arrange - h-a holds a sense of "tuna" (sorted first, so it is the lemma itself) and one of "fish"; h-b holds "food"
        var hypernyms = new Dictionary<string, IReadOnlyList<string>> { ["y1"] = ["h-b", "h-a"], ["y2"] = [] };
        var inputs = Inputs(
            [
                Sense("t1", "tuna", "n", "y1"),
                Sense("t2", "tuna", "n", "y2"),
                Sense("a1", "Tuna", "n", "h-a"),
                Sense("a2", "fish", "n", "h-a"),
                Sense("g1", "food", "n", "h-b"),
            ],
            hypernyms);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert
        Assert.Equal("fish", labels["t1"]);
    }

    /// <summary>
    ///     A count that is not a number, and a row of the wrong width, throw an <see cref="InvalidDataException"/> that
    ///     names the file and the line, and nothing is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_UnreadableRows_ThrowInvalidDataWithLocation()
    {
        // Arrange - a frequency file with a text count on line 2, and a copy whose sense row lost a cell
        var badCount = DataFixture.ImportSlice(_dir, "count");
        DataFixture.Write(badCount, "sense_frequency/b.tsv", "sense_key\ttag_count\tsource\nbass%1:05:00::\tmany\twn30\n");
        var shortRow = DataFixture.ImportSlice(_dir, "short");
        DataFixture.Replace(shortRow, "sense/b.tsv", "oewn-bass__1.05.00..\tbass%1:05:00::\toewn-bass-n\toewn-00000001-n\t1", "oewn-bass__1.05.00..\tbass%1:05:00::");

        // Act
        var count = Assert.Throws<InvalidDataException>(() => SenseLabelGenerator.Run(badCount));
        var width = Assert.Throws<InvalidDataException>(() => SenseLabelGenerator.Run(shortRow));

        // Assert
        Assert.Contains("b.tsv:2:", count.Message, StringComparison.Ordinal);
        Assert.Contains("'many'", count.Message, StringComparison.Ordinal);
        Assert.Contains("b.tsv:", width.Message, StringComparison.Ordinal);
        Assert.Contains("cells", width.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(badCount, "sense_label")));
        Assert.False(Directory.Exists(Path.Join(shortRow, "sense_label")));
    }

    /// <summary>
    ///     Labels that exist are written back with the same value and origin, exactly once, and the new labels are added.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_ExistingLabels_KeepValueAndOrigin()
    {
        // Arrange - the first noun of bass has a curated label that differs from the generated one
        var data = DataFixture.ImportSlice(_dir);
        DataFixture.Write(data, "sense_label/b.tsv", "sense_id\tlabel\torigin\noewn-bass__1.05.00..\tlow-voice\tcurated\n");

        // Act
        var run = SenseLabelGenerator.Run(data);
        var rows = DataFixture.Rows(data, "sense_label");

        // Assert
        Assert.Equal(1, run.Kept);
        Assert.Equal(11, run.Generated);
        Assert.Single(rows, r => r[0] == "oewn-bass__1.05.00..");
        Assert.Equal(["oewn-bass__1.05.00..", "low-voice", "curated"], rows.Single(r => r[0] == "oewn-bass__1.05.00.."));
        Assert.Equal(12, rows.Count);
        Assert.All(rows.Where(r => r[0] != "oewn-bass__1.05.00.."), r => Assert.Equal("generated", r[2]));
    }

    /// <summary>
    ///     Senses with the same slug get the plain slug and then -2, -3 in labeling order (the tag count decides).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_DuplicateLabels_GetNumericSuffixes()
    {
        // Arrange - three senses of bat share the synonym club, with the counts 0, 2 and 5
        var inputs = Inputs(
            [
                Sense("b1", "bat", "n", "y1"), Sense("c1", "club", "n", "y1"),
                Sense("b2", "bat", "n", "y2"), Sense("c2", "club", "n", "y2"),
                Sense("b3", "bat", "n", "y3"), Sense("c3", "club", "n", "y3"),
            ],
            counts: new Dictionary<string, int> { ["b2%"] = 2, ["b3%"] = 5 });

        // Act
        var output = SenseLabelGenerator.Generate(inputs);
        var labels = LabelsOf(output);

        // Assert - two labels with a suffix for bat, and two for club (its three senses share the synonym bat)
        Assert.Equal("club", labels["b3"]);
        Assert.Equal("club-2", labels["b2"]);
        Assert.Equal("club-3", labels["b1"]);
        Assert.Equal(4, output.Suffixed);
    }

    /// <summary>
    ///     The labeling order is the part of speech, then the WordNet 3.0 count descending, then the UTF-8 bytes of the sense id;
    ///     it is not the order in which the senses are listed (the order of Open English WordNet), so the label follows the counts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_LabelingOrder_FollowsPartOfSpeechCountAndSenseIdNotTheListedOrder()
    {
        // Arrange - the senses of bat are listed verb first, then n3, n1, n2; all share the synonym club. The verbs have the
        // highest counts but the nouns come first; n2 and n3 have the same count, so the sense id decides; n1 has no count
        var inputs = Inputs(
            [
                Sense("v9", "bat", "v", "yv"), Sense("cv", "club", "n", "yv"),
                Sense("v8", "bat", "v", "yw"), Sense("cw", "club", "n", "yw"),
                Sense("n3", "bat", "n", "y3"), Sense("c3", "club", "n", "y3"),
                Sense("n1", "bat", "n", "y1"), Sense("c1", "club", "n", "y1"),
                Sense("n2", "bat", "n", "y2"), Sense("c2", "club", "n", "y2"),
            ],
            counts: new Dictionary<string, int> { ["v9%"] = 9, ["v8%"] = 1, ["n2%"] = 4, ["n3%"] = 4 });

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert - the first noun of the labeling order keeps the plain slug, then the verbs follow by count
        Assert.Equal("club", labels["n2"]);
        Assert.Equal("club-2", labels["n3"]);
        Assert.Equal("club-3", labels["n1"]);
        Assert.Equal("club-4", labels["v9"]);
        Assert.Equal("club-5", labels["v8"]);
    }

    /// <summary>
    ///     Spaces, apostrophes, capitals and non-ASCII letters give well-formed labels, and no two labels of a lemma
    ///     are equal.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_LabelsAreWellFormedAndUniquePerLemma()
    {
        // Arrange - synonyms with an apostrophe, capitals, accents, a space and an astral letter
        var senses = new List<LabelSense>();
        var odd = new[] { "O'Neil", "Ångström unit", "café au lait", "  --x--  ", "𝒜 matrix", "naïve", "!!!" };
        for (var i = 0; i < odd.Length; i++)
        {
            senses.Add(Sense($"w{i}", "word", "n", $"y{i}"));
            senses.Add(Sense($"s{i}", odd[i], "n", $"y{i}"));
        }

        // Act
        var output = SenseLabelGenerator.Generate(Inputs(senses));
        var labels = LabelsOf(output);

        // Assert
        var wordLabels = labels.Where(p => p.Key.StartsWith('w')).Select(p => p.Value).ToList();
        Assert.All(output.Rows, r => Assert.Matches(WellFormed(), r[1]));
        Assert.Equal(wordLabels.Count, wordLabels.Distinct().Count());
        Assert.Contains("o-neil", wordLabels);
        Assert.Contains("naive", wordLabels);
        Assert.Contains("angstrom-unit", wordLabels);
        Assert.Contains("cafe-au-lait", wordLabels);
        Assert.Contains("x", wordLabels);
        Assert.Contains("matrix", wordLabels);
    }

    /// <summary>
    ///     The slug of a text is lower-case ASCII letters, digits and single hyphens, or empty.
    /// </summary>
    [Theory]
    [InlineData("Bank", "bank")]
    [InlineData("ice cream", "ice-cream")]
    [InlineData("o'clock", "o-clock")]
    [InlineData("--a--b--", "a-b")]
    [InlineData("Ångström", "angstrom")]
    [InlineData("3-D", "3-d")]
    [InlineData("!!!", "")]
    public void WordSenseBuilder_SenseLabelGenerator_Slug_GivesLowerCaseAsciiWords(string text, string expected)
    {
        // Arrange / Act
        var slug = SenseLabelGenerator.Slug(text);

        // Assert
        Assert.Equal(expected, slug);
    }

    /// <summary>
    ///     A label that exists is never changed and never listed again; a new sense does not take it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_ExistingLabels_AreKept()
    {
        // Arrange - b1 has a curated label that is also the synonym slug of b2
        var inputs = Inputs(
            [Sense("b1", "bat", "n", "y1"), Sense("c1", "club", "n", "y1"), Sense("b2", "bat", "n", "y2"), Sense("c2", "club", "n", "y2")],
            labels: [["b1", "club", "curated"], ["c1", "noun", "generated"], ["c2", "noun-2", "project"]]);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert
        Assert.False(labels.ContainsKey("b1"));
        Assert.False(labels.ContainsKey("c1"));
        Assert.Equal("club-2", labels["b2"]);
    }

    /// <summary>
    ///     A new sense of a lemma never takes the label of a sense of the lemma or of a retired id.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_NewSenseOfLemma_NeverTakesAnExistingOrRetiredLabel()
    {
        // Arrange - club is in use, bat#club-2 is retired; the label of another lemma does not matter
        var inputs = Inputs(
            [
                Sense("b1", "bat", "n", "y1"), Sense("b2", "bat", "n", "y2"), Sense("c2", "club", "n", "y2"),
                Sense("x1", "cat", "n", "y3"),
            ],
            labels: [["b1", "club", "curated"], ["x1", "club-2", "curated"]],
            retired: ["bat#club-2"]);

        // Act
        var labels = LabelsOf(SenseLabelGenerator.Generate(inputs));

        // Assert
        Assert.Equal("club-3", labels["b2"]);
    }

    /// <summary>
    ///     A sense without a sense key is a project sense and a multi-word lemma has no label.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_ProjectSenseAndMultiWordLemma_GetTheRightOrigin()
    {
        // Arrange
        var senses = new[]
        {
            new LabelSense("p1", string.Empty, "the", "d", 1, "y1"),
            Sense("m1", "ice cream", "n", "y2", 2),
            Sense("o1", "ice", "n", "y3"),
        };

        // Act
        var rows = SenseLabelGenerator.Generate(Inputs(senses)).Rows.ToDictionary(r => r[0], r => r, StringComparer.Ordinal);

        // Assert
        Assert.Equal(["p1", "determiner", "project"], rows["p1"]);
        Assert.Equal(["o1", "noun", "generated"], rows["o1"]);
        Assert.False(rows.ContainsKey("m1"));
    }

    /// <summary>
    ///     After the synonym or the hypernym of a sense changed, a second run leaves the published label as it was.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_ChangedSourceData_KeepsPublishedLabels()
    {
        // Arrange - the first run labels bass (sense 1) with its hypernym head "fish"
        var data = LabeledSlice();
        var before = FolderText(data, "sense_label");
        Assert.Contains("oewn-bass__1.05.00..\tfish\tgenerated", before, StringComparison.Ordinal);

        // Act - the hypernym of that sense is now music
        DataFixture.Replace(data, "synset_relation/b.tsv", "oewn-00000001-n\thypernym\toewn-00000002-n", "oewn-00000001-n\thypernym\toewn-00000043-n");
        var run = SenseLabelGenerator.Run(data);

        // Assert
        Assert.Equal(0, run.Generated);
        Assert.Equal(before, FolderText(data, "sense_label"));
    }

    /// <summary>
    ///     The run labels every sense of a one-token lemma, creates no group and reports the counts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_Slice_LabelsOneTokenSensesAndCreatesNoGroup()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);

        // Act
        var run = SenseLabelGenerator.Run(data);
        var labels = DataFixture.Rows(data, "sense_label");

        // Assert - 14 senses, two of them of the two-token lemmas "run up" and "𝒜 matrix"
        Assert.Equal(12, run.Generated);
        Assert.Equal(0, run.Kept);
        Assert.Equal(12, labels.Count);
        Assert.False(Directory.Exists(Path.Join(data, "sense_group")));
        Assert.False(Directory.Exists(Path.Join(data, "group_sense")));
        Assert.Contains(labels, l => l[0] == "oewn-large__5.00.00.big.00" && l[1] == "adjective");
        Assert.True(DataValidator.Validate(data).IsValid, string.Join("\n", DataValidator.Validate(data).Violations));
    }

    /// <summary>
    ///     A curated group stays as it is, and its label does not matter to the sense labels: the id of a group
    ///     (<c>lemma#group:label</c>) is in its own namespace, so a sense may have the same label.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_ExistingGroup_IsKeptAndItsLabelStaysAvailable()
    {
        // Arrange - bass has a curated group named fish; the first noun of bass is still labeled fish
        var data = DataFixture.ImportSlice(_dir);
        DataFixture.Write(data, "sense_group/b.tsv", "group_id\tlemma\tpos\tlabel\torigin\tkind\nbass#group:fish\tbass\tn\tfish\tcurated\trelated\n");
        DataFixture.Write(data, "group_sense/b.tsv", "group_id\tsense_id\nbass#group:fish\toewn-bass__1.05.00..\nbass#group:fish\toewn-bass__1.10.00..\n");
        var before = FolderText(data, "sense_group", "group_sense");

        // Act
        SenseLabelGenerator.Run(data);

        // Assert
        Assert.Equal(before, FolderText(data, "sense_group", "group_sense"));
        Assert.Contains(DataFixture.Rows(data, "sense_label"), l => l[0] == "oewn-bass__1.05.00.." && l[1] == "fish");
    }

    /// <summary>
    ///     Two runs, and two copies of the same data, give identical files.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_SameInputs_GiveIdenticalBytes()
    {
        // Arrange
        var first = LabeledSlice("first");
        var second = LabeledSlice("second");
        var firstText = FolderText(first, "sense_label");

        // Act - run the first copy again
        SenseLabelGenerator.Run(first);

        // Assert
        Assert.Equal(firstText, FolderText(first, "sense_label"));
        Assert.Equal(firstText, FolderText(second, "sense_label"));
    }

    /// <summary>
    ///     A copy whose input rows are in another order gives the same bytes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_ReorderedInput_GivesIdenticalBytes()
    {
        // Arrange - reverse the rows of the entry, sense and relation files of the second copy
        var first = DataFixture.ImportSlice(_dir, "first");
        var second = DataFixture.ImportSlice(_dir, "second");
        foreach (var file in new[] { "entry", "sense", "synset_relation" }.SelectMany(t => Directory.GetFiles(Path.Join(second, t), "*.tsv")))
        {
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            File.WriteAllText(file, string.Join('\n', lines.Take(1).Concat(lines.Skip(1).Reverse())) + "\n", new UTF8Encoding(false));
        }

        // Act
        SenseLabelGenerator.Run(first);
        SenseLabelGenerator.Run(second);

        // Assert
        Assert.Equal(FolderText(first, "sense_label"), FolderText(second, "sense_label"));
    }

    /// <summary>
    ///     A missing input table throws and nothing is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Run_MissingInputTable_Throws()
    {
        // Arrange
        var data = DataFixture.ImportSlice(_dir);
        Directory.Delete(Path.Join(data, "synset_relation"), true);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => SenseLabelGenerator.Run(data));

        // Assert
        Assert.Contains("synset_relation", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(data, "sense_label")));
    }

    /// <summary>
    ///     Two existing labels of one lemma that are equal throw, and nothing is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseLabelGenerator_Generate_EqualExistingLabels_Throws()
    {
        // Arrange
        var inputs = Inputs([Sense("b1", "bat", "n", "y1"), Sense("b2", "bat", "n", "y2")], labels: [["b1", "club", "curated"], ["b2", "club", "curated"]]);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => SenseLabelGenerator.Generate(inputs));
    }
}
