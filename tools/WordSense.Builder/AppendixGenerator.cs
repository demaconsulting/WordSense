using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using WordSense.Builder.Data;

namespace WordSense.Builder;

/// <summary>
///     Generates the senses appendix of the user guide from the sense catalog of the data and the trained lemmas of the
///     corpus.
/// </summary>
/// <remarks>
///     <para>
///     The appendix lists every trained lemma (a lemma that has sentences in <c>corpus/anchors</c>)
///     with its senses in inventory order, the order of Open English WordNet (the position of the sense in its entry, the
///     project senses last): the id, the group (none when the sense is in no
///     group), the Open English WordNet sense key (a project sense has none) and the
///     gloss. The senses come from the word image that <see cref="ImageCompiler"/> compiles from the data folder, so
///     the appendix lists exactly what the library answers; the glosses are not in the image and are read from the
///     <c>sense</c>, <c>sense_label</c>, <c>entry</c> and <c>gloss</c> tables. The recognized forms of a lemma come from
///     the image too.
///     </para>
///     <para>
///     The tier and anchor counts of the senses are not listed: the tier report (<c>tiers</c>) is the place for them.
///     The output is deterministic, uses ordinal ordering
///     throughout, ends lines with LF and wraps text at <see cref="LineLimit"/> characters so the file passes the
///     markdown line-length rule without an exemption.
///     </para>
/// </remarks>
internal static class AppendixGenerator
{
    /// <summary>
    ///     The longest line written, matching the markdown line-length rule of the repository.
    /// </summary>
    public const int LineLimit = 120;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    ///     One sense of a trained lemma with its gloss.
    /// </summary>
    private sealed record Listed(SenseInfo Sense, string Gloss);

