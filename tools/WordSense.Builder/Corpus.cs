using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One labeled sentence of the fine-sense corpus.
/// </summary>
/// <param name="Lemma">The lower-case lemma of the target word.</param>
/// <param name="SenseId">The public id of the fine sense (<c>lemma#label</c>) of the target word in this sentence.</param>
/// <param name="PartOfSpeech">The part of speech of the sense, as written in the row.</param>
/// <param name="Sentence">The sentence with the target word wrapped in one pair of braces.</param>
/// <param name="Source">The name of the file the row came from, used to group reports.</param>
/// <param name="Words">The words of the sentence (braces removed), as the shared text splitter produces them.</param>
/// <param name="TargetWordIndex">The index in <paramref name="Words"/> of the braced target word.</param>
internal sealed record CorpusRow(string Lemma, string SenseId, PartOfSpeech PartOfSpeech, string Sentence, string Source, IReadOnlyList<string> Words, int TargetWordIndex);

/// <summary>
///     Reads and validates the fine-sense corpus files (<c>corpus/anchors</c> and <c>corpus/holdout</c>).
/// </summary>
/// <remarks>
///     A row has four tab-separated fields and no header: the lower-case lemma, the sense id, the name of the part of
///     speech and the sentence with the target word in one pair of braces. The reader checks the format and, through
///     <see cref="SenseInventory"/>, that the sense exists in the catalog of the word image, belongs to the lemma of the
///     row and has the part of speech of the row.
/// </remarks>
internal static class Corpus
{
    /// <summary>
    ///     The number of waves of the corpus that make one stage: waves 1 to 49 are the base stage (the rank waves), waves
    ///     50 to 99 the first added stage, and so on.
    /// </summary>
    public const int WavesPerStage = 50;

    /// <summary>
    ///     The name of the base stage.
    /// </summary>
    public const string BaseStage = "base";

    private const int ColumnCount = 4;

    private const string WavePrefix = "quota-w";

    /// <summary>
    ///     Gets the stage of every lemma from the file names of the anchors: the stage of the highest wave
    ///     (<c>quota-w{wave}-…</c>, the wave counted as its leading digits) that holds an anchor of the lemma, divided by
    ///     <see cref="WavesPerStage"/>; files that are not wave files do not count.
    /// </summary>
    /// <param name="anchors">The anchor rows.</param>
    /// <returns>The function that names the stage of a lemma: <see cref="BaseStage"/> for stage 0 and for a lemma without a wave file, else <c>stage N</c>.</returns>
    public static Func<string, string> StagesOf(IEnumerable<CorpusRow> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        var stageOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in anchors)
        {
            if (WaveOf(row.Source) is { } wave)
            {
                var stage = wave / WavesPerStage;
                stageOf[row.Lemma] = stageOf.TryGetValue(row.Lemma, out var known) ? Math.Max(known, stage) : stage;
            }
        }

