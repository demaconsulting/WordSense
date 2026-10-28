using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the quota report: the tier of a tag count, the status of each sense and the report command.
/// </summary>
public sealed class TiersTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string Line(SenseInfo sense, int number) =>
        $"{sense.Lemma}\t{sense.SenseId}\t{sense.PartOfSpeech}\tThe {{{sense.Lemma}}} {number.ToString(CultureInfo.InvariantCulture)} here.";

    /// <summary>
    ///     Five or more tags make tier C (20 anchors and 5 held-out), one to four tier U (10 and 3), none tier R (5 and 2, or 3
    ///     and 1 for a lemma with 15 or more senses), and a project sense, which has no key, tier U.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Tiers_Quota_FollowsTheTagCount()
    {
        Assert.Equal(("C", 20, 5), Tiers.Quota(5, false, 3));
        Assert.Equal(("C", 20, 5), Tiers.Quota(400, false, 70));
        Assert.Equal(("U", 10, 3), Tiers.Quota(4, false, 70));
        Assert.Equal(("U", 10, 3), Tiers.Quota(1, false, 3));
        Assert.Equal(("R", 5, 2), Tiers.Quota(0, false, 3));
        Assert.Equal(("R", 5, 2), Tiers.Quota(0, false, 14));
        Assert.Equal(("R", 3, 1), Tiers.Quota(0, false, 15));
        Assert.Equal(("R", 3, 1), Tiers.Quota(0, false, 70));
        Assert.Equal(("U", 10, 3), Tiers.Quota(0, true, 3));
        Assert.Equal(("U", 10, 3), Tiers.Quota(0, true, 70));
    }

    /// <summary>
    ///     The quota of a sense without a tag follows the size of its lemma: 3 and 1 for "cut" (70 senses), 5 and 2 for "record"
    ///     (12 senses).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Tiers_Build_LargeLemma_AsksFewerSentencesForUntaggedSenses()
    {
        // Arrange: one anchor for a sense of each lemma, no tag counts
        var cut = WordLexicon.SensesOf("cut", PartOfSpeech.Verb);
        var record = WordLexicon.SensesOf("record", PartOfSpeech.Noun);
        Assert.True(Enum.GetValues<PartOfSpeech>().Sum(p => WordLexicon.SensesOf("cut", p).Length) >= Tiers.LargeLemmaSenses);
        Assert.True(Enum.GetValues<PartOfSpeech>().Sum(p => WordLexicon.SensesOf("record", p).Length) < Tiers.LargeLemmaSenses);
        var anchors = new List<CorpusRow> { SenseFixtures.Row(cut[0], 1), SenseFixtures.Row(record[0], 2) };

        // Act
        var rows = Tiers.Build(anchors, [], new Dictionary<string, int>());

        // Assert
        var cutRow = rows.First(r => r.SenseId == cut[0].SenseId);
        var recordRow = rows.First(r => r.SenseId == record[0].SenseId);
        Assert.Equal(("R", 3, 1), (cutRow.Tier, cutRow.AnchorQuota, cutRow.HoldoutQuota));
        Assert.Equal(("R", 5, 2), (recordRow.Tier, recordRow.AnchorQuota, recordRow.HoldoutQuota));
    }

    /// <summary>
    ///     Every sense of a trained lemma is reported with its tier and status (met, short or empty), and the summary lists the
    ///     senses that fall short with the sentences that are missing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Tiers_Report_ListsShortAndEmptySensesWithTheMissingSentences()
    {
        // Arrange: the first sense of "record" (noun) has the tags to be tier C and nothing else is tagged; sense 2 has one anchor
        var senses = WordLexicon.SensesOf("record", PartOfSpeech.Noun);
        Assert.True(senses.Length >= 3);
        var tags = new Dictionary<string, int> { [senses[0].SenseKey!] = 12 };
        var anchors = Enumerable.Range(0, 20).Select(i => SenseFixtures.Row(senses[0], i)).Append(SenseFixtures.Row(senses[1], 100)).ToList();
        var holdout = Enumerable.Range(0, 5).Select(i => SenseFixtures.Row(senses[0], 200 + i)).ToList();

        // Act
        var rows = Tiers.Build(anchors, holdout, tags);
        var summary = Tiers.RenderSummary(rows);
        var report = Tiers.RenderRows(rows);

        // Assert
        var first = rows.First(r => r.SenseId == senses[0].SenseId);
        Assert.Equal(("C", 12, "met"), (first.Tier, first.TagCount, first.Status));
        var second = rows.First(r => r.SenseId == senses[1].SenseId);
        Assert.Equal(("R", "short"), (second.Tier, second.Status));
        var third = rows.First(r => r.SenseId == senses[2].SenseId);
        Assert.Equal("empty", third.Status);
        Assert.Contains(senses[2].SenseId, summary, StringComparison.Ordinal);
        Assert.Contains("Missing:", summary, StringComparison.Ordinal);
        Assert.StartsWith("sense_id\tpart_of_speech\ttier\ttag_count\tanchors\tanchor_quota\tholdout\tholdout_quota\tstatus\n", report, StringComparison.Ordinal);
        Assert.Contains($"{senses[0].SenseId}\tNoun\tC\t12\t20\t20\t5\t5\tmet\n", report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command prints the summary and, with <c>--output</c>, writes one line for every sense of the trained lemmas.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_Tiers_WritesReport()
    {
        // Arrange: one anchor of one sense of "record"
        var senses = WordLexicon.SensesOf("record", PartOfSpeech.Noun);
        _dir.Write("corpus/anchors/a.tsv", Line(senses[0], 1) + "\n");
        _dir.Write("data/sense_frequency/a.tsv", $"sense_key\ttag_count\tsource\n{senses[0].SenseKey}\t7\twn30\n");
        var report = Path.Join(_dir.Root, "quota.tsv");
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["tiers", "--repository", _dir.Root, "--output", report], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("| Tier | Senses | Met | Short | Empty |", output.ToString(), StringComparison.Ordinal);
        var lines = await File.ReadAllLinesAsync(report, TestContext.Current.CancellationToken);
        Assert.Equal(Enum.GetValues<PartOfSpeech>().Sum(p => WordLexicon.SensesOf("record", p).Length) + 1, lines.Length);
        Assert.Contains(lines, l => l.StartsWith($"{senses[0].SenseId}\tNoun\tC\t7\t1\t20\t0\t5\tshort", StringComparison.Ordinal));
    }
}
