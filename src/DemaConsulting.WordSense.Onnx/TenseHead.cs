namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The tense head of an encoder model: a binary logistic classifier over the pooled encoder vector of a verb that tells
///     whether it is past (a past tense or a participle) or non-past (infinitive, present, imperative).
/// </summary>
/// <remarks>
///     <para>
///     The head is asked about a verb whose past and non-past analyses share a spelling (<c>read</c>, <c>put</c>,
///     <c>found</c>); it says nothing about whether the word is a verb. Like <see cref="PosHead"/>, it reads the raw
///     pooled vector of the encoder (the standardization of training is folded into the weights), so
///     <c>logit = w · x + b</c> with <c>x</c> the vector of the word or, for the neighbor layout, the vectors of the
///     previous word, the word and the next word (a missing vector counts as zeros, as in training). The arithmetic is
///     plain scalar code in a fixed order (every vector element is first rounded to 32 bits, the sum runs in 64 bits from the
///     bias over the features in order), so a result does not depend on the hardware or on the number of threads.
///     A head is immutable and safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class TenseHead
{
    /// <summary>
    ///     The weights as doubles, so that the sum runs in 64 bits; one vector's worth for the word layout, three for the
    ///     layout with neighbors.
    /// </summary>
    private readonly double[] _weights;

    /// <summary>
    ///     The bias, the score of a verb whose vectors are all zero.
    /// </summary>
    private readonly double _bias;

    /// <summary>
    ///     The hidden size of the encoder, the length that every vector must have and the size of one block of weights.
    /// </summary>
    private readonly int _hiddenSize;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenseHead"/> class from a structurally valid head.
    /// </summary>
    /// <param name="data">The head as it was read.</param>
    /// <param name="hiddenSize">The hidden size of the encoder.</param>
    internal TenseHead(TenseHeadData data, int hiddenSize)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        _hiddenSize = hiddenSize;
        _weights = Array.ConvertAll(data.Weights, w => (double)w);
        _bias = data.Bias;
    }

    /// <summary>
    ///     Gets the head as it was read.
    /// </summary>
    public TenseHeadData Data { get; }

    /// <summary>
    ///     Computes the probability that a verb is past.
    /// </summary>
    /// <param name="previous">The vector of the previous word, or null at the start of the sentence or for a word without word pieces; read only by the neighbor layout.</param>
    /// <param name="word">The vector of the word.</param>
    /// <param name="next">The vector of the next word, or null; read only by the neighbor layout.</param>
    /// <returns>The probability of past, strictly between 0 and 1 for finite scores.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="word"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a vector does not have the hidden size.</exception>
    public double PastProbability(double[]? previous, double[] word, double[]? next)
    {
        ArgumentNullException.ThrowIfNull(word);
        Check(previous);
        Check(word);
        Check(next);
        var sum = _bias;
        if (Data.Layout == PosHeadBlob.TenseWordAndNeighbors)
        {
            sum = Add(sum, previous, 0);
            sum = Add(sum, word, _hiddenSize);
            sum = Add(sum, next, 2 * _hiddenSize);
        }
        else
        {
            sum = Add(sum, word, 0);
        }

        return 1.0 / (1.0 + Math.Exp(-sum));
    }

    /// <summary>
    ///     Adds the weighted part of a vector to a running score.
    /// </summary>
    /// <param name="sum">The score so far.</param>
    /// <param name="vector">The vector, or null for a missing one, which adds nothing (as zeros do).</param>
    /// <param name="offset">The index in the weights of the first weight of this vector's block.</param>
    /// <returns>The score with the vector added, summed in the order of the features so that the result does not depend on the hardware.</returns>
    private double Add(double sum, double[]? vector, int offset)
    {
        if (vector is null)
        {
            return sum;
        }

        var w = _weights.AsSpan(offset, _hiddenSize);
        for (var j = 0; j < w.Length; j++)
        {
            sum += w[j] * (float)vector[j];
        }

        return sum;
    }

    /// <summary>
    ///     Checks that a vector has the hidden size of the encoder.
    /// </summary>
    /// <param name="vector">The vector, or null for a missing one.</param>
    /// <exception cref="ArgumentException">Thrown when the vector has another length than the hidden size.</exception>
    private void Check(double[]? vector)
    {
        if (vector is not null && vector.Length != _hiddenSize)
        {
            throw new ArgumentException($"A vector must have the hidden size {_hiddenSize}, not {vector.Length}.", nameof(vector));
        }
    }
}
