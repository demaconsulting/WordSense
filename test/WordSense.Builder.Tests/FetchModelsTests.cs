using System.Net;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the <c>fetch-models</c> command: it fills the model folders from the release assets, with a stub
///     transport and never the network.
/// </summary>
public sealed class FetchModelsTests : IDisposable
{
    private const string OnnxText = "pretend this is an onnx file";
    private const string VocabText = "pretend this is a vocabulary";

    private static readonly PublicSources Release = new(new Uri("https://release.test/download/"));

    private static readonly ModelSpec[] Specs =
    [
        new ModelSpec(
            "fetch-model",
            1,
            [new ModelFile("model-int8-dynamic.onnx", Sha(OnnxText), OnnxText.Length), new ModelFile("vocab.txt", Sha(VocabText), VocabText.Length)],
            "model-int8-dynamic.onnx",
            "vocab.txt",
            true,
            typeof(FetchModelsTests).Assembly,
            "none",
            2),
    ];

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("ws-fetch-");

    /// <inheritdoc />
    public void Dispose() => _root.Delete(true);

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string TextOf(string file) => file == "vocab.txt" ? VocabText : OnnxText;

    /// <summary>
    ///     Answers requests from a function and records the requested URLs.
    /// </summary>
    /// <param name="respond">Creates the response of a request.</param>
    private sealed class StubHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        ///     Gets the URLs that were requested, in order.
        /// </summary>
        public List<Uri> Requests { get; } = [];

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }

    /// <summary>
    ///     Files that are already in the model folder and match their pins cause no request.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_FetchModels_FilesPresent_MakesNoRequest()
    {
        // Arrange
        var folder = Directory.CreateDirectory(Path.Join(_root.FullName, "fetch-model", "v1"));
        await File.WriteAllTextAsync(Path.Join(folder.FullName, "model-int8-dynamic.onnx"), OnnxText, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Join(folder.FullName, "vocab.txt"), VocabText, TestContext.Current.CancellationToken);
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        var output = new StringWriter();

        // Act
        var code = await Program.FetchModelsAsync(Specs, _root.FullName, output, client, Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Empty(handler.Requests);
        Assert.Contains("models/fetch-model/v1: 2 files verified", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing file is downloaded from the release asset and verified, into <c>{base-model}/v{version}</c>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_FetchModels_MissingFile_IsFetchedFromTheRelease()
    {
        // Arrange: the model file is present, the vocabulary is missing
        var folder = Directory.CreateDirectory(Path.Join(_root.FullName, "fetch-model", "v1"));
        await File.WriteAllTextAsync(Path.Join(folder.FullName, "model-int8-dynamic.onnx"), OnnxText, TestContext.Current.CancellationToken);
        using var handler = new StubHandler(uri => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TextOf(Path.GetFileName(uri.AbsolutePath))) });
        using var client = new HttpClient(handler);

        // Act
        var code = await Program.FetchModelsAsync(Specs, _root.FullName, new StringWriter(), client, Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(["https://release.test/download/models/fetch-model/v1/vocab.txt"], handler.Requests.Select(u => u.AbsoluteUri));
        Assert.Equal(VocabText, await File.ReadAllTextAsync(Path.Join(folder.FullName, "vocab.txt"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A file that the release does not have fails the command, and no other source is tried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_FetchModels_ReleaseWithoutTheFile_Fails()
    {
        // Arrange
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => Program.FetchModelsAsync(Specs, _root.FullName, new StringWriter(), client, Release, TestContext.Current.CancellationToken));

        // Assert: only the release was asked, and it is named in the message
        Assert.Equal(["https://release.test/download/models/fetch-model/v1/model-int8-dynamic.onnx"], handler.Requests.Select(u => u.AbsoluteUri));
        Assert.Contains("github-release", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The model folders of the catalog are the store layout of the specs, so the tests and the evaluation commands
    ///     read what <c>fetch-models</c> writes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ModelCatalog_ModelDirectories_AreTheStoreFoldersOfTheSpecs()
    {
        foreach (var info in ModelCatalog.All.Where(m => m.IsEncoder))
        {
            using var model = info.Create(null);
            Assert.Equal(ModelCatalog.EncoderOf(model).Spec.StoreFolder, info.ModelDirectory);
        }
    }
}
