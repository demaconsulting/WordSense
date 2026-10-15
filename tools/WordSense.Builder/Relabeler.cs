using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     Applies the part-of-speech relabel to the corpus, driven by the audit results.
/// </summary>
/// <remarks>
///     <para>
///     Rules, per (lemma, sense), over the rows the audit could decide (undecided rows are always dropped):
///     a sense whose single part-of-speech tag matches the dominant audited part of speech is kept and its
///     noise rows (at most 2 rows or 5 %) are dropped; a sense whose tag is wrong but whose rows are uniform
///     is retagged (a part-of-speech label is renamed to the true part of speech, a descriptive label is
///     split into <c>label-pos</c>); a mixed sense is split by part of speech. A part is kept only with at least
///     <see cref="MinAnchors"/> anchor rows and <see cref="MinHoldout"/> holdout rows when it is new, else its
///     rows are dropped, and a mixed sense with no surviving part is excluded. A sense left with fewer than
///     <see cref="FloorAnchors"/> anchors, or a lemma left with fewer than two senses, is excluded.
///     </para>
///     <para>
///     The run writes <c>corpus/audit/relabel-decisions.tsv</c> and <c>corpus/audit/dropped-rows.tsv</c> and
///     refuses to run twice.
///     </para>
/// </remarks>
internal static class Relabeler
{
    /// <summary>
    ///     The fewest anchors a newly created sense needs.
    /// </summary>
    internal const int MinAnchors = 14;

    /// <summary>
    ///     The fewest holdout rows a newly created sense needs.
    /// </summary>
    internal const int MinHoldout = 8;

    /// <summary>
    ///     The fewest anchors any sense keeps.
    /// </summary>
    internal const int FloorAnchors = 4;

    private const string DroppedMarker = "-";

    /// <summary>
    ///     Applies the relabel.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>A summary of the changes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the relabel already ran or the corpus disagrees with the audit.</exception>
    public static string Apply(string corpus)
    {
        var auditDir = Path.Combine(corpus, "audit");
        var decisionsPath = Path.Combine(auditDir, "relabel-decisions.tsv");
        if (File.Exists(decisionsPath))
        {
            throw new InvalidDataException("The relabel was already applied (corpus/audit/relabel-decisions.tsv exists).");
        }

        var audit = PosAudit.ReadResults(Path.Combine(auditDir, "pos-audit.tsv"));
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses")).Rows.ToDictionary(r => (r.Lemma, r.Sense));
        var plan = Plan(audit, inventory);

        // Sense-level floors: a lemma needs two senses with enough anchors
        EnforceFloors(plan, audit);

        var text = new StringBuilder();
        RewriteSenses(corpus, plan, inventory);
        var dropped = RewriteCorpus(corpus, plan, audit);
        RewriteForms(corpus, plan);
        WriteDecisions(decisionsPath, plan, audit);
        WriteDropped(Path.Combine(auditDir, "dropped-rows.tsv"), dropped);

        var changed = plan.RowTarget.Count(p => p.Value.NewSense != DroppedMarker && p.Value.NewSense != p.Value.OldSense);
        text.Append(CultureInfo.InvariantCulture, $"Relabeled {changed} rows, dropped {dropped.Count} rows, {plan.Excluded.Count} senses excluded.");
        return text.ToString();
    }

    private static RelabelPlan Plan(List<PosAudit.AuditLine> audit, Dictionary<(string Lemma, string Sense), InventoryRow> inventory)
    {
        var plan = new RelabelPlan();
        foreach (var g in audit.GroupBy(l => (l.Lemma, l.Sense))
            .OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.Sense, StringComparer.Ordinal))
        {
            var (lemma, sense) = g.Key;
            var inv = inventory[(lemma, sense)];
            foreach (var line in g.Where(l => l.FinalPos == PosAudit.Undecided))
            {
                plan.Drop(line, "undecided: the judges could not settle the part of speech");
            }

            var decided = g.Where(l => l.FinalPos != PosAudit.Undecided).ToList();
            if (decided.Count == 0)
            {
                plan.Exclude(lemma, sense, "every row is undecided or not a usage of the lemma");
                continue;
            }

            PlanSense(plan, inventory, inv, decided);
        }

