using System.Net;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Describes a user-supplied HTTP(S) mirror that is tried before any public download source, plus
///     the single authentication mechanism (if any) it requires.
/// </summary>
/// <remarks>
///     <para>
///     Some environments block public download hosts. A mirror lets such an environment serve the model
///     files from an internal location. Files are requested at
///     <c>{BaseUri}/{model-id}/{file-name}</c> (for example
///     <c>https://mirror.example/models/all-MiniLM-L6-v2/vocab.txt</c>); only the base is replaced, the
///     relative layout stays fixed. The mirror can be configured programmatically through
///     <see cref="WordSenseModelDownloaderOptions.Mirror"/> or through the environment variables named by
///     <see cref="UrlEnvironmentVariable"/>, <see cref="UserEnvironmentVariable"/>,
///     <see cref="PasswordEnvironmentVariable"/> and <see cref="BearerTokenEnvironmentVariable"/>; the
///     programmatic option wins when both are present.
///     </para>
///     <para>
///     <see cref="Credentials"/> (HTTP Basic) and <see cref="BearerToken"/> are mutually exclusive and are
///     sent preemptively only to requests within the mirror's own base location, so a redirect to another
///     host never receives them. Because they are sent without a challenge, they are rejected for plain
///     <c>http</c> mirrors unless the host is a loopback address. The base URI must be absolute, use
///     <c>http</c> or <c>https</c>, and carry no query, fragment or user-info.
///     </para>
/// </remarks>
public sealed record DownloadMirror
{
    /// <summary>
    ///     The name of the environment variable that supplies the mirror base URL.
    /// </summary>
    public const string UrlEnvironmentVariable = "WORDSENSE_MIRROR_URL";

    /// <summary>
    ///     The name of the environment variable that supplies the mirror HTTP Basic user name.
    /// </summary>
    public const string UserEnvironmentVariable = "WORDSENSE_MIRROR_USER";

    /// <summary>
    ///     The name of the environment variable that supplies the mirror HTTP Basic password.
    /// </summary>
    public const string PasswordEnvironmentVariable = "WORDSENSE_MIRROR_PASSWORD";

    /// <summary>
    ///     The name of the environment variable that supplies the mirror bearer token.
    /// </summary>
    public const string BearerTokenEnvironmentVariable = "WORDSENSE_MIRROR_BEARER_TOKEN";

    /// <summary>
    ///     Initializes a new instance of the <see cref="DownloadMirror"/> record, validating every field.
    /// </summary>
    /// <param name="baseUri">The absolute <c>http</c> or <c>https</c> base location of the mirror.</param>
    /// <param name="credentials">The HTTP Basic credential, or null for none; exclusive with <paramref name="bearerToken"/>.</param>
    /// <param name="bearerToken">The bearer token, or null for none; exclusive with <paramref name="credentials"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUri"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the base URI is relative, not http(s), has a query, fragment or user-info, when both
    ///     credential kinds are given, when the bearer token is blank, or when a credential is combined with
    ///     a non-loopback plain <c>http</c> base URI.
    /// </exception>
    public DownloadMirror(Uri baseUri, NetworkCredential? credentials = null, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        // Reject configurations that could never work or that would leak a secret, at construction time
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Mirror base URI must be absolute.", nameof(baseUri));
        }

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mirror base URI scheme must be 'http' or 'https'.", nameof(baseUri));
        }

        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException("Mirror base URI must not include a query string or fragment.", nameof(baseUri));
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new ArgumentException(
                "Mirror base URI must not include a user-info component; use credentials or bearerToken instead.",
                nameof(baseUri));
        }

        if (credentials is not null && bearerToken is not null)
        {
            throw new ArgumentException("Specify at most one of credentials or bearerToken.", nameof(bearerToken));
        }

        if (bearerToken is not null && string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("Mirror bearer token must not be empty or whitespace-only.", nameof(bearerToken));
        }

        if ((credentials is not null || bearerToken is not null) &&
            string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !IsLoopbackHost(baseUri.Host))
        {
            throw new ArgumentException(
                "A credentialed mirror must use the 'https' scheme unless the host is a loopback address.",
                nameof(baseUri));
        }

        BaseUri = baseUri;
        Credentials = credentials;
        BearerToken = bearerToken;
    }

    /// <summary>
    ///     Gets the absolute <c>http</c> or <c>https</c> base location of the mirror.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    ///     Gets the HTTP Basic credential, or null when none is needed.
    /// </summary>
    public NetworkCredential? Credentials { get; }

    /// <summary>
    ///     Gets the bearer token, or null when none is needed.
    /// </summary>
    public string? BearerToken { get; }

    /// <summary>
    ///     Builds a mirror from environment variables, or returns null when no mirror URL is configured.
    /// </summary>
    /// <remarks>
    ///     Blank values count as unset. A credential (user, password or bearer token) without a URL is
    ///     rejected because it almost always indicates a misspelled or missing URL rather than an intended
    ///     no-op. A user without a password (or the reverse) is rejected for the same reason.
    /// </remarks>
    /// <param name="getEnvironmentVariable">Reads an environment variable by name; null when unset.</param>
    /// <returns>The configured mirror, or null when <see cref="UrlEnvironmentVariable"/> is blank.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="getEnvironmentVariable"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the configuration is invalid.</exception>
    public static DownloadMirror? FromEnvironment(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        // Read each variable, treating blank as unset
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
        var url = Clean(getEnvironmentVariable(UrlEnvironmentVariable));
        var user = Clean(getEnvironmentVariable(UserEnvironmentVariable));
        var password = Clean(getEnvironmentVariable(PasswordEnvironmentVariable));
        var token = Clean(getEnvironmentVariable(BearerTokenEnvironmentVariable));

        if (url is null)
        {
            if (user is not null || password is not null || token is not null)
            {
                throw new ArgumentException(
                    $"{UserEnvironmentVariable}, {PasswordEnvironmentVariable} and {BearerTokenEnvironmentVariable} " +
                    $"require {UrlEnvironmentVariable}; none of them has any effect without a mirror URL.");
            }

            return null;
        }

        // The rejected value is deliberately not echoed: a mistyped URL can still carry a pasted credential
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("The mirror URL is not a valid absolute URL.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The mirror URL must use the http or https scheme.");
        }

        if ((user is null) != (password is null))
        {
            throw new ArgumentException("Mirror user and mirror password must both be supplied together.");
        }

        var credentials = user is not null ? new NetworkCredential(user, password) : null;
        return new DownloadMirror(baseUri, credentials, token);
    }

    /// <summary>
    ///     Determines whether a host can only ever refer to the local machine.
    /// </summary>
    /// <param name="host">The URI host.</param>
    /// <returns>True for <c>localhost</c> and loopback IP literals.</returns>
    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }

    /// <summary>
    ///     Writes the record members without ever printing the bearer token or a password.
    /// </summary>
    /// <param name="builder">The builder receiving the text.</param>
    /// <returns>Always true.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(BaseUri)} = {BaseUri}, ");
        builder.Append($"{nameof(Credentials)} = {(Credentials is null ? "null" : "***")}, ");
        builder.Append($"{nameof(BearerToken)} = {(BearerToken is null ? "null" : "***")}");
        return true;
    }
}
