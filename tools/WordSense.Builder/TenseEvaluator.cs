using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The tense accuracy of one model on one set of gold sentences for one group of same-spelling verbs: how the gold
///     past and non-past tokens were answered.
/// </summary>
/// <param name="Name">The name of the set.</param>
/// <param name="Group">The group of verbs (<see cref="TenseEvaluator.AllGroup"/>, <see cref="TenseEvaluator.SameLemmaGroup"/> or <see cref="TenseEvaluator.ReadGroup"/>).</param>
/// <param name="PastAsPast">Gold past tokens whose selected analysis is a past analysis.</param>
/// <param name="PastAsNonPast">Gold past tokens whose selected analysis is not a past analysis.</param>
/// <param name="NonPastAsPast">Gold non-past tokens whose selected analysis is a past analysis.</param>
/// <param name="NonPastAsNonPast">Gold non-past tokens whose selected analysis is not a past analysis.</param>
/// <param name="Unanswered">Tokens the model did not answer at all (counted as wrong, in none of the four cells).</param>
internal sealed record TenseScore(string Name, string Group, int PastAsPast, int PastAsNonPast, int NonPastAsPast, int NonPastAsNonPast, int Unanswered)
{
    /// <summary>
    ///     Gets the number of scored tokens.
    /// </summary>
    public int Tokens => PastAsPast + PastAsNonPast + NonPastAsPast + NonPastAsNonPast + Unanswered;

    /// <summary>
    ///     Gets the number of tokens whose selected analysis has the gold class.
    /// </summary>
    public int Correct => PastAsPast + NonPastAsNonPast;

    /// <summary>
    ///     Gets the number of gold past tokens.
    /// </summary>
    public int GoldPast => PastAsPast + PastAsNonPast;

    /// <summary>
    ///     Gets the number of tokens of the larger gold class: what always answering the more frequent class would get right.
    /// </summary>
    public int Majority => Math.Max(GoldPast, Tokens - GoldPast);

    /// <summary>
    ///     Adds the counts of another score of the same group, to pool sets.
    /// </summary>
    /// <param name="name">The name of the pooled set.</param>
    /// <param name="other">The other score.</param>
    /// <returns>The pooled score.</returns>
    public TenseScore Add(string name, TenseScore other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new TenseScore(name, Group, PastAsPast + other.PastAsPast, PastAsNonPast + other.PastAsNonPast, NonPastAsPast + other.NonPastAsPast, NonPastAsNonPast + other.NonPastAsNonPast, Unanswered + other.Unanswered);
    }
}

/// <summary>
///     Measures how well a model tells the tense of a verb whose past and non-past share a spelling (<c>put</c>, <c>cut</c>,
///     <c>set</c>, <c>read</c>, <c>come</c>): the question is the meaning of the word in its sentence, never its sound.
/// </summary>
/// <remarks>
///     <para>
///     A token is scored when the gold source calls it a verb and its surface form has, among its verb analyses in the
///     lexicon, both a past analysis (<see cref="BasicClassifier.IsPast"/>) and an analysis that is not past. The gold class is
///     past when the treebank tag is <c>VBD</c> or <c>VBN</c> (when the source has no treebank tag, when the feature
///     <c>Tense=Past</c> is present) and non-past otherwise (infinitive, present, imperative). The answered class is that
///     of the selected analysis of the model: past when it is a verb analysis with a past tense or a participle, non-past
///     for every other analysis, so a verb read as a noun is a wrong answer for a past token. Infinitive, present and
///     imperative are one class because the lexicon has one analysis for all three.
///     </para>
///     <para>
///     Three groups are reported: <see cref="AllGroup"/> (every such token), <see cref="SameLemmaGroup"/> (the forms whose
///     past and non-past analyses are of the same lemma, the ones a split inside a slot can decide: <c>put</c>, <c>cut</c>,
///     <c>read</c>; the other forms, such as <c>found</c> as the past of <c>find</c> or the infinitive of <c>found</c>,
///     are decided between two lemmas) and <see cref="ReadGroup"/> (the forms of the lemma <c>read</c> and its prefixed
///     compounds, which the lexicon spells as <c>Tense=Pres</c> and which are the textbook case). Only the sentences that
///     hold a scored token are classified, each on its own as <see cref="RunningTextEvaluator"/> does, so the result of
///     a token does not depend on which other sentences are measured.
///     </para>
/// </remarks>
internal static class TenseEvaluator
{
    /// <summary>The group of every same-spelling verb token.</summary>
    public const string AllGroup = "all same-spelling verbs";

