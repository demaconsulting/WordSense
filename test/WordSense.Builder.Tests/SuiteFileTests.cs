using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reviewed suite: the reader, the expectation file and the grading rule.
/// </summary>
public sealed class SuiteFileTests : IDisposable
{
    private const string SuiteHeader = "id\tlist\tlemma\tanalysis\tsenses\ttarget_index\tsentence\tnote\n";
    private const string ExpectationsHeader = "id\tbasic\tminilm\tbert-base-cased\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string SenseRow(string id, SenseInfo[] senses, string sentence = "I {record} it .", int index = 1) =>
        string.Join('\t', id, "H", senses[0].Lemma, $"{senses[0].Lemma}/{senses[0].PartOfSpeech}", string.Join('|', senses.Select(s => s.SenseId)), index, sentence, "note");

    private static ClassifyResult Result(SenseLevel level, WordDetails? details, string? groupId = null, string? senseId = null, params (string Id, double P)[] ranked) =>
        new("w", details is not null, details is null ? [] : [new AnalysisCandidate(details, 1.0, [.. ranked.Select(r => new SenseCandidate(r.Id, r.P))])], details, level, 0.9, groupId, senseId);

    /// <summary>
    ///     The reader parses the rows (analysis with features, acceptable senses, the braced target) and the expectation of
    ///     every model for every row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Read_ParsesRowsAndExpectations()
    {
        // Arrange
        var senses = SenseFixtures.Senses("record", PartOfSpeech.Noun, 2);
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + SenseRow("H-1", senses) + "\n" + "M-1\tM\tread\tread/Verb/Tense=Past+VerbForm=Part\t\t0\t{read} it .\t\n");
        _dir.Write(SuiteFile.ExpectationsName, ExpectationsHeader + "H-1\tfine\tmiss\tanswer\nM-1\tmiss\tanswer\tfine\n");

        // Act
        var suite = SuiteFile.Read(_dir.Root);

