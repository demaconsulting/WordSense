using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Obtains the encoder model files, verifying each against its pinned SHA-256.
/// </summary>
/// <remarks>
///     Files already present and valid in the store are reused without any network access. Otherwise each
///     missing file is requested from the sources described by <see cref="WordSenseModelDownloaderOptions"/>
///     in order, downloaded to a uniquely named temporary file, verified, and atomically moved into place,
///     so a partial or corrupt download never becomes visible under the final name.
/// </remarks>
public static class WordSenseModelDownloader
{
    /// <summary>
    ///     Ensures that the model is present and verified in the store, downloading what is missing.
    /// </summary>
    /// <param name="options">The download options; null uses the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The verified model location.</returns>
    /// <exception cref="WordSenseModelDownloadException">Thrown when a file could not be obtained from any source.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async Task<WordSenseModelLocation> EnsureModelAsync(
        WordSenseModelDownloaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WordSenseModelDownloaderOptions();

        // An explicit mirror beats the environment; an invalid environment configuration fails fast
        var mirror = options.Mirror ?? DownloadMirror.FromEnvironment(options.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable);

        var root = options.StoreRoot ?? WordSenseModelLocation.DefaultStoreRoot();
        var directory = Path.Combine(Path.GetFullPath(root), WordSenseModelCatalog.DirectoryName);
        Directory.CreateDirectory(directory);

        var ownsClient = options.HttpClient is null;
        var client = options.HttpClient ?? new HttpClient();
        try
        {
            foreach (var file in WordSenseModelCatalog.Files)
            {
                var path = Path.Combine(directory, file.Name);
                if (File.Exists(path) && string.Equals(WordSenseModelCatalog.HashFile(path), file.Sha256, StringComparison.Ordinal))
                {
                    continue;
                }

                await DownloadFileAsync(client, options, mirror, file, path, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }

        return WordSenseModelLocation.FromDirectory(directory);
    }

    /// <summary>
    ///     Builds the ordered list of candidate sources for one file.
    /// </summary>
    /// <param name="options">The download options.</param>
    /// <param name="mirror">The effective mirror, or null.</param>
    /// <param name="file">The file being requested.</param>
    /// <returns>Source names with their request URIs, in the order they must be tried.</returns>
    internal static IReadOnlyList<(string Name, Uri Uri, DownloadMirror? Auth)> BuildSources(
        WordSenseModelDownloaderOptions options,
        DownloadMirror? mirror,
        ModelFile file)
    {
        var sources = new List<(string, Uri, DownloadMirror?)>();
        var escaped = Uri.EscapeDataString(file.Name);

        if (mirror is not null)
        {
            sources.Add(("mirror", Combine(mirror.BaseUri, Uri.EscapeDataString(WordSenseModelCatalog.ModelId), escaped), mirror));
        }

        if (file.HuggingFace && options.HuggingFaceBaseUri is not null)
        {
            sources.Add(("huggingface", Combine(options.HuggingFaceBaseUri, escaped), null));
        }

        if (options.GitHubLfsBaseUri is not null)
        {
            sources.Add((
                "github-lfs",
                Combine(options.GitHubLfsBaseUri, Uri.EscapeDataString(options.GitHubLfsRef), "model", escaped),
                null));
        }

        return sources;
    }

    /// <summary>
    ///     Appends already-escaped segments to a base URI.
    /// </summary>
    /// <param name="baseUri">The base URI.</param>
    /// <param name="segments">The escaped path segments.</param>
    /// <returns>The combined URI.</returns>
    private static Uri Combine(Uri baseUri, params string[] segments)
    {
        var builder = new UriBuilder(baseUri);
        builder.Path = string.Join('/', segments.Prepend(builder.Path.TrimEnd('/')));
        return builder.Uri;
    }

    /// <summary>
    ///     Downloads one file, trying each source until a verified copy is stored.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="options">The download options.</param>
    /// <param name="mirror">The effective mirror, or null.</param>
    /// <param name="file">The file to obtain.</param>
    /// <param name="destination">The final path of the file.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the file is stored.</returns>
    private static async Task DownloadFileAsync(
        HttpClient client,
        WordSenseModelDownloaderOptions options,
        DownloadMirror? mirror,
        ModelFile file,
        string destination,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var (name, uri, auth) in BuildSources(options, mirror, file))
        {
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(options.RequestTimeout);

                var actual = await FetchAsync(client, options, name, uri, auth, file, temp, timeout.Token).ConfigureAwait(false);
                if (!string.Equals(actual, file.Sha256, StringComparison.Ordinal))
                {
                    failures.Add($"{name}: SHA-256 mismatch for '{file.Name}'.");
                    continue;
                }

                File.Move(temp, destination, true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                // Request failure, block, timeout or local IO problem: fall through to the next source
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        throw new WordSenseModelDownloadException(
            $"Could not obtain '{file.Name}' from any source. " + string.Join(" | ", failures),
            failures);
    }

    /// <summary>
    ///     Requests one URI and streams it to a temporary file, returning the digest of what was written.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="options">The download options.</param>
    /// <param name="sourceName">The source name for progress reports.</param>
    /// <param name="uri">The request URI.</param>
    /// <param name="auth">The mirror whose credentials apply, or null for an anonymous request.</param>
    /// <param name="file">The file being downloaded.</param>
    /// <param name="temp">The temporary file path.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The lower-case hexadecimal SHA-256 of the received bytes.</returns>
    private static async Task<string> FetchAsync(
        HttpClient client,
        WordSenseModelDownloaderOptions options,
        string sourceName,
        Uri uri,
        DownloadMirror? auth,
        ModelFile file,
        string temp,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // Credentials go only to the mirror's own location, never to the public sources
        if (auth is not null)
        {
            if (auth.BearerToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.BearerToken);
            }
            else if (auth.Credentials is not null)
            {
                var raw = Encoding.UTF8.GetBytes($"{auth.Credentials.UserName}:{auth.Credentials.Password}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            }
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            options.Progress?.Report(new WordSenseModelDownloadProgress(file.Name, sourceName, received, total));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    ///     Deletes a file, ignoring failures.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup of a temporary file
        }
    }
}
