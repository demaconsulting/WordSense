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
internal sealed record TrainSettings(string? CacheRoot, bool RecomputeMean = false)
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
    ///     their target word is no form of their lemma are reported as warnings and still train. Form choosers are not
    ///     trained: the corpus has no rows for them yet.
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
        var options = new TrainingOptions(Parallelism: TrainingOptions.DefaultParallelism);
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
            info.Name, model.ModelId, WordLexicon.Current.InventoryHash.ToArray(), anchors, options, caching, frozenMean, fitCache, output, clock, cancellationToken, holdout).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WeightsBlobWriter.Write(data));
        output.WriteLine($"{info.Name}: trained {data.Lemmas.Count} lemmas in {clock.Elapsed:hh\\:mm\\:ss}; wrote {new FileInfo(path).Length} bytes to {path}.");
        return data.Lemmas.Count;
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
    /// <returns>The full-precision weights.</returns>
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
        IReadOnlyList<CorpusRow>? holdout = null)
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
        var trained = WeightsTrainer.TrainDense(modelId, inventoryHash, pooled, anchors, options, frozenMean, fitCache);
        var data = trained.Data;
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

        ReportTraining(name, trained, output);
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
    private static async Task<double[][]> EncodeAsync(string name, string what, IReadOnlyList<CorpusRow> rows, CachingEncoder encoder, TextWriter output, Stopwatch clock, CancellationToken cancellationToken)
    {
        var pooled = new double[rows.Count][];
        var savedAtMisses = encoder.Misses;
        try
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                pooled[i] = await encoder.EncodeAsync(row.Words, row.TargetWordIndex, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"{row.Source}: the target word has no tokens: {row.Sentence}");
                if (encoder.Cache is not null && encoder.Misses - savedAtMisses >= CheckpointMisses)
                {
                    // A long encode must not be lost when it is interrupted
                    SaveCache(encoder.Cache, output, name);
                    savedAtMisses = encoder.Misses;
                }

                if ((i + 1) % ProgressInterval == 0)
                {
                    output.WriteLine($"{name}: processed {i + 1} of {rows.Count} {what}: {encoder.Hits} cache hits, {encoder.Misses} encoded ({clock.Elapsed:hh\\:mm\\:ss}).");
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
    ///     Reports which lemmas got weights and which thresholds were found.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="trained">The training result.</param>
    /// <param name="output">Receives the lines.</param>
    private static void ReportTraining(string name, TrainedWeights trained, TextWriter output)
    {
        var lemmas = trained.Data.Lemmas;
        var withGroups = lemmas.Where(l => l.SenseIds.Any(id => WordLexicon.TryGetSense(id, out var sense) && sense.GroupId is not null)).ToList();
        output.WriteLine($"{name}: trained {lemmas.Count} lemmas; fine threshold found for {lemmas.Count(l => l.Weights.FineThreshold < LemmaWeights.Never)}, never for {lemmas.Count(l => l.Weights.FineThreshold >= LemmaWeights.Never)}; group threshold found for {withGroups.Count(l => l.Weights.GroupThreshold < LemmaWeights.Never)} of {withGroups.Count} lemmas that have groups, never for {withGroups.Count(l => l.Weights.GroupThreshold >= LemmaWeights.Never)}.");
        if (trained.Untrained.Count > 0)
        {
            var listed = string.Join(", ", trained.Untrained.Take(MaxLemmasListed).Select(u => $"{u.Lemma} ({u.AnchoredSenses})"));
            output.WriteLine($"{name}: {trained.Untrained.Count} lemmas with anchors have fewer than two anchored senses and got no weights: {listed}{(trained.Untrained.Count > MaxLemmasListed ? ", ..." : string.Empty)}.");
        }
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
