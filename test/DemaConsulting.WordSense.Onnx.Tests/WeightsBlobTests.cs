using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the weights reader <see cref="WeightsBlob"/>. Round trips with the writer are tested in the build
///     tool's tests.
/// </summary>
public class WeightsBlobTests
{
    private static readonly byte[] Hash = [.. Enumerable.Range(1, WeightsBlob.HashSize).Select(i => (byte)i)];

    /// <summary>
    ///     Brotli-compresses raw blob bytes.
    /// </summary>
    /// <param name="raw">The raw bytes.</param>
    /// <returns>The compressed bytes.</returns>
    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            brotli.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Builds the header: magic, model id "m", class hash, dimension 1, dense flag and one mean value.
    /// </summary>
    /// <returns>The header bytes.</returns>
    private static byte[] Header() => [.. "WSX2"u8.ToArray(), 1, (byte)'m', .. Hash, 1, 0, 0, 0, 0, 0];

    /// <summary>
    ///     Builds a blob with one lemma "a" of the given kind and the given class ids (dimension 1).
    /// </summary>
    /// <param name="kind">The model kind byte.</param>
    /// <param name="classIds">The class ids, each of ASCII text.</param>
    /// <param name="lemmaByte">The single byte of the lemma text.</param>
    /// <returns>The compressed blob.</returns>
    private static byte[] OneLemma(byte kind, string[] classIds, byte lemmaByte = (byte)'a')
    {
        var rows = kind == 1 ? 1 : classIds.Length;
        List<byte> bytes = [.. Header(), 1, 1, lemmaByte, 0, 0, 0, 0, kind, (byte)classIds.Length];
        foreach (var id in classIds)
        {
            bytes.Add((byte)id.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(id));
        }

        bytes.AddRange(new byte[4 * rows]); // biases
        bytes.AddRange(new byte[4 * rows]); // weights, dimension 1
        return Compress([.. bytes]);
    }

    /// <summary>
    ///     Builds the raw (uncompressed) bytes of a valid blob with a binary lemma "a" and a one-versus-rest lemma "b"
    ///     of three classes, dimension 1.
    /// </summary>
    /// <returns>The raw bytes.</returns>
    private static byte[] TwoLemmasRaw()
    {
        List<byte> bytes = [.. Header(), 2];
        AddLemma(bytes, (byte)'a', 1, ["a#x", "a#y"]);
        AddLemma(bytes, (byte)'b', 2, ["b#x", "b#y", "b#z"]);
        return [.. bytes];
    }

    /// <summary>
    ///     Appends one lemma (name length, name, margin, kind, sense count, class ids, biases, weights) to a blob.
    /// </summary>
    /// <param name="bytes">The blob bytes to append to.</param>
    /// <param name="name">The single byte of the lemma text.</param>
    /// <param name="kind">The model kind byte.</param>
    /// <param name="classIds">The class ids, each of ASCII text.</param>
    private static void AddLemma(List<byte> bytes, byte name, byte kind, string[] classIds)
    {
        var rows = kind == 1 ? 1 : classIds.Length;
        bytes.AddRange([1, name, 0, 0, 0, 0, kind, (byte)classIds.Length]);
        foreach (var id in classIds)
        {
            bytes.Add((byte)id.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(id));
        }

        bytes.AddRange(new byte[4 * rows]);
        bytes.AddRange(new byte[4 * rows]);
    }

    /// <summary>
    ///     The valid two-lemma blob reads back, which makes the failure matrix below meaningful.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TwoLemmaBlob_IsAccepted()
    {
        var data = WeightsBlob.Read(Compress(TwoLemmasRaw()));

        Assert.Equal(["a", "b"], data.Lemmas.Select(l => l.Lemma));
        Assert.Equal(["b#x", "b#y", "b#z"], data.Lemmas[1].ClassIds);
    }

    /// <summary>
    ///     A blob cut at any byte, so at every section boundary (header, mean, lemma name, margin, kind, class ids,
    ///     biases, weights) and inside every section, is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TwoLemmasRaw();

