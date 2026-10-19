using System.Diagnostics;
using System.Globalization;
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

    /// <summary>
    ///     Trains a model.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="info">The model.</param>
    /// <param name="settings">The cache and mean settings.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of lemmas trained.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent.</exception>
    public static async Task<int> TrainAsync(string repository, string corpus, ModelInfo info, TrainSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' is statistical and has no encoder weights to train; use the 'train-basic' command.");
        }

        var inventory = SenseInventory.ReadDirectory(Path.Join(corpus, "senses"));
        var anchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Join(corpus, "holdout"));
        var formsDirectory = Path.Join(corpus, "forms");
        var forms = Directory.Exists(formsDirectory)
            ? Directory.GetFiles(formsDirectory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).SelectMany(Corpus.ReadForms).ToList()
            : [];
        Corpus.EnsureDisjoint(anchors, holdout);
        inventory.Validate(anchors, holdout, forms);
        var options = new TrainingOptions(Parallelism: TrainingOptions.DefaultParallelism);
        var clock = Stopwatch.StartNew();

        using var model = (OnnxWordSenseModel)info.Create(Path.Join(repository, "model", info.ModelDirectory!));
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
            info.Name, model.ModelId, DemaConsulting.WordSense.WordLexicon.Current.ClassHash.ToArray(), anchors, inventory, options, caching, frozenMean, fitCache, output, clock, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WeightsBlobWriter.Write(data));
        output.WriteLine($"{info.Name}: trained {data.Lemmas.Count} lemmas in {clock.Elapsed:hh\\:mm\\:ss}; wrote {new FileInfo(path).Length} bytes to {path}.");
        return data.Lemmas.Count;
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
    ///     Encodes the anchors through a caching encoder and trains the weights.
    /// </summary>
    /// <param name="name">The model name used in messages.</param>
    /// <param name="modelId">The model identifier recorded in the weights.</param>
    /// <param name="classHash">The class hash of the word image.</param>
    /// <param name="anchors">The training rows.</param>
    /// <param name="inventory">The sense inventory.</param>
    /// <param name="options">The trainer settings.</param>
    /// <param name="encoder">The encoder, with its cache.</param>
    /// <param name="frozenMean">The global mean to reuse, or null to compute it.</param>
    /// <param name="fitCache">The cache of lemma fits, or null to fit every lemma.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="clock">The clock of the whole run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The weights.</returns>
    /// <exception cref="InvalidDataException">Thrown when a target word has no tokens or the corpus is inconsistent.</exception>
    internal static async Task<WeightsData> TrainDataAsync(
        string name,
        string modelId,
        byte[] classHash,
        IReadOnlyList<CorpusRow> anchors,
        SenseInventory inventory,
        TrainingOptions options,
        CachingEncoder encoder,
        float[]? frozenMean,
        FitCache? fitCache,
        TextWriter output,
        Stopwatch clock,
        CancellationToken cancellationToken)
    {
        var pooled = new double[anchors.Count][];
        var savedAtMisses = 0;
        try
        {
            for (var i = 0; i < anchors.Count; i++)
            {
                var row = anchors[i];
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
                    output.WriteLine($"{name}: processed {i + 1} of {anchors.Count} sentences: {encoder.Hits} cache hits, {encoder.Misses} encoded ({clock.Elapsed:hh\\:mm\\:ss}).");
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

        output.WriteLine($"{name}: embeddings: {encoder.Hits} cache hits, {encoder.Misses} encoded in {encoder.EncodeTime:hh\\:mm\\:ss\\.fff} ({(encoder.Cache is null ? "cache off" : $"{encoder.Cache.Count} entries cached")}).");

        if (frozenMean is not null && pooled.Length > 0 && frozenMean.Length != pooled[0].Length)
        {
            output.WriteLine($"{name}: global mean: computed from the anchors (the existing mean has {frozenMean.Length} numbers, the encoder gives {pooled[0].Length}).");
            frozenMean = null;
        }

        output.WriteLine($"{name}: fitting the classifiers of {pooled.Length} sentences on {options.Parallelism} threads ({clock.Elapsed:hh\\:mm\\:ss}).");
        var fitClock = Stopwatch.StartNew();
        var data = WeightsTrainer.TrainDense(modelId, classHash, pooled, anchors, inventory, options, frozenMean, fitCache);
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

        return data;
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
