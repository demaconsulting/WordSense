namespace DemaConsulting.WordSense;

/// <summary>
///     One possible answer for a word: its lemma, its part of speech and, for a heteronym, its pronunciation.
/// </summary>
/// <remarks>
///     The classifier matches a word as written to its lemma through the table's known forms, so inflected
///     forms resolve to their base form: the word <c>records</c> has the lemma <c>record</c>. A dictionary answer
///     (one part of speech, no context needed) has a null <see cref="Pronunciation"/>.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///     foreach (WordDetails candidate in classifier.GetCandidates("record"))
///     {
///         Console.WriteLine($"{candidate.Lemma}: {candidate.PartOfSpeech}, {candidate.Pronunciation?.Meaning ?? "-"}");
///     }
///     </code>
/// </example>
/// <param name="Lemma">The lower-case base form of the word, for example <c>record</c> for <c>records</c>.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Pronunciation">
///     The pronunciation, or <see langword="null"/> when the lemma is not a heteronym (a lemma is a heteronym when its
///     senses have at least two distinct meanings).
/// </param>
public sealed record WordDetails(string Lemma, PartOfSpeech PartOfSpeech, Pronunciation? Pronunciation);
