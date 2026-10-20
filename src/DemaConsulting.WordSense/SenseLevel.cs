namespace DemaConsulting.WordSense;

/// <summary>
///     The level at which a word was answered, from nothing to a single fine sense.
/// </summary>
/// <remarks>
///     <para>
///     The levels nest and are ordered by increasing precision: a word has a part of speech (the lemma and part of
///     speech of its analysis), a part of speech may have groups of closely related and easily confused senses, and a
///     group or a part of speech has senses. A group exists only where it was curated: a set of at least two, but not
///     all, senses of one lemma and part of speech that are closely related in meaning and easily confused. A model
///     that is unsure between two senses answers at the next level that exists above them: the group they share, or
///     else the part of speech.
///     </para>
///     <para>
///     Test the level of a result, never whether an identifier is <see langword="null"/>: an answer at the
///     <see cref="PartOfSpeech"/> level is a valid answer that names no sense by design.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static string Describe(ClassifyResult answer) => answer.Level switch
///     {
///         SenseLevel.Sense => $"sense {answer.SenseId}",
///         SenseLevel.Group => $"group {answer.GroupId}",
///         SenseLevel.PartOfSpeech => $"part of speech {answer.Details?.PartOfSpeech}",
///         _ => "unknown",
///     };
///     </code>
/// </example>
public enum SenseLevel
{
    /// <summary>
    ///     The word is unknown: it has no analysis, <see cref="ClassifyResult.Details"/> is null and the confidence is 0.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The word was answered with its analysis (lemma, part of speech and features) only; no group and no sense is named.
    /// </summary>
    PartOfSpeech = 1,

    /// <summary>
    ///     The word was answered with a group of closely related senses: <see cref="ClassifyResult.GroupId"/> is set and
    ///     <see cref="ClassifyResult.SenseId"/> is not.
    /// </summary>
    Group = 2,

    /// <summary>
    ///     The word was answered with one fine sense: <see cref="ClassifyResult.SenseId"/> is set, and
    ///     <see cref="ClassifyResult.GroupId"/> is set when the sense belongs to a group.
    /// </summary>
    Sense = 3,
}
