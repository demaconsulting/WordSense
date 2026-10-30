namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the arithmetic of <see cref="PosHead"/>.
/// </summary>
public class PosHeadTests
{
    private const int Hidden = 4;

    private static PosHead Create()
    {
        var spec = HeadSpec.Create(Hidden);
        return new PosHead(PosHeadBlob.Read(HeadBlobWriter.Write(spec)));
    }

    private static void AssertClose(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 12);
        }
    }

    private static double[] Expected(PosHead head, double[]? previous, double[] word, double[]? next)
    {
        // Independent computation with plain loops over the same half-precision weights
        var f = 3 * Hidden;
        var logits = new double[head.ClassCount];
        for (var c = 0; c < logits.Length; c++)
        {
            var sum = (double)head.Data.Biases[c];
            for (var j = 0; j < Hidden; j++)
            {
                sum += head.Data.Weights[(c * f) + j] * (previous?[j] ?? 0);
                sum += head.Data.Weights[(c * f) + Hidden + j] * word[j];
                sum += head.Data.Weights[(c * f) + (2 * Hidden) + j] * (next?[j] ?? 0);
            }

            logits[c] = sum;
        }

        var max = logits.Max();
        var exp = logits.Select(l => Math.Exp(l - max)).ToArray();
        return [.. exp.Select(e => e / exp.Sum())];
    }

    /// <summary>
    ///     The posterior of a tiny head equals an independent computation and sums to one.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_TinyHead_MatchesIndependentComputation()
    {
        // Arrange: a tiny head and three distinct context vectors
        var head = Create();
        double[] previous = [0.5, -1.0, 0.25, 2.0];
        double[] word = [1.0, 0.5, -0.5, 0.0];
        double[] next = [-0.25, 0.75, 1.5, -1.0];

        // Act: compute the posterior
        var posterior = head.Posterior(previous, word, next);

        // Assert: one probability per part of speech, summing to one and matching the independent computation
        Assert.Equal(Enum.GetValues<PartOfSpeech>().Length, posterior.Length);
        Assert.Equal(1.0, posterior.Sum(), 12);
        AssertClose(Expected(head, previous, word, next), posterior);
    }

    /// <summary>
    ///     A missing neighbor counts as zeros, a missing word vector as zeros too.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_MissingVectors_AreZeros()
    {
        // Arrange: a word vector and an all-zero vector of the same length
        var head = Create();
        double[] word = [1.0, 0.5, -0.5, 0.0];
        double[] zeros = new double[Hidden];

        // Act / Assert: null neighbors or a null word give the same posterior as explicit zeros
        Assert.Equal(head.Posterior(zeros, word, zeros), head.Posterior(null, word, null));
        Assert.Equal(head.Posterior(zeros, zeros, zeros), head.Posterior(null, null, null));
        AssertClose(Expected(head, null, word, null), head.Posterior(null, word, null));
    }

    /// <summary>
    ///     The posterior is bit for bit the sum of one class at a time in the fixed order: bias, previous, word, next.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_FixedOrder_IsBitIdenticalToOneClassAtATime()
    {
        // Arrange: a head and vectors with values that are not exact in 32 bits
        var head = Create();
        double[] previous = [0.1, -1.0 / 3.0, 0.25, 2.0 / 7.0];
        double[] word = [1.0 / 9.0, 0.5, -0.5, 1e-3];
        double[] next = [-0.25, 0.75, Math.PI, -1.0];
        double[][] vectors = [previous, word, next];
        var f = 3 * Hidden;
        var logits = new double[head.ClassCount];
        for (var c = 0; c < logits.Length; c++)
        {
            double sum = head.Data.Biases[c];
            for (var block = 0; block < 3; block++)
            {
                var vector = vectors[block];
                for (var j = 0; j < Hidden; j++)
                {
                    sum += head.Data.Weights[(c * f) + (block * Hidden) + j] * (double)(float)vector[j];
                }
            }

            logits[c] = sum;
        }

        var max = logits.Max();
        var exp = logits.Select(l => Math.Exp(l - max)).ToArray();
        var total = 0.0;
        foreach (var e in exp)
        {
            total += e;
        }

        // Act: compute the posterior
        var posterior = head.Posterior(previous, word, next);

        // Assert: every probability is identical to the last bit
        Assert.Equal(exp.Select(e => e / total).ToArray(), posterior);
    }

    /// <summary>
    ///     The restricted posterior is, bit for bit, the restriction of the posterior when that has mass.
    /// </summary>
    [Fact]
    public void PosHead_RestrictedPosterior_NormalMass_IsBitIdenticalToRestrictOfPosterior()
    {
        // Arrange
        var head = Create();
        double[] previous = [0.5, -1.0, 0.25, 2.0];
        double[] word = [1.0, 0.5, -0.5, 0.0];
        double[] next = [-0.25, 0.75, 1.5, -1.0];
        var mask = (1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb);

        // Act
        var restricted = head.RestrictedPosterior(previous, word, next, mask);

        // Assert
        Assert.Equal(PosHead.Restrict(head.Posterior(previous, word, next), mask), restricted);
    }

    /// <summary>
    ///     An excluded class with a far higher score makes the full softmax give the allowed classes zero; the restriction
    ///     is then the softmax of the allowed scores, summing to one.
    /// </summary>
    [Fact]
    public void PosHead_RestrictedPosterior_ExtremeLogitOfAnExcludedClass_UsesTheAllowedScores()
    {
        // Arrange: no vectors, so the scores are the biases: adjective 1000, noun 1, verb 0
        var biases = new float[Enum.GetValues<PartOfSpeech>().Length];
        biases[(int)PartOfSpeech.Adjective] = 1000f;
        biases[(int)PartOfSpeech.Noun] = 1f;
        var spec = HeadSpec.Create(Hidden) with { Biases = biases };
        var head = new PosHead(PosHeadBlob.Read(HeadBlobWriter.Write(spec)));
        var mask = (1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb);
        var plain = head.Posterior(null, null, null);
        Assert.Equal(0.0, plain[(int)PartOfSpeech.Noun] + plain[(int)PartOfSpeech.Verb]);

        // Act
        var restricted = head.RestrictedPosterior(null, null, null, mask);

        // Assert: the restriction of the posterior has no mass, the new one does
        Assert.NotNull(restricted);
        Assert.Equal(1.0, restricted.Sum(), 12);
        Assert.Equal(Math.Exp(1) / (Math.Exp(1) + 1), restricted[(int)PartOfSpeech.Noun], 12);
        Assert.Equal(1 / (Math.Exp(1) + 1), restricted[(int)PartOfSpeech.Verb], 12);
        Assert.Equal(0.0, restricted[(int)PartOfSpeech.Adjective]);
    }

    /// <summary>
    ///     A mask that allows nothing gives no distribution.
    /// </summary>
    [Fact]
    public void PosHead_RestrictedPosterior_EmptyMask_ReturnsNull()
    {
        // Arrange
        var head = Create();
        double[] word = [1.0, 0.5, -0.5, 0.0];

        // Act / Assert
        Assert.Null(head.RestrictedPosterior(null, word, null, 0));
    }

    /// <summary>
    ///     A score that is not a number in an allowed class gives no distribution.
    /// </summary>
    [Fact]
    public void PosHead_RestrictedPosterior_NotANumberScore_ReturnsNull()
    {
        // Arrange: a vector with a NaN makes every score NaN
        var head = Create();
        double[] word = [double.NaN, 0.5, -0.5, 0.0];

        // Act / Assert
        Assert.Null(head.RestrictedPosterior(null, word, null, 1 << (int)PartOfSpeech.Noun));
    }

    /// <summary>
    ///     The same input always gives bit-identical output.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_RepeatedCalls_AreIdentical()
    {
        // Arrange: a head and one input vector
        var head = Create();
        double[] word = [0.1, 0.2, 0.3, 0.4];

        // Act: compute the posterior twice from the same input
        var first = head.Posterior(word, word, word);
        var second = head.Posterior(word, word, word);

        // Assert: both results are identical
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     A vector of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_WrongLength_Throws()
    {
        // Arrange: a head whose hidden size is four
        var head = Create();

        // Act / Assert: a word vector that is too long and a neighbor that is too short are rejected
        Assert.Throws<ArgumentException>(() => head.Posterior(null, new double[Hidden + 1], null));
        Assert.Throws<ArgumentException>(() => head.Posterior(new double[2], new double[Hidden], null));
    }

    /// <summary>
    ///     A restriction keeps the allowed parts of speech, renormalizes them and gives the others zero.
    /// </summary>
    [Fact]
    public void PosHead_Restrict_AllowedMask_RenormalizesAllowed()
    {
        // Arrange: a posterior and a mask that allows only Noun and Verb
        double[] posterior = [0.4, 0.1, 0.3, 0.1, 0.05, 0.02, 0.01, 0.01, 0.0, 0.0];
        var mask = (1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb);

        // Act: restrict the posterior to the mask
        var restricted = PosHead.Restrict(posterior, mask);

        // Assert: the allowed values are renormalized to sum to one and the others are zero
        Assert.Equal(1.0, restricted.Sum(), 12);
        Assert.Equal(0.4 / 0.7, restricted[(int)PartOfSpeech.Noun], 12);
        Assert.Equal(0.3 / 0.7, restricted[(int)PartOfSpeech.Verb], 12);
        Assert.Equal(0, restricted[(int)PartOfSpeech.Adjective]);
    }

    /// <summary>
    ///     A mask that allows nothing, or only parts of speech of probability zero, gives the posterior back.
    /// </summary>
    [Fact]
    public void PosHead_Restrict_NothingAllowed_FallsBackToPosterior()
    {
        // Arrange: a posterior with probability only on Noun and Pronoun
        double[] posterior = [0.5, 0.5, 0, 0, 0, 0, 0, 0, 0, 0];

        // Act / Assert: an empty mask and a mask of a zero-probability class give an equal copy of the posterior
        Assert.Equal(posterior, PosHead.Restrict(posterior, 0));
        Assert.Equal(posterior, PosHead.Restrict(posterior, 1 << (int)PartOfSpeech.Auxiliary));
        Assert.NotSame(posterior, PosHead.Restrict(posterior, 0));
    }

    /// <summary>
    ///     A null posterior or head data is rejected.
    /// </summary>
    [Fact]
    public void PosHead_NullArguments_Throw()
    {
        // Arrange: null for the posterior and for the head data
        // Act / Assert: both calls throw
        Assert.Throws<ArgumentNullException>(() => PosHead.Restrict(null!, 1));
        Assert.Throws<ArgumentNullException>(() => new PosHead(null!));
    }
}