        // Act / Assert: every proper prefix is rejected as corrupt data
        for (var length = 0; length < raw.Length; length++)
        {
            var prefix = raw[..length];
            Assert.True(
                Throws(() => WeightsBlob.Read(Compress(prefix))),
                $"A blob cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     A lemma count that is larger or smaller than the lemmas present, and a sense count that disagrees with the
    ///     class ids present, are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_CountMismatch_Throws()
    {
        // Arrange: the lemma count is the byte after the header; the first sense count is the 8th byte of the first lemma
        var headerLength = Header().Length;
        var tooMany = TwoLemmasRaw();
        tooMany[headerLength] = 3;
        var tooFew = TwoLemmasRaw();
        tooFew[headerLength] = 1;
        var moreSenses = TwoLemmasRaw();
        moreSenses[headerLength + 1 + 7] = 3;
        var fewerSenses = TwoLemmasRaw();
        fewerSenses[headerLength + 1 + 7] = 1;

        // Act / Assert
        Assert.True(Throws(() => WeightsBlob.Read(Compress(tooMany))), "Lemma count too large.");
        Assert.True(Throws(() => WeightsBlob.Read(Compress(tooFew))), "Lemma count too small.");

        // The sense-count mutations must fail for the intended reason, not by truncation or another check
        var more = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress(moreSenses)));
        Assert.Contains("'a' are binary but have 3 senses", more.Message, StringComparison.Ordinal);
        var fewer = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress(fewerSenses)));
        Assert.Contains("'a' are binary but have 1 senses", fewer.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A blob with an unsupported version in its magic, and a flag byte for sparse weights, is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_WrongVersionOrFlag_Throws()
    {
        var version = TwoLemmasRaw();
        version[3] = (byte)'3';
        var sparse = TwoLemmasRaw();
        sparse[Header().Length - 5] = 1;

        Assert.True(Throws(() => WeightsBlob.Read(Compress(version))), "Version WSX3.");
        Assert.True(Throws(() => WeightsBlob.Read(Compress(sparse))), "Sparse flag.");
    }

    /// <summary>
    ///     The reader does not judge the class hash: whatever the blob carries is returned, and the table that resolves
    ///     the classes rejects a foreign hash (see <c>WeightedLemmaTableTests</c>).
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_AnyClassHash_IsReturnedUnchecked()
    {
        var raw = TwoLemmasRaw();
        raw[2 + 4] = 0xEE;

        var data = WeightsBlob.Read(Compress(raw));

        Assert.Equal(0xEE, data.ClassHash[0]);
    }

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
    ///     Data that is not a compressed stream is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read([9, 8, 7, 6, 5]));
    }

    /// <summary>
    ///     A compressed stream with an unknown magic number, including the old layout, is rejected.
    /// </summary>
    /// <param name="magic">The magic text.</param>
    [Theory]
    [InlineData("NOPE")]
    [InlineData("WSX1")]
    public void WeightsBlob_Read_UnknownMagic_Throws(string magic)
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress(Encoding.ASCII.GetBytes(magic))));
    }

    /// <summary>
    ///     A blob that ends inside the header is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Truncated_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX2"u8.ToArray(), 40])));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX2"u8.ToArray(), 1, (byte)'m', 1, 2, 3])));
    }

    /// <summary>
    ///     A blob with no lemmas reads as an empty set of weights that keeps its class hash.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NoLemmas_ReturnsEmptyWeights()
    {
        var data = WeightsBlob.Read(Compress([.. Header(), 0]));

        Assert.Equal("m", data.ModelId);
        Assert.Equal(Hash, data.ClassHash);
        Assert.Equal(1, data.Dimension);
        Assert.Empty(data.Lemmas);
    }

    /// <summary>
    ///     A lemma reads back its class ids in column order.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_OneVersusRestLemma_KeepsClassIds()
    {
        var data = WeightsBlob.Read(OneLemma(2, ["a#x", "a#y", "a#z"]));

        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal(["a#x", "a#y", "a#z"], lemma.ClassIds);
        Assert.Equal(3, lemma.Weights.SenseCount);
    }

    /// <summary>
    ///     A binary lemma has exactly two classes; one or three are rejected, and so is a one-versus-rest lemma with fewer than two.
    /// </summary>
    /// <param name="kind">The model kind byte.</param>
    /// <param name="count">The number of class ids.</param>
    [Theory]
    [InlineData((byte)1, 1)]
    [InlineData((byte)1, 3)]
    [InlineData((byte)2, 1)]
    public void WeightsBlob_Read_WrongSenseCountForKind_Throws(byte kind, int count)
    {
        var ids = Enumerable.Range(0, count).Select(i => $"a#{i}").ToArray();

        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(kind, ids)));
    }

    /// <summary>
    ///     A binary lemma with two classes is accepted.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_BinaryLemmaWithTwoClasses_IsAccepted()
    {
        var data = WeightsBlob.Read(OneLemma(1, ["a#x", "a#y"]));

        Assert.Equal(LemmaModelKind.Binary, Assert.Single(data.Lemmas).Weights.Kind);
    }

    /// <summary>
    ///     An empty class id and a class id that two columns name are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_EmptyOrDuplicateClassId_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(2, ["a#x", string.Empty])));
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(2, ["a#x", "a#x"])));
    }

    /// <summary>
    ///     Malformed UTF-8 in a lemma is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_MalformedUtf8Lemma_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(1, ["a#x", "a#y"], lemmaByte: 0xFF)));

        Assert.IsType<DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A blob with a dimension of zero is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_ZeroDimension_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX2"u8.ToArray(), 1, (byte)'m', .. Hash, 0, 0, 0])));
    }

    /// <summary>
    ///     Trailing bytes after the last lemma are rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_TrailingBytes_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. Header(), 0, 0])));
    }
}
