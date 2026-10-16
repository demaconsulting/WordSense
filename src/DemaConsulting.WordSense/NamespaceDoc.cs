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
///     <see cref="Pronunciation"/>), the confidence margin, the runner-up, whether the classifier abstained, where the
///     answer comes from (<see cref="AnswerBasis"/>) and every possible answer (<see cref="WordAnalysis.Candidates"/>). For
///     raw text the analyses come as <see cref="WordSenseToken"/> values. Configure the classifier with
///     <see cref="WordSenseClassifierOptions"/> and the model download with
///     <see cref="WordSenseModelDownloaderOptions"/>, <see cref="DownloadMirror"/> and
///     <see cref="WordSenseModelDownloader"/>.
///     </para>
///     <para>
///     Advanced types: <see cref="LemmaEntry"/>, <see cref="SenseWeights"/>, <see cref="LemmaModelKind"/>,
///     <see cref="LemmaMorphologyRules"/>, <see cref="WordSenseTable"/>,
///     <see cref="WordSenseTableFormatException"/>, <see cref="IWordSenseEncoder"/>,
///     <see cref="OnnxWordSenseEncoder"/>, <see cref="WordSenseModelMismatchException"/> and
///     <see cref="SenseDefinition"/> expose the trained table and the encoder. They exist for tooling, custom
///     encoders and tests; an application that only classifies words never needs them.
///     </para>
///     <para>
///     Glossary: a <em>lemma</em> is the dictionary base form of a word (for example <c>record</c> for
///     <c>records</c>); a <em>heteronym</em> is a lemma with at least two distinct meanings that are pronounced
///     differently (for example <c>record</c>, the noun REK-urd and the verb rih-KORD, or <c>bow</c>, to bend
///     and the weapon). A <em>dictionary answer</em> is an answer taken from the table alone, for a word with exactly
///     one part of speech; a <em>context answer</em> is chosen by the classifier from the surrounding words for a
///     lemma that has a trained classifier. The <em>basis</em> (<see cref="WordAnalysis.Basis"/>) says which of the two
///     an analysis is, or that the word is unknown; the <em>candidates</em> (<see cref="WordAnalysis.Candidates"/>) are
///     the possible answers for the word's lemma.
///     </para>
///     <para>
///     One flow: every word is looked up as a lemma entry with one or more senses and optionally trained weights.
///     With one sense the answer is the dictionary answer; with several senses and weights the classifier decides from
///     context (and may abstain); with several senses and no weights the word abstains and the candidates list the
///     possibilities; a word that is not found is unknown (basis <see cref="AnswerBasis.None"/>, no candidates). The
///     encoder runs only when at least one word needs context, so text made only of dictionary and unknown words is
///     answered without running the model. Set <see cref="WordSenseClassifierOptions.UseLexicon"/> to false to answer
///     only lemmas that have a trained classifier. Dictionary words are matched in lower case; a word that starts with a
///     capital letter is looked up only at the start of the input or of a sentence, or when it is <c>I</c>, because
///     other capitalized words are mostly names. Determiners and articles (<c>the</c>, <c>a</c>, <c>an</c>, and
///     possessive determiners such as <c>his</c>) have no part-of-speech value and are unknown. A word is therefore
///     unknown (basis <see cref="AnswerBasis.None"/>) when it is not in the table, when the case policy skips it, when
///     it is a determiner, or when it is not a single word (for example <c>race.</c> with its period); the result does
///     not say which. Example: with a trained classifier <c>bow</c> in "The hunter drew his bow" answers
///     <see cref="PartOfSpeech.Noun"/> with <see cref="Pronunciation.Meaning"/> <c>weapon</c> and respelling
///     <c>boh</c> (basis <see cref="AnswerBasis.Context"/>).
///     </para>
///     <para>
///     Where the data comes from: the part-of-speech dictionary (over 140,000 common words) is derived from Moby
///     Part-of-Speech II by Grady Ward, which is in the public domain; the trained lemmas and the respellings are our
///     own work and no pronunciation data is copied from any dictionary. Measured on the English Web Treebank, the
///     dictionary answers for the parts of speech noun, pronoun, verb, adjective, adverb and preposition are correct
///     97 to 99 percent of the time; for words with several parts of speech, the candidates contain the right one
///     97 percent of the time. Conjunction and interjection are only given as candidates because they did not reach
///     the precision gate. Dictionary answers know nothing about the sentence. For the trained lemmas (the 744 listed
///     in the appendix, which include <c>bow</c>, <c>record</c> and <c>lead</c>) the best sense is correct about
///     97 percent of the time on held-out text and 92.6 percent on real blind-judged sentences; the part of speech is
///     right 97 percent of the time there.
///     </para>
///     <para>
///     The two facts the library answers: first, the part of speech (<see cref="PartOfSpeech"/> defines all eight
///     classic parts of speech; the context classifier answers <see cref="PartOfSpeech.Noun"/>,
///     <see cref="PartOfSpeech.Verb"/>, <see cref="PartOfSpeech.Adjective"/> and <see cref="PartOfSpeech.Adverb"/>);
///     second, for a heteronym only, the pronunciation (<see cref="WordDetails.Pronunciation"/>). A
///     <see cref="Pronunciation"/> has a <see cref="Pronunciation.Meaning"/>, a short identifier of the meaning that
///     decides how the word is said (for example <c>bend</c> or <c>weapon</c> for <c>bow</c>, or <c>noun</c> and
///     <c>verb</c> for <c>record</c>), and an optional <see cref="Pronunciation.Respelling"/>, our own plain-letter
///     spelling of the sound (<c>bow</c> for bending, <c>boh</c> for the weapon): syllables are separated by hyphens
///     and the stressed syllable is written in capitals when there are several, such as <c>REK-urd</c> and
///     <c>rih-KORD</c>. For a lemma that is not a heteronym (most nouns and verbs) <see cref="WordDetails.Pronunciation"/>
///     is <see langword="null"/>. Supported lemmas can be discovered at run time with
///     <see cref="IWordSenseClassifier.ContextLemmas"/> and <see cref="IWordSenseClassifier.GetCandidates"/>; the full
///     catalog of lemmas, parts of speech and meanings is the user guide appendix
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md).
///     </para>
///     <para>
///     Classifying one word: pass the whole sentence and read the entry of the target word. A word is matched to its
///     lemma through known forms (inflections): <see cref="WordAnalysis.Word"/> is the input and
///     <see cref="WordDetails.Lemma"/> the base form. <see cref="WordAnalysis.Details"/> is null when the word is not
///     known, has several parts of speech and no classifier (it abstains), or when the classifier abstained because
///     the evidence was
///     weak (<see cref="WordAnalysis.Abstained"/>). To tell them apart read <see cref="WordAnalysis.Basis"/> and
///     <see cref="WordAnalysis.Abstained"/>: an unknown word has basis <see cref="AnswerBasis.None"/> and is not
///     abstained. A consumer that needs an answer for every word
///     must supply its own fallback; the library never guesses.
///     </para>
///     <para>
///     Confidence: every call returns the full <see cref="WordAnalysis"/>. For a context classification
///     <see cref="WordAnalysis.Margin"/> is the
///     score of the best candidate minus the score of the runner-up (a score difference, not a probability; each
///     lemma has its own scale), <see cref="WordAnalysis.RunnerUp"/> is the second-best candidate and
///     <see cref="WordAnalysis.RunnerUpMargin"/> is the runner-up's score minus the third-best's (0 when there are only
///     two candidates). They are also set when the classifier abstained, so a caller may apply its own threshold; for
///     a dictionary answer they are 0 and null.
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
///     harmless); after disposal a call that needs the encoder (some word needs
///     context) throws <see cref="ObjectDisposedException"/>, while a call that needs none still works.
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
///         // Details is null: unknown, several possible parts of speech, or the classifier abstained
///         var analysis = analyses[i];
///         Console.WriteLine($"{words[i]}: {analysis.Details?.PartOfSpeech.ToString() ?? "-"} ({analysis.Basis})");
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
