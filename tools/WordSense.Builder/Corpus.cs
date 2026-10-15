using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One labeled sentence of the corpus.
/// </summary>
/// <param name="Lemma">The lower-case lemma of the target word.</param>
/// <param name="Sense">The sense label.</param>
/// <param name="Sentence">The sentence with the target word wrapped in one pair of braces.</param>
/// <param name="Source">The name of the file the row came from, used to group reports.</param>
/// <param name="Words">The words of the sentence (braces removed), as the shared text splitter produces them.</param>
/// <param name="TargetWordIndex">The index in <paramref name="Words"/> of the braced target word.</param>
internal sealed record CorpusRow(string Lemma, string Sense, string Sentence, string Source, IReadOnlyList<string> Words, int TargetWordIndex);

/// <summary>
///     Reads and validates the corpus files.
/// </summary>
internal static class Corpus
{
    /// <summary>
    ///     Reads every <c>*.tsv</c> sentence file in a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The rows of all files.</returns>
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
    ///     Reads one sentence file and validates the format of every row.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="InvalidDataException">Thrown when a row is malformed.</exception>
    public static List<CorpusRow> ReadSentences(string path)
    {
        var (rows, errors) = ReadSentencesLenient(path);
        return errors.Count > 0 ? throw new InvalidDataException(errors[0]) : rows;
    }

    /// <summary>
    ///     Reads one sentence file, keeping well-formed rows and describing the malformed ones.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The well-formed rows and one message per malformed row.</returns>
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
            if (columns.Length != 3)
            {
                errors.Add($"{name}:{number}: expected 3 tab-separated columns but found {columns.Length}.");
                continue;
            }

            var (lemma, sense, sentence) = (columns[0].Trim(), columns[1].Trim(), columns[2].Trim());
            if (lemma.Length == 0 || sense.Length == 0 || !string.Equals(lemma, lemma.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errors.Add($"{name}:{number}: lemma must be lower-case and sense must be non-empty.");
                continue;
            }

            var open = sentence.IndexOf('{', StringComparison.Ordinal);
            var close = sentence.IndexOf('}', StringComparison.Ordinal);
            if (open < 0 || close != sentence.LastIndexOf('}') || open != sentence.LastIndexOf('{') || close < open + 2)
            {
                errors.Add($"{name}:{number}: the sentence must contain exactly one non-empty {{target}} pair.");
                continue;
            }

            // The braces mark one word of the shared word split; a target inside a hyphenated or possessive word
            // splits that word at the braces, which BERT pre-tokenization does anyway (hyphens and apostrophes are punctuation)
            var plain = sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
            var words = TextSplitter.SplitWords(plain);
            var end = close - 1;
            var containing = words.FindIndex(w => w.Start <= open && end <= w.Start + w.Length);
            if (containing < 0)
            {
                errors.Add($"{name}:{number}: the braced target must lie within one word.");
                continue;
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
            rows.Add(new CorpusRow(lemma, sense, sentence, name, texts, targetIndex));
        }

        return (rows, errors);
    }

    /// <summary>
    ///     Reads a forms file: <c>form</c>, <c>lemma</c>, <c>labels</c> with a header line.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>Form, lemma and label list per row.</returns>
    public static List<(string Form, string Lemma, string[] Labels)> ReadForms(string path)
    {
        var forms = new List<(string, string, string[])>();
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 2)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{i + 1}: expected form and lemma columns.");
            }

            var labels = columns.Length > 2 ? columns[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            forms.Add((columns[0].Trim().ToLowerInvariant(), columns[1].Trim().ToLowerInvariant(), labels));
        }

        return forms;
    }

    /// <summary>
    ///     Checks that no sentence appears twice, in either set or across both.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <exception cref="InvalidDataException">Thrown when a sentence is duplicated.</exception>
    public static void EnsureDisjoint(IEnumerable<CorpusRow> anchors, IEnumerable<CorpusRow> holdout)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var tagged = anchors.Select(r => ("anchors", r)).Concat(holdout.Select(r => ("holdout", r)));
        foreach (var (set, row) in tagged)
        {
            if (!seen.TryAdd(row.Sentence, $"{set}/{row.Source}"))
            {
                throw new InvalidDataException($"Duplicate sentence in {set}/{row.Source} (first seen in {seen[row.Sentence]}): {row.Sentence}");
            }
        }
    }
}
