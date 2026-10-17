using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The bert-base-cased (int8) encoder as a word-sense model.
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
///     Choose BertBaseCased when accuracy matters most and the larger download is acceptable: 104 MiB (about 109 MB)
///     and about 9 ms per sentence, with about 99% best-sense accuracy on the holdout and about 97% on real text, and it
///     keeps the case of the text. The alternative <c>DemaConsulting.WordSense.MiniLM</c> is small and fast (23 MiB,
///     about 2 ms per sentence) but less accurate (about 97% and 93%); the figures are from
///     <c>docs/build_notes/model-table.md</c> and are approximate and machine dependent.
///     </para>
///     <para>
///     The weights trained for this encoder are embedded in the assembly. The encoder files (<c>model_int8.onnx</c>,
///     about 103 MiB, <c>vocab.txt</c>, <c>config.json</c> and <c>tokenizer.json</c>) are downloaded once by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>,
///     verified against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use
///     files you supply, in which case no network access happens at all. The files are described under
///     <c>model/bert-base-cased-int8/</c> in the repository
///     (https://github.com/demaconsulting/WordSense/tree/main/model/bert-base-cased-int8).
///     </para>
///     <para>
///     A supplied model directory must hold all four files directly (no <c>{model-id}</c> subfolder), and
///     <c>EnsureModelAsync</c> must still be called: it verifies every one of them, not only the ONNX file, against the
///     pinned SHA-256 digests listed in the namespace documentation, with no network access. Dispose the model once
///     after every classification has finished.
///     </para>
///     <para>
///     The encoder keeps the case of the text, so capitalization is part of the context the model sees. Sentences longer
///     than the maximum sequence length are handled per target word from a window around the word. The weights were
///     trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     // Download on first use (or reuse the per-user store); pass a directory instead to work offline:
///     // new BertBaseCasedWordSenseModel("/opt/models/bert")
///     using var model = new BertBaseCasedWordSenseModel();
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
public sealed class BertBaseCasedWordSenseModel : OnnxWordSenseModel
{
    private static readonly ModelSpec Definition = new(
        "bert-base-cased-int8",
        [
            new ModelFile("model_int8.onnx", "dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d", 108477896),
            new ModelFile("vocab.txt", "eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02", 213450),
            new ModelFile("config.json", "19aa23b85dfc4ce3e199a4ab06d51acd4b7bfc9ae9bae62b78823dd5dfb0c286", 639),
            new ModelFile("tokenizer.json", "343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1", 668923),
        ],
        "model_int8.onnx",
        "vocab.txt",
        false,
        typeof(BertBaseCasedWordSenseModel).Assembly,
        "DemaConsulting.WordSense.BertBaseCased.weights.bin",
        768);

    /// <summary>
    ///     Initializes a new instance of the <see cref="BertBaseCasedWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">
    ///     A directory that holds <c>model_int8.onnx</c>, <c>vocab.txt</c>, <c>config.json</c> and <c>tokenizer.json</c>, or null to use the per-user store that
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/> fills.
    /// </param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <remarks>
    ///     The constructor does not touch the file system: a directory that does not exist, a missing file or a file
    ///     with a wrong SHA-256 is reported by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public BertBaseCasedWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
