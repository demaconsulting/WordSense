using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DemaConsulting.WordSense;

/// <summary>
///     The word lexicon: every known written form of English with its possible analyses, and the sense catalog.
/// </summary>
/// <remarks>
///     <para>
///     The lexicon is a compact binary image embedded in the assembly. It is opened on first use, needs no model files and
///     no network, and is searched in place: opening it takes a few tens of milliseconds and a few tens of megabytes less
///     than a dictionary of every word would, and a lookup allocates only its result. Every form is listed, so no
///     morphology runs at run time: <c>record</c>, <c>records</c> and <c>recorded</c> are all there.
///     </para>
///     <para>
///     <see cref="Lookup"/> and <see cref="TryLookup"/> take the word as written and answer the morphological question:
///     which analyses (lemma, part of speech and features) can this spelling have. They look for the exact spelling first
///     and, when there is none, for the lower-case spelling and the spellings that differ only in case, so
///     <c>Record</c> finds <c>record</c>; the curly apostrophe U+2019 counts as <c>'</c>. A possessive that is not stored
///     (<c>dog's</c>, <c>dogs'</c>) gets the noun and pronoun analyses of its stem with <c>Poss=Yes</c> added to the
///     features, for example <c>Number=Sing+Poss=Yes</c>. The contractions (<c>don't</c>, <c>it's</c>) are a closed list.
///     The reference tokenizer of the models keeps an apostrophe between letters inside the word, so <c>dog's</c> is one token and
///     reaches this lookup whole, but a trailing apostrophe is a token of its own (<c>dogs'</c> becomes <c>dogs</c> and <c>'</c>); a
///     caller that wants the plural possessive resolved has to pass <c>dogs'</c> as one word itself.
///     There is one analysis for each lemma, part of speech and features of a spelling, never one for each sense; the
///     analyses of a word are in a fixed order that is part of the lexicon (for a word with several analyses the first is
///     the most frequent) and use the ten values of <see cref="PartOfSpeech"/>. A capitalized or inflected spelling
///     behaves like its base form: <c>Polish</c>, <c>using</c> and <c>paid</c> have the analyses of their lemmas. A word
///     that is not in the lexicon (a number written with digits, most proper nouns or a misspelling) has no analysis.
///     </para>
///     <para>
///     <see cref="SensesOf"/> answers the semantic question: which senses do a lemma and part of speech have, in the order
///     of Open English WordNet (the position of the sense in its entry, which puts the commonly used senses first; the
///     project senses come last). A sense has a readable id (<c>bank#coin-bank</c>), an Open English WordNet sense key (none for a
///     project sense that WordNet does not cover) and optionally a group of closely related senses; find a sense by id
///     with <see cref="TryGetSense"/>, by key with <see cref="TryGetSenseByKey"/> and a group with
///     <see cref="TryGetGroup"/>. The lexicon is read-only and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // "read" has two verb analyses (past and present) and a noun analysis
///     foreach (WordDetails analysis in WordLexicon.Lookup("read"))
///     {
///         Console.WriteLine($"{analysis.Lemma}: {analysis.PartOfSpeech}, {analysis.Features}");
///     }
///
///     // The senses of an analysis, in the order of Open English WordNet
///     foreach (SenseInfo sense in WordLexicon.SensesOf("bank", PartOfSpeech.Noun).Take(3))
///     {
///         Console.WriteLine($"{sense.SenseId} {sense.SenseKey}");
///     }
///
///     // A word that is not in the lexicon has no analysis, so use TryLookup for text from users
///     if (WordLexicon.TryLookup("zyzzyva", out var analyses))
///     {
///         Console.WriteLine($"zyzzyva: {analyses.Length} analyses");
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
    ///     Gets an identifier of the sense inventory of the embedded lexicon: it changes whenever an analysis, a sense or
    ///     a group of the catalog changes, so a consumer that stores sense or group ids can detect that the catalog it
    ///     saw is not the one it runs against.
    /// </summary>
    /// <remarks>
    ///     The value is the first 16 hexadecimal digits (lower case) of the inventory hash of the image, the hash the
    ///     models are trained against. It is not a version number: compare for equality only.
    /// </remarks>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static string Version => Convert.ToHexString(Image.Value.InventoryHash[..8]).ToLowerInvariant();

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
    ///     Lists the senses of a lemma and part of speech.
    /// </summary>
    /// <param name="lemma">The lemma exactly as it is written in the lexicon (the <see cref="WordDetails.Lemma"/> of an analysis).</param>
    /// <param name="pos">The part of speech (the <see cref="WordDetails.PartOfSpeech"/> of an analysis).</param>
    /// <returns>The senses in inventory order, the order of Open English WordNet (the position of the sense in its entry; project senses last); empty when the lemma and part of speech have no sense.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static ImmutableArray<SenseInfo> SensesOf(string lemma, PartOfSpeech pos) => Image.Value.SensesOf(lemma, pos);

    /// <summary>
    ///     Finds a sense by its readable id.
    /// </summary>
    /// <param name="senseId">The id, <c>lemma#label</c>.</param>
    /// <param name="sense">Receives the sense; null when it is not found.</param>
    /// <returns><see langword="true"/> when the sense exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="senseId"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static bool TryGetSense(string senseId, [NotNullWhen(true)] out SenseInfo? sense)
    {
        var found = Image.Value.TryGetSense(senseId, out var info);
        sense = found ? info : null;
        return found;
    }

    /// <summary>
    ///     Finds a sense by its Open English WordNet sense key, the attach point for glossaries and thesauri that use WordNet keys.
    /// </summary>
    /// <param name="senseKey">The full sense key, for example <c>bow%1:06:02::</c>.</param>
    /// <param name="sense">Receives the sense; null when it is not found.</param>
    /// <returns><see langword="true"/> when a sense has the key; a project sense has no key and is never found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="senseKey"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static bool TryGetSenseByKey(string senseKey, [NotNullWhen(true)] out SenseInfo? sense)
    {
        var found = Image.Value.TryGetSenseByKey(senseKey, out var info);
        sense = found ? info : null;
        return found;
    }

    /// <summary>
    ///     Finds a group of closely related senses by its readable id.
    /// </summary>
    /// <param name="groupId">The id, <c>lemma#group:slug</c>. Group ids are opaque and versioned per data release.</param>
    /// <param name="group">Receives the group; null when it is not found.</param>
    /// <returns><see langword="true"/> when the group exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groupId"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown on first access when the embedded lexicon is missing or corrupt.</exception>
    public static bool TryGetGroup(string groupId, [NotNullWhen(true)] out SenseGroup? group)
    {
        var found = Image.Value.TryGetGroup(groupId, out var info);
        group = found ? info : null;
        return found;
    }

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
