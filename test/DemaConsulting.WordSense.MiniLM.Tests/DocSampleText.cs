using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.Onnx;
using DemaConsulting.WordSense.Onnx.Tests;

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
            if (File.Exists(Path.Join(dir.FullName, "WordSense.slnx")))
            {
                return dir.FullName;
            }
        }

        ModelFileCheck.Unavailable("The repository root was not found.");
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
        var directory = Path.Join(FindRepositoryRoot(), "model", modelId);
        IWordSenseModel model = modelId switch
        {
            "all-MiniLM-L6-v2-int8" => new MiniLMWordSenseModel(directory),
            "bert-base-cased-int8" => new BertBaseCasedWordSenseModel(directory),
            _ => throw new ArgumentException($"Unknown model '{modelId}'.", nameof(modelId)),
        };
        using (model)
        {
            var encoder = model switch
            {
                MiniLMWordSenseModel minilm => minilm.Encoder,
                BertBaseCasedWordSenseModel bert => bert.Encoder,
                _ => throw new ArgumentException($"Unknown model '{modelId}'.", nameof(modelId)),
            };
            return ModelFileCheck.Require(encoder.Spec, directory);
        }
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

    /// <summary>
    ///     Matches the white space at the end of a text.
    /// </summary>
    /// <returns>The generated regular expression.</returns>
    [GeneratedRegex(@"\s+$")]
    private static partial Regex Trailing();

    /// <summary>
    ///     Asserts that the output of a README sample equals the documented output, with the confidence column compared
    ///     within a tolerance and every other column exactly.
    /// </summary>
    /// <remarks>
    ///     Each line is <c>word pos level confidence [sense]</c> separated by spaces. The confidences of an
    ///     encoder model come from an int8 ONNX graph whose output differs slightly between CPUs and operating systems,
    ///     so they are approximate. The word and
    ///     the part of speech are decisions and must match exactly. The level is a decision against a per-lemma
    ///     threshold, so a word whose margin lies close to its threshold may be answered one step more or less
    ///     specifically on another CPU: a documented sense may come out as its own group or at the part-of-speech level,
    ///     and a documented group as one of its senses or at the part-of-speech level. Such a line is accepted only when
    ///     the answers are consistent (same part of speech, the sense belongs to the group); its confidence
    ///     then refers to a different level and is not compared. Any other difference fails.
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
            Assert.True(expected.Length >= 4 && found.Length >= 4, $"Line {i + 1}: expected '{expectedLines[i]}' but found '{actualLines[i]}'.");

            // The word and the part of speech are decisions and must match exactly
            Assert.Equal(expected[0], found[0]);
            Assert.Equal(expected[1], found[1]);

            // A level one step away is accepted when the answers are consistent; the rest of the line then refers to
            // a different level and is not compared
            if (expected[2] != found[2])
            {
                Assert.True(
                    LevelsAreConsistent(expected[2], IdentifierOf(expected), found[2], IdentifierOf(found)),
                    $"Line {i + 1}: '{found[0]}' was answered at level {found[2]} ({IdentifierOf(found) ?? "no id"}), "
                    + $"which is not consistent with the documented level {expected[2]} ({IdentifierOf(expected) ?? "no id"}).");
                continue;
            }

            // Same level: the sense and the runner-up exactly except their numbers, which, like the confidence in the
            // fourth column, are probabilities that move slightly between CPUs
            Assert.True(found.Length == expected.Length, $"Line {i + 1}: expected '{expectedLines[i]}' but found '{actualLines[i]}'.");
            for (var c = 4; c < expected.Length; c++)
            {
                if (TryParseProbability(expected[c], out var documentedValue) && TryParseProbability(found[c], out var foundValue))
                {
                    Assert.True(
                        Math.Abs(documentedValue - foundValue) <= tolerance,
                        $"Line {i + 1}: the number {found[c]} in column {c + 1} differs from the documented {expected[c]} by more than {tolerance}.");
                }
                else
                {
                    Assert.Equal(expected[c], found[c]);
                }
            }

            var expectedConfidence = decimal.Parse(expected[3], System.Globalization.CultureInfo.InvariantCulture);
            var foundConfidence = decimal.Parse(found[3], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(expectedConfidence - foundConfidence) <= tolerance,
                $"Line {i + 1}: the confidence {found[3]} of '{found[0]}' differs from the documented {expected[3]} by more than {tolerance}.");
        }
    }

    /// <summary>
    ///     Finds the sense or group identifier of a sample line: the first column after the confidence that contains
    ///     the <c>#</c> of a <c>lemma#id</c> identifier.
    /// </summary>
    /// <param name="columns">The columns of the line.</param>
    /// <returns>The identifier, or <c>null</c> when the line answers at the part-of-speech level.</returns>
    private static string? IdentifierOf(string[] columns) =>
        columns.Skip(4).FirstOrDefault(c => c.Contains('#', StringComparison.Ordinal));

    /// <summary>
    ///     Decides whether two answers of the same word and part of speech at different levels are consistent, i.e.
    ///     one is a less specific form of the other.
    /// </summary>
    /// <param name="levelA">The level of the first answer.</param>
    /// <param name="idA">The sense or group identifier of the first answer, or <c>null</c>.</param>
    /// <param name="levelB">The level of the second answer.</param>
    /// <param name="idB">The sense or group identifier of the second answer, or <c>null</c>.</param>
    /// <returns><c>true</c> when the answers are consistent; otherwise <c>false</c>.</returns>
    private static bool LevelsAreConsistent(string levelA, string? idA, string levelB, string? idB)
    {
        // Either answer at the part-of-speech level is consistent, because the parts of speech were already compared
        if (levelA == nameof(SenseLevel.PartOfSpeech) || levelB == nameof(SenseLevel.PartOfSpeech))
        {
            return true;
        }

        // A sense and a group are consistent when the sense belongs to the group
        var (senseId, groupId) = levelA == nameof(SenseLevel.Sense) ? (idA, idB) : (idB, idA);
        return senseId is not null
            && groupId is not null
            && WordLexicon.TryGetGroup(groupId, out var group)
            && group.SenseIds.Contains(senseId);
    }

    /// <summary>
    ///     Parses a column of a sample line as a probability, so that numeric columns are compared within the tolerance.
    /// </summary>
    /// <param name="text">The column text.</param>
    /// <param name="value">The parsed value when the column is a number.</param>
    /// <returns><c>true</c> when the column is a decimal number; otherwise <c>false</c>.</returns>
    private static bool TryParseProbability(string text, out decimal value) =>
        decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>
    ///     Matches a sample marker of the README and captures the name of the sample.
    /// </summary>
    /// <returns>The generated regular expression.</returns>
    [GeneratedRegex(@"<!-- sample:([a-z0-9-]+) -->")]
    private static partial Regex Marker();
}
