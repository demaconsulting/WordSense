using System.Diagnostics;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     Trains the weights of an encoder model from the corpus and writes them to the model's package.
/// </summary>
internal static class ModelTrainer
{
    private const int ProgressInterval = 2000;

    /// <summary>
    ///     Trains a model.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="info">The model.</param>
    /// <param name="output">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of lemmas trained.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent.</exception>
    public static async Task<int> TrainAsync(string repository, string corpus, ModelInfo info, TextWriter output, CancellationToken cancellationToken)
    {
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' is statistical and has no encoder weights to train; use the 'train-basic' command.");
        }

        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var formsDirectory = Path.Combine(corpus, "forms");
        var forms = Directory.Exists(formsDirectory)
            ? Directory.GetFiles(formsDirectory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).SelectMany(Corpus.ReadForms).ToList()
            : [];
        Corpus.EnsureDisjoint(anchors, holdout);
        inventory.Validate(anchors, holdout, forms);
        var options = new TrainingOptions();
        var clock = Stopwatch.StartNew();

        using var model = (OnnxWordSenseModel)info.Create(Path.Combine(repository, "model", info.ModelDirectory!));
        await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
        using var encoder = model.OpenEncoder();
        var pooled = new double[anchors.Count][];
        for (var i = 0; i < anchors.Count; i++)
        {
            var row = anchors[i];
            var vectors = await encoder.EncodeAsync(row.Words, [row.TargetWordIndex], cancellationToken).ConfigureAwait(false);
            pooled[i] = vectors[0] ?? throw new InvalidDataException($"{row.Source}: the target word has no tokens: {row.Sentence}");
            if ((i + 1) % ProgressInterval == 0)
            {
                output.WriteLine($"{info.Name}: encoded {i + 1} of {anchors.Count} sentences ({clock.Elapsed:hh\\:mm\\:ss}).");
            }
        }

        var data = WeightsTrainer.TrainDense(model.ModelId, DemaConsulting.WordSense.WordLexicon.Current.ClassHash.ToArray(), pooled, anchors, inventory, options);

        var path = info.WeightsPath(repository);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WeightsBlobWriter.Write(data));
        output.WriteLine($"{info.Name}: trained {data.Lemmas.Count} lemmas in {clock.Elapsed:hh\\:mm\\:ss}; wrote {new FileInfo(path).Length} bytes to {path}.");
        return data.Lemmas.Count;
    }
}
