using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The encoder model of all-MiniLM-L6-v2 (int8): the identity of the encoder (its pinned files and its embedded
///     weights) on top of the shared <see cref="OnnxWordSenseModel"/>.
/// </summary>
/// <remarks>
///     <see cref="MiniLMWordSenseModel"/> composes this model with the Basic fallback model and is the public model of
///     the package; this class is internal.
/// </remarks>
internal sealed class MiniLMEncoderModel : OnnxWordSenseModel
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
        typeof(MiniLMEncoderModel).Assembly,
        "DemaConsulting.WordSense.MiniLM.weights.bin",
        384,
        "DemaConsulting.WordSense.MiniLM.head.bin");

    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMEncoderModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">A directory that holds the model files, or null to use the per-user store.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public MiniLMEncoderModel(string? modelDirectory, int maxSequenceLength)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
