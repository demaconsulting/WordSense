using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reproducibility of the form tables: the same inputs give identical bytes, whatever the order of the input
///     lines, and an input that does not match its pin is refused.
/// </summary>
/// <remarks>
///     The tests use the small committed Open English WordNet slice and a small inflection list, so they need no download.
/// </remarks>
public sealed class FormReproducibilityTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private string TinyInputs(out string agid)
    {
        var oewn = Path.Combine(_dir.Root, "oewn.xml");
        File.Copy(DataFixture.SlicePath, oewn);
        agid = Path.Combine(_dir.Root, "infl.txt");
        File.WriteAllText(agid, "bag: N: bags\n");
        return oewn;
    }

    private static SortedDictionary<string, byte[]> Snapshot(string directory) =>
        new(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal), StringComparer.Ordinal);

    private SortedDictionary<string, byte[]> Generate(string name, string oewn, string agid)
    {
        var root = DataFixture.RepositoryRoot();
        var output = Path.Combine(_dir.Root, name);
        Directory.CreateDirectory(output);
        FormGenerator.Run(Path.Combine(root, "data"), Path.Combine(root, "corpus"), oewn, agid, output, null, null);
        return Snapshot(output);
    }

    private static void AssertIdentical(SortedDictionary<string, byte[]> expected, SortedDictionary<string, byte[]> actual)
    {
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (path, bytes) in expected)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(actual[path]), $"{path} differs");
        }
    }

    /// <summary>
    ///     Running the generator twice on the same inputs gives identical bytes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_SameInputs_GiveIdenticalBytes()
    {
        var oewn = TinyInputs(out var agid);
        AssertIdentical(Generate("first", oewn, agid), Generate("second", oewn, agid));
    }

    /// <summary>
    ///     Reordering the lines of the inflection list does not change the bytes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_ReorderedInput_GivesIdenticalBytes()
    {
        var oewn = TinyInputs(out var agid);
        var first = Generate("ordered", oewn, agid);
        File.WriteAllLines(agid, ["walk: V: walked | walking | walks", "bag: N: bags", "mouse: N: mice"]);
        var ordered = Generate("ordered2", oewn, agid);
        File.WriteAllLines(agid, ["mouse: N: mice", "bag: N: bags", "walk: V: walked | walking | walks"]);
        var reordered = Generate("reordered", oewn, agid);
        AssertIdentical(ordered, reordered);
        Assert.NotEmpty(first);
    }

    /// <summary>
    ///     An input that does not match its SHA-256 pin is refused before anything is generated.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_UnpinnedInput_IsRefused()
    {
        var oewn = TinyInputs(out var agid);
        var root = DataFixture.RepositoryRoot();
        var ex = Assert.Throws<InvalidDataException>(() => FormGenerator.Run(Path.Combine(root, "data"), Path.Combine(root, "corpus"), oewn, agid, _dir.Root));
        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An -ing noun has no plural spelled like its singular unless it is one of the invariant names.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="plural">True when the spelling has a plural analysis of its own entry.</param>
    [Theory]
    [InlineData("bowling", false)]
    [InlineData("sibling", false)]
    [InlineData("feeling", false)]
    [InlineData("herring", true)]
    [InlineData("offspring", true)]
    public void WordSenseBuilder_FormGenerator_Data_IngNounPlural_IsNotTheSingular(string spelling, bool plural)
    {
        var data = Path.Combine(DataFixture.RepositoryRoot(), "data");
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        var own = DataTables.Read(data, schema.Table("form")).Where(r => r[0] == spelling && r[2] == $"oewn-{spelling}-n" && r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.Equal(plural, own.Any());
    }
}
