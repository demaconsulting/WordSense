using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     What one model did on one token of the second all-words gold set: 1 when right, 0 when wrong, -1 when the measure does
///     not apply to the token.
/// </summary>
/// <param name="Id">The token id.</param>
/// <param name="Pos">The part of speech: the selected analysis has the gold part of speech (a token that is not answered is wrong).</param>
/// <param name="Lemma">The lemma: the selected analysis has the gold lemma.</param>
/// <param name="Tense">The tense of a same-spelling verb: the selected analysis is past (past tense or participle) exactly when the gold analysis is.</param>
/// <param name="Sense">The sense: the first sense of the selected analysis is the gold sense.</param>
/// <param name="SenseOnAnalysis">The sense, only when the selected analysis has the gold lemma and part of speech.</param>
internal sealed record Gold2Scored(string Id, int Pos, int Lemma, int Tense, int Sense, int SenseOnAnalysis)
{
    /// <summary>The names of the measures, in the order of <see cref="Value"/>.</summary>
    public static readonly IReadOnlyList<string> Measures = ["part of speech", "lemma", "tense", "sense", "sense (analysis right)"];

    /// <summary>
    ///     Gets the outcome of a measure.
    /// </summary>
    /// <param name="measure">The index into <see cref="Measures"/>.</param>
    /// <returns>1, 0 or -1.</returns>
    public int Value(int measure) => measure switch
    {
        0 => Pos,
        1 => Lemma,
        2 => Tense,
        3 => Sense,
        _ => SenseOnAnalysis,
    };
}

/// <summary>
///     The result of a paired comparison of two models on one measure and stratum.
/// </summary>
/// <param name="Stratum">The stratum.</param>
/// <param name="Measure">The measure.</param>
/// <param name="Tokens">The tokens that both models were scored on.</param>
/// <param name="CorrectFirst">The tokens the first model got right.</param>
/// <param name="CorrectSecond">The tokens the second model got right.</param>
/// <param name="Wins">The tokens only the second model got right.</param>
/// <param name="Losses">The tokens only the first model got right.</param>
/// <param name="PValue">The exact two-sided McNemar p value.</param>
/// <param name="Low">The lower end of the 95% interval of the difference (second minus first), as a share.</param>
/// <param name="High">The upper end of the 95% interval of the difference.</param>
internal sealed record Gold2Comparison(string Stratum, string Measure, int Tokens, int CorrectFirst, int CorrectSecond, int Wins, int Losses, double PValue, double Low, double High);

/// <summary>
///     Measures models on the second all-words gold set (<c>corpus/allwords/v2</c>): part of speech, lemma, tense and sense on
///     every stratum, and compares two models on the same tokens.
/// </summary>
/// <remarks>
///     <para>
///     Every token is classified inside its sentence (each sentence once) and scored on four measures. The part of speech and
///     the lemma are those of the selected analysis; a token the model does not answer is wrong. The tense is scored on the
///     tokens that the gold set labels as a verb whose spelling is shared by a past and a non-past verb (the rule of
///     <see cref="TenseEvaluator"/>: past is a past tense or a past participle). The sense is the first sense of the selected
///     analysis (<see cref="AllWordsEvaluator.TopSense"/>), scored on the tokens whose gold sense is one of the senses of a
///     lemma and part of speech of two or more senses (a token with no defensible single sense is left out and counted). Whether
///     a lemma is trained is read from the weights of the shipped models (a lemma has weights when two or more of its senses
///     have anchors), so the strata follow later lemma expansions.
///     </para>
///     <para>
///     A comparison of two models counts the tokens that only one gets right (McNemar, exact two-sided p) and gives the
///     difference with a 95% interval from a bootstrap that resamples whole documents (the tokens of one document are not
///     independent). The strata are the ones of the sampling (heteronym, noun and verb, tense, lemma-ambiguous), trained
///     and untrained lemmas by the gold lemma, and the source group (technical, news, literary).
///     </para>
/// </remarks>
internal static class Gold2Evaluator
{
    /// <summary>The stratum of every token.</summary>
    public const string All = "all";

