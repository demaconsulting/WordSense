using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     Writes the temporary working file that maps the class ids of the retired class inventory to the new senses and
///     groups, for the relabeling of the corpus.
/// </summary>
/// <remarks>
///     <para>
///     This is working data of the migration to fine senses, not a feature: the file lives in <c>corpus/migration</c>,
///     is used only to relabel the anchor, holdout and real-text sentences, and is deleted, with this class and its
///     command, when the relabeling is done. It is never validated as part of the data, shipped, published or listed in
///     the appendix.
///     </para>
///     <para>
///     One row per class and sense (a class of kind <c>sense_group</c>, <c>no_oewn</c> or <c>unmapped</c>) with the id of
///     the new sense and of its group, or one row per linked entry (a class of kind <c>lemma_split</c> or
///     <c>feature_only</c>) with the entry and the features. The respelling and the meaning of the old class come from
///     the retired sense inventory of the corpus. The output is deterministic.
///     </para>
/// </remarks>
internal static class LegacyClassMap
{
    private const string Header = "class_id\tkind\tlemma\tpos\tmeaning\trespelling\tsense_count\tsense_id\tgroup_id\tentry_id\tentry_lemma\tentry_pos\tfeatures";

    /// <summary>
    ///     Writes the working file.
    /// </summary>
    /// <param name="dataDirectory">The data folder, labeled and grouped.</param>
    /// <param name="corpusDirectory">The corpus folder with the retired sense inventory in <c>senses</c>.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>The number of classes and rows written.</returns>
    /// <exception cref="InvalidDataException">Thrown when a sense of a class has no label or no group, so the file would name no target.</exception>
    public static (int Classes, int Rows) Write(string dataDirectory, string corpusDirectory, string output)
    {
        var schema = DataSchema.Load(Path.Join(dataDirectory, "schema.yaml"));
        var entries = DataTables.Read(dataDirectory, schema.Table("entry")).ToDictionary(r => r[0], r => r, StringComparer.Ordinal);
        var senses = DataTables.Read(dataDirectory, schema.Table("sense")).ToDictionary(r => r[0], r => r, StringComparer.Ordinal);
        var labels = DataTables.Read(dataDirectory, schema.Table("sense_label")).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var groupOf = DataTables.Read(dataDirectory, schema.Table("group_sense")).ToDictionary(r => r[1], r => r[0], StringComparer.Ordinal);
        var classSenses = DataTables.Read(dataDirectory, schema.Table("class_sense")).GroupBy(r => r[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(r => r[1]).ToList(), StringComparer.Ordinal);
        var classEntries = DataTables.Read(dataDirectory, schema.Table("class_entry")).GroupBy(r => r[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var inventory = SenseInventory.ReadDirectory(Path.Join(corpusDirectory, "senses")).Rows.ToDictionary(r => (r.Lemma, r.Label));
        var rows = new List<(string ClassId, string SenseId, string Line)>();
        var classes = 0;
        foreach (var cls in DataTables.Read(dataDirectory, schema.Table("coarse_class")))
        {
            classes++;
            var old = inventory.GetValueOrDefault((cls[1], cls[2]));
            var common = new[] { cls[0], cls[4], cls[1], cls[3], old?.Meaning ?? string.Empty, old?.Respelling ?? string.Empty };
            if (classEntries.TryGetValue(cls[0], out var links))
            {
                foreach (var link in links)
                {
                    var entry = entries[link[1]];
                    rows.Add((cls[0], link[1], Line(common, "0", string.Empty, string.Empty, link[1], entry[1], entry[2], link[2])));
                }
            }

            var members = classSenses.GetValueOrDefault(cls[0], []);
            foreach (var senseId in members)
            {
                var label = labels.GetValueOrDefault(senseId)
                    ?? throw new InvalidDataException($"The sense '{senseId}' of the class '{cls[0]}' has no label; run label-senses first.");
                var group = groupOf.GetValueOrDefault(senseId)
                    ?? throw new InvalidDataException($"The sense '{senseId}' of the class '{cls[0]}' is in no group; run label-senses first.");
                var lemma = entries[senses[senseId][2]][1];
                rows.Add((cls[0], senseId, Line(common, members.Count.ToString(CultureInfo.InvariantCulture), lemma + "#" + label, group, string.Empty, string.Empty, string.Empty, string.Empty)));
            }
        }

        var ordered = rows.OrderBy(r => r.ClassId, Comparer<string>.Create(DataLayout.CompareUtf8)).ThenBy(r => r.SenseId, Comparer<string>.Create(DataLayout.CompareUtf8)).Select(r => r.Line);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, Header + "\n" + string.Join('\n', ordered) + "\n", new UTF8Encoding(false));
        return (classes, rows.Count);
    }

    private static string Line(string[] common, string count, string senseId, string groupId, string entryId, string entryLemma, string entryPos, string features) =>
        string.Join('\t', [.. common, count, senseId, groupId, entryId, entryLemma, entryPos, features]);
}
