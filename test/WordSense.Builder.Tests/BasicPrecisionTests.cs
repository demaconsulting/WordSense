using BasicRules = DemaConsulting.WordSense.Basic.Rules;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the precision measurement of the rules of the basic model.
/// </summary>
public sealed class BasicPrecisionTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     The precision is the share of correct answers among the firings, and 0 for a rule that never fired.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicPrecision_RulePrecision_IsCorrectOverFired()
    {
        Assert.Equal(0.75, new RulePrecision("r", 4, 3).Precision);
        Assert.Equal(1.0, new RulePrecision("r", 2, 2).Precision);
        Assert.Equal(0.0, new RulePrecision("r", 0, 0).Precision);
    }

    /// <summary>
    ///     The rendering has one row per rule, in rule order, with the measured precision and the current confidence.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicPrecision_Render_ListsEveryRule()
    {
        var rules = BasicRules.RuleNames.Select((name, i) => new RulePrecision(name, i + 1, i)).ToList();

        var text = BasicPrecision.Render(rules, 42);

        Assert.StartsWith("### Basic rule precision on the training anchors\n\nAnchors evaluated: 42\n\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal(BasicRules.RuleNames.Count, text.Split('\n').Count(l => l.StartsWith("| after-", StringComparison.Ordinal) || l.StartsWith("| before-", StringComparison.Ordinal)));
        Assert.Contains("| after-modal | 1 | 0 | 0.000 | 0.900 |\n", text, StringComparison.Ordinal);
        Assert.Contains("| after-to | 2 | 1 | 0.500 | 0.700 |\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Measuring a small corpus counts the rule that fired first and whether its part of speech was right.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicPrecision_Measure_CountsFiringsOnAnchors()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "walk\tnoun\tnoun\t\t\nwalk\tverb\tverb\t\t\n");
        _dir.Write(
            "corpus/anchors/a.tsv",
            "walk\tverb\tThey will {walk} home.\n" +
            "walk\tnoun\tThey will {walk} home too.\n" +
            "walk\tnoun\tThe {walk} was long.\n" +
            "walk\tnoun\tA {walk} helps.\n");

        var (rules, anchors) = BasicPrecision.Measure(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(4, anchors);
        // Depends on the committed word table listing walk as noun and verb
        Assert.Equal(BasicRules.RuleNames, rules.Select(r => r.Rule));
        var modal = rules.Single(r => r.Rule == BasicRules.AfterModal);
        Assert.Equal(2, modal.Fired);
        Assert.Equal(1, modal.Correct);
        var determiner = rules.Single(r => r.Rule == BasicRules.AfterDeterminer);
        Assert.Equal(2, determiner.Fired);
        Assert.Equal(2, determiner.Correct);
    }

    /// <summary>
    ///     A corpus without anchors measures nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicPrecision_Measure_NoAnchors_FiresNoRule()
    {
        var (rules, anchors) = BasicPrecision.Measure(Path.Combine(_dir.Root, "empty"));

        Assert.Equal(0, anchors);
        Assert.All(rules, r => Assert.Equal(0, r.Fired));
    }
}
