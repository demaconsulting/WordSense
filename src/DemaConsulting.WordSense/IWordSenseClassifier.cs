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
///     Each word is lower-cased and looked up in <see cref="Words"/>. The curly apostrophe (U+2019) is read as
///     <c>'</c>, and a possessive <c>'s</c> is accepted for a lemma that has a trained classifier. A word without an
///     entry is unknown. A word with one sense is answered with confidence 1. A word with several senses is resolved
///     from its context when the model has a classifier for its lemma; otherwise it stays unresolved.
///     </para>
///     <para>
///     A word that starts with a capital letter and has no trained classifier is looked up only when it is the first
///     word, follows <c>.</c>, <c>!</c>, <c>?</c>, <c>:</c> or an opening quote or bracket, or is exactly <c>I</c>; any
///     other capitalized word (usually a name) is unknown.
///     </para>
///     <para>
///     <b>Confidence.</b> For a word with several senses and a classifier, let <c>m</c> be the score margin between
///     the best and the second-best sense and <c>a</c> the abstain margin of the lemma (never negative). The
///     confidence is <c>clamp(0.5 + 0.5 * (m - a) / max(a, 0.5), 0, 1)</c>: it is exactly 0.5 at the point where the
///     lemma's own rule would abstain, grows toward 1 for larger margins and falls toward 0 for smaller ones. A caller
///     threshold of 0.5 therefore reproduces the lemma's abstain rule; a higher threshold is stricter.
///     </para>
///     <para>
///     A classifier is safe for concurrent use. It does not own the model that created it.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // classifier comes from IWordSenseModel.Create()
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words, CancellationToken.None);
///     foreach (WordSense sense in senses.Where(s => s.Details is not null))
///     {
///         Console.WriteLine($"{sense.Word}: {sense.Details!.PartOfSpeech} ({sense.Confidence:F2})");
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
    /// <exception cref="ArgumentNullException">Thrown when the list or an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed and has to run.</exception>
    Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken);
}

/// <summary>
///     The classification of one word.
/// </summary>
/// <remarks>
///     <see cref="Details"/> is null when the word is unknown, or when it has several senses and none could be chosen
///     (the model has no classifier for it); <see cref="Confidence"/> is then 0. For a word with one sense the
///     confidence is 1. See <see cref="IWordSenseClassifier"/> for how the confidence of a context decision is derived.
/// </remarks>
/// <example>
///     <code>
///     WordSense sense = new("record", new WordDetails("record", PartOfSpeech.Verb, "verb", "rih-KORD"), 0.93);
///     Console.WriteLine($"{sense.Word} is a {sense.Details?.PartOfSpeech} with confidence {sense.Confidence:P0}");
///     </code>
/// </example>
/// <param name="Word">The word as given by the caller.</param>
/// <param name="Details">The chosen sense, or <see langword="null"/> when none was chosen.</param>
/// <param name="Confidence">The confidence in <paramref name="Details"/>, from 0 to 1.</param>
public sealed record WordSense(string Word, WordDetails? Details, double Confidence);
