namespace DemaConsulting.WordSense;

/// <summary>
///     One possible sense of a word: its lemma, its part of speech and, for a heteronym, its pronunciation meaning.
/// </summary>
/// <remarks>
///     <para>
///     A lemma is the dictionary base form of a word, so the word <c>records</c> has the lemma <c>record</c>. A
///     lemma is a heteronym when its senses have at least two distinct meanings that are pronounced differently, such
///     as <c>lead</c> (to guide) and <c>lead</c> (the metal); only then do <see cref="Meaning"/> and
///     <see cref="Respelling"/> carry values.
///     </para>
///     <para>
///     For speech synthesis, key your own pronunciation lexicon on the pair (<see cref="Lemma"/>,
///     <see cref="Meaning"/>): the meaning is the stable value and the respelling is a human-readable hint.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     foreach (WordDetails sense in WordLexicon.Words["lead"])
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}, {sense.Meaning ?? "-"}, {sense.Respelling ?? "-"}");
///     }
///     </code>
/// </example>
/// <param name="Lemma">The lower-case base form of the word, for example <c>record</c> for <c>records</c>.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Meaning">
///     A short identifier of the meaning that determines the pronunciation, for example <c>bend</c> or <c>weapon</c>
///     for <c>bow</c>, or <see langword="null"/> when the lemma is not a heteronym. Lower-case ASCII letters, digits
///     and hyphens, starting with a letter.
/// </param>
/// <param name="Respelling">
///     A plain-letter respelling of the pronunciation (for example <c>REK-urd</c> for the noun <c>record</c>), or
///     <see langword="null"/> when there is none. ASCII letters, hyphens and apostrophes only; it is not IPA.
/// </param>
public sealed record WordDetails(string Lemma, PartOfSpeech PartOfSpeech, string? Meaning, string? Respelling);

/// <summary>
///     The part of speech of a word.
/// </summary>
/// <remarks>
///     All eight traditional parts of speech are defined. Determiners (for example <c>the</c>, <c>a</c>) have no
///     value: such words have no entry in <see cref="WordLexicon.Words"/>.
/// </remarks>
public enum PartOfSpeech
{
    /// <summary>
    ///     A noun, for example the <c>record</c> in "a world record".
    /// </summary>
    Noun = 0,

    /// <summary>
    ///     A pronoun, for example <c>she</c>.
    /// </summary>
    Pronoun = 1,

    /// <summary>
    ///     A verb, for example the <c>record</c> in "to record a song".
    /// </summary>
    Verb = 2,

    /// <summary>
    ///     An adjective, for example the <c>close</c> in "a close friend".
    /// </summary>
    Adjective = 3,

    /// <summary>
    ///     An adverb, for example the <c>close</c> in "stand close".
    /// </summary>
    Adverb = 4,

    /// <summary>
    ///     A preposition, for example <c>under</c>.
    /// </summary>
    Preposition = 5,

    /// <summary>
    ///     A conjunction, for example <c>because</c>.
    /// </summary>
    Conjunction = 6,

    /// <summary>
    ///     An interjection, for example <c>hello</c>.
    /// </summary>
    Interjection = 7,
}
