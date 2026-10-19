using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder;

/// <summary>
///     One sentence of the Universal Dependencies treebank reduced to what the tagger needs.
/// </summary>
/// <param name="Words">The tokens as written; a multi-word token such as "don't" is one token.</param>
/// <param name="Tags">The tag of each token (see <see cref="PosTag"/>).</param>
internal sealed record UdSentence(string[] Words, int[] Tags);

/// <summary>
///     The tag statistics of the treebank that the basic model is trained from: the tag n-gram counts and the per-word
///     tag counts. They are written to <c>corpus/ud</c> so that training does not need the treebank itself.
/// </summary>
internal sealed class UdCounts
{
    /// <summary>
    ///     The file name of the n-gram counts.
    /// </summary>
    public const string NgramsFile = "tag-ngrams.tsv";

    /// <summary>
    ///     The file name of the word counts.
    /// </summary>
    public const string WordsFile = "word-tags.tsv";

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
    ///     Gets the tag counts of every lower-case word that consists of letters and apostrophes.
    /// </summary>
    public SortedDictionary<string, int[]> Words { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Maps a Universal Dependencies part-of-speech tag to a tag of the tagger.
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
    ///     Reads the sentences of a CoNLL-U file.
    /// </summary>
    /// <remarks>
    ///     A multi-word token (an id range such as <c>1-2</c>) is one token, tagged like its first word; empty nodes are skipped.
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<UdSentence> ReadConllu(string path)
    {
        var sentences = new List<UdSentence>();
        var words = new List<string>();
        var tags = new List<int>();
        string? pendingForm = null;
        var pendingEnd = 0;
        var skipThrough = 0;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (line.Length == 0)
            {
                if (words.Count > 0)
                {
                    sentences.Add(new UdSentence([.. words], [.. tags]));
                    words.Clear();
                    tags.Clear();
                }

                skipThrough = 0;
                continue;
            }

            if (line[0] == '#')
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 4)
            {
                throw new InvalidDataException($"{path}({lineNumber.ToString(CultureInfo.InvariantCulture)}): a CoNLL-U line needs at least four fields.");
            }

            var id = fields[0];
            if (id.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            var dash = id.IndexOf('-', StringComparison.Ordinal);
            if (dash > 0)
            {
                pendingForm = fields[1];
                pendingEnd = ParseId(id[(dash + 1)..], path, lineNumber);
                continue;
            }

            var number = ParseId(id, path, lineNumber);
            if (pendingForm is not null)
            {
                words.Add(pendingForm);
                tags.Add(MapTag(fields[3], pendingForm));
                skipThrough = pendingEnd;
                pendingForm = null;
            }
            else if (number > skipThrough)
            {
                words.Add(fields[1]);
                tags.Add(MapTag(fields[3], fields[1]));
            }
        }

        if (words.Count > 0)
        {
            sentences.Add(new UdSentence([.. words], [.. tags]));
        }

        return sentences;
    }

    /// <summary>
    ///     Counts the tag statistics of sentences.
    /// </summary>
    /// <param name="sentences">The sentences.</param>
    /// <returns>The counts.</returns>
    public static UdCounts Count(IEnumerable<UdSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var counts = new UdCounts();
        foreach (var sentence in sentences)
        {
            var n = sentence.Tags.Length;
            var padded = new int[n + 3];
            padded[0] = PosTag.Boundary;
            padded[1] = PosTag.Boundary;
            padded[n + 2] = PosTag.Boundary;
            Array.Copy(sentence.Tags, 0, padded, 2, n);
            for (var i = 2; i < padded.Length; i++)
            {
                counts.Unigrams[padded[i]]++;
                counts.Bigrams[(padded[i - 1] * PosTag.Extended) + padded[i]]++;
                counts.Trigrams[(((padded[i - 2] * PosTag.Extended) + padded[i - 1]) * PosTag.Extended) + padded[i]]++;
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
    public static UdCounts Read(string directory)
    {
        var counts = new UdCounts();
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

        return counts;
    }

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

    private static int ParseId(string text, string path, int line) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{path}({line.ToString(CultureInfo.InvariantCulture)}): '{text}' is not a token id.");
}

/// <summary>
///     The pinned source of the treebank files: the exact commit and checksums that the vendored counts were made from.
/// </summary>
internal static class UdSource
{
    /// <summary>
    ///     The commit of the UD_English-EWT repository that is pinned.
    /// </summary>
    public const string Commit = "4a4d77f599ea53cc405f85d0cec4b2f14f81d42b";

    /// <summary>
    ///     The pinned files by name with their SHA-256 checksums (upper-case hexadecimal). The test file is deliberately absent.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en_ewt-ud-train.conllu"] = "D68E06122A702464C613076523D56740F047E5BBE89DD90EC32737E04D952143",
        ["en_ewt-ud-dev.conllu"] = "39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8",
    };

    /// <summary>
    ///     Gets the download address of a pinned file.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The address.</returns>
    public static Uri UrlOf(string name) =>
        new($"https://raw.githubusercontent.com/UniversalDependencies/UD_English-EWT/{Commit}/{name}");

    /// <summary>
    ///     Checks a file against its pinned checksum.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="expectedSha256">The expected checksum, hexadecimal.</param>
    /// <exception cref="InvalidDataException">Thrown when the checksum differs.</exception>
    public static void Verify(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{path}: the SHA-256 {actual} differs from the pinned {expectedSha256}.");
        }
    }

    /// <summary>
    ///     Makes sure the pinned files are in a directory: missing files are downloaded, and every file is verified.
    /// </summary>
    /// <param name="directory">The directory, created when missing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when every file is present and verified.</returns>
    /// <exception cref="InvalidDataException">Thrown when a file differs from its pinned checksum.</exception>
    /// <exception cref="HttpRequestException">Thrown when a download fails.</exception>
    public static async Task EnsureAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        using var client = new HttpClient();
        foreach (var (name, sha) in Files)
        {
            var path = Path.Join(directory, name);
            if (!File.Exists(path))
            {
                var bytes = await client.GetByteArrayAsync(UrlOf(name), cancellationToken).ConfigureAwait(false);
                var partial = path + ".tmp";
                await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
                try
                {
                    Verify(partial, sha);
                }
                catch (InvalidDataException)
                {
                    File.Delete(partial);
                    throw;
                }

                File.Move(partial, path, true);
            }

            Verify(path, sha);
        }
    }
}
