using System.Text.RegularExpressions;

namespace WordSense.Builder.Data;

/// <summary>
///     The rules of the sense inventory: the labels of the senses, the stability of published labels, the groups and
///     their partition, and the project senses.
/// </summary>
internal sealed partial class DataValidator
{
    private const string RuleLabel = "label";
    private const string RuleStability = "stability";
    private const string RuleGroup = "group";
    private const string RulePartition = "partition";
    private const string RuleUnique = "unique";
    private const string TableSenseLabel = "sense_label";
    private const string TableSenseLabelMap = "sense_label_map";
    private const string TableSenseGroup = "sense_group";
    private const string TableGroupSense = "group_sense";
    private const string OriginProject = "project";
    private const string OriginCurated = "curated";
    private const string OriginDefault = "default";

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormedLabel();

    /// <summary>
    ///     One sense of the inventory with what the rules need from its entry.
    /// </summary>
    /// <param name="Id">The sense id.</param>
    /// <param name="Key">The sense key, empty for a project sense.</param>
    /// <param name="Synset">The synset id.</param>
    /// <param name="Lemma">The lemma of the entry.</param>
    /// <param name="Pos">The part of speech letter of the entry.</param>
    /// <param name="Tokens">The token count of the entry.</param>
    private sealed record SenseFacts(string Id, string Key, string Synset, string Lemma, string Pos, string Tokens);

    private IEnumerable<(string Path, int Line, string[] Row)> Located(string table)
    {
        if (!_tables.TryGetValue(table, out var loaded))
        {
            yield break;
        }

        foreach (var file in loaded.Files)
        {
            for (var i = 0; i < file.Rows.Count; i++)
            {
                yield return (file.Path, i + 2, file.Rows[i]);
            }
        }
    }

