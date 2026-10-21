using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One irregular inflection of a word: the forms a lemma takes for one rule, such as <c>go</c> with the past form <c>went</c>.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Pos">The part of speech of the forms.</param>
/// <param name="Rule">The rule: <c>Past</c>, <c>Participle</c>, <c>Plural</c>, <c>Comparative</c>, <c>Superlative</c>, <c>Variant</c>, <c>Auxiliary</c> or <c>Modal</c>.</param>
/// <param name="Forms">The forms.</param>
/// <param name="Evidence">The evidence tokens of the row (one variant level per form in an AGID row).</param>
internal sealed record IrregularEntry(string Lemma, PartOfSpeech Pos, string Rule, string[] Forms, string[] Evidence);

/// <summary>
///     The irregular inflected forms of the dictionary words: a hand-written closed list of facts of the language
///     (<c>corpus/lexicon/irregular-forms.tsv</c>), read by the form generator and by the tests.
/// </summary>
/// <remarks>
///     <para>
///     The files of this kind have the columns lemma, part-of-speech letter, rule, space-separated forms and evidence; the
///     evidence of an AGID row holds one variant level per form.
///     </para>
/// </remarks>
internal static class IrregularForms
{
    /// <summary>
    ///     The name of the hand-written list.
    /// </summary>
    public const string HandFile = "irregular-forms.tsv";

    /// <summary>
    ///     Reads the hand-written irregular list of a lexicon directory.
    /// </summary>
    /// <param name="directory">The lexicon directory.</param>
    /// <returns>The entries; empty when the list does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<IrregularEntry> Read(string directory) => ReadFile(Path.Join(directory, HandFile));

    /// <summary>
    ///     Reads one irregular list.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The entries; empty when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<IrregularEntry> ReadFile(string path)
    {
        var entries = new List<IrregularEntry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        var name = Path.GetFileName(path);
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
            if (columns.Length < 4 || columns[1].Length != 1 || LexiconSource.Letters.IndexOf(columns[1][0]) < 0)
            {
                throw new InvalidDataException($"{name} line {number.ToString(CultureInfo.InvariantCulture)}: expected 'lemma<TAB>letter<TAB>rule<TAB>forms<TAB>evidence'.");
            }

            entries.Add(new IrregularEntry(
                columns[0],
                (PartOfSpeech)LexiconSource.Letters.IndexOf(columns[1][0]),
                columns[2],
                columns[3].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                columns.Length > 4 ? columns[4].Split(' ', StringSplitOptions.RemoveEmptyEntries) : []));
        }

        return entries;
    }
}
