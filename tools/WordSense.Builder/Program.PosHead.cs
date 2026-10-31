using System.Globalization;
using System.Text;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The <c>train-pos-head</c> command of the builder.
/// </summary>
internal static partial class Program
{
    private static async Task<int> RunTrainPosHeadAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var info = ModelCatalog.Find(Require(options, "model"));
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' has no encoder, so it has no part-of-speech head.");
        }

        var grid = CGridOf(options);
        using var model = ModelCatalog.Open(info, repository);
        await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        var encoderModel = ModelCatalog.EncoderOf(model);
        using var encoder = encoderModel.OpenEncoder();
        var identity = EncoderIdentityOf(encoderModel.Spec);
        var cacheDirectory = VectorCacheOf(options, repository, info);

        var request = new PosHeadRequest(
            corpus,
            options.TryGetValue("data", out var data) ? Path.GetFullPath(data) : Path.Join(repository, "data"),
            options.TryGetValue("train", out var train) ? Path.GetFullPath(train) : null,
            options.TryGetValue("gold-dir", out var goldDirectory) ? Path.GetFullPath(goldDirectory) : null,
            options.TryGetValue("read-set", out var readSet) ? Path.GetFullPath(readSet) : null,
            cacheDirectory,
            grid,
            IsOn(options, "report-fusion"));
        var outcome = await PosHeadTrainer.TrainAsync(request, identity, (words, token) => encoder.EncodeAllAsync(words, token), output, cancellationToken).ConfigureAwait(false);
        var file = options.TryGetValue("output", out var target) ? Path.GetFullPath(target) : info.HeadPath(repository);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, outcome.Blob);
        output.Write(outcome.Report);
        output.WriteLine($"Wrote the head to {file} ({outcome.Blob.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes).");
        return 0;
    }

    /// <summary>
    ///     Identifies the encoder of a model to the head trainers.
    /// </summary>
    /// <param name="spec">The description of the model.</param>
    /// <returns>The identity: the model id, the hidden size, the digests of the ONNX file and the vocabulary, the casing and the runtime.</returns>
    private static PosEncoderInfo EncoderIdentityOf(DemaConsulting.WordSense.Onnx.ModelSpec spec)
    {
        var runtime = typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown";
        return new PosEncoderInfo(
            spec.ModelId,
            spec.HiddenSize,
            spec.Files.First(f => string.Equals(f.Name, spec.OnnxFileName, StringComparison.Ordinal)).Sha256,
            spec.Files.First(f => string.Equals(f.Name, spec.VocabularyFileName, StringComparison.Ordinal)).Sha256,
            spec.LowerCase,
            runtime);
    }

    /// <summary>
    ///     Finds the directory of the vector cache of a model.
    /// </summary>
    /// <param name="options">The options (<c>--no-cache</c>, <c>--cache</c>).</param>
    /// <param name="repository">The repository directory.</param>
    /// <param name="info">The model.</param>
    /// <returns>The directory, or null for no cache.</returns>
    private static string? VectorCacheOf(Dictionary<string, string> options, string repository, ModelInfo info)
    {
        if (IsOn(options, "no-cache"))
        {
            return null;
        }

        var root = options.TryGetValue("cache", out var cache) ? Path.GetFullPath(cache) : Path.Join(repository, ".cache", "pos-vectors");
        return Path.Join(root, info.Name);
    }

    /// <summary>
    ///     Reads the grid of regularization strengths of a head training command, with the default grid of the
    ///     part-of-speech head.
    /// </summary>
    /// <param name="options">The options (<c>--c-grid</c>).</param>
    /// <returns>The strengths to try.</returns>
    private static List<double> CGridOf(Dictionary<string, string> options) => CGridOf(options, PosHeadTrainer.DefaultCGrid);

    /// <summary>
    ///     Reads the grid of regularization strengths of a head training command.
    /// </summary>
    /// <param name="options">The options (<c>--c-grid</c>, a comma-separated list of positive numbers).</param>
    /// <param name="defaults">The grid that applies when the option is not given.</param>
    /// <returns>The strengths to try.</returns>
    /// <exception cref="ArgumentException">Thrown when the option is not a list of positive numbers.</exception>
    private static List<double> CGridOf(Dictionary<string, string> options, IReadOnlyList<double> defaults)
    {
        if (!options.TryGetValue("c-grid", out var text))
        {
            return [.. defaults];
        }

        var grid = new List<double>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) || !double.IsFinite(c) || c <= 0)
            {
                throw new ArgumentException($"The option --c-grid must be a comma-separated list of positive numbers, not '{text}'.");
            }

            grid.Add(c);
        }

        return grid.Count > 0 ? grid : throw new ArgumentException("The option --c-grid must list at least one value.");
    }
}
