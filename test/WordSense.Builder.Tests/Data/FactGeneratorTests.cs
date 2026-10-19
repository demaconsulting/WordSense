using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="FactGenerator"/>: the source-derived facts are repeatable and the committed fact file is the output
///     of the command.
/// </summary>
public sealed class FactGeneratorTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "data", "schema.yaml")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("The repository was not found.");
    }

    private static FactGenerator.Generated Generate(int seed) =>
        FactGenerator.Generate(Path.Join(Root(), "data"), Path.Join(Root(), "corpus"), seed, FactGenerator.DefaultSample);

    /// <summary>
    ///     The same seed gives the same facts and another seed gives other sampled facts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FactGenerator_Generate_IsDeterministicForSeed()
    {
        var first = FactGenerator.Render(Generate(1).Facts);

        Assert.Equal(first, FactGenerator.Render(Generate(1).Facts));
        Assert.NotEqual(first, FactGenerator.Render(Generate(2).Facts));
    }

    /// <summary>
    ///     The committed generated fact file is the output of the command with the default seed and sample size.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FactGenerator_Generate_MatchesCommittedFile()
    {
        var path = Path.Join(Root(), "test", "DemaConsulting.WordSense.Tests", "Fixtures", "lexicon-facts-generated.tsv");
        var expected = FactGenerator.Render(Generate(FactGenerator.DefaultSeed).Facts);

        Assert.Equal(expected, File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Every generated fact holds in the shipped word table, and every -ing noun or adjective entry of the dictionary has
    ///     its own analysis there.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FactGenerator_Check_AllFactsAndIngEntriesHold()
    {
        var facts = Generate(FactGenerator.DefaultSeed).Facts;
        var (checkedCount, _, missing) = FactGenerator.CheckIngEntries(Path.Join(Root(), "data"));

        Assert.Empty(FactGenerator.Check(facts));
        Assert.True(checkedCount > 2000);
        Assert.Empty(missing);
    }
}
