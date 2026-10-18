using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     What the sense inventory says about one class: the trained lemma, the part of speech and the pronunciation meaning.
/// </summary>
/// <param name="Lemma">The trained lemma.</param>
/// <param name="Pos">The part of speech.</param>
/// <param name="Meaning">The pronunciation meaning, or null for a lemma that is not a heteronym.</param>
internal sealed record ClassFact(string Lemma, PartOfSpeech Pos, string? Meaning);

/// <summary>
///     The facts of the trained classes, by class id (<c>lemma#label</c>, the sense id of the public API).
/// </summary>
/// <remarks>
///     The evaluation and tuning tools judge an answer against the pronunciation meaning of a sense. The runtime no
///     longer carries that meaning, so the tools take it from the sense inventory, the authority on what the senses mean.
/// </remarks>
internal sealed class ClassFacts
{
    private readonly Dictionary<string, ClassFact> _facts;

    private ClassFacts(Dictionary<string, ClassFact> facts) => _facts = facts;

    /// <summary>
    ///     Builds the facts from sense inventory rows.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The facts.</returns>
    public static ClassFacts From(IEnumerable<InventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new ClassFacts(rows.ToDictionary(r => r.Lemma + "#" + r.Label, r => new ClassFact(r.Lemma, r.Pos, r.Meaning), StringComparer.Ordinal));
    }

    /// <summary>
    ///     Gets the trained lemma of an answer.
    /// </summary>
    /// <param name="details">The answer.</param>
    /// <returns>The lemma of the class, or the lemma of the answer when it names no trained class.</returns>
    public string LemmaOf(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details.SenseId is { } id && _facts.TryGetValue(id, out var fact) ? fact.Lemma : details.Lemma;
    }

    /// <summary>
    ///     Gets the pronunciation meaning of an answer.
    /// </summary>
    /// <param name="details">The answer.</param>
    /// <returns>The meaning of the class, or null when the answer names no trained class or the class has none.</returns>
    public string? MeaningOf(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details.SenseId is { } id && _facts.TryGetValue(id, out var fact) ? fact.Meaning : null;
    }
}
