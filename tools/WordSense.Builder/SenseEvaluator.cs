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
/// <param name="UntrainedPolysemous">The mapped words whose lemma is not trained and has two or more senses (over all parts of speech): with the trained words, the all-words set.</param>
/// <param name="Monosemous">The mapped words whose lemma is not trained and has fewer than two senses: left out of the all-words set (every model answers them at the sense level).</param>
internal sealed record SenseEvalFunnel(SemCorCounts Counts, int SingleKey, int Mapped, int Trained, IReadOnlyList<(string Key, int Count)> Unmapped, int LeftOutSentences = 0, int UntrainedPolysemous = 0, int Monosemous = 0);

/// <summary>
///     The all-words measurement of one model: the words of untrained lemmas with two or more senses, and the two together with the trained words.
/// </summary>
/// <param name="Untrained">The counts at each level on the words of the untrained polysemous lemmas.</param>
/// <param name="All">The counts at each level on all words: the trained words and the untrained polysemous words.</param>
/// <param name="TagCountTrained">The trained words whose gold sense is the sense with the highest WordNet 3.0 tag count of its lemma and part of speech (ties: inventory order).</param>
/// <param name="TagCountUntrained">The same for the untrained polysemous words.</param>
internal sealed record SenseEvalAllWords(SetResult Untrained, SetResult All, int TagCountTrained, int TagCountUntrained);