        return plan;
    }

    private static void PlanSense(
        RelabelPlan plan,
        Dictionary<(string Lemma, string Sense), InventoryRow> inventory,
        InventoryRow inv,
        List<PosAudit.AuditLine> decided)
    {
        var lemma = inv.Lemma;
        var sense = inv.Sense;
        var byPos = decided.GroupBy(l => l.FinalPos).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        var dominant = byPos[0].Key;
        var minority = decided.Where(l => l.FinalPos != dominant).ToList();
        var noise = Math.Max(2, decided.Count / 20);
        var posTags = inv.Tags.Where(t => PosRules.PartsOfSpeech.Contains(t)).ToList();
        var tagPos = posTags.Count == 1 ? posTags[0] : null;
        var isPosLabel = PosRules.PartsOfSpeech.Contains(sense);
        var qualifiers = inv.Tags.Where(t => !PosRules.PartsOfSpeech.Contains(t)).ToList();
        var group = inv.Group ?? (isPosLabel ? null : sense);

        if (tagPos == dominant || minority.Count <= noise)
        {
            // Keep the sense, or retag it when the tag is wrong but the rows are uniform
            var target = sense;
            if (tagPos != dominant)
            {
                target = isPosLabel ? dominant : sense + "-" + dominant;
                plan.Define(lemma, target, [.. qualifiers.Append(dominant).Order(StringComparer.Ordinal)], group, inv.Source, inv.Sense);
            }
            else
            {
                plan.Define(lemma, sense, inv.Tags, inv.Group, inv.Source, sense);
            }

            foreach (var line in byPos[0])
            {
                plan.Map(line, sense, target, tagPos != dominant ? "retag" : "keep", "the audited part of speech is " + dominant);
            }

            if (minority.Count > noise)
            {
                SplitParts(plan, inventory, inv, minority, qualifiers, group, isPosLabel, keepDominantId: true);
            }
            else
            {
                foreach (var line in minority)
                {
                    plan.Drop(line, $"noise: {line.FinalPos} among {decided.Count} {dominant} rows");
                }
            }

            return;
        }

        // Mixed sense whose tag disagrees with the audit: split every part
        SplitParts(plan, inventory, inv, decided, qualifiers, group, isPosLabel, keepDominantId: false);
    }

    private static void SplitParts(
        RelabelPlan plan,
        Dictionary<(string Lemma, string Sense), InventoryRow> inventory,
        InventoryRow inv,
        List<PosAudit.AuditLine> rows,
        List<string> qualifiers,
        string? group,
        bool isPosLabel,
        bool keepDominantId)
    {
        var survivors = 0;
        foreach (var part in rows.GroupBy(l => l.FinalPos).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var anchors = part.Count(l => l.Split == "anchors");
            var holdout = part.Count(l => l.Split == "holdout");
            var id = isPosLabel ? part.Key : inv.Sense + "-" + part.Key;
            var exists = inventory.ContainsKey((inv.Lemma, id)) || plan.HasSense(inv.Lemma, id);
            if (anchors >= MinAnchors && holdout >= MinHoldout && !exists)
            {
                var tags = qualifiers.Append(part.Key).Order(StringComparer.Ordinal).ToArray();
                plan.Define(inv.Lemma, id, tags, group, inv.Source, inv.Sense);
                foreach (var line in part)
                {
                    plan.Map(line, inv.Sense, id, "split", $"split by audited part of speech {part.Key}");
                }

                survivors++;
            }
            else
            {
                var why = exists
                    ? $"part of speech {part.Key} would merge into an existing sense of another group (folding is not proven)"
                    : $"part of speech {part.Key} has {anchors} anchors and {holdout} holdout rows, below the {MinAnchors}/{MinHoldout} minimum";
                foreach (var line in part)
                {
                    plan.Drop(line, why);
                }
            }
        }

        if (survivors == 0 && !keepDominantId)
        {
            plan.Exclude(inv.Lemma, inv.Sense, "mixed part of speech and no part reaches the minimum number of rows");
        }
    }

    private static void EnforceFloors(RelabelPlan plan, List<PosAudit.AuditLine> audit)
    {
        var split = audit.ToDictionary(l => l.Id, l => l.Split, StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var sense in plan.Senses.Keys.ToList())
            {
                var anchors = plan.RowTarget.Count(r => r.Value.Lemma == sense.Lemma && r.Value.NewSense == sense.Sense && split[r.Key] == "anchors");
                if (anchors < FloorAnchors)
                {
                    plan.RemoveSense(sense.Lemma, sense.Sense, $"fewer than {FloorAnchors} anchors remain");
                    changed = true;
                }
            }

            var small = plan.Senses.Keys.GroupBy(k => k.Lemma, StringComparer.Ordinal).Where(g => g.Count() < 2).SelectMany(g => g).ToList();
            foreach (var key in small)
            {
                plan.RemoveSense(key.Lemma, key.Sense, "the lemma is left with fewer than two senses");
                changed = true;
            }
        }
        while (changed);
    }

    private static void RewriteSenses(string corpus, RelabelPlan plan, Dictionary<(string Lemma, string Sense), InventoryRow> inventory)
    {
        var bySource = plan.Senses.Values.GroupBy(s => s.Source).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var source in inventory.Values.Select(r => r.Source).Distinct(StringComparer.Ordinal))
        {
            var rows = bySource.GetValueOrDefault(source) ?? [];
            SenseInventory.WriteFile(
                Path.Combine(corpus, "senses", source),
                rows.OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sense, StringComparer.Ordinal));
        }
    }

    private static List<(PosAudit.AuditLine Line, string Reason, string Sentence)> RewriteCorpus(string corpus, RelabelPlan plan, List<PosAudit.AuditLine> audit)
    {
        var dropped = new List<(PosAudit.AuditLine, string, string)>();
        var byId = audit.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var encoding = new UTF8Encoding(false);
        foreach (var kind in new[] { "anchors", "holdout" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(corpus, kind), "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                var output = new StringBuilder();
                foreach (var line in File.ReadAllLines(file, encoding))
                {
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    var cols = trimmed.Split('\t', 3);
                    var id = PosAudit.RowId(cols[0], cols[2]);
                    if (!byId.TryGetValue(id, out var audited))
                    {
                        throw new InvalidDataException($"{kind}/{Path.GetFileName(file)}: a row of '{cols[0]}' is missing from the audit.");
                    }

                    var target = plan.RowTarget[id].NewSense;
                    if (target == DroppedMarker)
                    {
                        dropped.Add((audited, plan.RowTarget[id].Reason, cols[2]));
                        continue;
                    }

                    output.Append(cols[0]).Append('\t').Append(target).Append('\t').Append(cols[2]).Append('\n');
                }

                File.WriteAllText(file, output.ToString(), encoding);
            }
        }

        return dropped;
    }

    private static void RewriteForms(string corpus, RelabelPlan plan)
    {
        var path = Path.Combine(corpus, "forms", "speech.tsv");
        var encoding = new UTF8Encoding(false);
        var output = new StringBuilder("form\tlemma\tlabels\n");
        foreach (var line in File.ReadAllLines(path, encoding).Skip(1))
        {
            var cols = line.TrimEnd('\r').Split('\t');
            if (cols.Length != 3)
            {
                continue;
            }

            var labels = cols[2].Split(',')
                .SelectMany(l => plan.SenseMap.TryGetValue((cols[1], l), out var targets) ? targets : [])
                .Where(t => plan.Senses.ContainsKey((cols[1], t)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (labels.Count > 0)
            {
                output.Append(cols[0]).Append('\t').Append(cols[1]).Append('\t').Append(string.Join(',', labels)).Append('\n');
            }
        }

        File.WriteAllText(path, output.ToString(), encoding);
    }

    private static void WriteDecisions(string path, RelabelPlan plan, List<PosAudit.AuditLine> audit)
    {
        var split = audit.ToDictionary(l => l.Id, l => l.Split, StringComparer.Ordinal);
        var output = new StringBuilder("lemma\told_sense\tnew_sense\tgroup\taction\trows\tanchors\tholdout\treason\n");
        var groups = plan.RowTarget.Values
            .Where(v => v.NewSense != v.OldSense || v.Action != "keep")
            .GroupBy(v => (v.Lemma, v.OldSense, v.NewSense, v.Action, v.Reason))
            .OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.OldSense, StringComparer.Ordinal)
            .ThenBy(g => g.Key.NewSense, StringComparer.Ordinal).ThenBy(g => g.Key.Reason, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var (lemma, old, target, action, reason) = g.Key;
            var group = plan.Senses.TryGetValue((lemma, target), out var s) ? s.Group ?? string.Empty : string.Empty;
            var anchors = g.Count(v => split[v.Id] == "anchors");
            var holdout = g.Count(v => split[v.Id] == "holdout");
            output.Append(string.Join('\t', lemma, old, target, group, target == DroppedMarker ? "drop" : action, g.Count().ToString(CultureInfo.InvariantCulture), anchors.ToString(CultureInfo.InvariantCulture), holdout.ToString(CultureInfo.InvariantCulture), reason)).Append('\n');
        }

        foreach (var (lemma, old, reason) in plan.Excluded.OrderBy(e => e.Lemma, StringComparer.Ordinal).ThenBy(e => e.Sense, StringComparer.Ordinal))
        {
            output.Append(string.Join('\t', lemma, old, DroppedMarker, string.Empty, "exclude", "0", "0", "0", reason)).Append('\n');
        }

        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteDropped(string path, List<(PosAudit.AuditLine Line, string Reason, string Sentence)> dropped)
    {
        var output = new StringBuilder("id\tlemma\told_sense\tsplit\treason\tsentence\n");
        foreach (var (line, reason, sentence) in dropped.OrderBy(d => d.Line.Lemma, StringComparer.Ordinal).ThenBy(d => d.Line.Sense, StringComparer.Ordinal).ThenBy(d => d.Line.Id, StringComparer.Ordinal))
        {
            output.Append(string.Join('\t', line.Id, line.Lemma, line.Sense, line.Split, reason, sentence)).Append('\n');
        }

        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private sealed record Target(string Id, string Lemma, string OldSense, string NewSense, string Action, string Reason);

    private sealed class RelabelPlan
    {
        public Dictionary<(string Lemma, string Sense), InventoryRow> Senses { get; } = [];

        public Dictionary<string, Target> RowTarget { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Lemma, string Sense), List<string>> SenseMap { get; } = [];

        public List<(string Lemma, string Sense, string Reason)> Excluded { get; } = [];

        public bool HasSense(string lemma, string sense) => Senses.ContainsKey((lemma, sense));

        public void Define(string lemma, string sense, IReadOnlyList<string> tags, string? group, string source, string oldSense)
        {
            if (!Senses.ContainsKey((lemma, sense)))
            {
                Senses[(lemma, sense)] = new InventoryRow(lemma, sense, tags, source, group);
            }

            if (!SenseMap.TryGetValue((lemma, oldSense), out var list))
            {
                SenseMap[(lemma, oldSense)] = list = [];
            }

            if (!list.Contains(sense))
            {
                list.Add(sense);
            }
        }

        public void Map(PosAudit.AuditLine line, string oldSense, string target, string action, string reason)
        {
            RowTarget[line.Id] = new Target(line.Id, line.Lemma, oldSense, target, action, reason);
        }

        public void Drop(PosAudit.AuditLine line, string reason)
        {
            RowTarget[line.Id] = new Target(line.Id, line.Lemma, line.Sense, DroppedMarker, "drop", reason);
        }

        public void Exclude(string lemma, string sense, string reason)
        {
            Excluded.Add((lemma, sense, reason));
            Senses.Remove((lemma, sense));
        }

        public void RemoveSense(string lemma, string sense, string reason)
        {
            Senses.Remove((lemma, sense));
            foreach (var key in RowTarget.Where(r => r.Value.Lemma == lemma && r.Value.NewSense == sense).Select(r => r.Key).ToList())
            {
                var old = RowTarget[key];
                RowTarget[key] = old with { NewSense = DroppedMarker, Action = "drop", Reason = reason };
            }

            Excluded.Add((lemma, sense, reason));
        }
    }
}
