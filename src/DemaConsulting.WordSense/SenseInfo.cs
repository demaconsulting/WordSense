using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     One sense of the sense catalog of the lexicon: a meaning of a lemma and part of speech.
/// </summary>
/// <remarks>
///     The identifier is readable and stable (for example <c>bank#coin-bank</c>) and is never reused for another meaning.
///     Every sense is linked to exactly one Open English WordNet sense key, except a project sense, a meaning that Open
///     English WordNet does not cover (for example <c>the#determiner</c>), which has no key. A sense is in at most one
///     group.
/// </remarks>
/// <example>
///     <code>
///     if (WordLexicon.TryGetSense("bank#coin-bank", out SenseInfo sense))
///     {
///         Console.WriteLine($"{sense.SenseId}: {sense.Lemma} ({sense.PartOfSpeech}), key {sense.SenseKey ?? "none"}");
///     }
///     </code>
/// </example>
/// <param name="SenseId">The readable identifier of the sense, <c>lemma#label</c>.</param>
/// <param name="GroupId">The identifier of the group the sense belongs to, or <see langword="null"/> when it is in no group.</param>
/// <param name="Lemma">The lemma of the sense.</param>
/// <param name="PartOfSpeech">The part of speech of the sense.</param>
/// <param name="SenseKey">The Open English WordNet sense key (for example <c>bow%1:06:02::</c>), or <see langword="null"/> for a project sense.</param>
/// <param name="Respelling">The respelling of the sense when its pronunciation differs from the other senses of its lemma, otherwise <see langword="null"/>.</param>
public sealed record SenseInfo(
    string SenseId,
    string? GroupId,
    string Lemma,
    PartOfSpeech PartOfSpeech,
    string? SenseKey,
    string? Respelling);

/// <summary>
///     A group of closely related and easily confused senses of one lemma and part of speech.
/// </summary>
/// <remarks>
///     A group exists only where it was curated: it has at least two senses and never all the senses of its lemma and
///     part of speech. A sense is in at most one group.
/// </remarks>
/// <example>
///     <code>
///     if (WordLexicon.TryGetGroup("just#fair", out SenseGroup group))
///     {
///         Console.WriteLine($"{group.GroupId}: {string.Join(", ", group.SenseIds)}");
///     }
///     </code>
/// </example>
/// <param name="GroupId">The identifier of the group, <c>lemma#group:slug</c>.</param>
/// <param name="Lemma">The lemma of the senses of the group.</param>
/// <param name="PartOfSpeech">The part of speech of the senses of the group.</param>
/// <param name="SenseIds">The identifiers of the senses of the group in inventory order (the most frequent first); at least two.</param>
/// <param name="Respelling">The respelling the senses of the group share, or <see langword="null"/> when they have none.</param>
public sealed record SenseGroup(
    string GroupId,
    string Lemma,
    PartOfSpeech PartOfSpeech,
    ImmutableArray<string> SenseIds,
    string? Respelling);
