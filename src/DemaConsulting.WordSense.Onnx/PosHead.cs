using System.Reflection;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The part-of-speech head of an encoder model: a linear classifier over the pooled encoder vectors of a word and
///     its previous and next word that gives the posterior of the ten parts of speech.
/// </summary>
/// <remarks>
///     <para>
///     The head reads the raw pooled vector of the encoder (the standardization of training is folded into the
///     weights), so <c>logit = W · [previous, word, next] + b</c>. A vector that is missing (a word without word pieces,
///     or a neighbor beyond the sentence edge) counts as zeros, which is how the head was trained. The arithmetic is
///     plain scalar code in a fixed order (the vector is first rounded to 32 bits, as it is when the training cache
///     stores it; the sum runs in 64 bits from the first feature to the last), so a result does not depend on the
///     hardware or on the number of threads.
///     </para>
///     <para>
///     The head has no lemma, no sense id and no inventory hash: it predicts part-of-speech classes from vectors only,
///     and a changed lexicon does not invalidate it. The restriction to the parts of speech that the lexicon lists for a
///     spelling is applied by <see cref="Restrict"/>. A head is immutable and safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class PosHead
{
    private readonly float[] _weights;
    private readonly float[] _biases;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PosHead"/> class from a structurally valid head.
    /// </summary>
    /// <param name="data">The head; the caller has checked its binding (see <see cref="LoadEmbedded"/>).</param>
    internal PosHead(PosHeadData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        _weights = data.Weights;
        _biases = data.Biases;
    }

    /// <summary>
    ///     Gets the head as it was read.
    /// </summary>
    public PosHeadData Data { get; }

    /// <summary>
    ///     Gets the hidden size of the encoder the head reads.
    /// </summary>
    public int HiddenSize => Data.HiddenSize;

    /// <summary>
    ///     Gets the number of classes, which is the number of parts of speech.
    /// </summary>
    public int ClassCount => _biases.Length;

    /// <summary>
    ///     Loads the head embedded in an assembly and binds it to its encoder and to the parts of speech of the library.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the resource.</param>
    /// <param name="resourceName">The manifest resource name of the head.</param>
    /// <param name="modelId">The identifier of the encoder model the head must belong to.</param>
    /// <param name="hiddenSize">The hidden size the encoder has.</param>
    /// <param name="encoderSha256">The pinned SHA-256 of the ONNX file of the encoder, as lower-case hexadecimal.</param>
    /// <returns>The head.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the resource is missing or corrupt, was trained for another model, hidden size or encoder file, or
    ///     has other classes than <see cref="PartOfSpeech"/> in its order.
    /// </exception>
    public static PosHead LoadEmbedded(Assembly assembly, string resourceName, string modelId, int hiddenSize, string encoderSha256)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(encoderSha256);
        var data = PosHeadBlob.Read(WeightedLemmaTable.ReadResource(assembly, resourceName));
        if (data.ModelId != modelId)
        {
            throw new InvalidDataException($"The part-of-speech head was trained for '{data.ModelId}', not '{modelId}'.");
        }

        if (data.HiddenSize != hiddenSize)
        {
            throw new InvalidDataException($"The part-of-speech head has hidden size {data.HiddenSize}, not {hiddenSize}.");
        }

        // The head was fitted on the vectors of one exact encoder file; any other file gives vectors it does not know
        if (!string.Equals(Convert.ToHexString(data.EncoderSha256), encoderSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The part-of-speech head was trained on another encoder file than the pinned one.");
        }

        if (data.FeatureLayout != PosHeadBlob.WordAndNeighbors)
        {
            throw new InvalidDataException($"The part-of-speech head has the unknown feature layout {data.FeatureLayout}.");
        }

        // The class order is the order of the enum: a changed enum must fail loudly instead of mislabeling answers
        if (!data.ClassNames.SequenceEqual(PosHeadBlob.ExpectedClassNames(), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The part-of-speech head has other classes, or another class order, than the parts of speech of the library.");
        }

        return new PosHead(data);
    }

    /// <summary>
    ///     Computes the posterior of the parts of speech of a word from its pooled vector and those of its neighbors.
    /// </summary>
    /// <param name="previous">The vector of the previous word, or null at the start of the sentence or for a word without word pieces.</param>
    /// <param name="word">The vector of the word, or null for a word without word pieces.</param>
    /// <param name="next">The vector of the next word, or null at the end of the sentence or for a word without word pieces.</param>
    /// <returns>The probabilities in the order of <see cref="PartOfSpeech"/>; they sum to one.</returns>
    /// <exception cref="ArgumentException">Thrown when a vector does not have the hidden size.</exception>
    public double[] Posterior(double[]? previous, double[]? word, double[]? next)
    {
        CheckLength(previous);
        CheckLength(word);
        CheckLength(next);
        var d = HiddenSize;
        var k = ClassCount;
        var logits = new double[k];
        for (var c = 0; c < k; c++)
        {
            // Sum the three blocks in the fixed order previous, word, next; a missing block adds nothing (zeros)
            var row = c * 3 * d;
            var sum = (double)_biases[c];
            sum = Accumulate(sum, row, previous);
            sum = Accumulate(sum, row + d, word);
            sum = Accumulate(sum, row + (2 * d), next);
            logits[c] = sum;
        }

        return Softmax(logits);
    }

    /// <summary>
    ///     Restricts a posterior to the parts of speech a spelling can have and renormalizes it.
    /// </summary>
    /// <param name="posterior">The posterior from <see cref="Posterior"/>.</param>
    /// <param name="allowedMask">The allowed parts of speech as a bit set: bit <c>(int)PartOfSpeech</c> is set for an allowed one.</param>
    /// <returns>
    ///     The renormalized posterior with zero for every part of speech that is not allowed; the unchanged posterior when
    ///     the mask allows nothing or the head gives every allowed part of speech the probability zero.
    /// </returns>
    public static double[] Restrict(double[] posterior, int allowedMask)
    {
        ArgumentNullException.ThrowIfNull(posterior);
        var masked = new double[posterior.Length];
        var total = 0.0;
        for (var c = 0; c < posterior.Length; c++)
        {
            if ((allowedMask & (1 << c)) != 0)
            {
                masked[c] = posterior[c];
                total += posterior[c];
            }
        }

        if (total <= 0)
        {
            return [.. posterior];
        }

        for (var c = 0; c < masked.Length; c++)
        {
            masked[c] /= total;
        }

        return masked;
    }

    /// <summary>
    ///     Computes a numerically stable softmax.
    /// </summary>
    /// <param name="logits">The scores.</param>
    /// <returns>The probabilities.</returns>
    private static double[] Softmax(double[] logits)
    {
        var max = logits[0];
        for (var i = 1; i < logits.Length; i++)
        {
            max = Math.Max(max, logits[i]);
        }

        var result = new double[logits.Length];
        var total = 0.0;
        for (var i = 0; i < logits.Length; i++)
        {
            result[i] = Math.Exp(logits[i] - max);
            total += result[i];
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] /= total;
        }

        return result;
    }

    /// <summary>
    ///     Adds the product of a weight block and a vector to a running sum.
    /// </summary>
    /// <param name="sum">The running sum.</param>
    /// <param name="offset">The index of the first weight of the block.</param>
    /// <param name="vector">The vector, or null for zeros.</param>
    /// <returns>The new sum.</returns>
    private double Accumulate(double sum, int offset, double[]? vector)
    {
        if (vector is null)
        {
            return sum;
        }

        var weights = _weights;
        for (var j = 0; j < vector.Length; j++)
        {
            sum += weights[offset + j] * (double)(float)vector[j];
        }

        return sum;
    }

    /// <summary>
    ///     Checks the length of a vector.
    /// </summary>
    /// <param name="vector">The vector, or null.</param>
    /// <exception cref="ArgumentException">Thrown when the vector does not have the hidden size.</exception>
    private void CheckLength(double[]? vector)
    {
        if (vector is not null && vector.Length != HiddenSize)
        {
            throw new ArgumentException($"A vector must have the hidden size {HiddenSize}, not {vector.Length}.", nameof(vector));
        }
    }
}
