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
///     is encoded per target from a window centered on the target by <see cref="EncodeAsync"/>, and in sliding windows,
///     one pass per window, by <see cref="EncodeAllAsync(IReadOnlyList{string}, CancellationToken)"/>. Dispose the encoder once, after every call has
///     finished; calls made after disposal throw <see cref="ObjectDisposedException"/>.
///     <para>
///     Without sense layers the sense vector of a word is the mean of its word pieces in the last layer. With sense
///     layers (see <see cref="FeatureLayout"/>) it is the mean, over the layers in increasing order, of the per-layer
///     means of the word pieces; the heads still read the last layer (<see cref="EncodeHeadAllAsync"/>).
///     </para>
/// </remarks>
internal sealed class OnnxEncoder : IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly string[] _senseOutputs;
    private readonly string[] _headOutputs;
    private readonly bool _layered;
    private readonly int _maxLength;
    private int _disposed;
    private long _passes;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxEncoder"/> class.
    /// </summary>
    /// <param name="modelPath">The path of the ONNX model file.</param>
    /// <param name="vocabularyPath">The path of the vocabulary file that matches the model.</param>
    /// <param name="lowerCase">True when the model is uncased.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <param name="intraOpThreads">Threads ONNX Runtime may use within one inference; 0 lets it decide.</param>
    /// <param name="senseLayers">The layers, in increasing order, whose mean is the sense vector (see <see cref="ModelSpec.SenseLayers"/>), or null for the last layer.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is outside its range.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="senseLayers"/> is not a valid list of layers.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model or vocabulary file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the model lacks an output of a sense layer or has no fixed hidden size.</exception>
    public OnnxEncoder(string modelPath, string vocabularyPath, bool lowerCase, int maxSequenceLength, int intraOpThreads, IReadOnlyList<int>? senseLayers = null)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(vocabularyPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSequenceLength, 512);
        ArgumentOutOfRangeException.ThrowIfNegative(intraOpThreads);
        if (senseLayers is not null)
        {
            FeatureLayout.Validate(senseLayers);
        }

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
            // The last layer is the output of that name; a model that does not name it has the one output of the graph
            var outputs = _session.OutputMetadata;
            var lastName = outputs.ContainsKey(FeatureLayout.LastOutputName) ? FeatureLayout.LastOutputName : outputs.Keys.First();
            _headOutputs = [lastName];
            HiddenSize = outputs[lastName].Dimensions[^1];
            if (HiddenSize <= 0)
            {
                throw new InvalidDataException("The ONNX model does not have a fixed hidden size.");
            }

            _layered = senseLayers is not null;
            _senseOutputs = senseLayers is null ? _headOutputs : CheckTaps(outputs, FeatureLayout.OutputNames(senseLayers), FeatureLayout.Describe(senseLayers), HiddenSize);
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
    ///     Encodes a sentence and pools the hidden states of each target word into its sense vector.
    /// </summary>
    /// <param name="words">The words of the sentence.</param>
    /// <param name="targets">The indexes into <paramref name="words"/> to pool.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One sense vector per target (the mean-pooled last layer, or the mean of the pooled sense layers), or null for a target without tokens.</returns>
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
        var whole = tokens.Count + 2 <= _maxLength ? Run(tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, 0, 0).InputIds, true) : null;
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
                result[t] = PoolSense(whole, first + 1, last + 1);
            }
            else
            {
                var window = tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, first, last);
                result[t] = PoolSense(Run(window.InputIds, true), window.TargetFirst, window.TargetEnd);
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    ///     Encodes every word of a text, in sliding windows when the text does not fit one window, and pools each word
    ///     from the window in which it is most central.
    /// </summary>
    /// <remarks>
    ///     A text that fits one window is encoded once and the result equals <see cref="EncodeAsync"/> for every word. A
    ///     longer text is cut into windows of the maximum length that start half a window apart (so about
    ///     <c>n / (window / 2)</c> passes for <c>n</c> word pieces, not one pass per word). A word is pooled from a
    ///     window that contains all its pieces and gives it the most context on its closer side; at the start and the
    ///     end of the text the text edge counts as unlimited context, like the sentence edge of a single window. A word
    ///     that straddles the border of two windows of that grid gets a window of its own that starts at the word, so no
    ///     word that fits a window is ever cut. A word of more word pieces than a window holds cannot fit: it gets a
    ///     window that starts at the word and is pooled from its first pieces, as many as the window holds.
    /// </remarks>
    /// <param name="words">The words of the text.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One sense vector per word (see <see cref="EncodeAsync"/>), or null for a word without tokens.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or a word is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the encoder has been disposed.</exception>
    public ValueTask<double[]?[]> EncodeAllAsync(IReadOnlyList<string> words, CancellationToken cancellationToken) =>
        ValueTask.FromResult(EncodeAllCore(words, cancellationToken, true, false).Sense!);

    /// <summary>
    ///     Encodes every word of a text in its possessive view (see <see cref="PossessiveView"/>).
    /// </summary>
    /// <param name="words">The words of the text.</param>
    /// <param name="isPossessive">Tells whether a word is a possessive written as one token; it is split into its stem and its marker.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The tokens of the view, the first token of every word, the last-layer vector of every token and, with sense layers, the sense vector of every token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument or a word is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the encoder has been disposed.</exception>
    public ValueTask<WordVectors> EncodeAllAsync(IReadOnlyList<string> words, Func<string, bool> isPossessive, CancellationToken cancellationToken)
    {
        var (tokens, start) = PossessiveView.Expand(words, isPossessive);
        var (sense, head) = EncodeAllCore(tokens, cancellationToken, _layered, true);
        return ValueTask.FromResult(new WordVectors(tokens, start, head!, _layered ? sense : null));
    }

    /// <summary>
    ///     Encodes every word of a text like <see cref="EncodeAllAsync(IReadOnlyList{string}, CancellationToken)"/> but
    ///     pools the last layer only, which is the vector the part-of-speech and tense heads read.
    /// </summary>
    /// <remarks>
    ///     Without sense layers the result equals the one of <see cref="EncodeAllAsync(IReadOnlyList{string}, CancellationToken)"/>.
    ///     With sense layers the encoder reads only the last-layer output.
    /// </remarks>
    /// <param name="words">The words of the text.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One last-layer vector per word, or null for a word without tokens.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or a word is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the encoder has been disposed.</exception>
    public ValueTask<double[]?[]> EncodeHeadAllAsync(IReadOnlyList<string> words, CancellationToken cancellationToken) =>
        ValueTask.FromResult(EncodeAllCore(words, cancellationToken, false, true).Head!);

    /// <summary>
    ///     Encodes every word of a text in the windows of <see cref="EncodeAllAsync(IReadOnlyList{string}, CancellationToken)"/>
    ///     and pools the vectors that the callers ask for.
    /// </summary>
    /// <param name="words">The words of the text.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <param name="wantSense">True to pool the sense vectors.</param>
    /// <param name="wantHead">True to pool the last-layer vectors. Without sense layers they are the sense vectors, the same array.</param>
    /// <returns>The requested vectors per word; null for the vectors that were not requested.</returns>
    private (double[]?[]? Sense, double[]?[]? Head) EncodeAllCore(IReadOnlyList<string> words, CancellationToken cancellationToken, bool wantSense, bool wantHead)
    {
        ArgumentNullException.ThrowIfNull(words);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var sense = wantSense || !_layered ? new double[]?[words.Count] : null;
        double[]?[]? head = sense;
        if (_layered)
        {
            head = wantHead ? new double[]?[words.Count] : null;
        }
        if (words.Count == 0)
        {
            return (sense, head);
        }

        var tokens = _tokenizer.TokenizeWords(words);
        var capacity = _maxLength - 2;
        if (tokens.Count <= capacity)
        {
            // One window: the same pass and the same pooling as EncodeAsync
            var whole = Run(tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, 0, 0).InputIds, wantSense);
            for (var w = 0; w < words.Count; w++)
            {
                if (tokens.WordEnd[w] > tokens.WordFirst[w])
                {
                    PoolWord(whole, tokens.WordFirst[w] + 1, tokens.WordEnd[w] + 1, sense, head, w);
                }
            }

            return (sense, head);
        }

        // Several windows: plan the windows, assign every word to its best window, then encode each window once and pool its words
        var (starts, windowOf) = PlanWindows(tokens.Count, capacity, tokens.WordFirst, tokens.WordEnd);
        var assigned = new List<int>[starts.Length];
        for (var w = 0; w < words.Count; w++)
        {
            if (windowOf[w] >= 0)
            {
                (assigned[windowOf[w]] ??= []).Add(w);
            }
        }

        for (var i = 0; i < starts.Length; i++)
        {
            if (assigned[i] is not { } members)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var ids = new int[capacity + 2];
            ids[0] = _tokenizer.ClsId;
            Array.Copy(tokens.Ids, starts[i], ids, 1, capacity);
            ids[capacity + 1] = _tokenizer.SepId;
            var hidden = Run(ids, wantSense);
            foreach (var w in members)
            {
                var first = Math.Max(tokens.WordFirst[w] - starts[i], 0) + 1;
                var end = Math.Min(tokens.WordEnd[w] - starts[i], capacity) + 1;
                PoolWord(hidden, first, end, sense, head, w);
            }
        }

        return (sense, head);
    }

    /// <summary>
    ///     Gets the number of encoder passes made so far.
    /// </summary>
    internal long PassCount => Interlocked.Read(ref _passes);

    /// <summary>
    ///     Lists the start tokens of the windows that cover a text longer than one window: windows start half a window
    ///     apart, and the last one ends at the end of the text.
    /// </summary>
    /// <param name="count">The number of tokens of the text; more than <paramref name="capacity"/>.</param>
    /// <param name="capacity">The number of text tokens in a window.</param>
    /// <returns>The start tokens in increasing order.</returns>
    private static int[] WindowStarts(int count, int capacity)
    {
        var stride = Math.Max(1, capacity / 2);
        var starts = new List<int>();
        for (var s = 0; s + capacity < count; s += stride)
        {
            starts.Add(s);
        }

        starts.Add(count - capacity);
        return [.. starts];
    }

    /// <summary>
    ///     Plans the windows that cover a text longer than one window and assigns every word to one of them.
    /// </summary>
    /// <remarks>
    ///     The windows start half a window apart, and the last one ends at the end of the text. A word that fits a window
    ///     (at most <paramref name="capacity"/> word pieces) is always inside the window it is assigned to: when no window
    ///     of the regular grid contains it, because it straddles a window border, a window that starts at the word is
    ///     added. A word longer than a window cannot be contained; a window that starts at the word is added and the word
    ///     is pooled from its first <paramref name="capacity"/> pieces.
    /// </remarks>
    /// <param name="count">The number of tokens of the text; more than <paramref name="capacity"/>.</param>
    /// <param name="capacity">The number of text tokens in a window.</param>
    /// <param name="wordFirst">The first token of every word.</param>
    /// <param name="wordEnd">The exclusive end token of every word; a word without tokens has an end that is not after its first.</param>
    /// <returns>The start tokens of the windows in increasing order, and per word the index of its window or -1 for a word without tokens.</returns>
    internal static (int[] Starts, int[] WindowOf) PlanWindows(int count, int capacity, int[] wordFirst, int[] wordEnd)
    {
        var starts = new List<int>(WindowStarts(count, capacity));
        var windowOf = new int[wordFirst.Length];
        for (var w = 0; w < wordFirst.Length; w++)
        {
            var first = wordFirst[w];
            var end = wordEnd[w];
            if (end > first && (end - first > capacity || IndexOfContainingWindow(starts, capacity, first, end) < 0))
            {
                // A word longer than a window starts its window; a word that fits starts one as far left as the text allows
                var start = Math.Min(first, count - capacity);
                var at = starts.BinarySearch(start);
                if (at < 0)
                {
                    starts.Insert(~at, start);
                }
            }
        }

        for (var w = 0; w < wordFirst.Length; w++)
        {
            if (wordEnd[w] <= wordFirst[w])
            {
                windowOf[w] = -1;
            }
            else if (wordEnd[w] - wordFirst[w] > capacity)
            {
                windowOf[w] = starts.BinarySearch(Math.Min(wordFirst[w], count - capacity));
            }
            else
            {
                windowOf[w] = BestWindow(starts, capacity, count, wordFirst[w], wordEnd[w]);
            }
        }

        return ([.. starts], windowOf);
    }

    /// <summary>
    ///     Finds the first window that contains the tokens <c>[first, end)</c>.
    /// </summary>
    /// <param name="starts">The start tokens of the windows in increasing order.</param>
    /// <param name="capacity">The number of text tokens in a window.</param>
    /// <param name="first">The first token of the word.</param>
    /// <param name="end">The exclusive end token of the word; at most <paramref name="capacity"/> after its first.</param>
    /// <returns>The index of the window, or -1 when there is none.</returns>
    private static int IndexOfContainingWindow(List<int> starts, int capacity, int first, int end)
    {
        // A window contains the word when its start is in [end - capacity, first]
        var at = starts.BinarySearch(end - capacity);
        if (at < 0)
        {
            at = ~at;
        }

        return at < starts.Count && starts[at] <= first ? at : -1;
    }

    /// <summary>
    ///     Chooses, among the windows that contain a word, the one that gives it the most context on its closer side.
    /// </summary>
    /// <param name="starts">The start tokens of the windows in increasing order; one of them contains the word.</param>
    /// <param name="capacity">The number of text tokens in a window.</param>
    /// <param name="count">The number of tokens of the text.</param>
    /// <param name="first">The first token of the word.</param>
    /// <param name="end">The exclusive end token of the word.</param>
    /// <returns>The index of the window; the earliest one on a tie.</returns>
    private static int BestWindow(List<int> starts, int capacity, int count, int first, int end)
    {
        var best = IndexOfContainingWindow(starts, capacity, first, end);
        var bestMargin = int.MinValue;
        for (var i = best; i < starts.Count && starts[i] <= first; i++)
        {
            // Tokens of context before and after the word inside the window; a text edge is unlimited context
            var before = starts[i] == 0 ? int.MaxValue : first - starts[i];
            var after = starts[i] + capacity >= count ? int.MaxValue : starts[i] + capacity - end;
            var margin = Math.Min(before, after);
            if (margin > bestMargin)
            {
                best = i;
                bestMargin = margin;
            }
        }

        return best;
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
    ///     Pools one word for the requested vectors.
    /// </summary>
    /// <param name="hidden">The outputs of one pass (see <see cref="Run"/>).</param>
    /// <param name="first">First target row.</param>
    /// <param name="end">Exclusive end target row.</param>
    /// <param name="sense">Receives the sense vector, or null when it was not requested.</param>
    /// <param name="head">Receives the last-layer vector, or null when it was not requested.</param>
    /// <param name="word">The index of the word in <paramref name="sense"/> and <paramref name="head"/>.</param>
    private void PoolWord(float[][] hidden, int first, int end, double[]?[]? sense, double[]?[]? head, int word)
    {
        if (!_layered)
        {
            // One output: the sense vector and the last-layer vector are the same
            var pooled = Pool(hidden[0], first, end);
            if (sense is not null)
            {
                sense[word] = pooled;
            }

            if (head is not null)
            {
                head[word] = pooled;
            }

            return;
        }

        if (sense is not null)
        {
            sense[word] = PoolSense(hidden, first, end);
        }

        if (head is not null)
        {
            head[word] = Pool(hidden[^1], first, end);
        }
    }

    /// <summary>
    ///     Pools the sense vector: the mean of the pooled vectors of the layers, in increasing order of the layers.
    /// </summary>
    /// <param name="hidden">The outputs of one pass, one per layer.</param>
    /// <param name="first">First target row.</param>
    /// <param name="end">Exclusive end target row.</param>
    /// <returns>The sense vector, or null when the range is empty or outside the sequence.</returns>
    private double[]? PoolSense(float[][] hidden, int first, int end)
    {
        if (hidden.Length == 1)
        {
            return Pool(hidden[0], first, end);
        }

        // Pooling is linear; each layer is pooled in double and the layers are averaged in double
        double[]? mean = null;
        foreach (var layer in hidden)
        {
            var pooled = Pool(layer, first, end);
            if (pooled is null)
            {
                return null;
            }

            if (mean is null)
            {
                mean = pooled;
                continue;
            }

            for (var d = 0; d < mean.Length; d++)
            {
                mean[d] += pooled[d];
            }
        }

        for (var d = 0; d < mean!.Length; d++)
        {
            mean[d] /= hidden.Length;
        }

        return mean;
    }

    /// <summary>
    ///     Checks that the graph has the output of every sense layer, with the hidden size.
    /// </summary>
    /// <param name="outputs">The outputs of the graph.</param>
    /// <param name="names">The output names of the sense layers.</param>
    /// <param name="layout">The layout of the sense feature, for the message.</param>
    /// <param name="hiddenSize">The hidden size of the last layer.</param>
    /// <returns>The names.</returns>
    /// <exception cref="InvalidDataException">Thrown when an output is missing or has another hidden size.</exception>
    private static string[] CheckTaps(IReadOnlyDictionary<string, NodeMetadata> outputs, string[] names, string layout, int hiddenSize)
    {
        foreach (var name in names)
        {
            if (!outputs.TryGetValue(name, out var output))
            {
                throw new InvalidDataException($"The ONNX model has no output '{name}', which the sense feature '{layout}' needs.");
            }

            if (output.Dimensions[^1] != hiddenSize)
            {
                throw new InvalidDataException($"The output '{name}' of the ONNX model does not have the hidden size {hiddenSize}.");
            }
        }

        return names;
    }

    /// <summary>
    ///     Encodes one sequence of WordPiece ids.
    /// </summary>
    /// <param name="inputIds">The WordPiece ids, including the special tokens; at least one.</param>
    /// <param name="senseOutputs">True to read the outputs of every sense layer; false to read only the last layer.</param>
    /// <returns>The hidden states of every output read, in the order of the layers; each is row-major, <c>inputIds.Length × HiddenSize</c> values.</returns>
    private float[][] Run(int[] inputIds, bool senseOutputs)
    {
        // Build the three int64 inputs of a BERT-style model for a single unpadded sequence
        Interlocked.Increment(ref _passes);
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

        using var results = _session.Run(inputs, senseOutputs ? _senseOutputs : _headOutputs);
        var layers = new float[results.Count][];
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = results[i].AsTensor<float>().ToArray();
        }

        return layers;
    }
}
