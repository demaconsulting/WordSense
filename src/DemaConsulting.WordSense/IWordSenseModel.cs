using System.Net;

namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model: something that can provide an <see cref="IWordSenseClassifier"/>.
/// </summary>
/// <remarks>
///     <para>
///     Model packages provide implementations: <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> run an encoder model and download its files once, and
///     <c>DemaConsulting.WordSense.Basic</c> needs no model files at all. The usual sequence is to call
///     <see cref="EnsureModelAsync"/> once, then <see cref="Create"/>, then classify as many texts as needed.
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. Dispose it once, after every call has finished; a
///     classification that needs the model after disposal throws <see cref="ObjectDisposedException"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using IWordSenseModel model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     await model.EnsureModelAsync(null, CancellationToken.None);
///     IWordSenseClassifier classifier = model.Create();
///     WordSense[] senses = await classifier.ClassifyAsync(["to", "record", "the", "record"], CancellationToken.None);
///     Console.WriteLine(senses[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///     </code>
/// </example>
public interface IWordSenseModel : IDisposable
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
    Task EnsureModelAsync(MirrorOptions? mirror, CancellationToken cancellationToken);

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
///     file is requested at <c>{BaseUri}/{model-id}/{file-name}</c>, where the model id is the folder name of the model
///     (for example <c>all-MiniLM-L6-v2-int8</c>). Each file must match its pinned SHA-256; a mirror copy that does not
///     match is discarded and the next source is tried.
///     </para>
///     <para>
///     <see cref="BearerToken"/> and <see cref="Credentials"/> are mutually exclusive and are sent only to requests
///     whose scheme, host and port equal those of <see cref="BaseUri"/>. Because they are sent without a challenge,
///     they are rejected for a plain <c>http</c> mirror unless the host is a loopback address. The base URI must be
///     absolute, use <c>http</c> or <c>https</c>, and carry no query, fragment or user-info.
///     </para>
///     <para>
///     When no mirror is passed to <see cref="IWordSenseModel.EnsureModelAsync"/>, the variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> configure one.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"))
///     {
///         BearerToken = "token",
///         Timeout = TimeSpan.FromMinutes(2),
///     };
///     </code>
/// </example>
/// <param name="BaseUri">The absolute <c>http</c> or <c>https</c> base location of the mirror.</param>
public sealed record MirrorOptions(Uri BaseUri)
{
    /// <summary>
    ///     Gets the bearer token, or null when none is needed. Exclusive with <see cref="Credentials"/>.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    ///     Gets the HTTP Basic credential, or null when none is needed. Exclusive with <see cref="BearerToken"/>.
    /// </summary>
    public ICredentials? Credentials { get; init; }

    /// <summary>
    ///     Gets the directory beneath which model folders are stored, or null for the per-user default
    ///     (<c>{LocalApplicationData}/DemaConsulting/WordSense/models</c>).
    /// </summary>
    public string? StoreRoot { get; init; }

    /// <summary>
    ///     Gets the longest time a single download attempt of a single file may take, or null for 5 minutes.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    ///     Writes the members without ever printing the bearer token or a credential.
    /// </summary>
    /// <param name="builder">The builder receiving the text.</param>
    /// <returns>Always true.</returns>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"{nameof(BaseUri)} = {BaseUri}, ");
        builder.Append($"{nameof(BearerToken)} = {(BearerToken is null ? "null" : "***")}, ");
        builder.Append($"{nameof(Credentials)} = {(Credentials is null ? "null" : "***")}");
        return true;
    }
}
