using System.Diagnostics;
using System.Globalization;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     Settings of one weights run.
/// </summary>
/// <param name="CacheRoot">The folder that holds the embedding caches (one subfolder per model id), or null to encode every sentence.</param>
/// <param name="RecomputeMean">True to compute the global mean again instead of reusing the one of the existing weights.</param>
/// <param name="Solver">The method that fits the classifiers; the kernel form is the default and the primal form is the slow reference.</param>
/// <param name="CalibrationTarget">The precision that the answers of a level must reach on the real calibration sentences (see <see cref="RealCalibrator"/>). The default 0.97 is two points above the 95 percent aimed at on real test text: the measured gap between the lemma-level precision on the calibration rows and the precision of the whole pipeline (analysis and chooser errors) is about two points.</param>
/// <param name="UsagePrior">True to correct the per-sense scores to the usage of the senses (see <see cref="SensePrior"/>); false (<c>--no-prior</c>) trains the raw balanced-anchor scores.</param>
/// <param name="PriorAlpha">The exponent of the usage share in the prior (<c>--prior-alpha</c>).</param>
/// <param name="PriorSmoothing">The constant added to every tag count in the prior (<c>--prior-smoothing</c>).</param>
internal sealed record TrainSettings(
    string? CacheRoot,
    bool RecomputeMean = false,
    FitSolver Solver = FitSolver.Kernel,
    double CalibrationTarget = RealCalibrator.DefaultLemmaTarget,
    bool UsagePrior = true,
    double PriorAlpha = PriorSettings.DefaultAlpha,
    double PriorSmoothing = PriorSettings.DefaultSmoothing)
{
    /// <summary>
    ///     Gets the default cache folder of a repository.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The folder.</returns>
    public static string DefaultCacheRoot(string repository) => Path.Join(repository, ".cache", "embeddings");
}

/// <summary>
///     Trains the weights of an encoder model from the corpus and writes them to the model's package.
/// </summary>
internal static class ModelTrainer
{
    private const int ProgressInterval = 2000;
    private const int CheckpointMisses = 4000;
    private const int MaxWarningsShown = 10;
    private const int MaxLemmasListed = 40;

    /// <summary>
    ///     Trains a model.
    /// </summary>
    /// <remarks>
    ///     The anchors and the holdout are read from the fine-sense folders of the corpus and checked against the sense
    ///     catalog of the word image; the legacy class-labeled folders are never read. Rows whose only problem is that
    ///     their target word is no form of their lemma are reported as warnings and still train. The form choosers are
    ///     trained from <c>corpus/choosers</c> (rows of the form, the label and the sentence) and stored with the weights.
    /// </remarks>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="info">The model.</param>
    /// <param name="settings">The cache and mean settings.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of lemmas trained.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent or the half-precision gate fails.</exception>
    public static async Task<int> TrainAsync(string repository, string corpus, ModelInfo info, TrainSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' is statistical and has no encoder weights to train; use the 'train-basic' command.");
        }

