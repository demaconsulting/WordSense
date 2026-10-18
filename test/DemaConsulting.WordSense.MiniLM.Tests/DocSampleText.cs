using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Text helpers shared by the doc-check tests that compare fenced code blocks of the documentation with
///     compiled sample code.
/// </summary>
internal static partial class DocSampleText
{
    /// <summary>
    ///     Finds the repository root by walking up from the test output directory to the solution file.
    /// </summary>
    /// <returns>The repository root.</returns>
    public static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WordSense.slnx")))
            {
                return dir.FullName;
            }
        }

        Assert.Skip("The repository root was not found.");
        return string.Empty;
    }

    /// <summary>
    ///     Gets the committed MiniLM model directory or skips the calling test when the files are unavailable (for
    ///     example in a checkout without Git LFS content).
    /// </summary>
    /// <returns>The directory that holds the real model files.</returns>
    public static string RequireMiniLMDirectory() => RequireModelDirectory("all-MiniLM-L6-v2-int8");

    /// <summary>
    ///     Gets the committed directory of a model or skips the calling test when the files are unavailable (for
    ///     example in a checkout without Git LFS content).
    /// </summary>
    /// <param name="modelId">The model id, which is the name of the folder under <c>model</c>.</param>
    /// <returns>The directory that holds the real model files.</returns>
    public static string RequireModelDirectory(string modelId)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "model", modelId);
        foreach (var name in new[] { "model_int8.onnx", "vocab.txt" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                Assert.Skip($"The model file '{name}' is missing.");
            }

            using var stream = File.OpenRead(path);
            var buffer = new byte[64];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (Encoding.ASCII.GetString(buffer, 0, read).StartsWith("version https://git-lfs", StringComparison.Ordinal))
            {
                Assert.Skip($"The model file '{name}' is a Git LFS pointer; run 'git lfs pull'.");
            }
        }

        return directory;
    }

    /// <summary>
    ///     Returns the content of the fenced block that follows a marker line.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="marker">The marker line, for example <c>&lt;!-- sample:classify --&gt;</c>.</param>
    /// <param name="language">The language of the fence that follows the marker.</param>
    /// <returns>The block content without the fences.</returns>
    public static string FencedBlock(string text, string marker, string language)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The marker '{marker}' was not found.");
        var open = $"```{language}";
        var from = text.IndexOf(open, start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No '{open}' block follows '{marker}'.");
        from = text.IndexOf('\n', from) + 1;
        var to = text.IndexOf("```", from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"The block after '{marker}' is not closed.");
        return text[from..to];
    }

    /// <summary>
    ///     Returns the names of all samples marked in a document.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The sample names in document order.</returns>
    public static IReadOnlyList<string> SampleNames(string text) =>
        [.. Marker().Matches(text).Select(m => m.Groups[1].Value)];

    /// <summary>
    ///     Returns the lines between two marker lines of the sample source.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="begin">The begin marker.</param>
    /// <param name="end">The end marker.</param>
    /// <returns>The lines between the markers.</returns>
    public static string Between(string text, string begin, string end)
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
    /// <param name="text">The text.</param>
    /// <returns>The text with the indentation removed.</returns>
    public static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var indents = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).ToList();
        Assert.True(indents.Count > 0, "The sample block is empty: it has no non-blank line to compare.");
        var indent = indents.Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    /// <summary>
    ///     Normalizes line endings and trailing white space, so only the content is compared.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd());
        return Trailing().Replace(string.Join('\n', lines), string.Empty);
    }

    [GeneratedRegex(@"\s+$")]
    private static partial Regex Trailing();

    /// <summary>
    ///     Asserts that the output of a README sample equals the documented output, with the confidence column compared
    ///     within a tolerance and every other column exactly.
    /// </summary>
    /// <remarks>
    ///     Each line is <c>word pos confidence [respelling]</c> separated by spaces. The confidences of an encoder model
    ///     come from an int8 ONNX graph whose output differs slightly between CPUs and operating systems, so they are
    ///     approximate; the word, the part of speech and the respelling are decisions and must match exactly.
    /// </remarks>
    /// <param name="documented">The output block of the README, the single source of truth.</param>
    /// <param name="actual">The output the sample wrote.</param>
    /// <param name="tolerance">The largest allowed absolute difference of a confidence.</param>
    public static void AssertSampleOutput(string documented, string actual, decimal tolerance)
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
            var expectedConfidence = decimal.Parse(expected[2], System.Globalization.CultureInfo.InvariantCulture);
            var foundConfidence = decimal.Parse(found[2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(expectedConfidence - foundConfidence) <= tolerance,
                $"Line {i + 1}: the confidence {found[2]} of '{found[0]}' differs from the documented {expected[2]} by more than {tolerance}.");
        }
    }

    [GeneratedRegex(@"<!-- sample:([a-z0-9-]+) -->")]
    private static partial Regex Marker();
}
