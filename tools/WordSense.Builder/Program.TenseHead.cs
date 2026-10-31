using System.Globalization;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The <c>train-tense-head</c> command of the builder.
/// </summary>
internal static partial class Program
{
    /// <summary>
    ///     Runs <c>train-tense-head</c>: adds the tense head to the head file of an encoder model and prints the report.
    /// </summary>
    /// <param name="options">The options of the command.</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer of the report.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The exit code, 0 when the head file was written.</returns>
    /// <exception cref="ArgumentException">Thrown when the model has no encoder.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the head file to extend does not exist.</exception>
    private static async Task<int> RunTrainTenseHeadAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var info = ModelCatalog.Find(Require(options, "model"));
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' has no encoder, so it has no tense head.");
        }

        var file = options.TryGetValue("output", out var target) ? Path.GetFullPath(target) : info.HeadPath(repository);
        var source = info.HeadPath(repository);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"The head file '{source}' does not exist; run train-pos-head first.", source);
        }

        var existing = DemaConsulting.WordSense.Onnx.PosHeadBlob.Read(File.ReadAllBytes(source));
        using var model = ModelCatalog.Open(info, repository);
        await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        var encoderModel = ModelCatalog.EncoderOf(model);
        using var encoder = encoderModel.OpenEncoder();
        var request = new TenseHeadRequest(
            corpus,
            options.TryGetValue("data", out var data) ? Path.GetFullPath(data) : Path.Join(repository, "data"),
            options.TryGetValue("gold-dir", out var goldDirectory) ? Path.GetFullPath(goldDirectory) : null,
            options.TryGetValue("read-set", out var readSet) ? Path.GetFullPath(readSet) : null,
            VectorCacheOf(options, repository, info),
            CGridOf(options, TenseHeadTrainer.DefaultCGrid),
            IsOn(options, "report-fusion"));
        var outcome = await TenseHeadTrainer.TrainAsync(request, EncoderIdentityOf(encoderModel.Spec), existing, (words, token) => encoder.EncodeAllAsync(words, token), output, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, outcome.Blob);
        output.Write(outcome.Report);
        output.WriteLine($"Wrote the head to {file} ({outcome.Blob.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes).");
        return 0;
    }
}
