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
///     needed. <see cref="EnsureModelAsync"/> is optional for every model: call it to fail early (for an encoder model
///     it verifies the files against their pinned SHA-256 digests, with no network access when you supply a model
///     directory) and to pass a <see cref="MirrorOptions"/>; without it, <see cref="ClassifyAsync"/> ensures lazily
///     and may throw the same exceptions. Calling it also keeps the code the same for every model.
///     </para>
///     <para>
///     Lazy ensure: <see cref="ClassifyAsync"/> ensures the model itself on first use, when (and only when) a word
///     needs it. A word list in which no word has several senses is answered from the lexicon alone and never touches
///     model files or ONNX, so a model that is never needed is never downloaded. When the model is needed and was not
///     ensured yet, the call awaits <see cref="EnsureModelAsync"/> with the defaults (no explicit mirror; the
///     <c>WORDSENSE_MIRROR_*</c> environment variables apply). Concurrent callers share one ensure, and a failed
///     ensure is retried by the next call. A <see cref="MirrorOptions"/> passed while an ensure is already in flight
///     is ignored. The ensured state belongs to one model instance (a new instance verifies again): once a call
///     succeeded, the instance keeps the directory it verified, including the
///     <see cref="MirrorOptions.StoreRoot"/> of that call, and later lazy ensures reuse it; after a failed call, the
///     next ensure starts afresh with only the options that call passes (a lazy one passes none).
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. <see cref="IWordSenseModel"/> is
///     <see cref="IDisposable"/>: the model owns what it needs (the ONNX session and the downloader state of an encoder
///     model). Recommendation: dispose it (for example with <c>using</c>) when you no longer need the model. Guarantee:
///     disposing while calls are running is safe; a classification that is already running completes normally, so
///     you do not have to wait for the calls to finish first. The Basic model holds nothing to release, but honors the
///     same rules. After the model is disposed, <see cref="EnsureModelAsync"/> and <see cref="ClassifyAsync"/> throw
///     <see cref="ObjectDisposedException"/> (whatever the words and even when the token is already cancelled);
///     whether that exception is thrown from the call or reported through the returned task depends on the model
///     (Basic throws every exception from the call; an encoder model throws it from <c>EnsureModelAsync</c> and reports
///     it through the task of <c>ClassifyAsync</c>), so use <c>await</c> inside a <c>try</c> and both are caught.
///     Disposing is idempotent and thread-safe, and <c>Dispose</c> itself never waits for running calls. The model is
///     marked disposed at once (new calls throw), a classification that is already running completes normally, and
///     the resources (the ONNX session of an encoder model) are released when the last running classification
///     finishes. Disposing during an ensure (encoder models): a download in progress is cancelled and the pending task
///     throws <see cref="OperationCanceledException"/>, but the hashing of files that are already in place is not
///     interrupted: when every file is present and valid, the pending ensure completes normally (the model is
///     disposed nevertheless, so every later call throws). A classification that is still waiting for its lazy ensure
///     when the model is disposed throws <see cref="OperationCanceledException"/> (the download was cancelled) or, when
///     the ensure finished first, <see cref="ObjectDisposedException"/> (it had not yet opened the ONNX session); only
///     a classification that already runs on the session completes normally, and one that never needs the model
///     (lexicon only) is not affected at all. Downloaded files are never deleted by disposal and are reused by the next
///     instance.
///     </para>
///     <para>
///     Implementing the interface. Third parties may implement <see cref="IWordSenseModel"/> themselves (for example
///     to wrap another disambiguation engine); the models of this repository are only the supplied implementations.
///     An implementation must honor the following, as the supplied models do. Argument validation:
///     <see cref="ClassifyAsync"/> throws <see cref="ArgumentNullException"/> for a null word list and
///     <see cref="ArgumentException"/> for a null element. Result shape: the returned array is never null and is
///     parallel to the input (same length and order; empty for an empty list). Disposal: after <c>Dispose</c>, both
///     methods throw <see cref="ObjectDisposedException"/>, and <c>Dispose</c> is idempotent and thread-safe.
///     Concurrency: <see cref="ClassifyAsync"/> is safe to call from several threads at once on one instance.
///     Confidence: a word that the lexicon does not know has <see cref="WordSense.Details"/> null and confidence 0; a
///     word with a single sense gets that sense with confidence 1; an ambiguous word gets its best sense together with
///     a confidence from 0 to 1 whose scale the implementation documents (callers must not reuse a threshold across
///     implementations). An implementation that needs no files completes <see cref="EnsureModelAsync"/> at once.
///     Result content: each element of the result has <see cref="WordSense.Word"/> equal to the input element at the
///     same index (as given, not lower-cased); <see cref="WordSense.Details"/> comes from
///     <c>WordLexicon.Words[key]</c> where the key is the word lower-cased (<c>ToLowerInvariant()</c>) with the curly
///     apostrophe U+2019 replaced by <c>'</c>; the shipped models do exactly that, and an implementation should too, so
///     that the same text finds the same candidates with every model (the candidate senses of a word are the entries of
///     the lexicon; an implementation must not invent others). Cancellation: honor the token (check it before the work
///     starts and between steps). <see cref="EnsureModelAsync"/>: idempotent (calling it again after it succeeded is
///     cheap and harmless), thread-safe (concurrent calls share one ensure) and retryable (a failed call can be
///     repeated). Exceptions: an implementation may throw synchronously from the call or report through the returned
///     task; the shipped models differ (<c>BasicWordSenseModel</c> throws every exception of both methods from the
///     call, except that <c>EnsureModelAsync</c> returns a cancelled task for an already cancelled token;
///     <c>OnnxWordSenseModel</c> throws <see cref="ObjectDisposedException"/> from <c>EnsureModelAsync</c> and reports
///     every other exception of it, and every exception of <c>ClassifyAsync</c>, through the returned task), so callers
///     should <c>await</c> inside a <c>try</c>. The supplied implementations are <c>BasicWordSenseModel</c> (package
///     <c>DemaConsulting.WordSense.Basic</c>) and <c>OnnxWordSenseModel</c> (package
///     <c>DemaConsulting.WordSense.Onnx</c>), which is used through <c>MiniLMWordSenseModel</c> and
///     <c>BertBaseCasedWordSenseModel</c>. <c>BasicWordSenseModel</c>, <c>MiniLMWordSenseModel</c> and
///     <c>BertBaseCasedWordSenseModel</c> are sealed; <c>OnnxWordSenseModel</c> is abstract but cannot be subclassed
///     outside the WordSense packages (its constructor is not accessible), so to add an engine implement this interface
///     yourself.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent; measured values are in the build notes): the first access
///     to <see cref="WordLexicon.Words"/> takes about 120 ms and about 43 MiB, and happens once per process for every
///     model; the Basic model adds a table load of about 22 ms on its first classification; an encoder model adds
///     about 0.4 s (MiniLM) or 0.5 s (BertBaseCased) to open its ONNX session on the first classification that needs
///     it, and <see cref="EnsureModelAsync"/> reads and hashes every model file once per instance (about 30 ms for the
///     22 MiB MiniLM files and about 110 ms for the 104 MiB BertBaseCased files with a warm file cache; more on a slow
///     disk). Warm the model at startup (ensure and classify one short text with an ambiguous word) if the first
///     request must be fast. Concurrent classifications share one ONNX session and run in parallel; the encoder
///     models run on the CPU only.
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
    /// <param name="cancellationToken">
    ///     A token that cancels the wait of this call: the returned task is cancelled (awaiting it throws
    ///     <see cref="OperationCanceledException"/>); the exception is not thrown from the call itself, also not for a
    ///     token that is already cancelled. The work that is shared with other callers (a download in progress) is not
    ///     cancelled by a token: it continues, also when no caller is waiting any more; only disposing the model stops it.
    /// </param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     <para>
    ///     Files that are already present and valid are never downloaded again. Concurrent calls share one ensure; a
    ///     <see cref="MirrorOptions"/> passed while another call's ensure is still running is ignored. The rule for
    ///     cancellation, the same for every caller: when the only waiting caller cancels, the shared ensure keeps
    ///     running (it is not cancelled), and the next call joins it if it is still running (its
    ///     <see cref="MirrorOptions"/> are ignored then), returns at once if it has succeeded in the meantime, and
    ///     starts a new ensure with its own <see cref="MirrorOptions"/> only if the earlier one failed. A failed call can be retried. The Basic model needs no files and completes at once.
    ///     </para>
    ///     <para>
    ///     When an encoder model was constructed with a model directory, this call is optional but is the way to verify
    ///     it up front (otherwise <see cref="ClassifyAsync"/> verifies lazily and may throw the same exceptions): it checks that
    ///     every file exists in that directory (directly in it, not in a subfolder) and matches its pinned SHA-256,
    ///     and it never touches the network. Without a directory it verifies the per-user store, or the store
    ///     beneath <see cref="MirrorOptions.StoreRoot"/>, and downloads what is missing or invalid.
    ///     </para>
    ///     <para>
    ///     Failures: a missing or wrong file in a supplied directory throws <see cref="FileNotFoundException"/> or
    ///     <see cref="InvalidDataException"/>; a file that no source could deliver verified throws
    ///     <see cref="HttpRequestException"/>; an invalid mirror configuration throws <see cref="ArgumentException"/>
    ///     (an invalid timeout, that is a <see cref="MirrorOptions.Timeout"/> that is zero, negative or longer than about
    ///     49 days, throws its subclass <see cref="ArgumentOutOfRangeException"/>; infinite is allowed); an unwritable store
    ///     reports the file system exception of the platform (for example <see cref="IOException"/> or
    ///     <see cref="UnauthorizedAccessException"/>); a disposed model throws <see cref="ObjectDisposedException"/>;
    ///     a cancelled token cancels the returned task (<see cref="OperationCanceledException"/> when awaited), and so
    ///     does a download that is still running when the model is disposed. The Basic model throws only
    ///     <see cref="ObjectDisposedException"/> (from the call) and returns a cancelled task for an already cancelled
    ///     token; an encoder model throws <see cref="ObjectDisposedException"/> from the call and reports everything
    ///     else through the returned task, so the exceptions are caught by an <c>await</c> in a <c>try</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the mirror configuration is invalid.</exception>
    /// <exception cref="HttpRequestException">Thrown when a file could not be obtained and verified from any source (encoder model without a model directory).</exception>
    /// <exception cref="FileNotFoundException">Thrown when a supplied model directory does not exist or lacks a model file (encoder model with a model directory).</exception>
    /// <exception cref="InvalidDataException">Thrown when a file of a supplied model directory does not match its pinned SHA-256 (encoder model with a model directory).</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model was already disposed when the call started.</exception>
    /// <exception cref="OperationCanceledException">Reported through the returned task (not thrown from the call) when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed while the call is downloading.</exception>
    Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text: which sense (lemma, part of speech, pronunciation
    ///     meaning) each word has.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">
    ///     A token that cancels the operation. It is checked before the work starts and between steps (see the remarks);
    ///     a running ONNX inference is not interrupted.
    /// </param>
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
    ///     after a failure; call <see cref="EnsureModelAsync"/> first to pass a <see cref="MirrorOptions"/> (it is
    ///     ignored while an ensure is in flight, and the ensured state is per model instance). The method
    ///     is safe for concurrent use. Disposal precedence: a disposed model throws <see cref="ObjectDisposedException"/>
    ///     even when the token is already cancelled (the Basic model throws it, and every other exception, from the
    ///     call itself; an encoder model reports exceptions through the returned task).
    ///     </para>
    ///     <para>
    ///     Threads and cancellation. The Basic model does everything synchronously on the calling thread (the lexicon
    ///     load, the table load and the classification) and returns a task that is already complete, so the call
    ///     blocks for that time; it checks the token before the work starts and between sentence segments. An encoder
    ///     model starts synchronously on the calling thread too (argument checks, the lexicon load and the decision
    ///     whether a word needs the model); the hashing and download of a lazy ensure run on the thread pool; the
    ///     opening of the ONNX session and the inference then run synchronously, on whichever thread continues the
    ///     call (the calling thread when the model is already ensured, otherwise a thread-pool thread), and are not
    ///     offloaded with <c>Task.Run</c>, so use <c>Task.Run</c> yourself if the caller must not block. For an
    ///     encoder model the token is checked before the work starts, while the lazy ensure is awaited (it cancels the
    ///     wait of this call only), and between the words that are encoded; a single ONNX run is never interrupted.
    ///     A word list is classified as given: an element that is an empty string is unknown (null, confidence 0),
    ///     and an element that contains a space is not split, so it is looked up as one key and is unknown unless the
    ///     lexicon has that exact key (it has none with a space); always pass one token per element.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null, or (encoder models) by the lazy ensure when the settings of the <c>WORDSENSE_MIRROR_*</c> environment variables are invalid.</exception>
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
///     that contains it; in code, write <c>WordSense[]</c> for the result type. In code that itself lives in a
///     namespace under <c>DemaConsulting.*</c> (for example <c>namespace DemaConsulting.MyApp</c>), the simple name
///     <c>WordSense</c> resolves to the namespace, not to the type, and the compiler reports an error (a namespace is
///     used like a type): add an alias inside your namespace declaration,
///     <c>using WordSense = DemaConsulting.WordSense.WordSense;</c>, or write the full name
///     <c>DemaConsulting.WordSense.WordSense</c> (see the second example below). Code outside such a namespace, with
///     <c>using DemaConsulting.WordSense;</c>, needs neither.
///     </para>
///     <para>
///     The test for "no sense chosen" is <c>Details is null</c>, not <c>Confidence == 0</c>: confidence 0 and a null
///     <see cref="Details"/> go together for every model of this repository, but a non-null <see cref="Details"/> is the
///     contract, whereas the confidence is only a score.
///     </para>
///     <para>
///     <see cref="Details"/> is null in exactly two cases. First, the word is unknown: it has no entry in the lexicon
///     (a determiner, a number, punctuation, most proper nouns, a misspelling, a contraction such as <c>don't</c>);
///     every model does this. Second, an encoder model (MiniLM, BertBaseCased) could not score a word that has several
///     senses: its lemma has no intact trained weights, or the encoder produced no vector for the word. The packages of
///     this repository ship weights for every lemma (a test asserts that no lemma is dropped), so the second case does
///     not occur in practice, but the API allows it. In both cases <see cref="Confidence"/> is 0. Every other
///     word gets a sense: a word with one sense gets it with confidence 1, and a word with several senses gets its best
///     sense even when the confidence is low, so a null <see cref="Details"/> never means "low confidence". The Basic
///     model never leaves <see cref="Details"/> null for a word that is in the lexicon.
///     </para>
///     <para>
///     Confidence is a number from 0 to 1: 0 means unknown or unscored, 1 means the word has only one sense in the
///     lexicon, and a word resolved from its context gets a value in between that the model package defines. The
///     scales differ per model and a threshold must not be reused across models. The Basic model gives a word with
///     several senses the probability of the chosen sense (never below 1/n for a word with n senses, so 0.5 for a
///     word with two senses: a threshold at or below 1/n keeps every answer of an n-sense word), so it is never
///     undecided; its recommended starting threshold is 0.8 (see the <c>DemaConsulting.WordSense.Basic</c> namespace
///     documentation). The encoder models (MiniLM, BertBaseCased) map
///     the margin between the best and the runner-up sense monotonically onto 0 to 1, relative to the abstain margin
///     trained for the word's lemma: 0.5 means the margin equals the abstain margin (the evaluation counts a word as
///     answered from 0.5 up and as abstained below it, so 0.5 is the lowest meaningful value, not a useful filter),
///     larger values mean a clearer
///     decision, and the result is clamped to 0 to 1. The recommended starting threshold for each encoder is 0.8
///     (about three quarters of the real-text target words kept, with about one error in fifty for MiniLM and one in
///     a hundred for BertBaseCased); the share of words that reaches a given value differs between the two encoders,
///     so tune the threshold per model on your own text. It is a ranking
///     score, not a calibrated probability: compare it against a threshold you choose and treat lower values as
///     uncertain; <see cref="Details"/> still holds the best sense when the confidence is low.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     WordSense sense = new("record", new WordDetails("record", PartOfSpeech.Verb, null, null), 0.93);
///     Console.WriteLine($"{sense.Word} is a {sense.Details?.PartOfSpeech} with confidence {sense.Confidence:P0}");
///
///     // Keep only confident answers. A threshold belongs to one model: 0.8 is the recommended start for each of
///     // Basic, MiniLM and BertBaseCased, but it is read in that model's own scale; re-tune it per model
///     const double threshold = 0.8;
///     bool confident = sense.Details is not null &amp;&amp; sense.Confidence >= threshold;
///     </code>
///     Code in a namespace under <c>DemaConsulting.*</c>: alias the type, because the simple name <c>WordSense</c>
///     means the namespace there.
///     <code>
///     namespace DemaConsulting.MyApp
///     {
///         using WordSense = DemaConsulting.WordSense.WordSense;
///
///         internal static class Disambiguator
///         {
///             internal static async Task&lt;WordSense[]&gt; ClassifyAsync(IWordSenseModel model, string[] words)
///             {
///                 WordSense[] senses = await model.ClassifyAsync(words);
///                 return senses;
///             }
///         }
///     }
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
///     absolute, use <c>http</c> or <c>https</c>, and carry no query, fragment or user-info; a relative URI, another
///     scheme, a query, a fragment or user-info is an error, reported by <c>EnsureModelAsync</c> (not by the
///     constructor of this record) as an <see cref="ArgumentException"/>, also when all files are already present. A
///     value of <c>WORDSENSE_MIRROR_URL</c> that is not an absolute <c>http</c> or <c>https</c> URL, or that has a query,
///     a fragment or user-info, is an <see cref="ArgumentException"/> as well.
///     </para>
///     <para>
///     Precedence: a <see cref="MirrorOptions"/> passed to <see cref="IWordSenseModel.EnsureModelAsync"/> always wins;
///     the environment is not consulted then. Only when no mirror is passed do the variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> configure one (the URL variable is required, the others are optional, and
///     user and password go together). Without either, only the public source is used. Models that need no files
///     ignore the mirror. Details of the variables: a blank value counts as unset; <c>WORDSENSE_MIRROR_USER</c>
///     without <c>WORDSENSE_MIRROR_PASSWORD</c> (or the reverse) is an error; any of user, password or token without
///     <c>WORDSENSE_MIRROR_URL</c> is an error too (it is almost always a misspelled URL variable), and so is a token
///     together with a user. The environment is read only when the mirror is needed, that is by an
///     <c>EnsureModelAsync</c> without a <see cref="MirrorOptions"/> or by the lazy ensure of an ambiguous word, and
///     never when the model was constructed with a model directory (the mirror, valid or not, is then ignored and
///     nothing is thrown). The invalid configurations (for example a token together with a user, or credentials for a
///     plain <c>http</c> host) are reported by <see cref="IWordSenseModel.EnsureModelAsync"/> (or by the lazy ensure
///     of <see cref="IWordSenseModel.ClassifyAsync"/>) as an
///     <see cref="ArgumentException"/>. A <see cref="Timeout"/> that is zero, negative or longer than about 49 days
///     throws <see cref="ArgumentOutOfRangeException"/> (a subclass of <see cref="ArgumentException"/>; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit); the
///     timeout applies only to a <see cref="MirrorOptions"/> that you pass, because the environment variables have
///     no timeout (the default of 5 minutes applies then).
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
///     <para>
///     Store root rules: <see cref="StoreRoot"/> may be relative; it is resolved with <c>Path.GetFullPath</c> against the
///     current directory of the process at the time the ensure runs, so prefer an absolute path (the directory is
///     created when it does not exist). Offline route through the store: files that you copy into
///     <c>{StoreRoot}/{model-id}/</c> (or into the default store, then no <see cref="MirrorOptions"/> is needed) are
///     hashed by <c>EnsureModelAsync</c>; a file that matches its pinned SHA-256 is kept and never requested, so when
///     every file is present and valid nothing is downloaded and the mirror (and the public source) is not contacted,
///     and a file that is missing or does not match is downloaded again (and replaced). Because a
///     <see cref="MirrorOptions"/> needs a <see cref="BaseUri"/>, use any valid URI there when you only want a
///     custom <see cref="StoreRoot"/> offline; on a machine without network access a missing or damaged file then
///     ends in <see cref="System.Net.Http.HttpRequestException"/>, whereas a model directory passed to the constructor never
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
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"), BearerToken: "token")
///     {
///         StoreRoot = OperatingSystem.IsWindows() ? @"C:\ProgramData\WordSense\models" : "/var/lib/wordsense",
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
