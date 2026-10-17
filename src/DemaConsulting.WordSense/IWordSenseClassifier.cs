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
///     how the context is used, and what the confidence means, is defined by each model package. A model that cannot
///     decide leaves <see cref="WordSense.Details"/> null with confidence 0.
///     </para>
///     <para>
///     A classifier is safe for concurrent use. It does not own the model that created it.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     IWordSenseModel model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words);
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
    /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed and has to run.</exception>
    Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default);
}

/// <summary>
///     The classification of one word.
/// </summary>
/// <remarks>
///     <see cref="Details"/> is null when the word is unknown, or when it has several senses and the model could not
///     choose one; <see cref="Confidence"/> is then 0. For a word with one sense the confidence is 1. For a word that
///     was resolved from its context, the confidence is defined by the model package that produced it.
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
public readonly record struct WordSense(string Word, WordDetails? Details, double Confidence);
