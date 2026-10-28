using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the shape probe set: the reader, the scoring of an answer and the report.
/// </summary>
public sealed class ShapeProbeTests : IDisposable
{
    private const string Header = "id\tshape\tlemma\tgold\ttarget_index\tsentence\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static ShapeProbeRow Row(string id, string shape, PartOfSpeech gold) =>
        new(id, shape, "check", gold, ["Do", "a", "check", "."], 2);

    /// <summary>
    ///     The reader parses the rows: the shape, the gold part of speech, the words without braces and the target index.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_ParsesRows()
    {
        // Arrange
        var path = _dir.Write("probe.tsv", Header + "obj-1\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .\nimp-1\tv-imperative-initial\twork\tVerb\t0\t{Work} in a clean area .\n");

        // Act
        var rows = ShapeProbeFile.ReadRows(path);

        // Assert
        Assert.Equal(["obj-1", "imp-1"], rows.Select(r => r.Id));
        Assert.Equal(("n-obj-light", "check", PartOfSpeech.Noun, 3), (rows[0].Shape, rows[0].Lemma, rows[0].Gold, rows[0].TargetIndex));
        Assert.Equal(["Do", "a", "quick", "check", "."], rows[0].Words);
        Assert.Equal((PartOfSpeech.Verb, 0), (rows[1].Gold, rows[1].TargetIndex));
    }