        return lemma => stageOf.TryGetValue(lemma, out var stage) && stage > 0 ? $"stage {stage.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : BaseStage;
    }

    private static int? WaveOf(string fileName)
    {
        if (!fileName.StartsWith(WavePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digits = fileName.AsSpan(WavePrefix.Length);
        var count = 0;
        while (count < digits.Length && char.IsAsciiDigit(digits[count]))
        {
            count++;
        }

        return count > 0 ? int.Parse(digits[..count], System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    ///     Reads every <c>*.tsv</c> sentence file in a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The directory; a missing directory has no rows.</param>
    /// <returns>The rows of all files.</returns>
    /// <exception cref="InvalidDataException">Thrown when a row is malformed or inconsistent with the sense catalog.</exception>
    public static List<CorpusRow> ReadDirectory(string directory)
    {
        var rows = new List<CorpusRow>();
        if (!Directory.Exists(directory))
        {
            return rows;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            rows.AddRange(ReadSentences(file));
        }

        return rows;
    }

    /// <summary>
    ///     Reads the lemmas of the rows of every <c>*.tsv</c> sentence file in a directory, without validating the rows.
    /// </summary>
    /// <remarks>
    ///     The appendix and the coverage report only need to know which lemmas have training sentences; they read the
    ///     first column and leave the checking of the rows to the reader that trains.
    /// </remarks>
    /// <param name="directory">The directory; a missing directory has no lemmas.</param>
    /// <returns>The distinct lemmas (the first column of every non-empty row, trimmed).</returns>
    public static HashSet<string> ReadLemmas(string directory)
    {
        var lemmas = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return lemmas;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv"))
        {
            foreach (var line in File.ReadLines(file, new UTF8Encoding(false)))
            {
                var lemma = line.Split('\t')[0].Trim();
                if (lemma.Length > 0)
                {
                    lemmas.Add(lemma);
                }
            }
        }

        return lemmas;
    }

    /// <summary>
    ///     Reads one sentence file and validates every row.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static List<CorpusRow> ReadSentences(string path)
    {
        var (rows, errors) = ReadSentencesLenient(path);
        return errors.Count > 0 ? throw new InvalidDataException(errors[0]) : rows;
    }

    /// <summary>
    ///     Reads one sentence file, keeping the valid rows and describing the others.
    /// </summary>
    /// <remarks>
    ///     A row is invalid when it is not four columns, its lemma is not lower-case, its part of speech is not the name
    ///     of a part of speech, its sentence has no single non-empty braced target inside one word, its sense is not in
    ///     the catalog, or the lemma or the part of speech of the row differs from the ones of the sense. Whether the
    ///     target word is a form of the lemma is a softer rule that <see cref="SenseInventory.Validate"/> reports.
    /// </remarks>
    /// <param name="path">The file path.</param>
    /// <returns>The valid rows and one message per invalid row.</returns>
    public static (List<CorpusRow> Rows, List<string> Errors) ReadSentencesLenient(string path)
    {
        var name = Path.GetFileName(path);
        var rows = new List<CorpusRow>();
        var errors = new List<string>();
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length != ColumnCount)
            {
                errors.Add($"{name}:{number}: expected {ColumnCount} tab-separated columns but found {columns.Length}.");
                continue;
            }

            var (lemma, senseId, posText, sentence) = (columns[0].Trim(), columns[1].Trim(), columns[2].Trim(), columns[3].Trim());
            if (lemma.Length == 0 || senseId.Length == 0 || !string.Equals(lemma, lemma.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errors.Add($"{name}:{number}: the lemma must be lower-case and the sense id must be non-empty.");
                continue;
            }

            if (!Enum.TryParse<PartOfSpeech>(posText, false, out var pos) || !Enum.IsDefined(pos) || !string.Equals(pos.ToString(), posText, StringComparison.Ordinal))
            {
                errors.Add($"{name}:{number}: '{posText}' is not the name of a part of speech.");
                continue;
            }

            var (words, targetIndex, problem) = SplitTarget(sentence);
            if (problem is not null)
            {
                errors.Add($"{name}:{number}: {problem}");
                continue;
            }

            if (SenseInventory.CheckSense(lemma, senseId, pos) is { } disagreement)
            {
                errors.Add($"{name}:{number}: {disagreement}");
                continue;
            }

            rows.Add(new CorpusRow(lemma, senseId, pos, sentence, name, words!, targetIndex));
        }

        return (rows, errors);
    }

    /// <summary>
    ///     Checks that no sentence appears twice, in either set or across both. Sentences are compared by their
    ///     normalized text, so a difference of case, spacing or punctuation does not make a sentence new.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <exception cref="InvalidDataException">Thrown when a sentence is duplicated.</exception>
    public static void EnsureDisjoint(IEnumerable<CorpusRow> anchors, IEnumerable<CorpusRow> holdout)
    {
        var tagged = anchors.Select(r => ("anchors", r.Source, Normalize(r.Sentence)))
            .Concat(holdout.Select(r => ("holdout", r.Source, Normalize(r.Sentence))));
        EnsureDistinct(tagged, true);
    }

    /// <summary>
    ///     Normalizes a sentence for the disjointness check: braces are dropped, letters and digits are lower-cased and
    ///     every other run of characters becomes a single space.
    /// </summary>
    /// <param name="sentence">The sentence of a row.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string sentence)
    {
        var text = new StringBuilder(sentence.Length);
        var space = false;
        foreach (var ch in sentence)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (space && text.Length > 0)
                {
                    text.Append(' ');
                }

                space = false;
                text.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                space = true;
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     Checks that no key occurs twice among the sentences of the sets.
    /// </summary>
    /// <param name="sentences">The set name, the file name and the comparison key of every sentence.</param>
    /// <param name="normalized">True when the keys are normalized texts, which the message then says.</param>
    /// <exception cref="InvalidDataException">Thrown when a key occurs twice.</exception>
    internal static void EnsureDistinct(IEnumerable<(string Set, string Source, string Key)> sentences, bool normalized)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (set, source, key) in sentences)
        {
            if (!seen.TryAdd(key, $"{set}/{source}"))
            {
                throw new InvalidDataException($"Duplicate sentence{(normalized ? " (after normalization)" : string.Empty)} in {set}/{source} (first seen in {seen[key]}): {key}");
            }
        }
    }

    /// <summary>
    ///     Splits a braced sentence into its words and finds the target word.
    /// </summary>
    /// <remarks>
    ///     The braces mark one word of the shared word split. A target inside a hyphenated or possessive word splits that
    ///     word at the braces, which BERT pre-tokenization does anyway (hyphens and apostrophes are punctuation).
    /// </remarks>
    /// <param name="sentence">The sentence with one pair of braces.</param>
    /// <returns>The words and the index of the target word, or a message that describes the problem.</returns>
    internal static (IReadOnlyList<string>? Words, int TargetIndex, string? Problem) SplitTarget(string sentence)
    {
        var open = sentence.IndexOf('{', StringComparison.Ordinal);
        var close = sentence.IndexOf('}', StringComparison.Ordinal);
        if (open < 0 || close != sentence.LastIndexOf('}') || open != sentence.LastIndexOf('{') || close < open + 2)
        {
            return (null, -1, "the sentence must contain exactly one non-empty {target} pair.");
        }

        var plain = sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
        var words = TextSplitter.SplitWords(plain);
        var end = close - 1;
        var containing = words.FindIndex(w => w.Start <= open && end <= w.Start + w.Length);
        if (containing < 0)
        {
            return (null, -1, "the braced target must lie within one word.");
        }

        var word = words[containing];
        var pieces = new List<string>();
        if (open > word.Start)
        {
            pieces.Add(plain[word.Start..open]);
        }

        var targetIndex = containing + pieces.Count;
        pieces.Add(plain[open..end]);
        if (end < word.Start + word.Length)
        {
            pieces.Add(plain[end..(word.Start + word.Length)]);
        }

        var texts = words.Select(w => w.Text).ToList();
        texts.RemoveAt(containing);
        texts.InsertRange(containing, pieces);
        return (texts, targetIndex, null);
    }
}
