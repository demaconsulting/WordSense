using System.IO.Compression;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the weights reader <see cref="WeightsBlob"/>. Round trips with the writer are tested in the build
///     tool's tests.
/// </summary>
public class WeightsBlobTests
{
    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            brotli.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Data that is not a compressed stream is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read([9, 8, 7, 6, 5]));
    }

    /// <summary>
    ///     A compressed stream with an unknown magic number is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_UnknownMagic_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress("NOPE"u8.ToArray())));
    }

    /// <summary>
    ///     A blob that ends inside the header is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Truncated_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX1"u8.ToArray(), 40])));
    }

    /// <summary>
    ///     A blob with no lemmas and a dimension of zero reads as an empty set of weights.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NoLemmas_ReturnsEmptyWeights()
    {
        // Magic, model id "m" (length 1), dimension 0, dense flag 0, no mean values, no lemmas
        var blob = Compress([.. "WSX1"u8.ToArray(), 1, (byte)'m', 0, 0, 0]);

        var data = WeightsBlob.Read(blob);

        Assert.Equal("m", data.ModelId);
        Assert.Equal(0, data.Dimension);
        Assert.Empty(data.Lemmas);
    }
}
