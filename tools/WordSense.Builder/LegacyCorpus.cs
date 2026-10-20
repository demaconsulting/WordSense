using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One labeled sentence of the LEGACY corpus, whose labels are the retired pronunciation classes.
/// </summary>
/// <param name="Lemma">The lower-case lemma of the target word.</param>
/// <param name="Sense">The legacy class label.</param>
/// <param name="Sentence">The sentence with the target word wrapped in one pair of braces.</param>
/// <param name="Source">The name of the file the row came from, used to group reports.</param>
/// <param name="Words">The words of the sentence (braces removed), as the shared text splitter produces them.</param>
/// <param name="TargetWordIndex">The index in <paramref name="Words"/> of the braced target word.</param>
internal sealed record LegacyRow(string Lemma, string Sense, string Sentence, string Source, IReadOnlyList<string> Words, int TargetWordIndex);

/// <summary>
///     Reads the LEGACY class-labeled corpus (<c>corpus/legacy/anchors</c>, <c>holdout</c>) with three columns: lemma,
///     class label and sentence.
/// </summary>
/// <remarks>
///     Only the Basic model commands <c>train-basic</c> and <c>tune-basic</c> read these files, because the shipped table
///     of the Basic model was trained from them. The fine-sense commands never do; they read <see cref="Corpus"/>.
/// </remarks>
internal static class LegacyCorpus
{
    private const int ColumnCount = 3;

    /// <summary>
    ///     Reads every <c>*.tsv</c> sentence file in a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The directory; a missing directory has no rows.</param>
    /// <returns>The rows of all files.</returns>
    /// <exception cref="InvalidDataException">Thrown when a row is malformed.</exception>
    public static List<LegacyRow> ReadDirectory(string directory)
    {
        var rows = new List<LegacyRow>();
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
    public static List<LegacyRow> ReadSentences(string path)
    {
        var (rows, errors) = ReadSentencesLenient(path);
        return errors.Count > 0 ? throw new InvalidDataException(errors[0]) : rows;
    }

    /// <summary>
    ///     Reads one sentence file, keeping well-formed rows and describing the malformed ones.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The well-formed rows and one message per malformed row.</returns>
    public static (List<LegacyRow> Rows, List<string> Errors) ReadSentencesLenient(string path)
    {
        var name = Path.GetFileName(path);
        var rows = new List<LegacyRow>();
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

            var (lemma, sense, sentence) = (columns[0].Trim(), columns[1].Trim(), columns[2].Trim());
            if (lemma.Length == 0 || sense.Length == 0 || !string.Equals(lemma, lemma.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errors.Add($"{name}:{number}: lemma must be lower-case and sense must be non-empty.");
                continue;
            }

            var (words, targetIndex, problem) = Corpus.SplitTarget(sentence);
            if (problem is not null)
            {
                errors.Add($"{name}:{number}: {problem}");
                continue;
            }

            rows.Add(new LegacyRow(lemma, sense, sentence, name, words!, targetIndex));
        }

        return (rows, errors);
    }

    /// <summary>
    ///     Checks that no sentence appears twice, in either set or across both, by its exact text.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <exception cref="InvalidDataException">Thrown when a sentence is duplicated.</exception>
    public static void EnsureDisjoint(IEnumerable<LegacyRow> anchors, IEnumerable<LegacyRow> holdout)
    {
        var tagged = anchors.Select(r => ("anchors", r.Source, r.Sentence))
            .Concat(holdout.Select(r => ("holdout", r.Source, r.Sentence)));
        Corpus.EnsureDistinct(tagged, false);
    }
}
