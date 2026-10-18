using System.Text;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     The code of the README usage samples, compiled and run by <see cref="ReadmeSampleTests"/>.
/// </summary>
/// <remarks>
///     The README code block (marked <c>&lt;!-- sample:readme --&gt;</c>) must equal the using directives, a blank
///     line and the text between the <c>&lt;sample:readme&gt;</c> markers, and the lines the sample writes for each
///     model must equal the text blocks marked <c>&lt;!-- output:basic --&gt;</c>, <c>&lt;!-- output:minilm --&gt;</c>
///     and <c>&lt;!-- output:bertbasecased --&gt;</c>. The member named <c>Console</c> only captures the lines, so the
///     sample reads like ordinary console code. The nested <c>BasicWordSenseModel</c> stands in for the model that
///     the sample creates: it wraps the Basic, MiniLM or BertBaseCased model chosen by the run, and the encoder models
///     use the committed model directory, so that the test needs no download.
/// </remarks>
internal sealed class ReadmeSample
{
    /// <summary>
    ///     The model the stand-in class wraps in the current run.
    /// </summary>
    private static readonly AsyncLocal<string> CurrentModel = new();

    private readonly StringBuilder _output = new();

    private Capture Console => new(_output);

    /// <summary>
    ///     Gets the lines the sample wrote.
    /// </summary>
    public string Output => _output.ToString();

    /// <summary>
    ///     The README sample. It runs against the Basic model or against the committed model of an encoder.
    /// </summary>
    /// <param name="modelId">The model to run: <c>basic</c>, <c>minilm</c> or <c>bertbasecased</c>.</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public async Task RunAsync(string modelId)
    {
        CurrentModel.Value = modelId;

        // The sample shows the alternative model lines as comments on purpose
#pragma warning disable S125
        // <sample:readme>
        // Basic needs no download; MiniLM and BertBaseCased need their package and using, and download on first use
        using var model = new BasicWordSenseModel();
        // using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
        // using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;
        await model.EnsureModelAsync();

        // Classify the words of a sentence whose "bow" and "record" are heteronyms
        var words = "The hunter fired an arrow from his bow at a deer so we will record a record".Split(' ');
        var results = await model.ClassifyAsync(words);

        // Print the part of speech (or unknown), the confidence and, for a heteronym, the sense id
        foreach (var result in results)
        {
            var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
            var say = result.Details?.SenseId ?? "";
            Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}  {say}".TrimEnd());
        }

        // </sample:readme>
#pragma warning restore S125
    }

    /// <summary>
    ///     Stands in for the model class of the sample, so that the sample runs against every model without a
    ///     download.
    /// </summary>
    private sealed class BasicWordSenseModel : IWordSenseModel
    {
        private readonly IWordSenseModel _model = CurrentModel.Value switch
        {
            "basic" => new DemaConsulting.WordSense.Basic.BasicWordSenseModel(),
            "minilm" => new DemaConsulting.WordSense.MiniLM.MiniLMWordSenseModel(
                DocSampleText.RequireModelDirectory("all-MiniLM-L6-v2-int8")),
            "bertbasecased" => new DemaConsulting.WordSense.BertBaseCased.BertBaseCasedWordSenseModel(
                DocSampleText.RequireModelDirectory("bert-base-cased-int8")),
            _ => throw new InvalidOperationException("Unknown model for the README sample."),
        };

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
