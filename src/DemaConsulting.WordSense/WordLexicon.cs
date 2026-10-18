using System.Collections;
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
    public static bool TryLookup(string word, out ImmutableArray<WordDetails> senses) => Image.Value.TryLookup(word, out senses);

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

    /// <summary>
    ///     Gets the lexicon as a read-only dictionary of exact spellings, for the models of this repository.
    /// </summary>
    internal static IReadOnlyDictionary<string, ImmutableArray<WordDetails>> View { get; } = new WordTable();

    /// <summary>
    ///     Gets the pronunciation meaning that the models of this repository still use as a class key.
    /// </summary>
    /// <remarks>Legacy bridge; removed when the model weights are keyed by sense id.</remarks>
    /// <param name="details">The analysis.</param>
    /// <returns>The meaning of the sense, or null.</returns>
    internal static string? MeaningOf(WordDetails details) => details.SenseId is null ? null : Image.Value.MeaningOf(details.SenseId);

    /// <summary>
    ///     Gets the lemma that the weights of the models know a sense by.
    /// </summary>
    /// <remarks>Legacy bridge; removed when the model weights are keyed by sense id.</remarks>
    /// <param name="details">The analysis.</param>
    /// <returns>The trained lemma of the sense, or the lemma of the analysis when it has no sense.</returns>
    internal static string TrainedLemmaOf(WordDetails details) => details.SenseId is null ? details.Lemma : WordImage.TrainedLemmaOf(details.SenseId);

    /// <summary>
    ///     Gets the classes of a trained lemma as analyses, in class order.
    /// </summary>
    /// <remarks>Legacy bridge; removed when the model weights are keyed by sense id.</remarks>
    /// <param name="trainedLemma">The lemma the classes are named after.</param>
    /// <returns>One analysis per class; empty when the lemma has no class.</returns>
    internal static ImmutableArray<WordDetails> ClassesOf(string trainedLemma)
    {
        var image = Image.Value;
        var builder = ImmutableArray.CreateBuilder<WordDetails>();
        foreach (var id in image.ClassesOf(trainedLemma))
        {
            builder.Add(new WordDetails(id, trainedLemma, image.ClassPos(id) ?? PartOfSpeech.Noun, string.Empty));
        }

        return builder.ToImmutable();
    }

    private static WordImage Load()
    {
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded word image is missing from the assembly.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return WordImage.Open(data);
    }

    private sealed record CacheEntry(string Key, ImmutableArray<WordDetails> Senses, bool Found);

    private sealed class WordTable : IReadOnlyDictionary<string, ImmutableArray<WordDetails>>
    {
        private const int CacheSize = 2048;

        private readonly CacheEntry?[] _cache = new CacheEntry?[CacheSize];

#pragma warning disable S3218 // Count is the member of the dictionary interface the adapter implements
        public int Count => Image.Value.SpellingCount;
#pragma warning restore S3218

        public IEnumerable<string> Keys => Enumerable.Range(0, Count).Select(Image.Value.Spelling);

        public IEnumerable<ImmutableArray<WordDetails>> Values => Enumerable.Range(0, Count).Select(Image.Value.Analyses);

        public ImmutableArray<WordDetails> this[string key] =>
            TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"The word '{key}' is not in the lexicon.");

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, out ImmutableArray<WordDetails> value)
        {
            // A small direct-mapped cache of recent answers: the models ask for the same words repeatedly and a lookup
            // decodes and allocates. Entries are immutable, so a racing writer can only replace one valid entry with another.
            var slot = (key.GetHashCode() & int.MaxValue) % CacheSize;
            var cached = Volatile.Read(ref _cache[slot]);
            if (cached is not null && string.Equals(cached.Key, key, StringComparison.Ordinal))
            {
                value = cached.Senses;
                return cached.Found;
            }

            var found = Image.Value.TryLookup(key, out value);
            Volatile.Write(ref _cache[slot], new CacheEntry(key, value, found));
            return found;
        }

        public IEnumerator<KeyValuePair<string, ImmutableArray<WordDetails>>> GetEnumerator()
        {
            var image = Image.Value;
            for (var i = 0; i < image.SpellingCount; i++)
            {
                yield return new KeyValuePair<string, ImmutableArray<WordDetails>>(image.Spelling(i), image.Analyses(i));
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
