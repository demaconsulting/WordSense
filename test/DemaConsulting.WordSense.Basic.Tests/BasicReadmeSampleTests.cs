using System.Globalization;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Doc-check tests that keep the Basic README usage sample and its documented output from drifting.
/// </summary>
/// <remarks>
///     <c>README.md</c> marks the sample with <c>&lt;!-- sample:readme --&gt;</c> before the code block and
///     <c>&lt;!-- output:basic --&gt;</c> before the output block. The code block must equal the using directives plus
///     the region of <c>BasicReadmeSample.cs</c> between the <c>&lt;sample:readme&gt;</c> markers, and the output block
///     must equal what that code writes.
/// </remarks>
public class BasicReadmeSampleTests
{
    private const string Usings = "using DemaConsulting.WordSense;\nusing DemaConsulting.WordSense.Basic;\n\n";

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WordSense.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private static string Normalize(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd())).TrimEnd();

    private static string FencedBlock(string text, string marker, string language)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The marker '{marker}' was not found.");
        var from = text.IndexOf($"```{language}", start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No '{language}' block follows '{marker}'.");
        from = text.IndexOf('\n', from) + 1;
        var to = text.IndexOf("```", from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"The block after '{marker}' is not closed.");
        return text[from..to];
    }

    private static string CompiledSample(string source)
    {
        const string begin = "// <sample:readme>";
        const string end = "// </sample:readme>";
        var from = source.IndexOf(begin, StringComparison.Ordinal);
        var to = source.IndexOf(end, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "The sample markers were not found.");
        from = source.IndexOf('\n', from) + 1;
        var lines = source[from..to].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return Normalize(Usings + string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())));
    }

    /// <summary>
    ///     The README code block equals the compiled sample code.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicReadme_CodeBlock_MatchesCompiledSample()
    {
        var root = FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), token);
        var source = await File.ReadAllTextAsync(Path.Combine(root, "test", "DemaConsulting.WordSense.Basic.Tests", "BasicReadmeSample.cs"), token);

        Assert.Equal(CompiledSample(source), Normalize(FencedBlock(readme, "<!-- sample:readme -->", "csharp")));
    }

    /// <summary>
    ///     The documented output equals the output of the sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicReadme_Output_MatchesSampleRun()
    {
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "README.md"), token);
        var documented = FencedBlock(readme, "<!-- output:basic -->", "text");

        var sample = new BasicReadmeSample();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await sample.RunAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        AssertOutput(documented, sample.Output);
    }

    /// <summary>
    ///     The allowed difference of a documented confidence. The Basic model is deterministic managed code, so only the
    ///     last digit of a rounded value may differ between math libraries.
    /// </summary>
    private const decimal ConfidenceTolerance = 0.01m;

    private static void AssertOutput(string documented, string actual)
    {
        var expectedLines = Normalize(documented).Split('\n');
        var actualLines = Normalize(actual).Split('\n');
        Assert.Equal(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < expectedLines.Length; i++)
        {
            var expected = expectedLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = actualLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(expected.Length >= 3 && found.Length == expected.Length, $"Line {i + 1}: expected '{expectedLines[i]}' but found '{actualLines[i]}'.");

            // Word, part of speech and respelling exactly; the confidence (third column) within the tolerance
            Assert.Equal(expected[0], found[0]);
            Assert.Equal(expected[1], found[1]);
            Assert.Equal(string.Join(' ', expected.Skip(3)), string.Join(' ', found.Skip(3)));
            var difference = Math.Abs(decimal.Parse(expected[2], CultureInfo.InvariantCulture) - decimal.Parse(found[2], CultureInfo.InvariantCulture));
            Assert.True(difference <= ConfidenceTolerance, $"Line {i + 1}: the confidence {found[2]} of '{found[0]}' differs from the documented {expected[2]}.");
        }
    }
}
