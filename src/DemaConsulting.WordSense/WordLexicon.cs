using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     The word lexicon: every known written form of English with its possible analyses.
/// </summary>
/// <remarks>
///     <para>
///     The lexicon is a compact binary image embedded in the assembly. It is opened on first use, needs no model files and
///     no network, and is searched in place: opening it takes a few tens of milliseconds and a few tens of megabytes less
///     than a dictionary of every word would, and a lookup allocates only its result. Every form is listed, so no
///     morphology runs at run time: <c>record</c>, <c>records</c> and <c>recorded</c> are all there.
///     </para>
///     <para>
///     <see cref="Lookup"/> and <see cref="TryLookup"/> take the word as written. They look for the exact spelling first
///     and, when there is none, for the lower-case spelling and the spellings that differ only in case, so
///     <c>Record</c> finds <c>record</c>; the curly apostrophe U+2019 counts as <c>'</c>. The analyses of a word are in a
///     fixed order that is part of the lexicon (for a word with several analyses the first is the most frequent) and
///     use the ten values of <see cref="PartOfSpeech"/>. A word that is not in the lexicon (a number written with digits, most proper
///     nouns or a misspelling) has no analysis. The lexicon is read-only and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // "read" has analyses with the senses "read#past" and "read#present"
///     foreach (WordDetails sense in WordLexicon.Lookup("read"))
///     {
///         Console.WriteLine($"{sense.SenseId ?? "-"}: {sense.Lemma}, {sense.PartOfSpeech}, {sense.Features}");
///     }
///
///     // A word that is not in the lexicon has no analysis, so use TryLookup for text from users
///     if (WordLexicon.TryLookup("zyzzyva", out var senses))
///     {
///         Console.WriteLine($"zyzzyva: {senses.Length} analyses");
///     }
///     else
///     {
///         Console.WriteLine("zyzzyva is unknown");
///     }
///     </code>
/// </example>
public static class WordLexicon
{
    private const string ResourceName = "DemaConsulting.WordSense.wordsense.img";

    private const int CacheSize = 2048;

    private static readonly CacheEntry?[] Cache = new CacheEntry?[CacheSize];

    private static readonly Lazy<WordImage> Image = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets the number of distinct written forms in the lexicon.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static int Count => Image.Value.SpellingCount;

    /// <summary>
    ///     Looks a word up.
    /// </summary>
    /// <param name="word">The word as written; it is matched exactly first, then without regard to case.</param>
    /// <param name="senses">The analyses of the word in answer order, or the default array when the word is unknown.</param>
    /// <returns><see langword="true"/> when the word is in the lexicon.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="word"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static bool TryLookup(string word, out ImmutableArray<WordDetails> senses)
    {
        ArgumentNullException.ThrowIfNull(word);

        // A small direct-mapped cache of recent answers: the models ask for the same words repeatedly and a lookup
        // decodes and allocates. Entries are immutable, so a racing writer can only replace one valid entry with another.
        var slot = (word.GetHashCode() & int.MaxValue) % CacheSize;
        var cached = Volatile.Read(ref Cache[slot]);
        if (cached is not null && string.Equals(cached.Key, word, StringComparison.Ordinal))
        {
            senses = cached.Senses;
            return cached.Found;
        }

        var found = Image.Value.TryLookup(word, out senses);
        Volatile.Write(ref Cache[slot], new CacheEntry(word, senses, found));
        return found;
    }

    /// <summary>
    ///     Looks a word up and returns an empty array when it is unknown.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The analyses of the word in answer order; empty for an unknown word.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="word"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static ImmutableArray<WordDetails> Lookup(string word) => TryLookup(word, out var senses) ? senses : [];

    /// <summary>
    ///     Determines whether a word is in the lexicon.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns><see langword="true"/> when <see cref="TryLookup"/> would find it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="word"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static bool Contains(string word) => TryLookup(word, out _);

    /// <summary>
    ///     Gets every written form of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma exactly as it is written in the lexicon (the <see cref="WordDetails.Lemma"/> of an analysis).</param>
    /// <returns>The forms (the lemma itself included) in spelling order; empty for an unknown lemma.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static IReadOnlyList<string> FormsOf(string lemma) => Image.Value.FormsOf(lemma);

    /// <summary>
    ///     Gets the opened image (for tests and tools of this repository).
    /// </summary>
    internal static WordImage Current => Image.Value;

    private static WordImage Load()
    {
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded word image is missing from the assembly.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return WordImage.Open(data);
    }

    private sealed record CacheEntry(string Key, ImmutableArray<WordDetails> Senses, bool Found);
}
