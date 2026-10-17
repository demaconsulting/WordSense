using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;
using BasicRules = DemaConsulting.WordSense.Basic.Rules;

namespace WordSense.Builder;

/// <summary>
///     The measured precision of one rule of the basic model on the training anchors.
/// </summary>
/// <param name="Rule">The name of the rule.</param>
/// <param name="Fired">The anchors on which the rule fired first.</param>
/// <param name="Correct">The anchors on which the part of speech the rule selected was right.</param>
internal sealed record RulePrecision(string Rule, int Fired, int Correct)
{
    /// <summary>
    ///     Gets the precision, or 0 when the rule never fired.
    /// </summary>
    public double Precision => Fired == 0 ? 0 : (double)Correct / Fired;
}

/// <summary>
///     Measures the precision of every rule of the basic model on the TRAIN anchors, so the fixed confidences in
///     <c>Rules</c> can be calibrated to measured values. The holdout is never read here.
/// </summary>
internal static class BasicPrecision
{
    /// <summary>
    ///     Measures the rules.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>One result per rule, in the order the rules are tried, and the number of anchors evaluated.</returns>
    public static (IReadOnlyList<RulePrecision> Rules, int Anchors) Measure(string corpus)
    {
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var gold = inventory.Rows.ToDictionary(r => (r.Lemma, r.Label));
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var fired = Rules().ToDictionary(r => r, _ => 0, StringComparer.Ordinal);
        var correct = Rules().ToDictionary(r => r, _ => 0, StringComparer.Ordinal);
        foreach (var row in anchors)
        {
            var keys = row.Words.Select(BasicClassifier.Normalize).ToList();
            if (!WordLexicon.Words.TryGetValue(keys[row.TargetWordIndex], out var senses) || senses.Length < 2)
            {
                continue;
            }

            var candidates = senses.Select(s => s.PartOfSpeech).ToHashSet();
            var previous = row.TargetWordIndex > 0 && keys[row.TargetWordIndex - 1].Length > 0 ? keys[row.TargetWordIndex - 1] : null;
            var next = row.TargetWordIndex + 1 < keys.Count && keys[row.TargetWordIndex + 1].Length > 0 ? keys[row.TargetWordIndex + 1] : null;
            if (BasicRules.Evaluate(previous, next, candidates) is not { } match)
            {
                continue;
            }

            fired[match.Rule]++;
            if (gold[(row.Lemma, row.Sense)].Pos == match.PartOfSpeech)
            {
                correct[match.Rule]++;
            }
        }

        var results = Rules().Select(r => new RulePrecision(r, fired[r], correct[r])).ToList();
        return (results, anchors.Count);
    }

    /// <summary>
    ///     Renders the measurements as Markdown with the confidence each rule currently has.
    /// </summary>
    /// <param name="rules">The measurements.</param>
    /// <param name="anchors">The number of anchors evaluated.</param>
    /// <returns>The text, with LF line ends.</returns>
    public static string Render(IReadOnlyList<RulePrecision> rules, int anchors)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### Basic rule precision on the training anchors\n\n");
        text.Append("Anchors evaluated: ").Append(anchors.ToString(inv)).Append("\n\n");
        text.Append("| Rule | Fired | Correct | Precision | Current confidence |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: |\n");
        foreach (var r in rules)
        {
            text.Append("| ").Append(r.Rule)
                .Append(" | ").Append(r.Fired.ToString(inv))
                .Append(" | ").Append(r.Correct.ToString(inv))
                .Append(" | ").Append(r.Precision.ToString("0.000", inv))
                .Append(" | ").Append(Rules_Confidence(r.Rule).ToString("0.000", inv)).Append(" |\n");
        }

        return text.ToString();
    }

    private static IReadOnlyList<string> Rules() => BasicRules.RuleNames;

    private static double Rules_Confidence(string rule) => BasicRules.Confidences[rule];
}
