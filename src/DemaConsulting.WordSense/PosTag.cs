namespace DemaConsulting.WordSense;

/// <summary>
///     The tag set of the statistical tagger: the ten parts of speech of the lexicon plus three closed-class tags that
///     only exist inside the tagger, and the sentence boundary.
/// </summary>
/// <remarks>
///     The first ten tags have the same numeric values as <see cref="PartOfSpeech"/>, so a part of speech converts to
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

    /// <summary>The determiner tag (also the lexicon part of speech <see cref="PartOfSpeech.Determiner"/>).</summary>
    public const int Determiner = 8;

    /// <summary>The auxiliary tag (modals and forms of be, do and have used as helpers; also the lexicon part of speech <see cref="PartOfSpeech.Auxiliary"/>).</summary>
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
    ///     The closed-class tags (determiner, auxiliary, infinitive marker, punctuation, other) that a word may have
    ///     regardless of the dictionary; only the determiner and the auxiliary are also parts of speech of the lexicon.
    /// </summary>
    public static readonly int[] ClosedClasses = [Determiner, Auxiliary, InfinitiveTo, Punctuation, Other];

    /// <summary>
    ///     The single-letter names of the tags, in tag order, followed by the boundary; used in the vendored count files.
    /// </summary>
    public const string Letters = "NRVJBPCIDATUOS";

    /// <summary>
    ///     The tags that mark a clause as having a verb: the main verb tag. The auxiliary tag (modals and the helper forms of
    ///     be, do and have) does not count; the development sets were slightly better without it.
    /// </summary>
    public const int ClauseVerbMask = 1 << Verb;

    /// <summary>
    ///     The tags that start a new clause: the conjunction (coordinating and subordinating) and punctuation.
    /// </summary>
    public const int ClauseBreakMask = (1 << Conjunction) | (1 << Punctuation);

    /// <summary>
    ///     Gets the tag of a part of speech.
    /// </summary>
    /// <param name="partOfSpeech">The part of speech.</param>
    /// <returns>The tag with the same numeric value.</returns>
    public static int Of(PartOfSpeech partOfSpeech) => (int)partOfSpeech;

    /// <summary>
    ///     Gets the clause state after a tag: whether the current clause has a verb. A clause starts at the beginning of
    ///     the sentence segment and after a tag of <see cref="ClauseBreakMask"/>; it has a verb from the first tag of
    ///     <see cref="ClauseVerbMask"/> on, except that the first token of the segment does not give the clause a verb when
    ///     the dictionary lists it as a verb only in forms that are never the finite verb of a clause (a gerund such as
    ///     <c>Resolving</c> in <c>Resolving this issue requires care</c>, or a participle that is not also a past tense). The
    ///     boundary pseudo tag does not change the state.
    /// </summary>
    /// <param name="hasVerb">The state before the tag: 1 when the clause already has a verb, otherwise 0.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="nonFiniteStart">Whether the token is the first of its segment and is listed only as a non-finite verb.</param>
    /// <returns>The state after the tag: 1 or 0.</returns>
    public static int NextClause(int hasVerb, int tag, bool nonFiniteStart = false)
    {
        if (tag >= Count)
        {
            return hasVerb;
        }

        var bit = 1 << tag;
        if ((ClauseBreakMask & bit) != 0 || nonFiniteStart)
        {
            return 0;
        }

        return hasVerb != 0 || (ClauseVerbMask & bit) != 0 ? 1 : 0;
    }
}