    private void CheckInventory()
    {
        // A data folder without any label or group has no inventory yet (a fresh import); the rules apply from the first row on
        var labels = Located(TableSenseLabel).ToList();
        var groups = Located(TableSenseGroup).ToList();
        var maps = Located(TableSenseLabelMap).ToList();
        if (labels.Count == 0 && groups.Count == 0 && maps.Count == 0 && _baseline is null)
        {
            return;
        }

        var entries = Rows(TableEntry).Where(e => e.Length > 5).GroupBy(e => e[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var senses = new Dictionary<string, SenseFacts>(StringComparer.Ordinal);
        foreach (var row in Rows(TableSense))
        {
            if (entries.TryGetValue(row[2], out var entry))
            {
                senses[row[0]] = new SenseFacts(row[0], row[1], row[3], entry[1], entry[2], entry[4]);
            }
        }

        var labelOf = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (_, _, row) in labels)
        {
            labelOf[row[0]] = row;
        }

        var groupIds = groups.Select(g => g.Row[0]).ToHashSet(StringComparer.Ordinal);
        var publicIds = CheckLabels(labels, senses, labelOf, groupIds);
        CheckProjectSenses(labels, senses);
        CheckLabelMap(maps, publicIds, groupIds);
        CheckStability(maps, publicIds);
        CheckGroups(groups, senses, labelOf.Count > 0 || groups.Count > 0);
    }

    // ---- labels ----

    private static bool InScope(SenseFacts sense) => sense.Tokens == "1" || sense.Key.Length == 0;

    private Dictionary<string, string> CheckLabels(
        List<(string Path, int Line, string[] Row)> labels,
        Dictionary<string, SenseFacts> senses,
        Dictionary<string, string[]> labelOf,
        HashSet<string> groupIds)
    {
        var labelPath = Path.Join(_root, TableSenseLabel);
        foreach (var sense in senses.Values.Where(s => InScope(s) && !labelOf.ContainsKey(s.Id)).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            Report(labelPath, 0, RuleLabel, $"Sense '{sense.Id}' of '{sense.Lemma}' has no label.");
        }

        var publicIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var owner = new Dictionary<(string Lemma, string Label), string>();
        foreach (var (path, line, row) in labels)
        {
            if (!senses.TryGetValue(row[0], out var sense))
            {
                continue;
            }

            if (!WellFormedLabel().IsMatch(row[1]))
            {
                Report(path, line, RuleLabel, $"{row[0]}: the label '{row[1]}' must be lower-case ASCII letters, digits and single hyphens.");
            }

            if (!owner.TryAdd((sense.Lemma, row[1]), row[0]))
            {
                Report(path, line, RuleUnique, $"{row[0]}: the label '{row[1]}' is already the label of '{owner[(sense.Lemma, row[1])]}' of lemma '{sense.Lemma}'.");
            }

            var id = sense.Lemma + "#" + row[1];
            publicIds[row[0]] = id;
            if (groupIds.Contains(id))
            {
                Report(path, line, RuleLabel, $"{row[0]}: the id '{id}' is the id of a sense and of a group.");
            }
        }

        return publicIds;
    }

    private void CheckProjectSenses(List<(string Path, int Line, string[] Row)> labels, Dictionary<string, SenseFacts> senses)
    {
        var glossed = Rows("gloss").Select(g => g[0]).ToHashSet(StringComparer.Ordinal);
        foreach (var (path, line, row) in labels)
        {
            if (!senses.TryGetValue(row[0], out var sense))
            {
                continue;
            }

            var isProject = row[2] == OriginProject;
            if (isProject && sense.Key.Length > 0)
            {
                Report(path, line, TableSense, $"{row[0]}: a project sense has no sense key but this one has '{sense.Key}'.");
            }

            if (isProject && !glossed.Contains(sense.Synset))
            {
                Report(path, line, TableSense, $"{row[0]}: a project sense needs an authored gloss.");
            }

            if (!isProject && sense.Key.Length == 0)
            {
                Report(path, line, TableSense, $"{row[0]}: a sense without a sense key is a project sense and must have the origin '{OriginProject}'.");
            }
        }
    }

    // ---- the id map and the published labels ----

    private void CheckLabelMap(List<(string Path, int Line, string[] Row)> maps, Dictionary<string, string> publicIds, HashSet<string> groupIds)
    {
        var current = publicIds.Values.ToHashSet(StringComparer.Ordinal);
        current.UnionWith(groupIds);
        foreach (var (path, line, row) in maps)
        {
            if (current.Contains(row[0]))
            {
                Report(path, line, RuleStability, $"The retired id '{row[0]}' is the id of a current sense or group; a retired id is never reused.");
            }

            if (row[1].Length > 0 && !current.Contains(row[1]))
            {
                Report(path, line, RuleStability, $"The id map sends '{row[0]}' to '{row[1]}', which is not the id of a current sense or group.");
            }
        }
    }

    private void CheckStability(
        List<(string Path, int Line, string[] Row)> maps,
        Dictionary<string, string> publicIds)
    {
        if (_baseline is null)
        {
            return;
        }

        var published = ReadPublished(_baseline);
        if (published is null)
        {
            return;
        }

        var retired = maps.Select(m => m.Row[0]).ToHashSet(StringComparer.Ordinal);
        var bySense = publicIds.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        var path = Path.Join(_root, TableSenseLabel);
        foreach (var (senseId, oldId) in published.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (bySense.TryGetValue(oldId, out var holder) && holder != senseId)
            {
                Report(path, 0, RuleStability, $"The published id '{oldId}' of sense '{senseId}' is now the id of sense '{holder}'; a published id is never reused.");
                continue;
            }

            var hasNow = publicIds.TryGetValue(senseId, out var now);
            if ((hasNow && now == oldId) || retired.Contains(oldId))
            {
                continue;
            }

            var what = hasNow ? $"changed to '{now}'" : "is gone";
            Report(path, 0, RuleStability, $"The published id '{oldId}' of sense '{senseId}' {what} and the id map does not record it.");
        }
    }

    // The published ids of the baseline (sense id to lemma#label), or null (and a violation) when the baseline cannot be read
    private Dictionary<string, string>? ReadPublished(string baseline)
    {
        var path = Path.Join(baseline, TableSenseLabel);
        try
        {
            if (!Directory.Exists(baseline))
            {
                Report(baseline, 0, RuleStability, "The baseline data folder does not exist.");
                return null;
            }

            var lemmaOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in DataTables.Read(baseline, _schema.Table(TableEntry)))
            {
                lemmaOf[row[0]] = row[1];
            }

            var senseLemma = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in DataTables.Read(baseline, _schema.Table(TableSense)))
            {
                senseLemma[row[0]] = lemmaOf.GetValueOrDefault(row[2], string.Empty);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in DataTables.Read(baseline, _schema.Table(TableSenseLabel)))
            {
                result[row[0]] = senseLemma.GetValueOrDefault(row[0], string.Empty) + "#" + row[1];
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or IndexOutOfRangeException or System.Text.DecoderFallbackException)
        {
            Report(path, 0, RuleStability, $"The baseline cannot be read: {ex.Message}");
            return null;
        }
    }

    // ---- groups ----

