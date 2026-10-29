using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     One model file, with the SHA-256 digest every copy must match.
/// </summary>
/// <param name="Name">The file name, which is also its name beneath every source's model folder.</param>
/// <param name="Sha256">The pinned lower-case hexadecimal SHA-256 digest.</param>
/// <param name="Length">The exact size in bytes, or 0 when unknown; a download is aborted as soon as it exceeds the size
/// (or <see cref="ModelDownloader.MaxUnpinnedLength"/> when unknown).</param>
internal sealed record ModelFile(string Name, string Sha256, long Length = 0);

/// <summary>
///     Everything the shared ONNX implementation needs to know about one model.
/// </summary>
/// <param name="ModelId">The model identifier: the folder name beneath <c>model/</c>, a mirror base and the store root.</param>
/// <param name="Files">The files that make up the model, each with its pinned digest.</param>
/// <param name="OnnxFileName">The name of the ONNX encoder among <paramref name="Files"/>.</param>
/// <param name="VocabularyFileName">The name of the vocabulary among <paramref name="Files"/>.</param>
/// <param name="LowerCase">True when the model is uncased.</param>
/// <param name="WeightsAssembly">The assembly that embeds the trained weights.</param>
/// <param name="WeightsResourceName">The manifest resource name of the trained weights.</param>
/// <param name="HiddenSize">The hidden size (output dimension) of the encoder.</param>
/// <param name="HeadResourceName">The manifest resource name, in <paramref name="WeightsAssembly"/>, of the part-of-speech head, or null for a model without one.</param>
internal sealed record ModelSpec(
    string ModelId,
    IReadOnlyList<ModelFile> Files,
    string OnnxFileName,
    string VocabularyFileName,
    bool LowerCase,
    Assembly WeightsAssembly,
    string WeightsResourceName,
    int HiddenSize,
    string? HeadResourceName = null);

/// <summary>
///     Obtains and verifies the files of a model: from the per-user store, a mirror or the GitHub LFS copy.
/// </summary>
/// <remarks>
///     A valid file in the store is reused without network access. Otherwise each missing file is requested from the
///     mirror (when there is one) and then from GitHub, downloaded to a uniquely named temporary file, verified against
///     its pinned SHA-256 and moved into place, so a partial or corrupt download is never visible under the final name.
/// </remarks>
internal static class ModelDownloader
{
    /// <summary>
    ///     The branch the GitHub copy is read from.
    /// </summary>
    public const string GitHubRef = "main";

    /// <summary>
    ///     The largest download accepted for a file whose size is not pinned (1 GiB).
    /// </summary>
    public const long MaxUnpinnedLength = 1L << 30;

    /// <summary>
    ///     The default longest time a single download attempt of a single file may take.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The base location of the GitHub LFS copy of the repository.
    /// </summary>
    public static readonly Uri GitHubBaseUri = new("https://media.githubusercontent.com/media/demaconsulting/WordSense/");

