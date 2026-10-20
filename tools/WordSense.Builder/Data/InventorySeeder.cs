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
/// <param name="DroppedSingletons">The curated groups left out because they have only one sense.</param>
/// <param name="DroppedWholePartOfSpeech">The curated groups left out because they have every sense of their lemma and part of speech.</param>
/// <param name="RemovedStale">The group rows removed because their origin is not <c>curated</c> (the retired default groups).</param>
internal sealed record SeedRun(
    int ProjectSenses,
    int CuratedLabels,
    int CuratedGroups,
    int SplitGroups,
    int DroppedSingletons,
    int DroppedWholePartOfSpeech,
    int RemovedStale)
{
    /// <summary>
    ///     Renders the counts as one line.
    /// </summary>
    /// <returns>The summary line.</returns>
    public string Render() => string.Create(
        CultureInfo.InvariantCulture,
        $"seed-inventory: {ProjectSenses} project senses added, {CuratedLabels} curated labels, {CuratedGroups} curated groups ({SplitGroups} split by part of speech); left out: {DroppedSingletons} one-sense groups, {DroppedWholePartOfSpeech} groups of a whole part of speech; {RemovedStale} stale group rows removed.");
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
///     <c>groups.tsv</c> (<c>group_id</c>, <c>lemma</c>, <c>part_of_speech</c>, <c>member_sense_ids</c>, <c>kind</c> and
///     <c>evidence</c>); the ids of senses there are public ids (<c>lemma#label</c>) and the id of a curated group is
///     <c>lemma#slug</c>, which the seeder writes as the group id <c>lemma#group:slug</c>.
///     </para>
///     <para>
///     A group exists only when it is needed: a set of at least two, but not all, senses of one lemma and one part of
///     speech. A curated group that mixes two parts of speech is split by part of speech; a group with one sense, and a
///     group with every sense of its lemma and part of speech, is left out. A group that is written needs a
///     <c>kind</c> of the <c>group_kind</c> vocabulary and non-empty <c>evidence</c> (the admission rule: the
///     curator's same-meaning judgment plus observed confusion or near-identical glosses); the evidence is curation
///     metadata and is not written to the data. The id of a group carries the marker <c>group:</c>, so it can never be
///     the id of a sense.
///     </para>
///     <para>
///     The run is idempotent. It replaces a generated label (which was never published) by the curated one and
///     refuses to change a curated or project label that a sense already has. It is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class InventorySeeder
{
    /// <summary>The marker that follows the <c>#</c> in the id of a group (<c>lemma#group:slug</c>).</summary>
    public const string GroupMarker = "group:";

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

        // A group has the origin curated only; a group of any other origin (the retired default groups) is stale
        var stale = groups.Where(g => g[4] != SenseLabelGenerator.GroupCurated).Select(g => g[0]).ToHashSet(StringComparer.Ordinal);
        groups.RemoveAll(g => stale.Contains(g[0]));
        members.RemoveAll(m => stale.Contains(m[0]));
        var counts = new GroupImport(0, 0, 0, 0, 0);
        if (curationDirectory is not null)
        {
            counts = ImportCuration(curationDirectory, entries, senses, glosses, labels, groups, members, posNames, schema.Vocabulary("group_kind"));
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
        return new SeedRun(added, counts.Labels, counts.Groups, counts.Split, counts.Singletons, counts.WholePartOfSpeech, stale.Count);
    }

    private sealed record GroupImport(int Labels, int Groups, int Split, int Singletons, int WholePartOfSpeech);

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

    private static GroupImport ImportCuration(
        string folder,
        Dictionary<string, string[]> entries,
        List<string[]> senses,
        List<string[]> glosses,
        List<string[]> labels,
        List<string[]> groups,
        List<string[]> members,
        IReadOnlyDictionary<string, string> posNames,
        IReadOnlyDictionary<string, string> kinds)
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

            // The part of speech of a sense is the one of its Open English WordNet entry (an adjective satellite is an adjective)
            if (!posNames.TryGetValue(entries[sense[2]][2], out var dataPos) || !string.Equals(dataPos, row["part_of_speech"], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The curated sense '{publicId}' has the part of speech '{row["part_of_speech"]}', but the sense '{id}' of the data is a {dataPos ?? entries[sense[2]][2]}.");
            }

            if (isProject)
            {
                ReplaceGloss(glosses, sense[3], row["gloss"]);
            }

            var origin = isProject ? SenseLabelGenerator.OriginProject : "curated";
            labelOf.TryGetValue(id, out var existing);
            if (existing is not null && (string.Equals(existing[2], SenseLabelGenerator.OriginGenerated, StringComparison.Ordinal) || (isProject && string.Equals(existing[2], SenseLabelGenerator.OriginProject, StringComparison.Ordinal))))
            {
                // A generated or project label that is not curated yet was never published: the curation takes over
                // the sense, with its label when it differs
                if (existing[1] != label || existing[2] != origin)
                {
                    newLabels++;
                }

                existing[1] = label;
                existing[2] = origin;
            }
            else if (existing is not null && existing[1] != label)
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

        var counts = SplitGroups(curatedGroups, entries, senseRow, dataId, byLemmaPos, labels, groups, members, posNames, kinds);
        return counts with { Labels = newLabels };
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

    private static GroupImport SplitGroups(
        List<Dictionary<string, string>> curatedGroups,
        Dictionary<string, string[]> entries,
        Dictionary<string, string[]> senseRow,
        Dictionary<string, string> dataId,
        Dictionary<(string Lemma, string Pos), List<string>> byLemmaPos,
        List<string[]> labels,
        List<string[]> groups,
        List<string[]> members,
        IReadOnlyDictionary<string, string> posNames,
        IReadOnlyDictionary<string, string> kinds)
    {
        int split = 0, singletons = 0, wholePos = 0;

        // The id of a group carries the marker, so it can never be the id of a sense; the public id of every labeled
        // sense is still collected as a defensive check
        var publicSenseIds = labels.Select(l => entries[senseRow[l[0]][2]][1] + "#" + l[1]).ToHashSet(StringComparer.Ordinal);
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
                var memberIds = part.Select(m => dataId[m]).Distinct(StringComparer.Ordinal).ToList();
                if (memberIds.Count < 2)
                {
                    singletons++;
                    continue;
                }

                if (memberIds.Count >= byLemmaPos[(lemma, part.Key)].Count)
                {
                    wholePos++;
                    continue;
                }

                var label = part.Key == primary ? baseLabel : baseLabel + "-" + posNames[part.Key];
                if (SenseLabelGenerator.Slug(label) != label)
                {
                    throw new InvalidDataException($"The curated group '{group["group_id"]}' needs a slug of lower-case ASCII letters, digits and single hyphens; '{label}' is not one.");
                }

                var kind = group.GetValueOrDefault("kind", string.Empty).Trim();
                if (!kinds.ContainsKey(kind))
                {
                    throw new InvalidDataException($"The curated group '{group["group_id"]}' has the kind '{kind}', which is not one of: {string.Join(", ", kinds.Keys)}.");
                }

                if (group.GetValueOrDefault("evidence", string.Empty).Trim().Length == 0)
                {
                    throw new InvalidDataException($"The curated group '{group["group_id"]}' has no evidence; a group needs observed confusion or near-identical glosses besides the curator's judgment.");
                }

                var id = lemma + "#" + GroupMarker + label;
                if (publicSenseIds.Contains(id) || groups.Any(g => g[0] == id))
                {
                    throw new InvalidDataException($"The group id '{id}' is already the id of a sense or of another group.");
                }

                groups.Add([id, lemma, part.Key, label, SenseLabelGenerator.GroupCurated, kind]);
                members.AddRange(memberIds.Select(m => new[] { id, m }));
                written++;
            }
        }

        return new GroupImport(0, written, split, singletons, wholePos);
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