/// <summary>
///     The measurement of one model on the evaluation set.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Set">The counts at each level (see <see cref="SetResult"/>).</param>
/// <param name="GroupRows">The words whose gold sense is in a group.</param>
/// <param name="GroupTop1">The words among them whose most probable ranked sense is in the group of the gold sense.</param>
/// <param name="MostFrequentSense">The words whose gold sense is the first sense of its lemma and part of speech in the inventory order (the most-frequent-sense baseline, which knows the gold lemma and part of speech).</param>
/// <param name="AllWords">The all-words measurement, or null when it was not made.</param>
internal sealed record SenseEvalModelResult(string Model, SetResult Set, int GroupRows, int GroupTop1, int MostFrequentSense, SenseEvalAllWords? AllWords = null);

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
        text.Append("| lemma is trained (the trained-lemma set) | ").Append(Funnel.Trained.ToString("N0", inv)).Append(" |\n");
        var allWords = Funnel.Trained + Funnel.UntrainedPolysemous;
        text.Append("| lemma is not trained and has two or more senses | ").Append(Funnel.UntrainedPolysemous.ToString("N0", inv)).Append(" |\n");
        text.Append("| the all-words set: trained lemma, or lemma with two or more senses (share of words of a trained lemma ").Append(EvaluationResult.Percent(Funnel.Trained, allWords)).Append(") | ").Append(allWords.ToString("N0", inv)).Append(" |\n");
        text.Append("| left out of the all-words set: lemma is not trained and has fewer than two senses | ").Append(Funnel.Monosemous.ToString("N0", inv)).Append(" |\n\n");
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

        text.Append("\nSemCor is human-annotated running text that no training or tuning step reads. The baseline is the first sense of the lemma and part\n");
        text.Append("of speech in Open English WordNet order (the inventory order). That order follows Princeton WordNet's frequency order (it matches the\n");
        text.Append("WordNet 3.0 top-count sense in 10,047 of the 10,062 blocks with a unique maximum), and those counts include the counts of SemCor\n");
        text.Append("itself: the baseline is therefore partly circular and optimistic (it has seen the answers), and so is the quota tier of every\n");
        text.Append("sense; no SemCor sentence is in any training corpus. The words of the two tables above are the single-word annotations of the\n");
        text.Append("lemmas the models are trained for; the figures of the next section cover the other polysemous words too.\n");
        if (Models.Any(m => m.AllWords is not null))
        {
            AppendAllWords(text, inv);
        }

        return text.ToString();
    }

    private void AppendAllWords(StringBuilder text, CultureInfo inv)
    {
        text.Append("\n### All words (trained lemmas and untrained lemmas with two or more senses)\n\n");
        text.Append("| Model | Set | Words | Sense answered | Sense precision | Part of speech answered | None | Fine top-1 | Most-frequent-sense (tag count) |\n");
        text.Append("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var m in Models.Where(m => m.AllWords is not null))
        {
            var a = m.AllWords!;
            AppendAllWordsRow(text, inv, m.Model, "trained", m.Set, a.TagCountTrained);
            AppendAllWordsRow(text, inv, m.Model, "untrained", a.Untrained, a.TagCountUntrained);
            AppendAllWordsRow(text, inv, m.Model, "all", a.All, a.TagCountTrained + a.TagCountUntrained);
        }

        text.Append("\nThe all-words set is every mapped single-word annotation of a lemma that is trained or has two or more senses in the catalog\n");
        text.Append("(over all parts of speech); untrained lemmas with fewer than two senses are left out because every model answers them at the sense\n");
        text.Append("level. A word without a ranked sense counts as a miss of the fine top-1, and a word answered at the part-of-speech level is not a\n");
        text.Append("sense answer. The trained rows are the words of the tables above. The most-frequent-sense baseline chooses, for the gold lemma\n");
        text.Append("and part of speech, the sense with the highest WordNet 3.0 tag count (ties: inventory order). Those tag counts are counted on SemCor\n");
        text.Append("itself, so on SemCor the baseline has seen the answers: it is an optimistic reference, not a competitor, and it is a lemma-level\n");
        text.Append("prior that the library does not ship for untrained lemmas, where the models carry no prior at all; the gap to the baseline there\n");
        text.Append("shows what such a prior could add, not a defect of the evaluation.\n");
    }

    private static void AppendAllWordsRow(StringBuilder text, CultureInfo inv, string model, string subset, SetResult s, int tagCount)
    {
        text.Append("| ").Append(model).Append(" | ").Append(subset).Append(" | ").Append(s.Rows.ToString("N0", inv))
            .Append(" | ").Append(EvaluationResult.Percent(s.Sense.Answered, s.Rows))
            .Append(" | ").Append(EvaluationResult.Percent(s.Sense.Correct, s.Sense.Answered))
            .Append(" | ").Append(EvaluationResult.Percent(s.PartOfSpeech.Answered, s.Rows))
            .Append(" | ").Append(EvaluationResult.Percent(s.None, s.Rows))
            .Append(" | ").Append(EvaluationResult.Percent(s.FineTop1, s.Rows))
            .Append(" | ").Append(EvaluationResult.Percent(tagCount, s.Rows)).Append(" |\n");
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
///     of that sense is trained (the trained-lemma set, the source of the model table cell) or, for the all-words measurement,
///     has two or more senses in the catalog over all parts of speech. Every sentence is classified once with its
///     punctuation as separate words, and each evaluation word is judged like a held-out row (see
///     <see cref="Evaluator.Judge"/>): the answer is right at its own level; the fine top-1 is the most probable ranked
///     sense, and a word without ranked senses is a miss.
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
    /// <returns>The sentences with at least one evaluation word, each with its trained-lemma rows and its other rows (words of untrained lemmas with two or more senses), and the funnel.</returns>
    public static (List<(SemCorSentence Sentence, List<CorpusRow> Rows, List<CorpusRow> Others)> Selected, SenseEvalFunnel Funnel) Select(
        IReadOnlyList<SemCorSentence> sentences,
        SemCorCounts counts,
        IReadOnlySet<string> trainedLemmas,
        int sample = 1,
        IReadOnlySet<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(trainedLemmas);
        var selected = new List<(SemCorSentence, List<CorpusRow>, List<CorpusRow>)>();
        int single = 0, mapped = 0, trained = 0, leftOut = 0, polysemous = 0, monosemous = 0;
        var unmapped = new Dictionary<string, int>(StringComparer.Ordinal);
        var polysemy = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (var s = 0; s < sentences.Count; s += Math.Max(1, sample))
        {
            var sentence = sentences[s];
            var joined = string.Join(' ', sentence.Words);
            if (excluded is not null && excluded.Contains(Corpus.Normalize(joined)))
            {
                leftOut++;
                continue;
            }

            var rows = new List<CorpusRow>();
            var others = new List<CorpusRow>();
            foreach (var target in sentence.Targets.Where(t => t.SenseKeys.Count == 1))
            {
                single++;
                if (!WordLexicon.TryGetSenseByKey(target.SenseKeys[0], out var sense))
                {
                    unmapped[target.SenseKeys[0]] = unmapped.GetValueOrDefault(target.SenseKeys[0]) + 1;
                    continue;
                }

                mapped++;
                var row = new CorpusRow(sense.Lemma, sense.SenseId, sense.PartOfSpeech, joined, "semcor", sentence.Words, target.Index);
                if (trainedLemmas.Contains(sense.Lemma))
                {
                    trained++;
                    rows.Add(row);
                }
                else if (IsPolysemous(sense.Lemma, polysemy))
                {
                    polysemous++;
                    others.Add(row);
                }
                else
                {
                    monosemous++;
                }
            }

            if (rows.Count > 0 || others.Count > 0)
            {
                selected.Add((sentence, rows, others));
            }
        }

        var top = unmapped.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
        return (selected, new SenseEvalFunnel(counts, single, mapped, trained, top, leftOut, polysemous, monosemous));
    }

    private static bool IsPolysemous(string lemma, Dictionary<string, bool> memo)
    {
        if (!memo.TryGetValue(lemma, out var result))
        {
            result = Enum.GetValues<PartOfSpeech>().Sum(pos => WordLexicon.SensesOf(lemma, pos).Length) >= 2;
            memo[lemma] = result;
        }

        return result;
    }

    /// <summary>
    ///     Tells whether the gold sense of a row has the highest WordNet 3.0 tag count among the senses of its lemma and part of speech.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="tagCounts">The tag count of each sense key (a missing key counts 0), or null for none.</param>
    /// <returns><see langword="true"/> when the first sense with the highest count (inventory order breaks ties) is the gold sense.</returns>
    private static bool IsTagCountBaselineRight(CorpusRow row, IReadOnlyDictionary<string, int>? tagCounts)
    {
        SenseInfo? best = null;
        var bestCount = -1;
        foreach (var sense in WordLexicon.SensesOf(row.Lemma, row.PartOfSpeech))
        {
            var count = sense.SenseKey is { } key && tagCounts is not null ? tagCounts.GetValueOrDefault(key) : 0;
            if (count > bestCount)
            {
                best = sense;
                bestCount = count;
            }
        }

        return best is not null && string.Equals(best.SenseId, row.SenseId, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Measures one model on the selected sentences.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="model">The model.</param>
    /// <param name="selected">The sentences with their trained-lemma rows and other rows.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="tagCounts">The WordNet 3.0 tag count of each sense key for the tag-count baseline of the all-words measurement, or null for none (the baseline is then the first sense in inventory order).</param>
    /// <returns>The measurement.</returns>
    public static async Task<SenseEvalModelResult> MeasureAsync(
        string name,
        IWordSenseModel model,
        IReadOnlyList<(SemCorSentence Sentence, List<CorpusRow> Rows, List<CorpusRow> Others)> selected,
        TextWriter progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, int>? tagCounts = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selected);
        var verdicts = new List<Evaluator.Verdict>();
        var otherVerdicts = new List<Evaluator.Verdict>();
        int groupRows = 0, groupTop1 = 0, mfs = 0, tagTrained = 0, tagUntrained = 0;
        var done = 0;
        foreach (var (sentence, rows, others) in selected)
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
                tagTrained += IsTagCountBaselineRight(row, tagCounts) ? 1 : 0;
            }

            foreach (var row in others)
            {
                otherVerdicts.Add(Evaluator.Judge(row, results[row.TargetWordIndex]));
                tagUntrained += IsTagCountBaselineRight(row, tagCounts) ? 1 : 0;
            }

            done++;
            if (done % 2000 == 0)
            {
                progress.WriteLine($"{name}: {done.ToString(CultureInfo.InvariantCulture)} of {selected.Count.ToString(CultureInfo.InvariantCulture)} sentences.");
            }
        }

        var allWords = new SenseEvalAllWords(Evaluator.Summarize("semcor-untrained", otherVerdicts), Evaluator.Summarize("semcor-all", verdicts.Concat(otherVerdicts)), tagTrained, tagUntrained);
        return new SenseEvalModelResult(name, Evaluator.Summarize("semcor", verdicts), groupRows, groupTop1, mfs, allWords);
    }
}
