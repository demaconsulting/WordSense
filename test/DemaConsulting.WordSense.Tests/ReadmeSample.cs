using System.Text;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     The code of the README usage sample, compiled and run by <see cref="ReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The README code block (marked <c>&lt;!-- sample:readme --&gt;</c>) must equal
///     <c>using DemaConsulting.WordSense;</c>, a blank line and the text between the
///     <c>&lt;sample:readme&gt;</c> and <c>&lt;/sample:readme&gt;</c> markers, and the lines the sample writes must
///     equal the text block marked <c>&lt;!-- output:readme --&gt;</c>. The member named <c>Console</c> only
///     captures the lines, so the sample reads like ordinary console code. The nested
///     <c>WordSenseModelDownloader</c> stands in for the real downloader so that the test needs no download: it
///     returns the committed model directory.
/// </remarks>
internal sealed class ReadmeSample
{
    private readonly StringBuilder _output = new();

    private Capture Console => new(_output);

    /// <summary>
    ///     Gets the lines the sample wrote.
    /// </summary>
    public string Output => _output.ToString();

    /// <summary>
    ///     The README sample. It runs against the committed model through the stand-in downloader.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task RunAsync()
    {
        // <sample:readme>
        // Ensure the model is present, downloading and verifying it on first use
        WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync();

        // Create a classifier that runs the model from that location
        using var classifier = await WordSenseClassifier.CreateAsync(
            new WordSenseClassifierOptions { ModelDirectory = location.Directory });

        // Classify the words of a sentence whose "record", "read" and "bow" are heteronyms
        var words = "The hunter fired an arrow from his bow so I will record a record and read the book yesterday".Split(' ');
        var results = await classifier.ClassifyAsync(words);

        // Print the part of speech (or the candidates, or unknown), where the answer comes from, the margin and,
        // for a heteronym, the meaning and the respelling
        foreach (var a in results)
        {
            var pos = a.Details?.PartOfSpeech.ToString()
                ?? (a.Candidates.Count > 0
                    ? string.Join("/", a.Candidates.Select(c => c.PartOfSpeech.ToString()[..3]).Distinct())
                    : "unknown");
            var say = a.Details?.Pronunciation is { } p ? $"{p.Meaning} {p.Respelling}" : "";
            Console.WriteLine($"{a.Word,-9} {pos,-24} {a.Basis,-10} {a.Margin,4:F1}  {say}".TrimEnd());
        }

        // </sample:readme>
    }

    /// <summary>
    ///     Stands in for the real downloader, so that the sample runs without a download.
    /// </summary>
    private static class WordSenseModelDownloader
    {
        /// <summary>
        ///     Returns the committed model directory.
        /// </summary>
        /// <returns>The model location.</returns>
        public static Task<WordSenseModelLocation> EnsureModelAsync() =>
            Task.FromResult(WordSenseModelLocation.FromDirectory(ModelAssets.RequireModelDirectory()));
    }

    /// <summary>
    ///     Captures the lines written by a sample.
    /// </summary>
    /// <param name="Buffer">The buffer that receives the lines.</param>
    private readonly record struct Capture(StringBuilder Buffer)
    {
        /// <summary>
        ///     Writes a line, ending it with a line feed.
        /// </summary>
        /// <param name="line">The text.</param>
        public void WriteLine(string line) => Buffer.Append(line).Append('\n');
    }
}
