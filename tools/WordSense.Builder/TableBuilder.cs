using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Settings of the table builder.
/// </summary>
/// <param name="C">Inverse regularization strength of the logistic regression.</param>
/// <param name="Folds">Number of cross-validation folds used to choose abstain margins.</param>
/// <param name="TargetAccuracy">Accuracy the answered sentences should reach when choosing a margin.</param>
/// <param name="MinCoverage">Share of sentences that must stay answered when choosing a margin.</param>
/// <param name="MaxSequenceLength">Encoder window size; must match the classifier's.</param>
/// <param name="Parallelism">Number of sentences encoded concurrently.</param>
internal sealed record TableBuilderOptions(
    double C = 100,
    int Folds = 5,
    double TargetAccuracy = 0.99,
    double MinCoverage = 0.7,
    int MaxSequenceLength = 256,
    int Parallelism = 4);

/// <summary>
///     The trained parameters of one lemma.
/// </summary>
/// <param name="Senses">The sense definitions in ordinal order of label.</param>
/// <param name="Names">The sense labels, parallel to the definitions.</param>
/// <param name="Kind">Binary or one-versus-rest.</param>
/// <param name="Weights">The weight rows.</param>
/// <param name="Bias">One bias per row.</param>
/// <param name="Margin">The cross-validated abstain margin (meaningful only when <paramref name="Cv"/>).</param>
/// <param name="Cv">Whether the margin was cross-validated.</param>
/// <param name="Labels">The sense index of each anchor.</param>
/// <param name="Index">The indices of the lemma's anchor rows.</param>
internal sealed record TrainedLemma(
    SenseDefinition[] Senses,
    string[] Names,
    LemmaModelKind Kind,
    float[] Weights,
    float[] Bias,
    float Margin,
    bool Cv,
    int[] Labels,
    int[] Index);

/// <summary>
///     Trains the per-lemma classifiers and assembles the embedded table.
/// </summary>
internal static class TableBuilder
{
    private const int MinSamplesForCrossValidation = 3;
    private const int MinTotalForCrossValidation = 10;
    private const double ReportTargetAccuracy = 0.95;

    /// <summary>
    ///     Builds a table from corpus rows.
    /// </summary>
    /// <param name="encoder">The encoder used to featurize sentences.</param>
    /// <param name="tokenizer">The tokenizer that matches the encoder.</param>
    /// <param name="anchors">Training sentences.</param>
    /// <param name="holdout">Evaluation sentences; never used for training or margin selection.</param>
    /// <param name="forms">The form to lemma rows.</param>
    /// <param name="inventory">The sense inventory; it must agree with the anchors, holdout and forms.</param>
    /// <param name="options">The builder settings.</param>
    /// <returns>The table and the report.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent.</exception>
    public static BuildResult Build(
        IWordSenseEncoder encoder,
        WordPieceTokenizer tokenizer,
        IReadOnlyList<CorpusRow> anchors,
        IReadOnlyList<CorpusRow> holdout,
        IReadOnlyList<(string Form, string Lemma, string[] Labels)> forms,
        SenseInventory inventory,
        TableBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(inventory);
        Corpus.EnsureDisjoint(anchors, holdout);
        inventory.Validate(anchors, holdout, forms);
        var warnings = new List<string>();

        // Featurize: pooled vectors first, because the anchor mean must be known before centering
        var anchorPooled = Pool(encoder, tokenizer, anchors, options);
        var holdoutPooled = Pool(encoder, tokenizer, holdout, options);
        var dim = encoder.HiddenSize;
        var mean = new float[dim];
        foreach (var pooled in anchorPooled)
        {
            for (var d = 0; d < dim; d++)
            {
                mean[d] += (float)(pooled[d] / anchorPooled.Length);
            }
        }

        var anchorFeatures = Center(anchorPooled, mean, anchors);
        var holdoutFeatures = Center(holdoutPooled, mean, holdout);

        // Group rows by lemma, in ordinal order
        var lemmas = anchors.Select((row, i) => (row, i)).GroupBy(t => t.row.Lemma, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var holdoutByLemma = holdout.Select((row, i) => (row, i)).ToLookup(t => t.row.Lemma, StringComparer.Ordinal);
        var unknown = holdoutByLemma.Select(g => g.Key).Where(k => lemmas.All(l => l.Key != k)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidDataException($"Holdout lemmas without anchor sentences: {string.Join(", ", unknown)}");
        }

        // Train every lemma; results land in fixed slots so the outcome is order independent
        var trained = new TrainedLemma[lemmas.Count];
        Parallel.For(0, lemmas.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism) }, k =>
        {
            var index = lemmas[k].Select(t => t.i).ToArray();
            trained[k] = TrainLemma(lemmas[k].Key, index, anchors, anchorFeatures, inventory, options);
        });

