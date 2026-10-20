namespace DemaConsulting.WordSense;

/// <summary>
///     One analysis of a written word: its lemma, its part of speech and its grammatical features.
/// </summary>
/// <remarks>
///     <para>
///     A lemma is the dictionary base form of a word, so the word <c>records</c> has the lemma <c>record</c>. An analysis
///     names no sense: the senses of an analysis are the senses of its lemma and part of speech, listed by
///     <see cref="WordLexicon.SensesOf"/>. A form that belongs to several lemmas or tenses (<c>left</c>, <c>bases</c>,
///     <c>read</c>) has one analysis for each.
///     </para>
///     <para>
///     <see cref="Features"/> lists the grammatical features of the form as <c>Feature=Value</c> items joined by
///     <c>+</c> in a fixed order, for example <c>Number=Plur</c> for <c>records</c> as a noun or
///     <c>Person=3+Tense=Pres</c> for <c>records</c> as a verb; it is empty when the features are not known.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     foreach (WordDetails analysis in WordLexicon.Lookup("lead"))
///     {
///         Console.WriteLine($"{analysis.Lemma}, {analysis.PartOfSpeech}, {analysis.Features}");
///     }
///     </code>
/// </example>
/// <param name="Lemma">The base form of the word, for example <c>record</c> for <c>records</c>.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Features">The grammatical features, or an empty string when they are not known.</param>
public sealed record WordDetails(string Lemma, PartOfSpeech PartOfSpeech, string Features)
{
    /// <summary>
    ///     Finds the value of a grammatical feature in <see cref="Features"/>.
    /// </summary>
    /// <param name="name">The name of the feature, for example <c>Tense</c> or <c>Number</c>.</param>
    /// <param name="value">Receives the value, for example <c>Past</c>; <see langword="null"/> when the analysis has no such feature.</param>
    /// <returns><see langword="true"/> when the feature is present.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public bool TryGetFeature(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        value = null;
        if (name.Length == 0)
        {
            return false;
        }

        // Scan the items without splitting the string; only the found value is allocated
        var rest = Features.AsSpan();
        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf('+');
            var item = end < 0 ? rest : rest[..end];
            var equals = item.IndexOf('=');
            if (equals == name.Length && item[..equals].SequenceEqual(name))
            {
                value = item[(equals + 1)..].ToString();
                return true;
            }

            rest = end < 0 ? default : rest[(end + 1)..];
        }

        return false;
    }
}
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
    ///     "she is here". The lexicon lists it as the last analysis of the word, after the analyses it already had.
    /// </summary>
    Auxiliary = 9,
}
