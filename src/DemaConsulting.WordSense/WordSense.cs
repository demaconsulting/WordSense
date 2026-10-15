using System.Collections.Frozen;

namespace DemaConsulting.WordSense;

/// <summary>
///     A sense of an ambiguous lemma together with the tags that describe it.
/// </summary>
/// <remarks>
///     Exactly one instance exists per (lemma, sense) pair of a loaded table, so instances are compared
///     by reference and the same sense returned for two words is the same object. Instances are
///     immutable and thread-safe. Tags are lower-case ordinal strings such as <c>noun</c>, <c>verb</c>
///     or <c>past</c>; a sense may carry several (for example <c>past</c> and <c>verb</c>).
/// </remarks>
public sealed class WordSense
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSense"/> class.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sense">The sense label.</param>
    /// <param name="tags">The tags; already validated.</param>
    /// <param name="group">The group name, or null for a group of its own (the sense label).</param>
    internal WordSense(string lemma, string sense, IEnumerable<string> tags, string? group = null)
    {
        Lemma = lemma;
        Sense = sense;
        Tags = tags.ToFrozenSet(StringComparer.Ordinal);
        Group = string.IsNullOrEmpty(group) ? sense : group;
    }

    /// <summary>
    ///     Gets the lower-case lemma, for example <c>record</c>.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the sense label, unique within the lemma, for example <c>noun</c> or <c>bend</c>.
    /// </summary>
    public string Sense { get; }

    /// <summary>
    ///     Gets the group of the sense: senses that share a meaning but differ in part of speech share a group
    ///     (for example <c>bend-noun</c> and <c>bend-verb</c> in group <c>bend</c>). It is the sense label
    ///     when the sense has no explicit group, and is never null or empty.
    /// </summary>
    public string Group { get; }

    /// <summary>
    ///     Gets the tags of the sense; a frozen, ordinal set that is never empty.
    /// </summary>
    public IReadOnlySet<string> Tags { get; }

    /// <summary>
    ///     Determines whether the sense carries a tag.
    /// </summary>
    /// <param name="tag">The tag, compared ordinally (case-sensitive).</param>
    /// <returns>True when the sense carries the tag; false for any other tag, including unknown tags.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tag"/> is null.</exception>
    public bool HasTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return Tags.Contains(tag);
    }

    /// <summary>
    ///     Returns the lemma and sense, for example <c>record:noun</c>.
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Lemma + ":" + Sense;
}
