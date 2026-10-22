using System.Text;
using DemaConsulting.WordSense;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the sense catalog part of the image compiler on a small hand-written data folder.
/// </summary>
public sealed class ImageCompilerTests : IDisposable
{
    private const string Bank = "oewn-bank__";
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    /// <summary>
    ///     The catalog of a small data folder is compiled deterministically: equal data gives equal sections and an equal
    ///     inventory hash, the senses of a lemma are in the order of Open English WordNet (the position of the sense in its
    ///     entry; the tag counts of the fixture rank them the other way round and must not matter), and the group is listed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_CatalogIsDeterministic()
    {
        var data = WriteData(_dir.Root);

        var (first, result) = ImageCompiler.Build(data);
        var (second, _) = ImageCompiler.Build(data);

        var a = WordImage.Open(first);
        var b = WordImage.Open(second);
        Assert.Equal(6, result.Senses);
        Assert.Equal(1, result.Groups);
        Assert.True(a.InventoryHash.SequenceEqual(b.InventoryHash));
        Assert.All(a.SectionIds, id => Assert.True(a.RawSection(id).AsSpan().SequenceEqual(b.RawSection(id)), $"Section {id} differs."));
        Assert.Equal(
            ["bank#coin-bank", "bank#banking-company", "bank#ridge", "bank#incline"],
            a.SensesOf("bank", PartOfSpeech.Noun).Select(s => s.SenseId));
        Assert.True(a.TryGetGroup("bank#group:depository", out var group));
        Assert.Equal(["bank#coin-bank", "bank#banking-company"], group.SenseIds);
        Assert.True(a.TryGetSenseByKey("bank%1:17:01::", out var incline));
        Assert.Null(incline.GroupId);
        Assert.True(a.InventoryHash.SequenceEqual(ImageFormat.ComputeInventoryHash(a.RawSection)));
        a.ValidateCatalog();
    }

    /// <summary>
    ///     The order of the senses of a lemma and part of speech is the position in the Open English WordNet entry, then the
    ///     homograph number of the entry, with the project senses (no sense key) last whatever their position; the tag
    ///     counts of the sense frequency table do not decide it, and the order is the same when the counts are removed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_SenseOrder_IsOewnPositionThenHomographThenProjectSenses()
    {
        string[] entries = ["oewn-bank-1-n\tbank\tn\t1\t1\t1\toewn"];
        string[] senses =
        [
            Bank + "1.99.00..\tbank%1:99:00::\toewn-bank-1-n\toewn-7-n\t1",
            Bank + "9.99.00..\t\toewn-bank-n\tdema-bank-n\t0",
        ];
        string[] labels = [Bank + "1.99.00..\thomograph-one\tgenerated", Bank + "9.99.00..\tproject-sense\tcurated"];
        var data = WriteData(_dir.Root, extraEntries: entries, extraSenses: senses, extraLabels: labels);
        var order = new[] { "bank#coin-bank", "bank#homograph-one", "bank#banking-company", "bank#ridge", "bank#incline", "bank#project-sense" };

        var (image, _) = ImageCompiler.Build(data);
        var noCounts = Path.Join(_dir.Root, "no-counts");
        var (without, _) = ImageCompiler.Build(WriteData(noCounts, extraEntries: entries, extraSenses: senses, extraLabels: labels, frequencies: false));

        Assert.Equal(order, WordImage.Open(image).SensesOf("bank", PartOfSpeech.Noun).Select(s => s.SenseId));
        Assert.Equal(order, WordImage.Open(without).SensesOf("bank", PartOfSpeech.Noun).Select(s => s.SenseId));
    }

    /// <summary>
    ///     A sense of a single-token entry that has no label fails the compilation and is named.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_SenseWithoutLabel_Fails()
    {
        var data = WriteData(_dir.Root, labels: Labels.Where(l => !l.StartsWith(Bank + "1.17.00..\t", StringComparison.Ordinal)));

        var error = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(data));

