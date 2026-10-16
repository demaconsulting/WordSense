using System.Text;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     The code of the README usage samples, compiled and run by <see cref="ReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The text between the <c>&lt;sample:name&gt;</c> and <c>&lt;/sample:name&gt;</c> markers must equal the
///     matching code block of <c>README.md</c> (marked <c>&lt;!-- sample:name --&gt;</c>), and the lines the
///     sample writes must equal the output block marked <c>&lt;!-- output:name --&gt;</c>. The member named
///     <c>Console</c> only captures the lines, so the sample reads like ordinary console code.
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
    ///     The "download" sample. It downloads the model when it is missing, so it is compiled but not run.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task DownloadAsync()
    {
        // <sample:download>
        // Downloads missing files (mirror, Hugging Face, GitHub LFS) and verifies each against its pinned SHA-256
        WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync();
        Console.WriteLine(location.Directory); // holds model_int8.onnx and vocab.txt
        // </sample:download>
    }

    /// <summary>
    ///     The "classify" sample.
    /// </summary>
    /// <param name="location">The verified model location (from the download step of the README).</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task ClassifyAsync(WordSenseModelLocation location)
    {
        // <sample:classify>
        using var classifier = await WordSenseClassifier.CreateAsync(
            new WordSenseClassifierOptions { ModelDirectory = location.Directory });

        string[] words = ["I", "read", "that", "the", "lead", "singer", "will", "bow", "and", "a", "tear", "will",
            "fall", "when", "she", "presents", "the", "present"];
        WordSense?[] senses = await classifier.ClassifyAsync(words); // parallel to words; null = unknown or unsure

        for (var i = 0; i < words.Length; i++)
        {
            Console.WriteLine(senses[i] is { } s
                ? $"{words[i],-8} lemma={s.Lemma} sense={s.Sense} group={s.Group} tags={string.Join(",", s.Tags.Order())}"
                : $"{words[i],-8} -");
        }

        // </sample:classify>
    }

    /// <summary>
    ///     The "tags" sample.
    /// </summary>
    /// <param name="classifier">The classifier created by the classify sample.</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task TagsAsync(WordSenseClassifier classifier)
    {
        // <sample:tags>
        string[] words = ["They", "will", "record", "the", "record"];
        string?[] pos = await classifier.ClassifyTagAsync(words, ["noun", "verb"]); // "noun", "verb" or null
        bool?[] isVerb = await classifier.IsTagAsync(words, "verb");                // true, false or null
        WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(words);   // also for abstained words

        for (var i = 0; i < words.Length; i++)
        {
            var detail = details[i] is { } d ? $"sense={d.Sense.Sense} margin={d.Margin:F2} abstained={d.Abstained}" : "-";
            Console.WriteLine($"{words[i],-8} pos={pos[i] ?? "-"} isVerb={isVerb[i]?.ToString() ?? "-"} {detail}");
        }

        // </sample:tags>
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
