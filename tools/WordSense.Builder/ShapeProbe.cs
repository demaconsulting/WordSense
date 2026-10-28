using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One sentence of the shape probe set: a noun or a verb in a common sentence shape, with its gold part of speech.
/// </summary>
/// <param name="Id">The unique id of the row, for example <c>obj-001</c>.</param>
/// <param name="Shape">The shape of the sentence, one of <see cref="ShapeProbeFile.Shapes"/>.</param>
/// <param name="Lemma">The lemma of the target word.</param>
/// <param name="Gold">The gold part of speech of the target: <see cref="PartOfSpeech.Noun"/> for a noun shape, <see cref="PartOfSpeech.Verb"/> for a verb shape.</param>
/// <param name="Words">The words of the sentence, without braces.</param>
/// <param name="TargetIndex">The index of the target word in <paramref name="Words"/>.</param>
internal sealed record ShapeProbeRow(string Id, string Shape, string Lemma, PartOfSpeech Gold, IReadOnlyList<string> Words, int TargetIndex);

/// <summary>
///     Reads the shape probe set (<c>corpus/shapes/probe.tsv</c>).
/// </summary>
/// <remarks>
///     <para>
///     The probe set is a held-out evaluation set of sentences in which a word that can be both a noun and a verb is used in
///     one of seven common shapes: five noun shapes (the object of a light verb, the head of a noun compound, a modifier of
///     a noun, a sentence-initial subject, a measurement or quantity) and two verb shapes (a sentence-initial imperative, a
///     verb after a modal or to). Every sentence has one target word and one gold part of speech, so the evaluation is a
///     noun/verb decision per word. No training step reads the file, and no setting is chosen on it.
///     </para>
///     <para>
///     <c>probe.tsv</c> has the header <c>id, shape, lemma, gold, target_index, sentence</c>. The gold is <c>Noun</c> or
///     <c>Verb</c> and follows from the shape; the sentence is the words separated by single spaces (punctuation as
///     separate words) with the target word in braces, and <c>target_index</c> is its zero-based index.
///     </para>
/// </remarks>
internal static class ShapeProbeFile
{
    /// <summary>The folder of the probe set in the corpus.</summary>
    public const string Folder = "shapes";

    /// <summary>The name of the file with the rows.</summary>
    public const string FileName = "probe.tsv";

    /// <summary>The confidence from which a noun answered as a verb counts as harmful.</summary>
    public const double HarmfulConfidence = 0.45;

    /// <summary>The shapes of the probe set and the gold part of speech of each, in report order.</summary>
    public static readonly IReadOnlyList<(string Shape, PartOfSpeech Gold)> Shapes =
    [
        ("n-obj-light", PartOfSpeech.Noun),
        ("n-compound-head", PartOfSpeech.Noun),
        ("n-modifier", PartOfSpeech.Noun),
        ("n-subject-initial", PartOfSpeech.Noun),
        ("n-measure", PartOfSpeech.Noun),
        ("v-imperative-initial", PartOfSpeech.Verb),
        ("v-after-modal", PartOfSpeech.Verb),
    ];

    private const string Header = "id\tshape\tlemma\tgold\ttarget_index\tsentence";

    /// <summary>
    ///     Gets the path of the probe file of a corpus directory.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The path of <c>shapes/probe.tsv</c>.</returns>
    public static string PathOf(string corpus) => Path.Join(corpus, Folder, FileName);

    /// <summary>
    ///     Reads the rows of a probe file and validates every one.
    /// </summary>
    /// <remarks>
    ///     A row is rejected when it does not have six columns, repeats an id, names an unknown shape, has a gold that is not
    ///     the part of speech of its shape, has no or more than one braced target, or has a <c>target_index</c> that is not the
    ///     index of the braced word.
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <returns>The rows in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static IReadOnlyList<ShapeProbeRow> ReadRows(string path)
    {
        var name = Path.GetFileName(path);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || !string.Equals(lines[0].TrimEnd('\r'), Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}:1: the header must be '{Header.Replace("\t", ", ", StringComparison.Ordinal)}'.");
        }

        var rows = new List<ShapeProbeRow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var where = $"{name}:{(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var columns = line.Split('\t');
            if (columns.Length != 6)
            {
                throw new InvalidDataException($"{where}: expected 6 tab-separated columns but found {columns.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            var row = ParseRow(columns, where);
            if (!ids.Add(row.Id))
            {
                throw new InvalidDataException($"{where}: the id '{row.Id}' occurs twice.");
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    ///     Reads the probe file of a corpus directory.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The rows, or an empty list when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static IReadOnlyList<ShapeProbeRow> Read(string corpus)
    {
        var path = PathOf(corpus);
        return File.Exists(path) ? ReadRows(path) : [];
    }

    private static ShapeProbeRow ParseRow(string[] columns, string where)
    {
        var (id, shape, lemma, goldText, indexText, sentence) = (columns[0], columns[1], columns[2], columns[3], columns[4], columns[5]);
        if (id.Length == 0 || lemma.Length == 0)
        {
            throw new InvalidDataException($"{where}: the id and the lemma must not be empty.");
        }

        var known = Shapes.Where(s => string.Equals(s.Shape, shape, StringComparison.Ordinal)).Select(s => ((string Shape, PartOfSpeech Gold)?)s).FirstOrDefault();
        if (known is null)
        {
            throw new InvalidDataException($"{where}: the shape '{shape}' is not one of {string.Join(", ", Shapes.Select(s => s.Shape))}.");
        }

        if (!string.Equals(goldText, known.Value.Gold.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{where}: the gold '{goldText}' must be {known.Value.Gold} for the shape '{shape}'.");
        }

        var tokens = sentence.Split(' ');
        var targets = tokens.Select((t, i) => (Token: t, Index: i)).Where(t => t.Token.Length > 2 && t.Token[0] == '{' && t.Token[^1] == '}').ToList();
        var braces = sentence.Count(c => c is '{' or '}');
        if (targets.Count != 1 || braces != 2 || tokens.Any(t => t.Length == 0))
        {
            throw new InvalidDataException($"{where}: the sentence must have single-space separated words and exactly one braced target word.");
        }

        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var targetIndex) || targetIndex != targets[0].Index)
        {
            throw new InvalidDataException($"{where}: the target_index '{indexText}' is not the index of the braced word ({targets[0].Index.ToString(CultureInfo.InvariantCulture)}).");
        }

        var words = tokens.Select(t => t.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal)).ToArray();
        return new ShapeProbeRow(id, shape, lemma, known.Value.Gold, words, targetIndex);
    }
}
