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
        var sample = new List<FormAuditor.Row> { Row("mongoes", "agid/entry") };
        var verdicts = new Dictionary<string, string> { [Key("mongoes")] = "bad" };
        var adjudications = new Dictionary<string, string> { ["mongoes\tmongoes\tn"] = "valid" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["agid/entry"] = 700 }, verdicts, adjudications, 0.01);
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
        var verdicts = new Dictionary<string, string> { [Key("cats")] = "ok" };
        var result = FormAuditor.Evaluate(sample, new Dictionary<string, int> { ["rule/entry"] = 10 }, verdicts, [], 0.01);
        Assert.Equal(1, result.Strata[0].Checked);
    }

    /// <summary>
    ///     The adjudication reader skips comments and keys by spelling, lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormAuditor_ReadAdjudications_SkipsComments()
    {
        using var dir = new TestDirectory();
        var path = Path.Combine(dir.Root, "adj.tsv");
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
        var path = Path.Combine(dir.Root, "adj.tsv");
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
}
