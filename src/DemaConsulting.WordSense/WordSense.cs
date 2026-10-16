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
///     <para>
///     <b>Sense or tag?</b> A <em>tag</em> answers a coarse question about the word ("is this a verb?") and
///     is answered by <see cref="IWordSenseClassifier.ClassifyTagAsync"/> and
///     <see cref="IWordSenseClassifier.IsTagAsync"/>, which compare tags rather than individual senses, so they do not
///     abstain merely because a lemma is split into several senses of the same part of speech. A <em>sense</em>
///     answers a fine question ("which meaning, and therefore which group?") and is answered by
///     <see cref="IWordSenseClassifier.ClassifyAsync(IReadOnlyList{string}, CancellationToken)"/>. Contrast for
///     <c>lead</c>, whose senses are <c>guide-noun</c> (tag <c>noun</c>, group <c>guide</c>), <c>guide-verb</c>
///     (tag <c>verb</c>, group <c>guide</c>) and <c>metal</c> (tag <c>noun</c>, group <c>metal</c>): the tag question
///     "noun or verb?" cannot tell the guide noun from the metal because both are nouns, whereas the sense (or its
///     group) can.
///     </para>
///     <para>
///     <b>Groups and pronunciation.</b> A <see cref="Group"/> is an equivalence class of the senses of one lemma:
///     senses in the same group share a pronunciation, so a consumer that only needs the pronunciation cannot and
///     need not tell them apart. A group is not a coarser meaning and does not mean "same meaning across parts of
///     speech". The library has <em>no pronunciation data</em>; it does not know how any word is spoken, and
///     <see cref="Group"/> is its only equivalence mechanism. Which pronunciation belongs to a group is the
///     consumer's knowledge. A consumer such as Speech therefore keeps a table keyed by
///     (<see cref="Lemma"/>, <see cref="Group"/>) and looks up the group of the classified sense. Because a
///     sense without an explicit group is a group of its own named like the sense, the same lookup also works for
///     lemmas that have no explicit groups (the key is then effectively the sense). Groups cut across parts of
///     speech: <c>lead</c> has the senses <c>guide-noun</c> and <c>guide-verb</c> in group <c>guide</c> (pronounced
///     like "leed") and the sense <c>metal</c> in group <c>metal</c> (pronounced like "led"), so the two noun senses
///     are in different groups while a noun and a verb share one.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordSense?[] senses = await classifier.ClassifyAsync(["They", "will", "record", "it"]);
///
///     if (senses[2] is { } sense)
///     {
///         Console.WriteLine($"{sense.Lemma} -> {sense.Sense} (group {sense.Group})");
///         bool isVerb = sense.HasTag("verb"); // tags are case-sensitive
///     }
///     </code>
///     A consumer that has pronunciations of its own (this library has none) keys them by lemma and group, so
///     the guide senses of <c>lead</c> (noun or verb) share one entry and the metal has another:
///     <code>
///     var pronunciations = new Dictionary&lt;(string Lemma, string Group), string&gt;
///     {
///         [("lead", "guide")] = "leed",
///         [("lead", "metal")] = "led",
///     };
///
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordSense?[] senses = await classifier.ClassifyAsync(["A", "lead", "pipe", "leaks"]);
///     if (senses[1] is { } lead &amp;&amp; pronunciations.TryGetValue((lead.Lemma, lead.Group), out var spoken))
///     {
///         Console.WriteLine($"{lead.Sense} is pronounced {spoken}");
///     }
///     </code>
/// </example>
public sealed class WordSense
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSense"/> class.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sense">The sense name.</param>
    /// <param name="tags">The tags; already validated.</param>
    /// <param name="group">The group name, or null for a group of its own (the sense name).</param>
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
    ///     Gets the sense name (the <see cref="SenseDefinition.Name"/> it was built from), unique within the
    ///     lemma, for example <c>noun</c> or <c>bend</c>.
    /// </summary>
    public string Sense { get; }

    /// <summary>
    ///     Gets the group of the sense: the equivalence class of senses of the lemma that share a pronunciation
    ///     (for example <c>bend-noun</c> and <c>bend-verb</c> of <c>bow</c> are both in group <c>bend</c>, while its
    ///     <c>weapon</c> sense is in group <c>weapon</c>). It is the sense name when the sense has no explicit group,
    ///     and is never null or empty. An explicit group is validated like a tag (lower-case letters, digits and
    ///     hyphens, starting with a letter).
    /// </summary>
    /// <remarks>
    ///     The library has no pronunciation data and <see cref="Group"/> is its only equivalence mechanism: a
    ///     consumer maps (<see cref="Lemma"/>, <see cref="Group"/>) to a pronunciation of its own, which also covers
    ///     senses without an explicit group because their group equals their sense name. See the remarks of
    ///     <see cref="WordSense"/> for the <c>lead</c> example (groups <c>guide</c> and <c>metal</c>).
    /// </remarks>
    public string Group { get; }

    /// <summary>
    ///     Gets the tags of the sense; a frozen, ordinal set that is never empty.
    /// </summary>
    /// <remarks>
    ///     The shipped vocabulary is: the part-of-speech tags <c>noun</c>, <c>verb</c>, <c>adj</c> and <c>adv</c>
    ///     (every sense has exactly one) and the qualifier tags <c>past</c>, <c>present</c>, <c>comparative</c>,
    ///     <c>participle</c> and <c>superlative</c>. The last two are valid but no shipped sense carries them
    ///     (<see cref="SenseDefinition.IsValidTag"/> accepts any well-formed tag, so custom tables may use others). The
    ///     senses of every lemma are listed in the user guide appendix
    ///     https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md.
    /// </remarks>
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
