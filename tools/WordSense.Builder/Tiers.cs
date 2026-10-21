using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The quota status of one sense.
/// </summary>
/// <param name="SenseId">The sense id.</param>
/// <param name="PartOfSpeech">The part of speech of the sense.</param>
/// <param name="Tier">The tier: <c>C</c>, <c>U</c> or <c>R</c>.</param>
/// <param name="TagCount">The WordNet 3.0 tag count of the sense (0 for a project sense).</param>
/// <param name="Anchors">The anchor sentences of the sense.</param>
/// <param name="AnchorQuota">The anchor sentences the tier asks for.</param>
/// <param name="Holdout">The held-out sentences of the sense.</param>
/// <param name="HoldoutQuota">The held-out sentences the tier asks for.</param>
internal sealed record TierRow(string SenseId, PartOfSpeech PartOfSpeech, string Tier, int TagCount, int Anchors, int AnchorQuota, int Holdout, int HoldoutQuota)
{
    /// <summary>
    ///     Gets the status of the sense: <c>met</c> when both quotas are reached, <c>empty</c> when it has no sentence,
    ///     otherwise <c>short</c>.
    /// </summary>
    public string Status
    {
        get
        {
            if (Anchors >= AnchorQuota && Holdout >= HoldoutQuota)
            {
                return "met";
            }

            return Anchors + Holdout == 0 ? "empty" : "short";
        }
    }
}

/// <summary>
///     Reports how many sentences every sense of the trained lemmas should have, and which senses fall short.
/// </summary>
/// <remarks>
///     <para>
///     The tiers size the data of a sense by its WordNet 3.0 tag count: tier <c>C</c> (five or more tags) needs 20
///     anchors and 5 held-out sentences, tier <c>U</c> (one to four tags, and a project sense, which has no key) 10 and
///     3, tier <c>R</c> (no tag) 5 and 2. A sense is <c>met</c> when it has both quotas, <c>short</c> when it has at
///     least one sentence but misses a quota and <c>empty</c> when it has none. The senses are those of the trained
///     lemmas, the lemmas that have sentences in the anchors.
///     </para>
/// </remarks>
internal static class Tiers
{
    /// <summary>
    ///     Gets the tier and the quotas of a sense.
    /// </summary>
    /// <param name="tagCount">The WordNet 3.0 tag count.</param>
    /// <param name="isProjectSense">Whether the sense is a project sense, which Open English WordNet does not have.</param>
    /// <returns>The tier and the anchor and held-out quota.</returns>
    public static (string Tier, int Anchors, int Holdout) Quota(int tagCount, bool isProjectSense)
    {
        if (isProjectSense || (tagCount >= 1 && tagCount < 5))
        {
            return ("U", 10, 3);
        }

        return tagCount >= 5 ? ("C", 20, 5) : ("R", 5, 2);
    }

