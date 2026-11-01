using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="ModelDownloader"/>, using synthetic model files and only a loopback server or a stub transport.
/// </summary>
public sealed class ModelDownloaderTests : IDisposable
{
    private const string ModelId = "test-model";
    private const string OnnxText = "pretend this is an onnx file";
    private const string VocabText = "pretend this is a vocabulary";

    private readonly string _store = Path.Join(Path.GetTempPath(), "wordsense-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _defaultStoreFolders = [];

    private static readonly ModelSpec Spec = new(
        ModelId,
        1,
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

    private static string TextOf(string file) => file == "model.onnx" ? OnnxText : VocabText;

    private static Task Serve(HttpListenerContext context) => LocalServer.SendAsync(context, 200, TextOf(Path.GetFileName(context.Request.Url!.AbsolutePath)));

    private static bool IsMirror(HttpListenerContext context) => context.Request.Url!.AbsolutePath.StartsWith("/mirror/", StringComparison.Ordinal);

    private DownloadOptions Mirror(LocalServer server, string? bearer = null) =>
        new(new Uri(server.BaseUri, "/mirror/"), BearerToken: bearer, StoreRoot: _store, Timeout: TimeSpan.FromSeconds(30));

    /// <summary>
    ///     The release location on the loopback server: no test may reach the real GitHub.
    /// </summary>
    /// <param name="server">The loopback server.</param>
    /// <returns>The public sources of the test.</returns>
    private static PublicSources Public(LocalServer server) => new(new Uri(server.BaseUri, "/release/"));

    private static string[] Paths(LocalServer server) => [.. server.Requests.Select(r => r.Path)];

    /// <summary>
    ///     Runs the downloader with the test settings: no retry wait and no environment.
    /// </summary>
    private static Task<string> Ensure(
        ModelSpec spec,
        DownloadOptions? options,
        PublicSources sources,
        CancellationToken cancellationToken,
        HttpClient? client = null,
        Func<string, string?>? environment = null,
        string? storeRoot = null) =>
        ModelDownloader.EnsureAsync(spec, options, cancellationToken, client, sources, environment ?? (_ => null), storeRoot, TimeSpan.Zero);

    private static Task<string> Ensure(ModelSpec spec, DownloadOptions? options, LocalServer server, Func<string, string?>? environment = null) =>
        Ensure(spec, options, Public(server), TestContext.Current.CancellationToken, null, environment);

    /// <summary>
    ///     A mirror that serves valid files is the only source contacted and the files land in the model folder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_MirrorServes_UsesOnlyMirror()
    {
        using var server = new LocalServer(Serve);

        var directory = await Ensure(Spec, Mirror(server), server);

        Assert.Equal(Path.Join(_store, ModelId, "v1"), directory);
        Assert.Equal(["/mirror/test-model/v1/model.onnx", "/mirror/test-model/v1/vocab.txt"], Paths(server));
        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     The model folder includes the version, in the store and on the mirror.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_StoreFolder_IncludesVersion()
    {
        using var server = new LocalServer(Serve);
        var second = Spec with { Version = 2 };

        var directory = await Ensure(second, Mirror(server), server);

        Assert.Equal(Path.Join(_store, ModelId, "v2"), directory);
        Assert.Equal(["/mirror/test-model/v2/model.onnx", "/mirror/test-model/v2/vocab.txt"], Paths(server));
        Assert.Equal("test-model/v2", second.StoreFolder);
    }

    /// <summary>
    ///     When the mirror fails, the release asset is used for the same file.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_MirrorFails_FallsBackToRelease()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 404, "no") : Serve(c));

        var directory = await Ensure(Spec, Mirror(server), server);

        Assert.Contains("/release/models/test-model/v1/model.onnx", Paths(server));
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

        var directory = await Ensure(Spec, Mirror(server), server);

        ModelDownloader.Verify(Spec, directory);
        Assert.Empty(Directory.GetFiles(directory, "*.part"));
    }

    /// <summary>
    ///     When the release serves wrong bytes too, the failure reports a mismatch for every source, with its path.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_EverySourceHashMismatch_ReportsEachSource()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 200, "not the model"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(Spec, Mirror(server), server));

        Assert.Equal(2, ex.Message.Split("SHA-256 mismatch").Length - 1);
        Assert.Contains("mirror (", ex.Message, StringComparison.Ordinal);
        Assert.Contains("github-release (", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Join(_store, ModelId, "v1")));
    }

