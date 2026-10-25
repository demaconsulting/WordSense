using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     What a gold source says about one token beyond its part-of-speech tag: the lemma, the treebank tag and the
///     morphological features. It lets an evaluation score the tense of a verb, not only its part of speech.
/// </summary>
/// <param name="Lemma">The lemma of the token, or empty when the source gives none.</param>
/// <param name="Xpos">The language-specific tag of the source (for example <c>VBD</c>), or empty when the source has none.</param>
/// <param name="Features">The morphological features of the source in its own notation (<c>Tense=Past|VerbForm=Part</c>), or empty when the source has none.</param>
internal readonly record struct GoldToken(string Lemma, string Xpos, string Features);

/// <summary>
///     One sentence reduced to what the tagger needs: its tokens and the tag of each token.
/// </summary>
/// <param name="Words">The tokens as written; a contraction such as "don't" is one token.</param>
/// <param name="Tags">The tag of each token (see <see cref="PosTag"/>).</param>
/// <param name="Gold">What the source says about each token beyond its tag (parallel to <paramref name="Words"/>), or null when the source gives nothing but the tags.</param>
internal sealed record TaggedSentence(string[] Words, int[] Tags, GoldToken[]? Gold = null);

/// <summary>
///     The tag statistics that the basic model is trained from: the tag n-gram counts and the per-word tag counts of the
///     <c>train</c> split of the representative part of the open corpus. They are written to <c>corpus/open/counts</c> so
///     that training needs neither the corpus nor any other text.
/// </summary>
/// <remarks>
///     These are usage statistics, so they come only from the representative (random) part of the corpus, never from the
///     targeted part or from the balanced encoder corpus. The counts are CC0 1.0, like the annotations they are derived from.
/// </remarks>
internal sealed class TaggerCounts
{
    /// <summary>
    ///     The file name of the n-gram counts.
    /// </summary>
    public const string NgramsFile = "tag-ngrams.tsv";

    /// <summary>
    ///     The file name of the word counts.
    /// </summary>
    public const string WordsFile = "word-tags.tsv";

    /// <summary>
    ///     The file name of the clause n-gram counts: the bigram and trigram counts of the positions whose clause already
    ///     has a verb (see <see cref="PosTag.NextClause"/>).
    /// </summary>
    public const string ClauseNgramsFile = "tag-clause-ngrams.tsv";

    private const string WordsHeader = "word\tcounts";

    /// <summary>
    ///     Gets the unigram counts, indexed by tag with the boundary last.
    /// </summary>
    public int[] Unigrams { get; } = new int[PosTag.Extended];

    /// <summary>
    ///     Gets the bigram counts, row-major by (previous, current).
    /// </summary>
    public int[] Bigrams { get; } = new int[PosTag.Extended * PosTag.Extended];

    /// <summary>
    ///     Gets the trigram counts, row-major by (before previous, previous, current).
    /// </summary>
    public int[] Trigrams { get; } = new int[PosTag.Extended * PosTag.Extended * PosTag.Extended];

    /// <summary>
    ///     Gets the bigram counts of the positions whose clause already has a verb, row-major by (previous, current); the
    ///     counts of the other positions are the difference to <see cref="Bigrams"/>.
    /// </summary>
    public int[] ClauseBigrams { get; } = new int[PosTag.Extended * PosTag.Extended];

    /// <summary>
    ///     Gets the trigram counts of the positions whose clause already has a verb, row-major by (before previous,
    ///     previous, current); the counts of the other positions are the difference to <see cref="Trigrams"/>.
    /// </summary>
    public int[] ClauseTrigrams { get; } = new int[PosTag.Extended * PosTag.Extended * PosTag.Extended];

    /// <summary>
    ///     Gets the tag counts of every lower-case word that consists of letters and apostrophes.
    /// </summary>
    public SortedDictionary<string, int[]> Words { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Maps a universal part-of-speech tag of the open corpus to a tag of the tagger.
    /// </summary>
    /// <param name="upos">The universal part-of-speech tag.</param>
    /// <param name="form">The word as written; only the particle "to" is told apart.</param>
    /// <returns>The tag.</returns>
    public static int MapTag(string upos, string form) => upos switch
    {
        "NOUN" or "PROPN" => PosTag.Noun,
        "PRON" => PosTag.Pronoun,
        "VERB" => PosTag.Verb,
        "ADJ" => PosTag.Adjective,
        "ADV" => PosTag.Adverb,
        "ADP" => PosTag.Preposition,
        "CCONJ" or "SCONJ" => PosTag.Conjunction,
        "INTJ" => PosTag.Interjection,
        "DET" => PosTag.Determiner,
        "AUX" => PosTag.Auxiliary,
        "PART" => string.Equals(form, "to", StringComparison.OrdinalIgnoreCase) ? PosTag.InfinitiveTo : PosTag.Other,
        "PUNCT" => PosTag.Punctuation,
        _ => PosTag.Other,
    };

    /// <summary>
    ///     Gets the key of a token in the word table.
    /// </summary>
    /// <param name="word">The token as written.</param>
    /// <returns>The lower-case key, or null when the token is not made of letters and apostrophes or has no letter.</returns>
    public static string? KeyOf(string word)
    {
        var key = BasicClassifier.Lower(word);
        var hasLetter = false;
        foreach (var ch in key)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
            }
            else if (ch != '\'')
            {
                return null;
            }
        }

