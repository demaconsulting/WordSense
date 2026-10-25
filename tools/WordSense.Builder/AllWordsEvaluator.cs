using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The measurement of one model on one half of the all-words gold set.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Split">The half: <c>dev</c> or <c>test</c>.</param>
/// <param name="Set">The counts at each level (see <see cref="SetResult"/>).</param>
/// <param name="FineTop1">The tokens whose selected analysis ranks the gold sense first.</param>
/// <param name="Ranked">The tokens whose selected analysis ranks at least one sense.</param>
/// <param name="FineTop1GivenAnalysis">The tokens among the tokens whose selected analysis has the gold lemma and part of speech whose selected analysis ranks the gold sense first.</param>
internal sealed record AllWordsModelScore(string Model, string Split, SetResult Set, int FineTop1, int Ranked, int FineTop1GivenAnalysis);

/// <summary>
///     The baselines on one half of the all-words gold set; all of them know the gold lemma and part of speech.
/// </summary>
/// <param name="Split">The half.</param>
/// <param name="Rows">The tokens.</param>
/// <param name="FirstSense">The tokens whose gold sense is the first sense of its lemma and part of speech in inventory order.</param>
/// <param name="TagCount">The tokens whose gold sense is the sense with the highest WordNet 3.0 tag count (inventory order breaks ties).</param>
/// <param name="Random">The expected number of tokens a uniformly random sense of the lemma and part of speech gets right: the sum of one over the number of senses.</param>
internal sealed record AllWordsBaselines(string Split, int Rows, int FirstSense, int TagCount, double Random);

/// <summary>
///     The mean log loss of the gold sense under the prior for one smoothing constant.
/// </summary>
/// <param name="K">The constant added to every tag count.</param>
/// <param name="LogLoss">The mean of <c>-ln p(gold)</c> over the tokens, in nats; lower is better.</param>
internal sealed record SmoothingRow(double K, double LogLoss);

/// <summary>
///     Measures the models on the all-words gold set (<c>corpus/allwords</c>): what the ranking of the senses of untrained
///     polysemous lemmas is worth on text that no tag count was made on.
/// </summary>
/// <remarks>
///     <para>
///     Every token is classified inside its sentence and judged like any held-out row (<see cref="Evaluator.Judge"/>): the
///     share of the tokens answered at the sense level and the precision of those answers are the figures of the other
///     evaluations and do not change with the ranking, which is never an answer. The fine top-1 is the new figure: the
///     token counts when the most probable sense of the <i>selected</i> analysis (the first listed sense of it) is the gold
///     sense; a token whose selected analysis ranks no sense counts as a miss. The baselines (the first sense of the lemma and
///     part of speech in inventory order, the sense with the highest WordNet 3.0 tag count and a random sense) are told the gold
///     lemma and part of speech, so they do not pay for a wrong analysis and are an upper reference for the models, whose own
///     choice of the analysis is part of the score.
///     </para>
///     <para>
///     The smoothing constant of the prior does not change the ranking, only the probabilities, so it is chosen on the
///     development half by the mean log loss of the gold sense (<see cref="Sweep"/>); the test half is only reported.
///     </para>
/// </remarks>
internal static class AllWordsEvaluator
{
    /// <summary>
    ///     The smoothing constants of the sweep.
    /// </summary>
    public static readonly IReadOnlyList<double> SweepValues = [0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0];

    /// <summary>
    ///     Measures one model on one half.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="model">The model.</param>
    /// <param name="split">The half.</param>
    /// <param name="items">The gold tokens of the half.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The measurement.</returns>
    public static async Task<AllWordsModelScore> MeasureAsync(string name, IWordSenseModel model, string split, IReadOnlyList<AllWordsItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(items);
        var verdicts = new List<Evaluator.Verdict>(items.Count);
        int top1 = 0, ranked = 0, top1GivenAnalysis = 0;
        foreach (var item in items)
        {
            var results = await model.ClassifyAsync(item.Words, cancellationToken).ConfigureAwait(false);
            var result = results[item.Target];
            var verdict = Evaluator.Judge(new CorpusRow(item.Lemma, item.SenseId, item.PartOfSpeech, string.Join(' ', item.Words), "allwords", item.Words, item.Target), result);
            verdicts.Add(verdict);
            var top = TopSense(result);
            var right = string.Equals(top, item.SenseId, StringComparison.Ordinal);
            top1 += right ? 1 : 0;
            ranked += top is not null ? 1 : 0;
            top1GivenAnalysis += verdict.AnalysisCorrect && right ? 1 : 0;
        }

        return new AllWordsModelScore(name, split, Evaluator.Summarize("all-words " + split, verdicts), top1, ranked, top1GivenAnalysis);
    }

    /// <summary>
    ///     Finds the most probable ranked sense of the selected analysis of a result.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The id of the first sense of the selected analysis, or null when the result has no selected analysis or it ranks no sense.</returns>
    internal static string? TopSense(ClassifyResult result)
    {
        if (result.Details is null)
        {
            return null;
        }

        var selected = result.Analyses.Where(a => a.Details.Equals(result.Details)).Select(a => a.Senses).FirstOrDefault();
        return selected.IsDefaultOrEmpty ? null : selected[0].SenseId;
    }