    /// <summary>
    ///     Gets the default directory beneath which model folders are stored for the current user.
    /// </summary>
    /// <returns>
    ///     <c>{LocalApplicationData}/DemaConsulting/WordSense/models</c>, with the application base directory replacing
    ///     the local application data directory when the platform has none.
    /// </returns>
    public static string DefaultStoreRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            local = AppContext.BaseDirectory;
        }

        return Path.Join(local, "DemaConsulting", "WordSense", "models");
    }

    /// <summary>
    ///     Validates a store root and resolves it to a full path.
    /// </summary>
    /// <param name="storeRoot">The store root, or null for the per-user default store.</param>
    /// <returns>The full path of the store root.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown (with the parameter name <c>StoreRoot</c>) when the store root is empty, whitespace-only or a path that
    ///     the platform rejects.
    /// </exception>
    [SuppressMessage("Major Bug", "S3928:Parameter names used into ArgumentException constructors should match an existing one", Justification = "The invalid value is the StoreRoot member of the caller's DownloadOptions, which is the name the contract reports.")]
    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "The invalid value is the StoreRoot member of the caller's DownloadOptions, which is the name the contract reports.")]
    internal static string ResolveStoreRoot(string? storeRoot)
    {
        if (storeRoot is null)
        {
            return Path.GetFullPath(DefaultStoreRoot());
        }

        // Whitespace is a legal file name on some platforms, so reject it explicitly to behave the same everywhere
        if (string.IsNullOrWhiteSpace(storeRoot))
        {
            throw new ArgumentException("The store root must not be empty or whitespace-only.", nameof(DownloadOptions.StoreRoot));
        }

        try
        {
            return Path.GetFullPath(storeRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"The store root is not a valid path: {ex.Message}", nameof(DownloadOptions.StoreRoot), ex);
        }
    }

    /// <summary>
    ///     Computes the lower-case hexadecimal SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The digest.</returns>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    ///     Verifies that a directory holds every model file with its pinned digest.
    /// </summary>
    /// <param name="spec">The model.</param>
    /// <param name="directory">The directory.</param>
    /// <exception cref="FileNotFoundException">Thrown when a file is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when a file does not match its digest.</exception>
    public static void Verify(ModelSpec spec, string directory)
    {
        foreach (var file in spec.Files)
        {
            var path = Path.Join(directory, file.Name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"The model file '{file.Name}' was not found in '{directory}'.", path);
            }

            if (!string.Equals(HashFile(path), file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The model file '{file.Name}' in '{directory}' does not match its pinned SHA-256.");
            }
        }
    }

    /// <summary>
    ///     Ensures that every file of a model is present and verified in the store, downloading what is missing.
    /// </summary>
    /// <param name="spec">The model.</param>
    /// <param name="options">The download options (mirror, store root, timeout), or null to use the environment's mirror (if any).</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <param name="client">The HTTP client to use, or null to create one that follows redirects manually.</param>
    /// <param name="gitHubBaseUri">The GitHub LFS base location, or null for the default.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable, or null for the process environment.</param>
    /// <returns>The directory that holds the verified files.</returns>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained from any source.</exception>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration or the store root is invalid.</exception>
    public static async Task<string> EnsureAsync(
        ModelSpec spec,
        DownloadOptions? options,
        CancellationToken cancellationToken,
        HttpClient? client = null,
        Uri? gitHubBaseUri = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var timeout = options?.Timeout ?? DefaultTimeout;
        if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), timeout, "The timeout must be positive (or infinite) and no longer than about 49 days.");
        }

        // Explicit options beat the environment; an invalid configuration fails fast
        var effective = options is not null
            ? DownloadMirror.FromOptions(options)
            : DownloadMirror.FromEnvironment(getEnvironmentVariable ?? Environment.GetEnvironmentVariable);

        var directory = Path.Join(ResolveStoreRoot(options?.StoreRoot), spec.ModelId);
        Directory.CreateDirectory(directory);

        // The private client never follows redirects itself, so credentials are attached per hop
        var ownsClient = client is null;
        client ??= new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        try
        {
            foreach (var file in spec.Files)
            {
                var path = Path.Join(directory, file.Name);
                if (File.Exists(path) && string.Equals(HashFile(path), file.Sha256, StringComparison.Ordinal))
                {
                    continue;
                }

                await DownloadFileAsync(new DownloadContext(client, spec, effective, gitHubBaseUri ?? GitHubBaseUri, timeout), file, path, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }

        return directory;
    }

    /// <summary>
    ///     Builds the ordered list of candidate sources for one file.
    /// </summary>
    /// <param name="spec">The model.</param>
    /// <param name="mirror">The effective mirror, or null.</param>
    /// <param name="gitHubBaseUri">The GitHub LFS base location.</param>
    /// <param name="file">The file being requested.</param>
    /// <returns>Source names with their request URIs, in the order they must be tried.</returns>
    internal static IReadOnlyList<(string Name, Uri Uri, DownloadMirror? Auth)> BuildSources(ModelSpec spec, DownloadMirror? mirror, Uri gitHubBaseUri, ModelFile file)
    {
        var sources = new List<(string, Uri, DownloadMirror?)>();
        var id = Uri.EscapeDataString(spec.ModelId);
        var name = Uri.EscapeDataString(file.Name);
        if (mirror is not null)
        {
            sources.Add(("mirror", Combine(mirror.BaseUri, id, name), mirror));
        }

        sources.Add(("github-lfs", Combine(gitHubBaseUri, Uri.EscapeDataString(GitHubRef), "model", id, name), null));
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
    ///     The settings shared by every file download of one <see cref="EnsureAsync"/> call.
    /// </summary>
    /// <param name="Client">The HTTP client.</param>
    /// <param name="Spec">The model.</param>
    /// <param name="Mirror">The effective mirror, or null.</param>
    /// <param name="LfsBaseUri">The GitHub LFS base location.</param>
    /// <param name="AttemptTimeout">The time limit of one attempt.</param>
    private readonly record struct DownloadContext(HttpClient Client, ModelSpec Spec, DownloadMirror? Mirror, Uri LfsBaseUri, TimeSpan AttemptTimeout);

    /// <summary>
    ///     Downloads one file, trying each source until a verified copy is stored.
    /// </summary>
    /// <param name="context">The settings shared by all files of the model.</param>
    /// <param name="file">The file to obtain.</param>
    /// <param name="destination">The final path of the file.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the file is stored.</returns>
    private static async Task DownloadFileAsync(
        DownloadContext context,
        ModelFile file,
        string destination,
        CancellationToken cancellationToken)
    {
        var (client, spec, mirror, gitHubBaseUri, timeout) = context;
        var failures = new List<string>();
        foreach (var (name, uri, auth) in BuildSources(spec, mirror, gitHubBaseUri, file))
        {
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(timeout);

                var actual = await FetchAsync(client, uri, auth, temp, file.Length > 0 ? file.Length : MaxUnpinnedLength, limit.Token).ConfigureAwait(false);
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

        throw new HttpRequestException($"Could not obtain '{file.Name}' of model '{spec.ModelId}' from any source. " + string.Join(" | ", failures));
    }

    /// <summary>
    ///     Requests one URI and streams it to a temporary file, returning the digest of what was written.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="uri">The request URI.</param>
    /// <param name="auth">The mirror whose credentials apply, or null for an anonymous request.</param>
    /// <param name="temp">The temporary file path.</param>
    /// <param name="maxLength">The most bytes accepted; the download is aborted beyond it.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The lower-case hexadecimal SHA-256 of the received bytes.</returns>
    private static async Task<string> FetchAsync(HttpClient client, Uri uri, DownloadMirror? auth, string temp, long maxLength, CancellationToken cancellationToken)
    {
        using var response = await SendFollowingRedirectsAsync(client, uri, auth, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxLength)
        {
            throw new HttpRequestException("The response is larger than the pinned size of the file.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxLength)
            {
                throw new HttpRequestException("The response is larger than the pinned size of the file.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
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
    /// </remarks>
    /// <param name="client">The HTTP client.</param>
    /// <param name="uri">The first request URI.</param>
    /// <param name="auth">The mirror whose credentials apply, or null for an anonymous request.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The final response, which the caller must dispose.</returns>
    private static async Task<HttpResponseMessage> SendFollowingRedirectsAsync(HttpClient client, Uri uri, DownloadMirror? auth, CancellationToken cancellationToken)
    {
        const int maxRedirects = 10;
        var current = uri;
        for (var hop = 0; hop <= maxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            ApplyAuthorization(request, auth, current);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
    ///     Attaches the mirror's credentials to a request, but only when the request goes to the mirror's own origin.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="auth">The mirror whose credentials apply, or null for an anonymous request.</param>
    /// <param name="target">The location the request is sent to.</param>
    private static void ApplyAuthorization(HttpRequestMessage request, DownloadMirror? auth, Uri target)
    {
        if (auth is null || !IsSameOrigin(auth.BaseUri, target))
        {
            return;
        }

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

    /// <summary>
    ///     Checks whether two URIs share scheme, host and port.
    /// </summary>
    /// <param name="a">The first URI.</param>
    /// <param name="b">The second URI.</param>
    /// <returns>True when they have the same origin.</returns>
    private static bool IsSameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    /// <summary>
    ///     Deletes a file, ignoring failures.
    /// </summary>
    /// <param name="path">The file path.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is harmless
        }
    }
}
