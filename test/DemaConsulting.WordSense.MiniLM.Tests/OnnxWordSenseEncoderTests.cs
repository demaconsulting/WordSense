namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the ONNX encoder against the real model. They are skipped when the Git LFS content of
///     the <c>model</c> directory is not available.
/// </summary>
public class OnnxWordSenseEncoderTests
{
    /// <summary>
    ///     The encoder reports its shape and returns one hidden state per input id.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseEncoder_EncodeAsync_ValidIds_ReturnsHiddenStates()
    {
        // Arrange: load the pinned model
        var path = Path.Combine(ModelAssets.RequireModelDirectory(), "model_int8.onnx");
        using var encoder = new OnnxWordSenseEncoder(path, 1);
        int[] ids = [101, 2501, 102];

        // Act: encode the same ids twice
        var first = await encoder.EncodeAsync(ids, TestContext.Current.CancellationToken);
        var second = await encoder.EncodeAsync(ids, TestContext.Current.CancellationToken);

        // Assert: shape, identity and determinism
        Assert.Equal(384, encoder.HiddenSize);
        Assert.Equal(WordSenseModelCatalog.OnnxSha256, encoder.ModelSha256);
        Assert.Equal(3 * 384, first.Length);
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Invalid arguments, cancellation and disposal are handled.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseEncoder_EncodeAsync_InvalidUse_Throws()
    {
        // Arrange: an encoder and a cancelled token
        var path = Path.Combine(ModelAssets.RequireModelDirectory(), "model_int8.onnx");
        using var encoder = new OnnxWordSenseEncoder(path);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act / Assert: each misuse throws the documented exception
        await Assert.ThrowsAsync<ArgumentException>(async () => await encoder.EncodeAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await encoder.EncodeAsync([101, 102], cts.Token));
        encoder.Dispose();
        encoder.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await encoder.EncodeAsync([101, 102], TestContext.Current.CancellationToken));
        Assert.Throws<FileNotFoundException>(() => new OnnxWordSenseEncoder(Path.Combine(Path.GetTempPath(), "missing.onnx")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxWordSenseEncoder(path, -1));
    }

    /// <summary>
    ///     Concurrent encodes on one encoder give the same hidden states as sequential encodes.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task OnnxWordSenseEncoder_EncodeAsync_Concurrent_MatchesSequentialResults()
    {
        var path = Path.Combine(ModelAssets.RequireModelDirectory(), "model_int8.onnx");
        using var encoder = new OnnxWordSenseEncoder(path, 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        int[][] inputs = [[101, 2501, 102], [101, 2023, 2003, 1037, 3231, 102], [101, 2501, 2501, 2501, 2501, 102]];
        var expected = new List<float[]>();
        foreach (var ids in inputs)
        {
            expected.Add(await encoder.EncodeAsync(ids, cancellationToken));
        }

        var tasks = Enumerable.Range(0, 24)
            .Select(i => Task.Run(async () => await encoder.EncodeAsync(inputs[i % inputs.Length], cancellationToken), cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(expected[i % inputs.Length], results[i]);
        }
    }

    /// <summary>
    ///     A file that is not a valid ONNX model is rejected with an exception.
    /// </summary>
    [Fact]
    public void OnnxWordSenseEncoder_Constructor_InvalidModelFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordsense-invalid-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            Assert.ThrowsAny<Exception>(() => new OnnxWordSenseEncoder(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
