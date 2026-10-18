using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WordSense.Builder.Data;

/// <summary>
///     Imports an Open English WordNet (OEWN) LMF file into the relational TSV tables of the <c>data</c> folder.
/// </summary>
/// <remarks>
///     <para>
///     The import is a deterministic bootstrap: the same input and schema always produce byte-identical files, and it
///     refuses to write over existing tables unless told to, because the committed data is authoritative once it has
///     been edited. It never parses an identifier to get a lemma; the only identifier tails it reads are the numeric
///     lex-id fields of a sense id, to derive the Princeton-style sense key (which exists only there).
///     </para>
///     <para>
///     Derivations: entry part of speech <c>s</c> becomes <c>a</c> (satellite status is a synset property); homograph
///     numbers are 0 for a unique (lemma, part of speech) and otherwise 1..n by the numeric tail of the OEWN entry
///     id; a satellite synset's head is the target of its one <c>similar</c> relation; relations are stored in one
///     direction (the vocabulary decides which) and the other direction is derived, so a symmetric relation is stored
///     once with the smaller id first. Fields that are not imported are listed in <c>data/PROVENANCE.md</c>.
///     </para>
///     <para>
///     The type is stateless and thread-safe; each call reads the whole file in memory (about 200 MB peak for the
///     full release).
///     </para>
/// </remarks>
internal static partial class OewnImporter
{
    /// <summary>
    ///     The SHA-256 of the pinned OEWN 2025 file <c>english-wordnet-2025.xml.gz</c>, as upper-case hexadecimal.
    /// </summary>
    public const string PinnedSha256 = "9CA6D1DCB75F822FDD66617F7D9DA48142ACE38DD544D6AD5E2FECA1674AD3FE";

    /// <summary>
    ///     The default number of entries above which a first letter gets two-letter files.
    /// </summary>
    public const int DefaultSplitThreshold = 8000;

    private static readonly XNamespace DcNamespace = "https://globalwordnet.github.io/schemas/dc/";

    /// <summary>
    ///     The outcome of an import.
    /// </summary>
    /// <param name="Rows">The number of rows written per table.</param>
    /// <param name="Files">The number of files written per table.</param>
    /// <param name="FileSplit">The split plan that was chosen and written to the schema.</param>
    /// <param name="MissingInverse">Relation rows whose inverse row was absent in the source.</param>
    /// <param name="SelfRelations">Relation rows from a node to itself, which were dropped.</param>
    /// <param name="CleanedCells">Cells that needed white-space or Unicode normalization.</param>
    internal sealed record ImportResult(
        IReadOnlyDictionary<string, int> Rows,
        IReadOnlyDictionary<string, int> Files,
        IReadOnlyList<char> FileSplit,
        int MissingInverse,
        int SelfRelations,
        int CleanedCells)
    {
        /// <summary>
        ///     Renders the result as a short report.
        /// </summary>
        /// <returns>One line per table and a summary of the derived values.</returns>
        public string Render()
        {
            var text = new StringBuilder();
            foreach (var (table, rows) in Rows)
            {
                text.Append(CultureInfo.InvariantCulture, $"{table}: {rows} rows in {Files[table]} file(s)\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"file_split: [{string.Join(", ", FileSplit)}]\n");
            text.Append(CultureInfo.InvariantCulture, $"missing inverse rows: {MissingInverse}; self relations dropped: {SelfRelations}; cells cleaned: {CleanedCells}\n");
            return text.ToString();
        }
    }

    /// <summary>
    ///     Imports an OEWN file and writes the tables below a data directory.
    /// </summary>
    /// <param name="input">The LMF file, gzip-compressed (<c>.gz</c>) or plain XML.</param>
    /// <param name="output">The data directory that receives the table files.</param>
    /// <param name="schemaPath">The schema file; its <c>file_split</c> line is updated with the chosen plan.</param>
    /// <param name="expectedSha256">The SHA-256 the input file must have, or null to skip the check.</param>
    /// <param name="splitThreshold">The entry count above which a first letter is split by two letters.</param>
    /// <param name="overwrite">True to replace existing table files; false to refuse when any exists.</param>
    /// <returns>The counts of the import.</returns>
    /// <exception cref="InvalidDataException">Thrown when the hash does not match, the XML is malformed or inconsistent, or the output already holds tables and <paramref name="overwrite"/> is false.</exception>
    public static ImportResult Import(string input, string output, string schemaPath, string? expectedSha256 = PinnedSha256, int splitThreshold = DefaultSplitThreshold, bool overwrite = false)
    {
        // Verify the pin first so that a wrong or changed file never produces data
        CheckHash(input, expectedSha256);
        var schema = DataSchema.Load(schemaPath);
        PrepareOutput(schema, output, overwrite);

        // Read the XML into memory, translating malformed XML into the one error type the builder reports
        OewnDocument document;
        try
        {
            document = ReadDocument(input);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{input} is not well-formed XML: {ex.Message}", ex);
        }

        // Derive the tables and choose the file split from the entry counts
        var cleaner = new Cleaner();
        var tables = Derive(document, schema, cleaner, out var missing, out var selfRelations);
        var split = ChooseSplit(tables.Entries, splitThreshold);
        DataSchema.WriteFileSplit(schemaPath, split);
        var resolver = new PartitionResolver(split);
        foreach (var row in tables.Entries)
        {
            resolver.AddEntry(row[0], row[1]);
        }

        foreach (var row in tables.Senses)
        {
            resolver.AddSense(row[0], row[2], row[3]);
        }

        // Write every table to its files
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, list) in tables.All())
        {
            files[name] = WriteTable(schema.Table(name), list, resolver, output);
            rows[name] = list.Count;
        }

        return new ImportResult(rows, files, split, missing, selfRelations, cleaner.Count);
    }

