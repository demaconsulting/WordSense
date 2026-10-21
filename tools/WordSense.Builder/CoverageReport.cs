using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder;

/// <summary>
///     Measures how much of the running words of a directory of plain-text files the word table knows, and lists the
///     words it does not know.
/// </summary>
/// <remarks>
///     <para>
///     Every <c>*.txt</c> file is cut to the text between its <c>*** START</c> and <c>*** END</c> marker lines (a file
///     without a marker is read completely); the license text of the Project Gutenberg files is thereby excluded. The
///     tokens are runs of letters that may contain apostrophes (<c>don't</c>, <c>Mary’s</c>); a token is known when
///     its lower-case form is a word of the table. A token with an apostrophe is never known by design and is counted
///     apart. An unknown word is name-like when it is written with a capital first letter at least four times as often
///     as without one, and otherwise it is an "other" word (a misspelling, an archaic word or a fragment).
///     </para>
/// </remarks>
internal sealed partial class CoverageReport
{
    /// <summary>
    ///     The factor by which a capitalized spelling must outnumber the lower-case spelling for an unknown word to be a name.
    /// </summary>
    public const int NameFactor = 4;

    private const string StartMarker = "*** START";
    private const string EndMarker = "*** END";

    private readonly Dictionary<string, (int Total, int Capitalized, int Lower)> _unknown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _trainedTypes = new(StringComparer.Ordinal);
    private bool _hasTrained;

    [GeneratedRegex("[A-Za-z]+(?:['’][A-Za-z]+)*")]
    private static partial Regex TokenPattern();

    /// <summary>
    ///     Gets the number of files read.
    /// </summary>
    public int Files { get; private set; }

    /// <summary>
    ///     Gets the number of tokens.
    /// </summary>
    public long Tokens { get; private set; }

    /// <summary>
    ///     Gets the number of tokens that are known.
    /// </summary>
    public long KnownTokens { get; private set; }

    /// <summary>
    ///     Gets the number of tokens that contain an apostrophe.
    /// </summary>
    public long ApostropheTokens { get; private set; }

    /// <summary>
    ///     Gets the number of known tokens whose lemma has trained fine senses.
    /// </summary>
    public long TrainedTokens { get; private set; }

    /// <summary>
    ///     Gets the number of distinct lower-case words of the known tokens.
    /// </summary>
    public int KnownTypes => _knownTypes.Count;

    /// <summary>
    ///     Gets the number of distinct lower-case words of the known tokens whose lemma has trained fine senses.
    /// </summary>
    public int TrainedTypes => _trainedTypes.Count;

    /// <summary>
    ///     Gets the percentage of the known tokens whose lemma has trained fine senses.
    /// </summary>
    public double TrainedShare => KnownTokens == 0 ? 0 : 100.0 * TrainedTokens / KnownTokens;

    /// <summary>
    ///     Gets the unknown words that are not name-like, with their token counts, most frequent first.
    /// </summary>
    public IReadOnlyList<(string Word, int Count)> Other => Unknown(false);

    /// <summary>
    ///     Gets the name-like unknown words with their token counts, most frequent first.
    /// </summary>
    public IReadOnlyList<(string Word, int Count)> Names => Unknown(true);

    /// <summary>
    ///     Gets the percentage of tokens that are known.
    /// </summary>
    public double Coverage => Tokens == 0 ? 0 : 100.0 * KnownTokens / Tokens;

    /// <summary>
    ///     Gets the percentage of the tokens without an apostrophe that are known.
    /// </summary>
    public double CoverageWithoutApostrophes => Tokens == ApostropheTokens ? 0 : 100.0 * KnownTokens / (Tokens - ApostropheTokens);

