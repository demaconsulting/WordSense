namespace DemaConsulting.WordSense;

/// <summary>
///     The part of speech of a word in its sentence.
/// </summary>
/// <remarks>
///     All eight traditional parts of speech are defined. The context classifier answers only <see cref="Noun"/>,
///     <see cref="Verb"/>, <see cref="Adjective"/> and <see cref="Adverb"/>; a dictionary answer (see
///     <see cref="AnswerBasis.Dictionary"/>) may also be a <see cref="Pronoun"/> or <see cref="Preposition"/>, and
///     <see cref="Conjunction"/> and <see cref="Interjection"/> appear only among the candidates of a word that has
///     other parts of speech too. Determiners (for example <c>the</c>, <c>a</c>) have no value: such words are unknown.
/// </remarks>
public enum PartOfSpeech
{
    /// <summary>
    ///     A noun, for example the <c>record</c> in "a world record". Answered by context and by the dictionary.
    /// </summary>
    Noun = 0,

    /// <summary>
    ///     A pronoun, for example <c>she</c>. A dictionary answer or candidate; never a context answer.
    /// </summary>
    Pronoun = 1,

    /// <summary>
    ///     A verb, for example the <c>record</c> in "to record a song". Answered by context and by the dictionary.
    /// </summary>
    Verb = 2,

    /// <summary>
    ///     An adjective, for example the <c>close</c> in "a close friend". Answered by context and by the dictionary.
    /// </summary>
    Adjective = 3,

    /// <summary>
    ///     An adverb, for example the <c>close</c> in "stand close". Answered by context and by the dictionary.
    /// </summary>
    Adverb = 4,

    /// <summary>
    ///     A preposition, for example <c>under</c>. A dictionary answer or candidate; never a context answer.
    /// </summary>
    Preposition = 5,

    /// <summary>
    ///     A conjunction. Appears only as a candidate of a dictionary word that has other parts of speech too; never an
    ///     answer.
    /// </summary>
    Conjunction = 6,

    /// <summary>
    ///     An interjection. Appears only as a candidate of a dictionary word that has other parts of speech too; never
    ///     an answer.
    /// </summary>
    Interjection = 7,
}
