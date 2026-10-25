using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One token of the open part-of-speech corpus.
/// </summary>
/// <param name="Form">The token as tokenized by the corpus (a contraction is split: <c>do</c> and <c>n't</c>).</param>
/// <param name="Upos">The universal part-of-speech tag.</param>
/// <param name="Consensus">The consensus status: <c>unanimous</c>, <c>disputed</c> or <c>unchecked</c>.</param>
/// <param name="Lemma">The lemma of the token, or empty when a caller that builds a token by hand gives none.</param>
/// <param name="Features">The morphological features of the token (<c>Tense=Past|VerbForm=Part</c>), or empty when it has none or a caller that builds a token by hand gives none.</param>
internal sealed record OpenToken(string Form, string Upos, string Consensus, string Lemma = "", string Features = "");

/// <summary>
///     One sentence of the open part-of-speech corpus.
/// </summary>
/// <param name="SentenceId">The sentence id.</param>
/// <param name="SourceId">The id of the document the sentence comes from.</param>
/// <param name="Text">The sentence text.</param>
/// <param name="Tokens">The tokens.</param>
internal sealed record OpenSentence(string SentenceId, string SourceId, string Text, IReadOnlyList<OpenToken> Tokens);

/// <summary>
///     Reads the open English part-of-speech corpus (<c>corpus/open</c>): the representative part, a random sample that
///     is the only input for usage statistics and for the real-text evaluation, and the targeted part, whose
///     test split is a challenge set that is reported separately.
/// </summary>
/// <remarks>
///     <para>
///     A data file (<c>&lt;part&gt;/data/&lt;split&gt;.tsv</c>) has header comment lines, then sentences separated by a
///     blank line. A sentence starts with the comment lines <c># sent_id = ...</c>, <c># source_id = ...</c> and
///     <c># text = ...</c> (the targeted part adds <c># selection</c> and <c># query</c>), followed by one token per line
///     with seven tab-separated columns: id, form, lemma, universal tag, collapsed tag, features, consensus.
///     </para>
///     <para>
///     The corpus tokenizes contractions into two tokens (<c>do</c> and <c>n't</c>, <c>it</c> and <c>'s</c>), but a text
///     handed to the tagger by an application usually has the contraction as one token, as the treebank-trained tagger saw it.
///     <see cref="ToTagged"/> therefore joins them again (the joined token has the tag of its first part) so that the
///     counts and the evaluation present the classifier the same tokens an application does.
///     </para>
/// </remarks>
internal static class OpenCorpus
{
    /// <summary>
    ///     The folder of the open corpus below the corpus directory.
    /// </summary>
    public const string Folder = "open";

    /// <summary>
    ///     The name of the representative part.
    /// </summary>
    public const string Representative = "representative";

    /// <summary>
    ///     The name of the targeted part.
    /// </summary>
    public const string Targeted = "targeted";

    private const int TokenColumns = 7;

    private static readonly string[] Clitics = ["n't", "'s", "'re", "'m", "'ll", "'ve", "'d"];

    /// <summary>
    ///     Gets the directory of the count files (the tag statistics of the representative training split).
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The directory.</returns>
    public static string CountsDirectory(string corpus) => Path.Join(corpus, Folder, "counts");

    /// <summary>
    ///     Gets the path of the data file of a split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="part">The part: <see cref="Representative"/> or <see cref="Targeted"/>.</param>
    /// <param name="split">The split: <c>train</c>, <c>dev</c> or <c>test</c>.</param>
    /// <returns>The path.</returns>
    public static string DataFile(string corpus, string part, string split) =>
        Path.Join(corpus, Folder, part, "data", split + ".tsv");

    /// <summary>
    ///     Reads the sentences of one split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="part">The part: <see cref="Representative"/> or <see cref="Targeted"/>.</param>
    /// <param name="split">The split: <c>train</c>, <c>dev</c> or <c>test</c>.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is malformed.</exception>
    public static List<OpenSentence> ReadSplit(string corpus, string part, string split) =>
        ReadFile(DataFile(corpus, part, split));

    /// <summary>
    ///     Reads one data file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The sentences; the header comment lines belong to no sentence.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<OpenSentence> ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The open corpus file '{path}' was not found.", path);
        }

        var sentences = new List<OpenSentence>();
        string? sentenceId = null;
        var sourceId = string.Empty;
        var text = string.Empty;
        var tokens = new List<OpenToken>();
        var number = 0;

        void Flush()
        {
            if (sentenceId is not null && tokens.Count > 0)
            {
                sentences.Add(new OpenSentence(sentenceId, sourceId, text, [.. tokens]));
            }

            sentenceId = null;
            tokens.Clear();
        }

        foreach (var raw in File.ReadLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
            }
            else if (line.StartsWith("# sent_id = ", StringComparison.Ordinal))
            {
                Flush();
                sentenceId = line["# sent_id = ".Length..];
            }
            else if (line.StartsWith("# source_id = ", StringComparison.Ordinal))
            {
                sourceId = line["# source_id = ".Length..];
            }
            else if (line.StartsWith("# text = ", StringComparison.Ordinal))
            {
                text = line["# text = ".Length..];
            }
            else if (line[0] != '#')
            {
                var columns = line.Split('\t');
                if (sentenceId is null || columns.Length != TokenColumns || !int.TryParse(columns[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    || columns[1].Length == 0 || columns[2].Length == 0 || columns[3].Length == 0)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)}:{number.ToString(CultureInfo.InvariantCulture)}: expected a token line with {TokenColumns.ToString(CultureInfo.InvariantCulture)} columns, a form, a lemma and a tag, inside a sentence.");
                }

                tokens.Add(new OpenToken(columns[1], columns[3], columns[6], columns[2], columns[5] == "_" ? string.Empty : columns[5]));
            }
        }

        Flush();
        return sentences;
    }

    /// <summary>
    ///     Turns a sentence into the tokens and tags the tagger works with: a contraction that the corpus splits is joined
    ///     again when the joined form is in the sentence text, and the universal tags are mapped to tagger tags.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The tagged sentence; a joined token has the tag of its first part, and the possessive <c>'s</c>, which is a
    /// separate token in running text, stays one.</returns>
    public static TaggedSentence ToTagged(OpenSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var text = Straighten(sentence.Text).ToLowerInvariant();
        var forms = new List<string>();
        var tags = new List<string>();
        var gold = new List<GoldToken>();
        foreach (var token in sentence.Tokens)
        {
            var lower = Straighten(token.Form).ToLowerInvariant();
            var possessive = lower == "'s" && token.Upos == "PART";
            if (forms.Count > 0
                && !possessive
                && Array.IndexOf(Clitics, lower) >= 0
                && char.IsLetter(forms[^1][^1])
                && text.Contains(Straighten(forms[^1]).ToLowerInvariant() + lower, StringComparison.Ordinal))
            {
                forms[^1] += token.Form;
                continue;
            }

            forms.Add(token.Form);
            tags.Add(token.Upos);
            gold.Add(new GoldToken(token.Lemma, string.Empty, token.Features));
        }

        return new TaggedSentence([.. forms], [.. forms.Select((f, i) => TaggerCounts.MapTag(tags[i], f))], [.. gold]);
    }

    private static string Straighten(string text) => text.Replace('\u2019', '\'');
}