        // Assert
        Assert.Equal(["H-1", "M-1"], suite.Rows.Select(r => r.Id));
        var first = suite.Rows[0];
        Assert.Equal(("record", PartOfSpeech.Noun, 1), (first.AnalysisLemma, first.PartOfSpeech, first.TargetIndex));
        Assert.Equal(senses.Select(s => s.SenseId), first.SenseIds);
        Assert.Equal(["I", "record", "it", "."], first.Words);
        var second = suite.Rows[1];
        Assert.Equal(["Tense=Past", "VerbForm=Part"], second.Features);
        Assert.Empty(second.SenseIds);
        Assert.Equal(SuiteGrade.Fine, suite.Expectations["basic"]["H-1"]);
        Assert.Equal(SuiteGrade.Miss, suite.Expectations["minilm"]["H-1"]);
        Assert.Equal(SuiteGrade.Fine, suite.Expectations["bert-base-cased"]["M-1"]);
    }

    /// <summary>
    ///     A row that has no expectation, a value that is no expectation and a header without a model are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Read_RowWithoutModelExpectation_IsRejected()
    {
        // Arrange
        var senses = SenseFixtures.Senses("record", PartOfSpeech.Noun, 1);
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + SenseRow("H-1", senses) + "\n" + SenseRow("H-2", senses) + "\n");

        // Act and assert
        _dir.Write(SuiteFile.ExpectationsName, ExpectationsHeader + "H-1\tfine\tfine\tfine\n");
        Assert.Contains("'H-2' has no expectation", Assert.Throws<InvalidDataException>(() => SuiteFile.Read(_dir.Root)).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.ExpectationsName, ExpectationsHeader + "H-1\tfine\tfine\tfine\nH-2\tfine\tpass\tfine\n");
        Assert.Contains("'pass' is not an expectation", Assert.Throws<InvalidDataException>(() => SuiteFile.Read(_dir.Root)).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.ExpectationsName, "id\tbasic\tminilm\n" + "H-1\tfine\tfine\nH-2\tfine\tfine\n");
        Assert.Contains("header", Assert.Throws<InvalidDataException>(() => SuiteFile.Read(_dir.Root)).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.ExpectationsName, ExpectationsHeader + "H-1\tfine\tfine\tfine\nH-2\tfine\tfine\tfine\nH-3\tfine\tfine\tfine\n");
        Assert.Contains("'H-3' is not in the suite", Assert.Throws<InvalidDataException>(() => SuiteFile.Read(_dir.Root)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense that is not in the catalog, or that belongs to another lemma or part of speech than the analysis, is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Read_UnknownSenseOrSenseOfAnotherLemma_IsRejected()
    {
        // Arrange
        var noun = SenseFixtures.Senses("record", PartOfSpeech.Noun, 1)[0];
        var verb = SenseFixtures.Senses("record", PartOfSpeech.Verb, 1)[0];
        var other = SenseFixtures.Senses("work", PartOfSpeech.Noun, 1)[0];

        // Act and assert
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + "H-1\tH\trecord\trecord/Noun\trecord#no-such-sense\t1\tI {record} it .\t\n");
        Assert.Contains("not in the sense catalog", Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName))).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + $"H-1\tH\trecord\trecord/Noun\t{verb.SenseId}\t1\tI {{record}} it .\t\n");
        Assert.Contains("not a sense of record (Noun)", Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName))).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + $"H-1\tH\trecord\trecord/Noun\t{other.SenseId}\t1\tI {{record}} it .\t\n");
        Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName)));
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + $"H-1\tH\trecord\trecord/Noun\t{noun.SenseId}\t1\tI {{record}} it .\t\n");
        Assert.Single(SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName)));
    }

    /// <summary>
    ///     A malformed row (columns, an analysis that is not lemma/Part, a target that is not at its index or
    ///     is not single) is rejected with the line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Read_MalformedRow_IsRejected()
    {
        var cases = new (string Row, string Expected)[]
        {
            ("H-1\tH\trecord\trecord/Noun\t\t1\tI {record} it .", "expected 8"),
            ("H-1\tH\trecord\trecord\t\t1\tI {record} it .\t", "analysis"),
            ("H-1\tH\trecord\trecord/Thing\t\t1\tI {record} it .\t", "analysis"),
            ("H-1\tH\trecord\trecord/Verb/Tense\t\t1\tI {record} it .\t", "Name=Value"),
            ("H-1\tH\trecord\trecord/Noun\t\t0\tI {record} it .\t", "target_index"),
            ("H-1\tH\trecord\trecord/Noun\t\t1\tI record it .\t", "braced target"),
            ("H-1\tH\trecord\trecord/Noun\t\t1\tI {record} {it} .\t", "braced target"),
            ("H-1\tH\trecord\trecord/Noun\t\t1\tI  {record} it .\t", "braced target"),
        };
        foreach (var (row, expected) in cases)
        {
            // Arrange
            _dir.Write(SuiteFile.SuiteName, SuiteHeader + row + "\n");

            // Act
            var error = Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName)));

            // Assert
            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
            Assert.Contains("suite.tsv:2", error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A repeated id and a wrong header are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Read_RepeatedIdOrWrongHeader_IsRejected()
    {
        // Act and assert
        var row = "H-1\tH\trecord\trecord/Noun\t\t1\tI {record} it .\t\n";
        _dir.Write(SuiteFile.SuiteName, SuiteHeader + row + row);
        Assert.Contains("occurs twice", Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName))).Message, StringComparison.Ordinal);
        _dir.Write(SuiteFile.SuiteName, "id\tlemma\n" + row);
        Assert.Contains("header", Assert.Throws<InvalidDataException>(() => SuiteFile.ReadRows(Path.Join(_dir.Root, SuiteFile.SuiteName))).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The answer is judged at its own level: a sense or group answer is right when it is (or has) an acceptable sense,
    ///     a part-of-speech answer when the analysis has the lemma, the part of speech and the features of the row; the grade
    ///     is fine only when the most probable ranked sense is acceptable too.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Grade_JudgesTheAnswerAtItsOwnLevel()
    {
        // Arrange: two senses of one group are acceptable, a third is not
        var (a1, a2, b, _) = SenseFixtures.Grouped("test");
        var row = new SuiteRow("H-1", "H", "test", "test", PartOfSpeech.Noun, [], [a1.SenseId, a2.SenseId], ["a", "test"], 1, string.Empty);
        var details = new WordDetails("test", PartOfSpeech.Noun, string.Empty);

        // Act and assert: the sense level
        Assert.Equal(SuiteGrade.Fine, SuiteFile.Grade(row, Result(SenseLevel.Sense, details, a1.GroupId, a1.SenseId, (a1.SenseId, 0.9), (b.SenseId, 0.1))));
        Assert.Equal(SuiteGrade.Answer, SuiteFile.Grade(row, Result(SenseLevel.Sense, details, a1.GroupId, a1.SenseId, (b.SenseId, 0.9), (a1.SenseId, 0.1))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.Sense, details, b.GroupId, b.SenseId, (b.SenseId, 1.0))));

        // The group level: right when the group has an acceptable sense
        Assert.Equal(SuiteGrade.Fine, SuiteFile.Grade(row, Result(SenseLevel.Group, details, a1.GroupId, null, (a2.SenseId, 0.6))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.Group, details, b.GroupId, null, (a1.SenseId, 0.6))));

        // The part-of-speech level: right when the analysis is right; no ranked sense means no fine grade
        Assert.Equal(SuiteGrade.Answer, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, details)));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, new WordDetails("test", PartOfSpeech.Verb, string.Empty))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.None, null)));
    }

    /// <summary>
    ///     A row without acceptable senses asks for the analysis only (lemma, part of speech and every feature), at any level.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Grade_AnalysisOnlyRow_ChecksLemmaPartOfSpeechAndFeatures()
    {
        // Arrange
        var row = new SuiteRow("M-1", "M", "read", "read", PartOfSpeech.Verb, ["Tense=Past"], [], ["I", "read"], 1, string.Empty);

        // Act and assert
        Assert.Equal(SuiteGrade.Fine, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, new WordDetails("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part"))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, new WordDetails("read", PartOfSpeech.Verb, "Tense=Pres"))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, new WordDetails("reed", PartOfSpeech.Verb, "Tense=Past"))));
        Assert.Equal(SuiteGrade.Miss, SuiteFile.Grade(row, Result(SenseLevel.PartOfSpeech, new WordDetails("read", PartOfSpeech.Noun, "Tense=Past"))));
    }

    /// <summary>
    ///     The regressions are the rows below their expectation; the expectation file written from grades reads back.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Regressions_ListRowsBelowTheirExpectation_AndExpectationsRoundTrip()
    {
        // Arrange
        var rows = new[]
        {
            new SuiteRow("A", "H", "x", "x", PartOfSpeech.Noun, [], [], ["x"], 0, string.Empty),
            new SuiteRow("B", "H", "x", "x", PartOfSpeech.Noun, [], [], ["x"], 0, string.Empty),
        };
        var expectation = new Dictionary<string, SuiteGrade> { ["A"] = SuiteGrade.Fine, ["B"] = SuiteGrade.Miss };

        // Act and assert
        Assert.Equal(["A: expected fine, got answer"], SuiteFile.Regressions(rows, expectation, [SuiteGrade.Answer, SuiteGrade.Fine]));
        Assert.Empty(SuiteFile.Regressions(rows, expectation, [SuiteGrade.Fine, SuiteGrade.Miss]));

        _dir.Write(SuiteFile.SuiteName, SuiteHeader + "A\tH\tx\tx/Noun\t\t0\t{x}\t\nB\tH\tx\tx/Noun\t\t0\t{x}\t\n");
        var grades = new Dictionary<string, IReadOnlyList<SuiteGrade>>
        {
            ["basic"] = [SuiteGrade.Miss, SuiteGrade.Answer],
            ["minilm"] = [SuiteGrade.Answer, SuiteGrade.Fine],
            ["bert-base-cased"] = [SuiteGrade.Fine, SuiteGrade.Fine],
        };
        var text = SuiteFile.RenderExpectations(rows, grades);
        Assert.Equal("id\tbasic\tminilm\tbert-base-cased\nA\tmiss\tanswer\tfine\nB\tanswer\tfine\tfine\n", text);
        _dir.Write(SuiteFile.ExpectationsName, text);
        Assert.Equal(SuiteGrade.Answer, SuiteFile.Read(_dir.Root).Expectations["basic"]["B"]);
    }

    /// <summary>
    ///     The suite of the repository reads (every sense is in the catalog) and has an expectation of each model for every row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Suite_Committed_ReadsWithAnExpectationForEveryRow()
    {
        // Act
        var suite = SuiteFile.Read(SuiteFile.Locate());

        // Assert
        Assert.Equal(348, suite.Rows.Count);
        Assert.All(SuiteFile.Models, model => Assert.Equal(suite.Rows.Count, suite.Expectations[model].Count));
        Assert.Equal(["C", "H", "M"], suite.Rows.Select(r => r.List).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(suite.Rows, r => r.SenseIds.Count == 1);
        Assert.Contains(suite.Rows, r => r.SenseIds.Count > 1);
        Assert.Contains(suite.Rows, r => r.SenseIds.Count == 0);
    }

    /// <summary>
    ///     A word that is not classified (an unknown result) is a miss, not an error.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Suite_GradeAsync_ClassifiesTheSentenceOfEveryRow()
    {
        // Arrange
        var row = new SuiteRow("A", "M", "x", "x", PartOfSpeech.Noun, [], [], ["a", "x"], 1, string.Empty);
        using var model = new FakeModel(words => [.. words.Select(w => w == "x"
            ? new ClassifyResult(w, true, ImmutableArray<AnalysisCandidate>.Empty, new WordDetails("x", PartOfSpeech.Noun, string.Empty), SenseLevel.PartOfSpeech, 0.9, null, null)
            : ClassifyResult.Unknown(w))]);

        // Act
        var grades = await SuiteFile.GradeAsync(model, [row], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SuiteGrade.Fine], grades);
    }
}
