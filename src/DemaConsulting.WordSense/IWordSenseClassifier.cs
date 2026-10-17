namespace DemaConsulting.WordSense;

/// <summary>
///     Classifies words in context: which sense (lemma, part of speech, pronunciation meaning) each word has.
/// </summary>
/// <remarks>
///     <para>
///     The input is a list of words that the caller has already tokenized; the result array is parallel to it. The
///     list is read as one running text in which the words are joined by single spaces, so several sentences in one
///     list share context. To classify independent sentences, make one call per sentence. Punctuation marks must be
///     elements of their own (<c>"race"</c> then <c>"."</c>, never <c>"race."</c>).
///     </para>
///     <para>
///     Each word is lower-cased and looked up in <see cref="WordLexicon.Words"/>; the curly apostrophe (U+2019) is read
///     as <c>'</c>. A word without an entry is unknown: <see cref="WordSense.Details"/> is null and the confidence is 0.
///     A word with one sense is answered with confidence 1. A word with several senses is resolved from its context;
///     how the context is used, and what the confidence means, is defined by each model package (see
///     <see cref="WordSense"/>). There are exactly two ways to get a null <see cref="WordSense.Details"/>: the word is
///     unknown to the lexicon (every model), or an encoder model cannot score the word (it has several senses but its
///     lemma has no intact trained weights, or the encoder produced no usable vector for it). The Basic model never
///     leaves <see cref="WordSense.Details"/> null for a word that is in the lexicon. Every other word with several
///     senses gets its best sense, even when the confidence is low.
///     </para>
///     <para>
///     Tokenization is the caller's job, and the models expect the tokens that the evaluation used: split off
///     punctuation (<c>"race"</c>, <c>"."</c>), but keep a contraction (<c>"don't"</c>) and a hyphenated word
///     (<c>"re-read"</c>) as one token. Such a token has no lexicon entry, so it is unknown (null, confidence 0), but
///     the surrounding words are still classified. Classify one sentence per call: the Basic model splits the list
///     at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>, while an encoder model reads the whole list as one text,
///     of at most 128 word pieces by default (512 at most); a longer list is handled per word from a window of that
///     size around the word, so very long lists lose distant context but are not rejected. The documentation of the
///     <c>DemaConsulting.WordSense</c> namespace has a reference tokenizer, a table of edge cases (numbers,
///     abbreviations, quotes, dashes, possessives) and the rules for case: the lookup ignores case, MiniLM is uncased
///     and BertBaseCased is cased.
///     </para>
///     <para>
///     A classifier is safe for concurrent use. It does not own the model that created it, so disposing the model
///     while a classification is running is not supported: finish (await) every classification first. After an
///     encoder model is disposed, a call that needs the encoder throws <see cref="ObjectDisposedException"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     IWordSenseModel model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words);
///     foreach (WordSense sense in senses)
///     {
///         // "the" and "." are not in the lexicon: Details is null and Confidence is 0
///         // "record" has several senses: the best sense is returned, with the confidence of the model
///         Console.WriteLine($"{sense.Word}: {sense.Details?.PartOfSpeech.ToString() ?? "unknown"} ({sense.Confidence:F2})");
///     }
///     </code>
/// </example>
public interface IWordSenseClassifier
{
    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>; empty for an empty list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown by an encoder model that has been disposed, when the text has a word that needs the encoder; a text without such a word is still answered. The Basic model is never disposed.</exception>
    Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default);
}

/// <summary>
///     The classification of one word.
/// </summary>
/// <remarks>
///     <para>
///     The type <c>WordSense</c> (this record struct) shares its name with the namespace <c>DemaConsulting.WordSense</c>
///     that contains it; in code, write <c>WordSense[]</c> for the result type.
///     </para>
///     <para>
///     <see cref="Details"/> is null in exactly two cases. First, the word is unknown: it has no entry in the lexicon
///     (a determiner, a number, punctuation, most proper nouns, a misspelling, a contraction such as <c>don't</c>);
///     every model does this. Second, an encoder model (MiniLM, BertBaseCased) could not score a word that has several
///     senses: its lemma has no intact trained weights, or the encoder produced no vector for the word. The packages of
///     this repository ship weights for every lemma (a test asserts that no lemma is dropped), so the second case does
///     not occur in practice, but the contract allows it. In both cases <see cref="Confidence"/> is 0. Every other
///     word gets a sense: a word with one sense gets it with confidence 1, and a word with several senses gets its best
///     sense even when the confidence is low, so a null <see cref="Details"/> never means "low confidence". The Basic
///     model never leaves <see cref="Details"/> null for a word that is in the lexicon.
///     </para>
///     <para>
///     Confidence is a number from 0 to 1: 0 means unknown or unscored, 1 means the word has only one sense in the
///     lexicon, and a word resolved from its context gets a value in between that the model package defines. The
///     scales differ per model and a threshold must not be reused across models. The Basic model gives a word with
///     several senses the probability of the chosen sense (at least one over the number of senses; a threshold such
///     as 0.8 works for it, see the <c>DemaConsulting.WordSense.Basic</c> namespace documentation), so it is never
///     undecided. The encoder models (MiniLM, BertBaseCased) map
///     the margin between the best and the runner-up sense monotonically onto 0 to 1, relative to the abstain margin
///     trained for the word's lemma: 0.5 means the margin equals the abstain margin, larger values mean a clearer
///     decision, and the result is clamped to 0 to 1; the threshold used in their evaluation is 0.5, and the share of words that reaches a given value differs between the two encoders, so tune a threshold per model on your own text. It is a ranking
///     score, not a calibrated probability: compare it against a threshold you choose and treat lower values as
///     uncertain; <see cref="Details"/> still holds the best sense when the confidence is low.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     WordSense sense = new("record", new WordDetails("record", PartOfSpeech.Verb, null, null), 0.93);
///     Console.WriteLine($"{sense.Word} is a {sense.Details?.PartOfSpeech} with confidence {sense.Confidence:P0}");
///
///     // Keep only confident answers; the threshold is specific to the model that produced the confidence
///     bool confident = sense.Details is not null &amp;&amp; sense.Confidence >= 0.5;
///     </code>
/// </example>
/// <param name="Word">The word as given by the caller.</param>
/// <param name="Details">The chosen sense, or <see langword="null"/> when none was chosen.</param>
/// <param name="Confidence">The confidence in <paramref name="Details"/>, from 0 to 1.</param>
public readonly record struct WordSense(string Word, WordDetails? Details, double Confidence);
