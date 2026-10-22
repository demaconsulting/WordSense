using System.Net;

namespace DemaConsulting.WordSense;

/// <summary>
///     Describes how model files are downloaded: a user-supplied HTTP(S) mirror that is tried before the public
///     download source, optional credentials, where downloaded files are stored, and the timeout of one download.
/// </summary>
/// <remarks>
///     <para>
///     Some environments block public download hosts. A mirror serves the model files from an internal location. A
///     file is requested at <c>{BaseUri}/{model-id}/{file-name}</c>, where the model id is the <c>ModelId</c> of the
///     model (the folder name of the model, for example <c>all-MiniLM-L6-v2-int8</c> or <c>bert-base-cased-int8</c>).
///     A mirror therefore holds one folder per model that is used, for example:
///     </para>
///     <code>
///     https://models.example.com/wordsense/               (the BaseUri)
///         all-MiniLM-L6-v2-int8/model_int8.onnx
///         all-MiniLM-L6-v2-int8/vocab.txt
///         bert-base-cased-int8/model_int8.onnx
///         bert-base-cased-int8/vocab.txt
///         bert-base-cased-int8/config.json
///         bert-base-cased-int8/tokenizer.json
///     </code>
///     <para>
///     For each file, the mirror is tried first and the public source (the GitHub copy of the repository) second; a
///     source that fails, is blocked, times out or serves bytes that do not match the pinned SHA-256 is discarded and
///     the next source is tried. A file that is already present and valid in the store is never requested again, so the
///     mirror is not contacted at all when every file is present. The files are always verified, whichever source
///     delivered them.
///     </para>
///     <para>
///     <see cref="BearerToken"/> and <see cref="Credentials"/> are mutually exclusive and are sent only to requests
///     whose scheme, host and port equal those of <see cref="BaseUri"/>. Because they are sent without a challenge,
///     they are rejected for a plain <c>http</c> mirror unless the host is a loopback address. The base URI must be
///     absolute, use <c>http</c> or <c>https</c>, and carry no query, fragment or user-info; a relative URI, another
///     scheme, a query, a fragment or user-info is an error. The record does not validate its values: the error is
///     reported by <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> (not by the constructor of this record) as a
///     <see cref="DownloadConfigurationException"/>, also when all files are already present. A
///     value of <c>WORDSENSE_MIRROR_URL</c> that is not an absolute <c>http</c> or <c>https</c> URL, or that has a query,
///     a fragment or user-info, is a <see cref="DownloadConfigurationException"/> as well.
///     </para>
///     <para>
///     Precedence: a <see cref="DownloadOptions"/> passed to <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/>
///     always wins; the environment is not consulted then. Only when no options are passed do the variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> configure a mirror (the URL variable is required, the others are optional, and
///     user and password go together). Without either, only the public source is used. Details of the variables: a
///     blank value counts as unset; <c>WORDSENSE_MIRROR_USER</c>
///     without <c>WORDSENSE_MIRROR_PASSWORD</c> (or the reverse) is an error; any of user, password or token without
///     <c>WORDSENSE_MIRROR_URL</c> is an error too (it is almost always a misspelled URL variable), and so is a token
///     together with a user. The environment is read only when the settings are needed, that is by an
///     <c>EnsureModelAsync</c> without <see cref="DownloadOptions"/> or by the lazy ensure of an ambiguous word, and
///     never when the model was constructed with a model directory (the settings, valid or not, are then ignored and
///     nothing is thrown). The invalid configurations (for example a token together with a user, or credentials for a
///     plain <c>http</c> host) are reported by <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> (or by the lazy ensure
///     of <see cref="IWordSenseModel.ClassifyAsync"/>) as a
///     <see cref="DownloadConfigurationException"/> whose inner exception is the <see cref="ArgumentException"/>. A
///     <see cref="Timeout"/> that is zero, negative or longer than about 49 days is invalid too (the inner exception is an
///     <see cref="ArgumentOutOfRangeException"/>; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit); the
///     timeout applies only to a <see cref="DownloadOptions"/> that you pass, because the environment variables have
///     no timeout (the default of 5 minutes applies then). The timeout limits one download attempt: an attempt that
///     exceeds it is not reported as an exception of its own (not as <see cref="TimeoutException"/> or
///     <see cref="OperationCanceledException"/>); that source is discarded and the next one is tried, and when no
///     source delivers the file, <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> fails with
///     <see cref="ModelUnavailableException"/>. When the model was constructed with a model directory, no
///     <see cref="DownloadConfigurationException"/> is thrown for invalid <see cref="DownloadOptions"/> (not for the base URI, the
///     credentials or the timeout), because the options are not read at all.
///     </para>
///     <para>
///     Store location: downloaded files are kept in <c>{StoreRoot}/{model-id}/{file}</c>; without a store root, the
///     root is <c>{LocalApplicationData}/DemaConsulting/WordSense/models</c> (the application base directory when the
///     platform has no local application data folder). Concretely, as .NET reports the local application data folder:
///     on Windows <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> (for example
///     <c>C:\Users\Ann\AppData\Local\DemaConsulting\WordSense\models</c>); on Linux and macOS
///     <c>$XDG_DATA_HOME/DemaConsulting/WordSense/models</c>, which is
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> when <c>XDG_DATA_HOME</c> is not set (on macOS too, not
///     <c>~/Library/Application Support</c>). The only way to relocate the store is
///     <see cref="StoreRoot"/>, and no environment variable controls it: the four <c>WORDSENSE_MIRROR_*</c>
///     variables configure only the mirror. <see cref="BaseUri"/> is a required constructor argument, so a
///     <see cref="DownloadOptions"/> always describes a mirror; there is no way to set only a store root. To use files
///     that you copied yourself, pass the directory to the constructor of the model instead (see the model packages);
///     that directory is flat (the model files directly in it, no <c>{model-id}</c> subfolder), and a mirror is never
///     used with it.
///     </para>
///     <para>
///     Store root rules: <see cref="StoreRoot"/> may be relative; it is resolved with <c>Path.GetFullPath</c> against the
///     current directory of the process at the time the ensure runs, so prefer an absolute path (the directory is
///     created when it does not exist). Offline route through the store: files that you copy into
///     <c>{StoreRoot}/{model-id}/</c> (or into the default store, then no <see cref="DownloadOptions"/> are needed) are
///     hashed by <c>EnsureModelAsync</c>; a file that matches its pinned SHA-256 is kept and never requested, so when
///     every file is present and valid nothing is downloaded and the mirror (and the public source) is not contacted,
///     and a file that is missing or does not match is downloaded again (and replaced). Because
///     <see cref="DownloadOptions"/> need a <see cref="BaseUri"/>, use any valid URI there when you only want a
///     custom <see cref="StoreRoot"/> offline; on a machine without network access a missing or damaged file then
///     ends in <see cref="ModelUnavailableException"/>, whereas a model directory passed to the constructor never
///     touches the network at all.
///     </para>
///     <para>
///     Network notes: downloads use <c>HttpClient</c> with the default proxy behavior of .NET (the system proxy
///     settings, and the <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c> and <c>NO_PROXY</c> variables on Linux and macOS), with
///     no proxy credentials of its own. Allow-list the host of your mirror, if any, and for the public source
///     <c>media.githubusercontent.com</c> (files at
///     https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/{model-id}/{file}); the
///     library follows HTTP redirects itself (http or https only, up to a fixed limit), so a host that the public
///     source redirects to must be reachable too, and mirror credentials are sent only to the mirror's own origin.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Relocate the store (a mirror is mandatory: BaseUri has no default). StoreRoot is the folder that holds the
///     // model folders; on Linux and macOS: "/var/lib/wordsense", on Windows: @"C:\ProgramData\WordSense\models"
///     var options = new DownloadOptions(new Uri("https://models.example.com/wordsense"), BearerToken: "token")
///     {
///         StoreRoot = OperatingSystem.IsWindows() ? @"C:\ProgramData\WordSense\models" : "/var/lib/wordsense",
///         Timeout = TimeSpan.FromMinutes(2),
///     };
///
///     // Pass it to EnsureModelAsync of any downloadable model (MiniLMWordSenseModel or BertBaseCasedWordSenseModel)
///     async Task PrepareAsync(IDownloadableWordSenseModel model)
///     {
///         await model.EnsureModelAsync(options);   // mirror first, then the public source; files verified
///         ClassifyResult[] results = await model.ClassifyAsync(["to", "record", "the", "record"]);
///     }
///     </code>
/// </example>
/// <param name="BaseUri">The absolute <c>http</c> or <c>https</c> base location of the mirror.</param>
/// <param name="BearerToken">The bearer token, or null when none is needed. Exclusive with <paramref name="Credentials"/>.</param>
/// <param name="Credentials">The HTTP Basic credential, or null when none is needed. Exclusive with <paramref name="BearerToken"/>.</param>
/// <param name="StoreRoot">
///     The directory beneath which model folders are stored, or null for the per-user default
///     (<c>{LocalApplicationData}/DemaConsulting/WordSense/models</c>).
/// </param>
/// <param name="Timeout">The longest time a single download attempt of a single file may take, or null for 5 minutes.</param>
public sealed record DownloadOptions(
    Uri BaseUri,
    string? BearerToken = null,
    ICredentials? Credentials = null,
    string? StoreRoot = null,
    TimeSpan? Timeout = null)
{
    /// <summary>
    ///     Writes the members without ever printing the bearer token or a credential.
    /// </summary>
    /// <param name="builder">The builder receiving the text.</param>
    /// <returns>Always true.</returns>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        // Print scheme, host, port and path only: user-info, query and fragment may carry secrets
        var location = "***";
        if (BaseUri is { IsAbsoluteUri: true } uri)
        {
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            location = $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";
        }

        builder.Append($"{nameof(BaseUri)} = {location}, ");
        builder.Append($"{nameof(BearerToken)} = {(BearerToken is null ? "null" : "***")}, ");
        builder.Append($"{nameof(Credentials)} = {(Credentials is null ? "null" : "***")}, ");
        builder.Append($"{nameof(StoreRoot)} = {StoreRoot ?? "null"}, ");
        builder.Append($"{nameof(Timeout)} = {(Timeout is null ? "null" : Timeout.ToString())}");
        return true;
    }
}
