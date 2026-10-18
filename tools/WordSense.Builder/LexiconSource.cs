using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Reads the dictionary word list of the corpus (<c>corpus/lexicon/lexicon.tsv</c>): a word and the set of parts of
///     speech it can have.
/// </summary>
internal static class LexiconSource
{
    /// <summary>
    ///     The letters of the parts of speech, in the order of <see cref="PartOfSpeech"/>.
    /// </summary>
    public const string Letters = "NRVJBPCIDA";

    /// <summary>
    ///     The name of the closed, hand-written list of auxiliary verbs.
    /// </summary>
    public const string AuxiliariesFile = "auxiliaries.tsv";

    /// <summary>
    ///     Reads the closed list of auxiliary verbs: one lower-case word per line, then a tab and the evidence
    ///     (<c>modal</c> or <c>primary</c>); blank lines and lines starting with <c>#</c> are skipped.
    /// </summary>
    /// <param name="path">The auxiliaries file.</param>
    /// <returns>The distinct words in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed or a word is repeated.</exception>
    public static List<string> ReadAuxiliaries(string path)
    {
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            var where = $"{AuxiliariesFile} line {number}";
            if (columns.Length != 2 || columns[0].Length == 0 || !columns[0].All(c => c is >= 'a' and <= 'z') ||
                columns[1] is not ("modal" or "primary"))
            {
                throw new InvalidDataException($"{where}: expected 'word<TAB>modal|primary' with a word of the letters a-z.");
            }

            if (!seen.Add(columns[0]))
            {
                throw new InvalidDataException($"{where}: the word '{columns[0]}' is listed twice.");
            }

            words.Add(columns[0]);
        }

        return words;
    }

    /// <summary>
    ///     Gets the part-of-speech mask bit of a part of speech.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The bit.</returns>
    public static ushort Bit(PartOfSpeech pos) => (ushort)(1 << (int)pos);

    /// <summary>
    ///     The name of the lexicon file.
    /// </summary>
    public const string LexiconFile = "lexicon.tsv";

    /// <summary>
    ///     The name of the file that enables or disables the morphology rules.
    /// </summary>
    public const string MorphologyFile = "morphology.tsv";

    /// <summary>
    ///     Converts a part-of-speech mask to its letters, in the order of <see cref="PartOfSpeech"/>.
    /// </summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The letters.</returns>
    public static string ToLetters(ushort mask)
    {
        var text = new StringBuilder(Letters.Length);
        for (var i = 0; i < Letters.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                text.Append(Letters[i]);
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     The name of the file of additions to the dictionary.
    /// </summary>
    public const string AdditionsFile = "additions.tsv";

    /// <summary>
    ///     Merges the hand-written additions into the dictionary: a word that is new is added, and a word that exists gains
    ///     the parts of speech it lacks (after its own, in the order of the additions).
    /// </summary>
    /// <param name="words">The dictionary words in ordinal order.</param>
    /// <param name="path">The additions file (<c>word</c>, <c>letters</c>, <c>evidence</c>); nothing is added when it does not exist.</param>
    /// <returns>The merged dictionary in ordinal order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<(string Word, ushort Mask, string Letters)> MergeAdditions(List<(string Word, ushort Mask, string Letters)> words, string path)
    {
        if (!File.Exists(path))
        {
            return words;
        }

        var merged = words.ToDictionary(w => w.Word, w => w.Letters, StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            var where = $"{AdditionsFile} line {number}";
            if (columns.Length < 2 || columns[0].Length == 0 || !columns[0].All(c => c is >= 'a' and <= 'z'))
            {
                throw new InvalidDataException($"{where}: expected 'word<TAB>letters<TAB>evidence' with a word of the letters a-z.");
            }

            ToMask(columns[1], where);
            var current = merged.GetValueOrDefault(columns[0], string.Empty);
            merged[columns[0]] = current + string.Concat(columns[1].Where(l => !current.Contains(l, StringComparison.Ordinal)));
        }

        return merged.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => (m.Key, ToMask(m.Value, m.Key), m.Value)).ToList();
    }

    /// <summary>
    ///     Reads the morphology rules next to the lexicon, when the file exists.
    /// </summary>
    /// <param name="directory">The lexicon directory.</param>
    /// <returns>The rule names with their state, or null when there is no morphology file.</returns>
    public static List<(string Rule, bool Enabled)>? ReadMorphologyIfPresent(string directory)
    {
        var path = Path.Combine(directory, MorphologyFile);
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllLines(path)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'))
            .Where(c => c.Length >= 2)
            .Select(c => (c[0], c[1] == "enabled"))
            .ToList();
    }

    /// <summary>
    ///     Converts part-of-speech letters to a mask.
    /// </summary>
    /// <param name="letters">The letters, in any order and without repeats.</param>
    /// <param name="context">A description of the place of the letters, for the error message.</param>
    /// <returns>The mask.</returns>
    /// <exception cref="InvalidDataException">Thrown when the letters are empty, unknown or repeated.</exception>
    public static ushort ToMask(string letters, string context)
    {
        var mask = 0;
        foreach (var letter in letters)
        {
            var index = Letters.IndexOf(letter, StringComparison.Ordinal);
            if (index < 0 || (mask & (1 << index)) != 0)
            {
                throw new InvalidDataException($"{context}: '{letters}' is not a set of distinct part-of-speech letters from '{Letters}'.");
            }

            mask |= 1 << index;
        }

        return mask == 0 ? throw new InvalidDataException($"{context}: the part-of-speech set is empty.") : (ushort)mask;
    }

    /// <summary>
    ///     Reads a lexicon TSV: strictly ascending lower-case words with letter sets. The order of the letters of a word is
    ///     the order of its senses (the order of the source dictionary, most frequent first).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The words with their masks and their letters in sense order, in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed or out of order.</exception>
    public static List<(string Word, ushort Mask, string Letters)> ReadWords(string path)
    {
        var words = new List<(string Word, ushort Mask, string Letters)>();
        var number = 0;
        foreach (var raw in File.ReadAllText(path, Encoding.UTF8).Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            var where = $"{Path.GetFileName(path)} line {number}";
            if (columns.Length != 2 || columns[0].Length == 0 || !columns[0].All(c => c is >= 'a' and <= 'z'))
            {
                throw new InvalidDataException($"{where}: expected 'word<TAB>letters' with a word of the letters a-z.");
            }

            if (words.Count > 0 && string.CompareOrdinal(words[^1].Word, columns[0]) >= 0)
            {
                throw new InvalidDataException($"{where}: the words are not unique and in ordinal order.");
            }

            words.Add((columns[0], ToMask(columns[1], where), columns[1]));
        }

        return words;
    }
}
