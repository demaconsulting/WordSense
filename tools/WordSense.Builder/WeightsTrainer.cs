using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Settings of the weights trainer.
/// </summary>
/// <param name="C">Inverse regularization strength of the logistic regression.</param>
/// <param name="Folds">Number of cross-validation folds used to choose abstain margins.</param>
/// <param name="TargetAccuracy">Accuracy the answered sentences should reach when choosing a margin.</param>
/// <param name="MinCoverage">Share of sentences that must stay answered when choosing a margin.</param>
/// <param name="Parallelism">Number of lemmas trained concurrently.</param>
internal sealed record TrainingOptions(
    double C = 100,
    int Folds = 5,
    double TargetAccuracy = 0.99,
    double MinCoverage = 0.7,
    int Parallelism = 2)
{
    /// <summary>
    ///     Gets the number of lemmas the builder trains concurrently: the fit of each lemma is independent and written to
    ///     its own slot, so the result does not depend on it; two processors are left to the machine.
    /// </summary>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 2, 2, 16);
}

/// <summary>
///     Trains the per-lemma classifiers of a model and assembles its weights.
/// </summary>
internal static class WeightsTrainer
{
    private const int MinSamplesForCrossValidation = 3;
    private const int MinTotalForCrossValidation = 10;

    /// <summary>
    ///     Trains dense-feature weights from pooled encoder vectors.
    /// </summary>
    /// <param name="modelId">The model identifier recorded in the weights.</param>
    /// <param name="classHash">The class hash of the word image the weights are trained for (<c>WordImage.ClassHash</c>).</param>
    /// <param name="pooled">One pooled vector per anchor row.</param>
    /// <param name="anchors">The training rows.</param>
    /// <param name="inventory">The sense inventory.</param>
    /// <param name="options">The trainer settings.</param>
    /// <param name="frozenMean">The global mean to center on, or null to compute it from <paramref name="pooled"/>.</param>
    /// <param name="fitCache">The cache of lemma fits, or null to fit every lemma.</param>
    /// <returns>The weights.</returns>
    public static WeightsData TrainDense(string modelId, byte[] classHash, double[][] pooled, IReadOnlyList<CorpusRow> anchors, SenseInventory inventory, TrainingOptions options, float[]? frozenMean = null, FitCache? fitCache = null)
    {
        if (pooled.Length == 0)
        {
            throw new InvalidDataException("There are no anchor sentences to train from.");
        }

        var dim = pooled[0].Length;
        if (frozenMean is not null && frozenMean.Length != dim)
        {
            throw new ArgumentException($"The frozen mean has {frozenMean.Length} numbers but the vectors have {dim}.", nameof(frozenMean));
        }

        // A frozen mean keeps the features of a lemma independent of the anchors of every other lemma
        var mean = frozenMean is null ? ComputeMean(pooled, dim) : (float[])frozenMean.Clone();

        var features = new float[pooled.Length][];
        for (var i = 0; i < pooled.Length; i++)
        {
            features[i] = FeatureExtractor.Center(pooled[i], mean)
                ?? throw new InvalidDataException($"{anchors[i].Source}: the feature has no magnitude: {anchors[i].Sentence}");
        }

        var entries = TrainAll(anchors, inventory, options, (_, index) => index.Select(i => features[i]).ToArray(), fitCache);
        return new WeightsData(modelId, classHash, dim, mean, entries);
    }

    /// <summary>
    ///     Computes the global mean of the pooled vectors.
    /// </summary>
    /// <param name="pooled">The pooled vectors.</param>
    /// <param name="dim">The vector length.</param>
    /// <returns>The mean.</returns>
    internal static float[] ComputeMean(double[][] pooled, int dim)
    {
        var mean = new float[dim];
        foreach (var vector in pooled)
        {
            for (var d = 0; d < dim; d++)
            {
                mean[d] += (float)(vector[d] / pooled.Length);
            }
        }

        return mean;
    }

