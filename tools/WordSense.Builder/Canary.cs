using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One sentence of a canary file: an identifier and its words.
/// </summary>
/// <param name="Id">The identifier of the sentence; it holds no tab and no line break.</param>
/// <param name="Words">The words of the sentence; none holds a space, a tab or a line break.</param>
internal sealed record CanarySentence(string Id, string[] Words);

/// <summary>
///     One sentence of a canary file with the answer of the model to each of its words.
/// </summary>
/// <param name="Id">The identifier of the sentence.</param>
/// <param name="Words">The words of the sentence.</param>
/// <param name="Answers">The answer to each word: the level and the key (<c>Sense:bank#financial-institution</c>), or <c>-</c> for a word without an answer.</param>
internal sealed record CanaryEntry(string Id, string[] Words, string[] Answers);

/// <summary>
///     A canary file: the header lines and the answered sentences.
/// </summary>
/// <param name="Header">The header lines (<c># name: value</c>) by name.</param>
/// <param name="Entries">The answered sentences.</param>
internal sealed record CanaryFile(IReadOnlyDictionary<string, string> Header, IReadOnlyList<CanaryEntry> Entries);

/// <summary>
///     A word whose answer differs from the one in the canary file.
/// </summary>
/// <param name="Id">The identifier of the sentence.</param>
/// <param name="Index">The index of the word in the sentence.</param>
/// <param name="Word">The word.</param>
/// <param name="Expected">The answer in the file.</param>
/// <param name="Actual">The answer on this machine.</param>
internal sealed record CanaryFlip(string Id, int Index, string Word, string Expected, string Actual);

/// <summary>
///     The verdict of a canary check on one processor architecture.
/// </summary>
/// <param name="Applicable">False when the architecture has no rule (the check is skipped).</param>
/// <param name="Limit">The most words that may differ.</param>
/// <param name="Passed">True when the check is skipped or the number of differing words is within the limit.</param>
internal sealed record CanaryVerdict(bool Applicable, int Limit, bool Passed);

/// <summary>
///     The CPU canary: a fixed set of sentences with the answers of a model on a reference CPU, and the check of the
///     answers of the machine it runs on.
/// </summary>
/// <remarks>
///     <para>
///     An int8 model that rounds its activations does not give bit-identical vectors on every CPU. The canary makes the
///     difference visible and bounds it: on x86 and x86-64 every answer must equal the one of the file; on ARM at most a
///     given number of words (<see cref="DefaultArmTolerance"/> unless the file or the command line says otherwise) may
///     differ. A processor of another architecture is not judged.
///     </para>
///     <para>
///     File format (UTF-8, line feeds): header lines <c># name: value</c> (<c>model</c>, <c>encoder-sha256</c>,
///     <c>arm-tolerance</c>, <c>written-on</c>), then one line per sentence with tab-separated columns: the id, the words
///     joined by spaces, and one answer per word (<c>Level:key</c>, or <c>-</c>). The answer of a word is the level and the
///     key of the public <c>ClassifyAsync</c> result, so the heads and the fallback are part of it; the confidence is not.
///     </para>
/// </remarks>
internal static class Canary
{
    /// <summary>
    ///     The most words that may differ on an ARM processor when nothing else is said.
    /// </summary>
    public const int DefaultArmTolerance = 40;

    private const string NoAnswer = "-";

    /// <summary>
    ///     Gets the answer text of a word.
    /// </summary>
    /// <param name="result">The result of the model.</param>
    /// <returns>The level and the key, or <c>-</c> when the word has no key.</returns>
    public static string AnswerOf(ClassifyResult result) =>
        result.Key is { } key ? string.Create(CultureInfo.InvariantCulture, $"{result.Level}:{key}") : NoAnswer;

    /// <summary>
    ///     Reads the sentences to answer: tab-separated lines whose first column is the id and whose last column is the
    ///     words joined by spaces; other columns are ignored, so a file with more columns serves as well.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line has fewer than two columns, a sentence has no words or an id is used twice.</exception>
    public static List<CanarySentence> ReadSentences(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        var sentences = new List<CanarySentence>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, number) in File.ReadLines(path, new UTF8Encoding(false)).Select((l, i) => (l.TrimEnd('\r'), i + 1)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            var words = columns.Length < 2 ? [] : columns[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || columns[0].Length == 0)
            {
                throw new InvalidDataException($"{name}:{number}: a sentence line needs an id and words, separated by a tab.");
            }

            if (!ids.Add(columns[0]))
            {
                throw new InvalidDataException($"{name}:{number}: the id '{columns[0]}' is used twice.");
            }

            sentences.Add(new CanarySentence(columns[0], words));
        }

