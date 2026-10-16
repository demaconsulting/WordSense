namespace DemaConsulting.WordSense;

/// <summary>
///     The part of speech of a word in its sentence.
/// </summary>
/// <remarks>
///     All eight traditional parts of speech are defined so that a later table can use them without a breaking
///     change, but the shipped table only produces <see cref="Noun"/>, <see cref="Verb"/>, <see cref="Adjective"/>
///     and <see cref="Adverb"/>. Pronouns, prepositions, conjunctions and interjections are never answered.
/// </remarks>
public enum PartOfSpeech
{
    /// <summary>
    ///     A noun, for example the <c>record</c> in "a world record". Produced by the shipped table.
    /// </summary>
    Noun = 0,

    /// <summary>
    ///     A pronoun. Defined for completeness; the shipped table never produces it.
    /// </summary>
    Pronoun = 1,

    /// <summary>
    ///     A verb, for example the <c>record</c> in "to record a song". Produced by the shipped table.
    /// </summary>
    Verb = 2,

    /// <summary>
    ///     An adjective, for example the <c>close</c> in "a close friend". Produced by the shipped table.
    /// </summary>
    Adjective = 3,

    /// <summary>
    ///     An adverb, for example the <c>close</c> in "stand close". Produced by the shipped table.
    /// </summary>
    Adverb = 4,

    /// <summary>
    ///     A preposition. Defined for completeness; the shipped table never produces it.
    /// </summary>
    Preposition = 5,

    /// <summary>
    ///     A conjunction. Defined for completeness; the shipped table never produces it.
    /// </summary>
    Conjunction = 6,

    /// <summary>
    ///     An interjection. Defined for completeness; the shipped table never produces it.
    /// </summary>
    Interjection = 7,
}
