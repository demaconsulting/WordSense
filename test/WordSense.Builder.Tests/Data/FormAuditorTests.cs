// cspell:ignore dogz
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="FormAuditor"/>: the confidence interval, the verdict evaluation and the gate.
/// </summary>
public sealed class FormAuditorTests
{
    private static FormAuditor.Row Row(string spelling, string stratum) =>
        new(spelling, spelling, "n", "Number=Plur", stratum);

    private static string Key(string spelling) => $"{spelling}\t{spelling}\tn\tNumber=Plur";

    /// <summary>
    ///     The Wilson interval of no errors starts at zero and is above zero at the top.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Wilson_NoErrors_StartsAtZero()
    {
        var (low, high) = FormAuditor.Wilson(0, 300);
        Assert.Equal(0, low, 6);
        Assert.InRange(high, 0.01, 0.015);
    }

    /// <summary>
    ///     The Wilson interval brackets the observed rate.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Wilson_SomeErrors_BracketsRate()
    {
        var (low, high) = FormAuditor.Wilson(30, 300);
        Assert.True(low < 0.1 && high > 0.1);
        Assert.InRange(low, 0.07, 0.09);
        Assert.InRange(high, 0.13, 0.14);
    }

    /// <summary>
    ///     An empty sample gives the whole range.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Wilson_EmptySample_IsWholeRange()
    {
        Assert.Equal((0.0, 1.0), FormAuditor.Wilson(0, 0));
    }

