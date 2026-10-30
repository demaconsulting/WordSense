using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="PosHeadBlob"/> and the binding checks of <see cref="PosHead.LoadEmbedded"/>.
/// </summary>
public class PosHeadBlobTests
{
    private const string Resource = "head.bin";
    private static readonly string EncoderHex = Convert.ToHexString(HeadSpec.Create().EncoderSha256).ToLowerInvariant();

    private static PosHead Load(byte[] blob, string modelId = "m", int hiddenSize = 4, string? encoder = null) =>
        PosHead.LoadEmbedded(new FixtureAssembly(Resource, blob), Resource, modelId, hiddenSize, encoder ?? EncoderHex);

    /// <summary>
    ///     A head written with the test writer reads back with every field, and the weights are the half-precision values.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_RoundTrip_ReturnsEveryField()
    {
        // Arrange
        var spec = HeadSpec.Create();

        // Act
        var data = PosHeadBlob.Read(HeadBlobWriter.Write(spec));

        // Assert: the weights are multiples of 0.25 and survive half precision exactly
        Assert.Equal("m", data.ModelId);
        Assert.Equal(4, data.HiddenSize);
        Assert.Equal(spec.EncoderSha256, data.EncoderSha256);
        Assert.Equal(PosHeadBlob.WordAndNeighbors, data.FeatureLayout);
        Assert.Equal(Enum.GetNames<PartOfSpeech>(), data.ClassNames);
        Assert.Equal(new byte[PosHeadBlob.HashSize], data.TrainSha256);
        Assert.Equal(0.01f, data.C);
        Assert.Equal("word+-1", data.Variant);
        Assert.Equal(spec.Weights, data.Weights);
        Assert.Equal(spec.Biases, data.Biases);
    }

    /// <summary>
    ///     A head bound to its encoder, hidden size, model and classes loads.
    /// </summary>
    [Fact]
    public void PosHead_LoadEmbedded_MatchingBinding_Loads()
    {
        // Arrange: a head blob written for hidden size four and model "m"
        var blob = HeadBlobWriter.Write(HeadSpec.Create());

        // Act: load it with the matching binding
        var head = Load(blob);

        // Assert: the head reports its hidden size and one class per part of speech
        Assert.Equal(4, head.HiddenSize);
        Assert.Equal(Enum.GetValues<PartOfSpeech>().Length, head.ClassCount);
    }

    /// <summary>
    ///     A head trained on another encoder file than the pinned one is rejected.
    /// </summary>
    [Fact]
    public void PosHead_LoadEmbedded_WrongEncoderDigest_Throws()
    {
        // Arrange: a valid blob and the digest of a different encoder file
        var blob = HeadBlobWriter.Write(HeadSpec.Create());
        var other = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant();

        // Act: load the blob against the other digest
        var ex = Assert.Throws<InvalidDataException>(() => Load(blob, encoder: other));

        // Assert: the message names the encoder mismatch
        Assert.Contains("another encoder file", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A head whose classes or class order differ from the parts of speech of the library is rejected.
    /// </summary>
    [Fact]
    public void PosHead_LoadEmbedded_WrongClassOrder_Throws()
    {
        // Arrange: Noun and Verb swapped, and a renamed class
        var swapped = Enum.GetNames<PartOfSpeech>();
        (swapped[0], swapped[2]) = (swapped[2], swapped[0]);
        var renamed = Enum.GetNames<PartOfSpeech>();
        renamed[9] = "Modal";

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => Load(HeadBlobWriter.Write(HeadSpec.Create() with { ClassNames = swapped })));
        Assert.Throws<InvalidDataException>(() => Load(HeadBlobWriter.Write(HeadSpec.Create() with { ClassNames = renamed })));
    }

    /// <summary>
    ///     A head for another model, hidden size or feature layout is rejected.
    /// </summary>
    [Fact]
    public void PosHead_LoadEmbedded_WrongModelSizeOrLayout_Throws()
    {
        // Arrange: a valid blob to load with a wrong model id, a wrong hidden size, and a blob with another feature layout
        var blob = HeadBlobWriter.Write(HeadSpec.Create());

        // Act / Assert: each mismatch is rejected
        Assert.Throws<InvalidDataException>(() => Load(blob, modelId: "other"));
        Assert.Throws<InvalidDataException>(() => Load(blob, hiddenSize: 5));
        Assert.Throws<InvalidDataException>(() => Load(HeadBlobWriter.Write(HeadSpec.Create() with { FeatureLayout = 2 })));
    }

    /// <summary>
    ///     A missing resource is reported as invalid data.
    /// </summary>
    [Fact]
    public void PosHead_LoadEmbedded_MissingResource_Throws()
    {
        // Arrange: an assembly that lacks the requested resource
        // Act / Assert: loading throws
        Assert.Throws<InvalidDataException>(() => PosHead.LoadEmbedded(new FixtureAssembly("other", []), Resource, "m", 4, EncoderHex));
    }

    /// <summary>
    ///     A flipped byte anywhere in the blob fails the trailing hash.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_FlippedByte_ThrowsChecksum()
    {
        // Arrange: a valid blob
        var blob = HeadBlobWriter.Write(HeadSpec.Create());

        // Act / Assert: flipping a byte at each position fails the checksum
        foreach (var index in new[] { 4, 10, blob.Length / 2, blob.Length - PosHeadBlob.HashSize - 1, blob.Length - 1 })
        {
            var damaged = (byte[])blob.Clone();
            damaged[index] ^= 0x40;
            var ex = Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(damaged));
            Assert.Contains("checksum", ex.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A blob with the wrong magic, a truncated blob and an empty blob are rejected.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_BadMagicOrTruncated_Throws()
    {
        // Arrange: a valid blob and a copy with a corrupted magic
        var blob = HeadBlobWriter.Write(HeadSpec.Create());
        var badMagic = (byte[])blob.Clone();
        badMagic[3] = (byte)'2';

        // Act / Assert: the bad magic, the empty blob and every truncation are rejected
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(badMagic));
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read([]));
        foreach (var length in new[] { 1, 4, 20, blob.Length / 2, blob.Length - 1 })
        {
            Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(blob.AsSpan(0, length)));
        }
    }

