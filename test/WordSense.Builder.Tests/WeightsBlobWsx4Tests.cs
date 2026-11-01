using System.IO.Compression;
using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests that the weights writer records the encoder and the feature layout (<c>WSX4</c>) only for weights that name
///     their encoder, and that it still writes the committed <c>WSX3</c> weights of the two packages unchanged.
/// </summary>
public sealed class WeightsBlobWsx4Tests
{
    private const int Dimension = 3;

    private static readonly byte[] InventoryHash = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] EncoderSha = Enumerable.Range(40, 32).Select(i => (byte)i).ToArray();

    private static readonly WeightedLemma Bank = new(
        "bank",
        ["bank#noun", "bank#verb"],
        new LemmaWeights([0.5f, -0.25f, 1f, -0.1f, 0.3f, 0.7f], [0.125f, -0.125f], Dimension, 0.75f, LemmaWeights.Never));

    private static WeightsData Sample(byte[]? encoder = null, string layout = FeatureLayout.Last) =>
        new("test-model", InventoryHash, Dimension, [0.1f, -0.2f, 0.3f], [Bank], encoder, layout);

    private static byte[] Raw(byte[] blob)
    {
        using var brotli = new BrotliStream(new MemoryStream(blob), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        brotli.CopyTo(raw);
        return raw.ToArray();
    }

    private static byte[] Resource(System.Reflection.Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>
    ///     Weights that name their encoder are written as WSX4 and read back with the encoder and the layout.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_WithEncoder_WritesWsx4AndRoundTrips()
    {
        // Arrange
        var data = Sample(EncoderSha, "mean:9-12");

        // Act
        var blob = WeightsBlobWriter.Write(data);
        var read = WeightsBlob.Read(blob);

        // Assert
        Assert.Equal("WSX4"u8.ToArray(), Raw(blob)[..4]);
        Assert.Equal(EncoderSha, read.EncoderSha256);
        Assert.Equal("mean:9-12", read.Layout);
        Assert.Equal("test-model", read.ModelId);
        Assert.Equal(InventoryHash, read.InventoryHash);
        Assert.Equal(["bank"], read.Lemmas.Select(l => l.Lemma));
    }

    /// <summary>
    ///     Weights without an encoder keep the WSX3 layout and read back with the layout <c>last</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_WithoutEncoder_WritesWsx3()
    {
        // Arrange / Act
        var blob = WeightsBlobWriter.Write(Sample());
        var read = WeightsBlob.Read(blob);

        // Assert
        Assert.Equal("WSX3"u8.ToArray(), Raw(blob)[..4]);
        Assert.Null(read.EncoderSha256);
        Assert.Equal(FeatureLayout.Last, read.Layout);
    }

    /// <summary>
    ///     A layout other than <c>last</c> without an encoder, an empty layout and an encoder hash of the wrong size are refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsBlobWriter_Write_InvalidEncoderOrLayout_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(null, "mean:9-12")));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample(EncoderSha, string.Empty)));
        Assert.Throws<ArgumentException>(() => WeightsBlobWriter.Write(Sample([1, 2, 3], "mean:9-12")));
    }

    /// <summary>
    ///     Reading the committed weights of a package and writing them again gives the same content, byte for byte
    ///     before the compression, so a rebuild of weights that name no encoder keeps the resource of the package.
    /// </summary>
    /// <param name="package">The package.</param>
    [Theory]
    [InlineData("minilm")]
    [InlineData("bert-base-cased")]
    public void WordSenseBuilder_WeightsBlobWriter_Write_CommittedWsx3Weights_AreReproducedUnchanged(string package)
    {
        // Arrange: the embedded weights of the package
        var committed = package == "minilm"
            ? Resource(typeof(MiniLMWordSenseModel).Assembly, "DemaConsulting.WordSense.MiniLM.weights.bin")
            : Resource(typeof(BertBaseCasedWordSenseModel).Assembly, "DemaConsulting.WordSense.BertBaseCased.weights.bin");

        // Act
        var data = WeightsBlob.Read(committed);
        var rewritten = WeightsBlobWriter.Write(data);

        // Assert
        Assert.Null(data.EncoderSha256);
        Assert.Equal("WSX3"u8.ToArray(), Raw(committed)[..4]);
        Assert.Equal(Raw(committed), Raw(rewritten));
    }
}