    /// <summary>
    ///     When every source fails, the error lists each source with its request path and never a query or a token.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_AllSourcesFail_Throws()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 500, "broken"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(Spec, Mirror(server, "tok-secret"), server));

        Assert.Contains($"mirror ({new Uri(server.BaseUri, "/mirror/test-model/v1/model.onnx")})", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"github-release ({new Uri(server.BaseUri, "/release/models/test-model/v1/model.onnx")})", ex.Message, StringComparison.Ordinal);
        Assert.Contains("test-model", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("?", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A release without the asset fails after the mirror and the release were tried, and a missing file is not retried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_Release404_FailsListingMirrorAndRelease()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 404, "no"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(Spec, Mirror(server), server));

        Assert.Contains("mirror (", ex.Message, StringComparison.Ordinal);
        Assert.Contains("github-release (", ex.Message, StringComparison.Ordinal);
        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["/mirror/test-model/v1/model.onnx", "/release/models/test-model/v1/model.onnx"], Paths(server));
    }

    /// <summary>
    ///     Without a mirror the failure lists only the release.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_NoMirrorRelease404_ListsOnlyTheRelease()
    {
        using var server = new LocalServer(c => LocalServer.SendAsync(c, 404, "no"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(UniqueSpec(), null, server));

        Assert.Contains("github-release (", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mirror (", ex.Message, StringComparison.Ordinal);
        Assert.Single(Paths(server));
    }

    /// <summary>
    ///     A transient failure is retried once: a 503 followed by a success needs two requests.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_Transient503_IsRetriedOnce()
    {
        var first = true;
        using var server = new LocalServer(c =>
        {
            if (first)
            {
                first = false;
                return LocalServer.SendAsync(c, 503, "busy");
            }

            return Serve(c);
        });

        var directory = await Ensure(Spec, Mirror(server), server);

        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(["/mirror/test-model/v1/model.onnx", "/mirror/test-model/v1/model.onnx", "/mirror/test-model/v1/vocab.txt"], Paths(server));
    }

    /// <summary>
    ///     A source that keeps failing is retried once, not more: two requests per source, then the next source.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_Persistent503_IsRetriedOnlyOnce()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 503, "busy") : Serve(c));

        var directory = await Ensure(Spec, Mirror(server), server);

        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(2, Paths(server).Count(p => p == "/mirror/test-model/v1/model.onnx"));
        Assert.Equal(1, Paths(server).Count(p => p == "/release/models/test-model/v1/model.onnx"));
    }

    /// <summary>
    ///     A missing file (404) is not retried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_NotFound_IsNotRetried()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 404, "no") : Serve(c));

        await Ensure(Spec, Mirror(server), server);

        Assert.Equal(1, Paths(server).Count(p => p == "/mirror/test-model/v1/model.onnx"));
    }

    /// <summary>
    ///     A throttled source (429) is retried once after the wait that it asks for, never longer than the cap.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_RetryAfter_IsHonoredUpToTheCap()
    {
        var first = true;
        using var server = new LocalServer(c =>
        {
            if (first)
            {
                first = false;
                c.Response.Headers["Retry-After"] = "0";
                return LocalServer.SendAsync(c, 429, "slow down");
            }

            return Serve(c);
        });

        var directory = await ModelDownloader.EnsureAsync(Spec, Mirror(server), TestContext.Current.CancellationToken, null, Public(server), _ => null, null, TimeSpan.FromMinutes(5));

        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(2, Paths(server).Count(p => p == "/mirror/test-model/v1/model.onnx"));
        Assert.Equal(TimeSpan.FromSeconds(3), ModelDownloader.RetryWait(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));
        Assert.Equal(ModelDownloader.MaxRetryDelay, ModelDownloader.RetryWait(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(2), ModelDownloader.RetryWait(null, TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(10), ModelDownloader.MaxRetryDelay);
    }

    /// <summary>
    ///     Valid files in the store are reused without any request, and a corrupt file is downloaded again.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_PresentAndCorrupt_ReusesValidRedownloadsCorrupt()
    {
        using var server = new LocalServer(Serve);
        var directory = await Ensure(Spec, Mirror(server), server);
        var before = Paths(server).Length;

        await Ensure(Spec, Mirror(server), server);
        Assert.Equal(before, Paths(server).Length);

        await File.WriteAllTextAsync(Path.Join(directory, "vocab.txt"), "corrupt", TestContext.Current.CancellationToken);
        await Ensure(Spec, Mirror(server), server);

        Assert.Equal(before + 1, Paths(server).Length);
        ModelDownloader.Verify(Spec, directory);
    }

    /// <summary>
    ///     A store folder of another version is not used for this version, and a folder of an old layout is ignored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_OldStoreLayout_IsIgnored()
    {
        using var server = new LocalServer(Serve);
        var old = Path.Join(_store, ModelId);
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Join(old, "model.onnx"), OnnxText, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Join(old, "vocab.txt"), VocabText, TestContext.Current.CancellationToken);

        var directory = await Ensure(Spec, Mirror(server), server);

        Assert.Equal(Path.Join(old, "v1"), directory);
        Assert.Equal(2, Paths(server).Length);
    }

    /// <summary>
    ///     The bearer token goes only to the mirror.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureAsync_BearerToken_SentOnlyToMirror()
    {
        using var server = new LocalServer(c => IsMirror(c) ? LocalServer.SendAsync(c, 404, "no") : Serve(c));

        await Ensure(Spec, Mirror(server, "tok-123"), server);

        Assert.All(server.Requests.Where(r => r.Path.StartsWith("/mirror/", StringComparison.Ordinal)),         r => Assert.Equal("Bearer tok-123", r.Authorization));
        Assert.All(server.Requests.Where(r => r.Path.StartsWith("/release/", StringComparison.Ordinal)), r => Assert.Null(r.Authorization));
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

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Ensure(Spec, options, server));

        Assert.Empty(server.Requests);
    }

    /// <summary>
    ///     An empty, whitespace-only or unparsable store root is rejected as an <see cref="ArgumentException"/> named
    ///     <c>StoreRoot</c> before any request is made, while a null store root selects the default store.
    /// </summary>
    /// <param name="storeRoot">The invalid store root.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("store\0root")]
    public async Task ModelDownloader_EnsureAsync_InvalidStoreRoot_ThrowsBeforeRequests(string storeRoot)
    {
        // Arrange
        using var server = new LocalServer(Serve);
        var options = Mirror(server) with { StoreRoot = storeRoot };

        // Act
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => Ensure(Spec, options, server));

        // Assert
        Assert.Equal("StoreRoot", failure.ParamName);
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

        File.WriteAllText(Path.Join(_store, "model.onnx"), "wrong");
        Assert.Throws<InvalidDataException>(() => ModelDownloader.Verify(Spec, _store));
    }

    /// <summary>
    ///     The default store is per user.
    /// </summary>
    [Fact]
    public void ModelDownloader_DefaultStoreRoot_IsPerUser()
    {
        Assert.EndsWith(Path.Join("DemaConsulting", "WordSense", "models"), ModelDownloader.DefaultStoreRoot(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The identity of a model comes from its base model, version and ONNX file name.
    /// </summary>
    [Fact]
    public void ModelDownloader_ModelSpec_DerivesQuantizationTagAndFolder()
    {
        var spec = Spec with { OnnxFileName = "model-int8-dynamic.onnx" };

        Assert.Equal("test-model", spec.ModelId);
        Assert.Equal("int8-dynamic", spec.Quantization);
        Assert.Equal("models/test-model/v1", spec.ReleaseTag);
        Assert.Equal("test-model/v1", spec.StoreFolder);
        Assert.Equal("test-model/v1/int8-dynamic", spec.WeightsId);
        Assert.Equal("old-blob-id", (spec with { BlobModelId = "old-blob-id" }).WeightsId);
    }

    /// <summary>
    ///     The sources of a file are the mirror first, then the release asset, laid out as the documentation says.
    /// </summary>
    [Fact]
    public void ModelDownloader_BuildSources_MirrorThenRelease_UsesDocumentedLayout()
    {
        var mirror = DownloadMirror.FromOptions(new DownloadOptions(new Uri("http://mirror.example/base/")));

        var sources = ModelDownloader.BuildSources(Spec, mirror, PublicSources.Default, Spec.Files[0]);

        Assert.Equal(2, sources.Count);
        Assert.Equal("mirror", sources[0].Name);
        Assert.Equal("http://mirror.example/base/test-model/v1/model.onnx", sources[0].Uri.ToString());
        Assert.Same(mirror, sources[0].Auth);
        Assert.Equal("github-release", sources[1].Name);
        Assert.Equal("https://github.com/demaconsulting/WordSense/releases/download/models/test-model/v1/model.onnx", sources[1].Uri.ToString());
        Assert.Null(sources[1].Auth);
    }

    /// <summary>
    ///     Without a mirror there is one source, the release asset.
    /// </summary>
    [Fact]
    public void ModelDownloader_BuildSources_NoMirror_OnlyRelease()
    {
        var sources = ModelDownloader.BuildSources(Spec, null, PublicSources.Default, Spec.Files[1]);

        var only = Assert.Single(sources);
        Assert.Equal("github-release", only.Name);
    }

    /// <summary>
    ///     The slashes of the tag stay literal and every segment is escaped.
    /// </summary>
    [Fact]
    public void ModelDownloader_BuildSources_Release_KeepsSlashesAndEscapesSegments()
    {
        var spec = Spec with { BaseModel = "a b", Files = [new ModelFile("m#1.onnx", Sha(OnnxText))] };

        var sources = ModelDownloader.BuildSources(spec, null, new PublicSources(new Uri("https://host.test/dl/")), spec.Files[0]);

        Assert.Equal("https://host.test/dl/models/a%20b/v1/m%231.onnx", sources[0].Uri.AbsoluteUri);
    }

    private static string FileOf(HttpListenerContext context) => Path.GetFileName(context.Request.Url!.AbsolutePath);

    private static Func<string, string?> Environment(params (string Name, string Value)[] values) =>
        name => values.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    /// <summary>
    ///     Creates a specification with a unique base model, so that a run without a store root option uses its own folder
    ///     in the per-user store, which is removed again on disposal.
    /// </summary>
    /// <returns>The specification.</returns>
    private ModelSpec UniqueSpec()
    {
        var spec = Spec with { BaseModel = "test-model-" + Guid.NewGuid().ToString("N") };
        _defaultStoreFolders.Add(Path.Join(ModelDownloader.DefaultStoreRoot(), spec.BaseModel));
        return spec;
    }

    /// <summary>
    ///     Without any mirror the release asset is used.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_NoMirror_UsesRelease()
    {
        using var server = new LocalServer(Serve);
        var spec = UniqueSpec();

        await Ensure(spec, null, server);

        Assert.Equal([$"/release/models/{spec.BaseModel}/v1/model.onnx", $"/release/models/{spec.BaseModel}/v1/vocab.txt"], Paths(server));
    }

    /// <summary>
    ///     An explicit store root is used when no options are passed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_StoreRootWithoutOptions_IsUsed()
    {
        using var server = new LocalServer(Serve);

        var directory = await Ensure(Spec, null, Public(server), TestContext.Current.CancellationToken, null, null, _store);

        Assert.Equal(Path.Join(_store, ModelId, "v1"), directory);
        ModelDownloader.Verify(Spec, directory);
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

        await Ensure(spec, null, server, Environment((DownloadMirror.UrlEnvironmentVariable, url)));

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

        await Ensure(Spec, Mirror(server), server, Environment((DownloadMirror.UrlEnvironmentVariable, url)));

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

        await Assert.ThrowsAsync<ArgumentException>(() => Ensure(UniqueSpec(), null, server, Environment((DownloadMirror.UserEnvironmentVariable, "user"))));

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
        await Ensure(spec, null, server);

        await Assert.ThrowsAsync<ArgumentException>(() => Ensure(spec, null, server, Environment((DownloadMirror.UserEnvironmentVariable, "user"))));
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

        await Ensure(Spec, options, server);

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

        await Ensure(Spec, Mirror(mirrorServer, "tok-123"), Public(mirrorServer), TestContext.Current.CancellationToken, callerClient ? client : null);

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

        await Ensure(Spec, Mirror(server, "tok-123"), server);

        Assert.Equal(4, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.NotNull(r.Authorization));
    }

    /// <summary>
    ///     The release redirects to a CDN host with a signed query, as GitHub does: no mirror credential reaches the
    ///     release host or the CDN host, and the mirror hops still carry it.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_ReleaseRedirectsToCdn_SendsNoAuthorization()
    {
        // Arrange: the mirror is down; github.com redirects to release-assets.githubusercontent.com with a signed query
        var seen = new List<(Uri Uri, string? Authorization)>();
        using var handler = new StubHandler(request =>
        {
            // Record only the scheme and a hash of the parameter, never the credential itself
            var auth = request.Headers.Authorization;
            seen.Add((request.RequestUri!, auth is null ? null : auth.Scheme));
            var uri = request.RequestUri!;
            var file = Path.GetFileName(uri.AbsolutePath);
            if (uri.Host == "mirror.test")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (uri.Host == "github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/" + file + "?sp=r&sig=SECRETSIG&jwt=SECRETJWT");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TextOf(file)) };
        });
        using var client = new HttpClient(handler);
        var options = new DownloadOptions(new Uri("https://mirror.test/models/"), BearerToken: "tok-123", StoreRoot: _store, Timeout: TimeSpan.FromSeconds(30));

        // Act
        var directory = await Ensure(Spec, options, PublicSources.Default, TestContext.Current.CancellationToken, client);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.All(seen.Where(r => r.Uri.Host == "mirror.test"), r => Assert.Equal("Bearer", r.Authorization));
        Assert.All(seen.Where(r => r.Uri.Host != "mirror.test"), r => Assert.Null(r.Authorization));
        Assert.Contains(seen, r => r.Uri.Host == "github.com" && r.Uri.AbsolutePath == "/demaconsulting/WordSense/releases/download/models/test-model/v1/model.onnx");
        Assert.Contains(seen, r => r.Uri.Host == "release-assets.githubusercontent.com");
    }

    /// <summary>
    ///     When the CDN refuses a signed redirect, the failure names the release path and never the signed query.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_ReleaseFails_ErrorOmitsRedirectQuery()
    {
        // Arrange
        using var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/x?sp=r&sig=SECRETSIG&jwt=SECRETJWT");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        using var client = new HttpClient(handler);

        // Act
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(UniqueSpec(), null, PublicSources.Default, TestContext.Current.CancellationToken, client));

        // Assert
        Assert.Contains("github-release (https://github.com/demaconsulting/WordSense/releases/download/models/", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETSIG", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETJWT", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("?", ex.Message, StringComparison.Ordinal);
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

        var directory = await Ensure(Spec, Mirror(server), server);

        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/release/models/test-model/v1/model.onnx", Paths(server));
    }

    /// <summary>
    ///     A source that is too slow is abandoned and the next one is tried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_Timeout_FallsThrough()
    {
        using var server = new LocalServer(async (context, stopping) =>
        {
            if (IsMirror(context))
            {
                // A hung mirror: it never answers, until the server is disposed at the end of the test
                await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
                return;
            }

            await Serve(context);
        });

        // The timeout only breaks the hang; it is far above any healthy local response, so it never limits the fallback
        var options = Mirror(server) with { Timeout = TimeSpan.FromSeconds(5) };

        var directory = await Ensure(Spec, options, server);

        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/release/models/test-model/v1/model.onnx", Paths(server));
    }

    /// <summary>
    ///     Cancelling the caller's token cancels the operation instead of falling through.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task ModelDownloader_EnsureModel_CallerCancels_ThrowsOperationCanceled()
    {
        using var server = new LocalServer(async (context, stopping) =>
        {
            // A hung server: it never answers, until the server is disposed at the end of the test
            await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
            await Serve(context);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ensure(Spec, Mirror(server), Public(server), cts.Token));
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

        var directory = await Ensure(Spec, options, server);

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
        var folder = Path.Join(_store, ModelId, "v1");
        Directory.CreateDirectory(folder);
        var vocabulary = Path.Join(folder, "vocab.txt");
        await File.WriteAllTextAsync(vocabulary, "corrupt", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(Spec, Mirror(server), server));

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

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TextOf(file)) };
        });
        using var client = new HttpClient(handler);
        var options = new DownloadOptions(new Uri("https://mirror.test:8443/mirror/"), BearerToken: "tok-123", StoreRoot: _store, Timeout: TimeSpan.FromSeconds(30));

        // Act
        var directory = await Ensure(Spec, options, new PublicSources(new Uri("https://release.test/release/")), TestContext.Current.CancellationToken, client);

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
        var directory = await Ensure(Spec, Mirror(server), server);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Equal(
            [
                "/mirror/test-model/v1/model.onnx",
                "/release/models/test-model/v1/model.onnx",
                "/mirror/test-model/v1/vocab.txt",
                "/release/models/test-model/v1/vocab.txt",
            ],
            Paths(server));
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
        var directory = await Ensure(Spec, Mirror(server), server);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/release/models/test-model/v1/model.onnx", Paths(server));
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
        using var server = new LocalServer(async (context, stopping) =>
        {
            if (IsMirror(context))
            {
                context.Response.ContentLength64 = 500;
                await context.Response.OutputStream.WriteAsync(new byte[10], stopping);
                await context.Response.OutputStream.FlushAsync(stopping);

                // A hung body: no more bytes arrive, until the server is disposed at the end of the test
                await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
                return;
            }

            await Serve(context);
        });

        // The timeout only breaks the hang; it is far above any healthy local response, so it never limits the fallback
        var options = Mirror(server) with { Timeout = TimeSpan.FromSeconds(5) };

        // Act
        var directory = await Ensure(Spec, options, server);

        // Assert
        ModelDownloader.Verify(Spec, directory);
        Assert.Contains("/release/models/test-model/v1/model.onnx", Paths(server));
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
            var body = Encoding.UTF8.GetBytes(TextOf(Path.GetFileName(context.Request.Url!.AbsolutePath)) + "!");
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
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Ensure(pinned, Mirror(server), server));

        // Assert: both sources were tried and refused for their size
        Assert.Equal(2, ex.Message.Split("larger than the pinned size").Length - 1);
        Assert.False(File.Exists(Path.Join(_store, ModelId, "v1", "model.onnx")));
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
        var folder = Path.Join(_store, ModelId, "v1");
        Directory.CreateDirectory(folder);
        var stale = Path.Join(folder, "model.onnx.0123456789abcdef0123456789abcdef.part");
        await File.WriteAllTextAsync(stale, "pretend", TestContext.Current.CancellationToken);
        using var server = new LocalServer(Serve);

        // Act
        var directory = await Ensure(Spec, Mirror(server), server);

        // Assert: the whole file was requested and verified; the stale file is untouched
        ModelDownloader.Verify(Spec, directory);
        Assert.Equal("pretend", await File.ReadAllTextAsync(stale, TestContext.Current.CancellationToken));
        Assert.Equal(OnnxText, await File.ReadAllTextAsync(Path.Join(folder, "model.onnx"), TestContext.Current.CancellationToken));
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

        var directory = await Ensure(Spec, Mirror(server), server);

        Assert.Equal(["model.onnx", "vocab.txt"], Directory.GetFiles(directory).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
    }
}
