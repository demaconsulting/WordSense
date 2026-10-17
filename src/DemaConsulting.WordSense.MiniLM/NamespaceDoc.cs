namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The MiniLM encoder for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from its
///     sentence with an int8 ONNX all-MiniLM-L6-v2 model.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.MiniLM</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends on <c>DemaConsulting.WordSense</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.MiniLM</c>; no native or other setup is needed. The weights
///     trained for the model are embedded in the assembly. The roughly 23 MB encoder (<c>model_int8.onnx</c>, plus a small
///     <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached per user, or
///     it is read from a directory you supply, in which case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="MiniLMWordSenseModel"/>: create it, call <c>EnsureModelAsync</c>, then <c>Create</c> to get an
///     <see cref="IWordSenseClassifier"/>. The files are described under <c>model/all-MiniLM-L6-v2-int8/</c> in the
///     repository (https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8).
///     </para>
///     <para>
///     Offline and air-gapped use: fetch <c>model_int8.onnx</c> (SHA-256
///     <c>afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1</c>) and <c>vocab.txt</c> (SHA-256
///     <c>07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3</c>) from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/all-MiniLM-L6-v2-int8/{file}</c>,
///     put them in one directory and pass it to the constructor. For a corporate mirror pass a
///     <see cref="MirrorOptions"/> to <c>EnsureModelAsync</c> or set <c>WORDSENSE_MIRROR_URL</c> (with the optional
///     <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> or <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>); files are
///     requested at <c>{mirror}/all-MiniLM-L6-v2-int8/{file}</c>. A file that fails verification is never used.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using var model = new MiniLMWordSenseModel();
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
