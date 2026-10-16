using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Generates the senses appendix of the user guide from the sense inventory and the forms of the corpus.
/// </summary>
/// <remarks>
///     The appendix lists every supported lemma with its senses (label, tags, group, one example sentence) and the
///     inflected forms it recognizes. The output is deterministic: it depends only on the content of
///     <c>corpus/senses</c>, <c>corpus/forms</c> and <c>corpus/anchors</c>, uses ordinal ordering throughout, ends
///     lines with LF and wraps text at <see cref="LineLimit"/> characters so the file passes the markdown line-length
///     rule without an exemption.
/// </remarks>
internal static class AppendixGenerator
{
    /// <summary>
    ///     The longest line written, matching the markdown line-length rule of the repository.
    /// </summary>
    public const int LineLimit = 120;

    /// <summary>
    ///     The shortest example sentence preferred, in characters; shorter sentences are rarely informative.
    /// </summary>
    private const int MinimumExampleLength = 30;

    /// <summary>
    ///     Generates the appendix text.
    /// </summary>
    /// <param name="corpus">The corpus directory with the <c>senses</c>, <c>forms</c> and <c>anchors</c> subdirectories.</param>
    /// <returns>The markdown text, with LF line endings and a final line feed.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found when the input is incomplete or inconsistent.</exception>
    public static string Generate(string corpus)
    {
        var sensesDirectory = Path.Combine(corpus, "senses");
        var formsDirectory = Path.Combine(corpus, "forms");
        var anchorsDirectory = Path.Combine(corpus, "anchors");
        var errors = new List<string>();
        foreach (var directory in new[] { sensesDirectory, formsDirectory, anchorsDirectory }.Where(d => !Directory.Exists(d)))
        {
            errors.Add($"The corpus directory '{directory}' was not found.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var inventory = SenseInventory.ReadDirectory(sensesDirectory);
        var rows = inventory.Rows.ToList();
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"The sense inventory '{sensesDirectory}' contains no senses.");
        }

        var forms = Directory.GetFiles(formsDirectory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(Corpus.ReadForms).ToList();
        foreach (var (form, lemma, labels) in forms.Where(f => inventory.ContainsLemma(f.Lemma)))
        {
            foreach (var label in labels.Where(l => !inventory.Contains(lemma, l)))
            {
                errors.Add($"forms: form '{form}' of lemma '{lemma}' lists label '{label}', which is not a sense of the lemma.");
            }
        }

        var examples = FindExamples(Corpus.ReadDirectory(anchorsDirectory));
        errors.AddRange(rows.Where(r => !examples.ContainsKey((r.Lemma, r.Sense)))
            .Select(r => $"The sense '{r.Lemma}/{r.Sense}' has no anchor sentence to use as an example."));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        return Render(rows, forms, examples);
    }

    /// <summary>
    ///     Writes the appendix to a file: UTF-8 without a byte-order mark, LF line endings.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>The number of lemmas listed.</returns>
    public static int Write(string corpus, string output)
    {
        var text = Generate(corpus);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, text, new UTF8Encoding(false));
        return text.Split('\n').Count(l => l.StartsWith("- **", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Picks one example sentence per sense: the shortest anchor of at least <see cref="MinimumExampleLength"/>
    ///     characters (ties broken ordinally), or the longest anchor when none is that long.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <returns>The braced example sentence by lemma and sense.</returns>
    private static Dictionary<(string Lemma, string Sense), string> FindExamples(IEnumerable<CorpusRow> anchors)
    {
        return anchors.GroupBy(r => (r.Lemma, r.Sense)).ToDictionary(
            g => g.Key,
            g =>
            {
                var sentences = g.Select(r => r.Sentence).ToList();
                var candidates = sentences.Where(s => s.Length >= MinimumExampleLength).ToList();
                return candidates.Count > 0
                    ? candidates.OrderBy(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).First()
                    : sentences.OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).First();
            });
    }

    /// <summary>
    ///     Renders the document.
    /// </summary>
    private static string Render(
        List<InventoryRow> rows,
        List<(string Form, string Lemma, string[] Labels)> forms,
        Dictionary<(string Lemma, string Sense), string> examples)
    {
        var lines = new List<string>
        {
            "# Appendix: Supported Senses and Tags",
            string.Empty,
            "<!-- Generated by 'WordSense.Builder generate-appendix' from corpus/senses and corpus/forms. Do not edit. -->",
            string.Empty,
        };

        var lemmas = rows.GroupBy(r => r.Lemma).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        AddExplanation(lines, rows, lemmas.Count);
        AddLemmas(lines, lemmas, forms, examples);

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    ///     Adds the explanation of tags, groups and forms.
    /// </summary>
    private static void AddExplanation(List<string> lines, List<InventoryRow> rows, int lemmaCount)
    {
        lines.Add("## How Tags Work");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "WordSense decides which **sense** of a **lemma** a word carries in its sentence. The lemma is the " +
            "dictionary form of the word (for example `record`), and a sense is one of the meanings the library has " +
            "learned for it (for example `noun` and `verb`). Every sense has a label that is unique within its lemma, " +
            "a set of **tags** and a **group**.");

        lines.Add("### Tags");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "Each sense has exactly one **part of speech** tag: `noun`, `verb`, `adj` or `adv`. It may also carry " +
            "**qualifier** tags that refine it, such as the tense of a verb form. " +
            "The tags of a sense are what `WordSense.Tags`, `WordSense.HasTag`, `ClassifyTagAsync` and `IsTagAsync` " +
            "work with. A tag query scores the best sense that carries the tag, so a coarse question such as noun " +
            "versus verb does not depend on how finely a lemma is split into senses.");
        lines.Add("| Tag           | Kind           | Meaning                      | Senses |");
        lines.Add("|---------------|----------------|------------------------------|--------|");
        foreach (var (tag, meaning) in TagMeanings)
        {
            var kind = PosRules.PartsOfSpeech.Contains(tag) ? "part of speech" : "qualifier";
            var count = rows.Count(r => r.Tags.Contains(tag));
            lines.Add($"| {"`" + tag + "`",-13} | {kind,-14} | {meaning,-28} | {count,6} |");
        }

        lines.Add(string.Empty);
        AddParagraph(lines,
            "For example, `read` has two senses: `past` (tags `past` and `verb`, pronounced like \"red\") and " +
            "`present` (tags `present` and `verb`, pronounced like \"reed\"). Both answer `true` to " +
            "`IsTagAsync(words, \"verb\")`, while only the first answers `true` for `past`.");

        lines.Add("### Groups");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "A **group** is a coarser label that joins senses a consumer usually does not need to tell apart. " +
            "`WordSense.Group` is the group of the sense. It equals the sense label unless the inventory joins " +
            "senses: for example `bow` has the senses `bend-noun` and `bend-verb` (the act of bending, as a noun or a " +
            "verb), which share the group `bend`, and `weapon`, which is a group of its own. A consumer that does " +
            "not care about the split reads `Group` instead of `Sense`.");

        lines.Add("### Forms");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "Inflected forms are mapped to their lemma by the forms map (`corpus/forms`), so `presents` is " +
            "classified as a form of `present`. A form may be restricted to some senses of the " +
            "lemma, for example `led` can only be a form of the verb sense of `lead`; such a restriction is " +
            "shown in parentheses after the form. Forms that are not listed are unknown and are never classified.");

