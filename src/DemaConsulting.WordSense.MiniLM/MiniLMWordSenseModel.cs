using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     Advanced: the all-MiniLM-L6-v2 (int8) encoder as an <see cref="IWordSenseModel"/>: a WordPiece tokenizer and
///     an ONNX Runtime session. Applications normally get one from <see cref="MiniLMWordSense.CreateClassifierAsync"/>
///     and never construct it.
/// </summary>
/// <remarks>
///     The instance owns one inference session, which ONNX Runtime documents as safe for concurrent <c>Run</c> calls,
///     so <see cref="EncodeAsync"/> may be called from many threads at once. The SHA-256 of the model file is computed
///     when the model is opened and is what the classifier compares with its weights table. Texts longer than the
///     maximum sequence length are encoded per target from a window centered on the target. Dispose the model once,
///     after every call has finished; calls made after disposal throw <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class MiniLMWordSenseModel : IWordSenseModel
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly string _outputName;
    private readonly int _maxLength;
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelPath">The path of the ONNX model file.</param>
    /// <param name="vocabularyPath">The path of the vocabulary file that matches the model.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <param name="intraOpThreads">Threads ONNX Runtime may use within one inference; 0 lets it decide.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelPath"/> or <paramref name="vocabularyPath"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512 or <paramref name="intraOpThreads"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the vocabulary lacks a required special token.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model or vocabulary file does not exist.</exception>
    public MiniLMWordSenseModel(string modelPath, string vocabularyPath, int maxSequenceLength = 256, int intraOpThreads = 0)
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

        _tokenizer = WordPieceTokenizer.FromFile(vocabularyPath);
        _maxLength = maxSequenceLength;
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
    public string ModelId => WordSenseModelCatalog.ModelId;

    /// <inheritdoc />
    public string ModelSha256 { get; }

    /// <inheritdoc />
    public int HiddenSize { get; }

    /// <inheritdoc />
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