        return sentences.Count > 0 ? sentences : throw new InvalidDataException($"{name}: the file has no sentence.");
    }

    /// <summary>
    ///     Answers every sentence with a model.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="sentences">The sentences.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The answered sentences.</returns>
    public static async Task<List<CanaryEntry>> AnswerAsync(IWordSenseModel model, IReadOnlyList<CanarySentence> sentences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sentences);
        var entries = new List<CanaryEntry>(sentences.Count);
        foreach (var sentence in sentences)
        {
            var results = await model.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
            entries.Add(new CanaryEntry(sentence.Id, sentence.Words, [.. results.Select(AnswerOf)]));
        }

        return entries;
    }

    /// <summary>
    ///     Renders a canary file.
    /// </summary>
    /// <param name="header">The header lines in the order they are written.</param>
    /// <param name="entries">The answered sentences.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(IEnumerable<KeyValuePair<string, string>> header, IEnumerable<CanaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(entries);
        var text = new StringBuilder();
        foreach (var (name, value) in header)
        {
            text.Append("# ").Append(name).Append(": ").Append(value).Append('\n');
        }

        foreach (var entry in entries)
        {
            text.Append(entry.Id).Append('\t').Append(string.Join(' ', entry.Words));
            foreach (var answer in entry.Answers)
            {
                text.Append('\t').Append(answer);
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads a canary file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The header and the answered sentences.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed or the file has no sentence.</exception>
    public static CanaryFile Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<CanaryEntry>();
        foreach (var (line, number) in File.ReadLines(path, new UTF8Encoding(false)).Select((l, i) => (l.TrimEnd('\r'), i + 1)))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 2)
                {
                    header[line[2..colon]] = line[(colon + 1)..].Trim();
                }

                continue;
            }

            var columns = line.Split('\t');
            var words = columns.Length < 2 ? [] : columns[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || columns.Length != words.Length + 2)
            {
                throw new InvalidDataException($"{name}:{number}: a sentence line has the id, the words and one answer per word.");
            }

            entries.Add(new CanaryEntry(columns[0], words, columns[2..]));
        }

        return entries.Count > 0 ? new CanaryFile(header, entries) : throw new InvalidDataException($"{name}: the file has no sentence.");
    }

    /// <summary>
    ///     Lists the words whose answer differs between a canary file and a run.
    /// </summary>
    /// <param name="expected">The sentences of the file.</param>
    /// <param name="actual">The sentences answered on this machine, in the same order.</param>
    /// <returns>The differing words.</returns>
    /// <exception cref="InvalidDataException">Thrown when the sentences or their words do not correspond.</exception>
    public static List<CanaryFlip> Compare(IReadOnlyList<CanaryEntry> expected, IReadOnlyList<CanaryEntry> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Count != actual.Count)
        {
            throw new InvalidDataException($"The run has {actual.Count} sentences, the file {expected.Count}.");
        }

        var flips = new List<CanaryFlip>();
        for (var s = 0; s < expected.Count; s++)
        {
            if (expected[s].Id != actual[s].Id || expected[s].Answers.Length != actual[s].Answers.Length)
            {
                throw new InvalidDataException($"The sentence '{expected[s].Id}' of the file does not correspond to the run.");
            }

            for (var w = 0; w < expected[s].Answers.Length; w++)
            {
                if (!string.Equals(expected[s].Answers[w], actual[s].Answers[w], StringComparison.Ordinal))
                {
                    flips.Add(new CanaryFlip(expected[s].Id, w, expected[s].Words[w], expected[s].Answers[w], actual[s].Answers[w]));
                }
            }
        }

        return flips;
    }

    /// <summary>
    ///     Judges a number of differing words for a processor architecture.
    /// </summary>
    /// <param name="flips">The number of words whose answer differs.</param>
    /// <param name="architecture">The architecture of the process.</param>
    /// <param name="armTolerance">The most words that may differ on ARM.</param>
    /// <returns>The verdict: x86 and x86-64 allow none, ARM allows <paramref name="armTolerance"/>, every other architecture is not judged.</returns>
    public static CanaryVerdict Judge(int flips, Architecture architecture, int armTolerance) => architecture switch
    {
        Architecture.X64 or Architecture.X86 => new CanaryVerdict(true, 0, flips == 0),
        Architecture.Arm64 => new CanaryVerdict(true, armTolerance, flips <= armTolerance),
        _ => new CanaryVerdict(false, 0, true),
    };

    /// <summary>
    ///     Chooses the number of words that may differ on ARM: the option, else the header of the file, else the default.
    /// </summary>
    /// <param name="header">The header lines of the canary file.</param>
    /// <param name="option">The value of <c>--arm-tolerance</c>, or null when it is not given.</param>
    /// <returns>The tolerance.</returns>
    /// <exception cref="InvalidDataException">Thrown when the header has an <c>arm-tolerance</c> that is not a number of words that is not negative; the header is checked even when the option overrides it.</exception>
    public static int ResolveArmTolerance(IReadOnlyDictionary<string, string> header, int? option)
    {
        ArgumentNullException.ThrowIfNull(header);
        int? fromFile = null;
        if (header.TryGetValue("arm-tolerance", out var text))
        {
            fromFile = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidDataException($"The header 'arm-tolerance' of the canary file must be a number of words that is not negative, not '{text}'.");
        }

        return option ?? fromFile ?? DefaultArmTolerance;
    }

    /// <summary>
    ///     Describes the machine a canary file is written on.
    /// </summary>
    /// <returns>The architecture and the operating system.</returns>
    public static string Machine() =>
        $"{RuntimeInformation.ProcessArchitecture} {RuntimeInformation.OSDescription}";
}
