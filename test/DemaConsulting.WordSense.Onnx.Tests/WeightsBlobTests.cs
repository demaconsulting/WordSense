using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for the weights reader <see cref="WeightsBlob"/>. Round trips with the writer are tested in the build
///     tool's tests.
/// </summary>
public class WeightsBlobTests
{
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
    ///     Data that is not a compressed stream is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read([9, 8, 7, 6, 5]));
    }

    /// <summary>
    ///     A compressed stream with an unknown magic number is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_UnknownMagic_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress("NOPE"u8.ToArray())));
    }

    /// <summary>
    ///     A blob that ends inside the header is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_Truncated_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX1"u8.ToArray(), 40])));
    }

    /// <summary>
    ///     A blob with no lemmas reads as an empty set of weights.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_NoLemmas_ReturnsEmptyWeights()
    {
        // Magic, model id "m" (length 1), dimension 1, dense flag 0, one mean value (0.0f), no lemmas
        var blob = Compress([.. "WSX1"u8.ToArray(), 1, (byte)'m', 1, 0, 0, 0, 0, 0, 0]);

        var data = WeightsBlob.Read(blob);

        Assert.Equal("m", data.ModelId);
        Assert.Equal(1, data.Dimension);
        Assert.Empty(data.Lemmas);
    }

    /// <summary>
    ///     Builds the smallest one-lemma blob: a binary lemma "a" with two senses, the given first-sense part of speech
    ///     and the given presence byte of the first sense's meaning.
    /// </summary>
    /// <param name="pos">The part-of-speech byte of the first sense.</param>
    /// <param name="presence">The presence byte of the first sense's meaning.</param>
    /// <param name="lemmaByte">The single byte of the lemma text.</param>
    /// <param name="meaningByte">When set, the first sense's meaning is this one byte instead of empty.</param>
    /// <returns>The compressed blob.</returns>
    private static byte[] OneLemma(byte pos, byte presence, byte lemmaByte = (byte)'a', byte? meaningByte = null) => Compress(
    [
        .. "WSX1"u8.ToArray(), 1, (byte)'m', 1, 0, 0, 0, 0, 0, // magic, model id, dimension, dense flag, mean
        1, 1, lemmaByte, 0, 0, 0, 0, 1, 2, // one lemma, abstain 0.0f, binary kind, two senses
        pos, .. (meaningByte is { } m ? (byte[])[1, m] : [0]), presence, // first sense: part of speech, meaning, presence byte
        (byte)PartOfSpeech.Verb, 0, 0, // second sense
        0, 0, 0, 0, 0, 0, 0, 0, // one bias and one weight (0.0f each)
    ]);

    /// <summary>
    ///     Every part of speech of the enum, Determiner included, is accepted in a sense.
    /// </summary>
    /// <param name="pos">The part-of-speech byte.</param>
    [Theory]
    [InlineData((byte)PartOfSpeech.Noun)]
    [InlineData((byte)PartOfSpeech.Interjection)]
    [InlineData((byte)PartOfSpeech.Determiner)]
    [InlineData((byte)PartOfSpeech.Auxiliary)]
    public void WeightsBlob_Read_EveryPartOfSpeech_IsAccepted(byte pos)
    {
        var data = WeightsBlob.Read(OneLemma(pos, 0));

        Assert.Equal((PartOfSpeech)pos, Assert.Single(data.Lemmas).Senses[0].PartOfSpeech);
    }

    /// <summary>
    ///     A part of speech beyond the last value of the enum is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_PartOfSpeechOutOfRange_Throws()
    {
        var beyond = (byte)((byte)Enum.GetValues<PartOfSpeech>().Max() + 1);

        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(beyond, 0)));
    }

    /// <summary>
    ///     The presence byte of a meaning is 0 or 1; any other value is rejected.
    /// </summary>
    /// <param name="presence">The presence byte.</param>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)255)]
    public void WeightsBlob_Read_PresenceByteOutOfRange_Throws(byte presence)
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(1, presence)));
    }

    /// <summary>
    ///     A presence byte of 1 keeps the meaning and 0 drops it.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_PresenceByteZeroAndOne_AreAccepted()
    {
        Assert.Null(Assert.Single(WeightsBlob.Read(OneLemma(1, 0)).Lemmas).Senses[0].Meaning);
        Assert.Equal(string.Empty, Assert.Single(WeightsBlob.Read(OneLemma(1, 1)).Lemmas).Senses[0].Meaning);
    }

    /// <summary>
    ///     Malformed UTF-8 in a lemma is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_MalformedUtf8Lemma_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(1, 0, lemmaByte: 0xFF)));

        Assert.IsType<DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     Malformed UTF-8 in a meaning is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_MalformedUtf8Meaning_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(OneLemma(1, 1, meaningByte: 0xC3)));

        Assert.IsType<DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A blob with a dimension of zero is rejected.
    /// </summary>
    [Fact]
    public void WeightsBlob_Read_ZeroDimension_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WeightsBlob.Read(Compress([.. "WSX1"u8.ToArray(), 1, (byte)'m', 0, 0, 0])));
    }
}
