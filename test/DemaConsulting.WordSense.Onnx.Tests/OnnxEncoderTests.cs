namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxEncoder"/> with the real MiniLM model files of the repository. They are skipped when
///     the files are not available (for example without Git LFS content).
/// </summary>
public class OnnxEncoderTests
{
    private static readonly string[] Sentence = ["They", "will", "record", "the", "record", "."];

    private static OnnxEncoder Open(int maxSequenceLength = 128, int intraOpThreads = 2)
    {
        var directory = ModelAssets.RequireModelDirectory();
        return new OnnxEncoder(
            Path.Combine(directory, "model_int8.onnx"),
            Path.Combine(directory, "vocab.txt"),
            true,
            maxSequenceLength,
            intraOpThreads);
    }

    private static double Distance(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    /// <summary>
    ///     The same text and targets always give the same vectors, one per target, of the hidden size.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors()
    {
        using var encoder = Open();

        var first = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);
        var second = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);

        Assert.Equal(384, encoder.HiddenSize);
        Assert.Equal(2, first.Length);
        Assert.All(first, v => Assert.Equal(384, v!.Length));
        Assert.Equal(first[0], second[0]);
        Assert.Equal(first[1], second[1]);

        // The same word in two contexts gets two different vectors
        Assert.True(Distance(first[0]!, first[1]!) > 1e-3);
    }

    /// <summary>
    ///     No targets give an empty result, and a word without word pieces gives no vector.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_NoTargetsOrNoTokens_ReturnsEmptyOrNull()
    {
        using var encoder = Open();

        var none = await encoder.EncodeAsync(Sentence, [], TestContext.Current.CancellationToken);
        var noTokens = await encoder.EncodeAsync(["record", "\u0001"], [0, 1], TestContext.Current.CancellationToken);

        Assert.Empty(none);
        Assert.NotNull(noTokens[0]);
        Assert.Null(noTokens[1]);
    }

    /// <summary>
    ///     Concurrent calls on one encoder return what sequential calls return.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_Concurrent_MatchesSequentialResults()
    {
        using var encoder = Open();
        var expected = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(
            async () => await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        Assert.All(results, r =>
        {
            Assert.Equal(expected[0], r[0]);
            Assert.Equal(expected[1], r[1]);
        });
    }

    /// <summary>
    ///     A text longer than the window still gives a vector for every target.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_TextLongerThanWindow_ReturnsVectorsForTargets()
    {
        using var encoder = Open(maxSequenceLength: 16);
        var words = Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? "record" : "the").ToArray();

        var vectors = await encoder.EncodeAsync(words, [0, 30, 58], TestContext.Current.CancellationToken);

        Assert.All(vectors, v => Assert.Equal(384, v!.Length));
    }

    /// <summary>
    ///     The thread count does not change the vectors beyond rounding.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_Constructor_IntraOpThreads_ProducesSameVectors()
    {
        using var single = Open(intraOpThreads: 1);
        using var multiple = Open(intraOpThreads: 2);

        var a = await single.EncodeAsync(Sentence, [2], TestContext.Current.CancellationToken);
        var b = await multiple.EncodeAsync(Sentence, [2], TestContext.Current.CancellationToken);

        Assert.True(Distance(a[0]!, b[0]!) < 1e-3);
    }

    /// <summary>
    ///     Bad arguments, a cancelled call and use after disposal are rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_InvalidUse_Throws()
    {
        using var encoder = Open();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(null!, [0], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(Sentence, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await encoder.EncodeAsync(Sentence, [-1], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await encoder.EncodeAsync(Sentence, [6], TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await encoder.EncodeAsync(Sentence, [0], cancelled.Token));

        encoder.Dispose();
        encoder.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await encoder.EncodeAsync(Sentence, [0], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Null paths and out-of-range limits are rejected.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_InvalidArguments_Throws()
    {
        var directory = ModelAssets.RequireModelDirectory();
        var model = Path.Combine(directory, "model_int8.onnx");
        var vocabulary = Path.Combine(directory, "vocab.txt");

        Assert.Throws<ArgumentNullException>(() => new OnnxEncoder(null!, vocabulary, true, 128, 2));
        Assert.Throws<ArgumentNullException>(() => new OnnxEncoder(model, null!, true, 128, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 7, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 513, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 128, -1));
    }

    /// <summary>
    ///     A missing or unusable model file is rejected.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_InvalidModelFile_Throws()
    {
        var directory = ModelAssets.RequireModelDirectory();
        var vocabulary = Path.Combine(directory, "vocab.txt");
        var garbage = Path.Combine(Path.GetTempPath(), "wordsense-garbage-" + Guid.NewGuid().ToString("N") + ".onnx");
        File.WriteAllText(garbage, "this is not an onnx model");

        try
        {
            Assert.Throws<FileNotFoundException>(() => new OnnxEncoder(garbage + ".missing", vocabulary, true, 128, 2));
            Assert.ThrowsAny<Exception>(() => new OnnxEncoder(garbage, vocabulary, true, 128, 2));
        }
        finally
        {
            File.Delete(garbage);
        }
    }
}