    private static void CheckHash(string input, string? expectedSha256)
    {
        if (expectedSha256 is null)
        {
            return;
        }

        using var stream = File.OpenRead(input);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{input} has SHA-256 {actual} but {expectedSha256.ToUpperInvariant()} was expected.");
        }
    }

    private static void PrepareOutput(DataSchema schema, string output, bool overwrite)
    {
        // Only the tables this import writes are checked and cleared; tables of other sources are left alone
        foreach (var name in OewnTables)
        {
            var table = schema.Table(name);
            var path = table.Partition == PartitionKind.None ? Path.Combine(output, name + DataLayout.Extension) : Path.Combine(output, name);
            var exists = table.Partition == PartitionKind.None ? File.Exists(path) : Directory.Exists(path) && Directory.EnumerateFiles(path).Any();
            if (!exists)
            {
                continue;
            }

            if (!overwrite)
            {
                throw new InvalidDataException($"{path} already exists; the committed data is authoritative. Use --overwrite true to replace it.");
            }

            if (table.Partition == PartitionKind.None)
            {
                File.Delete(path);
            }
            else
            {
                Directory.Delete(path, true);
            }
        }
    }

    private static readonly string[] OewnTables =
        ["source", "entry", "synset", "gloss", "example", "sense", "synset_relation", "sense_relation", "pronunciation", "id_map"];

    private static List<char> ChooseSplit(List<string[]> entries, int threshold)
    {
        var counts = new Dictionary<char, int>();
        foreach (var row in entries)
        {
            var key = DataLayout.KeyOf(row[1])[0];
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts.Where(p => p.Key != DataLayout.OtherKey && p.Value > threshold).Select(p => p.Key).OrderBy(c => c).ToList();
    }

    private static int WriteTable(TableDef table, List<string[]> rows, PartitionResolver resolver, string output)
    {
        var header = table.Columns.Select(c => c.Name).ToArray();
        var comparer = DataLayout.RowComparer(table);
        if (table.Partition == PartitionKind.None)
        {
            rows.Sort(comparer);
            DataLayout.WriteTable(DataLayout.FilePath(output, table, string.Empty), header, rows);
            return 1;
        }

        // Group by file key; each group is sorted by the primary key and written to its own file
        var groups = new SortedDictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = resolver.KeyFor(table, row) ?? throw new InvalidDataException($"A row of table '{table.Name}' refers to a missing parent: {string.Join('\t', row)}");
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(row);
        }

        foreach (var (key, list) in groups)
        {
            list.Sort(comparer);
            DataLayout.WriteTable(DataLayout.FilePath(output, table, key), header, list);
        }

        return groups.Count;
    }

    /// <summary>
    ///     Normalizes the text of cells so that it can be written to a table file, and counts what it changed.
    /// </summary>
    private sealed class Cleaner
    {
        /// <summary>
        ///     Gets the number of values that were changed.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        ///     Normalizes a value to NFC, replaces tabs and line breaks with spaces and trims edge spaces.
        /// </summary>
        /// <param name="value">The raw value, or null.</param>
        /// <returns>The clean value (empty when the input is null).</returns>
        public string Clean(string? value)
        {
            var text = value ?? string.Empty;
            var clean = text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(' ').Normalize(NormalizationForm.FormC);
            if (!string.Equals(clean, text, StringComparison.Ordinal))
            {
                Count++;
            }

            return clean;
        }
    }

    [GeneratedRegex(@"^.*?__(\d)\.(\d\d)\.(\d\d)\.(.*)\.(\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SenseIdTail();

    private static Stream OpenInput(string input)
    {
        var stream = File.OpenRead(input);
        return input.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(stream, CompressionMode.Decompress) : stream;
    }

    private static OewnDocument ReadDocument(string input)
    {
        var document = new OewnDocument();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };
        using var stream = OpenInput(input);
        using var reader = XmlReader.Create(stream, settings);
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.Name)
            {
                case "Lexicon":
                    document.ReadLexicon(reader);
                    reader.Read();
                    break;
                case "LexicalEntry":
                    document.AddEntry((XElement)XNode.ReadFrom(reader));
                    break;
                case "Synset":
                    document.AddSynset((XElement)XNode.ReadFrom(reader));
                    break;
                default:
                    reader.Read();
                    break;
            }
        }

        return document.Complete(input);
    }

    /// <summary>
    ///     The parts of an OEWN file that the import keeps, as read.
    /// </summary>
    private sealed class OewnDocument
    {
        public string? LexiconId { get; private set; }

        public string Label { get; private set; } = string.Empty;

        public string License { get; private set; } = string.Empty;

        public string Url { get; private set; } = string.Empty;

        public string Version { get; private set; } = string.Empty;

        public List<RawEntry> Entries { get; } = [];

        public List<RawSynset> Synsets { get; } = [];

        public void ReadLexicon(XmlReader reader)
        {
            if (LexiconId is not null)
            {
                throw new InvalidDataException("The file holds more than one Lexicon; the import expects exactly one.");
            }

            LexiconId = reader.GetAttribute("id") ?? throw new InvalidDataException("The Lexicon has no id.");
            Label = reader.GetAttribute("label") ?? LexiconId;
            var license = reader.GetAttribute("license") ?? string.Empty;
            License = license.TrimEnd('/').EndsWith("licenses/by/4.0", StringComparison.Ordinal) ? "CC BY 4.0" : license;
            Url = reader.GetAttribute("url") ?? string.Empty;
            Version = reader.GetAttribute("version") ?? string.Empty;
        }

        public void AddEntry(XElement element)
        {
            var lemma = element.Element("Lemma") ?? throw new InvalidDataException($"LexicalEntry '{element.Attribute("id")?.Value}' has no Lemma.");
            var entry = new RawEntry(
                Required(element, "id"),
                Required(lemma, "writtenForm"),
                Required(lemma, "partOfSpeech"));
            foreach (var pronunciation in lemma.Elements("Pronunciation"))
            {
                entry.Pronunciations.Add((pronunciation.Attribute("variety")?.Value ?? string.Empty, pronunciation.Value));
            }

            foreach (var sense in element.Elements("Sense"))
            {
                entry.Senses.Add(ReadSense(sense));
            }

            Entries.Add(entry);
        }

        public void AddSynset(XElement element)
        {
            var synset = new RawSynset(Required(element, "id"), Required(element, "partOfSpeech"), element.Attribute("lexfile")?.Value ?? string.Empty);
            synset.Definitions.AddRange(element.Elements("Definition").Select(d => d.Value));
            synset.Examples.AddRange(element.Elements("Example").Select(e => e.Value));
            foreach (var relation in element.Elements("SynsetRelation"))
            {
                synset.Relations.Add((Required(relation, "relType"), Required(relation, "target")));
            }

            Synsets.Add(synset);
        }

        public OewnDocument Complete(string input)
        {
            if (LexiconId is null)
            {
                throw new InvalidDataException($"{input} has no Lexicon element; it is not an OEWN LMF file.");
            }

            return this;
        }

        private static RawSense ReadSense(XElement element)
        {
            var sense = new RawSense(Required(element, "id"), Required(element, "synset"));
            foreach (var relation in element.Elements("SenseRelation"))
            {
                sense.Relations.Add((Required(relation, "relType"), Required(relation, "target"), relation.Attribute(DcNamespace + "type")?.Value ?? string.Empty));
            }

            return sense;
        }

        private static string Required(XElement element, string attribute) =>
            element.Attribute(attribute)?.Value ?? throw new InvalidDataException($"A {element.Name.LocalName} element has no '{attribute}' attribute.");
    }

    private sealed record RawEntry(string Id, string Lemma, string Pos)
    {
        public List<(string Variety, string Text)> Pronunciations { get; } = [];

        public List<RawSense> Senses { get; } = [];
    }

    private sealed record RawSense(string Id, string SynsetId)
    {
        public List<(string Rel, string Target, string Subtype)> Relations { get; } = [];
    }

    private sealed record RawSynset(string Id, string Pos, string Lexfile)
    {
        public List<string> Definitions { get; } = [];

        public List<string> Examples { get; } = [];

        public List<(string Rel, string Target)> Relations { get; } = [];
    }

    /// <summary>
    ///     The derived rows of every table, in schema column order.
    /// </summary>
    private sealed class DerivedTables
    {
        public List<string[]> Source { get; } = [];

        public List<string[]> Entries { get; } = [];

        public List<string[]> Synsets { get; } = [];

        public List<string[]> Glosses { get; } = [];

        public List<string[]> Examples { get; } = [];

        public List<string[]> Senses { get; } = [];

        public List<string[]> SynsetRelations { get; } = [];

        public List<string[]> SenseRelations { get; } = [];

        public List<string[]> Pronunciations { get; } = [];

        public IEnumerable<(string Name, List<string[]> Rows)> All()
        {
            yield return ("source", Source);
            yield return ("entry", Entries);
            yield return ("synset", Synsets);
            yield return ("gloss", Glosses);
            yield return ("example", Examples);
            yield return ("sense", Senses);
            yield return ("synset_relation", SynsetRelations);
            yield return ("sense_relation", SenseRelations);
            yield return ("pronunciation", Pronunciations);
            yield return ("id_map", []);
        }
    }

    private static DerivedTables Derive(OewnDocument document, DataSchema schema, Cleaner cleaner, out int missing, out int selfRelations)
    {
        var tables = new DerivedTables();
        tables.Source.Add([document.LexiconId!, cleaner.Clean(document.Label), cleaner.Clean(document.License), cleaner.Clean(document.Url), cleaner.Clean(document.Version)]);
        var synsetPos = document.Synsets.ToDictionary(s => s.Id, s => s.Pos, StringComparer.Ordinal);
        DeriveEntries(document, tables, cleaner, synsetPos);
        DeriveSynsets(document, tables, cleaner, synsetPos);
        var synsetLevel = document.Synsets.SelectMany(s => s.Relations.Select(r => (From: s.Id, r.Rel, Sub: string.Empty, To: r.Target)));
        var senseLevel = document.Entries.SelectMany(e => e.Senses).SelectMany(s => s.Relations.Select(r => (From: s.Id, r.Rel, Sub: r.Subtype, To: r.Target)));
        var first = NormalizeRelations(synsetLevel, schema.RelationTypes("synset_relation_type"), out var missingSynset, out var selfSynset);
        var second = NormalizeRelations(senseLevel, schema.RelationTypes("sense_relation_type"), out var missingSense, out var selfSense);
        tables.SynsetRelations.AddRange(first.Select(r => new[] { r.From, r.Rel, r.To }));
        tables.SenseRelations.AddRange(second.Select(r => new[] { r.From, r.Rel, r.Sub, r.To }));
        missing = missingSynset + missingSense;
        selfRelations = selfSynset + selfSense;
        return tables;
    }

    private static void DeriveEntries(OewnDocument document, DerivedTables tables, Cleaner cleaner, Dictionary<string, string> synsetPos)
    {
        var numbers = HomographNumbers(document.Entries, cleaner);
        foreach (var entry in document.Entries)
        {
            var lemma = cleaner.Clean(entry.Lemma);
            var pos = MapEntryPos(entry.Pos);
            var tokens = lemma.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            tables.Entries.Add([entry.Id, lemma, pos, Number(numbers[entry.Id]), Number(Math.Max(tokens.Length, 1)), Number(HeadIndex(tokens, pos)), document.LexiconId!]);
            var seq = 0;
            foreach (var (variety, text) in entry.Pronunciations)
            {
                seq++;
                tables.Pronunciations.Add([entry.Id, Number(seq), cleaner.Clean(variety), cleaner.Clean(text)]);
            }

            var position = 0;
            foreach (var sense in entry.Senses)
            {
                position++;
                if (!synsetPos.ContainsKey(sense.SynsetId))
                {
                    throw new InvalidDataException($"Sense '{sense.Id}' refers to the missing synset '{sense.SynsetId}'.");
                }

                tables.Senses.Add([sense.Id, SenseKey(sense.Id, lemma), entry.Id, sense.SynsetId, Number(position)]);
            }
        }
    }

    private static void DeriveSynsets(OewnDocument document, DerivedTables tables, Cleaner cleaner, Dictionary<string, string> synsetPos)
    {
        foreach (var synset in document.Synsets)
        {
            tables.Synsets.Add([synset.Id, synset.Pos, HeadOf(synset, synsetPos), cleaner.Clean(synset.Lexfile)]);
            var seq = 0;
            foreach (var definition in synset.Definitions)
            {
                seq++;
                tables.Glosses.Add([synset.Id, Number(seq), cleaner.Clean(definition)]);
            }

            seq = 0;
            foreach (var example in synset.Examples)
            {
                seq++;
                tables.Examples.Add([synset.Id, Number(seq), string.Empty, cleaner.Clean(example)]);
            }
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string MapEntryPos(string pos) => pos == "s" ? "a" : pos;

    private static string HeadOf(RawSynset synset, Dictionary<string, string> synsetPos)
    {
        // Only a satellite has a head: the one adjective synset it is "similar" to
        if (synset.Pos != "s")
        {
            return string.Empty;
        }

        var heads = synset.Relations.Where(r => r.Rel == "similar" && synsetPos.GetValueOrDefault(r.Target) == "a").Select(r => r.Target).Distinct().ToList();
        return heads.Count == 1
            ? heads[0]
            : throw new InvalidDataException($"The satellite synset '{synset.Id}' has {heads.Count.ToString(CultureInfo.InvariantCulture)} head synsets; exactly one is required.");
    }

    private static Dictionary<string, int> HomographNumbers(List<RawEntry> entries, Cleaner cleaner)
    {
        // Group by (lemma, mapped pos): a unique pair gets 0, a shared pair is numbered by the numeric id tail
        var groups = new Dictionary<(string Lemma, string Pos), List<RawEntry>>();
        foreach (var entry in entries)
        {
            var key = (cleaner.Clean(entry.Lemma), MapEntryPos(entry.Pos));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(entry);
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var list in groups.Values)
        {
            var ordered = list.OrderBy(e => IdNumber(e.Id)).ThenBy(e => e.Id, Comparer<string>.Create(DataLayout.CompareUtf8)).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                result[ordered[i].Id] = ordered.Count == 1 ? 0 : i + 1;
            }
        }

        return result;
    }

    private static int IdNumber(string entryId)
    {
        var dash = entryId.LastIndexOf('-');
        return dash >= 0 && int.TryParse(entryId.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static readonly HashSet<string> Prepositions = new(["of", "in", "on", "at", "for", "to", "with", "by", "de", "du", "la"], StringComparer.Ordinal);

    private static int HeadIndex(string[] tokens, string pos)
    {
        // One-based index of the token that inflects: the verb of a phrasal verb, the noun before a preposition,
        // otherwise the last token (the head of an English compound). This is a default that the data may override.
        if (tokens.Length <= 1)
        {
            return 1;
        }

        if (pos == "v")
        {
            return 1;
        }

        if (pos == "n")
        {
            var preposition = Array.FindIndex(tokens, 1, t => Prepositions.Contains(t));
            if (preposition > 0)
            {
                return preposition;
            }
        }

        return tokens.Length;
    }

    private static string SenseKey(string senseId, string lemma)
    {
        // lemma%ss_type:lex_filenum:lex_id:head_word:head_id; the numeric fields exist only in the id tail
        var match = SenseIdTail().Match(senseId);
        if (!match.Success)
        {
            throw new InvalidDataException($"The sense id '{senseId}' does not end in the expected __type.lexfile.lexid.head.headid tail.");
        }

        var groups = match.Groups;
        return DataLayout.AsciiLower(lemma).Replace(' ', '_') + "%" + groups[1].Value + ":" + groups[2].Value + ":" + groups[3].Value + ":" + groups[4].Value + ":" + groups[5].Value;
    }

    private static List<(string From, string Rel, string Sub, string To)> NormalizeRelations(
        IEnumerable<(string From, string Rel, string Sub, string To)> rows,
        IReadOnlyDictionary<string, RelationType> types,
        out int missingInverse,
        out int selfRelations)
    {
        var raw = new HashSet<(string, string, string, string)>(rows);
        var stored = new HashSet<(string From, string Rel, string Sub, string To)>();
        missingInverse = 0;
        selfRelations = 0;
        foreach (var (from, rel, sub, to) in raw)
        {
            var type = types.GetValueOrDefault(rel) ?? throw new InvalidDataException($"Unknown relation type '{rel}'.");
            if (from == to)
            {
                selfRelations++;
                continue;
            }

            if (type.Inverse is not null && !raw.Contains((to, type.Inverse, sub, from)))
            {
                missingInverse++;
            }

            stored.Add(StoredForm(type, from, sub, to));
        }

        var result = stored.ToList();
        result.Sort((a, b) => CompareRelation(a, b));
        return result;
    }

    private static (string From, string Rel, string Sub, string To) StoredForm(RelationType type, string from, string sub, string to)
    {
        if (type.Symmetric)
        {
            return DataLayout.CompareUtf8(from, to) < 0 ? (from, type.Name, sub, to) : (to, type.Name, sub, from);
        }

        return type.Stored ? (from, type.Name, sub, to) : (to, type.Inverse!, sub, from);
    }

    private static int CompareRelation((string From, string Rel, string Sub, string To) a, (string From, string Rel, string Sub, string To) b)
    {
        var result = DataLayout.CompareUtf8(a.From, b.From);
        result = result != 0 ? result : DataLayout.CompareUtf8(a.Rel, b.Rel);
        result = result != 0 ? result : DataLayout.CompareUtf8(a.Sub, b.Sub);
        return result != 0 ? result : DataLayout.CompareUtf8(a.To, b.To);
    }
}
