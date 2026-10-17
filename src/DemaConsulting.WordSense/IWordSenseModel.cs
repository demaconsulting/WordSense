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
///     as needed. <see cref="EnsureModelAsync"/> is required for the encoder models even when you supply a model
///     directory (it verifies the files against their pinned SHA-256 digests, with no network access); it is optional
///     for the Basic model, whose <see cref="Create"/> works at any time, but calling it keeps the code the same for
///     every model.
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. A model that holds native resources (the encoder models)
///     also implements <see cref="IDisposable"/>; dispose it once, after every classification has finished. The basic
///     model holds nothing to release.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent; measured values are in the build notes): the first access
///     to <see cref="WordLexicon.Words"/> takes about 120 ms and about 43 MiB, and happens once per process for every
///     model; the Basic model adds a table load of about 22 ms on its first classification; an encoder model adds
///     about 0.4 s (MiniLM) or 0.5 s (BertBaseCased) to open its ONNX session on the first <see cref="Create"/>, and
///     <see cref="EnsureModelAsync"/> reads and hashes every model file once, which takes longer for the larger
///     files. Warm the model at startup (ensure, create and classify one short text) if the first request must be fast.
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
    ///     that needs no files, and by an encoder model that was given a model directory.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     <para>
    ///     Files that are already present and valid are never downloaded again. Concurrent calls share one download.
    ///     A failed call can be retried. The Basic model needs no files and completes at once.
    ///     </para>
    ///     <para>
    ///     When an encoder model was constructed with a model directory, this call is still required: it checks that
    ///     every file exists in that directory (directly in it, not in a subfolder) and matches its pinned SHA-256,
    ///     and it never touches the network. Without a directory it verifies the per-user store, or the store
    ///     beneath <see cref="MirrorOptions.StoreRoot"/>, and downloads what is missing or invalid.
    ///     </para>
    ///     <para>
    ///     Failures: a missing or wrong file in a supplied directory throws <see cref="FileNotFoundException"/> or
    ///     <see cref="InvalidDataException"/>; a file that no source could deliver verified throws
    ///     <see cref="HttpRequestException"/>; an invalid mirror configuration throws <see cref="ArgumentException"/>
    ///     (an invalid timeout throws its subclass <see cref="ArgumentOutOfRangeException"/>); an unwritable store
    ///     reports the file system exception of the platform (for example <see cref="IOException"/> or
    ///     <see cref="UnauthorizedAccessException"/>); a disposed model throws <see cref="ObjectDisposedException"/>;
    ///     a cancelled token throws <see cref="OperationCanceledException"/>. The Basic model throws only the
    ///     cancellation exception.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained, verified, from any source (encoder model without a model directory).</exception>
    /// <exception cref="FileNotFoundException">Thrown when a supplied model directory does not exist or lacks a model file (encoder model with a model directory).</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256 (encoder model with a model directory).</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed (encoder models).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a classifier on this model.
    /// </summary>
    /// <returns>A classifier that shares this model and is safe for concurrent use.</returns>
    /// <remarks>
    ///     An encoder model needs <see cref="EnsureModelAsync"/> to have completed first; its first call loads the ONNX
    ///     session and the weights (about 0.4 to 0.5 s, approximate and machine dependent). The Basic model needs no
    ///     files, so its <see cref="Create"/> works at any time (it loads the word lexicon, about 120 ms on first use).
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown by an encoder model when the model files are not available; call <see cref="EnsureModelAsync"/> first. The Basic model never throws it.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed (encoder models).</exception>
    /// <exception cref="InvalidDataException">Thrown by an encoder model when its embedded weights or ONNX file cannot be read.</exception>
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
///     <para>
///     Store location: downloaded files are kept in <c>{StoreRoot}/{model-id}/{file}</c>; without a store root, the
///     root is <c>{LocalApplicationData}/DemaConsulting/WordSense/models</c> (the application base directory when the
///     platform has no local application data folder). The only way to relocate the store is
///     <see cref="StoreRoot"/>, and no environment variable controls it: the four <c>WORDSENSE_MIRROR_*</c>
///     variables configure only the mirror. <see cref="BaseUri"/> is a required constructor argument, so a
///     <see cref="MirrorOptions"/> always describes a mirror; there is no way to set only a store root. To use files
///     that you copied yourself, pass the directory to the constructor of the model instead (see the model packages);
///     that directory is flat (the model files directly in it, no <c>{model-id}</c> subfolder), and a mirror is never
///     used with it.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Relocate the store (a mirror is mandatory: BaseUri has no default)
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"), BearerToken: "token")
///     {
///         StoreRoot = "/var/lib/wordsense",
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
