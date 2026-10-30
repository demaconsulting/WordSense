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
        var spec = encoderModel.Spec;
        using var encoder = encoderModel.OpenEncoder();
        var runtime = typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown";
        var identity = new PosEncoderInfo(
            spec.ModelId,
            spec.HiddenSize,
            spec.Files.First(f => string.Equals(f.Name, spec.OnnxFileName, StringComparison.Ordinal)).Sha256,
            spec.Files.First(f => string.Equals(f.Name, spec.VocabularyFileName, StringComparison.Ordinal)).Sha256,
            spec.LowerCase,
            runtime);

        string? cacheDirectory = null;
        if (!IsOn(options, "no-cache"))
        {
            var root = options.TryGetValue("cache", out var cache) ? Path.GetFullPath(cache) : Path.Join(repository, ".cache", "pos-vectors");
            cacheDirectory = Path.Join(root, info.Name);
        }

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

    private static List<double> CGridOf(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("c-grid", out var text))
        {
            return [.. PosHeadTrainer.DefaultCGrid];
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
