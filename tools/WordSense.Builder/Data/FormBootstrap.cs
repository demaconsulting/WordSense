using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Data;

/// <summary>
///     The result of <see cref="FormBootstrap.Run"/>.
/// </summary>
/// <param name="NewEntries">The number of supplementary entries added.</param>
/// <param name="Forms">The number of analysis rows written.</param>
/// <param name="Spellings">The number of distinct spellings.</param>
/// <param name="ScopeRows">The number of scope rows written.</param>
/// <param name="Analyses">The number of answers of the legacy table that the rows stand for.</param>
internal sealed record BootstrapResult(int NewEntries, int Forms, int Spellings, int ScopeRows, int Analyses);

/// <summary>
///     Creates the <c>form</c> and <c>form_sense_scope</c> tables (and the sense-less entries they need) from the legacy
///     word table of the corpus, once. The result is committed and authoritative; the corpus is not read again by the
///     image compiler.
/// </summary>
/// <remarks>
///     Every answer of the legacy table becomes one analysis: its entry is the entry of the class it names (the
///     entry that holds most of the senses of the class, or the entry the class links), else the entry of its lemma
///     and part of speech, else a new entry of the <c>dema</c> source without senses. Consecutive answers of one entry
///     and one feature set with ascending class ids share one row whose scope lists the classes. The 19 inflected
///     forms that the corpus lists under a lemma that cannot produce them are re-pointed to the right lemma.
/// </remarks>
internal static class FormBootstrap
{
    private const string Source = "dema";

    // spelling -> (lemma of the legacy answer, lemma of the entry the analysis must use)
    private static readonly Dictionary<string, (string From, string To)> LemmaFixes = new(StringComparer.Ordinal)
    {
        ["wicks"] = ("wicked", "wick"),
        ["wick"] = ("wicked", "wick"),
        ["wicking"] = ("wicked", "wick"),
        ["re-sign"] = ("resign", "re-sign"),
        ["re-signs"] = ("resign", "re-sign"),
        ["re-signed"] = ("resign", "re-sign"),
        ["re-signing"] = ("resign", "re-sign"),
    };

