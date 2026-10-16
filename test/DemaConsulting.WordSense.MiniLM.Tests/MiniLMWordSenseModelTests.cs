namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Tests of the MiniLM model against the real ONNX file. They are skipped when the Git LFS content of the
///     <c>model</c> directory is not available.
/// </summary>
public class MiniLMWordSenseModelTests
{
    private static MiniLMWordSenseModel Open(int maxSequenceLength = 256, int threads = 1)
    {
        var directory = ModelAssets.RequireModelDirectory();
        return new MiniLMWordSenseModel(
            Path.Combine(directory, "model_int8.onnx"),
            Path.Combine(directory, "vocab.txt"),
            maxSequenceLength,
            threads);
    }

    /// <summary>
    ///     The model reports its identity and shape and returns one deterministic vector per target.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors()
    {
        // Arrange: open the pinned model
        using var model = Open();
        string[] words = ["The", "record", "was", "broken"];
        int[] targets = [1, 3];

        // Act: encode the same text twice
        var first = await model.EncodeAsync(words, targets, TestContext.Current.CancellationToken);
        var second = await model.EncodeAsync(words, targets, TestContext.Current.CancellationToken);

        // Assert: identity, shape and determinism
        Assert.Equal(384, model.HiddenSize);
        Assert.Equal(WordSenseModelCatalog.OnnxSha256, model.ModelSha256);
        Assert.Equal(WordSenseModelCatalog.ModelId, model.ModelId);
        Assert.Equal(2, first.Length);
        Assert.All(first, v => Assert.Equal(384, v!.Length));
        Assert.Equal(first[0], second[0]);
        Assert.Equal(first[1], second[1]);
        Assert.NotEqual(first[0], first[1]);
    }

    /// <summary>
    ///     No target gives no vector, and a word without tokens gives a null entry.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EncodeAsync_NoTargetsOrNoTokens_ReturnsEmptyOrNull()
    {
        using var model = Open();
        var token = TestContext.Current.CancellationToken;

        var none = await model.EncodeAsync(["record"], [], token);
        var blank = await model.EncodeAsync(["a", string.Empty], [1], token);

        Assert.Empty(none);
        Assert.Null(Assert.Single(blank));
    }

    /// <summary>
    ///     A text longer than the sequence window is classified per target from a centered window.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EncodeAsync_TextLongerThanWindow_ReturnsVectorsForTargets()
    {
        using var model = Open(maxSequenceLength: 16);
        var words = Enumerable.Repeat("filler", 40).Append("record").Concat(Enumerable.Repeat("filler", 40)).ToArray();

        var vectors = await model.EncodeAsync(words, [0, 40, words.Length - 1], TestContext.Current.CancellationToken);

        Assert.All(vectors, v => Assert.Equal(384, v!.Length));
    }

    /// <summary>
    ///     Invalid arguments, cancellation and disposal are handled.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EncodeAsync_InvalidUse_Throws()
    {
        // Arrange: a model and a cancelled token
        using var model = Open();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var token = TestContext.Current.CancellationToken;

        // Act / Assert: each misuse throws the documented exception
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await model.EncodeAsync(null!, [0], token));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await model.EncodeAsync(["a"], null!, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await model.EncodeAsync(["a"], [1], token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await model.EncodeAsync(["a"], [-1], token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await model.EncodeAsync(["a"], [0], cts.Token));
        model.Dispose();
        model.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await model.EncodeAsync(["a"], [0], token));
    }

    /// <summary>
    ///     Concurrent encodes on one model give the same vectors as sequential encodes.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task MiniLMWordSenseModel_EncodeAsync_Concurrent_MatchesSequentialResults()
    {
        using var model = Open();
        var token = TestContext.Current.CancellationToken;
        string[][] inputs = [["record"], ["This", "is", "a", "test"], ["record", "record", "record", "record"]];
        var expected = new List<double[]?[]>();
        foreach (var words in inputs)
        {
            expected.Add(await model.EncodeAsync(words, [0], token));
        }

        var tasks = Enumerable.Range(0, 24)
            .Select(i => Task.Run(async () => await model.EncodeAsync(inputs[i % inputs.Length], [0], token).AsTask(), token))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(expected[i % inputs.Length][0], results[i][0]);
        }
    }

    /// <summary>
    ///     The constructor validates its arguments, and the option ranges are checked before any file is opened.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Constructor_InvalidArguments_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentNullException>(() => new MiniLMWordSenseModel(null!, "vocab.txt"));
        Assert.Throws<ArgumentNullException>(() => new MiniLMWordSenseModel("model.onnx", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMWordSenseModel(missing, missing, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMWordSenseModel(missing, missing, 513));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMWordSenseModel(missing, missing, 256, -1));
        Assert.Throws<FileNotFoundException>(() => new MiniLMWordSenseModel(missing, missing));
    }

    /// <summary>
    ///     A file that is not a valid ONNX model is rejected with an exception.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_Constructor_InvalidModelFile_Throws()
    {
        var vocabulary = Path.Combine(ModelAssets.RequireModelDirectory(), "vocab.txt");
        var path = Path.Combine(Path.GetTempPath(), $"wordsense-invalid-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            Assert.ThrowsAny<Exception>(() => new MiniLMWordSenseModel(path, vocabulary));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
