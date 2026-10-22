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
        // Basic is part of the core package and needs no download; MiniLM and BertBaseCased need their package and using,
        // and download on first use
        using var model = new BasicWordSenseModel();
        // using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
        // using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;

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
    ///     The README sample that fetches and verifies the model files up front. It is compiled but never run, because
    ///     it would use the network; the README code block must equal it (see <see cref="ReadmeSampleTests"/>).
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    internal static async Task DownloadAsync()
    {
        // <sample:download>
        // The concrete model type has the method: call it directly. It is optional, because ClassifyAsync ensures the
        // files lazily; use it to control when the download happens (startup, before going offline) and to handle
        // failures up front (ModelUnavailableException, ModelIntegrityException, DownloadConfigurationException)
        using var model = new MiniLMWordSenseModel();
        await model.EnsureModelAsync();

        // Through a mirror, when the public download host is blocked
        await model.EnsureModelAsync(new DownloadOptions(new Uri("https://models.example.com/wordsense")));
        // </sample:download>
    }

    /// <summary>
    ///     The README sample for code that holds only an <see cref="IWordSenseModel"/>. It is compiled but never run,
    ///     because it would use the network; the README code block must equal it (see <see cref="ReadmeSampleTests"/>).
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    internal static async Task CapabilityAsync()
    {
        // <sample:capability>
        // Code that holds only an IWordSenseModel (Basic, MiniLM, BertBaseCased or your own) asks for the capability:
        // Basic has no files and does not offer EnsureModelAsync
        using IWordSenseModel model = new MiniLMWordSenseModel();
        if (model is IDownloadableWordSenseModel downloadable)
        {
            await downloadable.EnsureModelAsync();
        }
        // </sample:capability>
    }

    /// <summary>
    ///     Stands in for the model class of the sample, so that the sample runs against every model without a
    ///     download.
    /// </summary>
    private sealed class BasicWordSenseModel : IWordSenseModel
    {
        private readonly IWordSenseModel _model = CurrentModel.Value switch
        {
            "basic" => new DemaConsulting.WordSense.BasicWordSenseModel(),
            "minilm" => new DemaConsulting.WordSense.MiniLM.MiniLMWordSenseModel(
                DocSampleText.RequireModelDirectory("all-MiniLM-L6-v2-int8")),
            "bertbasecased" => new DemaConsulting.WordSense.BertBaseCased.BertBaseCasedWordSenseModel(
                DocSampleText.RequireModelDirectory("bert-base-cased-int8")),
            _ => throw new InvalidOperationException("Unknown model for the README sample."),
        };

        /// <inheritdoc />
        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
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
