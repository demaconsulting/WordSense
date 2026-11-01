namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the layered sense vector of <see cref="OnnxEncoder"/> with a tiny tapped graph (see
///     <see cref="TappedGraph"/>): the hidden state of layer <c>n</c> at a token is the token id times a constant
///     vector of its own, so every expected vector is computed without a model file.
/// </summary>
public class OnnxEncoderLayerTests
{
    private static readonly string[] Sentence = ["alpha", "beta", "alphas", "gamma"];
    private static readonly int[] Taps = [9, 10, 11];
    private static readonly int[] Layers = [9, 10, 11, 12];

    private static OnnxEncoder Open(string directory, IReadOnlyList<int>? layers, int maxSequenceLength = 128) =>
        new(Path.Join(directory, "model-int8-static.onnx"), Path.Join(directory, "vocab.txt"), true, maxSequenceLength, 1, layers);

    /// <summary>
    ///     The mean over the word pieces of a layer, for a word whose pieces have the given ids.
    /// </summary>
    private static double[] LayerMean(int layer, params int[] ids) =>
        [.. TappedGraph.Constant(layer).Select(c => ids.Average(i => (double)i * c))];

    private static double[] MeanOfLayers(IEnumerable<int> layers, params int[] ids) =>
        [.. Enumerable.Range(0, TappedGraph.HiddenSize).Select(d => layers.Average(l => LayerMean(l, ids)[d]))];

    private static void AssertClose(double[] expected, double[]? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var d = 0; d < expected.Length; d++)
        {
            Assert.Equal(expected[d], actual[d], 1e-9);
        }
    }

    /// <summary>
    ///     With sense layers the sense vector of a word is the mean, over the layers, of the per-layer means of its word
    ///     pieces: one piece for <c>alpha</c>, two for <c>alphas</c>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_SenseLayers_ReturnsMeanOfLayers()
    {
        // Arrange: the graph exposes hidden_9 to hidden_11 and last_hidden_state (layer 12)
        var directory = TappedGraph.Write(Taps);
        try
        {
            using var encoder = Open(directory, Layers);

            // Act
            var vectors = await encoder.EncodeAsync(Sentence, [0, 1, 2], TestContext.Current.CancellationToken);

            // Assert: alpha = id 4, beta = id 5, alphas = ids 4 and 7
            Assert.Equal(TappedGraph.HiddenSize, encoder.HiddenSize);
            AssertClose(MeanOfLayers(Layers, 4), vectors[0]);
            AssertClose(MeanOfLayers(Layers, 5), vectors[1]);
            AssertClose(MeanOfLayers(Layers, 4, 7), vectors[2]);
            Assert.NotEqual(LayerMean(12, 4), vectors[0]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     The sense vector of a text equals the sense vector of <see cref="OnnxEncoder.EncodeAsync"/> for every word,
    ///     and the mean of the layers does not depend on whether the text is cut into windows.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_SenseLayers_EqualsEncodeAsyncAndWindows()
    {
        // Arrange: a window of 8 tokens holds 6 word pieces, so the 15 pieces of the three sentences need several windows
        var directory = TappedGraph.Write(Taps);
        try
        {
            using var encoder = Open(directory, Layers);
            using var windowed = Open(directory, Layers, 8);
            string[] text = [.. Sentence, .. Sentence, .. Sentence];
            var all = Enumerable.Range(0, text.Length).ToArray();

            // Act
            var expected = await encoder.EncodeAsync(text, all, TestContext.Current.CancellationToken);
            var whole = await encoder.EncodeAllAsync(text, TestContext.Current.CancellationToken);
            var parts = await windowed.EncodeAllAsync(text, TestContext.Current.CancellationToken);

            // Assert: the graph has no context, so every window gives the same vector for the same word
            Assert.Equal(expected, whole);
            for (var w = 0; w < text.Length; w++)
            {
                AssertClose(expected[w]!, parts[w]);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     The head vector is the last layer: it equals what an encoder without sense layers returns, and the sense
    ///     vector of the view is the mean of the layers.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeHeadAllAsync_SenseLayers_ReturnsLastLayerUnchanged()
    {
        // Arrange: the same graph read with and without sense layers
        var directory = TappedGraph.Write(Taps);
        try
        {
            using var layered = Open(directory, Layers);
            using var plain = Open(directory, null);

            // Act
            var head = await layered.EncodeHeadAllAsync(Sentence, TestContext.Current.CancellationToken);
            var last = await plain.EncodeAllAsync(Sentence, TestContext.Current.CancellationToken);
            var plainHead = await plain.EncodeHeadAllAsync(Sentence, TestContext.Current.CancellationToken);
            var view = await layered.EncodeAllAsync(Sentence, _ => false, TestContext.Current.CancellationToken);
            var plainView = await plain.EncodeAllAsync(Sentence, _ => false, TestContext.Current.CancellationToken);

            // Assert: bit-identical last layer, the layer mean for the senses, no sense vectors without layers
            Assert.Equal(last, head);
            Assert.Equal(last, plainHead);
            AssertClose(LayerMean(12, 4), head[0]);
            Assert.Equal(head, view.Vectors);
            Assert.NotNull(view.SenseVectors);
            AssertClose(MeanOfLayers(Layers, 5), view.SenseVectors[1]);
            Assert.Equal(last, plainView.Vectors);
            Assert.Null(plainView.SenseVectors);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     An encoder without sense layers keeps the last layer as its sense vector, whichever other outputs the graph has.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_NoSenseLayers_ReturnsLastLayer()
    {
        // Arrange: a graph with taps, opened without sense layers
        var directory = TappedGraph.Write(Taps);
        try
        {
            using var encoder = Open(directory, null);

            // Act
            var vectors = await encoder.EncodeAsync(Sentence, [0, 2], TestContext.Current.CancellationToken);

            // Assert
            AssertClose(LayerMean(12, 4), vectors[0]);
            AssertClose(LayerMean(12, 4, 7), vectors[1]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A graph that lacks the output of a sense layer is rejected with the name of the missing output.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_MissingTapOutput_ThrowsInvalidData()
    {
        // Arrange: the graph has no hidden_10, and another has no taps at all
        var gap = TappedGraph.Write(9, 11);
        var none = TappedGraph.Write();
        try
        {
            // Act
            var missing = Assert.Throws<InvalidDataException>(() => Open(gap, Layers));
            var noTaps = Assert.Throws<InvalidDataException>(() => Open(none, Layers));

            // Assert
            Assert.Contains("hidden_10", missing.Message, StringComparison.Ordinal);
            Assert.Contains("hidden_9", noTaps.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(gap, true);
            Directory.Delete(none, true);
        }
    }

    /// <summary>
    ///     A list of sense layers that is not a mean of increasing layers is rejected.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_InvalidSenseLayers_ThrowsArgument()
    {
        // Arrange
        var directory = TappedGraph.Write(Taps);
        try
        {
            // Act / Assert: one layer is the layout "last"; the layers must be at least 1 and increase
            Assert.Throws<ArgumentException>(() => Open(directory, [12]));
            Assert.Throws<ArgumentException>(() => Open(directory, []));
            Assert.Throws<ArgumentException>(() => Open(directory, [11, 9, 12]));
            Assert.Throws<ArgumentException>(() => Open(directory, [0, 12]));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     The layout of the sense layers is <c>last</c>, a range for consecutive layers and a list otherwise.
    /// </summary>
    [Fact]
    public void FeatureLayout_Describe_Layers_ReturnsLayout()
    {
        // Arrange / Act / Assert
        Assert.Equal("last", FeatureLayout.Describe(null));
        Assert.Equal("mean:9-12", FeatureLayout.Describe([9, 10, 11, 12]));
        Assert.Equal("mean:9,11,12", FeatureLayout.Describe([9, 11, 12]));
        Assert.Equal(["hidden_9", "hidden_10", "last_hidden_state"], FeatureLayout.OutputNames([9, 10, 12]));
    }

    /// <summary>
    ///     A model whose description has sense layers and whose graph lacks a tap output is reported as
    ///     <see cref="ModelIntegrityException"/>; with every tap the same model answers.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MissingTapOutput_ThrowsModelIntegrity()
    {
        // Arrange: the same description over a graph with every tap and over a graph without hidden_10
        var complete = TappedGraph.Write(Taps);
        var gap = TappedGraph.Write(9, 11);
        try
        {
            using var good = new TappedModel(complete);
            using var broken = new TappedModel(gap);

            // Act
            var answers = await good.ClassifyAsync(["It", "was", "just", "fine"], TestContext.Current.CancellationToken);
            var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => broken.ClassifyAsync(["It", "was", "just", "fine"], TestContext.Current.CancellationToken));

            // Assert: the damaged graph is named by its missing output, and its encoder is released
            Assert.Equal(4, answers.Length);
            Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.Contains("hidden_10", failure.Message, StringComparison.Ordinal);
            Assert.True(broken.IsEncoderReleased);
        }
        finally
        {
            Directory.Delete(complete, true);
            Directory.Delete(gap, true);
        }
    }

    /// <summary>
    ///     The model pins embedded weights of the layer mean: weights of the last layer, or of another encoder file, are
    ///     reported as <see cref="ModelIntegrityException"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_WeightsOfOtherLayoutOrEncoder_ThrowsModelIntegrity()
    {
        // Arrange: the graph is complete, but the weights were trained on the last layer, or on another encoder file
        var directory = TappedGraph.Write(Taps);
        try
        {
            using var lastLayer = new TappedModel(directory, layout: FeatureLayout.Last);
            using var otherEncoder = new TappedModel(directory, encoderSha256: new string('1', 64));

            // Act
            var layout = await Assert.ThrowsAsync<ModelIntegrityException>(() => lastLayer.ClassifyAsync(["It", "was", "just", "fine"], TestContext.Current.CancellationToken));
            var encoder = await Assert.ThrowsAsync<ModelIntegrityException>(() => otherEncoder.ClassifyAsync(["It", "was", "just", "fine"], TestContext.Current.CancellationToken));

            // Assert
            Assert.Contains("'last', not 'mean:9-12'", layout.Message, StringComparison.Ordinal);
            Assert.Contains("encoder file", encoder.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A model over the tiny tapped graph, with embedded weights of the layer mean <c>mean:9-12</c>.
    /// </summary>
    private sealed class TappedModel : OnnxWordSenseModel
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TappedModel"/> class.
        /// </summary>
        /// <param name="directory">The directory of the graph and the vocabulary.</param>
        /// <param name="layout">The layout the weights record.</param>
        /// <param name="encoderSha256">The encoder digest the weights record, or null for the digest of the graph.</param>
        public TappedModel(string directory, string layout = "mean:9-12", string? encoderSha256 = null)
            : base(CreateSpec(directory, layout, encoderSha256), directory, 128)
        {
        }

        private static ModelSpec CreateSpec(string directory, string layout, string? encoderSha256)
        {
            var onnx = Path.Join(directory, "model-int8-static.onnx");
            var vocabulary = Path.Join(directory, "vocab.txt");
            var sha = TappedGraph.Sha256(onnx);
            var spec = new ModelSpec(
                "tapped",
                1,
                [
                    new ModelFile("model-int8-static.onnx", sha, new FileInfo(onnx).Length),
                    new ModelFile("vocab.txt", TappedGraph.Sha256(vocabulary), new FileInfo(vocabulary).Length),
                ],
                "model-int8-static.onnx",
                "vocab.txt",
                true,
                typeof(TappedModel).Assembly,
                "tapped.weights.bin",
                TappedGraph.HiddenSize,
                SenseLayers: [9, 10, 11, 12]);
            var senses = Fixtures.SenseIds("just");
            var lemma = new WeightedLemma("just", senses, new LemmaWeights(new float[senses.Length * TappedGraph.HiddenSize], new float[senses.Length], TappedGraph.HiddenSize), []);
            var data = new WeightsData(spec.WeightsId, Fixtures.InventoryHash(), TappedGraph.HiddenSize, new float[TappedGraph.HiddenSize], [lemma], Convert.FromHexString(encoderSha256 ?? sha), layout);
            return spec with { WeightsAssembly = new FixtureAssembly("tapped.weights.bin", BlobWriter.Write(data)) };
        }
    }
}