        Assert.Contains(Bank + "1.17.00..", error.Message, StringComparison.Ordinal);
        Assert.Contains("has no label", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense in two groups fails the compilation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_SenseInTwoGroups_Fails()
    {
        var data = WriteData(
            _dir.Root,
            groups: [.. Groups, "bank#group:other\tbank\tn\tother\tcurated\trelated"],
            members: [.. Members, "bank#group:other\t" + Bank + "1.14.00..", "bank#group:other\t" + Bank + "1.17.00.."]);

        var error = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(data));

        Assert.Contains("is in the groups", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group id without the group marker, with the marker of another lemma, or equal to a sense id fails the compilation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_GroupIdWithoutMarkerOrEqualToSenseId_Fails()
    {
        var noMarker = WriteData(
            _dir.Root + "-marker",
            groups: ["bank#depository\tbank\tn\tdepository\tcurated\trelated"],
            members: [.. Members.Select(m => m.Replace("#group:", "#", StringComparison.Ordinal))]);
        var senseId = WriteData(
            _dir.Root + "-sense",
            groups: ["bank#incline\tbank\tn\tincline\tcurated\trelated"],
            members: [.. Members.Select(m => m.Replace("bank#group:depository", "bank#incline", StringComparison.Ordinal))]);

        Assert.Contains("never the id of a sense", Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(noMarker)).Message, StringComparison.Ordinal);
        Assert.Contains("never the id of a sense", Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(senseId)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group of one sense and a group of all the senses of its lemma and part of speech fail the compilation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_GroupOfOneSenseOrAllSenses_Fails()
    {
        var one = WriteData(_dir.Root + "-one", members: [Members[0]]);
        var all = WriteData(
            _dir.Root + "-all",
            members: [.. Members, "bank#group:depository\t" + Bank + "1.17.01..", "bank#group:depository\t" + Bank + "1.17.00.."]);

        var oneError = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(one));
        var allError = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(all));

        Assert.Contains("at least two senses", oneError.Message, StringComparison.Ordinal);
        Assert.Contains("never holds all the senses", allError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A declared group that has no member in <c>group_sense</c> fails the compilation instead of being left out of
    ///     the image silently.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_GroupWithoutMembers_Fails()
    {
        // Arrange - the group is declared, and no sense is listed in it
        var data = WriteData(_dir.Root, members: []);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(data));

        // Assert
        Assert.Contains("bank#group:depository", error.Message, StringComparison.Ordinal);
        Assert.Contains("at least two senses", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group that mixes parts of speech fails the compilation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_GroupMixingPartsOfSpeech_Fails()
    {
        var data = WriteData(_dir.Root, members: [Members[0], "bank#group:depository\t" + Bank + "2.35.00.."]);

        var error = Assert.Throws<InvalidDataException>(() => ImageCompiler.Build(data));

        Assert.Contains("not all senses of the lemma", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Gets the label rows of the small data folder.
    /// </summary>
    private static string[] Labels =>
    [
        Bank + "1.06.01..\tcoin-bank\tcurated",
        Bank + "1.14.00..\tbanking-company\tcurated",
        Bank + "1.17.00..\tridge\tgenerated",
        Bank + "1.17.01..\tincline\tgenerated",
        Bank + "2.35.00..\tclose-in\tgenerated",
        Bank + "2.35.01..\tcover\tgenerated",
    ];

    private static string[] Groups => ["bank#group:depository\tbank\tn\tdepository\tcurated\trelated"];

    private static string[] Members => ["bank#group:depository\t" + Bank + "1.14.00..", "bank#group:depository\t" + Bank + "1.06.01.."];

    /// <summary>
    ///     Writes the small data folder of these tests below a directory.
    /// </summary>
    /// <param name="root">The directory.</param>
    /// <returns>The data folder.</returns>
    internal static string WriteSmallData(string root) => WriteData(root);

    private static string WriteData(
        string root,
        IEnumerable<string>? labels = null,
        IEnumerable<string>? groups = null,
        IEnumerable<string>? members = null,
        IEnumerable<string>? extraEntries = null,
        IEnumerable<string>? extraSenses = null,
        IEnumerable<string>? extraLabels = null,
        bool frequencies = true)
    {
        var data = Path.Join(root, "data");
        Directory.CreateDirectory(data);
        File.Copy(Path.Join(DataFixture.RepositoryRoot(), "data", "schema.yaml"), Path.Join(data, "schema.yaml"), true);
        Table(data, "entry/b.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id", ["oewn-bank-n\tbank\tn\t0\t1\t1\toewn", "oewn-bank-v\tbank\tv\t0\t1\t1\toewn", .. extraEntries ?? []]);
        Table(
            data,
            "sense/b.tsv",
            "sense_id\tsense_key\tentry_id\tsynset_id\tposition",
            [
                Bank + "1.06.01..\tbank%1:06:01::\toewn-bank-n\toewn-1-n\t1",
                Bank + "1.14.00..\tbank%1:14:00::\toewn-bank-n\toewn-2-n\t2",
                Bank + "1.17.00..\tbank%1:17:00::\toewn-bank-n\toewn-3-n\t3",
                Bank + "1.17.01..\tbank%1:17:01::\toewn-bank-n\toewn-4-n\t4",
                Bank + "2.35.00..\tbank%2:35:00::\toewn-bank-v\toewn-5-v\t1",
                Bank + "2.35.01..\tbank%2:35:01::\toewn-bank-v\toewn-6-v\t2",
                .. extraSenses ?? [],
            ]);
        Table(data, "sense_label/b.tsv", "sense_id\tlabel\torigin", [.. labels ?? Labels, .. extraLabels ?? []]);
        Table(
            data,
            "sense_frequency/b.tsv",
            "sense_key\ttag_count\tsource",
            frequencies
                ? ["bank%1:06:01::\t0\twn30", "bank%1:14:00::\t20\twn30", "bank%1:17:00::\t2\twn30", "bank%1:17:01::\t25\twn30", "bank%2:35:00::\t1\twn30", "bank%2:35:01::\t0\twn30"]
                : []);
        Table(data, "form/b.tsv", "spelling\trank\tentry_id\tfeatures\tsource\treviewed\tsurface_lemma\tsurface_pos\torigin", ["bank\t0\toewn-bank-n\tNumber=Sing\tdema\t0\t\t\toewn_form", "bank\t1\toewn-bank-v\tVerbForm=Inf\tdema\t0\t\t\toewn_form"]);
        Table(data, "sense_group/b.tsv", "group_id\tlemma\tpos\tlabel\torigin", groups ?? Groups);
        Table(data, "group_sense/b.tsv", "group_id\tsense_id", members ?? Members);
        return data;
    }

    private static void Table(string data, string relative, string header, IEnumerable<string> rows)
    {
        var path = Path.Join(data, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, header + "\n" + string.Concat(rows.Select(r => r + "\n")), new UTF8Encoding(false));
    }
}
