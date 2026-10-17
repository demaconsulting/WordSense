using System.Collections.Frozen;

namespace DemaConsulting.WordSense;

/// <summary>
///     The word lexicon: every known word, spelled as written in lower case, with its possible senses.
/// </summary>
/// <remarks>
///     <para>
///     The lexicon is embedded in the assembly and loaded on first use; it needs no model files and no network. A key is
///     a lower-case surface form: a lemma or an inflection of one (<c>record</c>, <c>records</c>, <c>recorded</c>); the
///     inflected forms are generated when the lexicon is built, so no morphology runs at run time. Every sense in the
///     array of a key has the same lemma. A key with one sense is unambiguous; a key with several senses (a noun and a
///     verb, or the meanings of a heteronym) needs context, which a model from one of the model packages provides.
///     </para>
///     <para>
///     Keys are lower case; lower-case a word yourself before looking it up (and read the curly apostrophe U+2019 as
///     <c>'</c>). The senses of a lemma appear in a fixed order that is part of the lexicon. The lexicon is read-only
///     and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     if (WordLexicon.Words.TryGetValue("records", out WordDetails[]? senses))
///     {
///         foreach (WordDetails sense in senses)
///         {
///             Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///         }
///     }
///     </code>
/// </example>
public static class WordLexicon
{
    private const string ResourceName = "DemaConsulting.WordSense.words.bin";

    private static readonly Lazy<FrozenDictionary<string, WordDetails[]>> Table =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets every known word with its possible senses.
    /// </summary>
    /// <remarks>
    ///     The keys are lower-case surface forms compared ordinally. The first access loads and indexes the embedded
    ///     lexicon; later accesses return the same instance.
    /// </remarks>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static FrozenDictionary<string, WordDetails[]> Words => Table.Value;

    /// <summary>
    ///     Loads the embedded lexicon.
    /// </summary>
    /// <returns>The lexicon.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing or corrupt.</exception>
    private static FrozenDictionary<string, WordDetails[]> Load()
    {
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded word lexicon is missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return WordsBlob.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
