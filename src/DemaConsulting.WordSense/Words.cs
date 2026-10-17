using System.Collections.Frozen;

namespace DemaConsulting.WordSense;

/// <summary>
///     The word table: every known word, spelled as written in lower case, with its possible senses.
/// </summary>
/// <remarks>
///     <para>
///     The table is embedded in the assembly and loaded on first use; it needs no model files and no network. A key is
///     a surface form: a lemma or an inflection of one (<c>record</c>, <c>records</c>, <c>recorded</c>). Every sense in
///     the array of a key has the same lemma. A key with one sense is unambiguous; a key with several senses (a noun and
///     a verb, or the meanings of a heteronym) needs context, which a model from one of the model packages provides.
///     </para>
///     <para>
///     The senses of a lemma appear in a fixed order that is part of the table; the table is read-only and safe for
///     concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     foreach (WordDetails sense in Words.Find("records"))
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///     }
///
///     bool known = Words.Entries.ContainsKey("record");
///     </code>
/// </example>
public static class Words
{
    private const string ResourceName = "DemaConsulting.WordSense.words.bin";

    private static readonly WordDetails[] NoSenses = [];

    private static readonly Lazy<FrozenDictionary<string, WordDetails[]>> Table =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets every known word with its possible senses.
    /// </summary>
    /// <remarks>
    ///     The first access loads and indexes the embedded table.
    /// </remarks>
    public static FrozenDictionary<string, WordDetails[]> Entries => Table.Value;

    /// <summary>
    ///     Looks a word up.
    /// </summary>
    /// <param name="word">The word; it is lower-cased with the invariant culture, and the curly apostrophe (U+2019) is read as <c>'</c>.</param>
    /// <returns>The senses of the word, or an empty array when the word is unknown.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="word"/> is null.</exception>
    public static WordDetails[] Find(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return Entries.TryGetValue(Normalize(word), out var senses) ? senses : NoSenses;
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lookup key.</returns>
    internal static string Normalize(string word) => word.ToLowerInvariant().Replace('\u2019', '\'');

    /// <summary>
    ///     Loads the embedded table.
    /// </summary>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing or corrupt.</exception>
    private static FrozenDictionary<string, WordDetails[]> Load()
    {
        using var stream = typeof(Words).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded word table is missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return WordsBlob.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
