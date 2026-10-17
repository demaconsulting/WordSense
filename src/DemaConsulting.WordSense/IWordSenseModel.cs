using System.Net;

namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model: classifies the words of a tokenized text, and owns the files and resources it needs.
/// </summary>
/// <remarks>
///     <para>
///     Model packages provide implementations: <c>DemaConsulting.WordSense.Basic</c> chooses senses with a small
///     statistical model and needs no model files; <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> run an encoder model and download its files once. The usual
///     sequence is to call <see cref="EnsureModelAsync"/> once, then <see cref="ClassifyAsync"/> as many times as
///     needed. <see cref="EnsureModelAsync"/> is the explicit way to prepare an encoder model, even when you supply a
///     model directory (it verifies the files against their pinned SHA-256 digests, with no network access), and the
///     only way to pass a <see cref="MirrorOptions"/>; it is optional for the Basic model, but calling it keeps the code
///     the same for every model.
///     </para>
///     <para>
///     Lazy ensure: <see cref="ClassifyAsync"/> ensures the model itself on first use, when (and only when) a word
///     needs it. A word list in which no word has several senses is answered from the lexicon alone and never touches
///     model files or ONNX, so a model that is never needed is never downloaded. When the model is needed and was not
///     ensured yet, the call awaits <see cref="EnsureModelAsync"/> with the defaults (no explicit mirror; the
///     <c>WORDSENSE_MIRROR_*</c> environment variables apply). Concurrent callers share one ensure, and a failed
///     ensure is retried by the next call.
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. <see cref="IWordSenseModel"/> is
///     <see cref="IDisposable"/>: the model owns what it needs (the ONNX session and the downloader state of an encoder
///     model), so dispose it, for example with <c>using</c>, once every classification has finished. The Basic model
///     holds nothing to release, but honors the same contract. After the model is disposed,
///     <see cref="EnsureModelAsync"/> and <see cref="ClassifyAsync"/> throw <see cref="ObjectDisposedException"/>.
///     Disposing is idempotent and thread-safe. Disposing while an ensure is downloading cancels the download, and the
///     pending task then throws <see cref="OperationCanceledException"/>. Disposing while a classification is running
///     is not supported.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent; measured values are in the build notes): the first access
///     to <see cref="WordLexicon.Words"/> takes about 120 ms and about 43 MiB, and happens once per process for every
///     model; the Basic model adds a table load of about 22 ms on its first classification; an encoder model adds
///     about 0.4 s (MiniLM) or 0.5 s (BertBaseCased) to open its ONNX session on the first classification that needs
///     it, and <see cref="EnsureModelAsync"/> reads and hashes every model file once, which takes longer for the larger
///     files. Warm the model at startup (ensure and classify one short text with an ambiguous word) if the first
///     request must be fast.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     IWordSenseModel model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     using (model)
///     {
///         await model.EnsureModelAsync();
///         WordSense[] senses = await model.ClassifyAsync(["to", "record", "the", "record"]);
///         Console.WriteLine(senses[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///     }
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
    ///     When an encoder model was constructed with a model directory, this call is still the way to verify it up
    ///     front: it checks that
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
    ///     a cancelled token throws <see cref="OperationCanceledException"/>, and so does a call that is still running
    ///     when the model is disposed. The Basic model throws only the cancellation exception and, after disposal,
    ///     <see cref="ObjectDisposedException"/>.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained and verified from any source (encoder model without a model directory).</exception>
    /// <exception cref="FileNotFoundException">Thrown when a supplied model directory does not exist or lacks a model file (encoder model with a model directory).</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256 (encoder model with a model directory).</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model was already disposed when the call started.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed while the call is downloading.</exception>
    Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text: which sense (lemma, part of speech, pronunciation
    ///     meaning) each word has.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>; empty for an empty list.</returns>
    /// <remarks>
    ///     <para>
    ///     The input is a list of words that the caller has already tokenized; the result array is parallel to it. The
    ///     list is read as one running text in which the words are joined by single spaces, so several sentences in one
    ///     list share context. To classify independent sentences, make one call per sentence. Punctuation marks must be
    ///     elements of their own (<c>"race"</c> then <c>"."</c>, never <c>"race."</c>).
    ///     </para>
    ///     <para>
    ///     Each word is lower-cased and looked up in <see cref="WordLexicon.Words"/>; the curly apostrophe (U+2019) is
    ///     read as <c>'</c>. A word without an entry is unknown: <see cref="WordSense.Details"/> is null and the
    ///     confidence is 0. A word with one sense is answered with confidence 1. A word with several senses is resolved
    ///     from its context; how the context is used, and what the confidence means, is defined by each model package
    ///     (see <see cref="WordSense"/>). There are exactly two ways to get a null <see cref="WordSense.Details"/>: the
    ///     word is unknown to the lexicon (every model), or an encoder model cannot score the word (it has several
    ///     senses but its lemma has no intact trained weights, or the encoder produced no usable vector for it). The
    ///     Basic model never leaves <see cref="WordSense.Details"/> null for a word that is in the lexicon. Every other
    ///     word with several senses gets its best sense, even when the confidence is low.
    ///     </para>
    ///     <para>
    ///     Tokenization is the caller's job, and the models expect the tokens that the evaluation used: split off
    ///     punctuation (<c>"race"</c>, <c>"."</c>), but keep a contraction (<c>"don't"</c>) and a hyphenated word
    ///     (<c>"re-read"</c>) as one token. Such a token has no lexicon entry, so it is unknown (null, confidence 0),
    ///     but the surrounding words are still classified. Classify one sentence per call: the Basic model splits the
    ///     list at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>, while an encoder model reads the whole list as one
    ///     text, of at most 128 word pieces by default (512 at most); a longer list is handled per word from a window
    ///     of that size around the word, so very long lists lose distant context but are not rejected. The
    ///     documentation of the <c>DemaConsulting.WordSense</c> namespace has a reference tokenizer, a table of edge
    ///     cases (numbers, abbreviations, quotes, dashes, possessives) and the rules for case: the lookup ignores case,
    ///     MiniLM is uncased and BertBaseCased is cased.
    ///     </para>
    ///     <para>
    ///     The call ensures the model lazily, when a word needs it (see the remarks of the interface): a list without
    ///     an ambiguous word never touches model files or ONNX. The lazy ensure uses the defaults (no explicit mirror;
    ///     the <c>WORDSENSE_MIRROR_*</c> environment variables apply), is shared by concurrent callers and is retried
    ///     after a failure; call <see cref="EnsureModelAsync"/> first to pass a <see cref="MirrorOptions"/>. The method
    ///     is safe for concurrent use.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed during a lazy ensure.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed, whatever the words.</exception>
    /// <exception cref="HttpRequestException">Thrown by a lazy ensure of an encoder model when a file could not be obtained and verified from any source.</exception>
    /// <exception cref="FileNotFoundException">Thrown by a lazy ensure of an encoder model when a supplied model directory does not exist or lacks a model file.</exception>
    /// <exception cref="InvalidDataException">Thrown by an encoder model when a file of a supplied model directory does not match its pinned SHA-256, or its embedded weights or ONNX file cannot be read.</exception>
    Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default);
}