        lines.Add("## Supported Lemmas");
        lines.Add(string.Empty);
        AddParagraph(lines,
            $"The library supports {lemmaCount} lemmas with {rows.Count} senses in total, listed alphabetically. For each " +
            "sense the list gives the sense label, the tags, the group and an example sentence in which the " +
            "classified word is shown in bold, followed by the inflected forms the lemma recognizes.");
    }

    /// <summary>
    ///     Adds the alphabetical lemma list.
    /// </summary>
    private static void AddLemmas(
        List<string> lines,
        List<IGrouping<string, InventoryRow>> lemmas,
        List<(string Form, string Lemma, string[] Labels)> forms,
        Dictionary<(string Lemma, string Sense), string> examples)
    {
        var formsByLemma = forms.GroupBy(f => f.Lemma).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var letter in lemmas.GroupBy(l => char.ToUpperInvariant(l.Key[0])).OrderBy(g => g.Key))
        {
            lines.Add($"### Lemmas Starting With {letter.Key}");
            lines.Add(string.Empty);
            foreach (var lemma in letter)
            {
                var senses = lemma.OrderBy(r => r.Sense, StringComparer.Ordinal).ToList();
                var formText = DescribeForms(lemma.Key, senses, formsByLemma.GetValueOrDefault(lemma.Key));
                lines.AddRange(Wrap("- ", $"**{lemma.Key}**; {formText}", "  "));
                foreach (var sense in senses)
                {
                    var group = string.IsNullOrEmpty(sense.Group) ? sense.Sense : sense.Group;
                    var text = $"`{sense.Sense}`; tags: {string.Join(", ", sense.Tags)}; group: {group}; example: " +
                        Escape(examples[(sense.Lemma, sense.Sense)]).Replace("{", "**", StringComparison.Ordinal)
                            .Replace("}", "**", StringComparison.Ordinal);
                    lines.AddRange(Wrap("  - ", text, "    "));
                }
            }

            lines.Add(string.Empty);
        }

        // Drop the blank line after the last list so the file ends with exactly one line feed
        lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>
    ///     Describes the inflected forms of a lemma, other than the lemma itself.
    /// </summary>
    private static string DescribeForms(
        string lemma,
        List<InventoryRow> senses,
        List<(string Form, string Lemma, string[] Labels)>? forms)
    {
        var byForm = (forms ?? [])
            .Where(f => f.Form != lemma)
            .GroupBy(f => f.Form, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var labels = g.SelectMany(f => f.Labels).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                var restricted = labels.Count > 0 && labels.Count < senses.Count && !g.Any(f => f.Labels.Length == 0);
                return restricted ? $"{g.Key} ({string.Join(", ", labels)})" : g.Key;
            })
            .ToList();
        return byForm.Count == 0 ? "no other forms" : "forms: " + string.Join(", ", byForm);
    }

    /// <summary>
    ///     Adds a wrapped paragraph followed by a blank line.
    /// </summary>
    private static void AddParagraph(List<string> lines, string text)
    {
        lines.AddRange(Wrap(string.Empty, text, string.Empty));
        lines.Add(string.Empty);
    }

    /// <summary>
    ///     Wraps text at <see cref="LineLimit"/> characters on spaces.
    /// </summary>
    /// <param name="prefix">The start of the first line, such as a list marker with its indentation.</param>
    /// <param name="text">The text.</param>
    /// <param name="continuation">The indentation of the continuation lines.</param>
    /// <returns>The lines.</returns>
    private static List<string> Wrap(string prefix, string text, string continuation)
    {
        // A word that would read as a list marker, quote or heading at the start of a line stays with its predecessor
        var tokens = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tokens.Count > 0 && IsBlockMarker(word))
            {
                tokens[^1] += " " + word;
            }
            else
            {
                tokens.Add(word);
            }
        }

        var lines = new List<string>();
        var current = new StringBuilder(prefix);
        var empty = true;
        foreach (var token in tokens)
        {
            if (!empty && current.Length + 1 + token.Length > LineLimit)
            {
                lines.Add(current.ToString());
                current.Clear().Append(continuation);
                empty = true;
            }

            if (!empty)
            {
                current.Append(' ');
            }

            current.Append(token);
            empty = false;
        }

        lines.Add(current.ToString());
        return lines;
    }

    /// <summary>
    ///     Determines whether a word would start a block (list item, quote, heading or table) at the start of a line.
    /// </summary>
    private static bool IsBlockMarker(string word)
    {
        return word is "-" or "+" or "*" or ">" or "|" || word.StartsWith('#') || word.StartsWith("---", StringComparison.Ordinal)
            || (word.Length > 1 && char.IsAsciiDigit(word[0]) && word.TrimEnd('.', ')') != word && word.TrimEnd('.', ')').All(char.IsAsciiDigit));
    }

    /// <summary>
    ///     Escapes the characters that markdown would read as emphasis, code, links or HTML.
    /// </summary>
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '<' or '>' or '[' or ']' or '|' or '&')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>
    ///     The meaning of every tag that is described in the appendix.
    /// </summary>
    private static readonly (string Tag, string Meaning)[] TagMeanings =
    [
        ("adj", "adjective"),
        ("adv", "adverb"),
        ("noun", "noun"),
        ("verb", "verb"),
        ("comparative", "comparative degree (closer)"),
        ("participle", "participle form"),
        ("past", "past tense"),
        ("present", "present tense"),
        ("superlative", "superlative degree"),
    ];
}
