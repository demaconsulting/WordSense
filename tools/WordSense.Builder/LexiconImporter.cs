using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Imports the Moby Part-of-Speech II list (<c>mobypos.txt</c>) into the dictionary word list of the corpus
///     (<c>corpus/lexicon/lexicon.tsv</c>).
/// </summary>
/// <remarks>
///     <para>
///     The import is deterministic and keeps every usable word; no class decides whether a word exists. The rules are:
///     </para>
///     <list type="number">
///     <item><description>The input is read as Latin-1, CRLF or LF; its SHA-256 (over the LF form) must equal the pinned value. A word may be listed on several lines; their codes are joined in file order.</description></item>
///     <item><description>Only single lower-case words of the letters <c>a-z</c> are kept.</description></item>
///     <item><description>Codes map to parts of speech: <c>N</c>, <c>h</c>, <c>p</c> noun; <c>V</c>, <c>t</c>, <c>i</c> verb; <c>A</c> adjective; <c>v</c> adverb; <c>r</c> pronoun; <c>P</c> preposition; <c>C</c> conjunction; <c>!</c> interjection; <c>D</c>, <c>I</c>, <c>o</c> determiner. A part of speech appears once, at its first code, so the order of the letters of a word is the order of the codes (the dictionary lists the most common use first). A word without a mappable code is dropped.</description></item>
///     <item><description>A word that is only an adjective and ends in <c>-ed</c> or <c>-ing</c> gains the verb sense. An <c>-ing</c> word of five or more letters without a verb code gains the verb sense when its stem (plain, plus <c>e</c>, or without a doubled final consonant) has a verb code.</description></item>
///     <item><description>The corrections of <c>corrections.tsv</c> replace the set of a word, in the order given there.</description></item>
///     </list>
/// </remarks>
internal static class LexiconImporter
{
    /// <summary>
    ///     The SHA-256 of the pinned <c>mobypos.txt</c> (with LF line ends), as upper-case hexadecimal.
    /// </summary>
    public const string PinnedSha256 = "AB516C4918E017D7771DA70766EFB95C6C9B81DCECC97395FE0B61EE5373582D";

    /// <summary>
    ///     The name of the file with the corrections, next to the lexicon.
    /// </summary>
    public const string CorrectionsFile = "corrections.tsv";

    /// <summary>
    ///     The counts of an import.
    /// </summary>
    /// <param name="Lines">The number of lines of the input.</param>
    /// <param name="SingleWords">The number of distinct single lower-case words.</param>
    /// <param name="Dropped">The number of those words without a mappable code.</param>
    /// <param name="GainedVerb">The number of words that gained the verb sense by the participle and gerund rule.</param>
    /// <param name="Corrected">The number of words replaced by a correction.</param>
    /// <param name="Determiners">The number of written words that carry the determiner sense.</param>
    /// <param name="Words">The number of words written.</param>
    internal readonly record struct Counts(int Lines, int SingleWords, int Dropped, int GainedVerb, int Corrected, int Determiners, int Words);

