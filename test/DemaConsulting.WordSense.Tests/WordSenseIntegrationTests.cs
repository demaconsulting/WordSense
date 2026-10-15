using System.Diagnostics;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests that run the real ONNX model and the embedded table. They are skipped when the Git LFS
///     content of the <c>model</c> directory is not available.
/// </summary>
public class WordSenseIntegrationTests
{
    private static async Task<WordSenseClassifier> CreateClassifierAsync(int threads = 0)
    {
        var options = new WordSenseClassifierOptions { ModelDirectory = ModelAssets.RequireModelDirectory(), IntraOpThreads = threads };
        return await WordSenseClassifier.CreateAsync(options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     The embedded table must have been trained with exactly the pinned model file, and the pinned
    ///     digests must match the committed files; a stale table fails here.
    /// </summary>
    [Fact]
    public void EmbeddedTable_MatchesPinnedModel()
    {
        var modelDirectory = ModelAssets.RequireModelDirectory();
        var table = WordSenseTable.LoadEmbedded();

        Assert.Equal(WordSenseModelCatalog.OnnxSha256, table.ModelSha256);
        Assert.Equal(WordSenseModelCatalog.OnnxSha256, WordSenseModelCatalog.HashFile(Path.Combine(modelDirectory, "model_int8.onnx")));
        Assert.Equal(WordSenseModelCatalog.VocabularySha256, WordSenseModelCatalog.HashFile(Path.Combine(modelDirectory, "vocab.txt")));
        Assert.Equal(WordSenseModelCatalog.ModelId, table.ModelId);

        // The committed checksum list is what the documentation and the workflow verify
        var sums = File.ReadAllLines(Path.Combine(modelDirectory, "SHA256SUMS")).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains(sums, l => l.StartsWith(WordSenseModelCatalog.OnnxSha256, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sums, l => l.StartsWith(WordSenseModelCatalog.VocabularySha256, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The encoder reports its shape and returns one hidden state per input id.
    /// </summary>
    [Fact]
    public async Task OnnxEncoder_Encode_ReturnsHiddenStates()
    {
        var path = Path.Combine(ModelAssets.RequireModelDirectory(), "model_int8.onnx");
        using var encoder = new OnnxWordSenseEncoder(path, 1);
        int[] ids = [101, 2501, 102];

        var first = await encoder.EncodeAsync(ids, TestContext.Current.CancellationToken);
        var second = await encoder.EncodeAsync(ids, TestContext.Current.CancellationToken);

        Assert.Equal(384, encoder.HiddenSize);
        Assert.Equal(WordSenseModelCatalog.OnnxSha256, encoder.ModelSha256);
        Assert.Equal(3 * 384, first.Length);
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Invalid arguments, cancellation and disposal are handled.
    /// </summary>
    [Fact]
    public async Task OnnxEncoder_InvalidUse_Throws()
    {
        var path = Path.Combine(ModelAssets.RequireModelDirectory(), "model_int8.onnx");
        var encoder = new OnnxWordSenseEncoder(path);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await encoder.EncodeAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await encoder.EncodeAsync([101, 102], cts.Token));
        encoder.Dispose();
        encoder.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await encoder.EncodeAsync([101, 102], TestContext.Current.CancellationToken));
        Assert.Throws<FileNotFoundException>(() => new OnnxWordSenseEncoder(Path.Combine(Path.GetTempPath(), "missing.onnx")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxWordSenseEncoder(path, -1));
        Assert.Throws<ArgumentNullException>(() => new OnnxWordSenseEncoder(null!));
    }

    /// <summary>
    ///     End to end: a classifier created from the model directory tells the senses of "record" apart.
    /// </summary>
    [Fact]
    public async Task Classifier_EndToEnd_ClassifiesRecord()
    {
        using var classifier = await CreateClassifierAsync();

        var tokens = await classifier.ClassifyAsync(
            "The record was broken last year, so they will record the new album.",
            TestContext.Current.CancellationToken);

        var senses = tokens.Where(t => t.Sense is not null).Select(t => t.Sense!).ToList();
        Assert.Equal(["noun", "verb"], senses.Where(s => s.Lemma == "record").Select(s => s.Label));
        Assert.Contains("record", classifier.KnownLemmas);
    }

    /// <summary>
    ///     Creating a classifier from a damaged model directory fails verification.
    /// </summary>
    [Fact]
    public async Task Classifier_CreateAsync_RejectsUnverifiedDirectory()
    {
        var empty = Path.Combine(Path.GetTempPath(), "wordsense-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = empty }, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    /// <summary>
    ///     Accuracy gate: the embedded table must classify the held-out sentences with high accuracy. Every
    ///     third holdout sentence of every corpus file is evaluated, which keeps the test quick while still
    ///     covering every source file.
    /// </summary>
    [Fact]
    public async Task Classifier_Holdout_MeetsAccuracyGate()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        using var classifier = await CreateClassifierAsync();
        int total = 0, correct = 0, answered = 0, answeredCorrect = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "corpus", "holdout"), "*.tsv").Order(StringComparer.Ordinal))
        {
            var rows = (await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)).Select(l => l.TrimEnd('\r').Split('\t')).Where(c => c.Length == 3).Where((_, i) => i % 3 == 0);
            foreach (var columns in rows)
            {
                var sentence = columns[2];
                var start = sentence.IndexOf('{', StringComparison.Ordinal);
                var end = sentence.IndexOf('}', StringComparison.Ordinal);
                var text = sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);

                var result = await classifier.ClassifyAsync(text, start, end - start - 1, TestContext.Current.CancellationToken);

                total++;
                if (result is not null)
                {
                    answered++;
                    var right = result.Label == columns[1];
                    correct += right ? 1 : 0;
                    answeredCorrect += right ? 1 : 0;
                }
            }
        }

        Assert.True(total > 400, "The holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.96, $"Accuracy on answered sentences was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Coverage was {(double)answered / total:P2}.");
        Assert.True(correct == answeredCorrect);
    }

    /// <summary>
    ///     Measures median latency of one classification with one and four ONNX Runtime threads and reports it
    ///     as a diagnostic message. Run with <c>--filter-trait "Category=Benchmark"</c>.
    /// </summary>
    /// <param name="threads">The ONNX Runtime intra-op thread count.</param>
    /// <returns>A task that completes when the measurement is done.</returns>
    [Theory]
    [Trait("Category", "Benchmark")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Benchmark_ClassifyLatency(int threads)
    {
        using var classifier = await CreateClassifierAsync(threads);
        const string text = "She will record the vote in the record book, then lead the team to the lead mine.";
        var token = TestContext.Current.CancellationToken;
        for (var i = 0; i < 20; i++)
        {
            await classifier.ClassifyAsync(text, token);
        }

        var samples = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var watch = Stopwatch.StartNew();
            await classifier.ClassifyAsync(text, token);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        TestContext.Current.TestOutputHelper?.WriteLine($"classify latency, {threads} thread(s): median {samples[50]:0.0} ms, p95 {samples[94]:0.0} ms");
        Assert.True(samples[50] < 2000);
    }
}
