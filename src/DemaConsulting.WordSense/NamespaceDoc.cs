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
///     the user guide appendix "Appendix: Supported Senses and Tags"
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md) and
///     can be discovered at run time with <see cref="IWordSenseClassifier.KnownLemmas"/> and
///     <see cref="IWordSenseClassifier.GetSenses"/>.
///     </para>
///     <para>
///     Tag and sense catalog: every sense has exactly one part-of-speech tag (<c>noun</c>, <c>verb</c>, <c>adj</c> or
///     <c>adv</c>) and may carry qualifier tags (<c>past</c> and <c>present</c> in the shipped inventory;
///     <c>comparative</c>, <c>participle</c> and <c>superlative</c> are valid but unused). The inventory is a set of
///     heteronyms such as <c>lead</c> (senses <c>guide-noun</c>, <c>guide-verb</c> and <c>metal</c>, groups
///     <c>guide</c> and <c>metal</c>) and <c>read</c> (senses <c>past</c> and <c>present</c>), plus several hundred
///     common words that are both a noun and a verb. Use <see cref="IWordSenseClassifier.ClassifyAsync(System.Collections.Generic.IReadOnlyList{string},System.Threading.CancellationToken)"/>
///     when the sense or group matters, and <see cref="IWordSenseClassifier.ClassifyTagAsync"/> or
///     <see cref="IWordSenseClassifier.IsTagAsync"/> for a coarse question such as noun versus verb.
///     </para>
///     <para>
///     Classifying one word: pass the whole sentence and read the entry of the target word. With the string overload,
///     pick the <see cref="WordSenseToken"/> whose start offset is the target's; with the list overload, tokenize so
///     that every word and every punctuation mark is its own element (<c>"race"</c> and <c>"."</c>, never
///     <c>"race."</c>) and read the entry at the target's index. A result is null when the word is not an ambiguous
///     word the library knows or when the classifier abstained because the evidence was weak.
///     </para>
///     <para>
///     Tokenization and context window: the list overloads read the whole list as one running text with the words
///     joined by single spaces, so several sentences in one list provide context for each other; classify independent
///     sentences with one call each. The string overload splits the text into words, whitespace and single punctuation
///     characters for you. The encoder reads at most <see cref="WordSenseClassifierOptions.MaxSequenceLength"/> word
///     pieces (default 256); a longer text is classified per target word from a window of that size centered on the
///     word, and text outside the window is ignored.
///     </para>
///     <para>
///     Thread safety: a <see cref="WordSenseClassifier"/> may be shared by many threads and its methods called
///     concurrently. Dispose it exactly once, after all calls have finished.
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
///     Classifying one target word of one sentence with the string overload:
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     const string sentence = "They went to the race.";
///     var start = sentence.IndexOf("race", StringComparison.Ordinal);
///     var tokens = await classifier.ClassifyAsync(sentence);
///     WordSense? sense = tokens.Single(t => t.Start == start).Sense;
///     Console.WriteLine(sense?.Sense ?? "-");
///     </code>
///     Choosing a pronunciation by group (the library holds no pronunciation data; the table is the consumer's own):
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     var pronunciations = new Dictionary&lt;(string Lemma, string Group), string&gt;
///     {
///         [("lead", "guide")] = "leed",
///         [("lead", "metal")] = "led",
///     };
///
///     string[] words = ["The", "pipe", "is", "made", "of", "lead", "."];
///     WordSense?[] senses = await classifier.ClassifyAsync(words);
///     WordSense? lead = senses[Array.IndexOf(words, "lead")];
///     if (lead is not null &amp;&amp; pronunciations.TryGetValue((lead.Lemma, lead.Group), out var phonemes))
///     {
///         Console.WriteLine(phonemes);
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