        var fallbackMargins = trained.Where(t => t.Cv).Select(t => t.Margin).OrderBy(m => m).ToList();
        var fallback = fallbackMargins.Count == 0 ? 0f : fallbackMargins[fallbackMargins.Count / 2];

        var models = new List<LemmaModel>();
        var reports = new List<LemmaReport>();
        var bySource = new Dictionary<string, GroupStats>(StringComparer.Ordinal);
        var accuracyEvaluator = new AccuracyEvaluator();
        for (var k = 0; k < lemmas.Count; k++)
        {
            var t = trained[k];
            var lemma = lemmas[k].Key;
            var margin = t.Cv ? t.Margin : fallback;
            var model = new LemmaModel(lemma, t.Senses, t.Kind, t.Weights, t.Bias, margin, dim);
            models.Add(model);

            var counts = new int[t.Senses.Length];
            foreach (var label in t.Labels)
            {
                counts[label]++;
            }

            // Score the holdout and group it by source file
            int correct = 0, answered = 0, answeredCorrect = 0, total = 0, meaningCorrect = 0;
            var lemmaAccuracy = new AccuracyEvaluator();
            var bySense = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var (row, i) in holdoutByLemma[lemma])
            {
                var (best, rowMargin) = model.Score(holdoutFeatures[i]);
                var right = string.Equals(t.Names[best], row.Sense, StringComparison.Ordinal);
                var goldIndex = Array.FindIndex(t.Names, s => s == row.Sense);
                var isAnswered = rowMargin >= margin;

                // The meaning level equals the sense level for a lemma that is not a heteronym
                var meaningRight = model.IsHeteronym
                    ? string.Equals(model.Senses[best].Meaning, model.Senses[goldIndex].Meaning, StringComparison.Ordinal)
                    : right;
                meaningCorrect += meaningRight ? 1 : 0;
                accuracyEvaluator.Add(row.Source, model, best, goldIndex, isAnswered);
                lemmaAccuracy.Add(row.Source, model, best, goldIndex, isAnswered);
                if (!bySense.TryGetValue(row.Sense, out var senseCounts))
                {
                    bySense[row.Sense] = senseCounts = new int[4];
                }

                senseCounts[0]++;
                senseCounts[1] += right ? 1 : 0;
                senseCounts[2] += isAnswered ? 1 : 0;
                senseCounts[3] += isAnswered && right ? 1 : 0;
                total++;
                correct += right ? 1 : 0;
                answered += isAnswered ? 1 : 0;
                answeredCorrect += isAnswered && right ? 1 : 0;
                var old = bySource.GetValueOrDefault(row.Source, new GroupStats(0, 0, 0, 0));
                bySource[row.Source] = new GroupStats(
                    old.Total + 1, old.Correct + (right ? 1 : 0), old.Answered + (isAnswered ? 1 : 0), old.AnsweredCorrect + (isAnswered && right ? 1 : 0));
            }

