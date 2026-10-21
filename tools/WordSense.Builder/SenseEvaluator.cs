using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     What happened to the annotations of SemCor on their way to the evaluation set.
/// </summary>
/// <param name="Counts">The counts of the reader.</param>
/// <param name="SingleKey">The used words that have exactly one sense key (a word with several keys is left out).</param>
/// <param name="Mapped">The words whose WordNet 3.0 key is the key of a sense of the catalog.</param>
/// <param name="Trained">The mapped words whose lemma is a trained lemma (it has sentences in <c>corpus/anchors</c>): the evaluation set.</param>
/// <param name="Unmapped">The distinct keys that no sense of the catalog has, most frequent first (key and number of words).</param>
/// <param name="LeftOutSentences">The sentences left out because the same sentence is in a part of the corpus or an example sentence of Open English WordNet.</param>
internal sealed record SenseEvalFunnel(SemCorCounts Counts, int SingleKey, int Mapped, int Trained, IReadOnlyList<(string Key, int Count)> Unmapped, int LeftOutSentences = 0);

/// <summary>
///     The measurement of one model on the evaluation set.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Set">The counts at each level (see <see cref="SetResult"/>).</param>
/// <param name="GroupRows">The words whose gold sense is in a group.</param>
/// <param name="GroupTop1">The words among them whose most probable ranked sense is in the group of the gold sense.</param>
/// <param name="MostFrequentSense">The words whose gold sense is the first sense of its lemma and part of speech in the inventory order (the most-frequent-sense baseline, which knows the gold lemma and part of speech).</param>
internal sealed record SenseEvalModelResult(string Model, SetResult Set, int GroupRows, int GroupTop1, int MostFrequentSense);

/// <summary>
///     The independent sense evaluation: what a model does on SemCor.
/// </summary>
/// <param name="Funnel">How the annotations became the evaluation set.</param>
/// <param name="Models">The measurement of each model.</param>
internal sealed record SenseEvalResult(SenseEvalFunnel Funnel, IReadOnlyList<SenseEvalModelResult> Models)
{
    /// <summary>
    ///     Gets the cell of the model comparison table for a model: the sense share and precision, and the fine top-1.
    /// </summary>
    /// <param name="model">The measurement of the model.</param>
    /// <returns>The text.</returns>
    public static string TableCell(SenseEvalModelResult model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var set = model.Set;
        return EvaluationResult.ShareAtPrecision(set.Sense.Answered, set.Sense.Correct, set.Rows) + " (top-1 " + EvaluationResult.Percent(set.FineTop1, set.Rows) + ")";
    }

    /// <summary>
    ///     Renders the funnel and the measurements as Markdown.
    /// </summary>
    /// <returns>The text with line feeds.</returns>
    public string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var counts = Funnel.Counts;
        var text = new StringBuilder();
        text.Append("### Independent sense evaluation (SemCor 3.0)\n\n");
        text.Append("| Step | Words |\n| --- | ---: |\n");
        text.Append("| annotated words | ").Append(counts.Annotated.ToString("N0", inv)).Append(" |\n");
        text.Append("| sentences left out: also in a part of the corpus or an example of Open English WordNet | ").Append(Funnel.LeftOutSentences.ToString("N0", inv)).Append(" |\n");
        text.Append("| left out: multiword expressions | ").Append(counts.MultiWord.ToString("N0", inv)).Append(" |\n");
        text.Append("| left out: sense dropped by the mapping to WordNet 3.0 | ").Append(counts.MissingSense.ToString("N0", inv)).Append(" |\n");
        text.Append("| single words with an annotation | ").Append(counts.Used.ToString("N0", inv)).Append(" |\n");
        text.Append("| left out: more than one sense key | ").Append((counts.Used - Funnel.SingleKey).ToString("N0", inv)).Append(" |\n");
        text.Append("| key is the key of a sense of the catalog (mapping coverage ").Append(EvaluationResult.Percent(Funnel.Mapped, Funnel.SingleKey)).Append(") | ").Append(Funnel.Mapped.ToString("N0", inv)).Append(" |\n");
        text.Append("| lemma is trained (the evaluation set) | ").Append(Funnel.Trained.ToString("N0", inv)).Append(" |\n\n");
        if (Funnel.Unmapped.Count > 0)
        {
            text.Append("Most frequent keys without a sense in the catalog: ")
                .Append(string.Join(", ", Funnel.Unmapped.Take(10).Select(u => $"{u.Key} ({u.Count.ToString(inv)})"))).Append("\n\n");
        }

