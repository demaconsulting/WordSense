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
public sealed record SenseInfo(
    string SenseId,
    string? GroupId,
    string Lemma,
    PartOfSpeech PartOfSpeech,
    string? SenseKey);

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
/// <param name="SenseIds">The identifiers of the senses of the group in inventory order (the order of Open English WordNet); at least two.</param>
public sealed record SenseGroup(
    string GroupId,
    string Lemma,
    PartOfSpeech PartOfSpeech,
    ImmutableArray<string> SenseIds)
{
    /// <summary>
    ///     Tells whether another group has the same values; the sense identifiers are compared as a sequence.
    /// </summary>
    /// <param name="other">The other group.</param>
    /// <returns>True when all values are equal, including the content and order of <see cref="SenseIds"/>.</returns>
    public bool Equals(SenseGroup? other) =>
        other is not null
        && string.Equals(GroupId, other.GroupId, StringComparison.Ordinal)
        && string.Equals(Lemma, other.Lemma, StringComparison.Ordinal)
        && PartOfSpeech == other.PartOfSpeech
        && SameSenseIds(SenseIds, other.SenseIds);

    /// <summary>
    ///     Gets a hash code that is consistent with <see cref="Equals(SenseGroup?)"/>.
    /// </summary>
    /// <returns>The hash code, which includes the content of <see cref="SenseIds"/>.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GroupId, StringComparer.Ordinal);
        hash.Add(Lemma, StringComparer.Ordinal);
        hash.Add(PartOfSpeech);
        if (!SenseIds.IsDefault)
        {
            foreach (var id in SenseIds)
            {
                hash.Add(id, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static bool SameSenseIds(ImmutableArray<string> a, ImmutableArray<string> b)
    {
        if (a.IsDefault || b.IsDefault)
        {
            return a.IsDefault && b.IsDefault;
        }

        return a.AsSpan().SequenceEqual(b.AsSpan());
    }
}
