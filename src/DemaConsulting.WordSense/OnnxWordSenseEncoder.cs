using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: runs the all-MiniLM-L6-v2 ONNX encoder with ONNX Runtime. Applications normally get one
///     from <see cref="WordSenseClassifier.CreateAsync"/> and never construct it.
/// </summary>
/// <remarks>
///     The instance owns one inference session, which ONNX Runtime documents as safe for concurrent
///     <c>Run</c> calls, so <see cref="EncodeAsync"/> may be called from many threads at once and a single encoder
///     may serve many threads. The SHA-256 of the model file is computed when the encoder is opened and is what the
///     classifier compares with the table. Dispose the encoder once, after every call has finished; calls made after
///     disposal throw <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class OnnxWordSenseEncoder : IWordSenseEncoder
{
    private readonly InferenceSession _session;
    private readonly string _outputName;
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxWordSenseEncoder"/> class.
    /// </summary>
    /// <param name="modelPath">The path of the ONNX model file.</param>
    /// <param name="intraOpThreads">Threads ONNX Runtime may use within one inference; 0 lets it decide.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelPath"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="intraOpThreads"/> is negative.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model file does not exist.</exception>
    public OnnxWordSenseEncoder(string modelPath, int intraOpThreads = 0)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentOutOfRangeException.ThrowIfNegative(intraOpThreads);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The ONNX model file was not found.", modelPath);
        }

        ModelSha256 = WordSenseModelCatalog.HashFile(modelPath);

        using var options = new SessionOptions();
        if (intraOpThreads > 0)
        {
            options.IntraOpNumThreads = intraOpThreads;
            options.InterOpNumThreads = 1;
        }

        _session = new InferenceSession(modelPath, options);
        try
        {
            _outputName = _session.OutputMetadata.Keys.First();
            HiddenSize = _session.OutputMetadata[_outputName].Dimensions[^1];
        }
        catch
        {
            // Do not leak the native session when the model shape cannot be determined
            _session.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public string ModelSha256 { get; }

    /// <inheritdoc />
    public int HiddenSize { get; }

    /// <summary>
    ///     Encodes one sequence of WordPiece ids; safe to call concurrently from several threads.
    /// </summary>
    /// <param name="inputIds">The WordPiece ids, including the special tokens; at least one.</param>
    /// <param name="cancellationToken">A token that cancels the call before inference starts.</param>
    /// <returns>The hidden states, row-major, <c>inputIds.Length × HiddenSize</c> values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputIds"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="inputIds"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the encoder has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public ValueTask<float[]> EncodeAsync(int[] inputIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputIds);
        if (inputIds.Length == 0)
        {
            throw new ArgumentException("At least one input id is required.", nameof(inputIds));
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Build the three int64 inputs of a BERT-style model for a single unpadded sequence
        var length = inputIds.Length;
        var ids = new DenseTensor<long>([1, length]);
        var mask = new DenseTensor<long>([1, length]);
        var types = new DenseTensor<long>([1, length]);
        for (var i = 0; i < length; i++)
        {
            ids[0, i] = inputIds[i];
            mask[0, i] = 1;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types),
        };

        using var results = _session.Run(inputs, [_outputName]);
        var output = results[0].AsTensor<float>();
        return ValueTask.FromResult(output.ToArray());
    }

    /// <summary>
    ///     Releases the ONNX inference session. Calling it more than once is harmless; after the first call
    ///     <see cref="EncodeAsync"/> throws <see cref="ObjectDisposedException"/>. Do not dispose while an
    ///     <see cref="EncodeAsync"/> call is still running.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.Dispose();
        }
    }
}