        text.Append("Share of the words answered at each level:\n\n");
        text.Append("| Model | Words | Sense | Group | Part of speech | None |\n| --- | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var m in Models)
        {
            var s = m.Set;
            text.Append("| ").Append(m.Model).Append(" | ").Append(s.Rows.ToString("N0", inv))
                .Append(" | ").Append(EvaluationResult.Percent(s.Sense.Answered, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(s.Group.Answered, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(s.PartOfSpeech.Answered, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(s.None, s.Rows)).Append(" |\n");
        }

        text.Append("\nPrecision at the answered level, and the accuracy of the ranked senses:\n\n");
        text.Append("| Model | Sense precision | Group precision | Part-of-speech precision | Correct at answered level | Fine top-1 | Group top-1 (words in a group) | Part of speech | Most-frequent-sense baseline |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var m in Models)
        {
            var s = m.Set;
            text.Append("| ").Append(m.Model)
                .Append(" | ").Append(EvaluationResult.Percent(s.Sense.Correct, s.Sense.Answered))
                .Append(" | ").Append(EvaluationResult.Percent(s.Group.Correct, s.Group.Answered))
                .Append(" | ").Append(EvaluationResult.Percent(s.PartOfSpeech.Correct, s.PartOfSpeech.Answered))
                .Append(" | ").Append(EvaluationResult.Percent(s.CorrectAtLevel, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(s.FineTop1, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(m.GroupTop1, m.GroupRows))
                .Append(" | ").Append(EvaluationResult.Percent(s.PosCorrect, s.Rows))
                .Append(" | ").Append(EvaluationResult.Percent(m.MostFrequentSense, s.Rows)).Append(" |\n");
        }

        text.Append("\nSemCor is human-annotated running text that no training or tuning step reads. The sense inventory order, and so the\n");
        text.Append("most-frequent-sense baseline, comes from WordNet 3.0 tag counts, which include the counts of SemCor itself: the baseline\n");
        text.Append("is therefore optimistic (it has seen the answers), and so is the quota tier of every sense; no SemCor sentence is\n");
        text.Append("in any training corpus. The words are the single-word annotations of the lemmas the models are trained for, so the\n");
        text.Append("figures say nothing about the other words.\n");
        return text.ToString();
    }
}

/// <summary>
///     Measures the models on SemCor, the independent sense evaluation.
/// </summary>
/// <remarks>
///     <para>
///     A SemCor word is an evaluation word when it is a single word with exactly one WordNet 3.0 sense key, the key is
///     the key of a sense of the catalog (<see cref="WordLexicon.TryGetSenseByKey"/>: the catalog keeps the Open English
///     WordNet keys, which are the WordNet 3.0 keys for every sense that the 2025 release did not change) and the lemma
///     of that sense is trained. Every sentence is classified once with its punctuation as separate words, and each
///     evaluation word is judged like a held-out row (see <see cref="Evaluator.Judge"/>): the answer is right at its own
///     level; the fine top-1 is the most probable ranked sense.
///     </para>
/// </remarks>
internal static class SenseEvaluator
{
    /// <summary>
    ///     Selects the evaluation words of the sentences.
    /// </summary>
    /// <param name="sentences">The SemCor sentences.</param>
    /// <param name="counts">The counts of the reader.</param>
    /// <param name="trainedLemmas">The trained lemmas.</param>
    /// <param name="sample">Keep every <paramref name="sample"/>-th sentence (1 keeps all).</param>
    /// <param name="excluded">The normalized sentences to leave out (see <see cref="Corpus.Normalize"/>), or null for none.</param>
    /// <returns>The sentences with at least one evaluation word, each with its evaluation rows, and the funnel.</returns>
    public static (List<(SemCorSentence Sentence, List<CorpusRow> Rows)> Selected, SenseEvalFunnel Funnel) Select(
        IReadOnlyList<SemCorSentence> sentences,
        SemCorCounts counts,
        IReadOnlySet<string> trainedLemmas,
        int sample = 1,
        IReadOnlySet<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(trainedLemmas);
        var selected = new List<(SemCorSentence, List<CorpusRow>)>();
        int single = 0, mapped = 0, trained = 0, leftOut = 0;
        var unmapped = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var s = 0; s < sentences.Count; s += Math.Max(1, sample))
        {
            var sentence = sentences[s];
            if (excluded is not null && excluded.Contains(Corpus.Normalize(string.Join(' ', sentence.Words))))
            {
                leftOut++;
                continue;
            }

            var rows = new List<CorpusRow>();
            foreach (var target in sentence.Targets.Where(t => t.SenseKeys.Count == 1))
            {
                single++;
                if (!WordLexicon.TryGetSenseByKey(target.SenseKeys[0], out var sense))
                {
                    unmapped[target.SenseKeys[0]] = unmapped.GetValueOrDefault(target.SenseKeys[0]) + 1;
                    continue;
                }

                mapped++;
                if (trainedLemmas.Contains(sense.Lemma))
                {
                    trained++;
                    rows.Add(new CorpusRow(sense.Lemma, sense.SenseId, sense.PartOfSpeech, string.Join(' ', sentence.Words), "semcor", sentence.Words, target.Index));
                }
            }

            if (rows.Count > 0)
            {
                selected.Add((sentence, rows));
            }
        }

        var top = unmapped.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
        return (selected, new SenseEvalFunnel(counts, single, mapped, trained, top, leftOut));
    }

    /// <summary>
    ///     Measures one model on the selected sentences.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="model">The model.</param>
    /// <param name="selected">The sentences with their evaluation rows.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The measurement.</returns>
    public static async Task<SenseEvalModelResult> MeasureAsync(
        string name,
        IWordSenseModel model,
        IReadOnlyList<(SemCorSentence Sentence, List<CorpusRow> Rows)> selected,
        TextWriter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selected);
        var verdicts = new List<Evaluator.Verdict>();
        int groupRows = 0, groupTop1 = 0, mfs = 0;
        var done = 0;
        foreach (var (sentence, rows) in selected)
        {
            var results = await model.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var verdict = Evaluator.Judge(row, results[row.TargetWordIndex]);
                verdicts.Add(verdict);
                WordLexicon.TryGetSense(row.SenseId, out var gold);
                if (gold?.GroupId is { } group)
                {
                    groupRows++;
                    groupTop1 += verdict.BestSense is { } best && WordLexicon.TryGetSense(best, out var info) && string.Equals(info.GroupId, group, StringComparison.Ordinal) ? 1 : 0;
                }

                var inventory = WordLexicon.SensesOf(row.Lemma, row.PartOfSpeech);
                mfs += inventory.Length > 0 && string.Equals(inventory[0].SenseId, row.SenseId, StringComparison.Ordinal) ? 1 : 0;
            }

            done++;
            if (done % 2000 == 0)
            {
                progress.WriteLine($"{name}: {done.ToString(CultureInfo.InvariantCulture)} of {selected.Count.ToString(CultureInfo.InvariantCulture)} sentences.");
            }
        }

        return new SenseEvalModelResult(name, Evaluator.Summarize("semcor", verdicts), groupRows, groupTop1, mfs);
    }
}
