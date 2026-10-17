using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     A BERT-style ONNX encoder with its WordPiece tokenizer: turns the words of a sentence into one pooled hidden-state
///     vector per target word.
/// </summary>
/// <remarks>
///     The instance owns one inference session, which ONNX Runtime documents as safe for concurrent <c>Run</c> calls, so
///     <see cref="EncodeAsync"/> may be called from many threads at once. A text longer than the maximum sequence length
///     is encoded per target from a window centered on the target. Dispose the encoder once, after every call has
///     finished; calls made after disposal throw <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class OnnxEncoder : IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly string _outputName;
    private readonly int _maxLength;
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxEncoder"/> class.
    /// </summary>
    /// <param name="modelPath">The path of the ONNX model file.</param>
    /// <param name="vocabularyPath">The path of the vocabulary file that matches the model.</param>
    /// <param name="lowerCase">True when the model is uncased.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <param name="intraOpThreads">Threads ONNX Runtime may use within one inference; 0 lets it decide.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is outside its range.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model or vocabulary file does not exist.</exception>
    public OnnxEncoder(string modelPath, string vocabularyPath, bool lowerCase, int maxSequenceLength, int intraOpThreads)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(vocabularyPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSequenceLength, 512);
        ArgumentOutOfRangeException.ThrowIfNegative(intraOpThreads);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The ONNX model file was not found.", modelPath);
        }

        _tokenizer = WordPieceTokenizer.FromFile(vocabularyPath, lowerCase);
        _maxLength = maxSequenceLength;

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

    /// <summary>
    ///     Gets the size of one hidden-state vector.
    /// </summary>
    public int HiddenSize { get; }

    /// <summary>
    ///     Encodes a sentence and pools the hidden states of each target word.
    /// </summary>
    /// <param name="words">The words of the sentence.</param>
    /// <param name="targets">The indexes into <paramref name="words"/> to pool.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One mean-pooled vector per target, or null for a target without tokens.</returns>
    public ValueTask<double[]?[]> EncodeAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var target in targets)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(target);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(target, words.Count);
        }

        var result = new double[]?[targets.Count];
        if (targets.Count == 0)
        {
            return ValueTask.FromResult(result);
        }

        // A text that fits one window is encoded once; a longer one is encoded per target from a centered window
        var tokens = _tokenizer.TokenizeWords(words);
        var whole = tokens.Count + 2 <= _maxLength ? Run(tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, 0, 0).InputIds) : null;
        for (var t = 0; t < targets.Count; t++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = tokens.WordFirst[targets[t]];
            var last = tokens.WordEnd[targets[t]];
            if (last <= first)
            {
                continue;
            }

            if (whole is not null)
            {
                result[t] = Pool(whole, first + 1, last + 1);
            }
            else
            {
                var window = tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, first, last);
                result[t] = Pool(Run(window.InputIds), window.TargetFirst, window.TargetEnd);
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    ///     Releases the ONNX inference session. Calling it more than once is harmless.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.Dispose();
        }
    }

    /// <summary>
    ///     Averages the hidden states of the rows <c>[first, end)</c> without centering or scaling.
    /// </summary>
    /// <param name="hidden">Row-major hidden states, one row of the hidden size per token.</param>
    /// <param name="first">First target row.</param>
    /// <param name="end">Exclusive end target row.</param>
    /// <returns>The pooled vector, or null when the range is empty or outside the sequence.</returns>
    private double[]? Pool(float[] hidden, int first, int end)
    {
        var dimension = HiddenSize;
        var rows = hidden.Length / dimension;
        if (first < 0 || end > rows || end <= first)
        {
            return null;
        }

        // Average in double so the result does not depend on summation-order effects
        var sums = new double[dimension];
        for (var row = first; row < end; row++)
        {
            for (var d = 0; d < dimension; d++)
            {
                sums[d] += hidden[(row * dimension) + d];
            }
        }

        var count = end - first;
        for (var d = 0; d < dimension; d++)
        {
            sums[d] /= count;
        }

        return sums;
    }

    /// <summary>
    ///     Encodes one sequence of WordPiece ids.
    /// </summary>
    /// <param name="inputIds">The WordPiece ids, including the special tokens; at least one.</param>
    /// <returns>The hidden states, row-major, <c>inputIds.Length × HiddenSize</c> values.</returns>
    private float[] Run(int[] inputIds)
    {
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
        return results[0].AsTensor<float>().ToArray();
    }
}