    /// <summary>
    ///     Cuts a file to its text between the <c>*** START</c> and <c>*** END</c> marker lines.
    /// </summary>
    /// <param name="lines">The lines of the file.</param>
    /// <returns>The lines of the body.</returns>
    public static IEnumerable<string> Body(IEnumerable<string> lines)
    {
        var all = lines as IReadOnlyList<string> ?? lines.ToList();
        var start = -1;
        var end = all.Count;
        for (var i = 0; i < all.Count; i++)
        {
            if (start < 0 && all[i].Contains(StartMarker, StringComparison.Ordinal))
            {
                start = i;
            }
            else if (start >= 0 && all[i].Contains(EndMarker, StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return all.Skip(start + 1).Take(end - start - 1);
    }

    /// <summary>
    ///     Reads a directory of text files.
    /// </summary>
    /// <param name="directory">The directory; every <c>*.txt</c> file in it is read.</param>
    /// <param name="known">Tells whether a lower-case word is known.</param>
    /// <param name="trained">Tells whether a known lower-case word has a lemma with trained fine senses, or null to leave the trained share out.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidDataException">Thrown when the directory does not exist or holds no text file.</exception>
    public static CoverageReport Read(string directory, Func<string, bool> known, Func<string, bool>? trained = null)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidDataException($"The text directory '{directory}' does not exist.");
        }

        var files = Directory.GetFiles(directory, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            throw new InvalidDataException($"The text directory '{directory}' holds no .txt file.");
        }

        var report = new CoverageReport();
        foreach (var file in files)
        {
            report.AddText(Body(File.ReadAllLines(file, Encoding.UTF8)), known, trained);
        }

        return report;
    }

    /// <summary>
    ///     Adds the lines of one text.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="known">Tells whether a lower-case word is known.</param>
    /// <param name="trained">Tells whether a known lower-case word has a lemma with trained fine senses, or null to leave the trained share out.</param>
    public void AddText(IEnumerable<string> lines, Func<string, bool> known, Func<string, bool>? trained = null)
    {
        Files++;
        _hasTrained |= trained is not null;
        foreach (var line in lines)
        {
            foreach (var match in TokenPattern().EnumerateMatches(line))
            {
                var token = line.AsSpan(match.Index, match.Length).ToString();
                Tokens++;
                var lower = token.ToLowerInvariant();
                if (lower.Contains('\'') || lower.Contains('’'))
                {
                    ApostropheTokens++;
                    continue;
                }

                if (known(lower))
                {
                    KnownTokens++;
                    _knownTypes.Add(lower);
                    if (trained is not null && trained(lower))
                    {
                        TrainedTokens++;
                        _trainedTypes.Add(lower);
                    }

                    continue;
                }

                _unknown.TryGetValue(lower, out var counts);
                var capitalized = char.IsUpper(token[0]) ? 1 : 0;
                var isLower = token == lower ? 1 : 0;
                _unknown[lower] = (counts.Total + 1, counts.Capitalized + capitalized, counts.Lower + isLower);
            }
        }
    }

    /// <summary>
    ///     Renders the report as markdown.
    /// </summary>
    /// <param name="top">The number of unknown words listed in each of the two lists.</param>
    /// <returns>The markdown text.</returns>
    public string Render(int top)
    {
        var inv = CultureInfo.InvariantCulture;
        var names = Names;
        var other = Other;
        var text = new StringBuilder();
        text.AppendLine("# Token Coverage");
        text.AppendLine();
        text.AppendLine("| Measure | Value |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| Files | {Files.ToString(inv)} |");
        text.AppendLine($"| Tokens | {Tokens.ToString("N0", inv)} |");
        text.AppendLine($"| Known tokens | {KnownTokens.ToString("N0", inv)} ({Coverage.ToString("F2", inv)} percent) |");
        text.AppendLine($"| Apostrophe tokens (unknown by design) | {ApostropheTokens.ToString("N0", inv)} |");
        text.AppendLine($"| Known without apostrophe tokens | {CoverageWithoutApostrophes.ToString("F2", inv)} percent |");
        text.AppendLine($"| Name-like unknown tokens | {names.Sum(n => (long)n.Count).ToString("N0", inv)} in {names.Count.ToString("N0", inv)} words |");
        text.AppendLine($"| Other unknown tokens | {other.Sum(n => (long)n.Count).ToString("N0", inv)} in {other.Count.ToString("N0", inv)} words |");
        text.AppendLine($"| Known words (types) | {KnownTypes.ToString("N0", inv)} |");
        if (_hasTrained)
        {
            text.AppendLine($"| Known tokens of trained lemmas | {TrainedTokens.ToString("N0", inv)} ({TrainedShare.ToString("F2", inv)} percent of the known tokens) |");
            text.AppendLine($"| Known words (types) of trained lemmas | {TrainedTypes.ToString("N0", inv)} |");
        }

        text.AppendLine();
        Append(text, "Top other unknown words", other, top);
        Append(text, "Top name-like unknown words", names, top);
        return text.ToString();
    }

    private static void Append(StringBuilder text, string title, IReadOnlyList<(string Word, int Count)> words, int top)
    {
        text.AppendLine($"## {title}");
        text.AppendLine();
        text.AppendLine("| Word | Tokens |");
        text.AppendLine("| --- | --- |");
        foreach (var (word, count) in words.Take(top))
        {
            text.AppendLine($"| {word} | {count.ToString(CultureInfo.InvariantCulture)} |");
        }

        text.AppendLine();
    }

    private List<(string Word, int Count)> Unknown(bool names) =>
        _unknown
            .Where(p => (p.Value.Capitalized > 0 && p.Value.Capitalized >= NameFactor * p.Value.Lower) == names)
            .Select(p => (p.Key, p.Value.Total))
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
}
