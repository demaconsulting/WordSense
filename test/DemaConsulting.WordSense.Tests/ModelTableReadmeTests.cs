namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Doc-check tests that keep the model comparison table of the README equal to the generated table file.
/// </summary>
/// <remarks>
///     <c>docs/user_guide/model-table.md</c> is written by <c>WordSense.Builder evaluate &lt;model&gt; --table</c> from
///     measured values. Both it and <c>README.md</c> enclose the table between <c>&lt;!-- model-table:begin --&gt;</c>
///     and <c>&lt;!-- model-table:end --&gt;</c>. The check needs no model files.
/// </remarks>
public class ModelTableReadmeTests
{
    private const string Begin = "<!-- model-table:begin -->";
    private const string End = "<!-- model-table:end -->";

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "WordSense.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private static string[] TableLines(string text, string source)
    {
        var from = text.IndexOf(Begin, StringComparison.Ordinal);
        var to = text.IndexOf(End, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"The model table markers were not found in {source}.");
        return text[(from + Begin.Length)..to]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();
    }

    /// <summary>
    ///     The README table equals the generated table file.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelTable_Readme_MatchesGeneratedFile()
    {
        var root = FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Join(root, "README.md"), token);
        var generated = await File.ReadAllTextAsync(Path.Join(root, "docs", "user_guide", "model-table.md"), token);

        var expected = TableLines(generated, "docs/user_guide/model-table.md");
        Assert.Equal(expected, TableLines(readme, "README.md"));
    }

    /// <summary>
    ///     The generated table has a header, a separator and one row for each of the three models.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelTable_GeneratedFile_HasRowForEachModel()
    {
        var token = TestContext.Current.CancellationToken;
        var generated = await File.ReadAllTextAsync(Path.Join(FindRepositoryRoot(), "docs", "user_guide", "model-table.md"), token);

        var lines = TableLines(generated, "docs/user_guide/model-table.md");

        Assert.Equal(5, lines.Length);
        Assert.Contains(lines, l => l.StartsWith("| Basic |", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("| MiniLM |", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("| BERT base cased |", StringComparison.Ordinal));
    }
}
