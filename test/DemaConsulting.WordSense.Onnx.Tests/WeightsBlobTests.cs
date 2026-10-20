using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the weights reader <see cref="WeightsBlob"/>. The blobs are written by the test helper
///     <see cref="BlobWriter"/>, which follows the WSX3 layout; round trips with the real writer are tested in the
///     build tool's tests.
/// </summary>
public class WeightsBlobTests
{
    private const int Dimension = 2;

    private static byte[] Hash => BlobWriter.Hash();

    private static LemmaSpec Lemma(string name, params string[] senseIds) => LemmaSpec.Of(name, Dimension, senseIds);

    private static byte[] Compressed(params LemmaSpec[] lemmas) =>
        BlobWriter.Compress(BlobWriter.Raw(BlobWriter.ModelId, Hash, Dimension, lemmas));

    /// <summary>
    ///     Builds the raw bytes of a valid blob with the lemmas "a" (two senses) and "b" (three senses), dimension 1.
    /// </summary>
    /// <returns>The raw bytes.</returns>
    private static byte[] TwoLemmasRaw() => BlobWriter.Raw(
        BlobWriter.ModelId,
        Hash,
        1,
        LemmaSpec.Of("a", 1, "a#x", "a#y"),
        LemmaSpec.Of("b", 1, "b#x", "b#y", "b#z"));

    /// <summary>
    ///     Runs an action and reports whether it threw <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <returns><c>true</c> when the action threw <see cref="InvalidDataException"/>; otherwise <c>false</c>.</returns>
    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>
    ///     A lemma reads back its sense ids in column order, with its biases and half-precision weights widened.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_FineSenseLemma_KeepsSenseIds()
    {
        // Arrange: three senses over two components, with values that a half represents exactly
        var spec = new LemmaSpec("a", ["a#x", "a#y", "a#z"], [0.5f, -1f, 2f], [1f, 0.5f, -2f, 0.25f, 4f, -0.125f]);

        // Act
        var data = WeightsBlob.Read(Compressed(spec));

        // Assert
        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal("a", lemma.Lemma);
        Assert.Equal(["a#x", "a#y", "a#z"], lemma.SenseIds);
        Assert.Equal(3, lemma.Weights.SenseCount);
        Assert.Equal(Dimension, lemma.Weights.Dimension);
        Assert.Equal([0.5f, -1f, 2f], lemma.Weights.Bias);
        Assert.Equal([1f, 0.5f, -2f, 0.25f, 4f, -0.125f], lemma.Weights.Weights);
        Assert.Empty(lemma.Choosers);
    }

    /// <summary>
    ///     The thresholds of a lemma and of a chooser read back; a threshold that is never reached stays so.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Thresholds_AreKept()
    {
        // Arrange: one lemma with both thresholds, one with a group threshold that is never reached, and a chooser
        var chooser = new ChooserSpec("left", ["leave", "left"], 1.25f, [0f, 0f], new float[2 * Dimension]);
        var first = Lemma("a", "a#x", "a#y") with { FineThreshold = 0.75f, GroupThreshold = 0.25f, Choosers = [chooser] };
        var second = Lemma("b", "b#x", "b#y") with { FineThreshold = 1.5f, GroupThreshold = LemmaWeights.Never };

        // Act
        var data = WeightsBlob.Read(Compressed(first, second));

        // Assert
        Assert.Equal(0.75f, data.Lemmas[0].Weights.FineThreshold);
        Assert.Equal(0.25f, data.Lemmas[0].Weights.GroupThreshold);
        Assert.Equal(1.5f, data.Lemmas[1].Weights.FineThreshold);
        Assert.Equal(LemmaWeights.Never, data.Lemmas[1].Weights.GroupThreshold);
        Assert.Equal(1.25f, Assert.Single(data.Lemmas[0].Choosers).Weights.FineThreshold);
    }

    /// <summary>
    ///     A form chooser reads back with its form, labels, biases and weights.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_FormChooser_IsKept()
    {
        // Arrange
        var chooser = new ChooserSpec("read", ["read", "read/Tense=Pres"], 0.5f, [0.25f, -0.5f], [1f, 2f, 3f, 4f]);
        var spec = Lemma("read", "read#x", "read#y") with { Choosers = [chooser] };

        // Act
        var data = WeightsBlob.Read(Compressed(spec));

        // Assert
        var read = Assert.Single(Assert.Single(data.Lemmas).Choosers);
        Assert.Equal("read", read.Form);
        Assert.Equal(["read", "read/Tense=Pres"], read.Labels);
        Assert.Equal([0.25f, -0.5f], read.Weights.Bias);
        Assert.Equal([1f, 2f, 3f, 4f], read.Weights.Weights);
        Assert.Equal(0.5f, read.Weights.FineThreshold);
        Assert.Equal(LemmaWeights.Never, read.Weights.GroupThreshold);
    }

