using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense;

/// <summary>
///     The definition of one sense of a lemma: its label and its tags.
/// </summary>
/// <param name="Name">The sense label, unique within the lemma.</param>
/// <param name="Tags">The tags: at least one, lower-case, sorted ordinally and without duplicates.</param>
/// <param name="Group">The group the sense belongs to, or null for a group of its own (named like the sense).</param>
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
    /// <exception cref="ArgumentException">Thrown when the name is empty or the tags are invalid, unsorted or duplicated.</exception>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(Name) || Tags is null || Tags.Count is 0 or > MaxTagsPerSense)
        {
            throw new ArgumentException("A sense needs a name and between 1 and 16 tags.");
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
