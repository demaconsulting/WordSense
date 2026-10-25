using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using WordSense.Builder.Data;

namespace WordSense.Builder;

/// <summary>
///     Derives the heteronyms of the word table: the spellings whose analyses or senses stand for different words, keyed
///     by the canonical word keys (<see cref="WordKey"/>), without any phonemes.
/// </summary>
/// <remarks>
///     <para>
///     There are three levels. <b>Entry level</b> is derived from the data: the pronunciations of Open English WordNet
///     (<c>data/pronunciation</c>, a hint for each entry) are normalized (no variety, no syllable marks or length marks,
///     the common dialect differences of vowels and of the <c>r</c> collapsed, stress kept) and the entries of one lemma
///     (case-folded) that share no pronunciation are different sound classes; a spelling that is a form of entries of
///     two classes is a heteronym whose classes are told apart by the analysis. The pronunciation of an entry is that of
///     its lemma, so entries of different lemmas are never compared (a participle and its verb differ only by the
///     suffix), and a lemma whose entries of one part of speech fall into different classes (<c>bass</c>, <c>bow</c>) is left
///     to the sense level. <b>Tense level</b> and <b>sense level</b> come from the curated table
///     <c>corpus/curation/heteronyms.tsv</c>, which also lists the few entry-level cases of different lemmas that the
///     pronunciations cannot show (<c>wound</c>, <c>dove</c>, <c>lives</c>); a row of it names a class of a group and gives
///     a pattern <c>lemma/Part[/Features][#label,label]</c> of the analyses (features are a subset of the items of the
///     analysis) and senses that belong to the class. A group may carry a note (the optional fifth cell of any of its rows)
///     that the output repeats for each of its spellings, for example that some dialects merge the classes.
///     </para>
///     <para>
///     A spelling is keyed by its normalized form (<see cref="BasicClassifier.Normalize"/>); a key without a label stands
///     for its analysis and every sense of it.
///     </para>
/// </remarks>
internal static class Heteronyms
{
    /// <summary>The level of the spellings that the pronunciations or the curated different-lemma rows tell apart by their analyses.</summary>
    public const string Entry = "entry";

    /// <summary>The level of the spellings whose past and non-past analyses sound different.</summary>
    public const string Tense = "tense";

    /// <summary>The level of the spellings whose senses of one analysis sound different.</summary>
    public const string Sense = "sense";

    private const string CuratedLemma = "lemma";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    ///     One key of a class: the key of an analysis, or of one sense of it.
    /// </summary>
    /// <param name="Analysis">The key of the analysis without a label.</param>
    /// <param name="Label">The label of the sense, or null when the class has the analysis and all its senses.</param>
    public sealed record HeteronymKey(string Analysis, string? Label)
    {
        /// <summary>
        ///     Gets the key text: the analysis key, with <c>#label</c> for a sense.
        /// </summary>
        public string Text => Label is null ? Analysis : Analysis + "#" + Label;
    }

    /// <summary>
    ///     One meaning class of a heteronym spelling with its keys.
    /// </summary>
    /// <param name="Name">The name of the class.</param>
    /// <param name="Keys">The keys of the class in a fixed order.</param>
    public sealed record HeteronymClass(string Name, IReadOnlyList<HeteronymKey> Keys);

    /// <summary>
    ///     One heteronym: a normalized spelling of a group with its classes.
    /// </summary>
    /// <param name="Level">The level: <see cref="Entry"/>, <see cref="Tense"/> or <see cref="Sense"/>.</param>
    /// <param name="Source">Where the group comes from: <c>pronunciation</c> or <c>curated</c>.</param>
    /// <param name="Group">The group: the lemma, case-folded, of an entry-level group, or the group of the curated table.</param>
    /// <param name="Spelling">The normalized spelling.</param>
    /// <param name="Classes">The classes, at least two.</param>
    /// <param name="Note">The note of the curated group (for example that some dialects merge the classes), or null.</param>
    public sealed record Heteronym(string Level, string Source, string Group, string Spelling, IReadOnlyList<HeteronymClass> Classes, string? Note = null);

    /// <summary>
    ///     A pattern of a curated row.
    /// </summary>
    private sealed record Pattern(string Lemma, PartOfSpeech Pos, string[] Items, string[]? Labels)
    {
        public bool Matches(WordDetails analysis) =>
            string.Equals(analysis.Lemma, Lemma, StringComparison.Ordinal)
            && analysis.PartOfSpeech == Pos
            && Items.All(item => analysis.Features.Split('+').Contains(item, StringComparer.Ordinal));
    }

