using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense;

/// <summary>
///     The definition of one sense of a lemma: its name, its tags and its group. Advanced: used to build
///     classifier tables; an application that only classifies words reads <see cref="WordSense"/> instead.
/// </summary>
/// <remarks>
///     The name becomes <see cref="WordSense.Sense"/> on the results. The group, when given, and every tag
///     are validated with <see cref="IsValidTag"/> (lower-case letters, digits and hyphens, starting with a
///     letter, at most <see cref="MaxTagLength"/> characters). The name itself is only required to be
///     non-empty and unique within the lemma; it is not held to the tag syntax. The group is the equivalence
///     class of senses that share a pronunciation (see <see cref="WordSense.Group"/>); the library carries no
///     pronunciation data itself.
/// </remarks>
/// <param name="Name">The sense name (exposed as <see cref="WordSense.Sense"/>), unique within the lemma.</param>
/// <param name="Tags">The tags: at least one, lower-case, sorted ordinally and without duplicates.</param>
/// <param name="Group">
///     The group (pronunciation equivalence class) the sense belongs to, or null for a group of its own (named like
///     the sense).
/// </param>
public sealed partial record SenseDefinition(string Name, IReadOnlyList<string> Tags, string? Group = null)
{
    /// <summary>
    ///     The longest tag, in characters.
    /// </summary>
    public const int MaxTagLength = 32;

    /// <summary>
    ///     The most tags a single sense may carry.
    /// </summary>
    public const int MaxTagsPerSense = 16;

    /// <summary>
    ///     Determines whether a string is a valid tag: lower-case letters, digits and hyphens,
    ///     starting with a letter, at most <see cref="MaxTagLength"/> characters.
    /// </summary>
    /// <param name="tag">The candidate tag.</param>
    /// <returns>True when valid; false for null, empty or malformed strings (including a trailing newline).</returns>
    public static bool IsValidTag(string? tag)
    {
        return tag is { Length: > 0 and <= MaxTagLength } && TagPattern().IsMatch(tag);
    }

    /// <summary>
    ///     Validates the definition.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the name is empty, or the tags or the group are invalid, unsorted or duplicated.</exception>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(Name) || Tags is null || Tags.Count is 0 or > MaxTagsPerSense)
        {
            throw new ArgumentException($"A sense needs a name and between 1 and {MaxTagsPerSense} tags.");
        }

        for (var i = 0; i < Tags.Count; i++)
        {
            if (!IsValidTag(Tags[i]) || (i > 0 && string.CompareOrdinal(Tags[i - 1], Tags[i]) >= 0))
            {
                throw new ArgumentException($"Sense '{Name}' has an invalid, unsorted or duplicated tag.");
            }
        }

        if (Group is not null && !IsValidTag(Group))
        {
            throw new ArgumentException($"Sense '{Name}' has an invalid group; use lower-case letters, digits and hyphens.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
