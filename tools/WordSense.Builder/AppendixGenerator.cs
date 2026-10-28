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

    private const string LemmasHeading = "## Supported Lemmas";

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
        var heteronyms = Heteronyms.Build(data, Path.Join(corpus, "curation", "heteronyms.tsv"), image);

        // The encoder models train a classifier for a lemma only when two or more of its senses have anchors (WeightsTrainer)
        var classified = Corpus.CountAnchoredSenses(anchorsDirectory).Count(p => senses.ContainsKey(p.Key) && p.Value >= 2);
        return Render(image, listed, missing, heteronyms, classified);
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
        return text.Split('\n').SkipWhile(l => l != LemmasHeading).Count(l => l.StartsWith("- **", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Reads the glosses of the senses of some lemmas from the data tables.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The definitions of the synset of each sense joined with a semicolon, by public sense id.</returns>
    internal static Dictionary<string, string> ReadGlosses(string data, List<string> lemmas)
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
    private static string Render(WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas, List<string> missing, IReadOnlyList<Heteronyms.Heteronym> heteronyms, int classified)
    {
        var lines = new List<string>
        {
            "# Appendix: Supported Senses",
            string.Empty,
            "<!-- Generated by 'WordSense.Builder appendix' from data and corpus/anchors. Do not edit. -->",
            string.Empty,
        };

        AddExplanation(lines, lemmas);
        AddDictionary(lines, image, lemmas, missing, classified);
        AddKeys(lines, heteronyms);
        AddLemmas(lines, image, lemmas, classified);

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
            "The sense inventory is Open English WordNet plus a few project senses for the words it does not cover " +
            "(for example `the#determiner`). An answer is a lemma, a part of speech and, for a trained lemma, a sense or " +
            "a group of senses.");

        lines.Add("### Senses");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "A sense id has the form `lemma#label` (for example `bank#coin-bank`). It is stable and never reused, and it " +
            "links to one Open English WordNet sense key; a project sense has no key. Senses are listed in the order of " +
            "Open English WordNet, project senses last, each with the gloss of its synset.");

        lines.Add("### Parts of Speech");
        lines.Add(string.Empty);
        var parts = all.Select(s => s.PartOfSpeech).Distinct().Order().ToList();
        AddParagraph(
            lines,
            "`PartOfSpeech` has ten values. The listed lemmas have senses of " +
            $"{parts.Count.ToString(Inv)} of them:");
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
            "A group is a curated set of at least two, but not all, closely related senses of one lemma and part of " +
            "speech. A sense is in at most one group; the sense lines show `none` for a sense in no group.");

        lines.Add("### Forms");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "Each lemma lists the analyses of its forms with their features (`Feature=Value` items joined by `+`), and " +
            "the part of speech first when there are several. So `read (verb, Tense=Past+VerbForm=Part)` is the past " +
            "tense and participle spelled like the lemma.");
    }

    /// <summary>
    ///     Adds the summary of the dictionary words: the size of the word table and the share of its senses that are trained.
    /// </summary>
    private static void AddDictionary(List<string> lines, WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas, List<string> missing, int classified)
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
            $"senses in {image.GroupCount.ToString("N0", Inv)} curated groups. Every word of the table that is not " +
            "listed below has no anchor sentences and no sense classifier.");
        AddParagraph(
            lines,
            $"The lemmas listed below are the {lemmas.Count.ToString("N0", Inv)} lemmas that have anchor sentences, with " +
            $"{trainedSenses.ToString("N0", Inv)} senses ({project.ToString("N0", Inv)} of them project senses) and " +
            $"{trainedGroups.ToString("N0", Inv)} groups. The encoder models train a sense classifier for {classified.ToString("N0", Inv)} of " +
            "them, the lemmas with anchor sentences for two or more senses; the other lemmas have one anchored sense and no " +
            "classifier.");
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
    ///     Lists every analysis of every form of a lemma, the forms spelled like the lemma included: the form with its
    ///     features, and with its part of speech first when the forms have several.
    /// </summary>
    private static List<string> FormItems(WordImage image, string lemma)
    {
        var analyses = new List<(string Form, WordDetails Details)>();
        foreach (var form in image.FormsOf(lemma))
        {
            var index = image.Find(form);
            if (index >= 0)
            {
                analyses.AddRange(image.Analyses(index).Where(a => string.Equals(a.Lemma, lemma, StringComparison.Ordinal)).Select(a => (form, a)));
            }
        }

        var several = analyses.Select(a => a.Details.PartOfSpeech).Distinct().Count() > 1;
        return [.. analyses.Select(a =>
        {
            var details = new List<string>();
            if (several)
            {
                details.Add(a.Details.PartOfSpeech.ToString().ToLowerInvariant());
            }

            if (a.Details.Features.Length > 0)
            {
                details.Add(a.Details.Features);
            }

            return details.Count == 0 ? Escape(a.Form) : $"{Escape(a.Form)} ({Escape(string.Join(", ", details))})";
        })];
    }

    /// <summary>
    ///     Adds the section of the keys and the heteronyms.
    /// </summary>
    private static void AddKeys(List<string> lines, IReadOnlyList<Heteronyms.Heteronym> heteronyms)
    {
        lines.Add("## Keys and Heteronyms");
        lines.Add(string.Empty);
        lines.Add("### Keys");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            "A key names a meaning and never a sound: `{spelling}|{lemma}/{Part}[/{Features}][#{label}]`, where the " +
            "label is the part after `#` of a sense id. An unknown word has no key. `WordLexicon.Keys()` enumerates " +
            "every key any model can return, and the builder command `keys --output FILE` writes them. The user guide " +
            "describes the format.");
        lines.Add("### Heteronyms");
        lines.Add(string.Empty);
        var entry = heteronyms.Where(h => h.Level == Heteronyms.Entry).ToList();
        var tense = heteronyms.Where(h => h.Level == Heteronyms.Tense).ToList();
        var sense = heteronyms.Where(h => h.Level == Heteronyms.Sense).ToList();
        AddParagraph(
            lines,
            "A heteronym is a spelling whose key, not the spelling, decides the word. The sections below list each one " +
            "with its classes of meaning and the keys of each class. A key without a label stands for its analysis and " +
            "every sense of it; `#{a,b}` stands for the keys `#a` and `#b`. " +
            $"The table has {heteronyms.Count.ToString("N0", Inv)} heteronym records, one for each spelling of a group " +
            $"({heteronyms.Select(h => h.Spelling).Distinct(StringComparer.Ordinal).Count().ToString("N0", Inv)} distinct " +
            "spellings, because a few spellings, such as `leading`, are in two groups): " +
            $"{entry.Count.ToString("N0", Inv)} at the entry level " +
            $"({entry.Count(h => h.Source == "pronunciation").ToString("N0", Inv)} derived from the pronunciations of " +
            $"Open English WordNet, {entry.Count(h => h.Source == "curated").ToString("N0", Inv)} curated), " +
            $"{tense.Count.ToString("N0", Inv)} at the tense level and {sense.Count.ToString("N0", Inv)} at the sense " +
            "level. `WordSense.Builder heteronyms --output FILE` writes the same table as a TSV.");

        AddHeteronymLevel(
            lines,
            "Heteronyms by Entry",
            "The analyses (lemma and part of speech) belong to entries that sound different. The curated table " +
            "`corpus/curation/heteronyms.tsv` adds spellings of different lemmas (`wound`, `dove`, `lives`) and pairs " +
            "whose entries have no pronunciation in the data (`use`, `object`, `present`).",
            entry);
        AddHeteronymLevel(
            lines,
            "Heteronyms by Tense",
            "The past tense and the present tense or infinitive are spelled alike and sound different.",
            tense);
        AddHeteronymLevel(
            lines,
            "Heteronyms by Sense",
            "The senses of one analysis belong to entries that sound different, so only the sense decides.",
            sense);
    }

    private static void AddHeteronymLevel(List<string> lines, string title, string text, List<Heteronyms.Heteronym> heteronyms)
    {
        lines.Add($"### {title}");
        lines.Add(string.Empty);
        AddParagraph(lines, text);
        foreach (var heteronym in heteronyms)
        {
            var classes = heteronym.Classes.Select(c => c.Name + ": " + string.Join(", ", KeyPatterns(c)));
            var note = heteronym.Note is null ? string.Empty : $" Note: {heteronym.Note}.";
            lines.AddRange(Wrap("- ", $"**{Escape(heteronym.Spelling)}** (group `{heteronym.Group}`): " + string.Join("; ", classes) + note, "  "));
        }

        if (heteronyms.Count > 0)
        {
            lines.Add(string.Empty);
        }
    }

    /// <summary>
    ///     Writes the keys of a class compactly: the keys of one analysis with labels as <c>key#{a,b}</c>.
    /// </summary>
    private static IEnumerable<string> KeyPatterns(Heteronyms.HeteronymClass heteronymClass)
    {
        foreach (var group in heteronymClass.Keys.GroupBy(k => k.Analysis, StringComparer.Ordinal))
        {
            var labels = group.Where(k => k.Label is not null).Select(k => k.Label!).ToList();
            if (labels.Count == 0)
            {
                yield return $"`{group.Key}`";
            }
            else
            {
                yield return labels.Count == 1 ? $"`{group.Key}#{labels[0]}`" : $"`{group.Key}#{{{string.Join(",", labels)}}}`";
            }
        }
    }

    /// <summary>
    ///     Adds the alphabetical lemma list.
    /// </summary>
    private static void AddLemmas(List<string> lines, WordImage image, List<(string Lemma, List<Listed> Senses)> lemmas, int classified)
    {
        lines.Add("## Supported Lemmas");
        lines.Add(string.Empty);
        AddParagraph(
            lines,
            $"The list holds the {lemmas.Count.ToString("N0", Inv)} lemmas that have anchor sentences ({classified.ToString("N0", Inv)} of them " +
            $"with a sense classifier) and their {lemmas.Sum(l => l.Senses.Count).ToString("N0", Inv)} senses in total, listed alphabetically. Each lemma lists " +
            "its parts of speech and forms. Under it, every sense gives its id and part of speech, its group, its Open " +
            "English WordNet sense key (a project sense has none) and its gloss.");

        foreach (var letter in lemmas.GroupBy(l => char.ToUpperInvariant(l.Lemma[0])).OrderBy(g => g.Key))
        {
            lines.Add($"### Lemmas Starting With {letter.Key}");
            lines.Add(string.Empty);
            foreach (var (lemma, senses) in letter)
            {
                var posList = senses.Select(s => s.Sense.PartOfSpeech).Distinct().Order().ToList();
                var posText = string.Join(", ", posList.Select(p => p.ToString().ToLowerInvariant()));
                var formText = "forms: " + string.Join(", ", FormItems(image, lemma));
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
