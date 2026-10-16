namespace DemaConsulting.WordSense;

/// <summary>
///     Offline word-sense disambiguation for English: decides which part of speech (and, for a heteronym, which
///     pronunciation) an ambiguous word has in its sentence.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; its only runtime dependency is ONNX Runtime. Classification runs locally. The classifier
///     table is embedded in the assembly; the ~23 MB int8 ONNX encoder model (<c>model_int8.onnx</c>, plus a
///     small <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached
///     per user, or it is read from a directory you supply (<see cref="WordSenseClassifierOptions.ModelDirectory"/>),
///     in which case no network access happens at all. Install it with
///     <c>dotnet add package DemaConsulting.WordSense</c>; no native or other setup is needed.
///     </para>
///     <para>
///     Offline and air-gapped use: on a machine with network access call
///     <see cref="WordSenseModelDownloader.EnsureModelAsync"/> and copy the folder it
///     reports (<see cref="WordSenseModelLocation.Directory"/>) to the target machine, or fetch
///     <c>model_int8.onnx</c> (SHA-256 <c>afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1</c>) and
///     <c>vocab.txt</c> (SHA-256 <c>07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3</c>) yourself:
///     <c>vocab.txt</c> from
///     <c>https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt</c> and both files from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/{file}</c>
///     (a file is verified against its pinned digest wherever it comes from). Then set
///     <see cref="WordSenseClassifierOptions.ModelDirectory"/> to a directory that holds the two
///     files directly. For a corporate mirror set <see cref="WordSenseModelDownloaderOptions.Mirror"/> or the
///     environment variables <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>,
///     <c>WORDSENSE_MIRROR_PASSWORD</c> and <c>WORDSENSE_MIRROR_BEARER_TOKEN</c> (see <see cref="DownloadMirror"/>).
///     The cache store holds one folder per model revision beneath the store root, named like
///     <c>all-MiniLM-L6-v2-int8-{first 8 hex digits of the model SHA-256}</c>; a file that fails verification is never
///     used.
///     </para>
///     <para>
///     Start with <see cref="WordSenseClassifier.CreateAsync"/> and the <see cref="IWordSenseClassifier"/> it returns.
///     Create one classifier, share it between threads, and dispose it when done. There is one classification
///     method with two overloads and one result type, <see cref="WordAnalysis"/>: the word as given, its
///     <see cref="WordDetails"/> (lemma, <see cref="PartOfSpeech"/> and, for a heteronym, a
///     <see cref="Pronunciation"/>), the confidence margin, the runner-up and whether the classifier abstained. For
///     raw text the analyses come as <see cref="WordSenseToken"/> values. Configure the classifier with
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
///     Parts of speech and pronunciation: <see cref="PartOfSpeech"/> defines all eight classic parts of speech, but the
///     shipped table only produces <see cref="PartOfSpeech.Noun"/>, <see cref="PartOfSpeech.Verb"/>,
///     <see cref="PartOfSpeech.Adjective"/> and <see cref="PartOfSpeech.Adverb"/>. A lemma whose senses have at least
///     two distinct pronunciation meanings is a heteronym, and only then is <see cref="WordDetails.Pronunciation"/>
///     set; its <see cref="Pronunciation.Meaning"/> is a short identifier such as <c>guide</c> or <c>metal</c>.
///     A <see cref="Pronunciation.Respelling"/> may be present. Supported lemmas can be discovered at run time with
///     <see cref="IWordSenseClassifier.KnownLemmas"/> and <see cref="IWordSenseClassifier.GetCandidates"/>; the full
///     list is the user guide appendix
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md).
///     </para>
///     <para>
///     Classifying one word: pass the whole sentence and read the entry of the target word. A word is matched to its
///     lemma through known forms (inflections): <see cref="WordAnalysis.Word"/> is the input and
///     <see cref="WordDetails.Lemma"/> the base form. <see cref="WordAnalysis.Details"/> is null when the word is not
///     known or when the classifier abstained because the evidence was weak (<see cref="WordAnalysis.Abstained"/>). A
///     consumer that needs an answer for every word must supply its own fallback; the library never guesses.
///     </para>
///     <para>
///     Tokenization and context window: the list overload reads the whole list as one running text with the words
///     joined by single spaces, so several sentences in one list provide context for each other; classify independent
///     sentences with one call each. The string overload splits the text into words, whitespace and single punctuation
///     characters for you. The encoder reads at most <see cref="WordSenseClassifierOptions.MaxSequenceLength"/> word
///     pieces (default 256); a longer text is classified per target word from a window of that size centered on the
///     word, and text outside the window is ignored.
///     </para>
///     <para>
///     Thread safety: a classifier returned by <see cref="WordSenseClassifier.CreateAsync"/> may be shared by many
///     threads and its methods called concurrently (a classifier built on a custom <see cref="IWordSenseEncoder"/>
///     is as thread-safe as that encoder). Dispose it once, after all calls have finished (a second dispose is
///     harmless); after disposal a call whose words include a known word throws
///     <see cref="ObjectDisposedException"/>.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a tokenized sentence and read the part of speech of the ambiguous word.
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordAnalysis[] analyses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         // Details is null: not an ambiguous word, or the classifier abstained
///         Console.WriteLine($"{words[i]}: {analyses[i].Details?.PartOfSpeech.ToString() ?? "-"}");
///     }
///     </code>
///     Choosing a pronunciation by meaning (the consumer keeps its own pronunciation table):
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     var pronunciations = new Dictionary&lt;(string Lemma, string Meaning), string&gt;
///     {
///         [("lead", "guide")] = "leed",
///         [("lead", "metal")] = "led",
///     };
///
///     string[] words = ["The", "pipe", "is", "made", "of", "lead", "."];
///     WordAnalysis[] analyses = await classifier.ClassifyAsync(words);
///     WordDetails? lead = analyses[Array.IndexOf(words, "lead")].Details;
///     if (lead?.Pronunciation is { } pronunciation &amp;&amp;
///         pronunciations.TryGetValue((lead.Lemma, pronunciation.Meaning), out var phonemes))
///     {
///         Console.WriteLine(phonemes);
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
