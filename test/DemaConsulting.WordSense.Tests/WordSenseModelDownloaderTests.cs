using System.Net;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseModelDownloader"/> and <see cref="WordSenseModelLocation"/>, using only a
///     loopback server.
/// </summary>
public sealed class WordSenseModelDownloaderTests : IDisposable
{
    private readonly string _store = Path.Combine(Path.GetTempPath(), "wordsense-tests-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_store))
        {
            Directory.Delete(_store, true);
        }
    }

    private static string Model(string name) => Path.Combine(ModelAssets.RequireModelDirectory(), name);

    private static string FileOf(HttpListenerContext context) => Path.GetFileName(context.Request.Url!.AbsolutePath);

    private static Task ServeReal(HttpListenerContext context) => LocalServer.SendFileAsync(context, Model(FileOf(context)));

    private WordSenseModelDownloaderOptions Options(LocalServer server, DownloadMirror? mirror = null, Func<string, string?>? env = null)
    {
        return new WordSenseModelDownloaderOptions
        {
            Mirror = mirror,
            GetEnvironmentVariable = env ?? (_ => null),
            HuggingFaceBaseUri = new Uri(server.BaseUri, "/hf"),
            GitHubLfsBaseUri = new Uri(server.BaseUri, "/lfs"),
            GitHubLfsRef = "main",
            StoreRoot = _store,
            RequestTimeout = TimeSpan.FromSeconds(30),
        };
    }

    private static DownloadMirror MirrorOn(LocalServer server, string? bearer = null)
    {
        return new DownloadMirror(new Uri(server.BaseUri, "/mirror/"), null, bearer);
    }

    private static string[] Paths(LocalServer server) => server.Requests.Select(r => r.Path).ToArray();

