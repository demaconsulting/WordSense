using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The bert-base-cased (int8) encoder as a word-sense model.
/// </summary>
/// <remarks>
///     <para>
///     The weights trained for this encoder are embedded in the assembly. The encoder files (<c>model_int8.onnx</c>,
///     about 108 MB, <c>vocab.txt</c>, <c>config.json</c> and <c>tokenizer.json</c>) are downloaded once by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>,
///     verified against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use
///     files you supply, in which case no network access happens at all. The files are described under
///     <c>model/bert-base-cased-int8/</c> in the repository
///     (https://github.com/demaconsulting/WordSense/tree/main/model/bert-base-cased-int8).
///     </para>
///     <para>
///     The encoder keeps the case of the text, so capitalization is part of the context the model sees. Sentences longer
///     than the maximum sequence length are handled per target word from a window around the word. The weights were
///     trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var model = new BertBaseCasedWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public sealed class BertBaseCasedWordSenseModel : OnnxWordSenseModel
{
    private static readonly ModelSpec Definition = new(
        "bert-base-cased-int8",
        [
            new ModelFile("model_int8.onnx", "dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d"),
            new ModelFile("vocab.txt", "eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02"),
            new ModelFile("config.json", "19aa23b85dfc4ce3e199a4ab06d51acd4b7bfc9ae9bae62b78823dd5dfb0c286"),
            new ModelFile("tokenizer.json", "343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1"),
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
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public BertBaseCasedWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
