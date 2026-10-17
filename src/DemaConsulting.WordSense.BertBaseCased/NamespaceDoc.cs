namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The BERT base cased encoder for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word
///     from its sentence with an int8 ONNX bert-base-cased model that keeps the case of the text.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.BertBaseCased</c>, which targets <c>net8.0</c>,
///     <c>net9.0</c> and <c>net10.0</c>; it depends on <c>DemaConsulting.WordSense</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.BertBaseCased</c>. The weights trained for the model are embedded in
///     the assembly. The roughly 108 MB encoder (<c>model_int8.onnx</c>, plus <c>vocab.txt</c>, <c>config.json</c> and
///     <c>tokenizer.json</c>) is downloaded
///     once on first use, verified against pinned SHA-256 digests and cached per user, or it is read from a directory you
///     supply, in which case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="BertBaseCasedWordSenseModel"/>: create it, call <c>EnsureModelAsync</c>, then <c>Create</c> to
///     get an <see cref="IWordSenseClassifier"/>. The files are described under <c>model/bert-base-cased-int8/</c> in the
///     repository (https://github.com/demaconsulting/WordSense/tree/main/model/bert-base-cased-int8). The encoder is
///     the base model without its masked-language head, cut and quantized with
///     <c>tools/model-prep/make-bert-encoder.py</c>.
///     </para>
///     <para>
///     Offline and air-gapped use: fetch <c>model_int8.onnx</c> (SHA-256
///     <c>dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d</c>) and <c>vocab.txt</c> (SHA-256
///     <c>eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02</c>), plus <c>config.json</c> and
///     <c>tokenizer.json</c>, from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/bert-base-cased-int8/{file}</c>,
///     put them in one directory and pass it to the constructor. For a corporate mirror pass a
///     <see cref="MirrorOptions"/> to <c>EnsureModelAsync</c> or set <c>WORDSENSE_MIRROR_URL</c> (with the optional
///     <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> or <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>); files are
///     requested at <c>{mirror}/bert-base-cased-int8/{file}</c>. A file that fails verification is never used.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using var model = new BertBaseCasedWordSenseModel();
///     await model.EnsureModelAsync(null, CancellationToken.None);
///     IWordSenseClassifier classifier = model.Create();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words, CancellationToken.None);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