            var lemmaReport = lemmaAccuracy.ToReport();
            var report = new LemmaReport(lemma, t.Index.Length, total, correct, answered, answeredCorrect, margin, !t.Cv, counts.Min(), meaningCorrect,
                bySense.Select(p => new SenseStats(p.Key, p.Value[0], p.Value[1], p.Value[2], p.Value[3])).ToList(),
                lemmaReport.Pos,
                model.IsHeteronym ? lemmaReport.Pronunciation : null);
            reports.Add(report);
            AddWarnings(report, t.Names, holdoutByLemma[lemma].Select(h => h.row.Sense), warnings);
        }

        var table = new WordSenseTable(
            WordSenseModelCatalog.ModelId, encoder.ModelSha256, dim, mean, models, BuildForms(forms, models));
        return new BuildResult(table, new BuildReport(reports, bySource, warnings, ReportTargetAccuracy, accuracyEvaluator.ToReport()));
    }

    /// <summary>
    ///     Adds quality warnings for one lemma.
    /// </summary>
    /// <param name="report">The lemma's evaluation.</param>
    /// <param name="senses">The senses of the lemma.</param>
    /// <param name="holdoutSenses">The senses seen in the holdout.</param>
    /// <param name="warnings">The list receiving the warnings.</param>
    private static void AddWarnings(LemmaReport report, string[] senses, IEnumerable<string> holdoutSenses, List<string> warnings)
    {
        if (report.MinAnchorsPerSense < 8)
        {
            warnings.Add($"{report.Lemma}: a sense has only {report.MinAnchorsPerSense} anchor sentences (fewer than 8).");
        }

        if (report.DefaultMargin)
        {
            warnings.Add($"{report.Lemma}: too few sentences for cross-validation; the default abstain margin was used.");
        }

        if (report.HoldoutCount > 0 && report.Accuracy < ReportTargetAccuracy)
        {
            warnings.Add($"{report.Lemma}: holdout accuracy {report.Accuracy:P1} is below {ReportTargetAccuracy:P0}.");
        }

        foreach (var sense in holdoutSenses.Distinct(StringComparer.Ordinal).Where(s => !senses.Contains(s, StringComparer.Ordinal)))
        {
            warnings.Add($"{report.Lemma}: holdout sense '{sense}' has no anchor sentences.");
        }
    }

    /// <summary>
    ///     Builds the form map for lemmas that have a classifier.
    /// </summary>
    /// <param name="forms">The form rows.</param>
    /// <param name="models">The trained lemma models.</param>
    /// <returns>The sorted form to lemma map.</returns>
    private static SortedDictionary<string, string> BuildForms(
        IReadOnlyList<(string Form, string Lemma, string[] Labels)> forms, List<LemmaModel> models)
    {
        var byLemma = models.ToDictionary(m => m.Lemma, StringComparer.Ordinal);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (form, lemma, _) in forms)
        {
            if (!byLemma.ContainsKey(lemma))
            {
                continue;
            }

            // A form listed for two lemmas keeps the first; the corpus is expected to avoid this
            result.TryAdd(form, lemma);
        }

        // Every classified lemma must also resolve as itself
        foreach (var lemma in models.Select(m => m.Lemma))
        {
            result.TryAdd(lemma, lemma);
        }

        return result;
    }

    /// <summary>
    ///     Encodes every row and pools the hidden states of its target word.
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="options">The builder settings.</param>
    /// <returns>One pooled vector per row, in row order.</returns>
    internal static double[][] Pool(IWordSenseEncoder encoder, WordPieceTokenizer tokenizer, IReadOnlyList<CorpusRow> rows, TableBuilderOptions options)
    {
        var result = new double[rows.Count][];
        Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism) }, i =>
        {
            var row = rows[i];
            var tokens = tokenizer.TokenizeWords(row.Words);
            var first = tokens.WordFirst[row.TargetWordIndex];
            var last = tokens.WordEnd[row.TargetWordIndex];
            var window = tokens.BuildWindow(tokenizer.ClsId, tokenizer.SepId, options.MaxSequenceLength, first, last);
            var hidden = encoder.EncodeAsync(window.InputIds, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            result[i] = FeatureExtractor.Pool(hidden, encoder.HiddenSize, window.TargetFirst, window.TargetEnd)
                ?? throw new InvalidDataException($"{row.Source}: the target word has no tokens: {row.Sentence}");
        });
        return result;
    }

    /// <summary>
    ///     Centers and normalizes pooled vectors.
    /// </summary>
    /// <param name="pooled">The pooled vectors.</param>
    /// <param name="mean">The anchor mean.</param>
    /// <param name="rows">The rows, for error messages.</param>
    /// <returns>The features.</returns>
    internal static float[][] Center(double[][] pooled, float[] mean, IReadOnlyList<CorpusRow> rows)
    {
        var features = new float[pooled.Length][];
        for (var i = 0; i < pooled.Length; i++)
        {
            features[i] = FeatureExtractor.Center(pooled[i], mean)
                ?? throw new InvalidDataException($"{rows[i].Source}: the feature has no magnitude: {rows[i].Sentence}");
        }

        return features;
    }

    /// <summary>
    ///     Trains one lemma and chooses its abstain margin by cross-validation.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="index">Indices of the lemma's anchor rows.</param>
    /// <param name="anchors">All anchor rows.</param>
    /// <param name="features">Features of all anchor rows.</param>
    /// <param name="inventory">The sense inventory.</param>
    /// <param name="options">The builder settings.</param>
    /// <returns>The trained parameters.</returns>
    internal static TrainedLemma TrainLemma(
        string lemma, int[] index, IReadOnlyList<CorpusRow> anchors, float[][] features, SenseInventory inventory, TableBuilderOptions options)
    {
        var senses = inventory.DefinitionsOf(lemma);
        var names = inventory.LabelsOf(lemma);
        if (senses.Length < 2)
        {
            throw new InvalidDataException($"Lemma '{lemma}' needs anchor sentences for at least two senses.");
        }

        var labels = index.Select(i => Array.BinarySearch(names, anchors[i].Sense, StringComparer.Ordinal)).ToArray();
        var x = index.Select(i => features[i]).ToArray();
        var kind = senses.Length == 2 ? LemmaModelKind.Binary : LemmaModelKind.OneVersusRest;
        var (weights, bias) = Fit(x, labels, senses.Length, kind, options.C);

        // Cross-validate to choose the abstain margin, when there is enough data
        var counts = new int[senses.Length];
        foreach (var label in labels)
        {
            counts[label]++;
        }

        var cv = counts.Min() >= MinSamplesForCrossValidation && labels.Length >= MinTotalForCrossValidation;
        var margin = 0f;
        if (cv)
        {
            margin = ChooseMargin(lemma, x, labels, senses, kind, options);
        }

        return new TrainedLemma(senses, names, kind, weights, bias, margin, cv, labels, index);
    }

    /// <summary>
    ///     Fits the weight rows for a lemma.
    /// </summary>
    /// <param name="x">The features.</param>
    /// <param name="labels">The sense index of each feature.</param>
    /// <param name="senseCount">The number of senses.</param>
    /// <param name="kind">Binary or one-versus-rest.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>Row-major weights and one bias per row.</returns>
    internal static (float[] Weights, float[] Bias) Fit(float[][] x, int[] labels, int senseCount, LemmaModelKind kind, double c)
    {
        var rows = kind == LemmaModelKind.Binary ? 1 : senseCount;
        var dim = x[0].Length;
        var weights = new float[rows * dim];
        var bias = new float[rows];
        for (var row = 0; row < rows; row++)
        {
            // The binary row scores the second sense positively
            var target = kind == LemmaModelKind.Binary ? 1 : row;
            var (w, b) = LogisticTrainer.Train(x, labels.Select(l => l == target).ToArray(), c);
            for (var d = 0; d < dim; d++)
            {
                weights[(row * dim) + d] = (float)w[d];
            }

            bias[row] = (float)b;
        }

        return (weights, bias);
    }

    /// <summary>
    ///     Chooses the lowest margin that reaches the target accuracy on cross-validated predictions.
    /// </summary>
    /// <param name="lemma">The lemma, which seeds the fold assignment.</param>
    /// <param name="x">The features.</param>
    /// <param name="labels">The sense index of each feature.</param>
    /// <param name="senses">The sense definitions.</param>
    /// <param name="kind">Binary or one-versus-rest.</param>
    /// <param name="options">The builder settings.</param>
    /// <returns>The abstain margin.</returns>
    private static float ChooseMargin(string lemma, float[][] x, int[] labels, SenseDefinition[] senses, LemmaModelKind kind, TableBuilderOptions options)
    {
        var folds = Math.Min(options.Folds, labels.GroupBy(l => l).Min(g => g.Count()));
        var assignment = AssignFolds(lemma, labels, folds);
        var observed = new List<(float Margin, bool Correct)>();
        var dim = x[0].Length;
        for (var fold = 0; fold < folds; fold++)
        {
            var train = Enumerable.Range(0, x.Length).Where(i => assignment[i] != fold).ToArray();
            var (weights, bias) = Fit(train.Select(i => x[i]).ToArray(), train.Select(i => labels[i]).ToArray(), senses.Length, kind, options.C);
            var model = new LemmaModel(lemma, senses, kind, weights, bias, 0f, dim);
            foreach (var i in Enumerable.Range(0, x.Length).Where(i => assignment[i] == fold))
            {
                var (best, margin) = model.Score(x[i]);
                observed.Add((margin, best == labels[i]));
            }
        }

        observed.Sort((a, b) => a.Margin.CompareTo(b.Margin));
        var total = observed.Count;
        var correctFromHere = observed.Count(o => o.Correct);
        for (var i = 0; i < total; i++)
        {
            // Accepting everything from i upward must stay accurate and keep enough coverage
            var coverage = (double)(total - i) / total;
            if (coverage < options.MinCoverage)
            {
                break;
            }

            if ((double)correctFromHere / (total - i) >= options.TargetAccuracy)
            {
                return observed[i].Margin;
            }

            correctFromHere -= observed[i].Correct ? 1 : 0;
        }

        // Nothing reaches the target: keep the minimum required coverage
        var cut = (int)Math.Floor((1 - options.MinCoverage) * total);
        return observed[Math.Min(cut, total - 1)].Margin;
    }

    /// <summary>
    ///     Assigns each sample to a fold, stratified by label and shuffled with a seed derived from the lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="labels">The label of each sample.</param>
    /// <param name="folds">The number of folds.</param>
    /// <returns>The fold of each sample.</returns>
    private static int[] AssignFolds(string lemma, int[] labels, int folds)
    {
        // string.GetHashCode is randomized per process, so derive the seed with FNV-1a instead
        var seed = 2166136261u;
        foreach (var ch in lemma)
        {
            seed = (seed ^ ch) * 16777619u;
        }

        var assignment = new int[labels.Length];
        foreach (var group in labels.Select((label, i) => (label, i)).GroupBy(t => t.label).OrderBy(g => g.Key))
        {
            // Shuffle by a seeded integer hash of the index; deterministic and independent of any RNG
            var order = group.Select(t => t.i).OrderBy(i => Mix(seed, (uint)i)).ThenBy(i => i).ToArray();
            for (var position = 0; position < order.Length; position++)
            {
                assignment[order[position]] = position % folds;
            }
        }

        return assignment;
    }

    /// <summary>
    ///     Mixes a seed and a value into a well-distributed hash (the murmur3 finalizer).
    /// </summary>
    /// <param name="seed">The seed.</param>
    /// <param name="value">The value.</param>
    /// <returns>The hash.</returns>
    private static uint Mix(uint seed, uint value)
    {
        unchecked
        {
            var h = seed ^ (value * 0x9E3779B1u);
            h ^= h >> 16;
            h *= 0x85EB_CA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