        return hasLetter ? key : null;
    }

    /// <summary>
    ///     Counts the tag statistics of sentences.
    /// </summary>
    /// <param name="sentences">The sentences.</param>
    /// <param name="lexicon">The lexicon: the analyses of a lower-case word, or the default array when unknown. It tells which sentence-initial words are non-finite verbs, which do not give their clause a verb.</param>
    /// <returns>The counts.</returns>
    public static TaggerCounts Count(IEnumerable<TaggedSentence> sentences, Func<string, ImmutableArray<WordDetails>> lexicon)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(lexicon);
        var counts = new TaggerCounts();
        foreach (var sentence in sentences)
        {
            var n = sentence.Tags.Length;
            var padded = new int[n + 3];
            padded[0] = PosTag.Boundary;
            padded[1] = PosTag.Boundary;
            padded[n + 2] = PosTag.Boundary;
            Array.Copy(sentence.Tags, 0, padded, 2, n);
            var hasVerb = 0;
            for (var i = 2; i < padded.Length; i++)
            {
                var bigram = (padded[i - 1] * PosTag.Extended) + padded[i];
                var trigram = (((padded[i - 2] * PosTag.Extended) + padded[i - 1]) * PosTag.Extended) + padded[i];
                counts.Unigrams[padded[i]]++;
                counts.Bigrams[bigram]++;
                counts.Trigrams[trigram]++;
                if (hasVerb != 0)
                {
                    counts.ClauseBigrams[bigram]++;
                    counts.ClauseTrigrams[trigram]++;
                }

                hasVerb = PosTag.NextClause(hasVerb, padded[i], i == 2 && n > 0 && StartsNonFinite(sentence.Words[0], lexicon));
            }

            for (var i = 0; i < n; i++)
            {
                if (KeyOf(sentence.Words[i]) is not { } key)
                {
                    continue;
                }

                if (!counts.Words.TryGetValue(key, out var row))
                {
                    row = new int[PosTag.Count];
                    counts.Words[key] = row;
                }

                row[sentence.Tags[i]]++;
            }
        }

        return counts;
    }

    /// <summary>
    ///     Tells whether the first word of a sentence is listed only as a non-finite verb, so that it does not give its
    ///     clause a verb (the rule that the tagger uses at run time).
    /// </summary>
    /// <param name="firstWord">The first word as written.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>True when the lexicon lists the word and every verb analysis of it is a gerund or a non-past participle.</returns>
    private static bool StartsNonFinite(string firstWord, Func<string, ImmutableArray<WordDetails>> lexicon)
    {
        var found = lexicon(BasicClassifier.Normalize(firstWord));
        return !found.IsDefaultOrEmpty && BasicClassifier.IsNonFiniteVerb(found);
    }

    /// <summary>
    ///     Writes the count files with LF line ends in a deterministic order.
    /// </summary>
    /// <param name="directory">The directory, created when missing.</param>
    public void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var inv = CultureInfo.InvariantCulture;
        var ngrams = new StringBuilder();
        ngrams.Append("order\ttags\tcount\n");
        AppendOrder(ngrams, 1, Unigrams, PosTag.Extended, inv);
        AppendOrder(ngrams, 2, Bigrams, PosTag.Extended, inv);
        AppendOrder(ngrams, 3, Trigrams, PosTag.Extended, inv);
        File.WriteAllText(Path.Join(directory, NgramsFile), ngrams.ToString(), new UTF8Encoding(false));

        var clause = new StringBuilder();
        clause.Append("order\ttags\tcount\n");
        AppendOrder(clause, 2, ClauseBigrams, PosTag.Extended, inv);
        AppendOrder(clause, 3, ClauseTrigrams, PosTag.Extended, inv);
        File.WriteAllText(Path.Join(directory, ClauseNgramsFile), clause.ToString(), new UTF8Encoding(false));

        var words = new StringBuilder();
        words.Append(WordsHeader).Append('\n');
        foreach (var (word, row) in Words)
        {
            words.Append(word).Append('\t');
            var first = true;
            for (var t = 0; t < PosTag.Count; t++)
            {
                if (row[t] == 0)
                {
                    continue;
                }

                words.Append(first ? string.Empty : ",").Append(PosTag.Letters[t]).Append(':').Append(row[t].ToString(inv));
                first = false;
            }

            words.Append('\n');
        }

        File.WriteAllText(Path.Join(directory, WordsFile), words.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Reads the count files that <see cref="Write"/> produced.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when a file is malformed.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a file is missing.</exception>
    public static TaggerCounts Read(string directory)
    {
        var counts = new TaggerCounts();
        var ngramsPath = Path.Join(directory, NgramsFile);
        var lines = File.ReadAllLines(ngramsPath, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != "order\ttags\tcount")
        {
            throw new InvalidDataException($"{ngramsPath}: unexpected header.");
        }

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length != 3 || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidDataException($"{ngramsPath}: malformed line '{line}'.");
            }

            var tags = fields[1].Split(' ').Select(TagOfLetter).ToArray();
            switch (fields[0])
            {
                case "1" when tags.Length == 1:
                    counts.Unigrams[tags[0]] = count;
                    break;
                case "2" when tags.Length == 2:
                    counts.Bigrams[(tags[0] * PosTag.Extended) + tags[1]] = count;
                    break;
                case "3" when tags.Length == 3:
                    counts.Trigrams[(((tags[0] * PosTag.Extended) + tags[1]) * PosTag.Extended) + tags[2]] = count;
                    break;
                default:
                    throw new InvalidDataException($"{ngramsPath}: malformed line '{line}'.");
            }
        }

        var wordsPath = Path.Join(directory, WordsFile);
        var wordLines = File.ReadAllLines(wordsPath, Encoding.UTF8);
        if (wordLines.Length == 0 || wordLines[0] != WordsHeader)
        {
            throw new InvalidDataException($"{wordsPath}: unexpected header.");
        }

        foreach (var line in wordLines.Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[0].Length == 0)
            {
                throw new InvalidDataException($"{wordsPath}: malformed line '{line}'.");
            }

            var row = new int[PosTag.Count];
            foreach (var part in fields[1].Split(','))
            {
                var pair = part.Split(':');
                var tag = pair.Length == 2 && pair[0].Length == 1 ? TagOfLetter(pair[0]) : -1;
                if (tag is < 0 or >= PosTag.Count || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out row[tag]))
                {
                    throw new InvalidDataException($"{wordsPath}: malformed line '{line}'.");
                }
            }

            if (!counts.Words.TryAdd(fields[0], row))
            {
                throw new InvalidDataException($"{wordsPath}: the word '{fields[0]}' appears twice.");
            }
        }

        var clausePath = Path.Join(directory, ClauseNgramsFile);
        var clauseLines = File.ReadAllLines(clausePath, Encoding.UTF8);
        if (clauseLines.Length == 0 || clauseLines[0] != "order\ttags\tcount")
        {
            throw new InvalidDataException($"{clausePath}: unexpected header.");
        }

        foreach (var line in clauseLines.Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length != 3 || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidDataException($"{clausePath}: malformed line '{line}'.");
            }

            var tags = fields[1].Split(' ').Select(TagOfLetter).ToArray();
            switch (fields[0])
            {
                case "2" when tags.Length == 2:
                    counts.ClauseBigrams[(tags[0] * PosTag.Extended) + tags[1]] = count;
                    break;
                case "3" when tags.Length == 3:
                    counts.ClauseTrigrams[(((tags[0] * PosTag.Extended) + tags[1]) * PosTag.Extended) + tags[2]] = count;
                    break;
                default:
                    throw new InvalidDataException($"{clausePath}: malformed line '{line}'.");
            }
        }

        if (ExceedsTotals(counts.ClauseBigrams, counts.Bigrams) || ExceedsTotals(counts.ClauseTrigrams, counts.Trigrams))
        {
            throw new InvalidDataException($"{clausePath}: a clause count exceeds its total count.");
        }

        return counts;
    }

    /// <summary>
    ///     Tells whether any clause count is larger than the total count at the same index.
    /// </summary>
    /// <param name="part">The clause counts.</param>
    /// <param name="whole">The total counts.</param>
    /// <returns>True when a clause count exceeds its total.</returns>
    private static bool ExceedsTotals(int[] part, int[] whole) => part.Where((count, i) => count > whole[i]).Any();

    private static void AppendOrder(StringBuilder text, int order, int[] counts, int width, IFormatProvider inv)
    {
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] == 0)
            {
                continue;
            }

            var tags = new char[order];
            var rest = index;
            for (var p = order - 1; p >= 0; p--)
            {
                tags[p] = PosTag.Letters[rest % width];
                rest /= width;
            }

            text.Append(order.ToString(inv)).Append('\t').Append(string.Join(' ', tags.Select(ch => ch.ToString()))).Append('\t').Append(counts[index].ToString(inv)).Append('\n');
        }
    }

    private static int TagOfLetter(string letter)
    {
        var index = letter.Length == 1 ? PosTag.Letters.IndexOf(letter[0], StringComparison.Ordinal) : -1;
        return index >= 0 ? index : throw new InvalidDataException($"'{letter}' is not a tag letter.");
    }
}
