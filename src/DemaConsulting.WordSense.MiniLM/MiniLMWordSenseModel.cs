using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The all-MiniLM-L6-v2 (int8) encoder as a word-sense model.
/// </summary>
/// <remarks>
///     <para>
///     The weights trained for this encoder are embedded in the assembly. The encoder files (<c>model_int8.onnx</c>, about
///     23 MB, and <c>vocab.txt</c>) are downloaded once by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>, verified
///     against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use files you
///     supply, in which case no network access happens at all. The files are described under
///     <c>model/all-MiniLM-L6-v2-int8/</c> in the repository
///     (https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8).
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
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public sealed class MiniLMWordSenseModel : OnnxWordSenseModel
{
    private static readonly ModelSpec Definition = new(
        "all-MiniLM-L6-v2-int8",
        [
            new ModelFile("model_int8.onnx", "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1"),
            new ModelFile("vocab.txt", "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"),
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
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public MiniLMWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
