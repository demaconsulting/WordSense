namespace DemaConsulting.WordSense;

/// <summary>
///     The part of speech of a word.
/// </summary>
/// <remarks>
///     All eight traditional parts of speech are defined. Determiners (for example <c>the</c>, <c>a</c>) have no
///     value: such words have no entry in <see cref="Words"/>.
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
