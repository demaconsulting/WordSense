using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Settings of the weights trainer and of the threshold choice.
/// </summary>
/// <param name="C">Inverse regularization strength of the logistic regression.</param>
/// <param name="Folds">Number of cross-validation folds used to choose the thresholds.</param>
/// <param name="TargetPrecision">Precision that the answers of a level must reach when its threshold is chosen.</param>
/// <param name="MinAnswered">Number of answers a threshold must keep before its precision is believed.</param>
/// <param name="Parallelism">Number of lemmas trained concurrently.</param>
/// <param name="Solver">The method that fits the classifiers; the kernel form and the primal form find the same optimum.</param>
internal sealed record TrainingOptions(
    double C = 100,
    int Folds = 5,
    double TargetPrecision = 0.95,
    int MinAnswered = 3,
    int Parallelism = 2,
    FitSolver Solver = FitSolver.Kernel)
{
    /// <summary>
    ///     Gets the number of lemmas the builder trains concurrently: the fit of each lemma is independent and written to
    ///     its own slot, so the result does not depend on it; two processors are left to the machine.
    /// </summary>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 2, 2, 16);
}

/// <summary>
///     The weights of a training run and the lemmas that got none.
/// </summary>
/// <param name="Data">The weights of every lemma that has anchors for at least two senses.</param>
/// <param name="Untrained">Each lemma with anchors that has no weights, with the number of senses it has anchors for.</param>
internal sealed record TrainedWeights(WeightsData Data, IReadOnlyList<(string Lemma, int AnchoredSenses)> Untrained);

/// <summary>
///     Trains the per-lemma fine-sense classifiers of a model and assembles its weights.
/// </summary>
/// <remarks>
///     <para>
///     For every lemma whose anchors name at least two distinct senses the trainer fits one one-versus-rest L2
///     logistic regression (see <see cref="LogisticTrainer"/>) over the senses of the lemma that have at least one
///     anchor, on features centered on the global mean and scaled to unit length (<see cref="FeatureExtractor"/>). The
///     columns are those senses in inventory order: the parts of speech in enumeration order and, in each, the order of
///     <see cref="WordLexicon.SensesOf"/>. A sense with a single anchor takes part in the training. A lemma with fewer
///     than two anchored senses has nothing to choose between and gets no weights; that is reported, not an error. A
///     two-sense lemma has two rows (there is no binary model). The thresholds of the lemma come from
///     <see cref="Thresholds.Choose"/> on the anchors alone.
///     </para>
///     <para>
///     Form choosers (the small classifiers that pick the lemma of a form such as <c>left</c>) are not trained: the
///     corpus has no rows for them yet, so the weights carry none (the blob format supports them).
///     </para>
/// </remarks>
internal static class WeightsTrainer
{
    /// <summary>
    ///     Trains dense-feature weights from pooled encoder vectors.
    /// </summary>
    /// <param name="modelId">The model identifier recorded in the weights.</param>
    /// <param name="inventoryHash">The 32-byte inventory hash of the word image the weights are trained for (<c>WordLexicon.Current.InventoryHash</c>).</param>
    /// <param name="pooled">One pooled vector per anchor row.</param>
    /// <param name="anchors">The training rows; their senses are looked up in the catalog of the word image.</param>
    /// <param name="options">The trainer settings.</param>
    /// <param name="frozenMean">The global mean to center on, or null to compute it from <paramref name="pooled"/>.</param>
    /// <param name="fitCache">The cache of lemma fits, or null to fit every lemma.</param>
    /// <returns>The weights, with the lemmas that got none.</returns>
    /// <exception cref="InvalidDataException">Thrown when there are no anchors, a feature has no magnitude or an anchor names a sense that the catalog does not have.</exception>
    /// <exception cref="ArgumentException">Thrown when the frozen mean does not have the dimension of the vectors.</exception>
    public static TrainedWeights TrainDense(string modelId, byte[] inventoryHash, double[][] pooled, IReadOnlyList<CorpusRow> anchors, TrainingOptions options, float[]? frozenMean = null, FitCache? fitCache = null)
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

        var (entries, untrained) = TrainAll(anchors, features, options, fitCache);
        return new TrainedWeights(new WeightsData(modelId, inventoryHash, dim, mean, entries), untrained);
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

    /// <summary>
    ///     Fits the weight rows for a lemma: one one-versus-rest row per sense.
    /// </summary>
    /// <param name="x">The features.</param>
    /// <param name="labels">The sense index of each feature.</param>
    /// <param name="senseCount">The number of senses.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <param name="solver">The solver; the default is the kernel form, which is much faster when there are fewer samples than features.</param>
    /// <returns>Row-major weights and one bias per row.</returns>
    internal static (float[] Weights, float[] Bias) Fit(float[][] x, int[] labels, int senseCount, double c, FitSolver solver = FitSolver.Kernel) =>
        new LemmaFitter(x, solver).Fit(Enumerable.Range(0, x.Length).ToArray(), labels, senseCount, c);

