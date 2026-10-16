using System.Net;
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
/// <example>
///     Download the model ahead of time (for example in an installer or a CI step), then use it offline.
///     <code>
///     var options = new WordSenseModelDownloaderOptions
///     {
///         StoreRoot = Path.Combine(Path.GetTempPath(), "wordsense-models"),
///         Progress = new Progress&lt;WordSenseModelDownloadProgress&gt;(
///             p => Console.WriteLine($"{p.FileName} from {p.Source}: {p.BytesTransferred} bytes")),
///     };
///
///     WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync(options);
///     using var classifier = await WordSenseClassifier.CreateAsync(
///         new WordSenseClassifierOptions { ModelDirectory = location.Directory });
///     </code>
/// </example>
public static class WordSenseModelDownloader
{
    /// <summary>
    ///     Ensures that the model is present and verified in the store, downloading what is missing.
    /// </summary>
    /// <param name="options">The download options; null uses the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The verified model location.</returns>
    /// <exception cref="WordSenseModelDownloadException">Thrown when a file could not be obtained from any source.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="WordSenseModelDownloaderOptions.RequestTimeout"/> is zero or negative and is not
    ///     <see cref="Timeout.InfiniteTimeSpan"/>, or exceeds the maximum the platform timer supports.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the mirror environment configuration (<c>WORDSENSE_MIRROR_*</c>, read only when no
    ///     <see cref="WordSenseModelDownloaderOptions.Mirror"/> is given) is invalid, or when
    ///     <see cref="WordSenseModelDownloaderOptions.StoreRoot"/> is not a valid path (an empty or malformed path, or,
    ///     depending on the platform, <see cref="NotSupportedException"/> or <see cref="PathTooLongException"/> for a
    ///     path the platform cannot represent).
    /// </exception>
    /// <exception cref="IOException">Thrown when the store directory cannot be created or an existing model file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the store directory cannot be created or accessed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <remarks>
    ///     A null <paramref name="options"/> is valid and means the defaults (no <see cref="ArgumentNullException"/>).
    ///     An exception thrown by the <see cref="WordSenseModelDownloaderOptions.Progress"/> callback propagates to the
    ///     caller and the remaining sources are not tried, unless it is an <see cref="HttpRequestException"/>,
    ///     <see cref="OperationCanceledException"/>, <see cref="IOException"/> or
    ///     <see cref="UnauthorizedAccessException"/>, which count as a failure of the source being tried. When no
    ///     <see cref="WordSenseModelDownloaderOptions.Mirror"/> is given, the mirror environment variables named
    ///     on <see cref="DownloadMirror"/> are consulted.
    /// </remarks>
    /// <example>
    ///     <code>
    ///     // A corporate mirror tried before Hugging Face and GitHub; files are fetched from
    ///     // https://models.example.com/wordsense/{model-id}/{file-name}
    ///     var options = new WordSenseModelDownloaderOptions
    ///     {
    ///         Mirror = new DownloadMirror(new Uri("https://models.example.com/wordsense"), bearerToken: "token"),
    ///     };
    ///
    ///     try
    ///     {
    ///         WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync(options);
    ///         Console.WriteLine(location.ModelPath);
    ///     }
    ///     catch (WordSenseModelDownloadException ex)
    ///     {
    ///         foreach (var failure in ex.Failures)
    ///         {
    ///             Console.Error.WriteLine(failure); // one entry per source that was tried
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public static async Task<WordSenseModelLocation> EnsureModelAsync(
        WordSenseModelDownloaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WordSenseModelDownloaderOptions();

        // CancelAfter rejects these values; fail up front with a clear message instead of a late, obscure error
        var requestTimeout = options.RequestTimeout;
        if (requestTimeout != Timeout.InfiniteTimeSpan && (requestTimeout <= TimeSpan.Zero || requestTimeout.TotalMilliseconds > uint.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                requestTimeout,
                "RequestTimeout must be positive (or Timeout.InfiniteTimeSpan) and no longer than about 49 days.");
        }

        // An explicit mirror beats the environment; an invalid environment configuration fails fast
        var mirror = options.Mirror ?? DownloadMirror.FromEnvironment(options.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable);

        var root = options.StoreRoot ?? WordSenseModelLocation.DefaultStoreRoot();
        var directory = Path.Combine(Path.GetFullPath(root), WordSenseModelCatalog.DirectoryName);
        Directory.CreateDirectory(directory);

        // The private client never follows redirects itself so that FetchAsync can decide per hop whether
        // credentials may be attached; a caller-supplied client keeps whatever redirect policy it has
        var ownsClient = options.HttpClient is null;
        var client = options.HttpClient ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
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
        using var response = await SendFollowingRedirectsAsync(client, uri, auth, cancellationToken).ConfigureAwait(false);
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
    ///     Sends a GET request, following redirects manually so credentials are attached per hop.
    /// </summary>
    /// <remarks>
    ///     Credentials are attached only to a hop whose scheme, host and port equal those of the mirror's base
    ///     location, so a redirect to another host, port or a downgraded scheme is requested anonymously.
    ///     When the client follows redirects itself (a caller-supplied client with automatic redirects) this
    ///     method sees only the final response and the client's own policy applies; the default
    ///     <see cref="HttpClient"/> handler removes the Authorization header when it is redirected.
    /// </remarks>
    /// <param name="client">The HTTP client.</param>
    /// <param name="uri">The first request URI.</param>
    /// <param name="auth">The mirror whose credentials apply, or null for an anonymous request.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The final response, which the caller must dispose.</returns>
    private static async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        HttpClient client,
        Uri uri,
        DownloadMirror? auth,
        CancellationToken cancellationToken)
    {
        const int maxRedirects = 10;
        var current = uri;
        for (var hop = 0; hop <= maxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (auth is not null && IsSameOrigin(auth.BaseUri, current))
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

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var location = response.Headers.Location;
            if (location is null || response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
            {
                return response;
            }

            response.Dispose();
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (current.Scheme != Uri.UriSchemeHttp && current.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException("A redirect to a non-http(s) location was refused.");
            }
        }

        throw new HttpRequestException($"Too many redirects requesting '{uri.GetLeftPart(UriPartial.Path)}'.");
    }

    /// <summary>
    ///     Determines whether two URIs share scheme, host and port.
    /// </summary>
    /// <param name="a">The first URI.</param>
    /// <param name="b">The second URI.</param>
    /// <returns>True when the origins are equal.</returns>
    private static bool IsSameOrigin(Uri a, Uri b)
    {
        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
               a.Port == b.Port;
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
