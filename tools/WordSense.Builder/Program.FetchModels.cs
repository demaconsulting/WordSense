using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The <c>fetch-models</c> command of the builder.
/// </summary>
internal static partial class Program
{
    /// <summary>
    ///     Runs <c>fetch-models</c>: puts the verified files of every encoder model into <c>model/{base-model}/v{version}/</c>.
    /// </summary>
    /// <param name="options">The options of the command: <c>--output</c> is the folder that holds the model folders (default: <c>model</c> of the repository).</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="output">The writer of the report.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The exit code, 0 when every file is present and matches its pinned SHA-256.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when a file could not be obtained and verified from any source.</exception>
    private static async Task<int> RunFetchModelsAsync(Dictionary<string, string> options, string repository, TextWriter output, CancellationToken cancellationToken)
    {
        var root = options.TryGetValue("output", out var target) ? Path.GetFullPath(target) : Path.Join(repository, "model");
        var specs = new List<ModelSpec>();
        foreach (var info in ModelCatalog.All.Where(m => m.IsEncoder))
        {
            using var model = ModelCatalog.Open(info, repository);
            specs.Add(ModelCatalog.EncoderOf(model).Spec);
        }

        return await FetchModelsAsync(specs, root, output, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Makes sure the files of the models are in the store folder, downloading what is missing from the mirror (when
    ///     <c>WORDSENSE_MIRROR_URL</c> is set) and then from the release assets.
    /// </summary>
    /// <param name="specs">The models.</param>
    /// <param name="root">The folder that holds the model folders.</param>
    /// <param name="output">The writer of the report.</param>
    /// <param name="client">The HTTP client, or null to create one; set by tests.</param>
    /// <param name="publicSources">The release location, or null for the default; set by tests.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>0 when every file is present and verified.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when a file could not be obtained and verified from any source.</exception>
    internal static async Task<int> FetchModelsAsync(IReadOnlyList<ModelSpec> specs, string root, TextWriter output, HttpClient? client, PublicSources? publicSources, CancellationToken cancellationToken)
    {
        foreach (var spec in specs)
        {
            var directory = await ModelDownloader.EnsureAsync(spec, null, cancellationToken, client, publicSources, null, root).ConfigureAwait(false);
            ModelDownloader.Verify(spec, directory);
            output.WriteLine($"{spec.ReleaseTag}: {spec.Files.Count} files verified in {directory}");
        }

        return 0;
    }
}
