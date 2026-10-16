namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The MiniLM encoder for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from its
///     sentence with an int8 ONNX all-MiniLM-L6-v2 model.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.MiniLM</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends on <c>DemaConsulting.WordSense</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.MiniLM</c>; no native or other setup is needed. The weights table
///     trained for the model is embedded in the assembly. The ~23 MB encoder model (<c>model_int8.onnx</c>, plus a small
///     <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached per user, or
///     it is read from a directory you supply (<see cref="MiniLMWordSenseOptions.ModelDirectory"/>), in which case no
///     network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="MiniLMWordSense.CreateClassifierAsync"/>, which returns the
///     <see cref="WordSenseClassifier"/> of the core package wired to this model. Configure it with
///     <see cref="MiniLMWordSenseOptions"/>. The same files are described under <c>model/all-MiniLM-L6-v2-int8/</c> in
///     the repository (https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8).
///     </para>
///     <para>
///     Offline and air-gapped use: on a machine with network access call
///     <see cref="WordSenseModelDownloader.EnsureModelAsync"/> and copy the folder it reports
///     (<see cref="WordSenseModelLocation.Directory"/>) to the target machine, or fetch <c>model_int8.onnx</c> (SHA-256
///     <c>afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1</c>) and <c>vocab.txt</c> (SHA-256
///     <c>07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3</c>) yourself: <c>vocab.txt</c> from
///     <c>https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt</c> and both files from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/all-MiniLM-L6-v2-int8/{file}</c>
///     (a file is verified against its pinned digest wherever it comes from). Then set
///     <see cref="MiniLMWordSenseOptions.ModelDirectory"/> to a directory that holds the two files directly. For a
///     corporate mirror set <see cref="WordSenseModelDownloaderOptions.Mirror"/> or the environment variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> (see <see cref="DownloadMirror"/>); files are requested at
///     <c>{mirror}/all-MiniLM-L6-v2-int8/{file}</c>. The cache store holds one folder named for the model identifier,
///     <c>all-MiniLM-L6-v2-int8</c>, beneath the store root; a file that fails verification is never used.
///     </para>
///     <para>
///     Advanced types: <see cref="MiniLMWordSenseModel"/> is the <see cref="IWordSenseModel"/> implementation and
///     <see cref="WordSenseModelLocation"/> names a verified set of model files.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using var classifier = await MiniLMWordSense.CreateClassifierAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordAnalysis[] analyses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         var analysis = analyses[i];
///         Console.WriteLine($"{words[i]}: {analysis.Details?.PartOfSpeech.ToString() ?? "-"} ({analysis.Basis})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
