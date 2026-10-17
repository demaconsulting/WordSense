using System.Net;

namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model: something that can provide an <see cref="IWordSenseClassifier"/>.
/// </summary>
/// <remarks>
///     <para>
///     Model packages provide implementations: <c>DemaConsulting.WordSense.Basic</c> chooses senses with a small
///     statistical model and needs no model files; <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> run an encoder model and download its files once. The usual
///     sequence is to call <see cref="EnsureModelAsync"/> once, then <see cref="Create"/>, then classify as many texts
///     as needed.
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. A model that holds native resources (the encoder models)
///     also implements <see cref="IDisposable"/>; dispose it once, after every classification has finished. The basic
///     model holds nothing to release.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     IWordSenseModel model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///     WordSense[] senses = await classifier.ClassifyAsync(["to", "record", "the", "record"]);
///     Console.WriteLine(senses[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///     </code>
/// </example>
public interface IWordSenseModel
{
    /// <summary>
    ///     Makes sure every file the model needs is present locally and verified, downloading what is missing.
    /// </summary>
    /// <param name="mirror">
    ///     A mirror to try before the public source, or <see langword="null"/> to use the mirror configured by the
    ///     <c>WORDSENSE_MIRROR_URL</c> environment variable (and its credential variables), if any. Ignored by a model
    ///     that needs no files.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     Files that are already present and valid are never downloaded again. Concurrent calls share one download.
    ///     A failed call can be retried.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained from any source.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a classifier on this model.
    /// </summary>
    /// <returns>A classifier that shares this model and is safe for concurrent use.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the model files are not available; call <see cref="EnsureModelAsync"/> first.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    IWordSenseClassifier Create();
}

/// <summary>
///     Describes a user-supplied HTTP(S) mirror that is tried before the public download source, plus where downloaded
///     files are stored.
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
///     absolute, use <c>http</c> or <c>https</c>, and carry no query, fragment or user-info.
///     </para>
///     <para>
///     Precedence: a <see cref="MirrorOptions"/> passed to <see cref="IWordSenseModel.EnsureModelAsync"/> always wins;
///     the environment is not consulted then. Only when no mirror is passed do the variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> configure one (the URL variable is required, the others are optional, and
///     user and password go together). Without either, only the public source is used. Models that need no files
///     ignore the mirror. The invalid configurations (for example a token together with a user, or credentials for a
///     plain <c>http</c> host) are reported by <see cref="IWordSenseModel.EnsureModelAsync"/> as an
///     <see cref="ArgumentException"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"), BearerToken: "token")
///     {
///         Timeout = TimeSpan.FromMinutes(2),
///     };
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
public sealed record MirrorOptions(
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