    /// <summary>
    ///     Imports a Moby file.
    /// </summary>
    /// <param name="input">The <c>mobypos.txt</c> file.</param>
    /// <param name="output">The lexicon file to write; <c>corrections.tsv</c> is read from its directory.</param>
    /// <param name="expectedSha256">The SHA-256 the input must have (over the LF form), or null to skip the check.</param>
    /// <returns>The counts of the import.</returns>
    /// <exception cref="InvalidDataException">Thrown when the input does not have the pinned hash or a correction is malformed.</exception>
    public static Counts Import(string input, string output, string? expectedSha256 = PinnedSha256)
    {
        var bytes = File.ReadAllBytes(input);
        var text = Encoding.Latin1.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (expectedSha256 is not null)
        {
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text)));
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{Path.GetFileName(input)} has the SHA-256 {actual}, not the pinned {expectedSha256}.");
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".";
        var corrections = ReadCorrections(Path.Join(directory, CorrectionsFile));

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = 0;
        foreach (var line in text.Split('\n'))
        {
            var cut = line.IndexOf('\\', StringComparison.Ordinal);
            if (line.Length == 0 || cut <= 0)
            {
                continue;
            }

            lines++;
            var word = line[..cut];
            codes[word] = codes.TryGetValue(word, out var earlier) ? earlier + line[(cut + 1)..] : line[(cut + 1)..];
        }

        var sets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (word, code) in codes)
        {
            if (word.All(c => c is >= 'a' and <= 'z'))
            {
                sets[word] = Map(code);
            }
        }

        var dropped = sets.Count(s => s.Value.Length == 0);
        var gained = 0;
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (word, set) in sets.Where(s => s.Value.Length > 0))
        {
            var letters = set;
            if (GainsVerb(word, letters, sets))
            {
                letters += "V";
                gained++;
            }

            result[word] = letters;
        }

        var corrected = 0;
        foreach (var (word, letters) in corrections)
        {
            result[word] = letters;
            corrected++;
        }

        var builder = new StringBuilder();
        foreach (var (word, letters) in result)
        {
            builder.Append(word).Append('\t').Append(letters).Append('\n');
        }

        File.WriteAllText(output, builder.ToString(), new UTF8Encoding(false));
        return new Counts(
            lines,
            sets.Count,
            dropped,
            gained,
            corrected,
            result.Count(r => r.Value.Contains('D', StringComparison.Ordinal)),
            result.Count);
    }

    /// <summary>
    ///     Renders the counts as the lines of the counts table of the provenance file.
    /// </summary>
    /// <param name="counts">The counts.</param>
    /// <returns>One line per count.</returns>
    public static string Render(Counts counts) =>
        string.Join(
            '\n',
            $"Entries in the input: {counts.Lines.ToString(CultureInfo.InvariantCulture)}",
            $"Single lower-case words: {counts.SingleWords.ToString(CultureInfo.InvariantCulture)}",
            $"Dropped, no mappable code: {counts.Dropped.ToString(CultureInfo.InvariantCulture)}",
            $"Participle and gerund words that gained the verb sense: {counts.GainedVerb.ToString(CultureInfo.InvariantCulture)}",
            $"Words replaced by a correction: {counts.Corrected.ToString(CultureInfo.InvariantCulture)}",
            $"Words with the determiner sense: {counts.Determiners.ToString(CultureInfo.InvariantCulture)}",
            $"Words in the lexicon: {counts.Words.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    ///     Maps Moby codes to part-of-speech letters, each letter once, in the order of the codes.
    /// </summary>
    private static string Map(string codes)
    {
        var letters = new StringBuilder();
        foreach (var code in codes)
        {
            var letter = code switch
            {
                'N' or 'h' or 'p' => 'N',
                'V' or 't' or 'i' => 'V',
                'A' => 'J',
                'v' => 'B',
                'r' => 'R',
                'P' => 'P',
                'C' => 'C',
                '!' => 'I',
                'D' or 'I' or 'o' => 'D',
                _ => ' ',
            };

            if (letter != ' ' && letters.ToString().IndexOf(letter, StringComparison.Ordinal) < 0)
            {
                letters.Append(letter);
            }
        }

        return letters.ToString();
    }

    /// <summary>
    ///     Applies the participle and gerund rule.
    /// </summary>
    private static bool GainsVerb(string word, string letters, SortedDictionary<string, string> sets)
    {
        if (letters == "J")
        {
            return word.EndsWith("ed", StringComparison.Ordinal) || word.EndsWith("ing", StringComparison.Ordinal);
        }

        if (!word.EndsWith("ing", StringComparison.Ordinal) || word.Length < 5 || letters.Contains('V', StringComparison.Ordinal))
        {
            return false;
        }

        var stem = word[..^3];
        var candidates = new List<string> { stem, stem + "e" };
        if (stem.Length >= 2 && stem[^1] == stem[^2])
        {
            candidates.Add(stem[..^1]);
        }

        return candidates.Any(c => sets.TryGetValue(c, out var set) && set.Contains('V', StringComparison.Ordinal));
    }

    /// <summary>
    ///     Reads the corrections (<c>word</c>, <c>letters</c>, <c>evidence</c>); the file is optional.
    /// </summary>
    private static List<(string Word, string Letters)> ReadCorrections(string path)
    {
        var list = new List<(string Word, string Letters)>();
        if (!File.Exists(path))
        {
            return list;
        }

        var number = 0;
        foreach (var raw in File.ReadAllText(path, Encoding.UTF8).Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            var where = $"{CorrectionsFile} line {number.ToString(CultureInfo.InvariantCulture)}";
            if (columns.Length < 2 || columns[0].Length == 0 || !columns[0].All(c => c is >= 'a' and <= 'z'))
            {
                throw new InvalidDataException($"{where}: expected 'word<TAB>letters<TAB>evidence' with a word of the letters a-z.");
            }

            _ = LexiconSource.ToMask(columns[1], where);
            list.Add((columns[0], columns[1]));
        }

        return list;
    }
}