    private sealed record CuratedRow(string Level, string Group, string Class, Pattern Pattern, string? Note, int Line);

    /// <summary>
    ///     Builds the heteronyms.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="curation">The curated table, <c>corpus/curation/heteronyms.tsv</c>.</param>
    /// <param name="image">The word image compiled from the data.</param>
    /// <returns>The heteronyms ordered by level, spelling and group.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found when the curated table is invalid or does not match the data.</exception>
    public static IReadOnlyList<Heteronym> Build(string data, string curation, WordImage image)
    {
        var errors = new List<string>();
        var curated = ReadCurated(curation, image, errors);
        var result = new List<Heteronym>();
        foreach (var group in curated.GroupBy(r => (r.Level, r.Group)).OrderBy(g => g.Key.Level, StringComparer.Ordinal).ThenBy(g => g.Key.Group, StringComparer.Ordinal))
        {
            var found = BuildCuratedGroup(group.Key.Level, group.Key.Group, [.. group], image, errors);
            if (found.Count == 0)
            {
                errors.Add($"The curated group '{group.Key.Group}' ({group.Key.Level}) matches no spelling of the word table with two classes.");
            }

            result.AddRange(found);
        }

        var curatedSpellings = new HashSet<string>(result.Select(h => h.Spelling), StringComparer.Ordinal);
        result.AddRange(BuildEntryLevel(data, image).Where(h => !curatedSpellings.Contains(h.Spelling)));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        return [.. result
            .OrderBy(h => LevelOrder(h.Level))
            .ThenBy(h => h.Spelling, StringComparer.Ordinal)
            .ThenBy(h => h.Group, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     Renders the heteronyms as a TSV a downstream library can load: one row for each key of a class.
    /// </summary>
    /// <param name="heteronyms">The heteronyms.</param>
    /// <returns>The text: a header row, then <c>level</c>, <c>group</c>, <c>spelling</c>, <c>class</c>, <c>key</c> and <c>note</c> (empty when the group has none; see the remarks of <see cref="Heteronyms"/>), LF line ends, final line feed.</returns>
    public static string RenderTsv(IReadOnlyList<Heteronym> heteronyms)
    {
        var text = new StringBuilder("level\tgroup\tspelling\tclass\tkey\tnote\n");
        foreach (var h in heteronyms)
        {
            foreach (var c in h.Classes)
            {
                foreach (var key in c.Keys)
                {
                    text.Append(h.Level).Append('\t').Append(h.Group).Append('\t').Append(h.Spelling).Append('\t').Append(c.Name).Append('\t').Append(key.Text).Append('\t').Append(h.Note).Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     Writes the TSV of the heteronyms to a file: UTF-8 without a byte-order mark, LF line endings.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="curation">The curated table.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>The number of heteronym records written (a record is a spelling of a group).</returns>
    public static int Write(string data, string curation, string output)
    {
        var (bytes, _) = ImageCompiler.Build(data);
        var heteronyms = Build(data, curation, WordImage.Open(bytes));
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, RenderTsv(heteronyms), new UTF8Encoding(false));
        return heteronyms.Count;
    }

    /// <summary>
    ///     Normalizes the text of a pronunciation for the comparison of two entries.
    /// </summary>
    /// <param name="text">The IPA text of the source.</param>
    /// <returns>The text without syllable and length marks and without the common dialect differences; the stress marks stay.</returns>
    internal static string NormalizePronunciation(string text)
    {
        var t = new StringBuilder(text.Normalize(NormalizationForm.FormC).Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            if (".ˑ‿\u0361\u035c()[]/ː:\u02d0\u0329\u032f\u0306 ".Contains(c, StringComparison.Ordinal))
            {
                continue;
            }

            t.Append(c);
        }

        return t.ToString()
            .Replace('ɹ', 'r').Replace('ɡ', 'g').Replace("ʧ", "tʃ", StringComparison.Ordinal).Replace("ʤ", "dʒ", StringComparison.Ordinal)
            .Replace("r", string.Empty, StringComparison.Ordinal)
            .Replace('ɚ', 'ə').Replace('ɝ', 'ɜ').Replace('ɒ', 'ɔ').Replace('ɑ', 'a').Replace('e', 'ɛ')
            .Replace("əʊ", "oʊ", StringComparison.Ordinal).Replace('ʉ', 'u').Replace('ɐ', 'ə')
            .Replace("ɪə", "ɪ", StringComparison.Ordinal).Replace("ɛə", "ɛ", StringComparison.Ordinal).Replace("ɛɪ", "eɪ", StringComparison.Ordinal)
            .Replace('æ', 'a').Replace('ɜ', 'ə');
    }

    private static int LevelOrder(string level) => level switch
    {
        Entry => 0,
        Tense => 1,
        _ => 2,
    };

    private static List<CuratedRow> ReadCurated(string path, WordImage image, List<string> errors)
    {
        var rows = new List<CuratedRow>();
        if (!File.Exists(path))
        {
            errors.Add($"The curated heteronym table '{path}' was not found.");
            return rows;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (lineNumber++ == 0)
            {
                if (line != "level\tgroup\tclass\tpattern\tnote")
                {
                    errors.Add($"{path}:1: the header must be 'level, group, class, pattern, note' separated by tabs.");
                }

                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length is not (4 or 5) || cells.Any(c => c.Length == 0 || c != c.Trim()))
            {
                errors.Add($"{path}:{lineNumber.ToString(Inv)}: a row has four non-empty cells, or five with a note, separated by tabs.");
                continue;
            }

            if (cells[0] is not (CuratedLemma or Tense or Sense))
            {
                errors.Add($"{path}:{lineNumber.ToString(Inv)}: the level '{cells[0]}' is not lemma, tense or sense.");
                continue;
            }

            var pattern = ParsePattern(cells[3], image, out var problem);
            if (pattern is null)
            {
                errors.Add($"{path}:{lineNumber.ToString(Inv)}: {problem}");
                continue;
            }

            rows.Add(new CuratedRow(cells[0], cells[1], cells[2], pattern, cells.Length == 5 ? cells[4] : null, lineNumber));
        }

        return rows;
    }

    private static Pattern? ParsePattern(string text, WordImage image, out string problem)
    {
        problem = string.Empty;
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        var body = hash < 0 ? text : text[..hash];
        var labels = hash < 0 ? null : text[(hash + 1)..].Split(',');
        var parts = body.Split('/');
        if (parts.Length is < 2 or > 3 || !Enum.TryParse<PartOfSpeech>(parts[1], out var pos) || !Enum.IsDefined(pos) || parts[0].Length == 0)
        {
            problem = $"the pattern '{text}' is not lemma/Part[/Features][#label,label].";
            return null;
        }

        var items = parts.Length == 3 ? parts[2].Split('+') : [];
        if (labels is not null)
        {
            var missing = labels.Where(l => l.Length == 0 || !image.TryGetSense(parts[0] + "#" + l, out var sense) || sense.PartOfSpeech != pos).ToList();
            if (missing.Count > 0)
            {
                problem = $"the pattern '{text}' names senses that the catalog does not have: {string.Join(", ", missing)}.";
                return null;
            }
        }

        return new Pattern(parts[0], pos, items, labels);
    }

    private static List<Heteronym> BuildCuratedGroup(string level, string group, List<CuratedRow> rows, WordImage image, List<string> errors)
    {
        var heteronymLevel = level == CuratedLemma ? Entry : level;
        var classNames = rows.Select(r => r.Class).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (classNames.Count < 2)
        {
            errors.Add($"The curated group '{group}' has fewer than two classes.");
            return [];
        }

        var spellings = rows.SelectMany(r => image.FormsOf(r.Pattern.Lemma)).Distinct(StringComparer.Ordinal).ToList();
        var byNormalized = new SortedDictionary<string, Dictionary<string, List<HeteronymKey>>>(StringComparer.Ordinal);
        foreach (var spelling in spellings)
        {
            var index = image.Find(spelling);
            var normalized = BasicClassifier.Normalize(spelling);
            if (index < 0 || normalized.Length == 0)
            {
                continue;
            }

            foreach (var analysis in image.Analyses(index))
            {
                AddClaims(byNormalized, normalized, analysis, rows, group, errors);
            }
        }

        var notes = rows.Where(r => r.Note is not null).Select(r => r.Note!).Distinct(StringComparer.Ordinal).ToList();
        if (notes.Count > 1)
        {
            errors.Add($"The curated group '{group}' has {notes.Count.ToString(Inv)} different notes; a group has one.");
        }

        var note = notes.Count == 1 ? notes[0] : null;
        var found = new List<Heteronym>();
        foreach (var (spelling, classes) in byNormalized)
        {
            if (classes.Count >= 2)
            {
                found.Add(new Heteronym(heteronymLevel, "curated", group, spelling, [.. classes.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new HeteronymClass(c.Key, c.Value))], note));
            }
        }

        return found;
    }

    private static void AddClaims(
        SortedDictionary<string, Dictionary<string, List<HeteronymKey>>> byNormalized,
        string spelling,
        WordDetails analysis,
        List<CuratedRow> rows,
        string group,
        List<string> errors)
    {
        var matches = rows.Where(r => r.Pattern.Matches(analysis)).ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var key = WordKey.Format(spelling, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, null);
        var wholeClasses = matches.Where(r => r.Pattern.Labels is null).Select(r => r.Class).Distinct(StringComparer.Ordinal).ToList();
        var labelClaims = matches.Where(r => r.Pattern.Labels is not null)
            .SelectMany(r => r.Pattern.Labels!.Select(l => (Label: l, r.Class)))
            .GroupBy(c => c.Label, StringComparer.Ordinal)
            .ToList();
        if (wholeClasses.Count > 1 || (wholeClasses.Count == 1 && labelClaims.Count > 0 && labelClaims.Any(g => g.Any(c => c.Class != wholeClasses[0])))
            || labelClaims.Any(g => g.Select(c => c.Class).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            errors.Add($"The curated group '{group}' puts the key '{key}' or one of its senses into two classes.");
            return;
        }

        if (!byNormalized.TryGetValue(spelling, out var classes))
        {
            byNormalized[spelling] = classes = new Dictionary<string, List<HeteronymKey>>(StringComparer.Ordinal);
        }

        foreach (var name in wholeClasses.Concat(labelClaims.SelectMany(g => g.Select(c => c.Class))).Distinct(StringComparer.Ordinal))
        {
            if (!classes.TryGetValue(name, out var keys))
            {
                classes[name] = keys = [];
            }

            if (wholeClasses.Contains(name, StringComparer.Ordinal))
            {
                AddUnique(keys, new HeteronymKey(key, null));
            }

            foreach (var label in labelClaims.Where(g => g.Any(c => c.Class == name)).Select(g => g.Key).Order(StringComparer.Ordinal))
            {
                AddUnique(keys, new HeteronymKey(key, label));
            }
        }
    }

    private static void AddUnique(List<HeteronymKey> keys, HeteronymKey key)
    {
        if (!keys.Contains(key))
        {
            keys.Add(key);
        }
    }

    /// <summary>
    ///     Derives the entry-level heteronyms from the pronunciations of the data.
    /// </summary>
    private static List<Heteronym> BuildEntryLevel(string data, WordImage image)
    {
        var schema = DataSchema.Load(Path.Join(data, "schema.yaml"));
        var formTable = schema.Table("form");
        var (entries, sounds) = ReadEntrySounds(data, schema);
        var (component, split) = SoundClasses(entries, sounds);
        var spellingColumn = formTable.IndexOf("spelling");
        var formEntry = formTable.IndexOf("entry_id");
        var featuresColumn = formTable.IndexOf("features");
        var found = new SortedDictionary<(string Spelling, string Group), Dictionary<string, SortedSet<WordDetails>>>(
            Comparer<(string Spelling, string Group)>.Create((a, b) =>
            {
                var c = string.CompareOrdinal(a.Spelling, b.Spelling);
                return c != 0 ? c : string.CompareOrdinal(a.Group, b.Group);
            }));
        foreach (var row in DataTables.Read(data, formTable))
        {
            if (row[formEntry].Length == 0 || !component.TryGetValue(row[formEntry], out var sound) || split.Contains(sound.Lemma))
            {
                continue;
            }

            var spelling = row[spellingColumn];
            var index = image.Find(spelling);
            var (lemma, pos) = entries[row[formEntry]];
            var details = new WordDetails(lemma, pos, row[featuresColumn]);
            if (index < 0 || !image.Analyses(index).Contains(details))
            {
                continue;
            }

            var key = (BasicClassifier.Normalize(spelling), sound.Lemma);
            if (key.Item1.Length == 0)
            {
                continue;
            }

            if (!found.TryGetValue(key, out var classes))
            {
                found[key] = classes = new Dictionary<string, SortedSet<WordDetails>>(StringComparer.Ordinal);
            }

            if (!classes.TryGetValue(sound.Root, out var members))
            {
                classes[sound.Root] = members = new SortedSet<WordDetails>(Comparer<WordDetails>.Create(CompareDetails));
            }

            members.Add(details);
        }

        var result = new List<Heteronym>();
        foreach (var ((spelling, group), classes) in found)
        {
            if (classes.Count < 2)
            {
                continue;
            }

            var named = classes.Values.Select(members => new HeteronymClass(
                string.Join(", ", members.Select(d => d.Lemma + " " + d.PartOfSpeech.ToString().ToLowerInvariant()).Distinct(StringComparer.Ordinal)),
                [.. members.Select(d => new HeteronymKey(WordKey.Format(spelling, d.Lemma, d.PartOfSpeech, d.Features, null), null))]))
                .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            result.Add(new Heteronym(Entry, "pronunciation", group, spelling, named));
        }

        return result;
    }

    /// <summary>
    ///     Lists the lemmas (case-folded) that have a lemma and part of speech in two sound classes and a form in the word
    ///     table: the lemmas whose spellings only the senses can decide, which the curated table must list at the sense level.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="image">The word image compiled from the data.</param>
    /// <returns>The folded lemmas in ordinal order.</returns>
    internal static IReadOnlyList<string> SenseSplitLemmas(string data, WordImage image)
    {
        var schema = DataSchema.Load(Path.Join(data, "schema.yaml"));
        var (entries, sounds) = ReadEntrySounds(data, schema);
        var (_, split) = SoundClasses(entries, sounds);
        return [.. split
            .Where(lemma => entries.Values.Where(e => string.Equals(e.Lemma.ToLowerInvariant(), lemma, StringComparison.Ordinal)).Any(e => image.FormsOf(e.Lemma).Count > 0))
            .Order(StringComparer.Ordinal)];
    }

    private static (Dictionary<string, (string Lemma, PartOfSpeech Pos)> Entries, Dictionary<string, HashSet<string>> Sounds) ReadEntrySounds(string data, DataSchema schema)
    {
        var entryTable = schema.Table("entry");
        var pronunciationTable = schema.Table("pronunciation");
        var entryId = entryTable.IndexOf("entry_id");
        var lemmaColumn = entryTable.IndexOf("lemma");
        var posColumn = entryTable.IndexOf("pos");
        var entries = DataTables.Read(data, entryTable).ToDictionary(r => r[entryId], r => (Lemma: r[lemmaColumn], Pos: DataTables.PosOf(r[posColumn])), StringComparer.Ordinal);

        var sounds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pronunciationEntry = pronunciationTable.IndexOf("entry_id");
        var pronunciationText = pronunciationTable.IndexOf("text");
        foreach (var row in DataTables.Read(data, pronunciationTable))
        {
            if (!entries.ContainsKey(row[pronunciationEntry]))
            {
                continue;
            }

            if (!sounds.TryGetValue(row[pronunciationEntry], out var set))
            {
                sounds[row[pronunciationEntry]] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            var normalized = NormalizePronunciation(row[pronunciationText]);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return (entries, sounds);
    }

    private static int CompareDetails(WordDetails? a, WordDetails? b)
    {
        var c = string.CompareOrdinal(a!.Lemma, b!.Lemma);
        c = c != 0 ? c : a.PartOfSpeech.CompareTo(b.PartOfSpeech);
        return c != 0 ? c : string.CompareOrdinal(a.Features, b.Features);
    }

    /// <summary>
    ///     Groups the entries that have a pronunciation into sound classes: within one case-folded lemma, entries that share a
    ///     normalized pronunciation are one class.
    /// </summary>
    /// <returns>The class of each entry (the folded lemma and a root entry that names the class) and the folded lemmas that have a lemma and part of speech in two classes (a split that only the senses can decide).</returns>
    private static (Dictionary<string, (string Lemma, string Root)> Component, HashSet<string> Split) SoundClasses(
        Dictionary<string, (string Lemma, PartOfSpeech Pos)> entries,
        Dictionary<string, HashSet<string>> sounds)
    {
        var component = new Dictionary<string, (string Lemma, string Root)>(StringComparer.Ordinal);
        var split = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lemmaGroup in sounds.Keys.GroupBy(e => entries[e].Lemma.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var ids = lemmaGroup.Order(StringComparer.Ordinal).ToList();
            var parent = ids.ToDictionary(e => e, e => e, StringComparer.Ordinal);
            string Find(string x)
            {
                while (parent[x] != x)
                {
                    x = parent[x] = parent[parent[x]];
                }

                return x;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                for (var j = i + 1; j < ids.Count; j++)
                {
                    if (sounds[ids[i]].Overlaps(sounds[ids[j]]))
                    {
                        var (a, b) = (Find(ids[i]), Find(ids[j]));
                        if (a != b)
                        {
                            parent[string.CompareOrdinal(a, b) < 0 ? b : a] = string.CompareOrdinal(a, b) < 0 ? a : b;
                        }
                    }
                }
            }

            foreach (var id in ids)
            {
                component[id] = (lemmaGroup.Key, Find(id));
            }

            if (ids.GroupBy(e => entries[e]).Any(g => g.Select(Find).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                split.Add(lemmaGroup.Key);
            }
        }

        return (component, split);
    }
}
