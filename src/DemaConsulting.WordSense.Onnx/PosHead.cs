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
    private readonly double[] _weights;
    private readonly float[] _biases;

    // Below this total, the restriction of the full softmax has lost its precision: the allowed scores are used directly
    private const double UnderflowLimit = 1e-250;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PosHead"/> class from a structurally valid head.
    /// </summary>
    /// <param name="data">The head; the caller has checked its binding (see <see cref="LoadEmbedded"/>).</param>
    internal PosHead(PosHeadData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;

        // A 32-bit weight widens to 64 bits without loss, so the products are the same as from the 32-bit weights
        _weights = Array.ConvertAll(data.Weights, w => (double)w);
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
    public double[] Posterior(double[]? previous, double[]? word, double[]? next) => Softmax(Logits(previous, word, next));

    /// <summary>
    ///     Computes the posterior of the parts of speech of a word, limited to the parts of speech that are allowed.
    /// </summary>
    /// <param name="previous">The vector of the previous word, or null.</param>
    /// <param name="word">The vector of the word, or null.</param>
    /// <param name="next">The vector of the next word, or null.</param>
    /// <param name="allowedMask">The allowed parts of speech as a bit set: bit <c>(int)PartOfSpeech</c> is set for an allowed one.</param>
    /// <returns>
    ///     The probabilities in the order of <see cref="PartOfSpeech"/>, zero for every part of speech that is not allowed,
    ///     summing to one; or null when no usable distribution exists (the mask allows nothing, or a score is not a number).
    /// </returns>
    /// <remarks>
    ///     Normally this is <see cref="Restrict"/> of <see cref="Posterior"/>, bit for bit. When the head gives every
    ///     allowed part of speech so little probability that the product underflows (an excluded class with a far larger
    ///     score), the restriction is computed from the scores themselves, as a softmax over the allowed scores only, so
    ///     the result never has zero mass and never falls back to the unrestricted posterior.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when a vector does not have the hidden size.</exception>
    public double[]? RestrictedPosterior(double[]? previous, double[]? word, double[]? next, int allowedMask)
    {
        var logits = Logits(previous, word, next);
        var masked = new double[logits.Length];
        var total = 0.0;
        var posterior = Softmax(logits);
        for (var c = 0; c < masked.Length; c++)
        {
            if ((allowedMask & (1 << c)) != 0)
            {
                masked[c] = posterior[c];
                total += posterior[c];
            }
        }

        if (total >= UnderflowLimit && double.IsFinite(total))
        {
            for (var c = 0; c < masked.Length; c++)
            {
                masked[c] /= total;
            }

            return masked;
        }

        // The softmax over all classes left no usable mass for the allowed ones: a softmax over the allowed scores alone
        var max = double.NegativeInfinity;
        for (var c = 0; c < logits.Length; c++)
        {
            if ((allowedMask & (1 << c)) != 0)
            {
                if (double.IsNaN(logits[c]))
                {
                    return null;
                }

                max = Math.Max(max, logits[c]);
            }
        }

        if (!double.IsFinite(max))
        {
            return null;
        }

        total = 0.0;
        for (var c = 0; c < logits.Length; c++)
        {
            masked[c] = (allowedMask & (1 << c)) != 0 ? Math.Exp(logits[c] - max) : 0.0;
            total += masked[c];
        }

        for (var c = 0; c < masked.Length; c++)
        {
            masked[c] /= total;
        }

        return masked;
    }

    /// <summary>
    ///     Computes the scores of the parts of speech of a word before the softmax.
    /// </summary>
    /// <param name="previous">The vector of the previous word, or null.</param>
    /// <param name="word">The vector of the word, or null.</param>
    /// <param name="next">The vector of the next word, or null.</param>
    /// <returns>One score per part of speech, in the order of <see cref="PartOfSpeech"/>.</returns>
    private double[] Logits(double[]? previous, double[]? word, double[]? next)
    {
        CheckLength(previous);
        CheckLength(word);
        CheckLength(next);
        var k = ClassCount;
        var d = HiddenSize;
        var logits = new double[k];

        // The vectors are rounded to 32 bits once, not once per class
        Span<double> rounded = stackalloc double[3 * d];
        Round(previous, rounded[..d]);
        Round(word, rounded.Slice(d, d));
        Round(next, rounded.Slice(2 * d, d));
        ReadOnlySpan<bool> present = [previous is not null, word is not null, next is not null];

        // Five classes at a time share the rounded vector elements and run five independent sums. Every logit is still
        // one sum in the fixed order: bias, previous block, word block, next block, each from the first feature to the
        // last, so the result is bit for bit that of one class at a time.
        var c = 0;
        for (; c + 5 <= k; c += 5)
        {
            Rows5(c, rounded, present, logits);
        }

        for (; c < k; c++)
        {
            var sum = (double)_biases[c];
            for (var block = 0; block < 3; block++)
            {
                if (present[block])
                {
                    var w = _weights.AsSpan((c * 3 * d) + (block * d), d);
                    var x = rounded.Slice(block * d, d);
                    for (var j = 0; j < w.Length; j++)
                    {
                        sum += w[j] * x[j];
                    }
                }
            }

            logits[c] = sum;
        }

        return logits;
    }

    /// <summary>
    ///     Rounds a vector to 32 bits, the precision of the cache the head was trained from.
    /// </summary>
    /// <param name="vector">The vector, or null for a missing one (nothing is written).</param>
    /// <param name="target">Receives the rounded vector.</param>
    private static void Round(double[]? vector, Span<double> target)
    {
        if (vector is null)
        {
            return;
        }

        for (var j = 0; j < target.Length; j++)
        {
            target[j] = (float)vector[j];
        }
    }

    /// <summary>
    ///     Computes the logits of five consecutive classes.
    /// </summary>
    /// <param name="first">The first class.</param>
    /// <param name="rounded">The rounded vectors of the previous word, the word and the next word, one after the other.</param>
    /// <param name="present">Tells for each of the three vectors whether it is present.</param>
    /// <param name="logits">Receives the five logits.</param>
    private void Rows5(int first, ReadOnlySpan<double> rounded, ReadOnlySpan<bool> present, double[] logits)
    {
        var d = HiddenSize;
        var stride = 3 * d;
        double s0 = _biases[first], s1 = _biases[first + 1], s2 = _biases[first + 2], s3 = _biases[first + 3], s4 = _biases[first + 4];
        for (var block = 0; block < 3; block++)
        {
            if (!present[block])
            {
                continue;
            }

            var x = rounded.Slice(block * d, d);
            var w0 = _weights.AsSpan((first * stride) + (block * d), d);
            var w1 = _weights.AsSpan(((first + 1) * stride) + (block * d), d);
            var w2 = _weights.AsSpan(((first + 2) * stride) + (block * d), d);
            var w3 = _weights.AsSpan(((first + 3) * stride) + (block * d), d);
            var w4 = _weights.AsSpan(((first + 4) * stride) + (block * d), d);
            for (var j = 0; j < x.Length; j++)
            {
                var v = x[j];
                s0 += w0[j] * v;
                s1 += w1[j] * v;
                s2 += w2[j] * v;
                s3 += w3[j] * v;
                s4 += w4[j] * v;
            }
        }

        logits[first] = s0;
        logits[first + 1] = s1;
        logits[first + 2] = s2;
        logits[first + 3] = s3;
        logits[first + 4] = s4;
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
