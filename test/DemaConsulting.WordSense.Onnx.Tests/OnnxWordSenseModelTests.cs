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
    ///     A caller-supplied directory is verified without network access, and a classifier can then be created.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesEveryFile()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());

        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var classifier = model.Create();
        var result = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);
        Assert.NotNull(result[2].Details);
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

            Assert.NotNull(model.Create());
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
    ///     Creating a classifier before the files are available is an error.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_Create_BeforeEnsure_ThrowsInvalidOperation()
    {
        using var model = Open();

        var ex = Assert.Throws<InvalidOperationException>(model.Create);

        Assert.Contains("EnsureModelAsync", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two classifiers of one model share one encoder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Create_TwiceAfterEnsure_ShareTheEncoder()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var first = model.Create();
        var second = model.Create();

        var field = typeof(OnnxClassifier).GetField("_encoder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotSame(first, second);
        Assert.Same(field.GetValue(first), field.GetValue(second));
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
    ///     Creating a classifier after disposal is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Create_AfterDispose_ThrowsObjectDisposed()
    {
        var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        model.Dispose();

        Assert.Throws<ObjectDisposedException>(model.Create);
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
    public void OnnxWordSenseModel_Dispose_Concurrent_IsHarmless()
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
        Assert.Throws<ObjectDisposedException>(model.Create);
    }

    /// <summary>
    ///     A classifier created earlier rejects every call once its model is disposed, whatever the words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Classifier_AfterModelDispose_ThrowsObjectDisposed()
    {
        var model = Open(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var classifier = model.Create();
        model.Dispose();

        // "the" never needs the encoder and an empty list needs nothing at all: both are still rejected
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(["record"], TestContext.Current.CancellationToken));
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
            Assert.Throws<ObjectDisposedException>(model.Create);

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
