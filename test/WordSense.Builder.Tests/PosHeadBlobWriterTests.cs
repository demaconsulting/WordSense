using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the writer of the part-of-speech head file and its agreement with the reader of the Onnx package.
/// </summary>
public sealed class PosHeadBlobWriterTests
{
    private const int Hidden = 6;

    private static PosHeadData Head(float scale = 0.37f)
    {
        var classes = Enum.GetNames<PartOfSpeech>();
        var weights = new float[classes.Length * 3 * Hidden];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = ((i % 13) - 6) * scale / 7;
        }

        return new PosHeadData("model-x", Hidden, [.. Enumerable.Range(0, 32).Select(i => (byte)(i + 1))], PosHeadBlob.WordAndNeighbors, classes, [.. Enumerable.Range(0, 32).Select(i => (byte)(255 - i))], 0.003f, "word+-1", weights, [.. Enumerable.Range(0, classes.Length).Select(c => (c - 4) / 3f)]);
    }

    /// <summary>
    ///     A written head is read back with every field, and the weights are those of half precision.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadBlobWriter_Write_RoundTripsThroughTheReader()
    {
        var head = Head();

        var read = PosHeadBlob.Read(PosHeadBlobWriter.Write(head));

        Assert.Equal(head.ModelId, read.ModelId);
        Assert.Equal(head.HiddenSize, read.HiddenSize);
        Assert.Equal(head.EncoderSha256, read.EncoderSha256);
        Assert.Equal(head.TrainSha256, read.TrainSha256);
        Assert.Equal(head.FeatureLayout, read.FeatureLayout);
        Assert.Equal(head.ClassNames, read.ClassNames);
        Assert.Equal(head.C, read.C);
        Assert.Equal(head.Variant, read.Variant);
        Assert.Equal(PosHeadBlobWriter.Round(head.Weights), read.Weights);
        Assert.Equal(head.Biases, read.Biases);
        Assert.NotEqual(head.Weights, read.Weights);
    }

    /// <summary>
    ///     The same head always gives the same bytes, and a different head gives different bytes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadBlobWriter_Write_IsDeterministic()
    {
        Assert.Equal(PosHeadBlobWriter.Write(Head()), PosHeadBlobWriter.Write(Head()));
        Assert.NotEqual(PosHeadBlobWriter.Write(Head()), PosHeadBlobWriter.Write(Head(0.5f)));
    }

    /// <summary>
    ///     Rounding to half precision is idempotent and refuses weights that half precision cannot hold.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadBlobWriter_Round_IsIdempotentAndRejectsOverflow()
    {
        var once = PosHeadBlobWriter.Round([0.1f, -2.7f, 1000.3f]);

        Assert.Equal(once, PosHeadBlobWriter.Round(once));
        Assert.Throws<InvalidDataException>(() => PosHeadBlobWriter.Round([70000f]));
        Assert.Throws<InvalidDataException>(() => PosHeadBlobWriter.Round([float.NaN]));
        Assert.Throws<ArgumentNullException>(() => PosHeadBlobWriter.Round(null!));
    }

    /// <summary>
    ///     An inconsistent head, a wrong digest size and a non-finite bias are refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadBlobWriter_Write_InvalidHead_Throws()
    {
        var head = Head();

        Assert.Throws<ArgumentNullException>(() => PosHeadBlobWriter.Write(null!));
        Assert.Throws<ArgumentException>(() => PosHeadBlobWriter.Write(head with { EncoderSha256 = [1, 2] }));
        Assert.Throws<ArgumentException>(() => PosHeadBlobWriter.Write(head with { TrainSha256 = [] }));
        Assert.Throws<ArgumentException>(() => PosHeadBlobWriter.Write(head with { Weights = head.Weights[1..] }));
        Assert.Throws<ArgumentException>(() => PosHeadBlobWriter.Write(head with { Biases = head.Biases[1..] }));
        Assert.Throws<InvalidDataException>(() => PosHeadBlobWriter.Write(head with { Biases = [.. head.Biases.Select((b, i) => i == 0 ? float.PositiveInfinity : b)] }));
    }

    /// <summary>
    ///     The written head loads into the posterior arithmetic of the library.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadBlobWriter_Write_LoadsAsAPosHead()
    {
        var head = new PosHead(PosHeadBlob.Read(PosHeadBlobWriter.Write(Head())));

        var posterior = head.Posterior(null, [.. Enumerable.Range(0, Hidden).Select(i => i / 4.0)], null);

        Assert.Equal(1.0, posterior.Sum(), 10);
        Assert.Equal(10, posterior.Length);
    }
}
