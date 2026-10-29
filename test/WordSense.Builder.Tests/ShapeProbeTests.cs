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

    private void Reject(string row, string expected)
    {
        var path = _dir.Write("probe.tsv", Header + row + "\n");
        var message = Assert.Throws<InvalidDataException>(() => ShapeProbeFile.ReadRows(path)).Message;
        Assert.Contains(expected, message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A row without six columns is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_WrongColumnCount_IsRejected()
    {
        Reject("a\tn-obj-light\tcheck\tNoun\t3", "expected 6");
    }

    /// <summary>
    ///     A row with a shape that is not one of the seven is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_UnknownShape_IsRejected()
    {
        Reject("a\tn-unknown\tcheck\tNoun\t3\tDo a quick {check} .", "shape");
    }

    /// <summary>
    ///     A row whose gold part of speech is not that of its shape (Noun for a noun shape, Verb for a verb shape) is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_GoldNotOfTheShape_IsRejected()
    {
        Reject("a\tn-obj-light\tcheck\tVerb\t3\tDo a quick {check} .", "gold");
        Reject("a\tv-after-modal\tcheck\tNoun\t3\tDo a quick {check} .", "gold");
    }

    /// <summary>
    ///     A row without exactly one braced target word, or whose target index is not the index of that word, is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_TargetMissingDoubledOrMisplaced_IsRejected()
    {
        Reject("a\tn-obj-light\tcheck\tNoun\t2\tDo a quick {check} .", "target_index");
        Reject("a\tn-obj-light\tcheck\tNoun\t3\tDo a quick check .", "braced target");
        Reject("a\tn-obj-light\tcheck\tNoun\t3\tDo a {quick} {check} .", "braced target");
        Reject("a\tn-obj-light\tcheck\tNoun\t3\tDo  a quick {check} .", "braced target");
    }

    /// <summary>
    ///     A row with an empty id is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_EmptyId_IsRejected()
    {
        Reject("\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .", "must not be empty");
    }

    /// <summary>
    ///     A row with an empty lemma is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_EmptyLemma_IsRejected()
    {
        Reject("a\tn-obj-light\t\tNoun\t3\tDo a quick {check} .", "must not be empty");
    }

    /// <summary>
    ///     A repeated id is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_RepeatedId_IsRejected()
    {
        var good = "a\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .\n";
        Assert.Contains("occurs twice", Assert.Throws<InvalidDataException>(() => ShapeProbeFile.ReadRows(_dir.Write("probe.tsv", Header + good + good))).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A file with another header is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_ReadRows_WrongHeader_IsRejected()
    {
        var good = "a\tn-obj-light\tcheck\tNoun\t3\tDo a quick {check} .\n";
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
    ///     An answer is right when its part of speech is the gold; a word that is not answered is wrong.
    /// </summary>
    [Theory]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Noun, true)]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Verb, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Noun, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Verb, true)]
    public void WordSenseBuilder_ShapeProbe_Answer_IsCorrect(PartOfSpeech gold, PartOfSpeech answered, bool correct)
    {
        var row = Row("a", gold == PartOfSpeech.Noun ? "n-obj-light" : "v-after-modal", gold);

        Assert.Equal(correct, ShapeProbeEvaluator.IsCorrect(row, new ShapeAnswer(answered, 0.9)));
        Assert.False(ShapeProbeEvaluator.IsCorrect(row, new ShapeAnswer(null, 0)));
    }

    /// <summary>
    ///     An answer is harmful only for a gold noun answered as a verb with a confidence of at least 0.45.
    /// </summary>
    [Theory]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Noun, 0.9, false)]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Verb, 0.45, true)]
    [InlineData(PartOfSpeech.Noun, PartOfSpeech.Verb, 0.44, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Noun, 0.9, false)]
    [InlineData(PartOfSpeech.Verb, PartOfSpeech.Verb, 0.9, false)]
    public void WordSenseBuilder_ShapeProbe_Answer_IsHarmful(PartOfSpeech gold, PartOfSpeech answered, double confidence, bool harmful)
    {
        var row = Row("a", gold == PartOfSpeech.Noun ? "n-obj-light" : "v-after-modal", gold);

        Assert.Equal(harmful, ShapeProbeEvaluator.IsHarmful(row, new ShapeAnswer(answered, confidence)));
    }

    /// <summary>
    ///     The committed probe file reads and has between 400 and 600 rows, at least 60 for each shape and at least 300
    ///     lemmas.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_CommittedFile_HasEnoughRowsInEveryShape()
    {
        // Act
        var rows = ShapeProbeFile.Read(Path.Join(DataFixture.RepositoryRoot(), "corpus"));

        // Assert
        Assert.InRange(rows.Count, 400, 600);
        foreach (var (shape, _) in ShapeProbeFile.Shapes)
        {
            Assert.True(rows.Count(r => r.Shape == shape) >= 60, shape);
        }

        Assert.True(rows.Select(r => r.Lemma).Distinct(StringComparer.Ordinal).Count() >= 300);
    }

    /// <summary>
    ///     In the committed probe file the target of every row is a form of its lemma with the gold part of speech in the
    ///     lexicon.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_CommittedFile_TargetsAreFormsOfTheirLemma()
    {
        // Act
        var rows = ShapeProbeFile.Read(Path.Join(DataFixture.RepositoryRoot(), "corpus"));

        // Assert
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var key = BasicClassifier.Normalize(row.Words[row.TargetIndex]);
            Assert.True(WordLexicon.TryLookup(key, out var analyses) && analyses.Any(a => a.Lemma == row.Lemma && a.PartOfSpeech == row.Gold), row.Id);
        }
    }

    /// <summary>
    ///     The command reports the accuracy of the model for every shape and the harmful rate.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateShapes_ReportsEveryShapeForTheModel()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();
        var report = Path.Join(_dir.Root, "report.md");

        // Act
        var code = await Program.RunAsync(["evaluate-shapes", "--model", "basic", "--repository", DataFixture.RepositoryRoot(), "--output", report], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        var text = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        Assert.Contains("### Shape probe", text, StringComparison.Ordinal);
        foreach (var (shape, _) in ShapeProbeFile.Shapes)
        {
            Assert.Contains("| " + shape + " |", text, StringComparison.Ordinal);
        }

        Assert.Contains("| all noun shapes |", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command writes the outcomes of every model and row when asked.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateShapes_WritesTheOutcomesFile()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();
        var outcomes = Path.Join(_dir.Root, "outcomes.tsv");

        // Act
        var code = await Program.RunAsync(["evaluate-shapes", "--model", "basic", "--repository", DataFixture.RepositoryRoot(), "--outcomes", outcomes], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.StartsWith("id\tshape\tlemma", await File.ReadAllTextAsync(outcomes, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corpus without a probe file is reported by the command.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateShapes_WithoutProbeFile_Fails()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["evaluate-shapes", "--model", "basic", "--repository", DataFixture.RepositoryRoot(), "--corpus", Path.Join(_dir.Root, "none")], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("shape probe set is not in", error.ToString(), StringComparison.Ordinal);
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
    ///     The report counts the right answers per shape and per model; a shape without rows shows n/a.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_Render_ReportsAccuracyPerShape()
    {
        // Arrange
        var (rows, good, weak) = RenderFixture();

        // Act
        var text = ShapeProbeEvaluator.Render(rows, [("Good", good), ("Weak", weak)]);

        // Assert
        Assert.Contains("| n-obj-light | Noun | 2 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| v-after-modal | Verb | 2 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| all | " + " | 4 | 100.00% | 50.00% |", text, StringComparison.Ordinal);
        Assert.Contains("| n-modifier | Noun | 0 | n/a | n/a |", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The report counts the harmful answers per model among the noun rows of each noun shape.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeProbe_Render_ReportsTheHarmfulRatePerNounShape()
    {
        // Arrange
        var (rows, good, weak) = RenderFixture();

        // Act
        var text = ShapeProbeEvaluator.Render(rows, [("Good", good), ("Weak", weak)]);

        // Assert
        Assert.Contains("| n-obj-light | 2 | 0 (0.00%) | 1 (50.00%) |", text, StringComparison.Ordinal);
    }

    private static (ShapeProbeRow[] Rows, ShapeAnswer[] Good, ShapeAnswer[] Weak) RenderFixture()
    {
        var rows = new[]
        {
            Row("a", "n-obj-light", PartOfSpeech.Noun),
            Row("b", "n-obj-light", PartOfSpeech.Noun),
            Row("c", "v-after-modal", PartOfSpeech.Verb),
            Row("d", "v-after-modal", PartOfSpeech.Verb),
        };
        var good = new[] { new ShapeAnswer(PartOfSpeech.Noun, 0.9), new ShapeAnswer(PartOfSpeech.Noun, 0.8), new ShapeAnswer(PartOfSpeech.Verb, 0.9), new ShapeAnswer(PartOfSpeech.Verb, 0.7) };
        var weak = new[] { new ShapeAnswer(PartOfSpeech.Verb, 0.6), new ShapeAnswer(PartOfSpeech.Noun, 0.8), new ShapeAnswer(PartOfSpeech.Noun, 0.9), new ShapeAnswer(PartOfSpeech.Verb, 0.7) };
        return (rows, good, weak);
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
