using DemaConsulting.WordSense.Basic;
using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The bert-base-cased (int8) encoder as a word-sense model, composed with the Basic model as its fallback.
/// </summary>
/// <remarks>
///     <para>
///     This model is built on <c>DemaConsulting.WordSense.Onnx</c>: it wraps an encoder model (an
///     <see cref="OnnxWordSenseModel"/> with the identity of this encoder: its pinned files and its embedded weights)
///     and the Basic model (<c>DemaConsulting.WordSense.Basic</c>) in a <see cref="FallbackWordSenseModel"/>, and
///     implements <see cref="IWordSenseModel"/>. <see cref="EnsureModelAsync"/> ensures the encoder files (mirror
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
///     at the <see cref="SenseLevel.Sense"/> level. Every other ambiguous word is answered by the Basic fallback at the
///     <see cref="SenseLevel.PartOfSpeech"/> level: it chooses the analysis (the part of speech) from the context, the
///     analyses of the result carry its probabilities, and no sense is scored. The composition rules (which model answers a word,
///     and that a result always belongs completely to one of the two models) are those of
///     <see cref="FallbackWordSenseModel"/>. <see cref="ClassifyAsync"/> needs the encoder files only for a text that
///     contains a trained lemma with several senses (it ensures them on first use), and never downloads a file or fails
///     because a file is missing for any other text.
///     </para>
///     <para>
///     Choose BertBaseCased when case-awareness matters and the larger download is acceptable: 104 MiB; it
///     keeps the case of the text. The alternative <c>DemaConsulting.WordSense.MiniLM</c> is small (22 MiB); the
///     sizes are approximate and the figures of the model table are in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md.
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
///     early with a <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/>. Dispose the model when you no longer need it; disposing while
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
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"));
///     using var mirrored = new BertBaseCasedWordSenseModel();
///     await mirrored.EnsureModelAsync(mirror);
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
public sealed class BertBaseCasedWordSenseModel : IWordSenseModel
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
    {
        Encoder = new BertBaseCasedEncoderModel(modelDirectory, maxSequenceLength);
        Fallback = new BasicWordSenseModel();
        _model = new FallbackWordSenseModel(Encoder, Fallback);
    }

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
    ///     Makes sure every file of the encoder is available and verified: from the directory given to the constructor,
    ///     or from the per-user store, downloading what is missing.
    /// </summary>
    /// <param name="mirror">A mirror to try before the public source; see <see cref="OnnxWordSenseModel.EnsureModelAsync"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait of this call.</param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     The behavior (verification, mirror precedence, shared in-flight ensure, retry and cancellation) is that of
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/>; the Basic model needs no file.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown (from the call itself) when the model has been disposed.</exception>
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) =>
        _model.EnsureModelAsync(mirror, cancellationToken);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text; every ambiguous word is answered by the Basic fallback model.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>; empty for an empty list.</returns>
    /// <remarks>
    ///     A word with one analysis and one sense is answered by the encoder model at the
    ///     <see cref="SenseLevel.Sense"/> level and every other known word by the Basic model at the
    ///     <see cref="SenseLevel.PartOfSpeech"/> level, as described in the remarks of the type. The call needs no model
    ///     file. An unknown word has no analysis, the level <see cref="SenseLevel.None"/> and confidence 0.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
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
