using System.Net;
using System.Security.Cryptography;
using System.Text;

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
}