    /// <summary>The group of the forms whose past and non-past analyses share a lemma.</summary>
    public const string SameLemmaGroup = "same lemma";

    /// <summary>The group of the forms of <c>read</c> and its compounds.</summary>
    public const string ReadGroup = "read family";

    /// <summary>The name of the open corpus test split.</summary>
    public const string OpenTestName = "open corpus test";

    /// <summary>The name of the open corpus development split (the split settings are chosen with).</summary>
    public const string OpenDevName = "open corpus dev";

    /// <summary>The name of the treebank test file.</summary>
    public const string TreebankTestName = "UD-EWT test";

    /// <summary>The name of the treebank development file (the file settings are chosen with).</summary>
    public const string TreebankDevName = "UD-EWT dev";

    /// <summary>The name of the treebank training file (never trained on: the models do not read the treebank).</summary>
    public const string TreebankTrainName = "UD-EWT train";

    /// <summary>The name of the three treebank files pooled.</summary>
    public const string TreebankPooledName = "UD-EWT train+dev+test pooled";

    private static readonly string[] Groups = [AllGroup, SameLemmaGroup, ReadGroup];


    /// <summary>
    ///     Tells whether a gold token is a past verb.
    /// </summary>
    /// <param name="gold">What the gold source says about the token.</param>
    /// <returns>True when the treebank tag is <c>VBD</c> or <c>VBN</c>; without a treebank tag, when the features have <c>Tense=Past</c>.</returns>
    public static bool IsGoldPast(GoldToken gold) =>
        gold.Xpos.Length > 0
            ? gold.Xpos is "VBD" or "VBN"
            : gold.Features.Split('|').Contains("Tense=Past", StringComparer.Ordinal);

    /// <summary>
    ///     Finds out whether a surface form is scored and in which groups.
    /// </summary>
    /// <param name="word">The token as written.</param>
    /// <returns>The groups of the form (empty when it has no past and no non-past verb analysis).</returns>
    public static IReadOnlyList<string> GroupsOf(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var key = BasicClassifier.Normalize(word);
        if (key.Length == 0 || !WordLexicon.TryLookup(key, out var analyses))
        {
            return [];
        }

        var verbs = analyses.Where(a => a.PartOfSpeech == PartOfSpeech.Verb).ToImmutableArray();
        if (!verbs.Any(BasicClassifier.IsPast) || verbs.All(BasicClassifier.IsPast))
        {
            return [];
        }

        var groups = new List<string> { AllGroup };
        var sameLemma = verbs.GroupBy(v => v.Lemma, StringComparer.Ordinal).Where(g => g.Any(BasicClassifier.IsPast) && g.Any(v => !BasicClassifier.IsPast(v))).Select(g => g.Key).ToList();
        if (sameLemma.Count > 0)
        {
            groups.Add(SameLemmaGroup);

            // The lemma read and its compounds (reread, misread, proofread); spread and its compounds only end in the same letters
            if (sameLemma.Any(l => l.EndsWith("read", StringComparison.Ordinal) && !l.EndsWith("spread", StringComparison.Ordinal)))
            {
                groups.Add(ReadGroup);
            }
        }

        return groups;
    }

