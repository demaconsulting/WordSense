using System.Security.Cryptography;
using DemaConsulting.WordSense.Onnx;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Tests that the part-of-speech head embedded in the MiniLM package belongs to the pinned MiniLM encoder file and to
///     the parts of speech of the library, and that the model loads and checks it without any model file.
/// </summary>
public class EmbeddedHeadTests
{
    /// <summary>
    ///     The MiniLM head loads with the weights, belongs to the pinned MiniLM encoder file and has the classes of the
    ///     library; its posterior is a distribution and its restriction keeps only the allowed class.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_EmbeddedHead_MatchesEncoderAndClasses()
    {
        // Arrange: the encoder model of the package, which needs no model file for its head
        using var model = new MiniLMEncoderModel(null, 128);

        // Act: the model loads the embedded head, checking its binding to the pinned encoder file, the hidden size and the class order
        var head = model.GetHead();

        // Assert: the identity, the classes, the layout and the encoder digest of the head
        Assert.NotNull(head);
        Assert.Equal("all-MiniLM-L6-v2-int8", head.Data.ModelId);
        Assert.Equal(384, head.HiddenSize);
        Assert.Equal(384, head.Data.HiddenSize);
        Assert.Equal(Enum.GetValues<PartOfSpeech>().Length, head.ClassCount);
        Assert.Equal(Enum.GetNames<PartOfSpeech>(), head.Data.ClassNames);
        Assert.Equal(PosHeadBlob.WordAndNeighbors, head.Data.FeatureLayout);
        Assert.Equal("word+-1", head.Data.Variant);
        var onnx = model.Spec.Files.First(f => f.Name == model.Spec.OnnxFileName);
        Assert.Equal(onnx.Sha256, Convert.ToHexString(head.Data.EncoderSha256).ToLowerInvariant());

        // A posterior is a probability distribution, and the restriction to the parts of speech of a spelling keeps only those
        var posterior = head.Posterior(null, new double[384], null);
        Assert.Equal(1.0, posterior.Sum(), 9);
        var restricted = PosHead.Restrict(posterior, 1 << (int)PartOfSpeech.Noun);
        Assert.Equal(1.0, restricted[(int)PartOfSpeech.Noun], 9);
    }

    /// <summary>
    ///     The embedded head read against another encoder file, or another model id, is rejected, so a changed encoder
    ///     file fails loudly.
    /// </summary>
    [Fact]
    public void MiniLMWordSenseModel_EmbeddedHead_AgainstAnotherEncoderFile_IsRejected()
    {
        // Arrange: the head resource of the package and the digest of some other file
        using var model = new MiniLMEncoderModel(null, 128);
        var other = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant();
        var assembly = typeof(MiniLMWordSenseModel).Assembly;

        // Act / Assert: another encoder digest and another model id both fail the binding
        Assert.Throws<InvalidDataException>(() => PosHead.LoadEmbedded(assembly, model.Spec.HeadResourceName!, model.Spec.ModelId, model.HiddenSize, other));
        Assert.Throws<InvalidDataException>(() => PosHead.LoadEmbedded(assembly, model.Spec.HeadResourceName!, "other-model", model.HiddenSize, model.Spec.Files.First(f => f.Name == model.Spec.OnnxFileName).Sha256));
    }
}
