using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the record of the suite answers and the row-by-row diff against a baseline.
/// </summary>
public sealed class SuiteDiffTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static SuiteRow Row(string id, params string[] senses) =>
        new(id, "M", "record", "record", PartOfSpeech.Noun, [], senses, ["the", "record", "stands"], 1, string.Empty);

    private static ClassifyResult Result(PartOfSpeech pos, SenseLevel level, string? senseId = null, double confidence = 0.9)
    {
        var details = new WordDetails("record", pos, string.Empty);
        return new ClassifyResult("record", true, [new AnalysisCandidate(details, 1.0, ImmutableArray<SenseCandidate>.Empty)], details, level, confidence, senseId, null);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SuiteAnswer>> AllModels(params SuiteAnswer[] answers) =>
        SuiteFile.Models.ToDictionary(m => m, _ => (IReadOnlyList<SuiteAnswer>)answers, StringComparer.Ordinal);

    /// <summary>
    ///     The answer of a row has its grade, level, analysis and sense, and reads as one line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SuiteDiff_AnswerOf_DescribesTheAnswer()
    {
        var right = SuiteDiff.AnswerOf(Row("M-1"), Result(PartOfSpeech.Noun, SenseLevel.PartOfSpeech));
        var wrong = SuiteDiff.AnswerOf(Row("M-1"), Result(PartOfSpeech.Verb, SenseLevel.PartOfSpeech, confidence: 0.5));
        var none = SuiteDiff.AnswerOf(Row("M-1"), ClassifyResult.Unknown("record"));

        Assert.Equal((SuiteGrade.Fine, "PartOfSpeech", "record/Noun", string.Empty), (right.Grade, right.Level, right.Analysis, right.Target));
        Assert.Equal("PartOfSpeech record/Noun", right.Text);
        Assert.Equal(SuiteGrade.Miss, wrong.Grade);
        Assert.Equal(0.5, wrong.Confidence);
        Assert.Equal("None -", none.Text);
    }

    /// <summary>
    ///     Answers written to an outcome file are read back, and a malformed file is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SuiteDiff_RenderAndReadOutcomes_RoundTrip()
    {
        // Arrange
        SuiteRow[] rows = [Row("M-1"), Row("M-2")];
        var answers = AllModels(new SuiteAnswer(SuiteGrade.Fine, "Sense", "record/Noun", "record#x", 0.97), new SuiteAnswer(SuiteGrade.Miss, "None", string.Empty, string.Empty, 0));
        var path = _dir.Write("suite.tsv", SuiteDiff.RenderOutcomes(rows, answers));

        // Act
        var read = SuiteDiff.ReadOutcomes(path);

        // Assert
        Assert.Equal(6, read.Count);
        Assert.Equal(new SuiteAnswer(SuiteGrade.Fine, "Sense", "record/Noun", "record#x", 0.97), read[("minilm", "M-1")]);
        Assert.Equal(SuiteGrade.Miss, read[("basic", "M-2")].Grade);
        Assert.Throws<InvalidDataException>(() => SuiteDiff.ReadOutcomes(_dir.Write("bad-header.tsv", "id\n")));
        Assert.Throws<InvalidDataException>(() => SuiteDiff.ReadOutcomes(_dir.Write("bad-grade.tsv", "model\tid\tgrade\tlevel\tanalysis\ttarget\tconfidence\nbasic\tM-1\tgood\tSense\ta\tb\t0.5\n")));
        Assert.Throws<InvalidDataException>(() => SuiteDiff.ReadOutcomes(_dir.Write("twice.tsv", "model\tid\tgrade\tlevel\tanalysis\ttarget\tconfidence\nbasic\tM-1\tfine\tSense\ta\tb\t0.5\nbasic\tM-1\tfine\tSense\ta\tb\t0.5\n")));
    }

    /// <summary>
    ///     Every row whose grade or answer changed is listed in either direction with its verdict, the gold and the expectation;
    ///     rows that did not change are not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SuiteDiff_Diff_ListsEveryChangeInBothDirections()
    {
        // Arrange: the baseline has M-1 right, M-2 wrong, M-3 right; now M-1 is wrong, M-2 right, M-3 right with another answer
        SuiteRow[] rows = [Row("M-1"), Row("M-2"), Row("M-3")];
        var expectations = SuiteFile.Models.ToDictionary(m => m, _ => (IReadOnlyDictionary<string, SuiteGrade>)rows.ToDictionary(r => r.Id, _ => SuiteGrade.Fine, StringComparer.Ordinal), StringComparer.Ordinal);
        var fine = new SuiteAnswer(SuiteGrade.Fine, "PartOfSpeech", "record/Noun", string.Empty, 0.9);
        var miss = new SuiteAnswer(SuiteGrade.Miss, "PartOfSpeech", "record/Verb", string.Empty, 0.8);
        var other = new SuiteAnswer(SuiteGrade.Fine, "PartOfSpeech", "record/Noun", string.Empty, 0.6) with { Level = "Group", Target = "g1" };
        var baseline = SuiteDiff.ReadOutcomes(_dir.Write("base.tsv", SuiteDiff.RenderOutcomes(rows, AllModels(fine, miss, fine))));

        // Act
        var changes = SuiteDiff.Diff(rows, expectations, baseline, AllModels(miss, fine, other));
        var text = SuiteDiff.RenderDiff(changes, rows.Length);

        // Assert
        Assert.Equal(9, changes.Count);
        Assert.Equal(["WORSE", "better", "same grade, other answer"], changes.Take(3).Select(c => c.Verdict));
        Assert.Contains("9 of 9 answers changed: 3 better, 3 worse, 3 with the same grade and another answer.", text, StringComparison.Ordinal);
        Assert.Contains("- **M-1** (basic): WORSE", text, StringComparison.Ordinal);
        Assert.Contains("  - sentence: the {record} stands", text, StringComparison.Ordinal);
        Assert.Contains("  - old: PartOfSpeech record/Noun (fine, 0.900)", text, StringComparison.Ordinal);
        Assert.Contains("  - new: PartOfSpeech record/Verb (miss, 0.800)", text, StringComparison.Ordinal);
        Assert.Contains("  - gold: record/Noun", text, StringComparison.Ordinal);
        Assert.Contains("  - expectation: fine (the new grade is BELOW it)", text, StringComparison.Ordinal);
        Assert.Empty(SuiteDiff.Diff(rows, expectations, baseline, AllModels(fine, miss, fine)));
    }

    /// <summary>
    ///     A baseline that lacks a row is refused, and the gold of a row with senses lists them.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SuiteDiff_Diff_MissingBaselineRow_Throws()
    {
        SuiteRow[] rows = [Row("M-1", "record#a", "record#b")];
        var expectations = SuiteFile.Models.ToDictionary(m => m, _ => (IReadOnlyDictionary<string, SuiteGrade>)new Dictionary<string, SuiteGrade> { ["M-1"] = SuiteGrade.Answer }, StringComparer.Ordinal);
        var empty = new Dictionary<(string Model, string Id), SuiteAnswer>();
        var answer = new SuiteAnswer(SuiteGrade.Miss, "None", string.Empty, string.Empty, 0);

        Assert.Throws<InvalidDataException>(() => SuiteDiff.Diff(rows, expectations, empty, AllModels(answer)));
        var changes = SuiteDiff.Diff(rows, expectations, SuiteFile.Models.ToDictionary(m => (m, "M-1"), _ => answer with { Level = "Sense" }), AllModels(answer));
        Assert.Contains("  - gold: record/Noun record#a | record#b", SuiteDiff.RenderDiff(changes, 1), StringComparison.Ordinal);
    }
}
