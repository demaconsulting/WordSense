using System.Collections.Immutable;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One labeled sentence of the form-chooser corpus.
/// </summary>
/// <param name="Form">The lower-case form (the braced target word) whose analyses the label chooses among.</param>
/// <param name="Label">The label: the lemma of the analyses it stands for (<c>leave</c>), or <c>lemma/features</c> for one analysis of a lemma whose analyses of the form differ in features (<c>read/Tense=Past+VerbForm=Part</c>).</param>
/// <param name="Sentence">The sentence with the target word wrapped in one pair of braces.</param>
/// <param name="Source">The name of the file the row came from, used to group reports.</param>
/// <param name="Words">The words of the sentence (braces removed), as the shared text splitter produces them.</param>
/// <param name="TargetWordIndex">The index in <paramref name="Words"/> of the braced target word.</param>
internal sealed record ChooserRow(string Form, string Label, string Sentence, string Source, IReadOnlyList<string> Words, int TargetWordIndex);

/// <summary>
///     Reads and validates the form-chooser corpus files (<c>corpus/choosers/anchors</c> and <c>corpus/choosers/holdout</c>).
/// </summary>
/// <remarks>
///     <para>
///     A form chooser decides which lemma, or which tense of one lemma, a word form has (<c>left</c> as a form of
///     <c>leave</c> or of the adjective <c>left</c>, <c>read</c> as a past or a present verb). A row has three
///     tab-separated fields and no header: the lower-case form, the label and the sentence with the form in one pair of
///     braces. The braced word, lower-cased, must be the form. The label names the lemma of one or more analyses of the
///     form, or, written <c>lemma/features</c>, exactly one analysis of that lemma with those features; this is the
///     label rule of the weights blob (the table checks it again when the weights load).
///     </para>
///     <para>
///     The chooser rows are a second, independent view of sentences that the fine-sense corpus labels in full: the split
///     of a sentence (anchors or holdout) is the one of its sense row, so a held-out sentence never trains a chooser.
///     </para>
/// </remarks>
internal static class ChooserCorpus
{
    private const int ColumnCount = 3;

    /// <summary>
    ///     Reads every <c>*.tsv</c> chooser file in a directory, in file-name order.
    /// </summary>
    /// <param name="directory">The directory; a missing directory has no rows.</param>
    /// <returns>The rows of all files.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem of a malformed row.</exception>
    public static List<ChooserRow> ReadDirectory(string directory)
    {
        var rows = new List<ChooserRow>();
        if (!Directory.Exists(directory))
        {
            return rows;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var (read, errors) = ReadSentencesLenient(file);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(errors[0]);
            }

            rows.AddRange(read);
        }

        return rows;
    }

    /// <summary>
    ///     Reads one chooser file, keeping the valid rows and describing the others.
    /// </summary>
    /// <remarks>
    ///     A row is invalid when it is not three columns, its form is empty or not lower-case, its sentence has no
    ///     single non-empty braced target inside one word, the braced word is not the form, the form is not in the word
    ///     image or the label stands for no analysis of the form.
    /// </remarks>
    /// <param name="path">The file path.</param>
    /// <returns>The valid rows and one message per invalid row.</returns>
    public static (List<ChooserRow> Rows, List<string> Errors) ReadSentencesLenient(string path)
    {
        var name = Path.GetFileName(path);
        var rows = new List<ChooserRow>();
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

            var (form, label, sentence) = (columns[0].Trim(), columns[1].Trim(), columns[2].Trim());
            if (form.Length == 0 || label.Length == 0 || !string.Equals(form, form.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errors.Add($"{name}:{number}: the form must be lower-case and the label must be non-empty.");
                continue;
            }

            var (words, targetIndex, problem) = Corpus.SplitTarget(sentence);
            if (problem is not null)
            {
                errors.Add($"{name}:{number}: {problem}");
                continue;
            }

            if (!string.Equals(words![targetIndex], form, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{name}:{number}: the braced word '{words[targetIndex]}' is not the form '{form}'.");
                continue;
            }

            if (CheckLabel(form, label) is { } disagreement)
            {
                errors.Add($"{name}:{number}: {disagreement}");
                continue;
            }

            rows.Add(new ChooserRow(form, label, sentence, name, words, targetIndex));
        }

        return (rows, errors);
    }

    /// <summary>
    ///     Checks the anchors and the holdout against the word image and against each other.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <returns>The errors: a sentence that occurs twice, in either set or across both (compared after normalization, as in the sense corpus); a label of a form that stands for no analysis of the form is already refused by the reader.</returns>
    public static CorpusValidation Validate(IReadOnlyList<ChooserRow> anchors, IReadOnlyList<ChooserRow> holdout)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(holdout);
        var errors = new List<string>();
        var tagged = anchors.Select(r => ("choosers/anchors", r.Source, Corpus.Normalize(r.Sentence)))
            .Concat(holdout.Select(r => ("choosers/holdout", r.Source, Corpus.Normalize(r.Sentence))));
        try
        {
            Corpus.EnsureDistinct(tagged, true);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(ex.Message);
        }

        return new CorpusValidation(errors, []);
    }

    /// <summary>
    ///     Checks that a label stands for an analysis of a form.
    /// </summary>
    /// <param name="form">The lower-case form.</param>
    /// <param name="label">The label: a lemma, or <c>lemma/features</c>.</param>
    /// <returns>A message that describes the problem, or null when the label is valid.</returns>
    public static string? CheckLabel(string form, string label)
    {
        if (!WordLexicon.TryLookup(form, out var analyses) || analyses.IsEmpty)
        {
            return $"the form '{form}' is not in the word image.";
        }

        return analyses.Any(a => StandsFor(label, a))
            ? null
            : $"the label '{label}' stands for no analysis of the form '{form}'.";
    }

    /// <summary>
    ///     Finds the label that stands for an analysis: the label of its lemma and features, else the label of its lemma.
    /// </summary>
    /// <param name="labels">The labels of a chooser.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The index of the label, or -1 when there is none.</returns>
    public static int LabelIndexOf(IReadOnlyList<string> labels, WordDetails analysis)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(analysis);
        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], analysis.Lemma + "/" + analysis.Features, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], analysis.Lemma, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool StandsFor(string label, WordDetails analysis) =>
        string.Equals(label, analysis.Lemma, StringComparison.Ordinal)
        || string.Equals(label, analysis.Lemma + "/" + analysis.Features, StringComparison.Ordinal);

    /// <summary>
    ///     Lists the analyses of a form (an empty list for an unknown form).
    /// </summary>
    /// <param name="form">The lower-case form.</param>
    /// <returns>The analyses.</returns>
    public static ImmutableArray<WordDetails> AnalysesOf(string form) =>
        WordLexicon.TryLookup(form, out var analyses) ? analyses : [];
}