    private static List<WeightedLemma> TrainAll(
        IReadOnlyList<CorpusRow> anchors,
        SenseInventory inventory,
        TrainingOptions options,
        Func<string, int[], float[][]> prepare,
        FitCache? fitCache)
    {
        var lemmas = anchors.Select((row, i) => (row, i)).GroupBy(t => t.row.Lemma, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var trained = new (LemmaModelKind Kind, float[] Weights, float[] Bias, float Margin, bool Cv, int Dimension)[lemmas.Count];
        try
        {
            Parallel.For(0, lemmas.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism) }, k =>
            {
                TrainLemma(k);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // Report the first data error itself, not the wrapper, so the caller can print one line
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
            throw;
        }

        void TrainLemma(int k)
        {
            var lemma = lemmas[k].Key;
            var index = lemmas[k].Select(t => t.i).ToArray();
            var names = inventory.LabelsOf(lemma);
            if (names.Length < 2)
            {
                throw new InvalidDataException($"Lemma '{lemma}' needs anchor sentences for at least two senses.");
            }

            var labels = index.Select(i => Array.BinarySearch(names, anchors[i].Sense, StringComparer.Ordinal)).ToArray();
            var x = prepare(lemma, index);
            var fitKey = fitCache is null ? null : FitCache.KeyOf(options, lemma, names, labels, x);
            if (fitKey is not null && fitCache!.TryGet(fitKey, out var cached))
            {
                trained[k] = (cached.Kind, cached.Weights, cached.Bias, cached.Margin, cached.CrossValidated, cached.Dimension);
                return;
            }

            var kind = names.Length == 2 ? LemmaModelKind.Binary : LemmaModelKind.OneVersusRest;
            var (weights, bias) = Fit(x, labels, names.Length, kind, options.C);
            var counts = new int[names.Length];
            foreach (var label in labels)
            {
                counts[label]++;
            }

            var cv = counts.Min() >= MinSamplesForCrossValidation && labels.Length >= MinTotalForCrossValidation;
            var margin = cv ? ChooseMargin(lemma, x, labels, names.Length, kind, options) : 0f;
            trained[k] = (kind, weights, bias, margin, cv, x[0].Length);
            if (fitKey is not null)
            {
                fitCache!.Add(fitKey, new LemmaFit(kind, weights, bias, margin, cv, x[0].Length));
            }
        }

        var margins = trained.Where(t => t.Cv).Select(t => t.Margin).Order().ToList();
        var fallback = margins.Count == 0 ? 0f : margins[margins.Count / 2];
        var entries = new List<WeightedLemma>(lemmas.Count);
        for (var k = 0; k < lemmas.Count; k++)
        {
            var t = trained[k];
            var lemma = lemmas[k].Key;
            var classIds = inventory.LabelsOf(lemma).Select(label => lemma + "#" + label).ToList();
            var weights = new LemmaWeights(t.Kind, t.Weights, t.Bias, t.Cv ? t.Margin : fallback, t.Dimension);
            entries.Add(new WeightedLemma(lemma, classIds, weights));
        }

        return entries;
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

    private static float ChooseMargin(string lemma, float[][] x, int[] labels, int senseCount, LemmaModelKind kind, TrainingOptions options)
    {
        var folds = Math.Min(options.Folds, labels.GroupBy(l => l).Min(g => g.Count()));
        var assignment = AssignFolds(lemma, labels, folds);
        var observed = new List<(float Margin, bool Correct)>();
        var dim = x[0].Length;
        var scores = new float[senseCount];
        for (var fold = 0; fold < folds; fold++)
        {
            var train = Enumerable.Range(0, x.Length).Where(i => assignment[i] != fold).ToArray();
            var (weights, bias) = Fit(train.Select(i => x[i]).ToArray(), train.Select(i => labels[i]).ToArray(), senseCount, kind, options.C);
            var model = new LemmaWeights(kind, weights, bias, 0f, dim);
            foreach (var i in Enumerable.Range(0, x.Length).Where(i => assignment[i] == fold))
            {
                model.Scores(x[i], scores);
                var (best, margin) = LemmaWeights.Rank(scores);
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
