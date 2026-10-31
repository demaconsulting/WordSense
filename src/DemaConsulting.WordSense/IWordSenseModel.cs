namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model: classifies the words of a tokenized text, and owns the resources it needs.
/// </summary>
/// <remarks>
///     <para>
///     Model implementations: <c>BasicWordSenseModel</c>, which is part of this package, chooses the analysis of every
///     word with a small statistical model and needs no model files; <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> run an encoder model and download its files once. The interface
///     is the classification contract only: <see cref="ClassifyAsync"/> and <see cref="IDisposable.Dispose"/>. A model
///     that needs files also implements <see cref="IDownloadableWordSenseModel"/>, which adds an explicit, optional
///     <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> step to fetch and verify the files up front and to pass
///     <see cref="DownloadOptions"/>. Code that holds only an <see cref="IWordSenseModel"/> asks for that capability
///     with <c>model is IDownloadableWordSenseModel</c>; the call is never required, because
///     <see cref="ClassifyAsync"/> ensures lazily and may throw the same exceptions.
///     </para>
///     <para>
///     Lazy ensure: <see cref="ClassifyAsync"/> ensures the files the model needs itself on first use, when (and only
///     when) a word needs them. The Basic model never needs files. The BertBaseCased model needs its encoder files for
///     every word list that contains a known word of two or more parts of speech (its part-of-speech classifier reads
///     the sentence, so almost every sentence of running text needs it) or a word whose senses it scores. The MiniLM
///     model needs them only for a word whose senses it scores (a trained lemma with several senses): its part of
///     speech comes from the mass of those senses and from the Basic model. There is no way to turn either rule off. A
///     list that needs no encoder (for BertBaseCased: unknown words and known words of one part of speech whose senses
///     it does not score) is answered from
///     the lexicon and the Basic model alone and never touches model files or ONNX. When the model is needed
///     and was not ensured yet, the call ensures it with the defaults (no explicit download options; the
///     <c>WORDSENSE_MIRROR_*</c> environment variables apply). Concurrent callers share one ensure, and a failed ensure
///     is retried by the next call. The ensured state belongs to one model instance (a new instance verifies again).
///     </para>
///     <para>
///     A model is thread-safe for concurrent classification. <see cref="IWordSenseModel"/> is
///     <see cref="IDisposable"/>: the model owns what it needs (the ONNX session and the downloader state of an encoder
///     model). Recommendation: dispose it (for example with <c>using</c>) when you no longer need the model. Guarantee:
///     disposing while calls are running is safe; a classification that is already running completes normally, so
///     you do not have to wait for the calls to finish first. The Basic model holds nothing to release, but honors the
///     same rules. After the model is disposed, <see cref="ClassifyAsync"/> (and
///     <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> of a model that has it) throws
///     <see cref="ObjectDisposedException"/> (whatever the words and even when the token is already cancelled);
///     whether that exception is thrown from the call or reported through the returned task depends on the model
///     (Basic throws every exception from the call; an encoder model reports it through the task of
///     <c>ClassifyAsync</c>), so use <c>await</c> inside a <c>try</c> and both are caught.
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
///     parallel to the input (same length and order; empty for an empty list). Disposal: after <c>Dispose</c>,
///     <see cref="ClassifyAsync"/> throws <see cref="ObjectDisposedException"/>, and <c>Dispose</c> is idempotent and
///     thread-safe. Concurrency: <see cref="ClassifyAsync"/> is safe to call from several threads at once on one instance.
///     Confidence: a word that the lexicon does not know has <see cref="ClassifyResult.Details"/> null, the level
///     <see cref="SenseLevel.None"/> and confidence 0; a word whose lemma and part of speech have a single sense gets
///     that sense with confidence 1; a word with a single lemma and part of speech but several senses that the
///     implementation does not decide gets the <see cref="SenseLevel.PartOfSpeech"/> level, also with confidence 1 (the
///     part of speech is certain, the sense is not, so test the level); an ambiguous word gets its best answer at the level the implementation is
///     confident in, with a confidence from 0 to 1 that refers to that level and whose scale the implementation
///     documents (callers must not reuse a threshold across implementations); only a null
///     <see cref="ClassifyResult.Details"/> means unknown.
///     Disposal while calls run: <c>Dispose</c> must be safe while calls are running (it must not break or hang a
///     running classification). Lazy ensure: an implementation that needs files should ensure them lazily and only when
///     a word needs them (the supplied BertBaseCased model: a word with scored senses, a known word of two or more parts
///     of speech, or a same-spelling verb such as <c>read</c> or <c>put</c>; the supplied MiniLM model: a word with scored
///     senses), never for a list that it answers without
///     them; such an implementation should also implement
///     <see cref="IDownloadableWordSenseModel"/>. Word list: enumerate the
///     <c>words</c> argument exactly once (copy it to a list first).
///     Result content: each element of the result has <see cref="ClassifyResult.Word"/> equal to the input element at the
///     same index (as given, not lower-cased); <see cref="ClassifyResult.Details"/> comes from
///     <see cref="WordLexicon.Lookup"/> semantics: the word is lower-cased (<c>ToLowerInvariant()</c>) and then looked up
///     with <see cref="WordLexicon.Lookup"/>, which matches the spelling exactly or without regard to case and counts the
///     curly apostrophe U+2019 as <c>'</c>, so <c>Record</c> finds <c>record</c> (the shipped models do exactly that, and an implementation should too), so
///     that the same text finds the same candidates with every model (the candidate senses of a word are the entries of
///     the lexicon; an implementation must not invent others). Cancellation: honor the token (check it before the work
///     starts and between steps). Exceptions: an implementation may throw synchronously from the call or report through
///     the returned task; the shipped models differ (<c>BasicWordSenseModel</c> throws every exception of
///     <c>ClassifyAsync</c> from the call; <c>OnnxWordSenseModel</c> reports every exception of it through the
///     returned task), so callers should <c>await</c> inside a <c>try</c>. An implementation that cannot obtain,
///     verify or configure what it needs should report <see cref="ModelUnavailableException"/>,
///     <see cref="ModelIntegrityException"/> or <see cref="DownloadConfigurationException"/> (all
///     <see cref="WordSenseException"/>), as the supplied models do. The supplied implementations are <c>BasicWordSenseModel</c> (in the core
///     package <c>DemaConsulting.WordSense</c>) and <c>OnnxWordSenseModel</c> (package
///     <c>DemaConsulting.WordSense.Onnx</c>), which is used through <c>MiniLMWordSenseModel</c> and
///     <c>BertBaseCasedWordSenseModel</c>. <c>BasicWordSenseModel</c>, <c>MiniLMWordSenseModel</c> and
///     <c>BertBaseCasedWordSenseModel</c> are sealed; <c>OnnxWordSenseModel</c> is abstract but cannot be subclassed
///     outside the WordSense packages (its constructor is not accessible), so to add an engine implement this interface
///     (or <see cref="IDownloadableWordSenseModel"/>) yourself.
///     </para>
///     <para>
///     First-use costs (approximate and machine dependent; measured values are in <c>docs/user_guide/model-table.md</c>): the first access
///     to <see cref="WordLexicon"/> takes about 55 ms and about 15 MiB, and happens once per process for every
///     model; the Basic model adds a table load of about 22 ms on its first classification; an encoder model adds
///     about 0.4 s (MiniLM) or 0.5 s (BertBaseCased) to open its ONNX session on the first classification that needs
///     it, and <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> reads and hashes every model file once per
///     instance (about 30 ms for the 22 MiB MiniLM files and about 110 ms for the 104 MiB BertBaseCased files with a
///     warm file cache; more on a slow disk). Warm the model at startup (ensure and classify one short text with an
///     ambiguous word) if the first request must be fast. Concurrent classifications share one ONNX session and run in
///     parallel; the encoder models run on the CPU only. The BertBaseCased model runs the encoder once for every sentence
///     that has a word of two or more parts of speech, so call <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/> at
///     startup to control when the download happens.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     IWordSenseModel model = new BasicWordSenseModel();
///     using (model)
///     {
///         ClassifyResult[] results = await model.ClassifyAsync(["to", "record", "the", "record"]);
///         Console.WriteLine(results[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///     }
///     </code>
/// </example>
public interface IWordSenseModel : IDisposable
{
    /// <summary>
    ///     Classifies every word of a tokenized sentence or text: which analysis (lemma, part of speech, features) and,
    ///     where the model can tell, which sense or group of senses each word has.
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
    ///     list share context (the context is shared across the whole list, except that the Basic model cuts the context
    ///     at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>). To classify independent sentences, make one call per
    ///     sentence. Punctuation marks must be
    ///     elements of their own (<c>"race"</c> then <c>"."</c>, never <c>"race."</c>).
    ///     </para>
    ///     <para>
    ///     Each word is lower-cased and looked up in <see cref="WordLexicon"/>; the curly apostrophe (U+2019) is
    ///     read as <c>'</c>. A word without an entry is unknown: <see cref="ClassifyResult.Details"/> is null, the level is
    ///     <see cref="SenseLevel.None"/> and the
    ///     confidence is 0. A word whose lemma and part of speech have one sense is answered with that sense and confidence 1; a word with
    ///     a single lemma and part of speech and several senses that the model does not decide is answered at the part-of-speech level, also with confidence 1. A word with several analyses or senses is resolved
    ///     from its context at the finest level the model is confident in; how the context is used, and what the confidence means, is defined by each model package
    ///     (see <see cref="ClassifyResult"/>). There is exactly one way to get a null <see cref="ClassifyResult.Details"/>: the
    ///     word is unknown to the lexicon (every model). Every word of the lexicon gets details; a word whose
    ///     analysis has several senses that the model cannot tell apart is answered at the part-of-speech level (or the
    ///     group level), and an encoder package falls back to the Basic model (<see cref="FallbackWordSenseModel"/>) for
    ///     the words its own model does not score.
    ///     </para>
    ///     <para>
    ///     Tokenization is the caller's job, and the models expect the tokens that the evaluation used: split off
    ///     punctuation (<c>"race"</c>, <c>"."</c>), but keep a contraction (<c>"don't"</c>) and a hyphenated word
    ///     (<c>"re-read"</c>) as one token. A contraction is a stored word; a hyphenated token is found only when the whole word is in the lexicon, otherwise it is unknown (null, confidence 0),
    ///     but the surrounding words are still classified. Classify one sentence per call: the Basic model splits the
    ///     list at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>, while an encoder model reads the whole list as one
    ///     text, of at most 128 word pieces by default (512 at most); a longer list is encoded in overlapping windows of
    ///     that size and each word is read from the window in which it is most central, so very long lists lose distant
    ///     context but are not rejected. The
    ///     documentation of the <c>DemaConsulting.WordSense</c> namespace has a reference tokenizer, a table of edge
    ///     cases (numbers, abbreviations, quotes, dashes, possessives) and the rules for case: the lookup ignores case,
    ///     MiniLM is uncased and BertBaseCased is cased.
    ///     </para>
    ///     <para>
    ///     The call ensures the model lazily, when a word needs it (see the remarks of the interface): for BertBaseCased
    ///     that is a word with scored senses, a known word of two or more parts of speech or a same-spelling verb (such as
    ///     <c>read</c> or <c>put</c>), for MiniLM a word with scored
    ///     senses, and a list without such a word never touches model files or ONNX. The lazy ensure uses the defaults (no explicit
    ///     <see cref="DownloadOptions"/>; the <c>WORDSENSE_MIRROR_*</c> environment variables apply), is shared by
    ///     concurrent callers and is retried after a failure; for a model that implements
    ///     <see cref="IDownloadableWordSenseModel"/>, call <see cref="IDownloadableWordSenseModel.EnsureModelAsync"/>
    ///     first to pass <see cref="DownloadOptions"/> or to handle the failures up front (the options are ignored while
    ///     an ensure is in flight, and the ensured state is per model instance). The method
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
    ///     <para>
    ///     Failures that concern the model itself are <see cref="WordSenseException"/>s that carry the original exception
    ///     as <see cref="Exception.InnerException"/>: a model file that no source could deliver, or that is missing from a
    ///     supplied model directory, is a <see cref="ModelUnavailableException"/>; a file that fails its pinned SHA-256,
    ///     an embedded resource that is damaged (the word image, the statistics table, the weights, the part-of-speech head) or an ONNX or
    ///     vocabulary file that cannot be used is a <see cref="ModelIntegrityException"/>; invalid download settings found by the lazy
    ///     ensure (the <c>WORDSENSE_MIRROR_*</c> variables) are a <see cref="DownloadConfigurationException"/>. Mistakes in
    ///     the call keep their usual exceptions.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed during a lazy ensure.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed, whatever the words.</exception>
    /// <exception cref="ModelUnavailableException">Thrown by a model that needs files when a file could not be obtained and verified from any source, or a supplied model directory does not exist or lacks a model file.</exception>
    /// <exception cref="ModelIntegrityException">Thrown by a model when a file of a supplied model directory does not match its pinned SHA-256, or an embedded resource or the ONNX file is damaged.</exception>
    /// <exception cref="DownloadConfigurationException">Thrown by a model that needs files when the lazy ensure finds invalid <c>WORDSENSE_MIRROR_*</c> environment settings.</exception>
    Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default);
}
