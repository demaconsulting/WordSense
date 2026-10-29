using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The outcome of one scored token of a running-text set: what the gold says, what a model answered and whether the
///     token counts as ambiguous.
/// </summary>
/// <param name="Set">The name of the set.</param>
/// <param name="Sentence">The index of the sentence in its set.</param>
/// <param name="Document">The document of the sentence (the source id of the open corpus; for a set without documents the sentence).</param>
/// <param name="Token">The index of the token in its sentence.</param>
/// <param name="Word">The token as written.</param>
/// <param name="Gold">The gold part of speech.</param>
/// <param name="Answer">The part of speech of the selected analysis of the model.</param>
/// <param name="Correct">Whether the answer is the gold part of speech.</param>
/// <param name="Ambiguous">Whether the word has at least two parts of speech in the lexicon.</param>
internal sealed record TokenOutcome(string Set, int Sentence, string Document, int Token, string Word, string Gold, string Answer, bool Correct, bool Ambiguous);

/// <summary>
///     Writes and reads the per-token outcomes of <c>evaluate-running --outcomes</c>.
/// </summary>
/// <remarks>
///     The file is tab-separated with the header <c>model, set, sentence, document, token, word, gold, answer, correct,
///     ambiguous</c>; <c>correct</c> and <c>ambiguous</c> are 1 or 0. A token is listed when it is scored (the model
///     answers it and its gold tag is a word class); two files of two versions of a model are paired on the set, the
///     sentence and the token.
/// </remarks>
internal static class OutcomeFile
{
    private const string Header = "model\tset\tsentence\tdocument\ttoken\tword\tgold\tanswer\tcorrect\tambiguous";

    /// <summary>
    ///     Gets the name of a part of speech tag of the tagger as the gold and answer columns write it.
    /// </summary>
    /// <param name="tag">The tag (see <see cref="PosTag"/>); the infinitive marker counts as a preposition.</param>
    /// <returns>The name of the part of speech.</returns>
    public static string NameOf(int tag) => ((PartOfSpeech)(tag == PosTag.InfinitiveTo ? PosTag.Preposition : tag)).ToString();

    /// <summary>
    ///     Renders outcomes as the text of an outcome file.
    /// </summary>
    /// <param name="outcomes">The outcomes with the name of the model of each.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(IEnumerable<(string Model, TokenOutcome Outcome)> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var text = new StringBuilder(Header).Append('\n');
        foreach (var (model, o) in outcomes)
        {
            text.Append(model).Append('\t').Append(o.Set).Append('\t')
                .Append(o.Sentence.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(o.Document).Append('\t')
                .Append(o.Token.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(o.Word).Append('\t')
                .Append(o.Gold).Append('\t').Append(o.Answer).Append('\t')
                .Append(o.Correct ? '1' : '0').Append('\t').Append(o.Ambiguous ? '1' : '0').Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads an outcome file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The outcomes with the name of the model of each.</returns>
    /// <exception cref="InvalidDataException">Thrown when the header or a line is malformed.</exception>
    public static List<(string Model, TokenOutcome Outcome)> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        var lines = File.ReadLines(path, new UTF8Encoding(false)).Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}: the header of an outcome file is '{Header.Replace('\t', ',')}'.");
        }

        var result = new List<(string, TokenOutcome)>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var c = lines[i].Split('\t');
            if (c.Length != 10 || !int.TryParse(c[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sentence) || !int.TryParse(c[4], NumberStyles.None, CultureInfo.InvariantCulture, out var token) || c[8] is not ("0" or "1") || c[9] is not ("0" or "1"))
            {
                throw new InvalidDataException($"{name}:{(i + 1).ToString(CultureInfo.InvariantCulture)}: expected ten columns with numbers in the sentence and token columns and 0 or 1 in the last two.");
            }

            result.Add((c[0], new TokenOutcome(c[1], sentence, c[3], token, c[5], c[6], c[7], c[8] == "1", c[9] == "1")));
        }

        return result;
    }
}
