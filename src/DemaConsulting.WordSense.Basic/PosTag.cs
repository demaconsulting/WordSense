namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The tag set of the statistical tagger: the eight parts of speech of the lexicon plus five closed-class tags that
///     only exist inside the tagger, and the sentence boundary.
/// </summary>
/// <remarks>
///     The first eight tags have the same numeric values as <see cref="PartOfSpeech"/>, so a part of speech converts to
///     its tag with a cast. The tags are the states of the hidden Markov model; the boundary is a pseudo state that
///     only appears in the transition counts.
/// </remarks>
internal static class PosTag
{
    /// <summary>The noun tag (also proper nouns).</summary>
    public const int Noun = 0;

    /// <summary>The pronoun tag.</summary>
    public const int Pronoun = 1;

    /// <summary>The verb tag (main verbs).</summary>
    public const int Verb = 2;

    /// <summary>The adjective tag.</summary>
    public const int Adjective = 3;

    /// <summary>The adverb tag.</summary>
    public const int Adverb = 4;

    /// <summary>The preposition tag.</summary>
    public const int Preposition = 5;

    /// <summary>The conjunction tag (coordinating and subordinating).</summary>
    public const int Conjunction = 6;

    /// <summary>The interjection tag.</summary>
    public const int Interjection = 7;

    /// <summary>The determiner tag (a closed class that has no <see cref="PartOfSpeech"/>).</summary>
    public const int Determiner = 8;

    /// <summary>The auxiliary tag (modals and forms of be, do and have used as helpers).</summary>
    public const int Auxiliary = 9;

    /// <summary>The infinitive marker tag (the word "to" used as a particle).</summary>
    public const int InfinitiveTo = 10;

    /// <summary>The punctuation tag.</summary>
    public const int Punctuation = 11;

    /// <summary>The tag of everything else: numbers, symbols, other particles and foreign words.</summary>
    public const int Other = 12;

    /// <summary>The number of tags.</summary>
    public const int Count = 13;

    /// <summary>The sentence boundary pseudo tag; its index follows the last tag.</summary>
    public const int Boundary = 13;

    /// <summary>The number of tags plus the boundary: the size of every dimension of the transition table.</summary>
    public const int Extended = 14;

    /// <summary>
    ///     The open classes (noun, verb, adjective, adverb) in ascending tag order: the classes that a word unknown to
    ///     the dictionary may belong to.
    /// </summary>
    public static readonly int[] OpenClasses = [Noun, Verb, Adjective, Adverb];

    /// <summary>
    ///     The closed-class tags (determiner, auxiliary, infinitive marker, punctuation, other) that are not parts of
    ///     speech of the lexicon.
    /// </summary>
    public static readonly int[] ClosedClasses = [Determiner, Auxiliary, InfinitiveTo, Punctuation, Other];

    /// <summary>
    ///     The single-letter names of the tags, in tag order, followed by the boundary; used in the vendored count files.
    /// </summary>
    public const string Letters = "NRVJBPCIDATUOS";

    /// <summary>
    ///     Gets the tag of a part of speech.
    /// </summary>
    /// <param name="partOfSpeech">The part of speech.</param>
    /// <returns>The tag with the same numeric value.</returns>
    public static int Of(PartOfSpeech partOfSpeech) => (int)partOfSpeech;
}