    private void CheckGroups(List<(string Path, int Line, string[] Row)> groups, Dictionary<string, SenseFacts> senses, bool inventoryExists)
    {
        if (!inventoryExists)
        {
            return;
        }

        var groupRows = groups.ToDictionary(g => g.Row[0], g => g.Row, StringComparer.Ordinal);
        var respelling = Rows("sense_respelling").GroupBy(r => r[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.Ordinal);
        var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (path, line, row) in Located(TableGroupSense))
        {
            groupOf[row[1]] = row[0];
            if (!members.TryGetValue(row[0], out var list))
            {
                members[row[0]] = list = [];
            }

            list.Add(row[1]);
            if (senses.TryGetValue(row[1], out var sense) && groupRows.TryGetValue(row[0], out var memberGroup))
            {
                CheckMember(path, line, memberGroup, sense);
            }
        }

        var curated = groups.Where(g => g.Row[4] == OriginCurated).Select(g => g.Row[1]).ToHashSet(StringComparer.Ordinal);
        var curatedPos = groups.Where(g => g.Row[4] == OriginCurated).Select(g => (g.Row[1], g.Row[2])).ToHashSet();
        var posNames = _schema.Vocabulary("pos");
        foreach (var (path, line, group) in groups)
        {
            var expected = group[1] + "#" + group[3];
            if (group[0] != expected)
            {
                Report(path, line, RuleGroup, $"{group[0]}: the id of a group is its lemma, '#' and its label ('{expected}').");
            }

            if (!WellFormedLabel().IsMatch(group[3]))
            {
                Report(path, line, RuleGroup, $"{group[0]}: the label '{group[3]}' must be lower-case ASCII letters, digits and single hyphens.");
            }

            if (!members.TryGetValue(group[0], out var list) || list.Count == 0)
            {
                Report(path, line, RuleGroup, $"{group[0]}: a group needs at least one sense.");
                continue;
            }

            var names = list.Select(m => respelling.GetValueOrDefault(m)).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).ToList();
            if (names.Count > 1)
            {
                Report(path, line, RuleGroup, $"{group[0]}: a group must not mix respellings ({string.Join(", ", names)}).");
            }

            if (group[4] == OriginDefault)
            {
                var name = group[1] + "#" + posNames[group[2]] + "-group";
                if (group[0] != name)
                {
                    Report(path, line, RuleGroup, $"{group[0]}: a default group is named '{name}'.");
                }

                if (curatedPos.Contains((group[1], group[2])))
                {
                    Report(path, line, RuleGroup, $"{group[0]}: the lemma '{group[1]}' has curated groups of this part of speech, so it has no default group of it.");
                }
            }
            else if (!curated.Contains(group[1]))
            {
                Report(path, line, RuleGroup, $"{group[0]}: the lemma '{group[1]}' has no curated group, so its groups must be default groups.");
            }
        }

        var partitionPath = Path.Join(_root, TableGroupSense);
        foreach (var sense in senses.Values.Where(s => s.Tokens == "1").OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!groupOf.TryGetValue(sense.Id, out var groupId))
            {
                var what = curated.Contains(sense.Lemma)
                    ? $"Sense '{sense.Id}' of the curated lemma '{sense.Lemma}' is in no group."
                    : $"The lemma '{sense.Lemma}' is not curated and needs the default group '{sense.Lemma}#{posNames[sense.Pos]}-group' for sense '{sense.Id}'.";
                Report(partitionPath, 0, RulePartition, what);
            }
            else if (!curated.Contains(sense.Lemma) && groupRows.TryGetValue(groupId, out var group) && (group[4] != OriginDefault || group[0] != sense.Lemma + "#" + posNames[sense.Pos] + "-group"))
            {
                Report(partitionPath, 0, RuleGroup, $"Sense '{sense.Id}' of the lemma '{sense.Lemma}', which is not curated, is in the group '{groupId}' and not in the default group of its part of speech.");
            }
        }
    }

    private void CheckMember(string path, int line, string[] group, SenseFacts sense)
    {
        if (sense.Lemma != group[1])
        {
            Report(path, line, RuleGroup, $"{group[0]}: the sense '{sense.Id}' belongs to the lemma '{sense.Lemma}', not '{group[1]}'.");
        }

        if (sense.Pos != group[2])
        {
            Report(path, line, RuleGroup, $"{group[0]}: the sense '{sense.Id}' has part of speech '{sense.Pos}', not '{group[2]}'; a group has one part of speech.");
        }
    }
}
