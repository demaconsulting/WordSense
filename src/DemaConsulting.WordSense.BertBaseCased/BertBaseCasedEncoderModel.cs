using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The encoder model of bert-base-cased (int8): the identity of the encoder (its pinned files and its embedded
///     weights) on top of the shared <see cref="OnnxWordSenseModel"/>.
/// </summary>
/// <remarks>
///     <see cref="BertBaseCasedWordSenseModel"/> composes this model with the Basic fallback model and is the public
///     model of the package; this class is internal.
/// </remarks>
internal sealed class BertBaseCasedEncoderModel : OnnxWordSenseModel
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
        typeof(BertBaseCasedEncoderModel).Assembly,
        "DemaConsulting.WordSense.BertBaseCased.weights.bin",
        768,
        "DemaConsulting.WordSense.BertBaseCased.head.bin",
        HeadInUse: true);

    /// <summary>
    ///     Initializes a new instance of the <see cref="BertBaseCasedEncoderModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">A directory that holds the model files, or null to use the per-user store.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public BertBaseCasedEncoderModel(string? modelDirectory, int maxSequenceLength)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
