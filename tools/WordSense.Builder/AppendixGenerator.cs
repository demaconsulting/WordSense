using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Generates the senses appendix of the user guide from the sense inventory and the forms of the corpus.
/// </summary>
/// <remarks>
///     The appendix lists every supported lemma with its parts of speech, pronunciation meanings, senses (with one
///     example sentence) and the
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
        errors.AddRange(rows.Where(r => !examples.ContainsKey((r.Lemma, r.Label)))
            .Select(r => $"The sense '{r.Lemma}/{r.Label}' has no anchor sentence to use as an example."));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        return Render(corpus, rows, forms, examples);
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
        string corpus,
        List<InventoryRow> rows,
        List<(string Form, string Lemma, string[] Labels)> forms,
        Dictionary<(string Lemma, string Sense), string> examples)
    {
        var lines = new List<string>
        {
            "# Appendix: Supported Senses",
            string.Empty,
            "<!-- Generated by 'WordSense.Builder appendix' from corpus/senses and corpus/forms. Do not edit. -->",
            string.Empty,
        };

        var lemmas = rows.GroupBy(r => r.Lemma).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        AddExplanation(lines, rows, lemmas.Count);
        AddLexicon(lines, corpus, rows, forms);
        AddLemmas(lines, lemmas, forms, examples);

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    ///     Adds the summary of the dictionary words, when the corpus has a lexicon: the counts per part-of-speech set and
    ///     the state of the morphology rules. The words themselves are not listed.
    /// </summary>
    private static void AddLexicon(
        List<string> lines,
        string corpus,
        List<InventoryRow> rows,
        List<(string Form, string Lemma, string[] Labels)> forms)
    {
        var lexiconDirectory = Path.Combine(corpus, "lexicon");
        var lexiconPath = Path.Combine(lexiconDirectory, LexiconSource.LexiconFile);
        if (!File.Exists(lexiconPath))
        {
            return;
        }

        var words = LexiconSource.ReadWords(lexiconPath);
        var taken = new HashSet<string>(rows.Select(r => r.Lemma), StringComparer.Ordinal);
        taken.UnionWith(forms.Select(f => f.Form));
        var all = words.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var embedded = words.Count(w => !taken.Contains(w.Word)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var sets = words
            .GroupBy(w => LexiconSource.ToLetters(w.Mask))
            .Select(g => (Letters: g.Key, Count: g.Count(), Name: SetName(g.Key)))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        lines.Add("### Dictionary Words");
        lines.Add(string.Empty);
        AddParagraph(lines,
            $"Besides the lemmas listed below, the lexicon list has {all:N0} words (derived from the " +
            "public-domain Moby Part-of-Speech II list, see `corpus/lexicon/PROVENANCE.md`), of which " +
            $"{embedded:N0} are embedded as dictionary words. A dictionary word has no " +
            "trained classifier: a word with one part of speech is answered directly, and a word with several parts " +
            "of speech returns them as candidates. A word that a lemma or a form of the list below spells is not " +
            "embedded as a dictionary word, because the lemma takes precedence. A capitalized word is " +
            "lower-cased before it is looked up. The words are not " +
            "listed here; this is how many words of the lexicon list have each set of parts of speech:");
        var width = Math.Max(sets.Max(s => s.Name.Length), "Parts of speech".Length);
        lines.Add($"| {"Parts of speech".PadRight(width)} | {"Words",7} |");
        lines.Add($"|{new string('-', width + 2)}|{new string('-', 9)}|");
        foreach (var set in sets)
        {
            lines.Add($"| {set.Name.PadRight(width)} | {set.Count,7} |");
        }

        lines.Add(string.Empty);

        var morphology = LexiconSource.ReadMorphologyIfPresent(lexiconDirectory);
        if (morphology is not null)
        {
            var enabled = morphology.Where(m => m.Enabled).Select(m => $"`{m.Rule}`").ToList();
            var disabled = morphology.Where(m => !m.Enabled).Select(m => $"`{m.Rule}`").ToList();
            AddParagraph(lines,
                "The word table also holds the inflected forms that suffix rules generate from dictionary words. Rules enabled: " +
                (enabled.Count == 0 ? "none" : string.Join(", ", enabled)) + ". Rules disabled because they did not " +
                "reach 95 percent precision on real text: " + (disabled.Count == 0 ? "none" : string.Join(", ", disabled)) + ".");
        }
    }

    /// <summary>
    ///     Names a set of parts of speech given as letters.
    /// </summary>
    private static string SetName(string letters)
    {
        var names = letters.Select(c => ((DemaConsulting.WordSense.PartOfSpeech)LexiconSource.Letters.IndexOf(c, StringComparison.Ordinal)).ToString());
        return string.Join(", ", names);
    }

    /// <summary>
    ///     Adds the explanation of parts of speech, pronunciations and forms.
    /// </summary>
    private static void AddExplanation(List<string> lines, List<InventoryRow> rows, int lemmaCount)
    {
        lines.Add("## How Answers Work");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "WordSense decides which **part of speech** a word carries in its sentence and, for a heteronym, which " +
            "**pronunciation**. The lemma is the dictionary form of the word (for example `record`); a word such as " +
            "`records` is matched to its lemma through the forms listed below. The answer for a word is a lemma, a " +
            "part of speech and, for a heteronym only, a pronunciation.");

        lines.Add("### Parts of Speech");
        lines.Add(string.Empty);
        var posCount = rows.Select(r => r.Pos).Distinct().Count();
        var posWord = posCount is >= 0 and <= 8
            ? new[] { "none", "one", "two", "three", "four", "five", "six", "seven", "eight" }[posCount]
            : posCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var posNames = string.Join(", ", rows.Select(r => r.Pos).Distinct().Order().Select(p => $"`{p}`"));
        AddParagraph(lines,
            "The `PartOfSpeech` enumeration defines all eight classic parts of speech. The trained senses listed " +
            $"here, which give the context answers, cover {posWord} of them ({posNames}). A dictionary answer can " +
            "also be a `Pronoun` or a `Preposition`; `Conjunction` and `Interjection` appear only as candidates of a " +
            "dictionary word that has other parts of speech too, never as an answer. The table counts the trained " +
            "senses of each part of speech:");
        lines.Add("| Part of speech | Senses |");
        lines.Add("| -------------- | -----: |");
        foreach (var pos in rows.Select(r => r.Pos).Distinct().Order())
        {
            lines.Add($"| {"`" + pos + "`",-14} | {rows.Count(r => r.Pos == pos),6} |");
        }

        lines.Add(string.Empty);

        lines.Add("### Pronunciation Meanings");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "A lemma is a **heteronym** when it has at least two distinct pronunciation meanings. Only then does an " +
            "answer carry a pronunciation: a short meaning id such as `guide` or `metal` for `lead`, and optionally a " +
            "plain respelling. The library has no phonetic data of its own; a consumer such as a speech synthesizer " +
            "keeps its own pronunciations keyed by (lemma, meaning). A lemma that is not a heteronym has no " +
            "pronunciation in its answers.");

        lines.Add("### Forms");
        lines.Add(string.Empty);
        AddParagraph(lines,
            "Inflected forms are mapped to their lemma by the forms map (`corpus/forms`), so `presents` is " +
            "classified as a form of `present`. A form may be restricted to some senses of the " +
            "lemma, for example `led` can only be a form of the verb of `lead`; such a restriction is " +
            "shown in parentheses after the form. Forms that are not listed are unknown and are never classified.");

        lines.Add("## Supported Lemmas");
        lines.Add(string.Empty);
        AddParagraph(lines,
            $"The library supports {lemmaCount} lemmas with {rows.Count} senses in total, listed alphabetically. Each " +
            "lemma lists its parts of speech and, for a heteronym, its pronunciation meanings and the inflected forms " +
            "it recognizes. Under it, every sense gives its part of speech, its meaning and respelling when it has " +
            "them, and an example sentence in which the classified word is shown in bold.");
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
                var senses = lemma.OrderBy(r => r.Label, StringComparer.Ordinal).ToList();
                var formText = DescribeForms(lemma.Key, senses, formsByLemma.GetValueOrDefault(lemma.Key));
                var posText = string.Join(", ", senses.Select(s => s.Pos).Distinct().Order().Select(p => p.ToString().ToLowerInvariant()));
                var meanings = senses.Where(s => s.Meaning is not null).Select(s => s.Meaning!).Distinct(StringComparer.Ordinal).ToList();
                var meaningText = meanings.Count == 0 ? string.Empty : $"; heteronym, meanings: {string.Join(", ", meanings)}";
                lines.AddRange(Wrap("- ", $"**{lemma.Key}**; parts of speech: {posText}{meaningText}; {formText}", "  "));
                foreach (var sense in senses)
                {
                    var text = $"{Describe(sense)}; example: " +
                        Escape(examples[(sense.Lemma, sense.Label)]).Replace("{", "**", StringComparison.Ordinal)
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
    ///     Describes a sense by its part of speech, meaning and respelling.
    /// </summary>
    private static string Describe(InventoryRow sense)
    {
        var text = $"`{sense.Pos.ToString().ToLowerInvariant()}`";
        if (sense.Meaning is not null)
        {
            text += $"; meaning: `{sense.Meaning}`";
        }

        if (sense.Respelling is not null)
        {
            text += $"; respelling: `{sense.Respelling}`";
        }

        return text;
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
                var named = labels.Select(l => senses.First(s => s.Label == l)).Select(s => s.Meaning is null ? s.Pos.ToString().ToLowerInvariant() : $"{s.Pos.ToString().ToLowerInvariant()} {s.Meaning}");
                return restricted ? $"{g.Key} ({string.Join(", ", named)})" : g.Key;
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
}