    /// <summary>
    ///     The mirror is asked first and, when it serves valid files, nothing else is contacted.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_MirrorServes_UsesOnlyMirror()
    {
        using var server = new LocalServer(ServeReal);

        var location = await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), TestContext.Current.CancellationToken);

        Assert.All(Paths(server), p => Assert.StartsWith("/mirror/all-MiniLM-L6-v2/", p, StringComparison.Ordinal));
        Assert.Equal(2, Paths(server).Length);
        Assert.True(File.Exists(location.ModelPath));
        Assert.True(File.Exists(location.VocabularyPath));
    }

    /// <summary>
    ///     When the mirror fails, vocab falls to Hugging Face and the ONNX file, which Hugging Face does not
    ///     publish in this form, falls to the GitHub LFS endpoint without ever asking Hugging Face.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_MirrorFails_FallsThroughInOrder()
    {
        using var server = new LocalServer(context => context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal)
            ? LocalServer.SendAsync(context, 503, "down")
            : ServeReal(context));

        await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "/mirror/all-MiniLM-L6-v2/model_int8.onnx",
                "/lfs/main/model/model_int8.onnx",
                "/mirror/all-MiniLM-L6-v2/vocab.txt",
                "/hf/vocab.txt",
            ],
            Paths(server));
    }

    /// <summary>
    ///     Without a mirror, Hugging Face is tried first for files it can supply and LFS is the fallback.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_NoMirror_HuggingFaceThenLfs()
    {
        using var server = new LocalServer(context => context.Request.Url!.AbsolutePath.StartsWith("/hf", StringComparison.Ordinal)
            ? LocalServer.SendAsync(context, 404, "no")
            : ServeReal(context));

        await WordSenseModelDownloader.EnsureModelAsync(Options(server), TestContext.Current.CancellationToken);

        Assert.Equal(["/lfs/main/model/model_int8.onnx", "/hf/vocab.txt", "/lfs/main/model/vocab.txt"], Paths(server));
    }

    /// <summary>
    ///     A source that serves wrong bytes is rejected, nothing is kept, and the next source is used.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_HashMismatch_FallsThroughAndKeepsNothingBad()
    {
        using var server = new LocalServer(context => context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal)
            ? LocalServer.SendAsync(context, 200, "this is not the model")
            : ServeReal(context));

        var location = await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), TestContext.Current.CancellationToken);

        Assert.Equal(WordSenseModelCatalog.OnnxSha256, WordSenseModelCatalog.HashFile(location.ModelPath));
        Assert.Empty(Directory.GetFiles(location.Directory, "*.part"));
        Assert.Contains("/lfs/main/model/model_int8.onnx", Paths(server));
    }

    /// <summary>
    ///     When every source fails, the exception lists each attempt and no partial file remains.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_AllSourcesFail_ThrowsWithEveryAttempt()
    {
        using var server = new LocalServer(context => LocalServer.SendAsync(context, 500, "broken"));

        var ex = await Assert.ThrowsAsync<WordSenseModelDownloadException>(
            () => WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), TestContext.Current.CancellationToken));

        Assert.Equal(2, ex.Failures.Count);
        Assert.Contains("mirror", ex.Failures[0], StringComparison.Ordinal);
        Assert.Contains("github-lfs", ex.Failures[1], StringComparison.Ordinal);
        var directory = Path.Combine(_store, WordSenseModelCatalog.DirectoryName);
        Assert.Empty(Directory.GetFiles(directory));
    }

    /// <summary>
    ///     An environment-configured mirror is asked first.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_MirrorFromEnvironment_IsUsedFirst()
    {
        using var server = new LocalServer(ServeReal);
        var url = new Uri(server.BaseUri, "/base-from-environment").ToString();

        await WordSenseModelDownloader.EnsureModelAsync(
            Options(server, null, name => name == DownloadMirror.UrlEnvironmentVariable ? url : null),
            TestContext.Current.CancellationToken);

        Assert.All(Paths(server), p => Assert.StartsWith("/base-from-environment/all-MiniLM-L6-v2/", p, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A programmatic mirror wins over the environment.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_ProgrammaticMirror_BeatsEnvironment()
    {
        using var server = new LocalServer(ServeReal);
        var url = new Uri(server.BaseUri, "/base-from-environment").ToString();

        await WordSenseModelDownloader.EnsureModelAsync(
            Options(server, MirrorOn(server), name => name == DownloadMirror.UrlEnvironmentVariable ? url : null),
            TestContext.Current.CancellationToken);

        Assert.All(Paths(server), p => Assert.StartsWith("/mirror/", p, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A bad environment configuration fails before any request.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_InvalidEnvironment_ThrowsBeforeRequests()
    {
        using var server = new LocalServer(ServeReal);

        await Assert.ThrowsAsync<ArgumentException>(
            () => WordSenseModelDownloader.EnsureModelAsync(
                Options(server, null, name => name == DownloadMirror.UserEnvironmentVariable ? "user" : null),
                TestContext.Current.CancellationToken));

        Assert.Empty(server.Requests);
    }

    /// <summary>
    ///     The bearer token goes to the mirror and never to the public sources.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_BearerToken_SentOnlyToMirror()
    {
        using var server = new LocalServer(context => context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal)
            ? LocalServer.SendAsync(context, 500, "fail")
            : ServeReal(context));

        await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server, "tok-123")), TestContext.Current.CancellationToken);

        Assert.All(
            server.Requests,
            r =>
            {
                if (r.Path.StartsWith("/mirror", StringComparison.Ordinal))
                {
                    Assert.Equal("Bearer tok-123", r.Authorization);
                }
                else
                {
                    Assert.Null(r.Authorization);
                }
            });
    }

    /// <summary>
    ///     Basic credentials are sent preemptively to the mirror.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_BasicCredentials_SentToMirror()
    {
        using var server = new LocalServer(ServeReal);
        var mirror = new DownloadMirror(new Uri(server.BaseUri, "/mirror"), new NetworkCredential("user", "pw"));

        await WordSenseModelDownloader.EnsureModelAsync(Options(server, mirror), TestContext.Current.CancellationToken);

        Assert.All(server.Requests, r => Assert.Equal("Basic " + Convert.ToBase64String("user:pw"u8.ToArray()), r.Authorization));
    }

    /// <summary>
    ///     Valid files already in the store are reused without any request.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_AlreadyPresent_MakesNoRequests()
    {
        using var server = new LocalServer(ServeReal);
        await WordSenseModelDownloader.EnsureModelAsync(Options(server), TestContext.Current.CancellationToken);
        var before = server.Requests.Count;

        await WordSenseModelDownloader.EnsureModelAsync(Options(server), TestContext.Current.CancellationToken);

        Assert.Equal(before, server.Requests.Count);
    }

    /// <summary>
    ///     A corrupt file in the store is replaced.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_CorruptExistingFile_IsRedownloaded()
    {
        using var server = new LocalServer(ServeReal);
        var directory = Path.Combine(_store, WordSenseModelCatalog.DirectoryName);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "vocab.txt"), "corrupt", TestContext.Current.CancellationToken);

        var location = await WordSenseModelDownloader.EnsureModelAsync(Options(server), TestContext.Current.CancellationToken);

        Assert.Equal(WordSenseModelCatalog.VocabularySha256, WordSenseModelCatalog.HashFile(location.VocabularyPath));
    }

    /// <summary>
    ///     A source that is too slow is abandoned and the next one is tried.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_Timeout_FallsThrough()
    {
        using var server = new LocalServer(async context =>
        {
            if (context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal))
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                return;
            }

            await ServeReal(context);
        });
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(server),
            HuggingFaceBaseUri = new Uri(server.BaseUri, "/hf"),
            GitHubLfsBaseUri = new Uri(server.BaseUri, "/lfs"),
            StoreRoot = _store,
            RequestTimeout = TimeSpan.FromSeconds(12),
        };

        var location = await WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(location.ModelPath));
        Assert.Contains("/lfs/main/model/model_int8.onnx", Paths(server));
    }

    /// <summary>
    ///     Cancelling the caller's token cancels the operation instead of falling through.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_CallerCancels_ThrowsOperationCanceled()
    {
        using var server = new LocalServer(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await ServeReal(context);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), cts.Token));
    }

    /// <summary>
    ///     Progress is reported with the source and file names.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_Progress_IsReported()
    {
        using var server = new LocalServer(ServeReal);
        var reports = new List<WordSenseModelDownloadProgress>();
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(server),
            StoreRoot = _store,
            Progress = new SynchronousProgress(reports),
        };

        await WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken);

        Assert.Contains(reports, r => r.FileName == "vocab.txt" && r.Source == "mirror" && r.BytesTransferred > 0);
    }

    /// <summary>
    ///     A verified directory is accepted; missing or altered files are rejected.
    /// </summary>
    [Fact]
    public void WordSenseModelDownloader_FromDirectory_ChecksEveryFile()
    {
        var good = WordSenseModelLocation.FromDirectory(ModelAssets.RequireModelDirectory());
        Assert.EndsWith("vocab.txt", good.VocabularyPath, StringComparison.Ordinal);

        Directory.CreateDirectory(_store);
        Assert.Throws<FileNotFoundException>(() => WordSenseModelLocation.FromDirectory(_store));
        File.WriteAllText(Path.Combine(_store, "model_int8.onnx"), "x");
        File.WriteAllText(Path.Combine(_store, "vocab.txt"), "x");
        Assert.Throws<InvalidDataException>(() => WordSenseModelLocation.FromDirectory(_store));
        Assert.Throws<ArgumentNullException>(() => WordSenseModelLocation.FromDirectory(null!));
    }

    /// <summary>
    ///     The default store lives beneath the user's local application data.
    /// </summary>
    [Fact]
    public void WordSenseModelDownloader_DefaultStoreRoot_IsPerUser()
    {
        Assert.EndsWith(Path.Combine("DemaConsulting", "WordSense", "models"), WordSenseModelLocation.DefaultStoreRoot(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A mirror that redirects to another origin never causes credentials to reach that origin.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WordSenseModelDownloader_EnsureModel_MirrorRedirectsToOtherOrigin_SendsNoCredentialsThere(bool callerClient)
    {
        // Arrange: a second server that serves the files, and a mirror that redirects every request to it
        using var other = new LocalServer(ServeReal);
        using var mirrorServer = new LocalServer(context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers["Location"] = new Uri(other.BaseUri, "/elsewhere/" + FileOf(context)).AbsoluteUri;
            return Task.CompletedTask;
        });
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(mirrorServer, "tok-123"),
            HuggingFaceBaseUri = null,
            GitHubLfsBaseUri = null,
            StoreRoot = _store,
            HttpClient = callerClient ? client : null,
        };

        // Act
        await WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken);

        // Assert: the mirror saw the token, the redirect target saw none but served the files
        Assert.All(mirrorServer.Requests, r => Assert.NotNull(r.Authorization));
        Assert.Equal(2, other.Requests.Count);
        Assert.All(other.Requests, r => Assert.Null(r.Authorization));
    }

    /// <summary>
    ///     A redirect within the mirror's own origin keeps the credentials.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_MirrorRedirectsWithinOrigin_KeepsCredentials()
    {
        // Arrange: /mirror/... redirects to /moved/... on the same server
        using var server = new LocalServer(context =>
        {
            if (context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 307;
                context.Response.Headers["Location"] = "/moved/" + FileOf(context);
                return Task.CompletedTask;
            }

            return ServeReal(context);
        });

        // Act
        await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server, "tok-123")), TestContext.Current.CancellationToken);

        // Assert: both hops carried the token
        Assert.Equal(4, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.NotNull(r.Authorization));
    }

    /// <summary>
    ///     A redirect loop is abandoned and the next source is used.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_RedirectLoop_FallsThrough()
    {
        // Arrange: the mirror redirects to itself forever
        using var server = new LocalServer(context =>
        {
            if (context.Request.Url!.AbsolutePath.StartsWith("/mirror", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 302;
                context.Response.Headers["Location"] = context.Request.Url.AbsolutePath;
                return Task.CompletedTask;
            }

            return ServeReal(context);
        });

        // Act
        var location = await WordSenseModelDownloader.EnsureModelAsync(Options(server, MirrorOn(server)), TestContext.Current.CancellationToken);

        // Assert: the files still arrive, from a later source
        Assert.True(File.Exists(location.ModelPath));
        Assert.Contains("/lfs/main/model/model_int8.onnx", Paths(server));
    }

    /// <summary>
    ///     A zero, negative or oversized request timeout is rejected before any request is made.
    /// </summary>
    /// <param name="milliseconds">The timeout in milliseconds.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    [InlineData(-2)]
    [InlineData(long.MaxValue / 20000)]
    public async Task WordSenseModelDownloader_EnsureModel_InvalidRequestTimeout_ThrowsBeforeRequests(long milliseconds)
    {
        // Arrange
        using var server = new LocalServer(ServeReal);
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(server),
            StoreRoot = _store,
            RequestTimeout = TimeSpan.FromMilliseconds(milliseconds),
        };

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken));
        Assert.Empty(server.Requests);
    }

    /// <summary>
    ///     An infinite request timeout is accepted.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_InfiniteRequestTimeout_Succeeds()
    {
        // Arrange
        using var server = new LocalServer(ServeReal);
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(server),
            StoreRoot = _store,
            RequestTimeout = Timeout.InfiniteTimeSpan,
        };

        // Act
        var location = await WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(File.Exists(location.ModelPath));
    }

    /// <summary>
    ///     An exception from the progress callback propagates and no further source is tried.
    /// </summary>
    [Fact]
    public async Task WordSenseModelDownloader_EnsureModel_ProgressThrows_PropagatesWithoutFallThrough()
    {
        // Arrange
        using var server = new LocalServer(ServeReal);
        var options = new WordSenseModelDownloaderOptions
        {
            Mirror = MirrorOn(server),
            StoreRoot = _store,
            Progress = new ThrowingProgress(),
        };

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WordSenseModelDownloader.EnsureModelAsync(options, TestContext.Current.CancellationToken));
        Assert.All(Paths(server), p => Assert.StartsWith("/mirror/", p, StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(_store, WordSenseModelCatalog.DirectoryName), "*.part"));
    }

    private sealed class ThrowingProgress : IProgress<WordSenseModelDownloadProgress>
    {
        public void Report(WordSenseModelDownloadProgress value) => throw new InvalidOperationException("progress receiver failed");
    }

    private sealed class SynchronousProgress(List<WordSenseModelDownloadProgress> reports) : IProgress<WordSenseModelDownloadProgress>
    {
        public void Report(WordSenseModelDownloadProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }
}