        var anchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Join(corpus, "holdout"));
        var validation = SenseInventory.Validate(anchors, holdout);
        ReportWarnings(validation, output, info.Name);
        validation.ThrowIfInvalid();
        var chooserAnchors = ChooserCorpus.ReadDirectory(Path.Join(corpus, "choosers", "anchors"));
        var chooserHoldout = ChooserCorpus.ReadDirectory(Path.Join(corpus, "choosers", "holdout"));
        ChooserCorpus.Validate(chooserAnchors, chooserHoldout).ThrowIfInvalid();
        var options = new TrainingOptions(Parallelism: TrainingOptions.DefaultParallelism, Solver: settings.Solver);
        var usage = UsageOf(repository, settings, info.Name, output);
        var clock = Stopwatch.StartNew();

        using var composed = info.Create(Path.Join(repository, "model", info.ModelDirectory!));
        var model = ModelCatalog.EncoderOf(composed);
        await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
        var path = info.WeightsPath(repository);
        float[]? frozenMean = null;
        if (settings.RecomputeMean)
        {
            output.WriteLine($"{info.Name}: global mean: recomputed from the anchors (--recompute-mean).");
        }
        else
        {
            frozenMean = ReadFrozenMean(path, model.ModelId, output, info.Name);
        }

        EmbeddingCache? cache = null;
        FitCache? fitCache = null;
        if (settings.CacheRoot is not null)
        {
            var directory = Path.Join(settings.CacheRoot, model.ModelId);
            cache = OpenCache(directory, model, output, info.Name);
            fitCache = FitCache.Open(directory);
            output.WriteLine($"{info.Name}: fit cache {fitCache.Path}: {fitCache.Loaded} lemma fits loaded, {fitCache.Discarded} dropped.");
        }

        using var encoder = model.OpenEncoder();
        var caching = new CachingEncoder(encoder.EncodeAsync, cache);
        var data = await TrainDataAsync(
            info.Name, model.ModelId, WordLexicon.Current.InventoryHash.ToArray(), anchors, options, caching, frozenMean, fitCache, output, clock, cancellationToken, holdout, chooserAnchors, chooserHoldout,
            RealSplit.Classify(corpus, holdout).Calibration, new CalibrationOptions(settings.CalibrationTarget, Stratum: Corpus.StagesOf(anchors)), usage).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WeightsBlobWriter.Write(data));
        output.WriteLine($"{info.Name}: trained {data.Lemmas.Count} lemmas in {clock.Elapsed:hh\\:mm\\:ss}; wrote {new FileInfo(path).Length} bytes to {path}.");
        return data.Lemmas.Count;
    }

    /// <summary>
    ///     Reads the tag counts and builds the usage prior of a run, and says what it is.
    /// </summary>
    /// <param name="repository">The repository root (the counts are in its <c>data</c> folder).</param>
    /// <param name="settings">The settings of the run.</param>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="output">Receives the line that reports the prior.</param>
    /// <returns>The usage, or null when the prior is switched off.</returns>
    /// <exception cref="ArgumentException">Thrown when the exponent or the smoothing constant is out of range.</exception>
    /// <exception cref="InvalidDataException">Thrown when the sense-frequency table has no counts.</exception>
    private static SenseUsage? UsageOf(string repository, TrainSettings settings, string name, TextWriter output)
    {
        if (!settings.UsagePrior)
        {
            output.WriteLine($"{name}: usage prior: off (--no-prior); the scores are those of the balanced anchors.");
            return null;
        }

        var prior = new PriorSettings(settings.PriorAlpha, settings.PriorSmoothing);
        prior.Validate();
        var counts = SensePrior.ReadCounts(Path.Join(repository, "data"));
        output.WriteLine($"{name}: usage prior: p' ~ p / q * pi^alpha with alpha {prior.Alpha.ToString(CultureInfo.InvariantCulture)} and smoothing {prior.Smoothing.ToString(CultureInfo.InvariantCulture)} (q the share of the sense among the anchors of its lemma, pi its WordNet 3.0 tag count share); {counts.Count} tag counts read; folded into the stored biases.");
        return new SenseUsage(counts, prior);
    }

    /// <summary>
    ///     Prints the warnings of the corpus validation: the count and the first few.
    /// </summary>
    /// <param name="validation">The validation result.</param>
    /// <param name="output">Receives the lines.</param>
    /// <param name="name">The model name used in messages.</param>
    private static void ReportWarnings(CorpusValidation validation, TextWriter output, string name)
    {
        if (validation.Warnings.Count == 0)
        {
            return;
        }

        output.WriteLine($"{name}: warning: {validation.Warnings.Count} group(s) of rows mark a target word that the lexicon does not analyze as the lemma and part of speech of the row; they still train.");
        foreach (var warning in validation.Warnings.Take(MaxWarningsShown))
        {
            output.WriteLine($"{name}:   {warning}");
        }
    }

    /// <summary>
    ///     Reads the global mean of the existing weights of a model.
    /// </summary>
    /// <param name="weightsPath">The path of the weights file.</param>
    /// <param name="modelId">The model identifier the weights shall name.</param>
    /// <param name="output">Receives the reason when the mean cannot be reused.</param>
    /// <param name="name">The model name used in messages.</param>
    /// <returns>The mean, or null when it shall be computed from the anchors.</returns>
    internal static float[]? ReadFrozenMean(string weightsPath, string modelId, TextWriter output, string name)
    {
        if (!File.Exists(weightsPath))
        {
            output.WriteLine($"{name}: global mean: computed from the anchors (no existing weights).");
            return null;
        }

        try
        {
            var existing = WeightsBlob.Read(File.ReadAllBytes(weightsPath));
            if (!string.Equals(existing.ModelId, modelId, StringComparison.Ordinal))
            {
                output.WriteLine($"{name}: global mean: computed from the anchors (the existing weights are for '{existing.ModelId}', not '{modelId}').");
                return null;
            }

            output.WriteLine($"{name}: global mean: reused from the existing weights ({existing.Dimension.ToString(CultureInfo.InvariantCulture)} numbers); use --recompute-mean to compute it again.");
            return existing.Mean;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"{name}: global mean: computed from the anchors (the existing weights are unreadable: {ex.Message}).");
            return null;
        }
    }

    /// <summary>
    ///     Encodes the anchors through a caching encoder, trains the weights and, when held-out rows are given, checks
    ///     the half-precision storage against them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The half-precision gate writes the weights to a blob in memory, reads them back with the real reader, scores
    ///     the held-out rows whose lemma has weights with both and compares the fine top-1 accuracy (see
    ///     <see cref="PrecisionGate"/>). The held-out sentences are only encoded and scored; they never reach the
    ///     training or the thresholds. Both accuracies are printed and a difference of more than 0.2 percentage point
    ///     fails the run before anything is written.
    ///     </para>
    /// </remarks>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="modelId">The model identifier recorded in the weights.</param>
    /// <param name="inventoryHash">The inventory hash of the word image.</param>
    /// <param name="anchors">The training rows.</param>
    /// <param name="options">The trainer settings.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="frozenMean">The global mean to reuse, or null to compute it.</param>
    /// <param name="fitCache">The cache of lemma fits, or null to fit every lemma.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="holdout">The held-out rows for the half-precision gate, or null to skip the gate.</param>
    /// <param name="chooserAnchors">The chooser training rows, or null for none.</param>
    /// <param name="chooserHoldout">The chooser held-out rows that measure the choosers, or null for none.</param>
    /// <param name="calibration">The real held-out rows of the calibration documents that move the thresholds to the precision target, or null to keep the cross-validation thresholds.</param>
    /// <param name="calibrationOptions">The settings of the calibration, or null for the defaults.</param>
    /// <param name="usage">The usage prior of the per-sense scores, or null to train without it.</param>
    /// <returns>The full-precision weights, with the form choosers.</returns>
    /// <exception cref="InvalidDataException">Thrown when a target word has no tokens, the corpus is inconsistent or the half-precision gate fails.</exception>
    internal static async Task<WeightsData> TrainDataAsync(
        string name,
        string modelId,
        byte[] inventoryHash,
        IReadOnlyList<CorpusRow> anchors,
        TrainingOptions options,
        CachingEncoder encoder,
        float[]? frozenMean,
        FitCache? fitCache,
        TextWriter output,
        Stopwatch clock,
        CancellationToken cancellationToken,
        IReadOnlyList<CorpusRow>? holdout = null,
        IReadOnlyList<ChooserRow>? chooserAnchors = null,
        IReadOnlyList<ChooserRow>? chooserHoldout = null,
        IReadOnlyList<CorpusRow>? calibration = null,
        CalibrationOptions? calibrationOptions = null,
        SenseUsage? usage = null)
    {
        var pooled = await EncodeAsync(name, "sentences", anchors, encoder, output, clock, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"{name}: embeddings: {encoder.Hits} cache hits, {encoder.Misses} encoded in {encoder.EncodeTime:hh\\:mm\\:ss\\.fff} ({(encoder.Cache is null ? "cache off" : $"{encoder.Cache.Count} entries cached")}).");

        if (frozenMean is not null && pooled.Length > 0 && frozenMean.Length != pooled[0].Length)
        {
            output.WriteLine($"{name}: global mean: computed from the anchors (the existing mean has {frozenMean.Length} numbers, the encoder gives {pooled[0].Length}).");
            frozenMean = null;
        }

        output.WriteLine($"{name}: fitting the classifiers of {pooled.Length} sentences on {options.Parallelism} threads ({clock.Elapsed:hh\\:mm\\:ss}).");
        var fitClock = Stopwatch.StartNew();
        var trained = WeightsTrainer.TrainDense(modelId, inventoryHash, pooled, anchors, options, frozenMean, fitCache, usage);
        var data = trained.Data;

        // The form choosers come first: the calibration scores a row with the plan that the classifier has for its form, and the
        // chooser of a form decides whether the encoder runs for it (the choosers do not read the thresholds of the lemmas)
        if (chooserAnchors is { Count: > 0 })
        {
            data = await TrainChoosersAsync(name, data, chooserAnchors, chooserHoldout ?? [], options, encoder, fitCache, output, clock, cancellationToken).ConfigureAwait(false);
        }

        if (calibration is { Count: > 0 })
        {
            data = await CalibrateAsync(name, data, calibration, calibrationOptions ?? new CalibrationOptions(), encoder, output, clock, cancellationToken).ConfigureAwait(false);
        }

        if (fitCache is not null)
        {
            output.WriteLine($"{name}: lemma fits: {fitCache.Hits} reused, {fitCache.Misses} fitted in {fitClock.Elapsed:hh\\:mm\\:ss\\.fff}.");
            try
            {
                fitCache.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteLine($"{name}: warning: the fit cache could not be saved: {ex.Message}");
            }
        }

        ReportTraining(name, data, trained.Untrained, output);
        if (holdout is not null)
        {
            await CheckPrecisionAsync(name, data, holdout, encoder, output, clock, cancellationToken).ConfigureAwait(false);
        }

        return data;
    }

    /// <summary>
    ///     Encodes the target word of every row through the caching encoder, saving the embedding cache as it grows and
    ///     at the end.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="what">What the rows are called in the progress lines.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pooled vector of each row.</returns>
    /// <exception cref="InvalidDataException">Thrown when a target word has no tokens.</exception>
    private static Task<double[][]> EncodeAsync(string name, string what, IReadOnlyList<CorpusRow> rows, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken) =>
        EncodeAsync(name, what, rows.Count, i => (rows[i].Words, rows[i].TargetWordIndex, rows[i].Source, rows[i].Sentence), encoder, output, clock, cancellationToken);

    /// <summary>
    ///     Encodes the target word of every row through the caching encoder, saving the embedding cache as it grows and
    ///     at the end.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="what">What the rows are called in the progress lines.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="row">Gives the words, the target index, the source file and the sentence of a row.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pooled vector of each row.</returns>
    /// <exception cref="InvalidDataException">Thrown when a target word has no tokens.</exception>
    private static async Task<double[][]> EncodeAsync(string name, string what, int count, Func<int, (IReadOnlyList<string> Words, int Target, string Source, string Sentence)> row, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken)
    {
        var pooled = new double[count][];
        var savedAtMisses = encoder.Misses;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var (words, target, source, sentence) = row(i);
                pooled[i] = await encoder.EncodeAsync(words, target, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"{source}: the target word has no tokens: {sentence}");
                if (encoder.Cache is not null && encoder.Misses - savedAtMisses >= CheckpointMisses)
                {
                    // A long encode must not be lost when it is interrupted
                    SaveCache(encoder.Cache, output, name);
                    savedAtMisses = encoder.Misses;
                }

                if ((i + 1) % ProgressInterval == 0)
                {
                    output.WriteLine($"{name}: processed {i + 1} of {count} {what}: {encoder.Hits} cache hits, {encoder.Misses} encoded ({clock.Elapsed:hh\\:mm\\:ss}).");
                }
            }
        }
        finally
        {
            if (encoder.Cache is { IsDirty: true })
            {
                SaveCache(encoder.Cache, output, name);
            }
        }

        return pooled;
    }

    /// <summary>
    ///     Trains the form choosers from the chooser rows, measures them on the held-out chooser rows and attaches them
    ///     to the weights.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="data">The weights of the lemmas.</param>
    /// <param name="anchors">The chooser training rows.</param>
    /// <param name="holdout">The held-out chooser rows; they are only scored, never trained on.</param>
    /// <param name="options">The settings.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="fitCache">The cache of fits, or null.</param>
    /// <param name="output">Receives the report lines.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The weights with the choosers.</returns>
    /// <exception cref="InvalidDataException">Thrown when a target word has no tokens or a feature has no magnitude.</exception>
    private static async Task<WeightsData> TrainChoosersAsync(
        string name,
        WeightsData data,
        IReadOnlyList<ChooserRow> anchors,
        IReadOnlyList<ChooserRow> holdout,
        TrainingOptions options,
        CachingEncoder encoder,
        FitCache? fitCache,
        TextWriter output,
        Stopwatch clock,
        CancellationToken cancellationToken)
    {
        var pooled = await EncodeAsync(name, "chooser sentences", anchors.Count, i => (anchors[i].Words, anchors[i].TargetWordIndex, anchors[i].Source, anchors[i].Sentence), encoder, output, clock, cancellationToken).ConfigureAwait(false);
        var features = Features(pooled, data.Mean, anchors.Select(r => r.Source + ": " + r.Sentence).ToList());
        var (withChoosers, report) = WeightsTrainer.AttachChoosers(data, anchors, features, options, fitCache);
        output.WriteLine($"{name}: form choosers: {report.Trained.Count} trained from {anchors.Count} sentences, {report.Trained.Count(c => c.Weights.FineThreshold < LemmaWeights.Never)} with a fine threshold; {report.Skipped.Count} forms got none.");
        foreach (var skipped in report.Skipped.Take(MaxLemmasListed))
        {
            output.WriteLine($"{name}:   no chooser for '{skipped.Form}': {skipped.Reason}.");
        }

        if (holdout.Count > 0 && report.Trained.Count > 0)
        {
            await MeasureChoosersAsync(name, report, data.Mean, holdout, encoder, output, clock, cancellationToken).ConfigureAwait(false);
        }

        return withChoosers;
    }

    /// <summary>
    ///     Measures the choosers on the held-out chooser rows: the share answered at the threshold and the precision.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="report">The trained choosers.</param>
    /// <param name="mean">The global mean of the weights.</param>
    /// <param name="holdout">The held-out rows.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="output">Receives the report lines.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task MeasureChoosersAsync(string name, ChooserTraining report, float[] mean, IReadOnlyList<ChooserRow> holdout, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken)
    {
        var choosers = report.Trained.ToDictionary(c => c.Form, StringComparer.Ordinal);
        var rows = holdout.Where(r => choosers.ContainsKey(r.Form)).ToList();
        if (rows.Count == 0)
        {
            output.WriteLine($"{name}: form choosers: no held-out row belongs to a trained chooser.");
            return;
        }

        var pooled = await EncodeAsync(name, "held-out chooser sentences", rows.Count, i => (rows[i].Words, rows[i].TargetWordIndex, rows[i].Source, rows[i].Sentence), encoder, output, clock, cancellationToken).ConfigureAwait(false);
        var features = Features(pooled, mean, rows.Select(r => r.Source + ": " + r.Sentence).ToList());
        int top1 = 0, answered = 0, answeredCorrect = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var chooser = choosers[rows[i].Form];
            var scores = new float[chooser.Weights.SenseCount];
            chooser.Weights.Scores(features[i], scores);
            var (best, margin) = LemmaWeights.Rank(scores);
            var correct = string.Equals(chooser.Labels[best], rows[i].Label, StringComparison.Ordinal);
            top1 += correct ? 1 : 0;
            if (margin >= chooser.Weights.FineThreshold)
            {
                answered++;
                answeredCorrect += correct ? 1 : 0;
            }
        }

        var inv = CultureInfo.InvariantCulture;
        output.WriteLine($"{name}: form choosers on {rows.Count} held-out sentences: top-1 {(100.0 * top1 / rows.Count).ToString("F2", inv)} %, answered at the threshold {(100.0 * answered / rows.Count).ToString("F2", inv)} % with precision {(answered == 0 ? "n/a" : (100.0 * answeredCorrect / answered).ToString("F2", inv) + " %")}.");
    }

    /// <summary>
    ///     Centers the pooled vectors on the global mean and scales them to unit length.
    /// </summary>
    /// <param name="pooled">The pooled vectors.</param>
    /// <param name="mean">The global mean.</param>
    /// <param name="descriptions">A description of each row, for the message of a vector without magnitude.</param>
    /// <returns>The features.</returns>
    /// <exception cref="InvalidDataException">Thrown when a feature has no magnitude.</exception>
    private static float[][] Features(double[][] pooled, float[] mean, IReadOnlyList<string> descriptions)
    {
        var features = new float[pooled.Length][];
        for (var i = 0; i < pooled.Length; i++)
        {
            features[i] = FeatureExtractor.Center(pooled[i], mean) ?? throw new InvalidDataException($"The feature has no magnitude: {descriptions[i]}");
        }

        return features;
    }

    /// <summary>
    ///     Reports which lemmas got weights and which thresholds were found.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="data">The trained weights.</param>
    /// <param name="untrained">The lemmas that got no weights.</param>
    /// <param name="output">Receives the lines.</param>
    private static void ReportTraining(string name, WeightsData data, IReadOnlyList<(string Lemma, int AnchoredSenses)> untrained, TextWriter output)
    {
        var lemmas = data.Lemmas;
        var withGroups = lemmas.Where(l => l.SenseIds.Any(id => WordLexicon.TryGetSense(id, out var sense) && sense.GroupId is not null)).ToList();
        output.WriteLine($"{name}: trained {lemmas.Count} lemmas; fine threshold found for {lemmas.Count(l => l.Weights.FineThreshold < LemmaWeights.Never)}, never for {lemmas.Count(l => l.Weights.FineThreshold >= LemmaWeights.Never)}; group threshold found for {withGroups.Count(l => l.Weights.GroupThreshold < LemmaWeights.Never)} of {withGroups.Count} lemmas that have groups, never for {withGroups.Count(l => l.Weights.GroupThreshold >= LemmaWeights.Never)}.");
        if (untrained.Count > 0)
        {
            var listed = string.Join(", ", untrained.Take(MaxLemmasListed).Select(u => $"{u.Lemma} ({u.AnchoredSenses})"));
            output.WriteLine($"{name}: {untrained.Count} lemmas with anchors have fewer than two anchored senses and got no weights: {listed}{(untrained.Count > MaxLemmasListed ? ", ..." : string.Empty)}.");
        }
    }

    /// <summary>
    ///     Moves the thresholds of the trained lemmas to the precision target on the real-text calibration rows.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="data">The weights with their first thresholds.</param>
    /// <param name="calibration">The real held-out rows of the calibration documents.</param>
    /// <param name="calibrationOptions">The settings of the calibration.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="output">Receives the summary line.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The weights with the calibrated thresholds.</returns>
    private static async Task<WeightsData> CalibrateAsync(string name, WeightsData data, IReadOnlyList<CorpusRow> calibration, CalibrationOptions calibrationOptions, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken)
    {
        var trainedLemmas = data.Lemmas.Select(l => l.Lemma).ToHashSet(StringComparer.Ordinal);
        var rows = calibration.Where(r => trainedLemmas.Contains(r.Lemma)).ToList();
        if (rows.Count == 0)
        {
            output.WriteLine($"{name}: real-text calibration: no calibration row belongs to a trained lemma; the cross-validation thresholds are kept.");
            return data;
        }

        var pooled = await EncodeAsync(name, "real calibration sentences", rows, encoder, output, clock, cancellationToken).ConfigureAwait(false);
        var features = Features(pooled, data.Mean, rows.Select(r => r.Sentence).ToList());
        // The result of Basic for each row decides which rows reach the sense level at run time (the closed-class split)
        using var basicModel = new BasicWordSenseModel();
        var basic = new List<ClassifyResult?>(rows.Count);
        foreach (var row in rows)
        {
            var results = await basicModel.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            basic.Add(results[row.TargetWordIndex]);
        }

        var (calibrated, report) = RealCalibrator.Apply(data, rows, features, calibrationOptions, basic);
        output.WriteLine($"{name}: {report.Describe()}");
        return calibrated;
    }

    /// <summary>
    ///     Measures the half-precision storage of the weights on the held-out rows and enforces the gate.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="data">The full-precision weights.</param>
    /// <param name="holdout">The held-out rows.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="output">Receives the lines.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="InvalidDataException">Thrown when the accuracies differ by more than the gate allows.</exception>
    private static async Task CheckPrecisionAsync(string name, WeightsData data, IReadOnlyList<CorpusRow> holdout, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken)
    {
        // Only the rows whose lemma has weights can be scored, so only those are encoded
        var trainedLemmas = data.Lemmas.Select(l => l.Lemma).ToHashSet(StringComparer.Ordinal);
        var rows = holdout.Where(r => trainedLemmas.Contains(r.Lemma)).ToList();
        if (rows.Count == 0)
        {
            output.WriteLine($"{name}: half-precision gate: no held-out row belongs to a trained lemma; nothing to compare.");
            return;
        }

        var pooled = await EncodeAsync(name, "held-out sentences", rows, encoder, output, clock, cancellationToken).ConfigureAwait(false);
        var reduced = WeightsBlob.Read(WeightsBlobWriter.Write(data));
        var result = PrecisionGate.Measure(data, reduced, rows, pooled);
        output.WriteLine($"{name}: half-precision gate: {PrecisionGate.Describe(result)}");
        PrecisionGate.Enforce(result);
    }

    private static EmbeddingCache OpenCache(string directory, OnnxWordSenseModel model, TextWriter output, string name)
    {
        var spec = model.Spec;
        var onnx = spec.Files.First(f => string.Equals(f.Name, spec.OnnxFileName, StringComparison.Ordinal)).Sha256;
        var vocabulary = spec.Files.First(f => string.Equals(f.Name, spec.VocabularyFileName, StringComparison.Ordinal)).Sha256;
        var runtime = typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown";
        var cache = EmbeddingCache.Open(directory, EmbeddingCache.ComputeDigest(onnx, vocabulary, spec.LowerCase, runtime));
        var state = cache.Invalidated
            ? "the existing file is unreadable or was written for other model files and is replaced"
            : $"{cache.Loaded} entries loaded, {cache.Discarded} damaged entries dropped";
        output.WriteLine($"{name}: embedding cache {cache.Path}: {state}.");
        return cache;
    }

    private static void SaveCache(EmbeddingCache cache, TextWriter output, string name)
    {
        try
        {
            cache.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache only saves time; failing to write it never fails the training
            output.WriteLine($"{name}: warning: the embedding cache could not be saved: {ex.Message}");
        }
    }
}
