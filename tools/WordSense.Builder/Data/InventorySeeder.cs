using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The counts of a run of the inventory seeder.
/// </summary>
/// <param name="ProjectSenses">The project senses added for closed-class entries that had no sense.</param>
/// <param name="CuratedLabels">The curated and project labels written from the curation files.</param>
/// <param name="CuratedGroups">The curated groups written.</param>
/// <param name="SplitGroups">The curated groups that had to be split because their senses have two parts of speech.</param>
/// <param name="RenamedGroups">The curated groups renamed because their id was the id of a sense.</param>
internal sealed record SeedRun(int ProjectSenses, int CuratedLabels, int CuratedGroups, int SplitGroups, int RenamedGroups)
{
    /// <summary>
    ///     Renders the counts as one line.
    /// </summary>
    /// <returns>The summary line.</returns>
    public string Render() => string.Create(
        CultureInfo.InvariantCulture,
        $"seed-inventory: {ProjectSenses} project senses added, {CuratedLabels} curated labels, {CuratedGroups} curated groups ({SplitGroups} split by part of speech, {RenamedGroups} renamed because the id was a sense id).");
}

/// <summary>
///     Seeds the sense inventory: the project senses of the closed-class entries, and the curated labels and groups of
///     a curation folder.
/// </summary>
/// <remarks>
///     <para>
///     A closed-class entry (determiner, pronoun, preposition, conjunction, interjection or auxiliary) has no sense in
///     Open English WordNet, so each one gets a project sense: a sense, a synset and a gloss of the source <c>dema</c>,
///     without a sense key. The curation folder holds the reviewed curated lemmas as <c>senses.tsv</c> (columns
///     <c>sense_id</c>, <c>oewn_sense_id</c>, <c>part_of_speech</c>, <c>gloss</c>, <c>source</c> and <c>group_id</c>) and
///     <c>groups.tsv</c> (<c>group_id</c>, <c>lemma</c>, <c>part_of_speech</c> and <c>member_sense_ids</c>); the ids there
///     are public ids (<c>lemma#label</c>). A curated group that mixes two parts of speech is split by part of speech,
///     and a group whose id is also the id of a sense gets the suffix <c>-group</c>, because the ids of senses and
///     groups share one namespace.
///     </para>
///     <para>
///     The run is idempotent and refuses to change a label that a sense already has. It is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class InventorySeeder
{
    private const string ClosedClass = "upcidx";

    /// <summary>
    ///     Adds the project senses and imports the curated labels and groups.
    /// </summary>
    /// <param name="dataDirectory">The data folder.</param>
    /// <param name="curationDirectory">The folder with <c>senses.tsv</c> and <c>groups.tsv</c>, or null to add only the project senses.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the curation does not fit the data; nothing is written.</exception>
    public static SeedRun Run(string dataDirectory, string? curationDirectory)
    {
        var schema = DataSchema.Load(Path.Join(dataDirectory, "schema.yaml"));
        var posNames = schema.Vocabulary("pos");
        var entries = DataTables.Read(dataDirectory, schema.Table("entry")).ToDictionary(r => r[0], r => r, StringComparer.Ordinal);
        var senses = DataTables.Read(dataDirectory, schema.Table("sense")).ToList();
        var synsets = DataTables.Read(dataDirectory, schema.Table("synset")).ToList();
        var glosses = DataTables.Read(dataDirectory, schema.Table("gloss")).ToList();
        var added = AddProjectSenses(entries, senses, synsets, glosses, posNames);

        var labels = DataTables.Read(dataDirectory, schema.Table("sense_label")).ToList();
        var groups = DataTables.Read(dataDirectory, schema.Table("sense_group")).ToList();
        var members = DataTables.Read(dataDirectory, schema.Table("group_sense")).ToList();
        var counts = (Labels: 0, Groups: 0, Split: 0, Renamed: 0);
        if (curationDirectory is not null)
        {
            counts = ImportCuration(curationDirectory, entries, senses, glosses, labels, groups, members, posNames);
        }

        var resolver = new PartitionResolver(schema.FileSplit);
        foreach (var entry in entries.Values)
        {
            resolver.AddEntry(entry[0], entry[1]);
        }

        foreach (var sense in senses)
        {
            resolver.AddSense(sense[0], sense[2], sense[3], sense[1]);
        }

        DataTables.Write(dataDirectory, schema.Table("sense"), senses, resolver);
        DataTables.Write(dataDirectory, schema.Table("synset"), synsets, resolver);
        DataTables.Write(dataDirectory, schema.Table("gloss"), glosses, resolver);
        DataTables.Write(dataDirectory, schema.Table("sense_label"), labels, resolver);
        DataTables.Write(dataDirectory, schema.Table("sense_group"), groups, resolver);
        DataTables.Write(dataDirectory, schema.Table("group_sense"), members, resolver);
        return new SeedRun(added, counts.Labels, counts.Groups, counts.Split, counts.Renamed);
    }

    private static int AddProjectSenses(
        Dictionary<string, string[]> entries,
        List<string[]> senses,
        List<string[]> synsets,
        List<string[]> glosses,
        IReadOnlyDictionary<string, string> posNames)
    {
        var sensed = senses.Select(s => s[2]).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var entry in entries.Values.Where(e => ClosedClass.Contains(e[2], StringComparison.Ordinal) && !sensed.Contains(e[0])).OrderBy(e => e[0], StringComparer.Ordinal))
        {
            var stem = entry[0].StartsWith("dema-", StringComparison.Ordinal) ? entry[0]["dema-".Length..] : entry[0];
            var senseId = entry[0] + "-1";
            var synsetId = "dema-syn-" + stem + "-1";
            var name = posNames[entry[2]];
            senses.Add([senseId, string.Empty, entry[0], synsetId, "1"]);
            synsets.Add([synsetId, entry[2], string.Empty, string.Empty]);
            glosses.Add([synsetId, "1", $"The {name} \"{entry[1]}\"; a project sense, because Open English WordNet has no {name} sense of this word."]);
            added++;
        }

        return added;
    }

    private static (int Labels, int Groups, int Split, int Renamed) ImportCuration(
        string folder,
        Dictionary<string, string[]> entries,
        List<string[]> senses,
        List<string[]> glosses,
        List<string[]> labels,
        List<string[]> groups,
        List<string[]> members,
        IReadOnlyDictionary<string, string> posNames)
    {
        var curatedSenses = ReadTsv(Path.Join(folder, "senses.tsv"));
        var curatedGroups = ReadTsv(Path.Join(folder, "groups.tsv"));
        var byLemmaPos = senses.GroupBy(s => (Lemma: entries[s[2]][1], Pos: entries[s[2]][2])).ToDictionary(g => g.Key, g => g.Select(s => s[0]).OrderBy(s => s, Comparer<string>.Create(DataLayout.CompareUtf8)).ToList());
        var senseRow = senses.ToDictionary(s => s[0], s => s, StringComparer.Ordinal);
        var dataId = new Dictionary<string, string>(StringComparer.Ordinal);
        var labelOf = labels.ToDictionary(l => l[0], l => l, StringComparer.Ordinal);
        var newLabels = 0;
        foreach (var row in curatedSenses)
        {
            var publicId = row["sense_id"];
            var cut = publicId.IndexOf('#', StringComparison.Ordinal);
            var (lemma, label) = (publicId[..cut], publicId[(cut + 1)..]);
            var isProject = row["source"] == "project" || row["oewn_sense_id"].Length == 0;
            var id = isProject ? ProjectSenseOf(lemma, row["part_of_speech"], posNames, byLemmaPos, senseRow) : row["oewn_sense_id"];
            if (!senseRow.TryGetValue(id, out var sense) || entries[sense[2]][1] != lemma)
            {
                throw new InvalidDataException($"The curated sense '{publicId}' does not match a sense of the lemma '{lemma}' in the data.");
            }

            if (isProject)
            {
                ReplaceGloss(glosses, sense[3], row["gloss"]);
            }

            var origin = isProject ? SenseLabelGenerator.OriginProject : "curated";
            if (labelOf.TryGetValue(id, out var existing) && existing[1] != label)
            {
                throw new InvalidDataException($"The sense '{id}' already has the label '{existing[1]}'; the curated label '{label}' would change a published id.");
            }

            if (existing is null)
            {
                var added = new[] { id, label, origin };
                labels.Add(added);
                labelOf[id] = added;
                newLabels++;
            }

            dataId[publicId] = id;
        }

        var written = SplitGroups(curatedGroups, entries, senseRow, dataId, groups, members, posNames, out var split, out var renamed);
        return (newLabels, written, split, renamed);
    }

    private static string ProjectSenseOf(
        string lemma,
        string partOfSpeech,
        IReadOnlyDictionary<string, string> posNames,
        Dictionary<(string Lemma, string Pos), List<string>> byLemmaPos,
        Dictionary<string, string[]> senseRow)
    {
        var letter = posNames.FirstOrDefault(p => string.Equals(p.Value, partOfSpeech, StringComparison.OrdinalIgnoreCase)).Key
            ?? throw new InvalidDataException($"The part of speech '{partOfSpeech}' of the project sense of '{lemma}' is unknown.");
        var candidates = byLemmaPos.GetValueOrDefault((lemma, letter), []).Where(s => senseRow[s][1].Length == 0).ToList();
        return candidates.Count == 1
            ? candidates[0]
            : throw new InvalidDataException($"The project sense '{lemma}' ({partOfSpeech}) needs exactly one sense without a key of that lemma and part of speech, but the data has {candidates.Count.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static void ReplaceGloss(List<string[]> glosses, string synsetId, string text)
    {
        var index = glosses.FindIndex(g => g[0] == synsetId && g[1] == "1");
        if (index < 0)
        {
            glosses.Add([synsetId, "1", text]);
        }
        else
        {
            glosses[index] = [synsetId, "1", text];
        }
    }

    private static int SplitGroups(
        List<Dictionary<string, string>> curatedGroups,
        Dictionary<string, string[]> entries,
        Dictionary<string, string[]> senseRow,
        Dictionary<string, string> dataId,
        List<string[]> groups,
        List<string[]> members,
        IReadOnlyDictionary<string, string> posNames,
        out int split,
        out int renamed)
    {
        split = 0;
        renamed = 0;
        var publicSenseIds = dataId.Keys.ToHashSet(StringComparer.Ordinal);
        var curatedLemmas = curatedGroups.Select(g => g["lemma"]).ToHashSet(StringComparer.Ordinal);

        // The groups of the curated lemmas are replaced as a whole; the groups of other lemmas are kept
        var dropped = groups.Where(g => curatedLemmas.Contains(g[1])).Select(g => g[0]).ToHashSet(StringComparer.Ordinal);
        groups.RemoveAll(g => dropped.Contains(g[0]));
        members.RemoveAll(m => dropped.Contains(m[0]));
        var written = 0;
        foreach (var group in curatedGroups)
        {
            var lemma = group["lemma"];
            var baseLabel = group["group_id"][(group["group_id"].IndexOf('#', StringComparison.Ordinal) + 1)..];
            var listed = group["member_sense_ids"].Split(',');
            var byPos = listed.GroupBy(m => entries[senseRow[dataId[m]][2]][2]).ToList();
            var primary = PrimaryPos(group["part_of_speech"], posNames, byPos.Select(p => p.Key).ToList()) ?? byPos[0].Key;
            split += byPos.Count > 1 ? 1 : 0;
            foreach (var part in byPos)
            {
                var label = part.Key == primary ? baseLabel : baseLabel + "-" + posNames[part.Key];
                if (publicSenseIds.Contains(lemma + "#" + label))
                {
                    label += SenseLabelGenerator.GroupSuffix;
                    renamed++;
                }

                var id = lemma + "#" + label;
                if (publicSenseIds.Contains(id) || groups.Any(g => g[0] == id))
                {
                    throw new InvalidDataException($"The group id '{id}' is already the id of a sense or of another group.");
                }

                groups.Add([id, lemma, part.Key, label, SenseLabelGenerator.GroupCurated]);
                members.AddRange(part.Select(m => new[] { id, dataId[m] }));
                written++;
            }
        }

        return written;
    }

    // The part of speech that keeps the label of a split group: the first one the group names that its senses have
    private static string? PrimaryPos(string listed, IReadOnlyDictionary<string, string> posNames, List<string> present)
    {
        foreach (var name in listed.Split('/'))
        {
            var letter = posNames.FirstOrDefault(p => string.Equals(p.Value, name.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
            if (letter is not null && present.Contains(letter))
            {
                return letter;
            }
        }

        return null;
    }

    private static List<Dictionary<string, string>> ReadTsv(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The curation file '{path}' does not exist.");
        }

        var lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
        var header = lines[0].Split('\t');
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var cells = line.Split('\t');
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Length; i++)
            {
                row[header[i]] = i < cells.Length ? cells[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }
}
