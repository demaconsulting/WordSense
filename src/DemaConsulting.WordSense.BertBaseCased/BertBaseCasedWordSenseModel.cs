using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The bert-base-cased (int8) encoder as a word-sense model, composed with the Basic model as its fallback.
/// </summary>
/// <remarks>
///     <para>
///     This model is built on <c>DemaConsulting.WordSense.Onnx</c>: it wraps an encoder model (an
///     <see cref="OnnxWordSenseModel"/> with the identity of this encoder: its pinned files and its embedded weights)
///     and the Basic model (<see cref="BasicWordSenseModel"/> of the core package) in a <see cref="FallbackWordSenseModel"/>, and
///     implements <see cref="IDownloadableWordSenseModel"/> (and so <see cref="IWordSenseModel"/>). <see cref="EnsureModelAsync"/> ensures the encoder files (download-options
///     precedence, verification, retry and cancellation behavior are those described for
///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/>) and <see cref="Dispose"/> disposes both models; dispose the
///     model with <c>using</c> (disposing while a classification runs is safe: the running calls complete and the
///     resources are released after the last one).
///     </para>
///     <para>
///     Classification. The encoder model scores the fine senses of the lemmas that this version has weights for
///     (the lemmas of the relabeled corpus, listed in the senses appendix of the user guide) and answers each of them at the finest level its
///     per-lemma thresholds support: the <see cref="SenseLevel.Sense"/> level with the sense id, the
///     <see cref="SenseLevel.Group"/> level with the group id, or the <see cref="SenseLevel.PartOfSpeech"/> level; the
///     ranked senses of every analysis are listed whatever the level. A word with one analysis and one sense is answered
///     at the <see cref="SenseLevel.Sense"/> level. The part of speech of every word that has two or more parts of speech
///     comes from the sentence: the embedded part-of-speech head reads the encoder vectors of the word and its
///     neighbors, and the composite combines its probabilities with those of the Basic fallback (weight
///     <see cref="PartOfSpeechHeadWeight"/>, see <see cref="FallbackWordSenseModel"/>); the answer keeps its level when
///     the most probable part of speech is the one the encoder answered, and otherwise steps down to
///     <see cref="SenseLevel.PartOfSpeech"/>. Every other ambiguous word (no trained lemma) is answered by the Basic
///     fallback with that part of speech: no sense is decided (the senses of the analyses are ranked by how often they
///     are used, which is a statement about the lemma and never an answer), and the level is
///     <see cref="SenseLevel.PartOfSpeech"/>, or <see cref="SenseLevel.Sense"/> when its selected analysis has exactly
///     one sense. The composition rules (which model answers a word, and that a result always belongs completely to one
///     of the two models) are those of <see cref="FallbackWordSenseModel"/>. <see cref="ClassifyAsync"/> needs the
///     encoder files for a text that contains a known word of two or more parts of speech, a word whose lemma has
///     scored senses (a trained lemma with several senses, or a form that has a chooser) or a same-spelling verb (a verb
///     with a past and a non-past analysis, whose tense the tense head reads from the sentence; <c>read</c>, <c>put</c>,
///     <c>found</c>). It ensures them on first use (there is no opt-out), and never downloads a file or fails because a
///     file is missing for any other text (unknown words, and known words of one part of speech whose senses are not
///     scored and that are not such a verb).
///     </para>
///     <para>
///     Choose BertBaseCased when case-awareness matters and the larger download is acceptable: 104 MiB; it
///     keeps the case of the text. The alternative <c>DemaConsulting.WordSense.MiniLM</c> is small (22 MiB); the
///     sizes are approximate and the figures of the model table are in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/model-table.md.
///     </para>
///     <para>
///     Two kinds of data are involved: the per-lemma classifier weights trained for this encoder are embedded in the
///     assembly (nothing to download), while the encoder files (<c>model_int8.onnx</c>,
///     103.5 MiB or 108,477,896 bytes; <c>vocab.txt</c>, 0.2 MiB or 213,450 bytes; <c>config.json</c>, 639 bytes; and
///     <c>tokenizer.json</c>, 0.6 MiB or 668,923 bytes; 104 MiB in total) are downloaded once by
///     <see cref="EnsureModelAsync"/>,
///     verified against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use
///     files you supply, in which case no network access happens at all. The first classification that needs the
///     encoder opens the ONNX session and checks the embedded weights against the word image. The files are described in the repository at
///     https://github.com/demaconsulting/WordSense/tree/main/model/bert-base-cased-int8. The identifier of this model
///     (<see cref="ModelId"/>) is <c>bert-base-cased-int8</c>; it names the store folder and the
///     folder on a mirror.
///     </para>
///     <para>
///     A supplied model directory must hold all four files directly (no <c>{model-id}</c> subfolder).
///     <c>EnsureModelAsync</c> is optional but recommended: it verifies every one of them, not only the ONNX file,
///     against the pinned SHA-256 digests listed in the namespace documentation, with no network access, and fails
///     early with a <see cref="ModelUnavailableException"/> (a missing file or directory) or a
///     <see cref="ModelIntegrityException"/> (a wrong digest); both are <see cref="WordSenseException"/>s that carry the
///     original exception as inner exception. Dispose the model when you no longer need it; disposing while
///     calls are running is safe.
///     </para>
///     <para>
///     The encoder keeps the case of the text, so capitalization is part of the context the model sees. Sentences longer
///     than the maximum sequence length are handled per target word from a window around the word. The weights were
///     trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
/// </remarks>
/// <example>
///     Download on first use (or reuse the per-user store) and classify a sentence.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     using var model = new BertBaseCasedWordSenseModel();
///     await model.EnsureModelAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     ClassifyResult[] results = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {results[i].Details?.PartOfSpeech.ToString() ?? "-"} {results[i].Level} ({results[i].Confidence:0.00})");
///     }
///     </code>
///     Download through a mirror (files at <c>{mirror}/bert-base-cased-int8/{file}</c>) instead of the public source.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     var options = new DownloadOptions(new Uri("https://models.example.com/wordsense"));
///     using var mirrored = new BertBaseCasedWordSenseModel();
///     await mirrored.EnsureModelAsync(options);
///     var mirroredSenses = await mirrored.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
///     Offline: the four files are in one flat directory, for example <c>/opt/models/bert</c>.
///     <code>
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     using var offline = new BertBaseCasedWordSenseModel("/opt/models/bert");
///     await offline.EnsureModelAsync();   // optional: verifies SHA-256 now, no network
///     var offlineSenses = await offline.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public sealed class BertBaseCasedWordSenseModel : IDownloadableWordSenseModel
{
    private readonly FallbackWordSenseModel _model;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BertBaseCasedWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">
    ///     A directory that holds <c>model_int8.onnx</c>, <c>vocab.txt</c>, <c>config.json</c> and <c>tokenizer.json</c>, or null to use the per-user store that
    ///     <see cref="EnsureModelAsync"/> fills.
    /// </param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <remarks>
    ///     The constructor does not touch the file system: a directory that does not exist, a missing file or a file
    ///     with a wrong SHA-256 is reported by <see cref="EnsureModelAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public BertBaseCasedWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : this(modelDirectory, maxSequenceLength, PartOfSpeechHeadWeight)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="BertBaseCasedWordSenseModel"/> class with an explicit primary weight
    ///     (the builder tunes it; tests pin it).
    /// </summary>
    /// <param name="modelDirectory">A directory that holds <c>model_int8.onnx</c> and <c>vocab.txt</c>, or null to use the per-user store.</param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <param name="primaryWeight">The weight of the encoder's part-of-speech head in the combination of the slot probabilities, from 0 to 1; see <see cref="PartOfSpeechHeadWeight"/>.</param>
    /// <param name="tenseWeight">The weight of the encoder's tense head in the combination of its probability of past with the tense split of Basic, from 0 to 1; see <see cref="TenseHeadWeight"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512 or a weight is outside 0 to 1.</exception>
    internal BertBaseCasedWordSenseModel(string? modelDirectory, int maxSequenceLength, double primaryWeight, double tenseWeight = TenseHeadWeight)
    {
        Encoder = new BertBaseCasedEncoderModel(modelDirectory, maxSequenceLength);
        Fallback = new BasicWordSenseModel();
        _model = new FallbackWordSenseModel(Encoder, Fallback, primaryWeight, tenseWeight);
    }

    /// <summary>
    ///     The weight of the encoder's part-of-speech head when the slot probabilities of a word of several parts of speech
    ///     are combined with those of the Basic model (see <see cref="FallbackWordSenseModel"/>): 1 takes the head alone,
    ///     0 takes the part of speech of Basic. Chosen on the development split of the open corpus.
    /// </summary>
    internal const double PartOfSpeechHeadWeight = 0.8;

    /// <summary>
    ///     The weight of the encoder's tense head when its probability that a same-spelling verb is past is combined with the
    ///     past and non-past split of the Basic model (see <see cref="FallbackWordSenseModel"/>): 1 takes the head alone,
    ///     0 takes the split of Basic. Chosen on the development split of the open corpus.
    /// </summary>
    internal const double TenseHeadWeight = 0.9;

    /// <summary>
    ///     Gets the identifier of the model, which names its folder in the repository, on a mirror and in the store.
    /// </summary>
    public string ModelId => Encoder.ModelId;

    /// <summary>
    ///     Gets the hidden size (feature dimension) of the encoder; known before <see cref="EnsureModelAsync"/>.
    /// </summary>
    public int HiddenSize => Encoder.HiddenSize;

    /// <summary>
    ///     Gets the encoder model that this model wraps; it is the primary model of the composition.
    /// </summary>
    internal OnnxWordSenseModel Encoder { get; }

    /// <summary>
    ///     Gets the Basic model that answers the words the encoder model does not score; it is the fallback of the composition.
    /// </summary>
    internal IWordSenseModel Fallback { get; }

    /// <summary>
    ///     Gets the weight of the encoder in the combination of its slot probabilities with those of the Basic model for a
    ///     word that the encoder answers at the part-of-speech level.
    /// </summary>
    internal double PrimaryWeight => _model.PrimaryWeight;

    /// <summary>
    ///     Makes sure every file of the encoder is available and verified: from the directory given to the constructor,
    ///     or from the per-user store, downloading what is missing.
    /// </summary>
    /// <param name="options">The download options (a mirror to try before the public source, the store location, the timeout); see <see cref="OnnxWordSenseModel.EnsureModelAsync"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait of this call.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     The behavior (verification, options precedence, shared in-flight ensure, retry and cancellation) is that of
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/>; the Basic model needs no file and is skipped. The call is
    ///     optional: <see cref="ClassifyAsync"/> ensures lazily.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown (from the call itself) when the model has been disposed.</exception>
    /// <exception cref="ModelUnavailableException">Reported through the returned task when a file is missing from the supplied directory or no source delivered it.</exception>
    /// <exception cref="ModelIntegrityException">Reported through the returned task when a file of the supplied directory fails its pinned SHA-256.</exception>
    /// <exception cref="DownloadConfigurationException">Reported through the returned task when the download options or environment settings are invalid.</exception>
    public Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default) =>
        _model.EnsureModelAsync(options, cancellationToken);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text; the encoder scores the senses of the lemmas it was
    ///     trained for, and the Basic fallback model answers every other word.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>; empty for an empty list.</returns>
    /// <remarks>
    ///     <para>
    ///     A word with one analysis and one sense is answered by the encoder model at the <see cref="SenseLevel.Sense"/>
    ///     level without running the encoder. A word whose lemma has trained weights and several senses is answered by the
    ///     encoder at the sense, group or part-of-speech level its thresholds allow, with the ranked senses of its
    ///     analyses. The first call with a word of two or more parts of speech, trained or not, ensures the model files
    ///     itself (it may download them, see <see cref="EnsureModelAsync"/>) and reads the whole sentence: the
    ///     part-of-speech head and the Basic model give the probabilities of its analyses. A word whose lemma has no
    ///     trained weights is answered at the <see cref="SenseLevel.PartOfSpeech"/> level with the senses of its
    ///     analyses ranked by how often they are used (the usage prior: a ranking of the lemma, never an answer), or
    ///     at the <see cref="SenseLevel.Sense"/> level when its selected analysis has exactly one sense, which the Basic
    ///     model names without scoring. The first call with a same-spelling verb (a verb with a past and a non-past
    ///     analysis: <c>read</c>, <c>put</c>, <c>found</c>) also ensures the model files and reads the whole sentence: the
    ///     tense head and the Basic model give the share of its past analyses. A text of unknown words and of known words
    ///     of one part of speech whose senses are not scored, and that has no such verb, needs no model file. An
    ///     unknown word has no analysis, the level <see cref="SenseLevel.None"/> and confidence 0.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    /// <exception cref="ModelUnavailableException">Reported through the returned task by the lazy ensure when the model files could not be obtained.</exception>
    /// <exception cref="ModelIntegrityException">Reported through the returned task when a model file or an embedded resource is damaged.</exception>
    /// <exception cref="DownloadConfigurationException">Reported through the returned task by the lazy ensure when the <c>WORDSENSE_MIRROR_*</c> settings are invalid.</exception>
    public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
        _model.ClassifyAsync(words, cancellationToken);

    /// <summary>
    ///     Disposes the encoder model and the Basic model and cancels a download that <see cref="EnsureModelAsync"/> has in flight.
    /// </summary>
    /// <remarks>
    ///     Calling it more than once, or from several threads at once, is harmless. Disposing while a classification is
    ///     running is safe; new calls throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public void Dispose() => _model.Dispose();
}
