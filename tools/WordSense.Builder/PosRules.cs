namespace WordSense.Builder;

/// <summary>
///     The part-of-speech honesty rule of the sense inventory: a sense carries exactly one true part of speech.
/// </summary>
/// <remarks>
///     A sense's tags are one part of speech from <see cref="PartsOfSpeech"/> plus optional tense or degree tags
///     from <see cref="Qualifiers"/>. Meaning or pronunciation stand-ins (such as <c>music</c>) are never tags.
/// </remarks>
internal static class PosRules
{
    /// <summary>
    ///     The parts of speech a sense can have.
    /// </summary>
    public static readonly string[] PartsOfSpeech = ["adj", "adv", "noun", "verb"];

    /// <summary>
    ///     The tense and degree tags allowed next to the part of speech.
    /// </summary>
    public static readonly string[] Qualifiers = ["comparative", "participle", "past", "present", "superlative"];

    /// <summary>
    ///     Checks every sense of the inventory.
    /// </summary>
    /// <param name="inventory">The inventory.</param>
    /// <returns>One message per violation.</returns>
    public static List<string> Validate(SenseInventory inventory)
    {
        var errors = new List<string>();
        foreach (var row in inventory.Rows)
        {
            var pos = row.Tags.Count(t => PartsOfSpeech.Contains(t));
            var other = row.Tags.Where(t => !PartsOfSpeech.Contains(t) && !Qualifiers.Contains(t)).ToList();
            if (pos != 1)
            {
                errors.Add($"senses/{row.Source}: sense '{row.Lemma}/{row.Sense}' must have exactly one part of speech tag (noun, verb, adj, adv) but has {pos}.");
            }

            if (other.Count > 0)
            {
                errors.Add($"senses/{row.Source}: sense '{row.Lemma}/{row.Sense}' has non part-of-speech tags: {string.Join(", ", other)}.");
            }
        }

        return errors;
    }
}
