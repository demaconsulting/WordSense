using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The all-MiniLM-L6-v2 (int8) encoder as a word-sense model.
/// </summary>
/// <remarks>
///     <para>
///     This model is built on <c>DemaConsulting.WordSense.Onnx</c>: it derives from <see cref="OnnxWordSenseModel"/>,
///     which provides the tokenizer, the ONNX session, the verified downloader and the classifier, and adds only the
///     identity of this encoder (its pinned files and its embedded weights). Everything described for
///     <see cref="OnnxWordSenseModel"/> applies: <c>EnsureModelAsync</c> (mirror precedence, verification, retry and
///     cancellation behavior), <c>Create</c>, the confidence of a result, and the requirement to dispose the model once
///     after all classifications have finished.
///     </para>
///     <para>
///     The weights trained for this encoder are embedded in the assembly. The encoder files (<c>model_int8.onnx</c>, about
///     23 MB, and <c>vocab.txt</c>) are downloaded once by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>, verified
///     against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use files you
///     supply, in which case no network access happens at all. The files are described under
///     <c>model/all-MiniLM-L6-v2-int8/</c> in the repository
///     (https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8).
///     </para>
///     <para>
///     Choose MiniLM when download size and speed matter: about 23 MB and about 1.6 ms per sentence, with 96.9 percent
///     best-sense accuracy on the holdout and 92.6 percent on real text. The alternative
///     <c>DemaConsulting.WordSense.BertBaseCased</c> is larger (about 109 MB, about 9.9 ms per sentence) and case-aware
///     but more accurate (99.1 and 97.4 percent); the figures are from <c>docs/build_notes/model-comparison.md</c>.
///     </para>
///     <para>
///     Sentences longer than the maximum sequence length are handled per target word from a window around the word. The
///     weights were trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var model = new MiniLMWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
public sealed class MiniLMWordSenseModel : OnnxWordSenseModel
{
    private static readonly ModelSpec Definition = new(
        "all-MiniLM-L6-v2-int8",
        [
            new ModelFile("model_int8.onnx", "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1", 22972370),
            new ModelFile("vocab.txt", "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3", 231508),
        ],
        "model_int8.onnx",
        "vocab.txt",
        true,
        typeof(MiniLMWordSenseModel).Assembly,
        "DemaConsulting.WordSense.MiniLM.weights.bin",
        384);

    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">
    ///     A directory that holds <c>model_int8.onnx</c> and <c>vocab.txt</c>, or null to use the per-user store that
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/> fills.
    /// </param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <remarks>
    ///     The constructor does not touch the file system: a directory that does not exist, a missing file or a file
    ///     with a wrong SHA-256 is reported by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public MiniLMWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
