using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="ModelDownloader"/>, using synthetic model files and only a loopback server.
/// </summary>
public sealed class ModelDownloaderTests : IDisposable
{
    private const string ModelId = "test-model";
    private const string OnnxText = "pretend this is an onnx file";
    private const string VocabText = "pretend this is a vocabulary";

    private readonly string _store = Path.Combine(Path.GetTempPath(), "wordsense-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _defaultStoreFolders = [];

    private static readonly ModelSpec Spec = new(
        ModelId,
        [new ModelFile("model.onnx", Sha(OnnxText)), new ModelFile("vocab.txt", Sha(VocabText))],
        "model.onnx",
        "vocab.txt",
        true,
        typeof(ModelDownloaderTests).Assembly,
        "none",
        2);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_store))
        {
            Directory.Delete(_store, true);
        }

        foreach (var folder in _defaultStoreFolders.Where(Directory.Exists))
        {
            Directory.Delete(folder, true);
        }
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static Task Serve(HttpListenerContext context) => LocalServer.SendAsync(
        context,
        200,
        Path.GetFileName(context.Request.Url!.AbsolutePath) == "model.onnx" ? OnnxText : VocabText);

    private static bool IsMirror(HttpListenerContext context) => context.Request.Url!.AbsolutePath.StartsWith("/mirror/", StringComparison.Ordinal);

    private MirrorOptions Mirror(LocalServer server, string? bearer = null) =>
        new(new Uri(server.BaseUri, "/mirror/"), BearerToken: bearer, StoreRoot: _store, Timeout: TimeSpan.FromSeconds(30));

    private static Uri Lfs(LocalServer server) => new(server.BaseUri, "/lfs/");

    private static string[] Paths(LocalServer server) => [.. server.Requests.Select(r => r.Path)];

    /// <summary>
    ///     A mirror that serves valid files is the only source contacted and the files land in the model folder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_MirrorServes_UsesOnlyMirror()
    {
        using var server = new LocalServer(Serve);

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Equal(Path.Combine(_store, ModelId), directory);
        Assert.Equal(["/mirror/test-model/model.onnx", "/mirror/test-model/vocab.txt"], Paths(server));
        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     When the mirror fails, the GitHub copy is used for the same file.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_MirrorFails_FallsBackToGitHub()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 503, "down") : Serve(c));

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Contains("/lfs/main/model/test-model/model.onnx", Paths(server));
        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     A source that serves wrong bytes is rejected and nothing bad stays in the store.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_HashMismatch_FallsThroughAndKeepsNothingBad()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 200, "not the model") : Serve(c));

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        ModelDownloader.Verify(Spec, directory);
        Assert.Empty(Directory.GetFiles(directory, "*.part"));
    }

    /// <summary>
    ///     When every source fails, the error lists the attempts.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_AllSourcesFail_Throws()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 500, "broken"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null));

        Assert.Contains("mirror", ex.Message, StringComparison.Ordinal);
        Assert.Contains("github-lfs", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Valid files in the store are reused without any request, and a corrupt file is downloaded again.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_PresentAndCorrupt_ReusesValidRedownloadsCorrupt()
    {
        using var server = new LocalServer(Serve);
        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);
        var before = Paths(server).Length;

        await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);
        Assert.Equal(before, Paths(server).Length);

        await File.WriteAllTextAsync(Path.Combine(directory, "vocab.txt"), "corrupt", TestContext.Current.CancellationToken);
        await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Equal(before + 1, Paths(server).Length);
        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     The bearer token goes only to the mirror.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_BearerToken_SentOnlyToMirror()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 404, "no") : Serve(c));

        await ModelDownloader.EnsureAsync(Spec, Mirror(server, "tok-123"), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.All(server.Requests.Where(r => r.Path.StartsWith("/mirror/", StringComparison.Ordinal)), r => Assert.Equal("Bearer tok-123", r.Authorization));
        Assert.All(server.Requests.Where(r => r.Path.StartsWith("/lfs/", StringComparison.Ordinal)), r => Assert.Null(r.Authorization));
    }

    /// <summary>
    ///     An invalid timeout is rejected before any request is made.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_InvalidTimeout_ThrowsBeforeRequests()
    {
        using var server = new LocalServer(Serve);
        var options = Mirror(server) with { Timeout = TimeSpan.FromSeconds(-5) };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, null, Lfs(server), _ => null));

        Assert.Empty(server.Requests);
    }

    /// <summary>
    ///     Verification reports a missing file and a wrong digest.
    /// </summary>
    [Fact]
    public void ModelDownloader_Verify_MissingOrWrongFile_Throws()
    {
        Directory.CreateDirectory(_store);
        Assert.Throws<FileNotFoundException>(() => ModelDownloader.Verify(Spec, _store));

        File.WriteAllText(Path.Combine(_store, "model.onnx"), "wrong");
        Assert.Throws<InvalidDataException>(() => ModelDownloader.Verify(Spec, _store));
    }

    /// <summary>
    ///     The default store is per user.
    /// </summary>
    [Fact]
    public void ModelDownloader_DefaultStoreRoot_IsPerUser()
    {
        Assert.EndsWith(Path.Combine("DemaConsulting", "WordSense", "models"), ModelDownloader.DefaultStoreRoot(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The sources of a file are the mirror first, then GitHub, laid out as the documentation says.
    /// </summary>
    [Fact]
    public void ModelDownloader_BuildSources_MirrorThenGitHub_UsesDocumentedLayout()
    {
        var mirror = DownloadMirror.FromOptions(new MirrorOptions(new Uri("http://mirror.example/base/")));

        var sources = ModelDownloader.BuildSources(Spec, mirror, ModelDownloader.GitHubBaseUri, Spec.Files[0]);

        Assert.Equal(2, sources.Count);
        Assert.Equal("http://mirror.example/base/test-model/model.onnx", sources[0].Uri.ToString());
        Assert.Equal("https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/test-model/model.onnx", sources[1].Uri.ToString());
    }

    private static string FileOf(HttpListenerContext context) => Path.GetFileName(context.Request.Url!.AbsolutePath);

    private static Func<string, string?> Environment(params (string Name, string Value)[] values) =>
        name => values.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    /// <summary>
    ///     Creates a specification with a unique model identifier, so that a run without a store root option uses its own folder
    ///     in the per-user store, which is removed again on disposal.
    /// </summary>
    /// <returns>The specification.</returns>
    private ModelSpec UniqueSpec()
    {
        var spec = Spec with { ModelId = "test-model-" + Guid.NewGuid().ToString("N") };
        _defaultStoreFolders.Add(Path.Combine(ModelDownloader.DefaultStoreRoot(), spec.ModelId));
        return spec;
    }

    /// <summary>
    ///     Without any mirror the GitHub copy is used.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_NoMirror_UsesGitHubLfs()
    {
        using var server = new LocalServer(Serve);
        var spec = UniqueSpec();

        await ModelDownloader.EnsureAsync(spec, null, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Equal([$"/lfs/main/model/{spec.ModelId}/model.onnx", $"/lfs/main/model/{spec.ModelId}/vocab.txt"], Paths(server));
    }

    /// <summary>
    ///     A mirror configured in the environment is asked first.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_MirrorFromEnvironment_IsUsedFirst()
    {
        using var server = new LocalServer(Serve);
        var url = new Uri(server.BaseUri, "/from-environment/").ToString();
        var spec = UniqueSpec();

        await ModelDownloader.EnsureAsync(spec, null, TestContext.Current.CancellationToken, null, Lfs(server), Environment((DownloadMirror.UrlEnvironmentVariable, url)));

        Assert.All(Paths(server), p => Assert.StartsWith("/from-environment/", p, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A programmatic mirror wins over the environment.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_ProgrammaticMirror_BeatsEnvironment()
    {
        using var server = new LocalServer(Serve);
        var url = new Uri(server.BaseUri, "/from-environment/").ToString();

        await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), Environment((DownloadMirror.UrlEnvironmentVariable, url)));

        Assert.All(Paths(server), p => Assert.StartsWith("/mirror/", p, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A bad environment configuration fails before any request.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_InvalidEnvironment_ThrowsBeforeRequests()
    {
        using var server = new LocalServer(Serve);

        await Assert.ThrowsAsync<ArgumentException>(
            () => ModelDownloader.EnsureAsync(UniqueSpec(), null, TestContext.Current.CancellationToken, null, Lfs(server), Environment((DownloadMirror.UserEnvironmentVariable, "user"))));

        Assert.Empty(server.Requests);
    }

    /// <summary>
    ///     A bad environment configuration is reported even when valid files are already in the store.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_InvalidEnvironmentWithValidCache_Throws()
    {
        using var server = new LocalServer(Serve);
        var spec = UniqueSpec();
        await ModelDownloader.EnsureAsync(spec, null, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => ModelDownloader.EnsureAsync(spec, null, TestContext.Current.CancellationToken, null, Lfs(server), Environment((DownloadMirror.UserEnvironmentVariable, "user"))));
    }

    /// <summary>
    ///     HTTP Basic credentials are sent to the mirror.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_BasicCredentials_SentToMirror()
    {
        using var server = new LocalServer(Serve);
        var options = Mirror(server) with { Credentials = new NetworkCredential("user", "pw") };

        await ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.All(server.Requests, r => Assert.Equal("Basic " + Convert.ToBase64String("user:pw"u8.ToArray()), r.Authorization));
    }

    /// <summary>
    ///     A mirror that redirects to another origin never causes credentials to reach that origin.
    /// </summary>
    /// <param name="callerClient">True to supply the HTTP client, false to let the downloader create one.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelDownloader_EnsureModel_MirrorRedirectsToOtherOrigin_SendsNoCredentialsThere(bool callerClient)
    {
        using var other = new LocalServer(Serve);
        using var mirrorServer = new LocalServer(context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers["Location"] = new Uri(other.BaseUri, "/elsewhere/" + FileOf(context)).AbsoluteUri;
            return Task.CompletedTask;
        });
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        await ModelDownloader.EnsureAsync(Spec, Mirror(mirrorServer, "tok-123"), TestContext.Current.CancellationToken, callerClient ? client : null, Lfs(mirrorServer), _ => null);

        Assert.All(mirrorServer.Requests, r => Assert.NotNull(r.Authorization));
        Assert.Equal(2, other.Requests.Count);
        Assert.All(other.Requests, r => Assert.Null(r.Authorization));
    }

    /// <summary>
    ///     A redirect within the mirror's own origin keeps the credentials.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_MirrorRedirectsWithinOrigin_KeepsCredentials()
    {
        using var server = new LocalServer(context =>
        {
            if (IsMirror(context))
            {
                context.Response.StatusCode = 307;
                context.Response.Headers["Location"] = "/moved/" + FileOf(context);
                return Task.CompletedTask;
            }

            return Serve(context);
        });

        await ModelDownloader.EnsureAsync(Spec, Mirror(server, "tok-123"), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Equal(4, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.NotNull(r.Authorization));
    }

    /// <summary>
    ///     A redirect loop is abandoned and the next source is used.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_RedirectLoop_FallsThrough()
    {
        using var server = new LocalServer(context =>
        {
            if (IsMirror(context))
            {
                context.Response.StatusCode = 302;
                context.Response.Headers["Location"] = context.Request.Url!.AbsolutePath;
                return Task.CompletedTask;
            }

            return Serve(context);
        });

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/lfs/main/model/test-model/model.onnx", Paths(server));
    }

    /// <summary>
    ///     A source that is too slow is abandoned and the next one is tried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_Timeout_FallsThrough()
    {
        using var server = new LocalServer(async context =>
        {
            if (IsMirror(context))
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return;
            }

            await Serve(context);
        });
        var options = Mirror(server) with { Timeout = TimeSpan.FromSeconds(1) };

        var directory = await ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/lfs/main/model/test-model/model.onnx", Paths(server));
    }

    /// <summary>
    ///     Cancelling the caller's token cancels the operation instead of falling through.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_CallerCancels_ThrowsOperationCanceled()
    {
        using var server = new LocalServer(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            await Serve(context);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ModelDownloader.EnsureAsync(Spec, Mirror(server), cts.Token, null, Lfs(server), _ => null));
    }

    /// <summary>
    ///     An infinite request timeout is accepted.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_InfiniteRequestTimeout_Succeeds()
    {
        using var server = new LocalServer(Serve);
        var options = Mirror(server) with { Timeout = Timeout.InfiniteTimeSpan };

        var directory = await ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     When a stored file is corrupt and every source fails, the file is left in place and the error lists the attempts.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_CorruptCacheAndAllSourcesFail_LeavesFileInPlace()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 500, "broken"));
        var folder = Path.Combine(_store, ModelId);
        Directory.CreateDirectory(folder);
        var vocabulary = Path.Combine(folder, "vocab.txt");
        await File.WriteAllTextAsync(vocabulary, "corrupt", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null));

        Assert.Equal("corrupt", await File.ReadAllTextAsync(vocabulary, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A redirect from the mirror's https origin to plain http on the same host and port is a downgrade: it is
    ///     followed, but without the credentials.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_MirrorRedirectsToDowngradedScheme_SendsNoCredentialsThere()
    {
        // Arrange: a fake transport, so no TLS is needed; https answers with a redirect to http on the same host and port
        var seen = new List<(Uri Uri, string? Authorization)>();
        var tokenHash = Convert.ToHexString(SHA256.HashData("tok-123"u8));
        using var handler = new StubHandler(request =>
        {
            // Record only the scheme and a hash of the parameter, never the credential itself
            var auth = request.Headers.Authorization;
            seen.Add((request.RequestUri!, auth is null ? null : auth.Scheme + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth.Parameter ?? string.Empty)))));
            var file = Path.GetFileName(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.Scheme == Uri.UriSchemeHttps && request.RequestUri.AbsolutePath.StartsWith("/mirror/", StringComparison.Ordinal))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("http://mirror.test:8443/moved/" + file);
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(file == "model.onnx" ? OnnxText : VocabText) };
        });
        using var client = new HttpClient(handler);
        var options = new MirrorOptions(new Uri("https://mirror.test:8443/mirror/"), BearerToken: "tok-123", StoreRoot: _store, Timeout: TimeSpan.FromSeconds(30));

        // Act
        var directory = await ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, client, new Uri("https://lfs.test/lfs/"), _ => null);

        // Assert: the https hops carried the token, the http hops did not
        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(4, seen.Count);
        Assert.All(seen.Where(r => r.Uri.Scheme == Uri.UriSchemeHttps), r => Assert.Equal("Bearer:" + tokenHash, r.Authorization));
        Assert.All(seen.Where(r => r.Uri.Scheme == Uri.UriSchemeHttp), r => Assert.Null(r.Authorization));
    }

    /// <summary>
    ///     A redirect to a location that is not http or https is refused, and the next source is used.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_RedirectToNonHttpScheme_IsRefusedAndFallsThrough()
    {
        // Arrange
        using var server = new LocalServer(context =>
        {
            if (IsMirror(context))
            {
                context.Response.StatusCode = 302;
                context.Response.Headers["Location"] = "ftp://example.test/" + FileOf(context);
                return Task.CompletedTask;
            }

            return Serve(context);
        });

        // Act
        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(["/mirror/test-model/model.onnx", "/lfs/main/model/test-model/model.onnx", "/mirror/test-model/vocab.txt", "/lfs/main/model/test-model/vocab.txt"], Paths(server));
    }

    /// <summary>
    ///     A body that ends before its declared length is a failed attempt: the next source is used and no partial
    ///     file is left behind.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_BodyCutShort_FallsThroughAndLeavesNoPartialFile()
    {
        // Arrange: the mirror declares 500 bytes, sends 10 and drops the connection
        using var server = new LocalServer(async context =>
        {
            if (IsMirror(context))
            {
                context.Response.ContentLength64 = 500;
                await context.Response.OutputStream.WriteAsync(new byte[10]);
                await context.Response.OutputStream.FlushAsync();
                context.Response.Abort();
                return;
            }

            await Serve(context);
        });

        // Act
        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/lfs/main/model/test-model/model.onnx", Paths(server));
        Assert.Equal(["model.onnx", "vocab.txt"], Directory.GetFiles(directory).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     A source that starts to answer and then stalls is abandoned when the per-request time limit expires, which
    ///     covers the body as well as the headers; no partial file is left behind.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_BodyStalls_TimesOutFallsThroughAndLeavesNoPartialFile()
    {
        // Arrange: the mirror sends the headers and 10 bytes of 500, then stalls
        using var server = new LocalServer(async context =>
        {
            if (IsMirror(context))
            {
                context.Response.ContentLength64 = 500;
                await context.Response.OutputStream.WriteAsync(new byte[10]);
                await context.Response.OutputStream.FlushAsync();
                await Task.Delay(TimeSpan.FromSeconds(15));
                return;
            }

            await Serve(context);
        });
        var options = Mirror(server) with { Timeout = TimeSpan.FromSeconds(1) };

        // Act
        var directory = await ModelDownloader.EnsureAsync(Spec, options, TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/lfs/main/model/test-model/model.onnx", Paths(server));
        Assert.Equal(["model.onnx", "vocab.txt"], Directory.GetFiles(directory).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     A body that is larger than the pinned size of the file is refused whether or not the server declares its
    ///     length, and the failure says so for every source.
    /// </summary>
    /// <param name="chunked">True to send the body without a declared length.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelDownloader_EnsureModel_BodyLargerThanPinnedSize_IsRefused(bool chunked)
    {
        // Arrange: a spec that pins the sizes and a server that sends one byte too many
        var pinned = Spec with
        {
            Files = [new ModelFile("model.onnx", Sha(OnnxText), OnnxText.Length), new ModelFile("vocab.txt", Sha(VocabText), VocabText.Length)],
        };
        using var server = new LocalServer(async context =>
        {
            var body = Encoding.UTF8.GetBytes((Path.GetFileName(context.Request.Url!.AbsolutePath) == "model.onnx" ? OnnxText : VocabText) + "!");
            if (chunked)
            {
                context.Response.SendChunked = true;
            }
            else
            {
                context.Response.ContentLength64 = body.Length;
            }

            await context.Response.OutputStream.WriteAsync(body);
        });

        // Act
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => ModelDownloader.EnsureAsync(pinned, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null));

        // Assert: both sources were tried and refused for their size
        Assert.Equal(2, Regex.Matches(ex.Message, "larger than the pinned size").Count);
        Assert.False(File.Exists(Path.Combine(_store, ModelId, "model.onnx")));
    }

    /// <summary>
    ///     The downloader does not resume: a partial file left by an earlier run is neither read nor appended to nor
    ///     removed, and the download starts afresh into its own temporary file.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_StalePartialFile_IsNotResumedOrReused()
    {
        // Arrange: a leftover partial file of an earlier run
        var folder = Path.Combine(_store, ModelId);
        Directory.CreateDirectory(folder);
        var stale = Path.Combine(folder, "model.onnx.0123456789abcdef0123456789abcdef.part");
        await File.WriteAllTextAsync(stale, "pretend", TestContext.Current.CancellationToken);
        using var server = new LocalServer(Serve);

        // Act
        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        // Assert: the whole file was requested and verified; the stale file is untouched
        ModelDownloader.Verify(Spec, directory);
        Assert.Equal("pretend", await File.ReadAllTextAsync(stale, TestContext.Current.CancellationToken));
        Assert.Equal(OnnxText, await File.ReadAllTextAsync(Path.Combine(folder, "model.onnx"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A transport that answers each request from a function, so that schemes and hosts need no real network.
    /// </summary>
    /// <param name="respond">Creates the response of a request.</param>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>
    ///     A successful download leaves no temporary file behind.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_Success_LeavesNoTemporaryFiles()
    {
        using var server = new LocalServer(Serve);

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Lfs(server), _ => null);

        Assert.Equal(["model.onnx", "vocab.txt"], Directory.GetFiles(directory).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
    }
}
