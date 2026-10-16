namespace DemaConsulting.WordSense;

/// <summary>
///     Offline word-sense disambiguation for English: decides which part of speech (and, for a heteronym, which
///     pronunciation) an ambiguous word has in its sentence.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c> and has no runtime dependencies. It holds the public API, the embedded lexicon (the
///     part-of-speech dictionary, the senses of the supported lemmas and the inflection rules) and the
///     <see cref="IWordSenseModel"/> abstraction that an encoder package implements. On its own it works without any
///     model, without ONNX Runtime and without network access: <see cref="WordSenseClassifier.Create"/> answers every
///     word with a single part of speech from the lexicon and lists the candidates of every ambiguous word. To also
///     choose between the senses of an ambiguous word from its sentence, add an encoder package, for example
///     <c>DemaConsulting.WordSense.MiniLM</c> (https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/introduction.md),
///     which supplies the encoder model and the weights trained for it.
///     </para>
///     <para>
///     Start with <see cref="WordSenseClassifier.Create"/> (lexicon only) or the factory of a model package, and with
///     the <see cref="IWordSenseClassifier"/> they return. Create one classifier, share it between threads, and dispose
///     it when done. There is one classification method with two overloads and one result type,
///     <see cref="WordAnalysis"/>: the word as given, its <see cref="WordDetails"/> (lemma, <see cref="PartOfSpeech"/>
///     and, for a heteronym, a <see cref="Pronunciation"/>), the confidence margin, the runner-up, whether the
///     classifier abstained, where the answer comes from (<see cref="AnswerBasis"/>) and every possible answer
///     (<see cref="WordAnalysis.Candidates"/>). For raw text the analyses come as <see cref="WordSenseToken"/> values.
///     Configure the classifier with <see cref="WordSenseClassifierOptions"/>.
///     </para>
///     <para>
///     Advanced types: <see cref="LemmaEntry"/>, <see cref="SenseWeights"/>, <see cref="LemmaModelKind"/>,
///     <see cref="LemmaMorphologyRules"/>, <see cref="WordSenseTable"/>, <see cref="WordSenseWeightsTable"/>,
///     <see cref="WordSenseTableFormatException"/>, <see cref="IWordSenseModel"/>,
///     <see cref="WordSenseModelMismatchException"/> and <see cref="SenseDefinition"/> expose the lexicon, the trained
///     weights and the model abstraction. They exist for tooling, custom encoder packages and tests; an application
///     that only classifies words never needs them.
///     </para>
///     <para>
///     Glossary: a <em>lemma</em> is the dictionary base form of a word (for example <c>record</c> for
///     <c>records</c>); a <em>heteronym</em> is a lemma with at least two distinct meanings that are pronounced
///     differently (for example <c>record</c>, the noun REK-urd and the verb rih-KORD, or <c>bow</c>, to bend
///     and the weapon). A <em>dictionary answer</em> is an answer taken from the lexicon alone, for a word with exactly
///     one part of speech; a <em>context answer</em> is chosen by the model from the surrounding words for a
///     lemma that has trained weights. The <em>basis</em> (<see cref="WordAnalysis.Basis"/>) says which of the two
///     an analysis is, or that the word is unknown; the <em>candidates</em> (<see cref="WordAnalysis.Candidates"/>) are
///     the possible answers for the word's lemma.
///     </para>
///     <para>
///     One flow: every word is looked up as a lemma entry with one or more senses. With one sense the answer is the
///     dictionary answer; with several senses and a model with weights the classifier decides from context (and may
///     abstain); with several senses and no model the word abstains and the candidates list the possibilities; a word
///     that is not found is unknown (basis <see cref="AnswerBasis.None"/>, no candidates). The model runs only when at
///     least one word needs context, so text made only of dictionary and unknown words is answered without running
///     it. Set <see cref="WordSenseClassifierOptions.UseLexicon"/> to false to answer only lemmas that have trained
///     weights. Dictionary words are matched in lower case; a word that starts with a capital letter is looked up only
///     at the start of the input or of a sentence, or when it is <c>I</c>, because other capitalized words are mostly
///     names. Determiners and articles (<c>the</c>, <c>a</c>, <c>an</c>, and possessive determiners such as <c>his</c>)
///     have no part-of-speech value and are unknown. A word is therefore unknown (basis
///     <see cref="AnswerBasis.None"/>) when it is not in the lexicon, when the case policy skips it, when it is a
///     determiner, or when it is not a single word (for example <c>race.</c> with its period); the result does not
///     say which.
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
///     97 percent of the time on held-out text and 92.6 percent on real blind-judged sentences when the MiniLM model
///     is used; the part of speech is right 97 percent of the time there.
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
///     <see cref="IWordSenseClassifier.ContextLemmas"/> (empty without a model) and
///     <see cref="IWordSenseClassifier.GetCandidates"/>; the full catalog of lemmas, parts of speech and meanings is
///     the user guide appendix
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md).
///     </para>
///     <para>
///     Classifying one word: pass the whole sentence and read the entry of the target word. A word is matched to its
///     lemma through known forms (inflections): <see cref="WordAnalysis.Word"/> is the input and
///     <see cref="WordDetails.Lemma"/> the base form. <see cref="WordAnalysis.Details"/> is null when the word is not
///     known, has several parts of speech and no usable classifier (it abstains), or when the classifier abstained
///     because the evidence was weak (<see cref="WordAnalysis.Abstained"/>). To tell them apart read
///     <see cref="WordAnalysis.Basis"/> and <see cref="WordAnalysis.Abstained"/>: an unknown word has basis
///     <see cref="AnswerBasis.None"/> and is not abstained. A consumer that needs an answer for every word must
///     supply its own fallback; the library never guesses.
///     </para>
///     <para>
///     Confidence: for a context classification <see cref="WordAnalysis.Margin"/> is the score of the best candidate
///     minus the score of the runner-up (a score difference, not a probability; each lemma has its own scale),
///     <see cref="WordAnalysis.RunnerUp"/> is the second-best candidate and <see cref="WordAnalysis.RunnerUpMargin"/>
///     is the runner-up's score minus the third-best's (0 when there are only two candidates). They are also set when
///     the classifier abstained, so a caller may apply its own threshold; for a dictionary answer they are 0 and null.
///     </para>
///     <para>
///     Tokenization and context: the list overload reads the whole list as one running text with the words joined by
///     single spaces, so several sentences in one list provide context for each other; classify independent
///     sentences with one call each. The string overload splits the text into words, whitespace and single punctuation
///     characters for you. How much text the model reads is a property of the model package.
///     </para>
///     <para>
///     Thread safety: a classifier may be shared by many threads and its methods called concurrently (as thread-safe as
///     its <see cref="IWordSenseModel"/>). Dispose it once, after all calls have finished (a second dispose is
///     harmless); after disposal a call that needs the model (some word needs context) throws
///     <see cref="ObjectDisposedException"/>, while a call that needs none still works.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a tokenized sentence with the lexicon alone and read what is known about each word.
///     <code>
///     using var classifier = WordSenseClassifier.Create();
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
/// </example>
internal static class NamespaceDoc;
