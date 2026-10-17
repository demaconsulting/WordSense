using System.Text;
using DemaConsulting.WordSense.Basic;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     The code of the README usage samples, compiled and run by <see cref="ReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The README code blocks (marked <c>&lt;!-- sample:readme --&gt;</c> and <c>&lt;!-- sample:basic --&gt;</c>) must
///     equal the using directives, a blank line and the text between the matching <c>&lt;sample:...&gt;</c> markers,
///     and the lines a sample writes must equal the text block marked <c>&lt;!-- output:... --&gt;</c>. The member
///     named <c>Console</c> only captures the lines, so the samples read like ordinary console code. The nested
///     <c>MiniLMWordSenseModel</c> stands in for the real model class so that the test needs no download: it uses the
///     committed model directory.
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
    ///     The MiniLM README sample. It runs against the committed model through the stand-in model class.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task RunMiniLMAsync()
    {
        // <sample:readme>
        // Create the model and make sure its files are available (downloaded and verified on first use)
        using var model = new MiniLMWordSenseModel();
        await model.EnsureModelAsync();

        // Classify the words of a sentence whose "bow" and "record" are heteronyms
        var words = "The hunter fired an arrow from his bow so I will record a record".Split(' ');
        var results = await model.ClassifyAsync(words);

        // Print the part of speech (or unknown), the confidence and, for a heteronym, the respelling
        foreach (var result in results)
        {
            var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
            var say = result.Details?.Respelling ?? "";
            Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}  {say}".TrimEnd());
        }

        // </sample:readme>
    }

    /// <summary>
    ///     The Basic README sample. It needs no model files and no network access.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task RunBasicAsync()
    {
        // <sample:basic>
        // The basic model uses the embedded dictionary and a small statistical table, so there is nothing to download
        using var model = new BasicWordSenseModel();
        await model.EnsureModelAsync();

        var words = "They said so and the hunter fired an arrow from his bow at a deer".Split(' ');
        var results = await model.ClassifyAsync(words);

        // Every word of the dictionary has details, the determiners among them;
        // a word that is not in it has none and a confidence of 0
        foreach (var result in results)
        {
            var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
            Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}");
        }

        // </sample:basic>
    }

    /// <summary>
    ///     Stands in for the real model class, so that the sample runs without a download.
    /// </summary>
    private sealed class MiniLMWordSenseModel : IWordSenseModel
    {
        private readonly DemaConsulting.WordSense.MiniLM.MiniLMWordSenseModel _model =
            new(DocSampleText.RequireMiniLMDirectory());

        /// <inheritdoc />
        public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) =>
            _model.EnsureModelAsync(mirror, cancellationToken);

        /// <inheritdoc />
        public Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            _model.ClassifyAsync(words, cancellationToken);

        /// <inheritdoc />
        public void Dispose() => _model.Dispose();
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