    /// <summary>The number of resamples of the bootstrap.</summary>
    public const int Resamples = 1000;

    /// <summary>The names of the strata in the order of the reports.</summary>
    public static readonly IReadOnlyList<string> Strata = [All, "trained lemma", "untrained lemma", "heteronym", "noun/verb", "tense", "lemma-ambiguous", "technical", "news", "literary"];

    /// <summary>
    ///     Finds the strata of a gold row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <returns>The names of the strata the row is in.</returns>
    public static IReadOnlyList<string> StrataOf(Gold2Row row, IReadOnlySet<string> trained)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(trained);
        var flags = row.Token.Strata.Split(',');
        var list = new List<string> { All, trained.Contains(row.Lemma) ? "trained lemma" : "untrained lemma", Gold2Strata.GroupOf(row.Token.Source) };
        if (flags.Contains(Gold2Strata.Heteronym))
        {
            list.Add("heteronym");
        }

        if (flags.Contains(Gold2Strata.NounVerb))
        {
            list.Add("noun/verb");
        }

        if (flags.Contains(Gold2Strata.Tense))
        {
            list.Add("tense");
        }

        if (flags.Contains(Gold2Strata.Lemma))
        {
            list.Add("lemma-ambiguous");
        }

        return list;
    }

    /// <summary>
    ///     Scores the answer of a model for one token.
    /// </summary>
    /// <param name="row">The gold row.</param>
    /// <param name="result">The result of the model for the token.</param>
    /// <returns>The outcome on each measure.</returns>
    public static Gold2Scored Score(Gold2Row row, ClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(row);
        var details = result.Details;
        var pos = row.Pos == "Other" ? -1 : Flag(details is not null && string.Equals(details.PartOfSpeech.ToString(), row.Pos, StringComparison.Ordinal));
        var lemma = Flag(details is not null && string.Equals(details.Lemma, row.Lemma, StringComparison.OrdinalIgnoreCase));
        var tense = row.Tense != "-" && TenseEvaluator.GroupsOf(row.Token.Form).Count > 0
            ? Flag(details is not null && BasicClassifier.IsPast(details) == (row.Tense == "past"))
            : -1;
        var sensed = row.Sense is not ("-" or "none");
        var top = AllWordsEvaluator.TopSense(result);
        var sense = sensed ? Flag(string.Equals(top, row.Sense, StringComparison.Ordinal)) : -1;
        var onAnalysis = sensed && details is not null && string.Equals(details.Lemma, row.Lemma, StringComparison.OrdinalIgnoreCase) && string.Equals(details.PartOfSpeech.ToString(), row.Pos, StringComparison.Ordinal)
            ? Flag(string.Equals(top, row.Sense, StringComparison.Ordinal))
            : -1;
        return new Gold2Scored(row.Token.Id, pos, lemma, tense, sense, onAnalysis);
    }

    /// <summary>
    ///     Turns a verdict into the outcome of a measure.
    /// </summary>
    /// <param name="right">Whether the model is right.</param>
    /// <returns>1 when right, else 0.</returns>
    private static int Flag(bool right) => right ? 1 : 0;

    /// <summary>
    ///     Measures a model on the rows: each sentence is classified once.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="rows">The gold rows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcomes in the order of the rows.</returns>
    public static async Task<List<Gold2Scored>> MeasureAsync(IWordSenseModel model, IReadOnlyList<Gold2Row> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        var cache = new Dictionary<string, ClassifyResult[]>(StringComparer.Ordinal);
        var scored = new List<Gold2Scored>(rows.Count);
        foreach (var row in rows)
        {
            if (!cache.TryGetValue(row.Token.SentenceId, out var results))
            {
                results = await model.ClassifyAsync(row.Token.Words, cancellationToken).ConfigureAwait(false);
                cache[row.Token.SentenceId] = results;
            }

            scored.Add(Score(row, results[row.Token.Target]));
        }

        return scored;
    }

    /// <summary>
    ///     Scores a baseline that is told the gold lemma and part of speech and answers a sense by a fixed rule.
    /// </summary>
    /// <param name="rows">The gold rows.</param>
    /// <param name="mostFrequent">True for the sense with the highest WordNet 3.0 tag count (inventory order breaks ties); false for the first sense in inventory order.</param>
    /// <returns>The outcomes; only the sense measures apply.</returns>
    public static List<Gold2Scored> Baseline(IReadOnlyList<Gold2Row> rows, bool mostFrequent)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var scored = new List<Gold2Scored>(rows.Count);
        foreach (var row in rows)
        {
            var sense = -1;
            if (row.Sense is not ("-" or "none") && Gold2.TryParsePartOfSpeech(row.Pos, out var pos))
            {
                var usage = WordLexicon.Current.UsageOf(row.Lemma, pos);
                if (usage.Length > 0)
                {
                    var best = 0;
                    for (var i = 1; mostFrequent && i < usage.Length; i++)
                    {
                        best = usage[i].TagCount > usage[best].TagCount ? i : best;
                    }

                    sense = Flag(string.Equals(usage[best].SenseId, row.Sense, StringComparison.Ordinal));
                }
            }

            scored.Add(new Gold2Scored(row.Token.Id, -1, -1, -1, sense, sense));
        }

        return scored;
    }

    /// <summary>
    ///     Counts the scored tokens and the right ones of a stratum on a measure.
    /// </summary>
    /// <param name="rows">The gold rows.</param>
    /// <param name="scored">The outcomes in the order of the rows.</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <param name="stratum">The stratum.</param>
    /// <param name="measure">The index into <see cref="Gold2Scored.Measures"/>.</param>
    /// <returns>The number of tokens the measure applies to and the number the model got right.</returns>
    public static (int Tokens, int Correct) Tally(IReadOnlyList<Gold2Row> rows, IReadOnlyList<Gold2Scored> scored, IReadOnlySet<string> trained, string stratum, int measure)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scored);
        int n = 0, right = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var v = scored[i].Value(measure);
            if (v >= 0 && StrataOf(rows[i], trained).Contains(stratum))
            {
                n++;
                right += v;
            }
        }

        return (n, right);
    }

    /// <summary>
    ///     Compares two models on the same rows.
    /// </summary>
    /// <param name="rows">The gold rows.</param>
    /// <param name="first">The outcomes of the first model.</param>
    /// <param name="second">The outcomes of the second model.</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <param name="minimum">The least number of tokens of a stratum that is compared.</param>
    /// <returns>One comparison for each stratum and measure with at least <paramref name="minimum"/> tokens.</returns>
    public static List<Gold2Comparison> Compare(IReadOnlyList<Gold2Row> rows, IReadOnlyList<Gold2Scored> first, IReadOnlyList<Gold2Scored> second, IReadOnlySet<string> trained, int minimum = 20)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var strata = rows.Select(r => StrataOf(r, trained)).ToList();
        var documents = rows.Select(r => r.Token.DocId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var documentIndex = documents.Select((d, i) => (d, i)).ToDictionary(p => p.d, p => p.i, StringComparer.Ordinal);
        var comparisons = new List<Gold2Comparison>();
        foreach (var stratum in Strata)
        {
            for (var measure = 0; measure < Gold2Scored.Measures.Count; measure++)
            {
                var sums = new (int N, int Diff)[documents.Count];
                int n = 0, a = 0, b = 0, wins = 0, losses = 0;
                for (var i = 0; i < rows.Count; i++)
                {
                    int x = first[i].Value(measure), y = second[i].Value(measure);
                    if (x < 0 || y < 0 || !strata[i].Contains(stratum))
                    {
                        continue;
                    }

                    n++;
                    a += x;
                    b += y;
                    wins += y > x ? 1 : 0;
                    losses += x > y ? 1 : 0;
                    var d = documentIndex[rows[i].Token.DocId];
                    sums[d] = (sums[d].N + 1, sums[d].Diff + y - x);
                }

                if (n >= minimum)
                {
                    var (low, high) = BootstrapInterval(sums, measure * 1000 + Strata.ToList().IndexOf(stratum));
                    comparisons.Add(new Gold2Comparison(stratum, Gold2Scored.Measures[measure], n, a, b, wins, losses, Gold2Power.ExactMcNemar(wins, losses), low, high));
                }
            }
        }

        return comparisons;
    }

    /// <summary>
    ///     Finds the 95% interval of the difference of two models by resampling whole documents.
    /// </summary>
    /// <remarks>
    ///     Only the documents that contribute at least one token to the stratum and measure take part: a document without a
    ///     token is no cluster of the sample, and resampling it would add empty draws that bias the interval (and give a
    ///     resample with no token at all).
    /// </remarks>
    /// <param name="sums">For each document, the tokens it contributes and the sum of the differences (second minus first) over them.</param>
    /// <param name="seed">The seed of the pseudo-random generator, so the interval is reproducible.</param>
    /// <returns>The 2.5th and the 97.5th percentile of the resampled differences, as shares; (0, 0) when no document contributes.</returns>
    internal static (double Low, double High) BootstrapInterval((int N, int Diff)[] sums, int seed)
    {
        var clusters = sums.Where(s => s.N > 0).ToArray();
        if (clusters.Length == 0)
        {
            return (0, 0);
        }

#pragma warning disable S2245 // a seeded pseudo-random generator makes the bootstrap reproducible; nothing here is a secret
        var random = new Random(seed + 20261029);
#pragma warning restore S2245
        var differences = new double[Resamples];
        for (var r = 0; r < Resamples; r++)
        {
            long n = 0, diff = 0;
            for (var k = 0; k < clusters.Length; k++)
            {
                var (dn, dd) = clusters[random.Next(clusters.Length)];
                n += dn;
                diff += dd;
            }

            differences[r] = (double)diff / n;
        }

        Array.Sort(differences);
        return (differences[(int)(0.025 * Resamples)], differences[(int)(0.975 * Resamples) - 1]);
    }

    /// <summary>
    ///     Renders the outcomes of a model as a file that <see cref="ReadOutcomes"/> reads.
    /// </summary>
    /// <param name="scored">The outcomes.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderOutcomes(IEnumerable<Gold2Scored> scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        var text = new StringBuilder("id\tpos\tlemma\ttense\tsense\tsense_on_analysis\n");
        foreach (var s in scored)
        {
            text.Append(string.Join('\t', s.Id, Cell(s.Pos), Cell(s.Lemma), Cell(s.Tense), Cell(s.Sense), Cell(s.SenseOnAnalysis))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Writes an outcome as a cell of the outcomes file.
    /// </summary>
    /// <param name="value">1, 0 or -1 (the measure does not apply).</param>
    /// <returns>The value, or a dash for -1.</returns>
    private static string Cell(int value) => value < 0 ? "-" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Reads the outcomes of a model.
    /// </summary>
    /// <param name="path">The file written by <see cref="RenderOutcomes"/>.</param>
    /// <returns>The outcomes by token id.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static Dictionary<string, Gold2Scored> ReadOutcomes(string path)
    {
        var map = new Dictionary<string, Gold2Scored>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false)).Skip(1))
        {
            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length != 6)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}: expected 6 tab-separated columns in '{line}'.");
            }

            map[c[0]] = new Gold2Scored(c[0], Parse(c[1]), Parse(c[2]), Parse(c[3]), Parse(c[4]), Parse(c[5]));
        }

        return map;
    }

    /// <summary>
    ///     Reads a cell of the outcomes file.
    /// </summary>
    /// <param name="cell">The cell: <c>1</c>, <c>0</c> or a dash.</param>
    /// <returns>1, 0 or -1.</returns>
    /// <exception cref="InvalidDataException">Thrown when the cell is none of the three.</exception>
    private static int Parse(string cell)
    {
        if (cell == "-")
        {
            return -1;
        }

        return cell switch
        {
            "1" => 1,
            "0" => 0,
            _ => throw new InvalidDataException($"'{cell}' is no outcome."),
        };
    }

    /// <summary>
    ///     Renders how the strata of the draw compare with the strata computed now: the flag <c>TR</c> or <c>U</c> of a token
    ///     is frozen in the manifest, while the sense strata of the results are computed from the trained lemmas.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="rows">The gold rows of the split.</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderStrata(string split, IReadOnlyList<Gold2Row> rows, IReadOnlySet<string> trained)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(trained);
        var inv = CultureInfo.InvariantCulture;
        var drawnTrained = rows.Where(r => r.Token.Strata.Split(',').Contains(Gold2Strata.Trained)).ToList();
        var drawnUntrained = rows.Where(r => r.Token.Strata.Split(',').Contains(Gold2Strata.Untrained)).ToList();
        var movedDown = drawnTrained.Count(r => Gold2Strata.SenseFlag(r.Token.Form, trained) == Gold2Strata.Untrained);
        var movedUp = drawnUntrained.Count(r => Gold2Strata.SenseFlag(r.Token.Form, trained) == Gold2Strata.Trained);
        var goldTrained = rows.Count(r => trained.Contains(r.Lemma));
        var text = new StringBuilder();
        text.Append("##### Strata of the draw against the strata now (").Append(split).Append(")\n\n");
        text.Append("| Drawn as `TR` | `TR` now | Moved to `U` | Drawn as `U` | `U` now `TR` | Gold lemma trained | Gold lemma untrained |\n| ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        text.Append("| ").Append(drawnTrained.Count.ToString("N0", inv)).Append(" | ").Append((drawnTrained.Count - movedDown).ToString("N0", inv))
            .Append(" | ").Append(movedDown.ToString("N0", inv)).Append(" | ").Append(drawnUntrained.Count.ToString("N0", inv))
            .Append(" | ").Append(movedUp.ToString("N0", inv)).Append(" | ").Append(goldTrained.ToString("N0", inv))
            .Append(" | ").Append((rows.Count - goldTrained).ToString("N0", inv)).Append(" |\n\n");
        return text.ToString();
    }

    /// <summary>
    ///     Renders the smallest improvement that the scored tokens of each planned measure detect (paired comparison, the
    ///     discordance and the level of <see cref="Gold2Power"/>).
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="rows">The gold rows of the split.</param>
    /// <param name="scored">The outcomes of any model (which tokens are scored does not depend on the model).</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderPower(string split, IReadOnlyList<Gold2Row> rows, IReadOnlyList<Gold2Scored> scored, IReadOnlySet<string> trained)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scored);
        var inv = CultureInfo.InvariantCulture;

        // The stratum and the measure of each planned measure of Gold2Power.Measures, in its order
        var cells = new (string Stratum, int Measure)[] { (All, 0), ("untrained lemma", 3), ("trained lemma", 3), ("tense", 2), ("lemma-ambiguous", 1) };
        var text = new StringBuilder();
        text.Append("##### Smallest improvement the scored tokens detect (").Append(split).Append(")\n\n");
        text.Append("| Measure | Planned improvement | Discordance | Tokens scored | Improvement detected |\n| --- | ---: | ---: | ---: | ---: |\n");
        for (var i = 0; i < Gold2Power.Measures.Count && i < cells.Length; i++)
        {
            var m = Gold2Power.Measures[i];
            var (n, _) = Tally(rows, scored, trained, cells[i].Stratum, cells[i].Measure);
            text.Append("| ").Append(m.Name).Append(" | ").Append((m.Mdi * 100).ToString("0.0", inv)).Append(" points | ").Append((m.Discordance * 100).ToString("0.0", inv))
                .Append("% | ").Append(n.ToString("N0", inv)).Append(" | ")
                .Append(n == 0 ? "n/a" : (Gold2Power.MinimumDetectable(n, m.Discordance) * 100).ToString("0.00", inv) + " points").Append(" |\n");
        }

        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    ///     Renders the accuracy of the models of one split: a table of the strata by measure for each model.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="rows">The gold rows of the split.</param>
    /// <param name="results">The outcomes of each model (a baseline is a model that answers only the sense).</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(string split, IReadOnlyList<Gold2Row> rows, IReadOnlyList<(string Model, IReadOnlyList<Gold2Scored> Scored)> results, IReadOnlySet<string> trained)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(results);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("#### ").Append(split).Append(" split (").Append(rows.Count.ToString("N0", inv)).Append(" tokens in ").Append(rows.Select(r => r.Token.DocId).Distinct(StringComparer.Ordinal).Count().ToString("N0", inv)).Append(" documents; ")
            .Append(rows.Count(r => r.Sense == "none").ToString("N0", inv)).Append(" with no defensible single sense, left out of the sense columns)\n\n");
        foreach (var (model, scored) in results)
        {
            text.Append("##### ").Append(model).Append("\n\n| Stratum | ");
            text.Append(string.Join(" | ", Gold2Scored.Measures)).Append(" |\n| --- |").Append(string.Concat(Enumerable.Repeat(" ---: |", Gold2Scored.Measures.Count))).Append('\n');
            foreach (var stratum in Strata)
            {
                text.Append("| ").Append(stratum);
                for (var m = 0; m < Gold2Scored.Measures.Count; m++)
                {
                    var (n, right) = Tally(rows, scored, trained, stratum, m);
                    text.Append(" | ").Append(n == 0 ? "n/a" : EvaluationResult.Percent(right, n) + " (" + right.ToString("N0", inv) + "/" + n.ToString("N0", inv) + ")");
                }

                text.Append(" |\n");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders a paired comparison as Markdown.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="firstName">The name of the first model.</param>
    /// <param name="secondName">The name of the second model.</param>
    /// <param name="comparisons">The comparisons.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderComparison(string split, string firstName, string secondName, IReadOnlyList<Gold2Comparison> comparisons)
    {
        ArgumentNullException.ThrowIfNull(comparisons);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("##### ").Append(split).Append(": ").Append(secondName).Append(" against ").Append(firstName).Append("\n\n");
        text.Append("| Stratum | Measure | Tokens | ").Append(firstName).Append(" | ").Append(secondName).Append(" | Difference (points) | 95% interval (document bootstrap) | Only ").Append(secondName).Append(" right | Only ").Append(firstName).Append(" right | p (exact McNemar) |\n");
        text.Append("| --- | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: |\n");
        foreach (var c in comparisons)
        {
            text.Append("| ").Append(c.Stratum).Append(" | ").Append(c.Measure)
                .Append(" | ").Append(c.Tokens.ToString("N0", inv))
                .Append(" | ").Append(EvaluationResult.Percent(c.CorrectFirst, c.Tokens))
                .Append(" | ").Append(EvaluationResult.Percent(c.CorrectSecond, c.Tokens))
                .Append(" | ").Append((100.0 * (c.CorrectSecond - c.CorrectFirst) / c.Tokens).ToString("+0.00;-0.00;0.00", inv))
                .Append(" | ").Append((100 * c.Low).ToString("+0.00;-0.00;0.00", inv)).Append(" to ").Append((100 * c.High).ToString("+0.00;-0.00;0.00", inv))
                .Append(" | ").Append(c.Wins.ToString("N0", inv))
                .Append(" | ").Append(c.Losses.ToString("N0", inv))
                .Append(" | ").Append(c.PValue < 0.0001 ? "<0.0001" : c.PValue.ToString("0.0000", inv)).Append(" |\n");
        }

        text.Append('\n');
        return text.ToString();
    }
}
