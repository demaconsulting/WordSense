using System.Security.Cryptography;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Keeps the part-of-speech heads embedded in the model packages tied to the training file they were fitted on, so
///     that a change of the corpus without a retrain fails the build.
/// </summary>
public sealed class PosHeadProvenanceTests
{
    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    /// <summary>
    ///     The training-file digest in the header of each embedded head is the digest of the committed representative training
    ///     split, the head is bound to the identifier of its model and was fitted with the variant the library reads.
    /// </summary>
    /// <param name="model">The command-line name of the model.</param>
    /// <param name="modelId">The identifier of the encoder model.</param>
    /// <returns>A task that completes when the check is done.</returns>
    [Theory]
    [InlineData("minilm", "all-MiniLM-L6-v2-int8")]
    [InlineData("bert-base-cased", "bert-base-cased-int8")]
    public async Task WordSenseBuilder_PosHeadProvenance_HeadsAreTrainedOnTheCommittedTrainingFile(string model, string modelId)
    {
        // Arrange
        var root = FindRepositoryRoot();
        var headFile = ModelCatalog.Find(model).HeadPath(root);
        var trainFile = OpenCorpus.DataFile(Path.Join(root, "corpus"), OpenCorpus.Representative, "train");

        // Act
        var head = PosHeadBlob.Read(await File.ReadAllBytesAsync(headFile, TestContext.Current.CancellationToken));
        var corpusDigest = SHA256.HashData(await File.ReadAllBytesAsync(trainFile, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(corpusDigest, head.TrainSha256);
        Assert.Equal(modelId, head.ModelId);
        Assert.Equal(PosHeadTrainer.Variant, head.Variant);
    }
}
