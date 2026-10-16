namespace DemaConsulting.WordSense;

/// <summary>
///     Offline word-sense disambiguation for English: decides which sense (for example noun or verb, or
///     one of several pronunciations) an ambiguous word has in its sentence.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; its only runtime dependency is ONNX Runtime. Classification runs locally. The classifier
///     table is embedded in the assembly; the ~23 MB int8 ONNX encoder model (<c>model_int8.onnx</c>, plus a
///     small <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached
///     per user, or it is read from a directory you supply (<see cref="WordSenseClassifierOptions.ModelDirectory"/>),
///     in which case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="WordSenseClassifier.CreateAsync"/> and the <see cref="IWordSenseClassifier"/> it returns.
///     Create one classifier, share it between threads, and dispose it when done. The answers are
///     <see cref="WordSense"/> values (lemma, sense, group and tags), <see cref="WordSenseDetail"/> values (with the
///     confidence margin) and <see cref="WordSenseToken"/> values (for raw text). Configure the classifier with
///     <see cref="WordSenseClassifierOptions"/> and the model download with
///     <see cref="WordSenseModelDownloaderOptions"/>, <see cref="DownloadMirror"/> and
///     <see cref="WordSenseModelDownloader"/>.
///     </para>
///     <para>
///     Advanced types: <see cref="LemmaModel"/>, <see cref="LemmaModelKind"/>, <see cref="WordSenseTable"/>,
///     <see cref="WordSenseTableFormatException"/>, <see cref="IWordSenseEncoder"/>,
///     <see cref="OnnxWordSenseEncoder"/>, <see cref="WordSenseModelMismatchException"/> and
///     <see cref="SenseDefinition"/> expose the trained table and the encoder. They exist for tooling, custom
///     encoders and tests; an application that only classifies words never needs them.
///     </para>
///     <para>
///     Groups and pronunciation: this library has no pronunciation data. A sense's <see cref="WordSense.Group"/> is
///     the library's only equivalence mechanism: senses of one lemma that share a group are interchangeable for the
///     consumer (in the shipped inventory, they share a pronunciation). A consumer such as a speech synthesizer keeps its
///     own pronunciations keyed by (<see cref="WordSense.Lemma"/>, <see cref="WordSense.Group"/>). See
///     <see cref="WordSense"/> for the full explanation. The supported lemmas, senses, tags and groups are listed in
///     the user guide appendix <c>docs/user_guide/appendix-senses.md</c> ("Appendix: Supported Senses and Tags") and
///     can be discovered at run time with <see cref="IWordSenseClassifier.KnownLemmas"/> and
///     <see cref="IWordSenseClassifier.GetSenses"/>.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a tokenized sentence and read the sense of the ambiguous word.
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense?[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         // null: not an ambiguous word, or the classifier abstained
///         Console.WriteLine($"{words[i]}: {senses[i]?.Sense ?? "-"}");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