    /// <summary>
    ///     Trains every lemma that has anchors, concurrently.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="features">The centered, unit-length feature of each row.</param>
    /// <param name="options">The settings.</param>
    /// <param name="fitCache">The cache of lemma fits, or null.</param>
    /// <returns>The trained lemmas in ordinal order of the lemma, and the lemmas without weights.</returns>
    private static (List<WeightedLemma> Entries, List<(string Lemma, int AnchoredSenses)> Untrained) TrainAll(
        IReadOnlyList<CorpusRow> anchors,
        float[][] features,
        TrainingOptions options,
        FitCache? fitCache)
    {
        var lemmas = anchors.Select((row, i) => (row, i)).GroupBy(t => t.row.Lemma, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var trained = new WeightedLemma?[lemmas.Count];
        var anchoredSenses = new int[lemmas.Count];
        try
        {
            Parallel.For(0, lemmas.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism) }, k =>
            {
                trained[k] = TrainLemma(lemmas[k].Key, lemmas[k].Select(t => t.i).ToArray(), anchors, features, options, fitCache, out anchoredSenses[k]);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // Report the first data error itself, not the wrapper, so the caller can print one line
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
            throw;
        }

        var entries = new List<WeightedLemma>(lemmas.Count);
        var untrained = new List<(string, int)>();
        for (var k = 0; k < lemmas.Count; k++)
        {
            if (trained[k] is { } entry)
            {
                entries.Add(entry);
            }
            else
            {
                untrained.Add((lemmas[k].Key, anchoredSenses[k]));
            }
        }

        return (entries, untrained);
    }

    /// <summary>
    ///     Trains one lemma, or reports that it has nothing to choose between.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="index">The indexes of the anchors of the lemma.</param>
    /// <param name="anchors">All training rows.</param>
    /// <param name="features">The feature of each row.</param>
    /// <param name="options">The settings.</param>
    /// <param name="fitCache">The cache of lemma fits, or null.</param>
    /// <param name="anchored">Receives the number of senses of the lemma that have anchors.</param>
    /// <returns>The weights, or null when fewer than two senses have anchors.</returns>
    private static WeightedLemma? TrainLemma(
        string lemma,
        int[] index,
        IReadOnlyList<CorpusRow> anchors,
        float[][] features,
        TrainingOptions options,
        FitCache? fitCache,
        out int anchored)
    {
        // The columns are the anchored senses in inventory order; an anchor of a sense the catalog lacks is a data error
        var withAnchors = index.Select(i => anchors[i].SenseId).ToHashSet(StringComparer.Ordinal);
        var catalog = SenseInventory.SensesOf(lemma);
        var columns = catalog.Where(s => withAnchors.Contains(s.SenseId)).ToList();
        if (columns.Count != withAnchors.Count)
        {
            var unknown = withAnchors.Except(columns.Select(c => c.SenseId), StringComparer.Ordinal).Order(StringComparer.Ordinal).First();
            throw new InvalidDataException($"The anchors of '{lemma}' name the sense '{unknown}', which is not a sense of the lemma in the catalog.");
        }

        anchored = columns.Count;
        if (columns.Count < 2)
        {
            return null;
        }

        var senseIds = columns.Select(c => c.SenseId).ToArray();
        var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = columns.Select(c => GroupIndexOf(c, groupIndex)).ToArray();
        var labels = index.Select(i => Array.IndexOf(senseIds, anchors[i].SenseId)).ToArray();
        var x = index.Select(i => features[i]).ToArray();

        var key = fitCache is null ? null : FitCache.KeyOf(options, lemma, senseIds, groups, labels, x);
        if (key is not null && fitCache!.TryGet(key, out var cached))
        {
            return Entry(lemma, senseIds, cached);
        }

        var fitter = new LemmaFitter(x, options.Solver, options.Parallelism);
        var (weights, bias) = fitter.Fit(Enumerable.Range(0, x.Length).ToArray(), labels, senseIds.Length, options.C);
        var thresholds = Thresholds.Choose(lemma, x, labels, senseIds.Length, groups, options, fitter);
        var fit = new LemmaFit(weights, bias, thresholds.Fine, thresholds.Group, x[0].Length);
        fitCache?.Add(key!, fit);
        return Entry(lemma, senseIds, fit);
    }

    /// <summary>
    ///     Gives the group of a sense an index in order of first appearance.
    /// </summary>
    /// <param name="sense">The sense.</param>
    /// <param name="indexes">The group indexes assigned so far; a new group is added.</param>
    /// <returns>The index of the group of the sense, or -1 when it is in no group.</returns>
    private static int GroupIndexOf(SenseInfo sense, Dictionary<string, int> indexes)
    {
        if (sense.GroupId is null)
        {
            return -1;
        }

        if (!indexes.TryGetValue(sense.GroupId, out var index))
        {
            index = indexes.Count;
            indexes[sense.GroupId] = index;
        }

        return index;
    }

    /// <summary>
    ///     Builds the weights of a lemma from a fit.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senseIds">The sense ids in column order.</param>
    /// <param name="fit">The fit.</param>
    /// <returns>The lemma with its classifier and thresholds.</returns>
    private static WeightedLemma Entry(string lemma, string[] senseIds, LemmaFit fit) =>
        new(lemma, senseIds, new LemmaWeights(fit.Weights, fit.Bias, fit.Dimension, fit.FineThreshold, fit.GroupThreshold));
}