    /// <summary>
    ///     Runs the bootstrap and writes the tables.
    /// </summary>
    /// <param name="dataDirectory">The data directory (its entries are extended, its form tables replaced).</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus and the class tables disagree.</exception>
    public static BootstrapResult Run(string dataDirectory, string corpus)
    {
        var schema = DataSchema.Load(Path.Combine(dataDirectory, "schema.yaml"));
        var entryTable = schema.Table("entry");
        var entries = DataTables.Read(dataDirectory, entryTable).ToList();
        var classes = DataTables.Read(dataDirectory, schema.Table("coarse_class")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var senseEntry = DataTables.Read(dataDirectory, schema.Table("sense")).ToDictionary(r => r[0], r => r[2], StringComparer.Ordinal);
        var classEntries = DataTables.Read(dataDirectory, schema.Table("class_entry"))
            .GroupBy(r => r[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(r => (Entry: r[1], Features: r[2])).ToList(), StringComparer.Ordinal);
        var classSenseEntries = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var row in DataTables.Read(dataDirectory, schema.Table("class_sense")).Where(r => senseEntry.ContainsKey(r[1])))
        {
            var entry = senseEntry[row[1]];
            if (!classSenseEntries.TryGetValue(row[0], out var counts))
            {
                classSenseEntries[row[0]] = counts = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            counts[entry] = counts.GetValueOrDefault(entry) + 1;
        }

        var entryById = entries.ToDictionary(r => r[0], StringComparer.Ordinal);
        var byLemmaPos = new Dictionary<(string, string), string[]>();
        foreach (var row in entries.OrderBy(r => int.Parse(r[3], CultureInfo.InvariantCulture)).ThenBy(r => r[0], StringComparer.Ordinal).Where(r => !byLemmaPos.ContainsKey((r[1], r[2]))))
        {
            byLemmaPos[(row[1], row[2])] = row;
        }

        var labels = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses")).Rows
            .ToDictionary(r => (r.Lemma, r.Pos, r.Meaning), r => r.Label);

        var newEntries = new List<string[]>();
        string EntryFor(string lemma, string pos)
        {
            if (byLemmaPos.TryGetValue((lemma, pos), out var found))
            {
                return found[0];
            }

            var created = new[] { $"dema-{lemma}-{pos}", lemma, pos, "0", (lemma.Split(' ').Length).ToString(CultureInfo.InvariantCulture), "1", Source };
            byLemmaPos[(lemma, pos)] = created;
            entryById[created[0]] = created;
            newEntries.Add(created);
            return created[0];
        }

        var legacy = WordsBuilder.Build(corpus);
        var forms = new List<string[]>();
        var scopes = new List<string[]>();
        var analyses = 0;
        foreach (var (spelling, details) in legacy)
        {
            // Resolve every answer, then merge neighbours
            var resolved = new List<(string Entry, string Features, string? ClassId, bool Reviewed)>();
            foreach (var d in details)
            {
                analyses++;
                var pos = DataTables.PosLetter(d.PartOfSpeech);
                string? classId = null;
                if (labels.TryGetValue((d.Lemma, d.PartOfSpeech, d.Meaning), out var label))
                {
                    classId = $"{d.Lemma}#{label}";
                    if (!classes.ContainsKey(classId))
                    {
                        throw new InvalidDataException($"Class '{classId}' of word '{spelling}' is not in the coarse_class table.");
                    }
                }

                var fixTo = LemmaFixes.TryGetValue(spelling, out var fix) && fix.From == d.Lemma ? fix.To : null;
                string entryId;
                var features = string.Empty;
                if (fixTo is not null)
                {
                    entryId = EntryFor(fixTo, pos);
                }
                else if (classId is not null)
                {
                    (entryId, features) = ClassEntry(classId, pos, classes[classId], classEntries, classSenseEntries, entryById);
                }
                else
                {
                    entryId = EntryFor(d.Lemma, pos);
                }

                var explicitFeatures = features.Length > 0;
                if (!explicitFeatures)
                {
                    features = FeatureInference.Infer(spelling, entryById[entryId][1], pos);
                }

                resolved.Add((entryId, features, classId, classId is not null && fixTo is null));
            }

            var rank = 0;
            var i = 0;
            while (i < resolved.Count)
            {
                var first = resolved[i];
                var group = new List<string>();
                if (first.ClassId is not null)
                {
                    group.Add(first.ClassId);
                }

                var j = i + 1;
                while (first.ClassId is not null && j < resolved.Count && resolved[j].ClassId is { } next
                    && resolved[j].Entry == first.Entry && resolved[j].Features == first.Features
                    && DataLayout.CompareUtf8(group[^1], next) < 0)
                {
                    group.Add(next);
                    j++;
                }

                var reviewed = first.Reviewed ? "1" : "0";
                forms.Add([spelling, rank.ToString(CultureInfo.InvariantCulture), first.Entry, first.Features, Source, reviewed]);
                foreach (var cls in group)
                {
                    scopes.Add([spelling, rank.ToString(CultureInfo.InvariantCulture), cls]);
                }

                rank++;
                i = j;
            }
        }

        var resolver = new PartitionResolver(schema.FileSplit);
        entries.AddRange(newEntries);
        DataTables.Write(dataDirectory, entryTable, entries, resolver);
        DataTables.Write(dataDirectory, schema.Table("form"), forms, resolver);
        DataTables.Write(dataDirectory, schema.Table("form_sense_scope"), scopes, resolver);
        return new BootstrapResult(newEntries.Count, forms.Count, legacy.Count, scopes.Count, analyses);
    }

    private static (string Entry, string Features) ClassEntry(
        string classId,
        string pos,
        string[] cls,
        Dictionary<string, List<(string Entry, string Features)>> classEntries,
        Dictionary<string, Dictionary<string, int>> classSenseEntries,
        Dictionary<string, string[]> entryById)
    {
        var kind = cls[4];
        if (kind is "lemma_split" or "feature_only")
        {
            var candidates = classEntries[classId].Where(c => entryById[c.Entry][2] == pos).ToList();
            if (candidates.Count == 0)
            {
                throw new InvalidDataException($"Class '{classId}' has no linked entry of part of speech '{pos}'.");
            }

            // Prefer the entry whose lemma is named by the label (bases-base names base), then the smallest lemma
            var named = candidates.Where(c => cls[2].Split('-').Contains(DataLayout.AsciiLower(entryById[c.Entry][1]), StringComparer.Ordinal)).ToList();
            var pool = named.Count > 0 ? named : candidates;
            var best = pool.OrderBy(c => DataLayout.AsciiLower(entryById[c.Entry][1]), StringComparer.Ordinal).ThenBy(c => c.Entry, StringComparer.Ordinal).First();
            return (best.Entry, best.Features);
        }

        if (!classSenseEntries.TryGetValue(classId, out var counts) || counts.Count == 0)
        {
            throw new InvalidDataException($"Class '{classId}' has no sense.");
        }

        var entry = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
        return (entry, string.Empty);
    }
}
