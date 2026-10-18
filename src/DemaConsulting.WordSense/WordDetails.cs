namespace DemaConsulting.WordSense;

/// <summary>
///     One possible analysis of a written word: the sense it stands for (when the word is a trained one), its lemma, its
///     part of speech and its grammatical features.
/// </summary>
/// <remarks>
///     <para>
///     A lemma is the dictionary base form of a word, so the word <c>records</c> has the lemma <c>record</c>. The
///     <see cref="SenseId"/> names one class of the coarse sense inventory of the shipped models, for example
///     <c>lead#metal</c> or <c>read#past</c>; it is <see langword="null"/> for an analysis that has no sense, which is the
///     case for most words. Treat a sense id as an opaque, stable key (for example the key of your own pronunciation
///     lexicon).
///     </para>
///     <para>
///     <see cref="Features"/> lists the grammatical features of the form as <c>Feature=Value</c> items joined by
///     <c>+</c> in a fixed order, for example <c>Number=Plur</c> for <c>records</c> as a noun or
///     <c>Person=3+Tense=Pres</c> for <c>records</c> as a verb; it is empty when the features are not known.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     foreach (WordDetails sense in WordLexicon.Lookup("lead"))
///     {
///         Console.WriteLine($"{sense.SenseId ?? "-"}: {sense.Lemma}, {sense.PartOfSpeech}, {sense.Features}");
///     }
///     </code>
/// </example>
/// <param name="SenseId">
///     The identifier of the sense (the class id, <c>lemma#label</c>), or <see langword="null"/> when the analysis has no
///     sense.
/// </param>
/// <param name="Lemma">The base form of the word, for example <c>record</c> for <c>records</c>.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Features">The grammatical features, or an empty string when they are not known.</param>
public sealed record WordDetails(string? SenseId, string Lemma, PartOfSpeech PartOfSpeech, string Features);

/// <summary>
///     The part of speech of a word.
/// </summary>
/// <remarks>
///     Ten parts of speech are defined: the eight traditional ones, <see cref="Determiner"/> and
///     <see cref="Auxiliary"/>. A determiner is one of the articles (<c>the</c>, <c>a</c>), possessives (<c>his</c>),
///     demonstratives (<c>this</c>), quantifiers (<c>some</c>) and number words (<c>two</c>); the dictionary the lexicon
///     is built from marks these words as a class of their own, and treating them as adjectives, pronouns or nouns would
///     give wrong answers for the most frequent words of English. An auxiliary is a modal or helping verb such as
///     <c>will</c>, <c>can</c>, <c>is</c> or <c>have</c>; the lexicon lists it as an additional sense next to the
///     existing ones, so <c>will</c> has the senses noun, verb and auxiliary.
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

    /// <summary>
    ///     A determiner: an article, possessive, demonstrative, quantifier or number word, for example <c>the</c> or
    ///     <c>his</c>.
    /// </summary>
    Determiner = 8,

    /// <summary>
    ///     An auxiliary (helping or modal) verb, for example the <c>will</c> in "we will record" or the <c>is</c> in
    ///     "she is here". Only the Basic model answers this value; the encoder models never do.
    /// </summary>
    Auxiliary = 9,
}
