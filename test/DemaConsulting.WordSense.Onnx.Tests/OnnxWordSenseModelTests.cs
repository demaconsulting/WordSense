using System.Net;
using System.Reflection;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxWordSenseModel"/>, using the MiniLM package as the concrete model and the real model
///     files of the repository. They are skipped when the files are not available.
/// </summary>
public class OnnxWordSenseModelTests
{
    private static readonly string[] ModelFiles = ["model_int8.onnx", "vocab.txt"];

    private static MiniLMWordSenseModel Open(string? directory = null) => new(directory) { IntraOpThreads = 2 };

    private static Task ServeModel(HttpListenerContext context, string directory) =>
        LocalServer.SendFileAsync(context, Path.Combine(directory, Path.GetFileName(context.Request.Url!.AbsolutePath)));

    /// <summary>
    ///     The identity is available before any file is.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_ModelIdAndHiddenSize_AreAvailableBeforeEnsure()
    {
        using var model = Open();

        Assert.Equal("all-MiniLM-L6-v2-int8", model.ModelId);
        Assert.Equal(384, model.HiddenSize);
    }

    /// <summary>
    ///     A sequence length outside 8 to 512 is rejected.
    /// </summary>
    /// <param name="length">The sequence length.</param>
    [Theory]
    [InlineData(7)]
    [InlineData(513)]
    public void OnnxWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMWordSenseModel(null, length));
    }

    /// <summary>
    ///     A caller-supplied directory is verified without network access, and the model can then classify.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesEveryFile()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var result = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);
        Assert.NotNull(result[2].Details);
    }

    /// <summary>
    ///     A list of single-sense and unknown words is answered from the lexicon alone: no ensure, no model file, no ONNX.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_LexiconOnlyWords_NeverTouchesModelFiles()
    {
        // The directory does not exist: an ensure would throw FileNotFoundException
        var missing = Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));
        using var model = Open(missing);

        var result = await model.ClassifyAsync(["The", "zzqxjw", "."], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Determiner, result[0].Details!.PartOfSpeech);
        Assert.Equal(1.0, result[0].Confidence);
        Assert.Null(result[1].Details);
        Assert.Null(result[2].Details);
        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    ///     An already canceled token is honored even when the list is empty or needs no model: the call faults with
    ///     <see cref="OperationCanceledException"/> before any work, as the Basic model does.
    /// </summary>
    /// <param name="word">The single word of the list, or null for an empty list.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("The")]
    public async Task OnnxWordSenseModel_ClassifyAsync_CanceledTokenWithoutModelWork_Throws(string? word)
    {
        var missing = Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));
        using var model = Open(missing);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.ClassifyAsync(word is null ? [] : [word], cts.Token));
        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    ///     Words with several senses but no trained weights (so, will) are answered from the lexicon alone with their
    ///     first sense and a confidence of one over the number of senses: no ensure, no model file, no ONNX.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MultiSenseWordsWithoutWeights_FallBackWithoutModelFiles()
    {
        // The directory does not exist: an ensure would throw FileNotFoundException
        var missing = Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));
        using var model = Open(missing);

        var result = await model.ClassifyAsync(["so", "will"], TestContext.Current.CancellationToken);

        foreach (var (word, sense) in new[] { "so", "will" }.Zip(result))
        {
            var senses = WordLexicon.View[word];
            Assert.True(senses.Length > 1);

            // A trailing untrained Auxiliary sense is not counted (encoder models never answer it)
            var counted = senses[^1].PartOfSpeech == PartOfSpeech.Auxiliary ? senses.Length - 1 : senses.Length;
            Assert.Equal(new WordSense(word, senses[0], 1.0 / counted), sense);
        }

        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    ///     An ambiguous word makes the model ensure itself lazily, without a prior EnsureModelAsync call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_WithoutEnsure_EnsuresLazily()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());

        var result = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        Assert.NotNull(result[2].Details);
        Assert.NotNull(result[4].Details);
    }

    /// <summary>
    ///     Concurrent classifications that need the model share one lazy ensure and give identical results.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_Concurrent_ShareOneLazyEnsure()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());
        string[] words = ["They", "will", "record", "the", "record", "."];

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => model.ClassifyAsync(words, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(runs, run => Assert.Equal(runs[0], run));
    }

    /// <summary>
    ///     A failed lazy ensure is retried by the next classification.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_AfterFailedLazyEnsure_CanBeRetried()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Combine(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var model = Open(directory);
            string[] words = ["They", "will", "record", "the", "record", "."];

            await Assert.ThrowsAsync<FileNotFoundException>(() => model.ClassifyAsync(words, TestContext.Current.CancellationToken));
            Directory.CreateDirectory(directory);
            foreach (var name in ModelFiles)
            {
                File.Copy(Path.Combine(source, name), Path.Combine(directory, name));
            }

            var result = await model.ClassifyAsync(words, TestContext.Current.CancellationToken);
            Assert.NotNull(result[2].Details);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    ///     A missing file in the supplied directory is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_MissingFile_Throws()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Combine(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(source, "vocab.txt"), Path.Combine(directory, "vocab.txt"));
            using var model = Open(directory);

            await Assert.ThrowsAsync<FileNotFoundException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A file with the wrong content in the supplied directory is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AlteredFile_Throws()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Combine(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(source, "vocab.txt"), Path.Combine(directory, "vocab.txt"));
            await File.WriteAllTextAsync(Path.Combine(directory, "model_int8.onnx"), "altered", TestContext.Current.CancellationToken);
            using var model = Open(directory);

            await Assert.ThrowsAsync<InvalidDataException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     Concurrent preparation calls share one download.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_Concurrent_SharesOneDownload()
    {
        var source = ModelAssets.RequireModelDirectory();
        var store = Path.Combine(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var server = new LocalServer(c => ServeModel(c, source));
            using var model = Open();
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken)));

            Assert.Equal(ModelFiles.Length, server.Requests.Count);
        }
        finally
        {
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }

    /// <summary>
    ///     A failed preparation can be retried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AfterFailure_CanBeRetried()
    {
        var source = ModelAssets.RequireModelDirectory();
        var store = Path.Combine(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var healthy = false;
            using var server = new LocalServer(c => healthy ? ServeModel(c, source) : LocalServer.SendAsync(c, 500, "broken"));
            using var model = Open();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            await Assert.ThrowsAsync<HttpRequestException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));
            healthy = true;
            await model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

            var result = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);
            Assert.NotNull(result[2].Details);
        }
        finally
        {
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }

    /// <summary>
    ///     A model that was never ensured and is asked about an ambiguous word with an unavailable directory fails with
    ///     the ensure error, not with an invalid-operation error.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MissingDirectory_ThrowsFileNotFound()
    {
        using var model = Open(Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<FileNotFoundException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Many classifications of one model share one encoder and one classifier.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_Repeated_ShareTheEncoder()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken);
        var field = typeof(OnnxWordSenseModel).GetField("_classifier", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var first = field.GetValue(model);
        await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Same(first, field.GetValue(model));
    }

    /// <summary>
    ///     A caller whose token is cancelled stops waiting, but the shared work is not cancelled.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_CancelledCaller_DoesNotCancelSharedWork()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.EnsureModelAsync(null, cts.Token));

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var result = await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    /// <summary>
    ///     Disposing while a classification is registered keeps the encoder usable until the classification finishes;
    ///     the last one to finish releases it, and new calls are rejected at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_DuringClassification_DefersEncoderRelease()
    {
        var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var classifier = model.AcquireClassifier();
        Assert.False(model.IsEncoderReleased);

        model.Dispose();

        // The running classification still completes on the live session; new calls are refused
        Assert.False(model.IsEncoderReleased);
        var result = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);
        Assert.NotNull(result[2].Details);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(model.AcquireClassifier);

        model.ReleaseClassifier();

        Assert.True(model.IsEncoderReleased);
        Assert.Null(Record.Exception(model.Dispose));
    }

    /// <summary>
    ///     Disposing while no classification runs releases the encoder at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_Idle_ReleasesEncoderImmediately()
    {
        var model = Open(ModelAssets.RequireModelDirectory());
        await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken);
        Assert.False(model.IsEncoderReleased);

        model.Dispose();

        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     Disposing twice is harmless.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_Dispose_CalledTwice_IsHarmless()
    {
        var model = Open();

        model.Dispose();

        var ex = Record.Exception(model.Dispose);
        Assert.Null(ex);
    }

    /// <summary>
    ///     Classifying after disposal is rejected, whatever the words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        model.Dispose();

        // "the" never needs the encoder and an empty list needs nothing at all: both are still rejected
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Preparing after disposal is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = Open();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Disposing from many threads at once releases once and never throws.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_Concurrent_IsHarmless()
    {
        var model = Open();

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ =>
        {
            try
            {
                model.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Disposing while a download is in flight cancels it: the pending calls throw
    ///     <see cref="OperationCanceledException"/>, nothing hangs and no partial file remains.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_DuringEnsure_CancelsDownload()
    {
        var store = Path.Combine(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // The server starts a response and then stalls until the test ends
            using var server = new LocalServer(async c =>
            {
                c.Response.StatusCode = 200;
                c.Response.ContentLength64 = 10_000_000;
                await c.Response.OutputStream.WriteAsync(new byte[16]);
                await c.Response.OutputStream.FlushAsync();
                await release.Task;
            });
            var model = Open();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            var pending = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);
            var second = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (server.Requests.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.NotEmpty(server.Requests);
            model.Dispose();

            // Every waiter ends with the cancellation exception, promptly
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));

            release.TrySetResult();
            var files = Directory.Exists(store) ? Directory.GetFiles(store, "*", SearchOption.AllDirectories) : [];
            Assert.DoesNotContain(files, path => path.EndsWith(".part", StringComparison.Ordinal) || path.EndsWith(".onnx", StringComparison.Ordinal));
        }
        finally
        {
            release.TrySetResult();
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }
}
