using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reproducibility of the form tables: the same inputs give identical bytes, whatever the order of the input
///     lines, and an input that does not match its pin is refused.
/// </summary>
/// <remarks>
///     The tests use the small committed Open English WordNet slice and a small list of authored forms, so they need no download.
/// </remarks>
public sealed class FormReproducibilityTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private string TinyInputs()
    {
        var oewn = Path.Join(_dir.Root, "oewn.xml");
        File.Copy(DataFixture.SlicePath, oewn);
        return oewn;
    }

    // A corpus of the repository lexicon whose authored forms are the given lines
    private string TinyCorpus(string name, params string[] authoredLines)
    {
        var corpus = Path.Join(_dir.Root, name);
        var lexicon = Path.Join(corpus, "lexicon");
        Directory.CreateDirectory(lexicon);
        foreach (var file in Directory.GetFiles(Path.Join(DataFixture.RepositoryRoot(), "corpus", "lexicon")))
        {
            if (!Path.GetFileName(file).StartsWith("forms-authored", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Join(lexicon, Path.GetFileName(file)));
            }
        }

        File.WriteAllLines(Path.Join(lexicon, AuthoredForms.FormsFile), authoredLines);
        File.WriteAllText(Path.Join(lexicon, AuthoredForms.ProvenanceFile), AuthoredFixtures.Sidecar(authoredLines));
        return corpus;
    }

    private static SortedDictionary<string, byte[]> Snapshot(string directory) =>
        new(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal), StringComparer.Ordinal);

    private SortedDictionary<string, byte[]> Generate(string name, string oewn, string? corpus = null)
    {
        var root = DataFixture.RepositoryRoot();
        var output = Path.Join(_dir.Root, name);
        Directory.CreateDirectory(output);
        FormGenerator.Run(Path.Join(root, "data"), corpus ?? Path.Join(root, "corpus"), oewn, output, null);
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
        var oewn = TinyInputs();
        AssertIdentical(Generate("first", oewn), Generate("second", oewn));
    }

    /// <summary>
    ///     Reordering the lines of the authored forms does not change the bytes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_ReorderedInput_GivesIdenticalBytes()
    {
        var oewn = TinyInputs();
        var ordered = Generate("ordered", oewn, TinyCorpus("corpus-ordered", "walk\tV\tPast\twalked\t0", "bag\tN\tPlural\tbags\t0", "mouse\tN\tPlural\tmice\t0"));
        var reordered = Generate("reordered", oewn, TinyCorpus("corpus-reordered", "mouse\tN\tPlural\tmice\t0", "bag\tN\tPlural\tbags\t0", "walk\tV\tPast\twalked\t0"));
        AssertIdentical(ordered, reordered);
    }

    /// <summary>
    ///     Two legacy analyses of one spelling with the same entry and features are rejected with the lines of both, so a
    ///     table never lists the same analysis twice (the committed file once held such a pair for "supposed").
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_RepeatedFrozenAnalysis_IsRefused()
    {
        // Arrange - a corpus whose frozen file repeats an analysis that differs only in its retired class column
        var oewn = TinyInputs();
        _dir.Write("corpus/lexicon/form-frozen.tsv",
            "frozen\tsupposed\t0\toewn-supposed-a\tDegree=Pos\tdema\t1\t\tsupposed#supposed-adj\n"
            + "frozen\tsupposed\t1\toewn-suppose-v\tTense=Past+VerbForm=Part\tdema\t1\t\tsupposed#supposed-past\n"
            + "frozen\tsupposed\t2\toewn-supposed-a\tDegree=Pos\tdema\t1\t\tsupposed#supposed-to\n");
        var root = DataFixture.RepositoryRoot();

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => FormGenerator.Run(Path.Join(root, "data"), Path.Join(_dir.Root, "corpus"), oewn, Path.Join(_dir.Root, "out"), null));

        // Assert
        Assert.Contains("form-frozen.tsv:3:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("line 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("supposed", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The same analysis (spelling, entry and features) listed once as frozen and once as closed is rejected too, with
    ///     the kind and the line of both rows; the kind is not part of the identity of an analysis.
    /// </summary>
    /// <param name="firstKind">The kind of the first row.</param>
    /// <param name="secondKind">The kind of the second row.</param>
    [Theory]
    [InlineData("frozen", "closed")]
    [InlineData("closed", "frozen")]
    [InlineData("closed", "closed")]
    public void WordSenseBuilder_FormGenerator_Run_RepeatedAnalysisOfAnyKind_IsRefused(string firstKind, string secondKind)
    {
        // Arrange - one analysis twice, the kinds as the case says, with an unrelated row between them
        var oewn = TinyInputs();
        _dir.Write("corpus/lexicon/form-frozen.tsv",
            $"{firstKind}\tsupposed\t0\toewn-supposed-a\tDegree=Pos\tdema\t1\t\tsupposed#supposed-adj\n"
            + "frozen\tsupposed\t1\toewn-suppose-v\tTense=Past+VerbForm=Part\tdema\t1\t\tsupposed#supposed-past\n"
            + $"{secondKind}\tsupposed\t2\toewn-supposed-a\tDegree=Pos\tdema\t1\t\tsupposed#supposed-to\n");
        var root = DataFixture.RepositoryRoot();

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => FormGenerator.Run(Path.Join(root, "data"), Path.Join(_dir.Root, "corpus"), oewn, Path.Join(_dir.Root, "out"), null));

        // Assert: the second row is named with its kind, and the first with its kind and line
        Assert.Contains("form-frozen.tsv:3:", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"the {secondKind} analysis of 'supposed'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"repeats the {firstKind} one on line 1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An input that does not match its SHA-256 pin is refused before anything is generated.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_UnpinnedInput_IsRefused()
    {
        var oewn = TinyInputs();
        var root = DataFixture.RepositoryRoot();
        var ex = Assert.Throws<InvalidDataException>(() => FormGenerator.Run(Path.Join(root, "data"), Path.Join(root, "corpus"), oewn, _dir.Root));
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
        var data = Path.Join(DataFixture.RepositoryRoot(), "data");
        var schema = DataSchema.Load(Path.Join(data, "schema.yaml"));
        var own = DataTables.Read(data, schema.Table("form")).Where(r => r[0] == spelling && r[2] == $"oewn-{spelling}-n" && r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.Equal(plural, own.Any());
    }
}
