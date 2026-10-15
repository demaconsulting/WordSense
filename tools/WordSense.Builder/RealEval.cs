using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The real-text evaluation: imports judged evaluation sentences and measures the embedded table on real text.
/// </summary>
internal static class RealEval
{
    /// <summary>
    ///     The file of the real evaluation sentences, relative to the corpus directory.
    /// </summary>
    public const string EvalFile = "realtext/eval-seed.tsv";

    /// <summary>
    ///     Imports judged evaluation candidates into <c>corpus/realtext/eval-seed.tsv</c> and its provenance.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="candidates">The harvested candidates.</param>
    /// <param name="judgements">The merged judgements by row id.</param>
    /// <param name="perSense">The maximum number of sentences per sense.</param>
    /// <returns>A summary of the rows kept per sense.</returns>
    public static string ImportEval(string corpus, IReadOnlyList<Candidate> candidates, IReadOnlyDictionary<string, SenseJudgement> judgements, int perSense)
    {
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var kept = new List<Candidate>();
        var summary = new StringBuilder("lemma\tsense\tkept\ttatoeba\tgutenberg\n");
        var accepted = candidates.GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First())
            .Where(c => judgements.TryGetValue(c.Id, out var j) && inventory.Contains(c.Lemma, j.Final)).ToList();
        foreach (var group in accepted.GroupBy(c => (c.Lemma, Sense: judgements[c.Id].Final)).OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.Sense, StringComparer.Ordinal))
        {
            var tatoeba = group.Where(c => c.Source != "gutenberg").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var gutenberg = group.Where(c => c.Source == "gutenberg").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var chosen = new List<Candidate>();
            for (var i = 0; chosen.Count < perSense && (i < tatoeba.Count || i < gutenberg.Count); i++)
            {
                if (i < tatoeba.Count && chosen.Count < perSense)
                {
                    chosen.Add(tatoeba[i]);
                }

                if (i < gutenberg.Count && chosen.Count < perSense)
                {
                    chosen.Add(gutenberg[i]);
                }
            }

            kept.AddRange(chosen);
            summary.Append($"{group.Key.Lemma}\t{group.Key.Sense}\t{chosen.Count}\t{chosen.Count(c => c.Source != "gutenberg")}\t{chosen.Count(c => c.Source == "gutenberg")}\n");
        }

        var text = new StringBuilder();
        var provenance = new List<ProvenanceRow>();
        foreach (var c in kept.OrderBy(c => c.Lemma, StringComparer.Ordinal).ThenBy(c => judgements[c.Id].Final, StringComparer.Ordinal).ThenBy(c => c.Sentence, StringComparer.Ordinal))
        {
            var sense = judgements[c.Id].Final;
            text.Append($"{c.Lemma}\t{sense}\t{c.Sentence}\n");
            provenance.Add(new ProvenanceRow(c.Id, c.Lemma, sense, "eval", c.Source, c.Licence, c.SourceKey, c.Url, c.Contributor, "real"));
        }

        Directory.CreateDirectory(Path.Combine(corpus, "realtext"));
        File.WriteAllText(Path.Combine(corpus, EvalFile), text.ToString(), new UTF8Encoding(false));
        RealImport.WriteProvenance(Path.Combine(corpus, "provenance", "realtext-eval.tsv"), provenance);
        return summary.ToString();
    }

    /// <summary>
    ///     Measures the embedded table on the real sentences and on the project-authored holdout of the same lemmas.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="modelDirectory">The verified model directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Markdown report.</returns>
    public static async Task<string> RunAsync(string corpus, string modelDirectory, CancellationToken cancellationToken)
    {
        using var classifier = await WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = modelDirectory }, cancellationToken).ConfigureAwait(false);
        var provenance = new Dictionary<string, ProvenanceRow>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(corpus, "provenance"), "*.tsv"))
        {
            foreach (var row in RealImport.ReadProvenance(file))
            {
                provenance[row.Id] = row;
            }
        }

        var realEval = Corpus.ReadSentences(Path.Combine(corpus, EvalFile));
        var restoredHoldout = Corpus.ReadSentences(Path.Combine(corpus, "holdout", RealPlan.RestoreFile));
        var realRestored = restoredHoldout.Where(r => Origin(provenance, r) == "real").ToList();
        var llmRestored = restoredHoldout.Where(r => Origin(provenance, r) != "real").ToList();
        var lemmas = realEval.Concat(realRestored).Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        var llmHoldout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"))
            .Where(r => lemmas.Contains(r.Lemma) && r.Source != RealPlan.RestoreFile).Concat(llmRestored).ToList();
        var evalLemmas = realEval.Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        var llmSeed = llmHoldout.Where(r => evalLemmas.Contains(r.Lemma)).ToList();
        var restoredLemmas = realRestored.Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        var llmRestoredLemmas = llmHoldout.Where(r => restoredLemmas.Contains(r.Lemma)).ToList();

        var scored = new Dictionary<string, List<Scored>>(StringComparer.Ordinal)
        {
            ["real-eval"] = await ScoreAsync(classifier, realEval, provenance, cancellationToken).ConfigureAwait(false),
            ["llm-holdout-same-lemmas"] = await ScoreAsync(classifier, llmSeed, provenance, cancellationToken).ConfigureAwait(false),
            ["real-restored"] = await ScoreAsync(classifier, realRestored, provenance, cancellationToken).ConfigureAwait(false),
            ["llm-holdout-restored-lemmas"] = await ScoreAsync(classifier, llmRestoredLemmas, provenance, cancellationToken).ConfigureAwait(false)
        };
        return Render(scored);
    }

    private static string Origin(Dictionary<string, ProvenanceRow> provenance, CorpusRow row) =>
        provenance.TryGetValue(PosAudit.RowId(row.Lemma, row.Sentence), out var p) ? p.Origin : "llm-corpus";

    private static async Task<List<Scored>> ScoreAsync(WordSenseClassifier classifier, IReadOnlyList<CorpusRow> rows, Dictionary<string, ProvenanceRow> provenance, CancellationToken token)
    {
        var result = new List<Scored>();
        foreach (var row in rows)
        {
            var senses = classifier.GetSenses(row.Lemma);
            var gold = senses.FirstOrDefault(s => s.Sense == row.Sense);
            if (gold is null)
            {
                continue;
            }

            var detail = (await classifier.ClassifyDetailAsync(row.Words, token).ConfigureAwait(false))[row.TargetWordIndex];
            if (detail is null)
            {
                continue;
            }

            var predicted = detail.Value.Sense;
            var source = provenance.TryGetValue(PosAudit.RowId(row.Lemma, row.Sentence), out var p) ? p.Source : "llm-corpus";
            result.Add(new Scored(row.Lemma, row.Sense, predicted.Sense, source, predicted.Sense == row.Sense, predicted.Group == gold.Group, !detail.Value.Abstained, row.Sentence));
        }

        return result;
    }

    private static string Render(Dictionary<string, List<Scored>> scored)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("## Summary").AppendLine();
        text.AppendLine("| Set | Rows | Best sense | Group | Answered | Answered accuracy |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (name, rows) in scored)
        {
            text.AppendLine(Line(name, rows, inv));
        }

        text.AppendLine().AppendLine("## By Source").AppendLine();
        text.AppendLine("| Set and source | Rows | Best sense | Group | Answered | Answered accuracy |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (name, rows) in scored.Where(s => s.Key.StartsWith("real", StringComparison.Ordinal)))
        {
            foreach (var group in rows.GroupBy(r => r.Source).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                text.AppendLine(Line($"{name} / {group.Key}", group.ToList(), inv));
            }
        }

        foreach (var (name, rows) in scored.Where(s => s.Key.StartsWith("real", StringComparison.Ordinal)))
        {
            text.AppendLine().AppendLine($"## Per Sense ({name})").AppendLine();
            text.AppendLine("| Lemma | Sense | Rows | Best sense | Group | Answered | Answered accuracy |");
            text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var group in rows.GroupBy(r => (r.Lemma, r.Gold)).OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.Gold, StringComparer.Ordinal))
            {
                text.AppendLine(Line($"{group.Key.Lemma} | {group.Key.Gold}", group.ToList(), inv));
            }
        }

        text.AppendLine().AppendLine("## Errors (real sets)").AppendLine();
        text.AppendLine("| Set | Gold | Predicted | Sentence |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var (name, rows) in scored.Where(s => s.Key.StartsWith("real", StringComparison.Ordinal)))
        {
            foreach (var r in rows.Where(r => !r.Correct).OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sentence, StringComparer.Ordinal))
            {
                text.AppendLine($"| {name} | {r.Lemma}:{r.Gold} | {r.Predicted} | {r.Sentence.Replace("|", "/", StringComparison.Ordinal)} |");
            }
        }

        return text.ToString();
    }

    private static string Line(string name, IReadOnlyList<Scored> rows, CultureInfo inv)
    {
        string Pct(int part, int whole) => whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", inv) + " %";
        var answered = rows.Where(r => r.Answered).ToList();
        return $"| {name} | {rows.Count} | {Pct(rows.Count(r => r.Correct), rows.Count)} | {Pct(rows.Count(r => r.GroupCorrect), rows.Count)} | {Pct(answered.Count, rows.Count)} | {Pct(answered.Count(r => r.Correct), answered.Count)} |";
    }

    private sealed record Scored(string Lemma, string Gold, string Predicted, string Source, bool Correct, bool GroupCorrect, bool Answered, string Sentence);
}
