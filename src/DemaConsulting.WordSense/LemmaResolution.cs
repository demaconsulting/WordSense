namespace DemaConsulting.WordSense;

/// <summary>
///     The outcome of resolving a surface word to a lemma entry.
/// </summary>
/// <param name="Entry">The entry of the lemma the word belongs to.</param>
/// <param name="SenseIndex">
///     <c>-1</c> when every sense of <paramref name="Entry"/> applies; otherwise the index in
///     <see cref="LemmaEntry.Senses"/> of the one sense the resolution narrowed the word to.
/// </param>
internal readonly record struct LemmaResolution(LemmaEntry Entry, int SenseIndex)
{
    /// <summary>
    ///     Gets a value indicating whether the resolution narrowed the entry to a single sense.
    /// </summary>
    public bool IsNarrowed => SenseIndex >= 0;
}