    /// <summary>
    ///     Rows the oracle accepts are not errors and the gate passes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_AllGood_Passes()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry"), Row("dogs", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok", [Key("dogs")] = "ok" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 1000 }, verdicts, [], 0.01);
        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
    }

    /// <summary>
    ///     A disagreement without an adjudication counts as an error and fails the gate.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_UnadjudicatedDisagreement_Fails()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry"), Row("dogz", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok", [Key("dogz")] = "bad" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 1000 }, verdicts, [], 0.01);
        Assert.False(result.Passed);
        Assert.Single(result.Failures);
    }

    /// <summary>
    ///     A valid adjudication overrides a disagreement of the oracle.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_ValidAdjudication_Clears()
    {
        var sample = new List<FormAuditor.Row> { Row("mongoes", "llm/entry") };
        var verdicts = new Dictionary<string, string> { [Key("mongoes")] = "bad" };
        var adjudications = new Dictionary<string, string> { ["mongoes\tmongoes\tn"] = "valid" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["llm/entry"] = 700 }, verdicts, adjudications, 0.01);
        Assert.True(result.Passed);
        Assert.Equal(1, result.Strata[0].Disagreed);
        Assert.Equal(0, result.Strata[0].Errors);
    }

    /// <summary>
    ///     Rows the oracle cannot judge are left out of the sample size.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_SkippedRows_AreNotChecked()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry"), Row("zzz", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok", [Key("zzz")] = "skip" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, [], 0.01);
        Assert.Equal(1, result.Strata[0].Checked);
    }

    /// <summary>
    ///     Rows the oracle cannot judge are left out of the sample size, also when an adjudication lists them: an
    ///     adjudication answers a disagreement of the oracle and never counts a row the oracle skipped.
    /// </summary>
    /// <param name="adjudication">The adjudication of the skipped row.</param>
    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    public void WordSenseBuilder_FormAuditor_Evaluate_SkippedAndAdjudicatedRow_IsNotCounted(string adjudication)
    {
        // Arrange: one row the oracle accepts and one it skips, the skipped one adjudicated
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry"), Row("zzz", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok", [Key("zzz")] = "skip" };
        var adjudications = new Dictionary<string, string> { ["zzz\tzzz\tn"] = adjudication };

        // Act
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, adjudications, 0.01);

        // Assert: only the accepted row is checked, nothing is an error and nothing disagrees
        Assert.Equal(1, result.Strata[0].Checked);
        Assert.Equal(0, result.Strata[0].Errors);
        Assert.Equal(0, result.Strata[0].Disagreed);
        Assert.Empty(result.Failures);
        Assert.True(result.Passed);
    }

    /// <summary>
    ///     An invalid adjudication of a row the oracle disagrees with is still an error, and the failure row says so.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_InvalidAdjudicationOfDisagreement_IsAnError()
    {
        // Arrange
        var sample = new List<FormAuditor.Row> { Row("dogz", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("dogz")] = "bad" };
        var adjudications = new Dictionary<string, string> { ["dogz\tdogz\tn"] = "invalid" };

        // Act
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, adjudications, 0.01);

        // Assert
        Assert.Equal(1, result.Strata[0].Errors);
        Assert.EndsWith("\tinvalid", Assert.Single(result.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The adjudication reader skips comments and keys by spelling, lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadAdjudications_SkipsComments()
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "adj.tsv");
        File.WriteAllText(path, "# comment\nmongoes\tmongoes\tn\tvalid\tnote\n");
        var read = FormAuditor.ReadAdjudications(path);
        Assert.Single(read);
        Assert.Equal("valid", read["mongoes\tmongoes\tn"]);
    }

    /// <summary>
    ///     A row that the checker accepts is never an error, whatever the adjudications list.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_OracleAcceptedRow_IsNotAnError()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok" };
        var adjudications = new Dictionary<string, string> { ["cats\tcats\tn"] = "invalid" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, adjudications, 0.01);
        Assert.Equal(0, result.Strata[0].Errors);
    }

    /// <summary>
    ///     A verdict restricted to a feature set applies to that analysis only.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_FeatureRestrictedVerdict_AppliesToThoseFeaturesOnly()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "bad" };
        var other = new Dictionary<string, string> { ["cats\tcats\tn\tTense=Past"] = "valid" };
        var same = new Dictionary<string, string> { ["cats\tcats\tn\tNumber=Plur"] = "valid" };
        Assert.Equal(1, FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, other, 0.01).Strata[0].Errors);
        Assert.Equal(0, FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, same, 0.01).Strata[0].Errors);
    }

    /// <summary>
    ///     An optional sixth column restricts the verdict to one feature set.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadAdjudications_FeatureColumn_RestrictsTheKey()
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "adj.tsv");
        File.WriteAllText(path, "gan\tgin\tv\tinvalid\tnote\tTense=Past+VerbForm=Part\n");
        Assert.Equal("invalid", FormAuditor.ReadAdjudications(path)["gan\tgin\tv\tTense=Past+VerbForm=Part"]);
    }

    /// <summary>
    ///     A missing adjudications file gives no verdicts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadAdjudications_NoFile_IsEmpty()
    {
        Assert.Empty(FormAuditor.ReadAdjudications(null));
    }

    /// <summary>
    ///     A generated stratum with rows but no checked row fails the gate.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_GeneratedStratumWithoutCheckedRows_Fails()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry"), Row("dogs", "llm/entry") };
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok", [Key("dogs")] = "skip" };
        var population = new Dictionary<string, int> { ["rule/entry"] = 10, ["llm/entry"] = 10, ["moby_reverse/surface"] = 5 };
        var result = FormAuditor.Evaluate(sample, population, verdicts, [], 0.01);
        Assert.False(result.Passed);
        Assert.Contains(result.Failures, f => f.StartsWith("llm/entry", StringComparison.Ordinal));
        Assert.Contains(result.Failures, f => f.StartsWith("moby_reverse/surface", StringComparison.Ordinal));
    }

    /// <summary>
    ///     An empty sample cannot pass the gate while the data has generated rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_EmptySample_Fails()
    {
        var result = FormAuditor.Evaluate([], new Dictionary<string, int> { ["rule/entry"] = 10 }, [], [], 0.01);
        Assert.False(result.Passed);
    }

    /// <summary>
    ///     A sampled row that the oracle gave no verdict for is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_Evaluate_MissingVerdict_Throws()
    {
        var sample = new List<FormAuditor.Row> { Row("cats", "rule/entry") };
        Assert.Throws<InvalidDataException>(() => FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, [], [], 0.01));
    }

    /// <summary>
    ///     An empty verdict file is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadVerdicts_EmptyFile_Throws()
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "oracle.tsv");
        File.WriteAllText(path, string.Empty);
        Assert.Throws<InvalidDataException>(() => FormAuditor.ReadVerdicts(path));
    }

    /// <summary>
    ///     A malformed line or an unknown verdict of the oracle file is rejected, including a line with more than the
    ///     five documented fields and a line with an empty field.
    /// </summary>
    /// <param name="content">The file content.</param>
    [Theory]
    [InlineData("cats\tcat\tn\tNumber=Plur\n")]
    [InlineData("cats\tcat\tn\tNumber=Plur\tmaybe\n")]
    [InlineData("cats\tcat\tn\tNumber=Plur\tOK\n")]
    [InlineData("cats\tcat\tn\tNumber=Plur\tok\textra\n")]
    [InlineData("cats\tcat\tn\tNumber=Plur\tok\t\n")]
    [InlineData("\tcat\tn\tNumber=Plur\tok\n")]
    [InlineData("cats\t\tn\tNumber=Plur\tok\n")]
    [InlineData("cats\tcat\t\tNumber=Plur\tok\n")]
    [InlineData("cats\tcat\tn\t\tok\n")]
    public void WordSenseBuilder_FormAuditor_ReadVerdicts_MalformedLine_Throws(string content)
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "oracle.tsv");
        File.WriteAllText(path, content);
        var ex = Assert.Throws<InvalidDataException>(() => FormAuditor.ReadVerdicts(path));
        Assert.Contains("line 1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A row that the oracle file repeats is rejected, whether the verdicts agree or not.
    /// </summary>
    /// <param name="second">The verdict of the second occurrence.</param>
    [Theory]
    [InlineData("ok")]
    [InlineData("bad")]
    public void WordSenseBuilder_FormAuditor_ReadVerdicts_DuplicateRow_Throws(string second)
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "oracle.tsv");
        File.WriteAllText(path, $"cats\tcat\tn\tNumber=Plur\tok\ncats\tcat\tn\tNumber=Plur\t{second}\n");
        var ex = Assert.Throws<InvalidDataException>(() => FormAuditor.ReadVerdicts(path));
        Assert.Contains("line 2", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A well-formed oracle file is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadVerdicts_ValidFile_IsRead()
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "oracle.tsv");
        File.WriteAllText(path, "cats\tcat\tn\tNumber=Plur\tok\ndogz\tdog\tn\tNumber=Plur\tbad\nzzz\tz\tn\tNumber=Plur\tskip\n");
        var read = FormAuditor.ReadVerdicts(path);
        Assert.Equal(3, read.Count);
        Assert.Equal("bad", read["dogz\tdog\tn\tNumber=Plur"]);
    }

    /// <summary>
    ///     An unknown adjudication verdict or a malformed line is rejected.
    /// </summary>
    /// <param name="content">The file content.</param>
    [Theory]
    [InlineData("mongoes\tmongo\tn\tVALID\tnote\n")]
    [InlineData("mongoes\tmongo\tn\n")]
    public void WordSenseBuilder_FormAuditor_ReadAdjudications_BadVerdict_Throws(string content)
    {
        using var dir = new TestDirectory();
        var path = Path.Join(dir.Root, "adj.tsv");
        File.WriteAllText(path, content);
        Assert.Throws<InvalidDataException>(() => FormAuditor.ReadAdjudications(path));
    }
}
