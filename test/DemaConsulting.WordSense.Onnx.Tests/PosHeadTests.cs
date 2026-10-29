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
        var head = Create();
        double[] previous = [0.5, -1.0, 0.25, 2.0];
        double[] word = [1.0, 0.5, -0.5, 0.0];
        double[] next = [-0.25, 0.75, 1.5, -1.0];

        var posterior = head.Posterior(previous, word, next);

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
        var head = Create();
        double[] word = [1.0, 0.5, -0.5, 0.0];
        double[] zeros = new double[Hidden];

        Assert.Equal(head.Posterior(zeros, word, zeros), head.Posterior(null, word, null));
        Assert.Equal(head.Posterior(zeros, zeros, zeros), head.Posterior(null, null, null));
        AssertClose(Expected(head, null, word, null), head.Posterior(null, word, null));
    }

    /// <summary>
    ///     The same input always gives bit-identical output.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_RepeatedCalls_AreIdentical()
    {
        var head = Create();
        double[] word = [0.1, 0.2, 0.3, 0.4];

        var first = head.Posterior(word, word, word);
        var second = head.Posterior(word, word, word);

        Assert.Equal(first, second);
    }

    /// <summary>
    ///     A vector of the wrong length is rejected.
    /// </summary>
    [Fact]
    public void PosHead_Posterior_WrongLength_Throws()
    {
        var head = Create();

        Assert.Throws<ArgumentException>(() => head.Posterior(null, new double[Hidden + 1], null));
        Assert.Throws<ArgumentException>(() => head.Posterior(new double[2], new double[Hidden], null));
    }

    /// <summary>
    ///     A restriction keeps the allowed parts of speech, renormalizes them and gives the others zero.
    /// </summary>
    [Fact]
    public void PosHead_Restrict_AllowedMask_RenormalizesAllowed()
    {
        double[] posterior = [0.4, 0.1, 0.3, 0.1, 0.05, 0.02, 0.01, 0.01, 0.0, 0.0];
        var mask = (1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb);

        var restricted = PosHead.Restrict(posterior, mask);

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
        double[] posterior = [0.5, 0.5, 0, 0, 0, 0, 0, 0, 0, 0];

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
        Assert.Throws<ArgumentNullException>(() => PosHead.Restrict(null!, 1));
        Assert.Throws<ArgumentNullException>(() => new PosHead(null!));
    }
}
