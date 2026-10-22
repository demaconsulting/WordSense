using System.Globalization;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx.Tests;

namespace WordSense.Builder.Tests;

/// <summary>
///     Keeps the part-of-speech accuracy of each model on the simplified-technical-English challenge set
///     (<c>corpus/open/targeted/data/test.tsv</c>, 32 sentences chosen for their hard cases) above the floor recorded in
///     <c>corpus/suite/ste-floors.tsv</c>, like the reviewed suite does for its rows.
/// </summary>
/// <remarks>
///     The floor is the accuracy measured when the file was written, truncated to one decimal. It is raised in a reviewed
///     commit when a model improves and is never lowered to hide a regression (see <c>corpus/suite/README.md</c>). The
///     encoder tests are skipped when the model files are not available (a checkout without Git LFS content).
/// </remarks>
public sealed class ChallengeSetFloorTests
{
    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    private static async Task AssertAboveFloorAsync(string modelName, CancellationToken cancellationToken)
    {
        var root = FindRepositoryRoot().FullName;
        var floors = File.ReadLines(Path.Join(root, "corpus", "suite", "ste-floors.tsv")).Skip(1).Select(l => l.Split('\t')).ToDictionary(c => c[0], c => (All: double.Parse(c[1], CultureInfo.InvariantCulture), Ambiguous: double.Parse(c[2], CultureInfo.InvariantCulture)), StringComparer.Ordinal);
        var sentences = OpenCorpus.ReadFile(OpenCorpus.DataFile(Path.Join(root, "corpus"), OpenCorpus.Targeted, "test")).Select(OpenCorpus.ToTagged).ToList();
        using var model = ModelCatalog.Open(ModelCatalog.Find(modelName), root);
        try
        {
            await model.EnsureModelAsync(null, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            ModelFileCheck.Unavailable("The model files are not available: " + ex.Message);
        }

        var score = await TaggerEvaluator.EvaluateAsync(RunningTextEvaluator.ChallengeSetName, model, sentences, cancellationToken);

        var (all, ambiguous) = (100.0 * score.Correct / score.Scored, 100.0 * score.AmbiguousCorrect / score.Ambiguous);
        Assert.True(all >= floors[modelName].All, $"{modelName}: {all:F2}% of the tokens of the challenge set, below the floor {floors[modelName].All:F1}%.");
        Assert.True(ambiguous >= floors[modelName].Ambiguous, $"{modelName}: {ambiguous:F2}% of the ambiguous tokens of the challenge set, below the floor {floors[modelName].Ambiguous:F1}%.");
    }

    /// <summary>
    ///     The Basic model keeps its accuracy on the challenge set.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public Task WordSenseBuilder_ChallengeSet_Basic_StaysAboveItsFloor() => AssertAboveFloorAsync("basic", TestContext.Current.CancellationToken);

    /// <summary>
    ///     The MiniLM model keeps its accuracy on the challenge set.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public Task WordSenseBuilder_ChallengeSet_MiniLM_StaysAboveItsFloor() => AssertAboveFloorAsync("minilm", TestContext.Current.CancellationToken);

    /// <summary>
    ///     The BertBaseCased model keeps its accuracy on the challenge set.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public Task WordSenseBuilder_ChallengeSet_BertBaseCased_StaysAboveItsFloor() => AssertAboveFloorAsync("bert-base-cased", TestContext.Current.CancellationToken);
}