/// <summary>
///     The classification of one word.
/// </summary>
/// <remarks>
///     <para>
///     The type <c>WordSense</c> (this record struct) shares its name with the namespace <c>DemaConsulting.WordSense</c>
///     that contains it; in code, write <c>WordSense[]</c> for the result type.
///     </para>
///     <para>
///     <see cref="Details"/> is null in exactly two cases. First, the word is unknown: it has no entry in the lexicon
///     (a determiner, a number, punctuation, most proper nouns, a misspelling, a contraction such as <c>don't</c>);
///     every model does this. Second, an encoder model (MiniLM, BertBaseCased) could not score a word that has several
///     senses: its lemma has no intact trained weights, or the encoder produced no vector for the word. The packages of
///     this repository ship weights for every lemma (a test asserts that no lemma is dropped), so the second case does
///     not occur in practice, but the contract allows it. In both cases <see cref="Confidence"/> is 0. Every other
///     word gets a sense: a word with one sense gets it with confidence 1, and a word with several senses gets its best
///     sense even when the confidence is low, so a null <see cref="Details"/> never means "low confidence". The Basic
///     model never leaves <see cref="Details"/> null for a word that is in the lexicon.
///     </para>
///     <para>
///     Confidence is a number from 0 to 1: 0 means unknown or unscored, 1 means the word has only one sense in the
///     lexicon, and a word resolved from its context gets a value in between that the model package defines. The
///     scales differ per model and a threshold must not be reused across models. The Basic model gives a word with
///     several senses the probability of the chosen sense (at least one over the number of senses; a threshold such
///     as 0.8 works for it, see the <c>DemaConsulting.WordSense.Basic</c> namespace documentation), so it is never
///     undecided. The encoder models (MiniLM, BertBaseCased) map
///     the margin between the best and the runner-up sense monotonically onto 0 to 1, relative to the abstain margin
///     trained for the word's lemma: 0.5 means the margin equals the abstain margin, larger values mean a clearer
///     decision, and the result is clamped to 0 to 1; the threshold used in their evaluation is 0.5, and the share of words that reaches a given value differs between the two encoders, so tune a threshold per model on your own text. It is a ranking
///     score, not a calibrated probability: compare it against a threshold you choose and treat lower values as
///     uncertain; <see cref="Details"/> still holds the best sense when the confidence is low.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     WordSense sense = new("record", new WordDetails("record", PartOfSpeech.Verb, null, null), 0.93);
///     Console.WriteLine($"{sense.Word} is a {sense.Details?.PartOfSpeech} with confidence {sense.Confidence:P0}");
///
///     // Keep only confident answers; the threshold is specific to the model that produced the confidence
///     bool confident = sense.Details is not null &amp;&amp; sense.Confidence >= 0.5;
///     </code>
/// </example>
/// <param name="Word">The word as given by the caller.</param>
/// <param name="Details">The chosen sense, or <see langword="null"/> when none was chosen.</param>
/// <param name="Confidence">The confidence in <paramref name="Details"/>, from 0 to 1.</param>
public readonly record struct WordSense(string Word, WordDetails? Details, double Confidence);

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
///     platform has no local application data folder). Concretely, as .NET reports the local application data folder:
///     on Windows <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> (for example
///     <c>C:\Users\Ann\AppData\Local\DemaConsulting\WordSense\models</c>); on Linux and macOS
///     <c>$XDG_DATA_HOME/DemaConsulting/WordSense/models</c>, which is
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> when <c>XDG_DATA_HOME</c> is not set (on macOS too, not
///     <c>~/Library/Application Support</c>). The only way to relocate the store is
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
///
///     // Pass it to EnsureModelAsync of any encoder model (MiniLMWordSenseModel or BertBaseCasedWordSenseModel)
///     async Task PrepareAsync(IWordSenseModel model)
///     {
///         await model.EnsureModelAsync(mirror);   // mirror first, then the public source; files verified
///         WordSense[] senses = await model.ClassifyAsync(["to", "record", "the", "record"]);
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
