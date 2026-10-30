using DemaConsulting.WordSense;
using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     A model the builder can train and evaluate.
/// </summary>
/// <param name="Name">The name used on the command line.</param>
/// <param name="Package">The directory name of the package that ships the model's weights, below <c>src</c>.</param>
/// <param name="ModelDirectory">The directory below <c>model</c> that holds the model files, or null for a model without files.</param>
/// <param name="Create">Creates the model, with the given model directory.</param>
internal sealed record ModelInfo(string Name, string Package, string? ModelDirectory, Func<string?, IWordSenseModel> Create)
{
    /// <summary>
    ///     Gets a value indicating whether the model uses an ONNX encoder.
    /// </summary>
    public bool IsEncoder => ModelDirectory is not null;

    /// <summary>
    ///     Gets the path of the weights file of the model.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The path.</returns>
    public string WeightsPath(string repository) => Path.Join(repository, "src", Package, "Resources", "weights.bin");

    /// <summary>
    ///     Gets the path of the part-of-speech head file of the model.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The path.</returns>
    public string HeadPath(string repository) => Path.Join(repository, "src", Package, "Resources", "head.bin");
}

/// <summary>
///     The models the builder knows.
/// </summary>
internal static class ModelCatalog
{
    /// <summary>
    ///     The number of threads one ONNX inference may use while building and evaluating.
    /// </summary>
    public const int IntraOpThreads = 2;

    /// <summary>
    ///     The names of the models.
    /// </summary>
    public static readonly IReadOnlyList<ModelInfo> All =
    [
        new("minilm", "DemaConsulting.WordSense.MiniLM", "all-MiniLM-L6-v2-int8", dir => Throttle(new MiniLMWordSenseModel(dir), m => m.Encoder)),
        new("bert-base-cased", "DemaConsulting.WordSense.BertBaseCased", "bert-base-cased-int8", dir => Throttle(new BertBaseCasedWordSenseModel(dir), m => m.Encoder)),
        new("basic", "DemaConsulting.WordSense", null, _ => new BasicWordSenseModel()),
    ];

    /// <summary>
    ///     Finds a model by its command-line name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is unknown.</exception>
    public static ModelInfo Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown model '{name}'; use one of: {string.Join(", ", All.Select(m => m.Name))}.");

    /// <summary>
    ///     Selects the models of a command that evaluates several: one by name, or every model in the order of the model table.
    /// </summary>
    /// <param name="name">The command-line name of a model, <c>all</c> or null for every model.</param>
    /// <returns>The models.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is unknown.</exception>
    public static IReadOnlyList<ModelInfo> Select(string? name) =>
        name is null or "all" ? [Find("basic"), Find("minilm"), Find("bert-base-cased")] : [Find(name)];

    /// <summary>
    ///     Creates a model on the model folder of the repository.
    /// </summary>
    /// <param name="info">The model.</param>
    /// <param name="repository">The repository root.</param>
    /// <returns>The model.</returns>
    public static IWordSenseModel Open(ModelInfo info, string repository)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.Create(info.ModelDirectory is null ? null : Path.Join(repository, "model", info.ModelDirectory));
    }

    /// <summary>
    ///     Ensures the files of a model when the model can be prepared; a model without files (the Basic model) is skipped.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the model is ready.</returns>
    public static Task EnsureAsync(IWordSenseModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model is IDownloadableWordSenseModel downloadable
            ? downloadable.EnsureModelAsync(null, cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the encoder model inside a composed encoder model (the model of a package wraps its encoder, which owns the
    ///     pinned files and the embedded weights), so the trainer can open the encoder; the composed model keeps owning it.
    /// </summary>
    /// <param name="model">A model created from a <see cref="ModelInfo"/> that has an encoder.</param>
    /// <returns>The encoder model.</returns>
    /// <exception cref="ArgumentException">Thrown when the model has no encoder.</exception>
    public static OnnxWordSenseModel EncoderOf(IWordSenseModel model) => model switch
    {
        MiniLMWordSenseModel minilm => minilm.Encoder,
        BertBaseCasedWordSenseModel bert => bert.Encoder,
        _ => throw new ArgumentException($"The model {model.GetType().Name} has no encoder."),
    };

    private static T Throttle<T>(T model, Func<T, OnnxWordSenseModel> encoder)
    {
        encoder(model).IntraOpThreads = IntraOpThreads;
        return model;
    }
}
