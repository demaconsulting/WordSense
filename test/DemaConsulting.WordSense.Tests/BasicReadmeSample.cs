using System.Text;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     The code of the Basic README usage sample, compiled and run by <see cref="BasicReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The README code block marked <c>&lt;!-- sample:readme --&gt;</c> must equal the using directives, a blank line
///     and the text between the <c>&lt;sample:readme&gt;</c> markers, and the lines the sample writes must equal the
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
        // The sample shows the alternative model lines as comments on purpose
#pragma warning disable S125
        // <sample:readme>
        // Basic is part of the core package and needs no download; MiniLM and BertBaseCased need their package and using,
        // and download on first use
        using var model = new BasicWordSenseModel();
        // using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
        // using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;
        await model.EnsureModelAsync();

        // Classify the words of a sentence whose "bow" and "record" are heteronyms and whose "go", "back" and "home"
        // are among the trained words of the encoder models
        var words = "The hunter fired an arrow from his bow at a deer so we will record a record then go back home".Split(' ');
        var results = await model.ClassifyAsync(words);

        // Print the part of speech (or unknown), the level, the confidence, the sense id (at the sense level) or the
        // group id (at the group level) and the runner-up part of speech with its probability, when the word has one
        // (the analyses that share a part of speech, such as the singular and the plural of "deer", share its probability)
        foreach (var result in results)
        {
            var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
            var sense = result.SenseId ?? result.GroupId ?? "";
            var next = result.Analyses
                .Where(a => a.Details.PartOfSpeech != result.Details?.PartOfSpeech)
                .GroupBy(a => a.Details.PartOfSpeech)
                .Select(g => (Pos: g.Key, Probability: g.Sum(a => a.Probability)))
                .OrderByDescending(g => g.Probability)
                .Select(g => $"{g.Pos} {g.Probability:F2}")
                .FirstOrDefault("");
            Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Level,-12} {result.Confidence,4:F2}  {sense,-14} {next}".TrimEnd());
        }

        // </sample:readme>
#pragma warning restore S125
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
