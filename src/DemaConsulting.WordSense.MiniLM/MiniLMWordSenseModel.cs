using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The all-MiniLM-L6-v2 (int8) encoder as a word-sense model.
/// </summary>
/// <remarks>
///     <para>
///     This model is built on <c>DemaConsulting.WordSense.Onnx</c>: it derives from <see cref="OnnxWordSenseModel"/>,
///     which provides the tokenizer, the ONNX session, the verified downloader and the classification, and adds only the
///     identity of this encoder (its pinned files and its embedded weights). Everything described for
///     <see cref="OnnxWordSenseModel"/> applies: <c>EnsureModelAsync</c> (mirror precedence, verification, retry and
///     cancellation behavior), <c>ClassifyAsync</c> (lazy ensure), the confidence of a result (recommended starting threshold 0.8), and the requirement to dispose the model with <c>using</c> (disposing while
///     a classification runs is safe: the running calls complete and the ONNX session is released after the last one).
///     </para>
///     <para>
///     Two kinds of data are involved: the per-lemma classifier weights trained for this encoder are embedded in the
///     assembly (nothing to download), while the encoder files (<c>model_int8.onnx</c>,
///     21.9 MiB or 22,972,370 bytes, and <c>vocab.txt</c>, 0.2 MiB or 231,508 bytes; 22 MiB in total) are downloaded
///     once by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>, verified
///     against pinned SHA-256 digests and cached per user; pass a model directory to the constructor to use files you
///     supply, in which case no network access happens at all. The files are described in the repository at
///     https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8. The identifier of this model
///     (<see cref="OnnxWordSenseModel.ModelId"/>) is <c>all-MiniLM-L6-v2-int8</c>; it names the store folder and the
///     folder on a mirror.
///     </para>
///     <para>
///     Choose MiniLM when download size and speed matter, the default recommendation: 22 MiB and about 2 ms per
///     sentence, with about 97% best-sense accuracy on the holdout and about 93% on real text. The alternative
///     <c>DemaConsulting.WordSense.BertBaseCased</c> is larger (104 MiB, about 9 ms per sentence) and case-aware
///     but more accurate (about 99% and 97%); the figures are from
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md and are
///     approximate and machine dependent. The memory the model adds (about 64 MiB) does not include the 15 MiB of the
///     word lexicon.
///     </para>
///     <para>
///     Sentences longer than the maximum sequence length are handled per target word from a window around the word. The
///     weights were trained with the default length of 128; a different length is possible but unvalidated.
///     </para>
///     <para>
///     When you supply a model directory, it must hold <c>model_int8.onnx</c> and <c>vocab.txt</c> directly (no
///     <c>{model-id}</c> subfolder). <c>EnsureModelAsync</c> is optional but recommended: it verifies both files against
///     their pinned SHA-256 digests without any network access and fails early; without it, <c>ClassifyAsync</c>
///     verifies lazily and may throw the same <see cref="FileNotFoundException"/> or
///     <see cref="InvalidDataException"/>. Dispose the model when you no longer need it; disposing while calls are running is safe.
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
///     WordSense[] senses = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
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
public sealed class MiniLMWordSenseModel : OnnxWordSenseModel
{
    private static readonly ModelSpec Definition = new(
        "all-MiniLM-L6-v2-int8",
        [
            new ModelFile("model_int8.onnx", "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1", 22972370),
            new ModelFile("vocab.txt", "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3", 231508),
        ],
        "model_int8.onnx",
        "vocab.txt",
        true,
        typeof(MiniLMWordSenseModel).Assembly,
        "DemaConsulting.WordSense.MiniLM.weights.bin",
        384);

    /// <summary>
    ///     Initializes a new instance of the <see cref="MiniLMWordSenseModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">
    ///     A directory that holds <c>model_int8.onnx</c> and <c>vocab.txt</c>, or null to use the per-user store that
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/> fills.
    /// </param>
    /// <param name="maxSequenceLength">The maximum sequence length including special tokens; 8 to 512.</param>
    /// <remarks>
    ///     The constructor does not touch the file system: a directory that does not exist, a missing file or a file
    ///     with a wrong SHA-256 is reported by <see cref="OnnxWordSenseModel.EnsureModelAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSequenceLength"/> is outside 8 to 512.</exception>
    public MiniLMWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)
        : base(Definition, modelDirectory, maxSequenceLength)
    {
    }
}