    /// <summary>
    ///     A body that is well formed up to a point and then truncated, with a valid hash, is rejected by the structure checks.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_TruncatedBodyWithValidHash_Throws()
    {
        // Arrange: the body of a valid head
        var body = HeadBlobWriter.WriteBody(HeadSpec.Create());

        // Act / Assert: every truncation, re-hashed to be valid, is rejected
        foreach (var length in new[] { 5, 12, 60, body.Length / 2, body.Length - 1 })
        {
            var cut = body[..length];
            byte[] blob = [.. cut, .. SHA256.HashData(cut)];
            Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(blob));
        }

        // Trailing bytes after the last section are rejected too
        byte[] padded = [.. body, 0];
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read([.. padded, .. SHA256.HashData(padded)]));
    }

    /// <summary>
    ///     A section of the wrong length, a duplicate section, a missing head section and non-finite values are rejected.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_InvalidSections_Throws()
    {
        // Arrange: a valid spec to damage in four ways
        var spec = HeadSpec.Create();

        // Act / Assert: each damaged blob is rejected
        // Wrong weight count: one weight too few
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Weights = spec.Weights[1..] })));

        // A second section with the id of the head
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Sections = [(PosHeadBlob.PosSection, true, [])] })));

        // A non-finite bias and a non-finite weight
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Biases = [.. spec.Biases.Select((b, i) => i == 3 ? float.NaN : b)] })));
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Weights = [.. spec.Weights.Select((w, i) => i == 5 ? float.PositiveInfinity : w)] })));
    }

    /// <summary>
    ///     A section that the reader does not know is skipped when it is optional and rejected when it is required.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_UnknownSection_SkippedWhenOptionalRejectedWhenRequired()
    {
        // Arrange: a valid spec and a five-byte payload for a section the reader does not know
        var spec = HeadSpec.Create();
        byte[] payload = [1, 2, 3, 4, 5];

        // Act: read the blob with the unknown section marked optional, then required
        var optional = PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Sections = [(2, false, payload)] }));
        var required = Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(HeadBlobWriter.Write(spec with { Sections = [(2, true, payload)] })));

        // Assert: the optional section is skipped and the required one is named in the error
        Assert.Equal(spec.Weights, optional.Weights);
        Assert.Contains("required section 2", required.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A blob without a part-of-speech section is rejected.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_NoHeadSection_Throws()
    {
        // Arrange: build a blob whose section list is empty by cutting the part-of-speech section: write the body of a valid
        // head, then rewrite its section count to zero and drop the section bytes
        var body = HeadBlobWriter.WriteBody(HeadSpec.Create());
        var prefix = SectionStart(body);
        byte[] empty = [.. body[..prefix], 0];

        // Act / Assert: reading the blob throws
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read([.. empty, .. SHA256.HashData(empty)]));
    }

    /// <summary>
    ///     A hidden size above the bound is rejected as invalid data, whether the products computed from it would overflow
    ///     (1,000,000,000 × 3, 700,000,000 × 3 × 10) or not.
    /// </summary>
    [Theory]
    [InlineData(PosHeadBlob.MaxHiddenSize + 1)]
    [InlineData(700_000_000)]
    [InlineData(1_000_000_000)]
    [InlineData(int.MaxValue)]
    public void PosHeadBlob_Read_OversizedHiddenSize_ThrowsInvalidData(int hiddenSize)
    {
        // Arrange: a structurally valid blob that states the oversized hidden size
        var blob = HeadBlobWriter.Write(HeadSpec.Create() with { HiddenSize = hiddenSize });

        // Act / Assert: the exact type, so an OverflowException would fail the test
        var ex = Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(blob));
        Assert.Contains("hidden size", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Load(blob, hiddenSize: hiddenSize));
    }

    /// <summary>
    ///     The largest hidden size is accepted by the size checks and then rejected only for its missing weights.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_MaximumHiddenSizeWithoutWeights_ThrowsInvalidData()
    {
        // Arrange: a blob that states the largest allowed hidden size but carries only the small weights
        var blob = HeadBlobWriter.Write(HeadSpec.Create() with { HiddenSize = PosHeadBlob.MaxHiddenSize });

        // Act: read the blob
        var ex = Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(blob));

        // Assert: the error is about the weight length, not the hidden size
        Assert.Contains("wrong length", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A varint whose fifth byte sets a bit above bit 30, or continues, is rejected instead of wrapping to a small
    ///     number: the feature layout 0x10_80_80_80_81 would wrap to 1, the layout of a valid head.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x81, 0x80, 0x80, 0x80, 0x10 })]
    [InlineData(new byte[] { 0x81, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(new byte[] { 0x81, 0x80, 0x80, 0x80, 0xFF })]
    [InlineData(new byte[] { 0x81, 0x80, 0x80, 0x80, 0x80, 0x00 })]
    public void PosHeadBlob_Read_VarintWithInvalidFifthByte_ThrowsInvalidData(byte[] layout)
    {
        // Arrange: replace the one-byte feature layout (after magic, model id, hidden size and digest) and re-hash
        var body = HeadBlobWriter.WriteBody(HeadSpec.Create());
        const int layoutAt = 4 + 2 + 1 + PosHeadBlob.HashSize;
        Assert.Equal(PosHeadBlob.WordAndNeighbors, body[layoutAt]);
        byte[] changed = [.. body[..layoutAt], .. layout, .. body[(layoutAt + 1)..]];

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read([.. changed, .. SHA256.HashData(changed)]));
    }

    /// <summary>
    ///     The largest value that fits, 2,147,483,647 in five bytes, is still read.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_VarintOfMaximumInt_IsReadAsThatNumber()
    {
        // Arrange: the feature layout int.MaxValue; the reader accepts the number and the binding rejects the layout
        var body = HeadBlobWriter.WriteBody(HeadSpec.Create());
        const int layoutAt = 4 + 2 + 1 + PosHeadBlob.HashSize;
        byte[] changed = [.. body[..layoutAt], 0xFF, 0xFF, 0xFF, 0xFF, 0x07, .. body[(layoutAt + 1)..]];

        // Act
        var data = PosHeadBlob.Read([.. changed, .. SHA256.HashData(changed)]);

        // Assert
        Assert.Equal(int.MaxValue, data.FeatureLayout);
    }

    /// <summary>
    ///     A class count above the bound, or one that claims more classes than the blob has bytes, is rejected as invalid
    ///     data before any list is allocated, however large the claimed number is.
    /// </summary>
    [Fact]
    public void PosHeadBlob_Read_ExcessiveClassCount_ThrowsInvalidData()
    {
        // Arrange: one more class than the bound, and a body that claims 2,147,483,647 classes (the count follows the one-byte feature layout)
        var tooMany = HeadBlobWriter.Write(HeadSpec.Create() with { ClassNames = [.. Enumerable.Range(0, PosHeadBlob.MaxClassCount + 1).Select(i => "c" + i)] });
        var body = HeadBlobWriter.WriteBody(HeadSpec.Create());
        const int countAt = 4 + 2 + 1 + PosHeadBlob.HashSize + 1;
        byte[] claimed = [.. body[..countAt], 0xFF, 0xFF, 0xFF, 0xFF, 0x07, .. body[(countAt + 1)..]];

        // Act / Assert: the exact type, so an OverflowException or an OutOfMemoryException would fail the test
        var ex = Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read(tooMany));
        Assert.Contains("too many classes", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PosHeadBlob.Read([.. claimed, .. SHA256.HashData(claimed)]));
    }

    /// <summary>
    ///     The expected class names are the names of the parts of speech in order.
    /// </summary>
    [Fact]
    public void PosHeadBlob_ExpectedClassNames_AreThePartsOfSpeechInOrder()
    {
        // Arrange: the fixed list of part-of-speech names
        // Act / Assert: the expected class names equal the list in enum order
        Assert.Equal(
            ["Noun", "Pronoun", "Verb", "Adjective", "Adverb", "Preposition", "Conjunction", "Interjection", "Determiner", "Auxiliary"],
            PosHeadBlob.ExpectedClassNames());
    }

    private static int SectionStart(byte[] body)
    {
        // magic, "m" (length 1 + 1 byte), hidden size (1), digest (32), layout (1), class count (1) and names, train hash (32), C (4), variant (1 + 7)
        var p = 4 + 2 + 1 + PosHeadBlob.HashSize + 1 + 1;
        foreach (var name in Enum.GetNames<PartOfSpeech>())
        {
            p += 1 + name.Length;
        }

        p += PosHeadBlob.HashSize + sizeof(float) + 1 + "word+-1".Length;
        Assert.Equal(1, body[p]);
        return p;
    }
}