    /// <summary>
    ///     Computes the baselines of one half.
    /// </summary>
    /// <param name="split">The half.</param>
    /// <param name="items">The gold tokens.</param>
    /// <returns>The baselines.</returns>
    public static AllWordsBaselines Baselines(string split, IReadOnlyList<AllWordsItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        int first = 0, tag = 0;
        var random = 0.0;
        foreach (var item in items)
        {
            var usage = WordLexicon.Current.UsageOf(item.Lemma, item.PartOfSpeech);
            if (usage.Length == 0)
            {
                continue;
            }

            first += string.Equals(usage[0].SenseId, item.SenseId, StringComparison.Ordinal) ? 1 : 0;
            var best = 0;
            for (var i = 1; i < usage.Length; i++)
            {
                best = usage[i].TagCount > usage[best].TagCount ? i : best;
            }

            tag += string.Equals(usage[best].SenseId, item.SenseId, StringComparison.Ordinal) ? 1 : 0;
            random += 1.0 / usage.Length;
        }

        return new AllWordsBaselines(split, items.Count, first, tag, random);
    }

    /// <summary>
    ///     Computes the mean log loss of the gold sense under the prior for several smoothing constants.
    /// </summary>
    /// <param name="items">The gold tokens (the development half: the constant is chosen on it only).</param>
    /// <param name="values">The constants.</param>
    /// <returns>One row for each constant.</returns>
    public static List<SmoothingRow> Sweep(IReadOnlyList<AllWordsItem> items, IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(values);
        var rows = new List<SmoothingRow>();
        foreach (var k in values)
        {
            var loss = 0.0;
            foreach (var item in items)
            {
                var ranked = UsagePrior.Rank(WordLexicon.Current.UsageOf(item.Lemma, item.PartOfSpeech), k);
                loss -= Math.Log(ranked.First(s => string.Equals(s.SenseId, item.SenseId, StringComparison.Ordinal)).Probability);
            }

            rows.Add(new SmoothingRow(k, items.Count == 0 ? 0 : loss / items.Count));
        }

        return rows;
    }

    /// <summary>
    ///     Renders the measurements as Markdown.
    /// </summary>
    /// <param name="scores">The score of each model on each half.</param>
    /// <param name="baselines">The baselines of each half.</param>
    /// <param name="sweep">The smoothing sweep on the development half, or null when it was not made.</param>
    /// <param name="lemmaCounts">The number of distinct lemmas of each half.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(IReadOnlyList<AllWordsModelScore> scores, IReadOnlyList<AllWordsBaselines> baselines, IReadOnlyList<SmoothingRow>? sweep, IReadOnlyDictionary<string, int> lemmaCounts)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(lemmaCounts);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("### All-words gold set (untrained polysemous lemmas, two independent labelers)\n\n");
        foreach (var half in baselines)
        {
            text.Append("#### ").Append(half.Split).Append(" half (").Append(half.Rows.ToString("N0", inv)).Append(" tokens, ").Append(lemmaCounts.GetValueOrDefault(half.Split).ToString("N0", inv)).Append(" lemmas)\n\n");
            text.Append("| Model or baseline | Sense answered | Sense precision | Part of speech answered | Analysis right | Fine top-1 | Fine top-1 when the analysis is right | Ranks senses |\n");
            text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (var s in scores.Where(x => x.Split == half.Split))
            {
                var set = s.Set;
                text.Append("| ").Append(s.Model)
                    .Append(" | ").Append(EvaluationResult.Percent(set.Sense.Answered, set.Rows))
                    .Append(" | ").Append(EvaluationResult.Percent(set.Sense.Correct, set.Sense.Answered))
                    .Append(" | ").Append(EvaluationResult.Percent(set.PartOfSpeech.Answered, set.Rows))
                    .Append(" | ").Append(EvaluationResult.Percent(set.AnalysisCorrect, set.Rows))
                    .Append(" | ").Append(EvaluationResult.Percent(s.FineTop1, set.Rows))
                    .Append(" | ").Append(EvaluationResult.Percent(s.FineTop1GivenAnalysis, set.AnalysisCorrect))
                    .Append(" | ").Append(EvaluationResult.Percent(s.Ranked, set.Rows)).Append(" |\n");
            }

            text.Append("| first sense (inventory order) | n/a | n/a | n/a | n/a | ").Append(EvaluationResult.Percent(half.FirstSense, half.Rows)).Append(" | n/a | n/a |\n");
            text.Append("| most frequent sense (WordNet 3.0 tag counts) | n/a | n/a | n/a | n/a | ").Append(EvaluationResult.Percent(half.TagCount, half.Rows)).Append(" | n/a | n/a |\n");
            text.Append("| random sense | n/a | n/a | n/a | n/a | ").Append((half.Rows == 0 ? "n/a" : (100.0 * half.Random / half.Rows).ToString("0.00", inv) + "%")).Append(" | n/a | n/a |\n\n");
        }

        text.Append("The fine top-1 counts a token when the first ranked sense of the selected analysis is the gold sense; a token whose selected analysis\n");
        text.Append("ranks no sense is a miss. The baselines are told the gold lemma and part of speech; the models choose their own analysis.\n");
        if (sweep is { Count: > 0 })
        {
            text.Append("\n#### Smoothing constant of the prior (development half only)\n\n| k | Mean log loss of the gold sense (nats) |\n| ---: | ---: |\n");
            foreach (var row in sweep)
            {
                text.Append("| ").Append(row.K.ToString("0.##", inv)).Append(" | ").Append(row.LogLoss.ToString("0.0000", inv)).Append(" |\n");
            }

            text.Append("\nThe ranking, and so the fine top-1, does not depend on the constant; it sets the probabilities of the ranked senses.\n");
        }

        return text.ToString();
    }
}