    /// <summary>
    ///     A malformed row (columns, a shape that is not known, a gold that does not follow from the shape, a target that is
    ///     missing, doubled or not at its index) and a repeated id are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_MalformedRow_IsRejected()
    {
        var cases = new (string Row, string Expected)[]
        {
            ("a\tn-obj-light\tcheck\tNoun\t3", "expected 6"),
            ("a\tn-unknown\tcheck\tNoun\t3\tDo a quick {check} .", "shape"),
            ("a\tn-obj-light\tcheck\tVerb\t3\tDo a quick {check} .", "gold"),
            ("a\tv-after-modal\tcheck\tNoun\t3\tDo a quick {check} .", "gold"),
            ("a\tn-obj-light\tcheck\tNoun\t2\tDo a quick {check} .", "target_index"),
            ("a\tn-obj-light\tcheck\tNoun\t3\tDo a quick check .", "braced target"),
            ("a\tn-obj-light\tcheck\tNoun\t3\tDo a {quick} {check} .", "braced target"),
            ("a\tn-obj-light\tcheck\tNoun\t3\tDo  a quick {check} .", "braced target"),
            ("\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .", "must not be empty"),
        };
        foreach (var (row, expected) in cases)
        {
            var path = _dir.Write("probe.tsv", Header + row + "\n");
            Assert.Contains(expected, Assert.Throws<InvalidDataException>(() => ShapeProbeFile.ReadRows(path)).Message, StringComparison.Ordinal);
        }

        var good = "a\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .\n";
        Assert.Contains("occurs twice", Assert.Throws<InvalidDataException>(() => ShapeProbeFile.ReadRows(_dir.Write("probe.tsv", Header + good + good))).Message, StringComparison.Ordinal);
        Assert.Contains("header", Assert.Throws<InvalidDataException>(() => ShapeProbeFile.ReadRows(_dir.Write("probe.tsv", "id\tshape\n" + good))).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corpus without a probe file has no rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_Read_MissingFile_IsEmpty()
    {
        Assert.Empty(ShapeProbeFile.Read(_dir.Root));
        _dir.Write(Path.Join(ShapeProbeFile.Folder, ShapeProbeFile.FileName), Header + "a\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .\n");
        Assert.Single(ShapeProbeFile.Read(_dir.Root));
    }

    /// <summary>
    ///     An answer is right when its part of speech is the gold; it is harmful only for a gold noun answered as a verb with
    ///     a confidence of at least 0.45.
    /// </summary>
    [Theory]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Noun, 0.9, true, false)]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Verb, 0.45, false, true)]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Verb, 0.44, false, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Noun, 0.9, false, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Verb, 0.9, true, false)]
    public void WordSenseBuilder_ShapeProbe_Answer_IsCorrectAndHarmful(PartOfSpeech gold, PartOfSpeech answered, double confidence, bool correct, bool harmful)
    {
        var row = Row("a", gold == PartOfSpeech.Noun ? "n-obj-light" : "v-after-modal", gold);
        var answer = new ShapeAnswer(answered, confidence);

        Assert.Equal(correct, ShapeProbeEvaluator.IsCorrect(row, answer));
        Assert.Equal(harmful, ShapeProbeEvaluator.IsHarmful(row, answer));
        Assert.False(ShapeProbeEvaluator.IsCorrect(row, new ShapeAnswer(null, 0)));
    }

    /// <summary>
    ///     The classification of a model gives the part of speech and the confidence of the target word of each row.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ShapeProbe_AnswerAsync_ReadsTheTargetWord()
    {
        // Arrange
        var rows = new[] { Row("a", "n-obj-light", PartOfSpeech.Noun) };
        var seen = new List<string>();
        var model = new FakeModel(words =>
        {
            seen.AddRange(words);
            return [.. words.Select((w, i) => i == 2
                ? new ClassifyResult(w, true, [], new WordDetails("check", PartOfSpeech.Verb, string.Empty), SenseLevel.PartOfSpeech, 0.7, null, null)
                : new ClassifyResult(w, true, [], new WordDetails("other", PartOfSpeech.Noun, string.Empty), SenseLevel.PartOfSpeech, 0.1, null, null))];
        });

        // Act
        var answers = await ShapeProbeEvaluator.AnswerAsync(model, rows, CancellationToken.None);

        // Assert
        Assert.Equal(rows[0].Words, seen);
        Assert.Equal(new ShapeAnswer(PartOfSpeech.Verb, 0.7), Assert.Single(answers));
    }

    /// <summary>
    ///     The report counts the right answers per shape and per model, and the harmful answers among the noun rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_Render_ReportsAccuracyAndHarmfulRate()
    {
        // Arrange
        var rows = new[]
        {
            Row("a", "n-obj-light", PartOfSpeech.Noun),
            Row("b", "n-obj-light", PartOfSpeech.Noun),
            Row("c", "v-after-modal", PartOfSpeech.Verb),
            Row("d", "v-after-modal", PartOfSpeech.Verb),
        };
        var good = new[] { new ShapeAnswer(PartOfSpeech.Noun, 0.9), new ShapeAnswer(PartOfSpeech.Noun, 0.8), new ShapeAnswer(PartOfSpeech.Verb, 0.9), new ShapeAnswer(PartOfSpeech.Verb, 0.7) };
        var weak = new[] { new ShapeAnswer(PartOfSpeech.Verb, 0.6), new ShapeAnswer(PartOfSpeech.Noun, 0.8), new ShapeAnswer(PartOfSpeech.Noun, 0.9), new ShapeAnswer(PartOfSpeech.Verb, 0.7) };

        // Act
        var text = ShapeProbeEvaluator.Render(rows, [("Good", good), ("Weak", weak)]);

        // Assert
        Assert.Contains("| n-obj-light | Noun | 2 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| v-after-modal | Verb | 2 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| all | " + " | 4 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| n-obj-light | 2 | 0 (0.00%) | 1 (50.00%) |", text, StringComparison.Ordinal);
        Assert.Contains("| n-modifier | Noun | 0 | n/a | n/a |", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The outcomes table has a line for every model and row, with the answer, the confidence and the two flags.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_RenderOutcomes_WritesOneLinePerModelAndRow()
    {
        // Arrange
        var rows = new[] { Row("a", "n-obj-light", PartOfSpeech.Noun) };

        // Act
        var text = ShapeProbeEvaluator.RenderOutcomes(rows, [("M", [new ShapeAnswer(PartOfSpeech.Verb, 0.5)]), ("N", [new ShapeAnswer(null, 0)])]);

        // Assert
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal("id\tshape\tlemma\tgold\tmodel\tanswered\tconfidence\tcorrect\tharmful", lines[0]);
        Assert.Equal("a\tn-obj-light\tcheck\tNoun\tM\tVerb\t0.500\t0\t1", lines[1]);
        Assert.Equal("a\tn-obj-light\tcheck\tNoun\tN\tnone\t0.000\t0\t0", lines[2]);
    }
}
