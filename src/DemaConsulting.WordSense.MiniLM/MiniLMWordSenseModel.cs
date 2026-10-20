using DemaConsulting.WordSense.Basic;
using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The all-MiniLM-L6-v2 (int8) encoder as a word-sense model, composed with the Basic model as its fallback.
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
///     Classification. The scoring of fine senses with the encoder is not part of this version, so the encoder model
///     answers only the words that need no choice: a word with one analysis and one sense is answered at the
///     <see cref="SenseLevel.Sense"/> level. Every ambiguous word is answered by the Basic fallback at the
///     <see cref="SenseLevel.PartOfSpeech"/> level: it chooses the analysis (the part of speech) from the context, the
///     analyses of the result carry its probabilities, and no sense is scored. The composition rules (which model answers a word,
///     and that a result always belongs completely to one of the two models) are those of
///     <see cref="FallbackWordSenseModel"/>. <see cref="ClassifyAsync"/> needs no model file, so it never downloads a
///     file and never fails because a file is missing.
///     </para>
///     <para>
///     Two kinds of data are involved: the per-lemma classifier weights trained for this encoder are embedded in the
///     assembly (nothing to download), while the encoder files (<c>model_int8.onnx</c>,
///     21.9 MiB or 22,972,370 bytes, and <c>vocab.txt</c>, 0.2 MiB or 231,508 bytes; 22 MiB in total) are downloaded
///     once by <see cref="EnsureModelAsync"/>, verified
///     against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use files you
///     supply, in which case no network access happens at all. In this version the classification itself does not read
///     the encoder files or the embedded weights. The files are described in the repository at
///     https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8. The identifier of this model
///     (<see cref="ModelId"/>) is <c>all-MiniLM-L6-v2-int8</c>; it names the store folder and the
///     folder on a mirror.
///     </para>
///     <para>
///     Choose MiniLM when download size and speed matter, the default recommendation: 22 MiB. The alternative
///     <c>DemaConsulting.WordSense.BertBaseCased</c> is larger (104 MiB) and case-aware. The sizes are approximate
///     and the figures of the model table are in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md.
///     </para>
///     <para>
///     Sentences longer than the maximum sequence length are handled per target word from a window around the word. The
///     weights were trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
///     <para>
///     When you supply a model directory, it must hold <c>model_int8.onnx</c> and <c>vocab.txt</c> directly (no
///     <c>{model-id}</c> subfolder). <c>EnsureModelAsync</c> is optional but recommended: it verifies both files against
///     their pinned SHA-256 digests without any network access and fails early with a
///     <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/>. Dispose the model when you no longer need it; disposing while calls are running is safe.
///     </para>
/// </remarks>
/// <example>
///     Download on first use (or reuse the per-user store) and classify a sentence.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     using var model = new MiniLMWordSenseModel();
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
///     Download through a mirror (files at <c>{mirror}/all-MiniLM-L6-v2-int8/{file}</c>) instead of the public source.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"));
///     using var mirrored = new MiniLMWordSenseModel();
///     await mirrored.EnsureModelAsync(mirror);
///     var mirroredSenses = await mirrored.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
///     Offline: the two files are in one flat directory, for example <c>/opt/models/minilm</c>.
///     <code>
///     using DemaConsulting.WordSense.MiniLM;
///
///     using var offline = new MiniLMWordSenseModel("/opt/models/minilm");
///     await offline.EnsureModelAsync();   // optional: verifies SHA-256 now, no network
///     var offlineSenses = await offline.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public sealed class MiniLMWordSenseModel : IWordSenseModel
{
    private readonly FallbackWordSenseModel _model;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">
    ///     A directory that holds <c>model_int8.onnx</c> and <c>vocab.txt</c>, or null to use the per-user store that
    ///     <see cref="EnsureModelAsync"/> fills.
    /// </param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <remarks>
    ///     The constructor does not touch the file system: a directory that does not exist, a missing file or a file
    ///     with a wrong SHA-256 is reported by <see cref="EnsureModelAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public MiniLMWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
    {
        Encoder = new MiniLMEncoderModel(modelDirectory, maxSequenceLength);
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
