using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that the part-of-speech heads embedded in the model packages of this repository belong to their encoder and
///     to the parts of speech of the library, and that a model loads and checks its head without any model file.
/// </summary>
public class EmbeddedHeadTests
{
    private static void AssertHead(OnnxWordSenseModel model, string modelId, int hiddenSize)
    {
        // The model loads the embedded head with the weights: its binding to the pinned encoder file, the hidden size and
        // the class order are checked by the load
        var head = model.GetHead();

        Assert.NotNull(head);
        Assert.Equal(modelId, head.Data.ModelId);
        Assert.Equal(hiddenSize, head.HiddenSize);
        Assert.Equal(hiddenSize, head.Data.HiddenSize);
        Assert.Equal(Enum.GetValues<PartOfSpeech>().Length, head.ClassCount);
        Assert.Equal(Enum.GetNames<PartOfSpeech>(), head.Data.ClassNames);
        Assert.Equal(PosHeadBlob.WordAndNeighbors, head.Data.FeatureLayout);
        Assert.Equal("word+-1", head.Data.Variant);
        var onnx = model.Spec.Files.First(f => f.Name == model.Spec.OnnxFileName);
        Assert.Equal(onnx.Sha256, Convert.ToHexString(head.Data.EncoderSha256).ToLowerInvariant());

        // A posterior is a probability distribution, and the restriction to the parts of speech of a spelling keeps only those
        var vector = new double[hiddenSize];
        var posterior = head.Posterior(null, vector, null);
        Assert.Equal(1.0, posterior.Sum(), 9);
        var restricted = PosHead.Restrict(posterior, 1 << (int)PartOfSpeech.Noun);
        Assert.Equal(1.0, restricted[(int)PartOfSpeech.Noun], 9);
    }

    /// <summary>
    ///     The MiniLM head loads, belongs to the pinned MiniLM encoder file and has the classes of the library.
    /// </summary>
    [Fact]
    public void EmbeddedHead_MiniLM_MatchesEncoderAndClasses()
    {
        using var model = new MiniLMEncoderModel(null, 128);

        AssertHead(model, "all-MiniLM-L6-v2-int8", 384);
    }

    /// <summary>
    ///     The BertBaseCased head loads, belongs to the pinned BERT encoder file and has the classes of the library.
    /// </summary>
    [Fact]
    public void EmbeddedHead_BertBaseCased_MatchesEncoderAndClasses()
    {
        using var model = new BertBaseCasedEncoderModel(null, 128);

        AssertHead(model, "bert-base-cased-int8", 768);
    }

    /// <summary>
    ///     A head read against the pinned file of the other encoder is rejected, so a changed encoder fails loudly.
    /// </summary>
    [Fact]
    public void EmbeddedHead_AgainstTheOtherEncoder_IsRejected()
    {
        using var minilm = new MiniLMEncoderModel(null, 128);
        using var bert = new BertBaseCasedEncoderModel(null, 128);
        var bertFile = bert.Spec.Files.First(f => f.Name == bert.Spec.OnnxFileName);

        Assert.Throws<InvalidDataException>(() => PosHead.LoadEmbedded(typeof(MiniLMWordSenseModel).Assembly, minilm.Spec.HeadResourceName!, minilm.Spec.ModelId, minilm.HiddenSize, bertFile.Sha256));
        Assert.Throws<InvalidDataException>(() => PosHead.LoadEmbedded(typeof(MiniLMWordSenseModel).Assembly, minilm.Spec.HeadResourceName!, bert.Spec.ModelId, minilm.HiddenSize, bertFile.Sha256));
    }
}
