using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;
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
    public string WeightsPath(string repository) => Path.Combine(repository, "src", Package, "Resources", "weights.bin");
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
        new("minilm", "DemaConsulting.WordSense.MiniLM", "all-MiniLM-L6-v2-int8", dir => Throttle(new MiniLMWordSenseModel(dir))),
        new("bert-base-cased", "DemaConsulting.WordSense.BertBaseCased", "bert-base-cased-int8", dir => Throttle(new BertBaseCasedWordSenseModel(dir))),
        new("basic", "DemaConsulting.WordSense.Basic", null, _ => new BasicWordSenseModel()),
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

    private static OnnxWordSenseModel Throttle(OnnxWordSenseModel model)
    {
        model.IntraOpThreads = IntraOpThreads;
        return model;
    }
}
