using System.Text;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     The code of the Basic README usage sample, compiled and run by <see cref="BasicReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The README code block marked <c>&lt;!-- sample:basic --&gt;</c> must equal the using directives, a blank line
///     and the text between the <c>&lt;sample:basic&gt;</c> markers, and the lines the sample writes must equal the
///     text block marked <c>&lt;!-- output:basic --&gt;</c>. The member named <c>Console</c> only captures the lines,
///     so the sample reads like ordinary console code.
/// </remarks>
internal sealed class BasicReadmeSample
{
    private readonly StringBuilder _output = new();

    private Capture Console => new(_output);

    /// <summary>
    ///     Gets the lines the sample wrote.
    /// </summary>
    public string Output => _output.ToString();

    /// <summary>
    ///     The Basic README sample. It needs no model files and no network access.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task RunAsync()
    {
        // <sample:basic>
        // The basic model uses the embedded dictionary and a small statistical table, so there is nothing to download
        var model = new BasicWordSenseModel();
        await model.EnsureModelAsync();

        var words = "They will record the record".Split(' ');
        var results = await model.Create().ClassifyAsync(words);

        // A word the model cannot decide has no details and a confidence of 0
        foreach (var result in results)
        {
            var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
            Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}");
        }

        // </sample:basic>
    }

    /// <summary>
    ///     Captures the lines written by the sample.
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