    /// <summary>
    ///     Reads the tag counts of the senses from the sense-frequency table of the data folder.
    /// </summary>
    /// <param name="data">The data directory.</param>
    /// <returns>The tag count of every sense key.</returns>
    public static Dictionary<string, int> ReadTagCounts(string data)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var directory = Path.Join(data, "sense_frequency");
        if (!Directory.Exists(directory))
        {
            return counts;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file, new UTF8Encoding(false)).Skip(1))
            {
                var columns = line.Split('\t');
                if (columns.Length >= 2 && int.TryParse(columns[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                {
                    counts[columns[0]] = count;
                }
            }
        }

        return counts;
    }

    /// <summary>
    ///     Builds the quota status of every sense of the trained lemmas.
    /// </summary>
    /// <param name="anchors">The anchor rows.</param>
    /// <param name="holdout">The held-out rows.</param>
    /// <param name="tagCounts">The tag count of every sense key.</param>
    /// <returns>One row for every sense of every lemma of the anchors, in lemma, part-of-speech and inventory order.</returns>
    public static List<TierRow> Build(IReadOnlyList<CorpusRow> anchors, IReadOnlyList<CorpusRow> holdout, IReadOnlyDictionary<string, int> tagCounts)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(holdout);
        ArgumentNullException.ThrowIfNull(tagCounts);
        var anchorCount = anchors.GroupBy(r => r.SenseId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var holdoutCount = holdout.GroupBy(r => r.SenseId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var rows = new List<TierRow>();
        foreach (var lemma in anchors.Select(r => r.Lemma).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal))
        {
            foreach (var pos in Enum.GetValues<PartOfSpeech>())
            {
                foreach (var sense in WordLexicon.SensesOf(lemma, pos))
                {
                    var tags = sense.SenseKey is { } key ? tagCounts.GetValueOrDefault(key) : 0;
                    var (tier, anchorQuota, holdoutQuota) = Quota(tags, sense.SenseKey is null);
                    rows.Add(new TierRow(sense.SenseId, pos, tier, tags, anchorCount.GetValueOrDefault(sense.SenseId), anchorQuota, holdoutCount.GetValueOrDefault(sense.SenseId), holdoutQuota));
                }
            }
        }

        return rows;
    }

    /// <summary>
    ///     Renders the quota report: one tab-separated line for every sense.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The file text with line feeds.</returns>
    public static string RenderRows(IReadOnlyList<TierRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("sense_id\tpart_of_speech\ttier\ttag_count\tanchors\tanchor_quota\tholdout\tholdout_quota\tstatus\n");
        foreach (var r in rows)
        {
            text.Append(string.Join('\t', r.SenseId, r.PartOfSpeech, r.Tier, r.TagCount.ToString(inv), r.Anchors.ToString(inv), r.AnchorQuota.ToString(inv), r.Holdout.ToString(inv), r.HoldoutQuota.ToString(inv), r.Status)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the summary: the senses of each tier by status, the sentences that are missing, and the senses that fall
    ///     short with the reason.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="listed">The most senses to list that fall short.</param>
    /// <returns>The Markdown text with line feeds.</returns>
    public static string RenderSummary(IReadOnlyList<TierRow> rows, int listed = 25)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("| Tier | Senses | Met | Short | Empty |\n| --- | ---: | ---: | ---: | ---: |\n");
        foreach (var tier in new[] { "C", "U", "R" })
        {
            var of = rows.Where(r => r.Tier == tier).ToList();
            var (anchors, holdout) = tier switch { "C" => (20, 5), "U" => (10, 3), _ => (5, 2) };
            text.Append($"| {tier} ({anchors.ToString(inv)} + {holdout.ToString(inv)}) | {of.Count.ToString("N0", inv)} | {of.Count(r => r.Status == "met").ToString("N0", inv)} | {of.Count(r => r.Status == "short").ToString("N0", inv)} | {of.Count(r => r.Status == "empty").ToString("N0", inv)} |\n");
        }

        text.Append($"| All | {rows.Count.ToString("N0", inv)} | {rows.Count(r => r.Status == "met").ToString("N0", inv)} | {rows.Count(r => r.Status == "short").ToString("N0", inv)} | {rows.Count(r => r.Status == "empty").ToString("N0", inv)} |\n\n");
        text.Append($"Missing: {rows.Sum(r => Math.Max(0, r.AnchorQuota - r.Anchors)).ToString("N0", inv)} anchors and {rows.Sum(r => Math.Max(0, r.HoldoutQuota - r.Holdout)).ToString("N0", inv)} held-out sentences.\n\n");
        var short_ = rows.Where(r => r.Status != "met").OrderByDescending(r => r.TagCount).ThenBy(r => r.SenseId, StringComparer.Ordinal).Take(listed).ToList();
        if (short_.Count > 0)
        {
            text.Append("Senses that fall short (most frequent first):\n\n");
            foreach (var r in short_)
            {
                text.Append($"- {r.SenseId} (tier {r.Tier}, {r.TagCount.ToString(inv)} tags): {r.Status}, {r.Anchors.ToString(inv)} of {r.AnchorQuota.ToString(inv)} anchors, {r.Holdout.ToString(inv)} of {r.HoldoutQuota.ToString(inv)} held-out\n");
            }
        }

        return text.ToString();
    }
}