    /// <summary>
    ///     Generates the appendix text.
    /// </summary>
    /// <param name="corpus">The corpus directory with the <c>anchors</c> subdirectory whose lemmas are the trained lemmas.</param>
    /// <param name="data">The data folder with the tables of the sense catalog.</param>
    /// <returns>The markdown text, with LF line endings and a final line feed.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found when the input is incomplete or inconsistent.</exception>
    public static string Generate(string corpus, string data)
    {
        var anchorsDirectory = Path.Join(corpus, "anchors");
        var errors = new List<string>();
        foreach (var directory in new[] { anchorsDirectory, data }.Where(d => !Directory.Exists(d)))
        {
            errors.Add($"The directory '{directory}' was not found.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var trained = Corpus.ReadLemmas(anchorsDirectory).Order(StringComparer.Ordinal).ToList();
        if (trained.Count == 0)
        {
            throw new InvalidDataException($"The anchors '{anchorsDirectory}' contain no sentences.");
        }

        var (bytes, _) = ImageCompiler.Build(data);
        var image = WordImage.Open(bytes);
        var senses = new Dictionary<string, List<SenseInfo>>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var lemma in trained)
        {
            var found = Enum.GetValues<PartOfSpeech>().SelectMany(pos => image.SensesOf(lemma, pos)).ToList();
            if (found.Count == 0)
            {
                missing.Add(lemma);
            }
            else
            {
                senses[lemma] = found;
            }
        }

        if (senses.Count == 0)
        {
            throw new InvalidDataException($"None of the trained lemmas of '{anchorsDirectory}' has a sense in the data '{data}'.");
        }

        var glosses = ReadGlosses(data, [.. senses.Keys]);
        foreach (var sense in senses.Values.SelectMany(s => s).Where(s => !glosses.ContainsKey(s.SenseId)))
        {
            errors.Add($"The sense '{sense.SenseId}' has no gloss.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var listed = senses.Keys.Order(StringComparer.Ordinal)
            .Select(l => (Lemma: l, Senses: senses[l].Select(s => new Listed(s, glosses[s.SenseId])).ToList())).ToList();
        return Render(image, listed, missing);
    }

    /// <summary>
    ///     Writes the appendix to a file: UTF-8 without a byte-order mark, LF line endings.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="data">The data folder.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>The number of lemmas listed.</returns>
    public static int Write(string corpus, string data, string output)
    {
        var text = Generate(corpus, data);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, text, new UTF8Encoding(false));
        return text.Split('\n').Count(l => l.StartsWith("- **", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Reads the glosses of the senses of some lemmas from the data tables.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The definitions of the synset of each sense joined with a semicolon, by public sense id.</returns>
    private static Dictionary<string, string> ReadGlosses(string data, List<string> lemmas)
    {
        var schema = DataSchema.Load(Path.Join(data, "schema.yaml"));
        var wanted = new HashSet<string>(lemmas, StringComparer.Ordinal);
        var entries = DataTables.Read(data, schema.Table("entry"))
            .Where(r => wanted.Contains(r[1]))
            .ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var labels = DataTables.Read(data, schema.Table("sense_label")).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var synsetOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in DataTables.Read(data, schema.Table("sense")))
        {
            if (entries.TryGetValue(row[2], out var lemma) && labels.TryGetValue(row[0], out var label))
            {
                synsetOf[lemma + "#" + label] = row[3];
            }
        }

        var needed = new HashSet<string>(synsetOf.Values, StringComparer.Ordinal);
        var definitions = DataTables.Read(data, schema.Table("gloss"))
            .Where(r => needed.Contains(r[0]))
            .GroupBy(r => r[0], StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => string.Join("; ", g.OrderBy(r => int.Parse(r[1], Inv)).Select(r => r[2].Trim())),
                StringComparer.Ordinal);
        var glosses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, synset) in synsetOf)
        {
            if (definitions.TryGetValue(synset, out var text) && text.Length > 0)
            {
                glosses[id] = text;
            }
        }

        return glosses;
    }

    /// <summary>
    ///     Renders the document.
    /// </summary>
    private static string Render(WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas, List<string> missing)
    {
        var lines = new List<string>
        {
            "# Appendix: Supported Senses",
            string.Empty,
            "<!-- Generated by 'WordSense.Builder appendix' from data and corpus/anchors. Do not edit. -->",
            string.Empty,
        };

        AddExplanation(lines, lemmas);
        AddDictionary(lines, image, lemmas, missing);
        AddLemmas(lines, image, lemmas);

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    ///     Adds the explanation of parts of speech, pronunciations, groups and forms.
    /// </summary>
    private static void AddExplanation(List<string> lines, List<(string Lemma, List<Listed> Senses)> lemmas)
    {
        var all = lemmas.SelectMany(l => l.Senses).Select(s => s.Sense).ToList();
        lines.Add("## How Answers Work");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "WordSense identifies the sense of a word in its sentence. The inventory of senses is the one of Open English " +
            "WordNet for the lemmas of one word, plus a few project senses for the words that Open English WordNet does " +
            "not cover (for example `the#determiner`). The lemma is the dictionary form of the word (for example " +
            "`record`); a word such as `records` is matched to its lemma through the forms listed below. An answer is a " +
            "lemma, a part of speech and, where the model is trained for the lemma, a sense or a group of senses.");

        lines.Add("### Senses");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "Every sense has a readable id of the form `lemma#label` (for example `bank#coin-bank`) that is stable and " +
            "never reused for another meaning, and is linked to one Open English WordNet sense key. A project sense has " +
            "no key. The senses of a lemma and part of speech are listed in the order of Open English WordNet, which puts " +
            "the commonly used senses first (the position of the sense in its entry); a project sense ranks last. Each " +
            "sense is listed with its gloss, the definition of " +
            "its Open English WordNet synset.");

        lines.Add("### Parts of Speech");
        lines.Add(string.Empty);
        var parts = all.Select(s => s.PartOfSpeech).Distinct().Order().ToList();
        AddParagraph(
            lines,
            "The `PartOfSpeech` enumeration defines ten parts of speech: the eight classic ones, `Determiner` and " +
            "`Auxiliary`. The trained lemmas listed here, which give the context answers, have senses of " +
            $"{parts.Count.ToString(Inv)} of them. A dictionary answer can also be a part of speech of a lemma that is " +
            "not listed, as the answer for a word with one part of speech or as a candidate of a word that has several. " +
            "The table counts the trained lemmas and senses of each part of speech:");
        lines.Add("| Part of speech | Lemmas | Senses |");
        lines.Add("| -------------- | -----: | -----: |");
        foreach (var pos in parts)
        {
            var lemmaCount = lemmas.Count(l => l.Senses.Any(s => s.Sense.PartOfSpeech == pos));
            lines.Add($"| {"`" + pos + "`",-14} | {lemmaCount,6} | {all.Count(s => s.PartOfSpeech == pos),6} |");
        }

        lines.Add(string.Empty);

        lines.Add("### Groups");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "A **group** is optional. It exists only where it was curated: a set of at least two, but not all, senses of " +
            "one lemma and part of speech that are closely related in meaning and that a reader can confuse. A sense is " +
            "in at most one group and needs none; the sense lines below show `none` for a sense in no group. When the " +
            "model cannot tell the senses of a group apart it answers with the group, which is the level between the part " +
            "of speech and the sense.");

        lines.Add("### Forms");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "Inflected forms are mapped to their lemma by the forms of the word table, so `presents` is analyzed as a " +
            "form of `present`. Each lemma below lists the forms the word table recognizes, other than the lemma itself. " +
            "A form that is not in the table is unknown and is never analyzed.");
    }

    /// <summary>
    ///     Adds the summary of the dictionary words: the size of the word table and the share of its senses that are trained.
    /// </summary>
    private static void AddDictionary(List<string> lines, WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas, List<string> missing)
    {
        var trainedSenses = lemmas.Sum(l => l.Senses.Count);
        var trainedGroups = lemmas.SelectMany(l => l.Senses).Select(s => s.Sense.GroupId).Where(g => g is not null).Distinct(StringComparer.Ordinal).Count();
        var project = lemmas.SelectMany(l => l.Senses).Count(s => s.Sense.SenseKey is null);
        lines.Add("### Dictionary Words");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            $"The word table knows {image.SpellingCount.ToString("N0", Inv)} spellings (lemmas and inflected forms) of " +
            $"{image.EntryCount.ToString("N0", Inv)} entries, and its sense catalog lists {image.SenseCount.ToString("N0", Inv)} " +
            $"senses in {image.GroupCount.ToString("N0", Inv)} curated groups. Besides the lemmas listed below, every " +
            "other word of the table is a dictionary word: it has no trained classifier, a word with one part of speech " +
            "is answered directly with confidence 1, and a word with several parts of speech returns them as candidates, " +
            "most frequent first (the encoder models answer it with the first one; the basic model chooses by tagger " +
            "and context, and by the case of the word when the spelling has both a capitalized and a lower-case lemma). " +
            "A capitalized word is looked up as written first and then in lower case.");
        AddParagraph(
            lines,
            $"The lemmas listed below are the {lemmas.Count.ToString("N0", Inv)} trained lemmas (the lemmas that have " +
            $"anchor sentences), with {trainedSenses.ToString("N0", Inv)} senses ({project.ToString("N0", Inv)} of them " +
            $"project senses) and {trainedGroups.ToString("N0", Inv)} groups.");
        if (missing.Count > 0)
        {
            AddParagraph(
                lines,
                $"{missing.Count.ToString(Inv)} lemmas have anchor sentences but no sense in the data and are " +
                "therefore not listed: " +
                string.Join(", ", missing.Select(m => $"`{m}`")) + ".");
        }
    }

    /// <summary>
    ///     Adds the alphabetical lemma list.
    /// </summary>
    private static void AddLemmas(List<string> lines, WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas)
    {
        lines.Add("## Supported Lemmas");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            $"The library supports {lemmas.Count.ToString("N0", Inv)} lemmas with " +
            $"{lemmas.Sum(l => l.Senses.Count).ToString("N0", Inv)} senses in total, listed alphabetically. Each lemma lists " +
            "its parts of speech and the forms it recognizes. Under it, every sense gives its id and part of speech, its " +
            "group, its Open English WordNet sense key (a project sense has none) and " +
            "its gloss, in inventory order (the order of Open English WordNet within a part of speech, project senses last).");

        foreach (var letter in lemmas.GroupBy(l => char.ToUpperInvariant(l.Lemma[0])).OrderBy(g => g.Key))
        {
            lines.Add($"### Lemmas Starting With {letter.Key}");
            lines.Add(string.Empty);
            foreach (var (lemma, senses) in letter)
            {
                var posText = string.Join(", ", senses.Select(s => s.Sense.PartOfSpeech).Distinct().Order().Select(p => p.ToString().ToLowerInvariant()));
                var forms = image.FormsOf(lemma).Where(f => f != lemma).Select(Escape).ToList();
                var formText = forms.Count == 0 ? "no other forms" : "forms: " + string.Join(", ", forms);
                lines.AddRange(Wrap("- ", $"**{lemma}**; parts of speech: {posText}; {formText}", "  "));
                foreach (var listed in senses)
                {
                    lines.AddRange(Wrap("  - ", Describe(listed), "    "));
                }
            }

            lines.Add(string.Empty);
        }

        // Drop the blank line after the last list so the file ends with exactly one line feed
        lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>
    ///     Describes a sense by its id, part of speech, group, key and gloss.
    /// </summary>
    private static string Describe(Listed listed)
    {
        var sense = listed.Sense;
        var text = $"`{sense.SenseId}` ({sense.PartOfSpeech.ToString().ToLowerInvariant()}); group: " +
            (sense.GroupId is null ? "none" : $"`{sense.GroupId}`") + "; key: " +
            (sense.SenseKey is null ? "project sense" : $"`{sense.SenseKey}`");
        return text + "; gloss: " + Escape(listed.Gloss);
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
