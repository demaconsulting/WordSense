using System.Globalization;
using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Doc-check tests that keep the README usage samples and their documented output from drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> marks each sample with <c>&lt;!-- sample:name --&gt;</c> before its code block and
///     <c>&lt;!-- output:name --&gt;</c> before its output block. The code block must equal the region of
///     <c>ReadmeSample.cs</c> between <c>&lt;sample:name&gt;</c> markers (which is compiled and run here against the
///     committed model), and the output block must equal what that code writes.
/// </remarks>
public partial class ReadmeSampleTests
{
    /// <summary>
    ///     The README code blocks equal the compiled sample code.
    /// </summary>
    /// <param name="name">The sample name.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("classify")]
    [InlineData("tags")]
    public async Task WordSense_Readme_CodeBlock_MatchesCompiledSample(string name)
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), token);
        var source = await File.ReadAllTextAsync(
            Path.Combine(root, "test", "DemaConsulting.WordSense.Tests", "ReadmeSample.cs"), token);

        var documented = Normalize(FencedBlock(readme, $"<!-- sample:{name} -->", "csharp"));
        var compiled = Normalize(Dedent(Between(source, $"// <sample:{name}>", $"// </sample:{name}>")));

        Assert.Equal(compiled, documented);
    }

    /// <summary>
    ///     The documented output equals the output of the sample run against the real model.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Readme_Output_MatchesReadme()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var location = WordSenseModelLocation.FromDirectory(ModelAssets.RequireModelDirectory());
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), TestContext.Current.CancellationToken);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var classify = new ReadmeSample();
        await classify.ClassifyAsync(location);

        using var classifier = await WordSenseClassifier.CreateAsync(
            new WordSenseClassifierOptions { ModelDirectory = location.Directory },
            TestContext.Current.CancellationToken);
        var tags = new ReadmeSample();
        await tags.TagsAsync(classifier);

        Assert.Equal(Normalize(classify.Output), Normalize(FencedBlock(readme, "<!-- output:classify -->", "text")));
        Assert.Equal(Normalize(tags.Output), Normalize(FencedBlock(readme, "<!-- output:tags -->", "text")));
    }

    /// <summary>
    ///     Returns the content of the fenced block that follows a marker line.
    /// </summary>
    private static string FencedBlock(string text, string marker, string language)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The marker '{marker}' was not found in README.md.");
        var open = $"```{language}";
        var from = text.IndexOf(open, start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No '{open}' block follows '{marker}'.");
        from = text.IndexOf('\n', from) + 1;
        var to = text.IndexOf("```", from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"The block after '{marker}' is not closed.");
        return text[from..to];
    }

    /// <summary>
    ///     Returns the lines between two marker lines of the sample source.
    /// </summary>
    private static string Between(string text, string begin, string end)
    {
        var from = text.IndexOf(begin, StringComparison.Ordinal);
        var to = text.IndexOf(end, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"The markers '{begin}' and '{end}' were not found.");
        from = text.IndexOf('\n', from) + 1;
        return text[from..to];
    }

    /// <summary>
    ///     Removes the common leading indentation of the lines.
    /// </summary>
    private static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    /// <summary>
    ///     Normalizes line endings and trailing white space, so only the content is compared.
    /// </summary>
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd());
        return Trailing().Replace(string.Join('\n', lines), string.Empty);
    }

    [GeneratedRegex(@"\s+$")]
    private static partial Regex Trailing();
}