    /// <summary>
    ///     A chooser with fewer than two labels, an empty or duplicate label, an empty form, a form that two choosers
    ///     share or a negative threshold is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_InvalidChooser_Throws()
    {
        // Arrange
        ChooserSpec Make(string form = "left", string[]? labels = null, float threshold = 0.5f)
        {
            labels ??= ["leave", "left"];
            return new ChooserSpec(form, labels, threshold, new float[labels.Length], new float[labels.Length * Dimension]);
        }

        // Act / Assert
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(labels: ["only"])] }))), "One label.");
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(labels: ["leave", string.Empty])] }))), "Empty label.");
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(labels: ["leave", "leave"])] }))), "Duplicate label.");
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(form: string.Empty)] }))), "Empty form.");
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(threshold: -1f)] }))), "Negative threshold.");
        Assert.True(Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make(), Make()] }))), "Form twice in one lemma.");
        Assert.True(
            Throws(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { Choosers = [Make()] }, Lemma("b", "b#x", "b#y") with { Choosers = [Make()] }))),
            "Form in two lemmas.");
    }

    /// <summary>
    ///     A blob with no lemmas reads as an empty set of weights that keeps its model id, hash and dimension.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NoLemmas_ReturnsEmptyWeights()
    {
        // Arrange / Act
        var data = WeightsBlob.Read(Compressed());

        // Assert
        Assert.Equal(BlobWriter.ModelId, data.ModelId);
        Assert.Equal(Hash, data.InventoryHash);
        Assert.Equal(Dimension, data.Dimension);
        Assert.Equal(new float[Dimension], data.Mean);
        Assert.Empty(data.Lemmas);
    }

    /// <summary>
    ///     The mean vector reads back as written.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Mean_IsKept()
    {
        // Arrange
        List<byte> bytes = [.. BlobWriter.Header(BlobWriter.ModelId, Hash, 3, [0.5f, -1.5f, 2f])];
        BlobWriter.Varint(bytes, 0);

        // Act
        var data = WeightsBlob.Read(BlobWriter.Compress([.. bytes]));

        // Assert
        Assert.Equal([0.5f, -1.5f, 2f], data.Mean);
        Assert.Equal(3, data.Dimension);
    }

    /// <summary>
    ///     The valid two-lemma blob reads back, which makes the failure matrix below meaningful.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TwoLemmaBlob_IsAccepted()
    {
        // Arrange / Act
        var data = WeightsBlob.Read(BlobWriter.Compress(TwoLemmasRaw()));

        // Assert
        Assert.Equal(["a", "b"], data.Lemmas.Select(l => l.Lemma));
        Assert.Equal(["b#x", "b#y", "b#z"], data.Lemmas[1].SenseIds);
    }

    /// <summary>
    ///     A blob cut at any byte, so at every section boundary (header, mean, lemma, thresholds, sense count, sense ids,
    ///     biases, weights, chooser count) and inside every section, is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TruncatedAtEveryLength_Throws()
    {
        // Arrange: a blob that has a chooser too, so that its sections are cut as well
        var chooser = new ChooserSpec("left", ["leave", "left"], 0.5f, [0f, 0f], [1f, 2f]);
        var raw = BlobWriter.Raw(
            BlobWriter.ModelId,
            Hash,
            1,
            LemmaSpec.Of("a", 1, "a#x", "a#y") with { Choosers = [chooser] },
            LemmaSpec.Of("b", 1, "b#x", "b#y", "b#z"));

        // Act / Assert: every proper prefix is rejected as corrupt data
        for (var length = 0; length < raw.Length; length++)
        {
            var prefix = raw[..length];
            Assert.True(
                Throws(() => WeightsBlob.Read(BlobWriter.Compress(prefix))),
                $"A blob cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     A lemma count that is larger or smaller than the lemmas present, and a sense count that disagrees with the
    ///     sense ids present, are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_CountMismatch_Throws()
    {
        // Arrange: the lemma count is the byte after the header; the sense count of the first lemma follows its name
        // (one byte of length, one byte of text) and its two thresholds
        var headerLength = BlobWriter.Header(BlobWriter.ModelId, Hash, 1).Length;
        var senseCount = headerLength + 1 + 2 + 8;
        var tooMany = TwoLemmasRaw();
        tooMany[headerLength] = 3;
        var tooFew = TwoLemmasRaw();
        tooFew[headerLength] = 1;
        var moreSenses = TwoLemmasRaw();
        moreSenses[senseCount] = 3;
        var fewerSenses = TwoLemmasRaw();
        fewerSenses[senseCount] = 1;

        // Act / Assert: the mutated bytes are really the counts
        Assert.Equal(2, TwoLemmasRaw()[headerLength]);
        Assert.Equal(2, TwoLemmasRaw()[senseCount]);
        Assert.True(Throws(() => WeightsBlob.Read(BlobWriter.Compress(tooMany))), "Lemma count too large.");
        Assert.True(Throws(() => WeightsBlob.Read(BlobWriter.Compress(tooFew))), "Lemma count too small.");
        Assert.True(Throws(() => WeightsBlob.Read(BlobWriter.Compress(moreSenses))), "Sense count too large.");

        // A sense count of one must fail because a lemma needs two senses, not by another check
        var fewer = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress(fewerSenses)));
        Assert.Contains("needs at least two", fewer.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A blob with an unsupported version in its magic, and a flag byte for sparse weights, is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_WrongVersionOrFlag_Throws()
    {
        // Arrange: another version digit, and the flag byte that follows the dimension
        var version = TwoLemmasRaw();
        version[3] = (byte)'4';
        var sparse = TwoLemmasRaw();
        sparse[BlobWriter.Header(BlobWriter.ModelId, Hash, 1).Length - 1 - 4] = 1;

        // Act / Assert
        Assert.True(Throws(() => WeightsBlob.Read(BlobWriter.Compress(version))), "Version WSX4.");
        Assert.True(Throws(() => WeightsBlob.Read(BlobWriter.Compress(sparse))), "Sparse flag.");
    }

    /// <summary>
    ///     The reader does not judge the inventory hash: whatever the blob carries is returned, and the table that
    ///     resolves the senses rejects a foreign hash (see <c>WeightedLemmaTableTests</c>).
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_AnyInventoryHash_IsReturnedUnchecked()
    {
        // Arrange: change the first byte of the hash, which follows the magic and the model id "m"
        var raw = TwoLemmasRaw();
        raw[4 + 1 + 1] = 0xEE;

        // Act
        var data = WeightsBlob.Read(BlobWriter.Compress(raw));

        // Assert
        Assert.Equal(0xEE, data.InventoryHash[0]);
        Assert.Equal(WeightsBlob.HashSize, data.InventoryHash.Length);
    }

    /// <summary>
    ///     Data that is not a compressed stream is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NotCompressed_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read([9, 8, 7, 6, 5]));
    }

    /// <summary>
    ///     A compressed stream with an unknown magic number, including the retired layout, is rejected.
    /// </summary>
    /// <param name="magic">The magic text.</param>
    [Theory]
    [InlineData("NOPE")]
    [InlineData("WSX1")]
    [InlineData("WSX2")]
    public void WeightsBlob_Read_UnknownMagic_Throws(string magic)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress(Encoding.ASCII.GetBytes(magic))));
    }

    /// <summary>
    ///     A blob that ends inside the header is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Truncated_Throws()
    {
        // Arrange / Act / Assert: a model id that is longer than the data, and a hash that is cut short
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress([.. "WSX3"u8.ToArray(), 40])));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress([.. "WSX3"u8.ToArray(), 1, (byte)'m', 1, 2, 3])));
    }

    /// <summary>
    ///     A lemma with fewer than two senses is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_LemmaWithOneSense_Throws()
    {
        // Arrange
        var raw = BlobWriter.Raw(BlobWriter.ModelId, Hash, Dimension, new LemmaSpec("a", ["a#x"], [0f], new float[Dimension]));

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress(raw)));

        // Assert
        Assert.Contains("needs at least two", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An empty sense id, a sense id that two columns name and a sense id that two lemmas name are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_EmptyOrDuplicateSenseId_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", string.Empty))));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#x"))));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y"), Lemma("b", "a#x", "b#y"))));
    }

    /// <summary>
    ///     A negative or non-finite threshold, of a lemma or of a chooser, is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NegativeThreshold_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { FineThreshold = -0.5f })));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { GroupThreshold = -0.5f })));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { FineThreshold = float.NaN })));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(Lemma("a", "a#x", "a#y") with { GroupThreshold = float.PositiveInfinity })));
    }

    /// <summary>
    ///     A weight that is not finite is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NonFiniteWeight_Throws()
    {
        // Arrange: one weight is NaN (a half keeps it)
        var spec = new LemmaSpec("a", ["a#x", "a#y"], [0f, 0f], [0f, float.NaN, 0f, 0f]);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compressed(spec)));
    }

    /// <summary>
    ///     Malformed UTF-8 in a lemma is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_MalformedUtf8Lemma_Throws()
    {
        // Arrange: replace the single text byte of the lemma "a" with a byte that is never valid UTF-8
        var raw = BlobWriter.Raw(BlobWriter.ModelId, Hash, 1, LemmaSpec.Of("a", 1, "a#x", "a#y"));
        var lemmaText = BlobWriter.Header(BlobWriter.ModelId, Hash, 1).Length + 1 + 1;
        Assert.Equal((byte)'a', raw[lemmaText]);
        raw[lemmaText] = 0xFF;

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress(raw)));

        // Assert
        Assert.IsType<DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A blob with a dimension of zero is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_ZeroDimension_Throws()
    {
        // Arrange
        List<byte> bytes = [.. BlobWriter.Header(BlobWriter.ModelId, Hash, 0)];
        BlobWriter.Varint(bytes, 0);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress([.. bytes])));
    }

    /// <summary>
    ///     Trailing bytes after the last lemma are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TrailingBytes_Throws()
    {
        // Arrange
        List<byte> bytes = [.. BlobWriter.Raw(BlobWriter.ModelId, Hash, Dimension), 0];

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(BlobWriter.Compress([.. bytes])));
    }
}
