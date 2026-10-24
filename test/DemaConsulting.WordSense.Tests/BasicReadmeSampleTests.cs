using System.Globalization;

namespace DemaConsulting.WordSense.Tests;

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
    /// <summary>
    ///     The using directives that the README sample shows before the compiled region.
    /// </summary>
    private const string Usings = "using DemaConsulting.WordSense;\n\n";

    /// <summary>
    ///     Finds the repository root by walking up from the test output directory to the solution file.
    /// </summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no solution file is found above the test output.</exception>
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

    /// <summary>
    ///     Normalizes line endings and trailing white space, so only the content is compared.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd())).TrimEnd();

    /// <summary>
    ///     Returns the content of the fenced block that follows a marker line.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="marker">The marker line, for example <c>&lt;!-- sample:readme --&gt;</c>.</param>
    /// <param name="language">The language of the fence that follows the marker.</param>
    /// <returns>The block content without the fences.</returns>
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

    /// <summary>
    ///     Builds the text that the README code block must equal: the using directives plus the unindented region of the
    ///     sample source between the <c>sample:readme</c> markers.
    /// </summary>
    /// <param name="source">The text of <c>BasicReadmeSample.cs</c>.</param>
    /// <returns>The normalized expected code block.</returns>
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
        // Arrange: read the README and the compiled sample source
        var root = FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Join(root, "README.md"), token);
        var source = await File.ReadAllTextAsync(Path.Join(root, "test", "DemaConsulting.WordSense.Tests", "BasicReadmeSample.cs"), token);

        // Act: take the documented code block and build the expected one from the sample
        var documented = Normalize(FencedBlock(readme, "<!-- sample:readme -->", "csharp"));
        var compiled = CompiledSample(source);

        // Assert: the documented block equals the compiled sample
        Assert.Equal(compiled, documented);
    }

    /// <summary>
    ///     The documented output equals the output of the sample.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicReadme_Output_MatchesSampleRun()
    {
        // Arrange: read the documented output and create the sample
        var token = TestContext.Current.CancellationToken;
        var readme = await File.ReadAllTextAsync(Path.Join(FindRepositoryRoot(), "README.md"), token);
        var documented = FencedBlock(readme, "<!-- output:basic -->", "text");
        var sample = new BasicReadmeSample();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // Act: run the sample with the invariant culture and restore the culture
        try
        {
            await sample.RunAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Assert: the documented output equals what the sample wrote
        AssertOutput(documented, sample.Output);
    }

    /// <summary>
    ///     The allowed difference of a documented confidence. The Basic model is deterministic managed code, so only the
    ///     last digit of a rounded value may differ between math libraries.
    /// </summary>
    private const decimal ConfidenceTolerance = 0.01m;

    /// <summary>
    ///     Asserts that the output of the sample equals the documented output, with the confidence column compared
    ///     within <see cref="ConfidenceTolerance"/> and every other column exactly.
    /// </summary>
    /// <param name="documented">The output block of the README, the single source of truth.</param>
    /// <param name="actual">The output the sample wrote.</param>
    private static void AssertOutput(string documented, string actual)
    {
        var expectedLines = Normalize(documented).Split('\n');
        var actualLines = Normalize(actual).Split('\n');
        Assert.Equal(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < expectedLines.Length; i++)
        {
            var expected = expectedLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = actualLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(expected.Length >= 4 && found.Length == expected.Length, $"Line {i + 1}: expected '{expectedLines[i]}' but found '{actualLines[i]}'.");

            // Word, part of speech, level, sense and runner-up exactly; the confidence (fourth column) within the tolerance
            Assert.Equal(expected[0], found[0]);
            Assert.Equal(expected[1], found[1]);
            Assert.Equal(expected[2], found[2]);
            Assert.Equal(string.Join(' ', expected.Skip(4)), string.Join(' ', found.Skip(4)));
            var difference = Math.Abs(decimal.Parse(expected[3], CultureInfo.InvariantCulture) - decimal.Parse(found[3], CultureInfo.InvariantCulture));
            Assert.True(difference <= ConfidenceTolerance, $"Line {i + 1}: the confidence {found[3]} of '{found[0]}' differs from the documented {expected[3]}.");
        }
    }
}