    /// <summary>
    ///     Loads the sets of the tense report.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="gold">The gold file of the treebank test file; the development and training files next to it (<c>ud_dev.tsv</c>, <c>ud_train.tsv</c>) are added when they exist.</param>
    /// <returns>The sets by name and the notes of the sets that were left out. A treebank file without the lemma, tag and features columns is left out with a note.</returns>
    public static (List<(string Name, IReadOnlyList<TaggedSentence> Sentences)> Sets, List<string> Notes) Load(string corpus, string gold)
    {
        var sets = new List<(string, IReadOnlyList<TaggedSentence>)>();
        var notes = new List<string>();
        foreach (var (name, split) in new[] { (OpenTestName, "test"), (OpenDevName, "dev") })
        {
            sets.Add((name, [.. OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, split).Select(OpenCorpus.ToTagged)]));
        }

        var directory = Path.GetDirectoryName(gold) ?? string.Empty;
        foreach (var (name, path) in new[]
        {
            (TreebankTestName, gold),
            (TreebankDevName, Path.Join(directory, "ud_dev.tsv")),
            (TreebankTrainName, Path.Join(directory, "ud_train.tsv")),
        })
        {
            if (!File.Exists(path))
            {
                notes.Add($"{name}: not measured, the gold file '{path}' does not exist (download the treebank file for evaluation and run tools/open-corpus/scripts/prepare_tagger_eval.py).");
                continue;
            }

            var sentences = TaggerEvaluator.ReadGold(path);
            if (sentences.Count > 0 && sentences[0].Gold is null)
            {
                notes.Add($"{name}: not measured, the gold file '{path}' has no lemma, tag and feature columns (run tools/open-corpus/scripts/prepare_tagger_eval.py again).");
                continue;
            }

            sets.Add((name, sentences));
        }

