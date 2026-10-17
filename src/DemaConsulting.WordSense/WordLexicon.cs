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
///     Keys are lower case and compared ordinally; lower-case a word yourself (<c>ToLowerInvariant()</c>) before looking
///     it up, and replace the curly apostrophe U+2019 with <c>'</c> (the classifiers do both for you). The indexer
///     <c>Words["x"]</c> throws <see cref="KeyNotFoundException"/> for a word that is not in the lexicon (and for a
///     word that is not lower case), so use <c>TryGetValue</c> for text from users. The senses of a lemma appear in a
///     fixed order that is part of the lexicon. The lexicon is read-only
///     and safe for concurrent use. The first access takes about 120 ms and about 43 MiB (approximate, machine
///     dependent) and happens once per process.
///     </para>
///     <para>
///     The senses of a word use the eight values of <see cref="PartOfSpeech"/>: <see cref="PartOfSpeech.Noun"/>,
///     <see cref="PartOfSpeech.Pronoun"/>, <see cref="PartOfSpeech.Verb"/>, <see cref="PartOfSpeech.Adjective"/>,
///     <see cref="PartOfSpeech.Adverb"/>, <see cref="PartOfSpeech.Preposition"/>, <see cref="PartOfSpeech.Conjunction"/>
///     and <see cref="PartOfSpeech.Interjection"/>. A word that is not in the lexicon (a determiner, a number, most
///     proper nouns or a misspelling) is simply missing from <see cref="Words"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // "read" has two entries, both verbs: the present (REED) and the past (RED); every sense has the lemma "read"
///     WordDetails[] read = WordLexicon.Words["read"];
///     foreach (WordDetails sense in read)
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}, meaning {sense.Meaning}, respelling {sense.Respelling}");
///     }
///
///     // A word that is not in the lexicon is missing from the dictionary, so use TryGetValue
///     if (WordLexicon.Words.TryGetValue("zyzzyva", out WordDetails[]? senses))
///     {
///         Console.WriteLine($"zyzzyva: {senses.Length} senses");
///     }
///     else
///     {
///         Console.WriteLine("zyzzyva is unknown");
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
    ///     The keys are lower-case surface forms compared ordinally (no case folding, so <c>Record</c> is not found); a
    ///     key never contains the curly apostrophe U+2019, which is normalized to <c>'</c>. The indexer throws
    ///     <see cref="KeyNotFoundException"/> for a missing word; use <c>TryGetValue</c>. The first access loads and
    ///     indexes the embedded lexicon; later accesses return the same instance.
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
