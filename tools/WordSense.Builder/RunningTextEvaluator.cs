using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Measures the part-of-speech accuracy of the models on running text, the same sets for every model.
/// </summary>
/// <remarks>
///     <para>
///     The sets are the <c>test</c> split of the representative part of the open corpus (the headline: random text that
///     no count, threshold or weight has seen), the test file of the English EWT treebank mapped to the conventions of
///     the lexicon (an evaluation source that is never committed: <c>tools/open-corpus/scripts/prepare_tagger_eval.py</c>
///     verifies the pinned SHA-256 of the downloaded files, applies the published convention map and writes the gold
///     file that this class reads) and the test split of the targeted part, a challenge set of simplified-technical-English
///     sentences that is reported on its own line. The development file of the treebank is the one to select settings
///     with and is never the file measured here.
///     </para>
///     <para>
///     The predicted part of speech is that of the selected analysis of each model, as an application sees it (the
///     encoder models fall back to the core tagger for the words they have no weights for); the scoring rule is that
///     of <see cref="TaggerEvaluator"/>. Tokens that the convention or the corpus tags as punctuation or other (numbers,
///     symbols, foreign words, particles other than <c>to</c>) are excluded and counted.
///     </para>
/// </remarks>
internal static class RunningTextEvaluator
{
    /// <summary>The name of the headline set.</summary>
    public const string OpenSetName = "open corpus test (representative)";

    /// <summary>The name of the treebank set.</summary>
    public const string TreebankSetName = "UD-EWT test (convention-mapped)";

    /// <summary>The name of the challenge set.</summary>
    public const string ChallengeSetName = "STE challenge set (targeted test)";

    /// <summary>
    ///     Gets the default place of the gold file of the treebank test file, written by <c>prepare_tagger_eval.py</c>.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The path of <c>ud_test.tsv</c>; the development file <c>ud_dev.tsv</c> next to it is never the default.</returns>
    public static string DefaultGold(string repository) =>
        Path.Join(repository, "tools", "open-corpus", "work", "tagger-eval", "ud_test.tsv");

    /// <summary>
    ///     Loads the sentences of the evaluation sets.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split">The split of the representative part (<c>test</c> for the headline).</param>
    /// <param name="gold">The gold file of the treebank test file.</param>
    /// <returns>The sets by name (the treebank set only when its file exists, the challenge set only when the corpus has it) and the notes of the sets that were left out.</returns>
    public static (List<(string Name, IReadOnlyList<TaggedSentence> Sentences)> Sets, List<string> Notes) Load(string corpus, string split, string gold)
    {
        var sets = new List<(string, IReadOnlyList<TaggedSentence>)>
        {
            (OpenSetName, [.. OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, split).Select(OpenCorpus.ToTagged)]),
        };
        var notes = new List<string>();
        if (File.Exists(gold))
        {
            sets.Add((TreebankSetName, TaggerEvaluator.ReadGold(gold)));
        }
        else
        {
            notes.Add($"{TreebankSetName}: not measured, the gold file '{gold}' does not exist (download the treebank test file for evaluation and run tools/open-corpus/scripts/prepare_tagger_eval.py).");
        }

        var challenge = OpenCorpus.DataFile(corpus, OpenCorpus.Targeted, "test");
        if (File.Exists(challenge))
        {
            sets.Add((ChallengeSetName, [.. OpenCorpus.ReadFile(challenge).Select(OpenCorpus.ToTagged)]));
        }
        else
        {
            notes.Add($"{ChallengeSetName}: not measured, '{challenge}' does not exist.");
        }

        return (sets, notes);
    }

    /// <summary>
    ///     Measures a model on the sets.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="sets">The sets.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score of every set, in order.</returns>
    public static async Task<List<TaggerScore>> RunAsync(IWordSenseModel model, IReadOnlyList<(string Name, IReadOnlyList<TaggedSentence> Sentences)> sets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sets);
        var scores = new List<TaggerScore>();
        foreach (var (name, sentences) in sets)
        {
            scores.Add(await TaggerEvaluator.EvaluateAsync(name, model, sentences, cancellationToken).ConfigureAwait(false));
        }

        return scores;
    }

    /// <summary>
    ///     Gets the cells of the model comparison table that these scores fill.
    /// </summary>
    /// <param name="scores">The scores of one model.</param>
    /// <returns>The cell of every set that was measured, by column name.</returns>
    public static Dictionary<string, string> TableCells(IReadOnlyList<TaggerScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (set, column) in new[] { (OpenSetName, ModelTable.RunningColumn), (TreebankSetName, ModelTable.TreebankColumn), (ChallengeSetName, ModelTable.ChallengeColumn) })
        {
            if (scores.FirstOrDefault(s => s.Name == set) is { } score)
            {
                cells[column] = Cell(score);
            }
        }

        return cells;
    }

    /// <summary>
    ///     Renders the scores of the models as Markdown: the all-token and ambiguous-token accuracy of every set.
    /// </summary>
    /// <param name="scoresByModel">The scores of each model.</param>
    /// <param name="notes">The notes of the sets that were left out.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(IReadOnlyList<(string Model, List<TaggerScore> Scores)> scoresByModel, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(scoresByModel);
        ArgumentNullException.ThrowIfNull(notes);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### Part of speech on running text\n\n");
        text.Append("| Model | Set | Sentences | Scored tokens | Accuracy (all tokens) | Ambiguous tokens | Accuracy (ambiguous tokens) | Not answered | Excluded |\n");
        text.Append("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var (model, scores) in scoresByModel)
        {
            foreach (var s in scores)
            {
                text.Append("| ").Append(model).Append(" | ").Append(s.Name)
                    .Append(" | ").Append(s.Sentences.ToString("N0", inv))
                    .Append(" | ").Append(s.Scored.ToString("N0", inv))
                    .Append(" | ").Append(EvaluationResult.Percent(s.Correct, s.Scored))
                    .Append(" | ").Append(s.Ambiguous.ToString("N0", inv))
                    .Append(" | ").Append(EvaluationResult.Percent(s.AmbiguousCorrect, s.Ambiguous))
                    .Append(" | ").Append(s.Unanswered.ToString("N0", inv))
                    .Append(" | ").Append(s.Excluded.ToString("N0", inv)).Append(" |\n");
            }
        }

        text.Append('\n');
        foreach (var note in notes)
        {
            text.Append(note).Append("\n\n");
        }

        text.Append("The open corpus test split is the headline; the treebank test file is mapped to the conventions of the lexicon\n");
        text.Append("(possessive determiners, the copula be, phrasal-verb particles, prepositions before gerunds) and the challenge set is\n");
        text.Append("small and chosen for its hard cases, so it is never merged into a headline figure. Excluded tokens are punctuation and\n");
        text.Append("the classes the lexicon has no part of speech for (numbers, symbols, foreign words, particles other than to).\n");
        return text.ToString();
    }

    private static string Cell(TaggerScore score) =>
        EvaluationResult.Percent(score.Correct, score.Scored) + " (ambiguous " + EvaluationResult.Percent(score.AmbiguousCorrect, score.Ambiguous) + ")";
}