        return (sets, notes);
    }

    /// <summary>
    ///     Measures a model on the sets.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="sets">The sets; a set without gold features is skipped.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score of every set and group that has at least one token, in the order of the sets, with the pooled treebank row after the treebank sets when all three files were measured.</returns>
    public static async Task<List<TenseScore>> RunAsync(IWordSenseModel model, IReadOnlyList<(string Name, IReadOnlyList<TaggedSentence> Sentences)> sets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sets);
        var scores = new List<TenseScore>();
        foreach (var (name, sentences) in sets)
        {
            scores.AddRange(await ScoreAsync(name, model.ClassifyAsync, sentences, cancellationToken).ConfigureAwait(false));
        }

        // The three treebank files pooled: the models never trained on the treebank, so the pooled figure is out of sample too
        var treebank = new[] { TreebankTestName, TreebankDevName, TreebankTrainName };
        if (treebank.All(n => scores.Any(s => s.Name == n)))
        {
            foreach (var group in Groups)
            {
                var parts = treebank.Select(n => scores.FirstOrDefault(s => s.Name == n && s.Group == group)).OfType<TenseScore>().ToList();
                if (parts.Count > 0)
                {
                    scores.Add(parts.Skip(1).Aggregate(parts[0], (sum, next) => sum.Add(TreebankPooledName, next)) with { Name = TreebankPooledName });
                }
            }
        }

        return scores;
    }

    /// <summary>
    ///     Measures the basic classifier with explicit statistics on the sets, the quick path of the tuner.
    /// </summary>
    /// <param name="tables">The statistics.</param>
    /// <param name="sentences">The gold sentences.</param>
    /// <param name="name">The name of the set.</param>
    /// <returns>The score of every group that has at least one token.</returns>
    public static Task<List<TenseScore>> ScoreBasicAsync(BasicTables tables, IReadOnlyList<TaggedSentence> sentences, string name)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(sentences);
        var lexicon = (Func<string, ImmutableArray<WordDetails>>)(key => WordLexicon.TryLookup(key, out var found) ? found : []);
        var classifier = new BasicClassifier(tables, lexicon, (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf);
        return ScoreAsync(name, classifier.ClassifyAsync, sentences, CancellationToken.None);
    }

    private static async Task<List<TenseScore>> ScoreAsync(string name, Func<IEnumerable<string>, CancellationToken, Task<ClassifyResult[]>> classify, IReadOnlyList<TaggedSentence> sentences, CancellationToken cancellationToken)
    {
        var counts = Groups.ToDictionary(g => g, _ => new int[5], StringComparer.Ordinal);
        foreach (var sentence in sentences)
        {
            if (sentence.Gold is not { } gold)
            {
                continue;
            }

            var groups = new IReadOnlyList<string>[sentence.Words.Length];
            var any = false;
            for (var i = 0; i < groups.Length; i++)
            {
                groups[i] = sentence.Tags[i] == PosTag.Verb ? GroupsOf(sentence.Words[i]) : [];
                any |= groups[i].Count > 0;
            }

            if (!any)
            {
                continue;
            }

            var results = await classify(sentence.Words, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < results.Length; i++)
            {
                if (groups[i].Count == 0)
                {
                    continue;
                }

                // The cell of the confusion matrix: past as past, past as non-past, non-past as past, non-past as non-past, unanswered
                var cell = 4;
                if (results[i].Details is { } details)
                {
                    cell = (IsGoldPast(gold[i]) ? 0 : 2) + (BasicClassifier.IsPast(details) ? 0 : 1);
                }

                foreach (var group in groups[i])
                {
                    counts[group][cell]++;
                }
            }
        }

        return [.. Groups.Where(g => counts[g].Sum() > 0).Select(g => new TenseScore(name, g, counts[g][0], counts[g][1], counts[g][2], counts[g][3], counts[g][4]))];
    }

    /// <summary>
    ///     Renders the scores of the models as Markdown: the count, the accuracy, the majority baseline and the confusion.
    /// </summary>
    /// <param name="scoresByModel">The scores of each model.</param>
    /// <param name="notes">The notes of the sets that were left out.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(IReadOnlyList<(string Model, List<TenseScore> Scores)> scoresByModel, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(scoresByModel);
        ArgumentNullException.ThrowIfNull(notes);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### Tense of same-spelling verbs\n\n");
        text.Append("| Model | Set | Verbs | Tokens | Accuracy | Majority baseline | Past as past | Past as non-past | Non-past as past | Non-past as non-past | Not answered |\n");
        text.Append("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var (model, scores) in scoresByModel)
        {
            foreach (var s in scores)
            {
                text.Append("| ").Append(model).Append(" | ").Append(s.Name).Append(" | ").Append(s.Group)
                    .Append(" | ").Append(s.Tokens.ToString("N0", inv))
                    .Append(" | ").Append(EvaluationResult.Percent(s.Correct, s.Tokens))
                    .Append(" | ").Append(EvaluationResult.Percent(s.Majority, s.Tokens))
                    .Append(" | ").Append(s.PastAsPast.ToString("N0", inv))
                    .Append(" | ").Append(s.PastAsNonPast.ToString("N0", inv))
                    .Append(" | ").Append(s.NonPastAsPast.ToString("N0", inv))
                    .Append(" | ").Append(s.NonPastAsNonPast.ToString("N0", inv))
                    .Append(" | ").Append(s.Unanswered.ToString("N0", inv)).Append(" |\n");
            }
        }

        text.Append('\n');
        foreach (var note in notes)
        {
            text.Append(note).Append("\n\n");
        }

        text.Append("The class is past (a past tense or a participle) or non-past (infinitive, present and imperative, which the lexicon does not\n");
        text.Append("tell apart); the question is the meaning of the word in its sentence, not its sound. A token is scored when the gold\n");
        text.Append("source calls it a verb and its form has a past and a non-past verb analysis in the lexicon; the answered class is that of the\n");
        text.Append("selected analysis (a verb read as a noun is not past). The treebank test file and the open corpus test split are the sets\n");
        text.Append("to report; the development sets are for choosing settings, and the pooled treebank row is out of sample because no model\n");
        text.Append("trains on the treebank.\n");
        return text.ToString();
    }
}
