using System.IO.Compression;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the reader of the embedded word lexicon blob. Round trips with the writer are tested in the build tool's
///     tests.
/// </summary>
public class WordsBlobTests
{
    private static readonly byte[] Magic = "WSW1"u8.ToArray();

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
    public void WordsBlob_Read_CorruptData_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WordsBlob.Read([1, 2, 3, 4, 5]));

        // A truncated blob: the magic and a string count of 5 with no strings following
        Assert.Throws<InvalidDataException>(() => WordsBlob.Read(Compress([.. Magic, 5])));
    }

    /// <summary>
    ///     A compressed stream with an unknown magic number is rejected.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_UnknownMagic_Throws()
    {
        var blob = Compress("NOPE"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => WordsBlob.Read(blob));
    }

    /// <summary>
    ///     A sense that refers to a string that does not exist is rejected.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_InvalidSense_Throws()
    {
        // Strings: "run"; one sense whose lemma index 5 is out of range
        var blob = Compress([.. Magic, 1, 3, (byte)'r', (byte)'u', (byte)'n', 1, 5, 0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => WordsBlob.Read(blob));
    }

    private static byte[] Raw(params byte[][] parts) => [.. Magic, .. parts.SelectMany(p => p)];

    // One string "run" and one detail (lemma 0, noun, no meaning, no respelling)
    private static readonly byte[] OneString = [1, 3, (byte)'r', (byte)'u', (byte)'n'];
    private static readonly byte[] OneDetail = [1, 0, 0, 0, 0];

    /// <summary>
    ///     Structurally broken blobs are rejected with <see cref="InvalidDataException"/>: a truncated magic number, an
    ///     over-long or over-large varint, counts that cannot fit in the data, a string that runs past the end, an
    ///     unknown part of speech, an out-of-range meaning, key or sense reference, an empty entry, a duplicate key and
    ///     trailing bytes.
    /// </summary>
    /// <param name="caseName">The name of the broken part, shown in the test output.</param>
    /// <param name="payload">The uncompressed payload (magic number included).</param>
    [Theory]
    [MemberData(nameof(BrokenPayloads))]
    public void WordsBlob_Read_BrokenPayload_Throws(string caseName, byte[] payload)
    {
        Assert.NotEmpty(caseName);

        Assert.Throws<InvalidDataException>(() => WordsBlob.Read(Compress(payload)));
    }

    /// <summary>
    ///     Gets the broken payloads for <see cref="WordsBlob_Read_BrokenPayload_Throws"/>.
    /// </summary>
    public static TheoryData<string, byte[]> BrokenPayloads() => new()
    {
        { "empty payload", [] },
        { "truncated magic", [(byte)'W', (byte)'S', (byte)'W'] },
        { "magic only", Magic },
        { "huge string count", Raw([0xFF, 0xFF, 0xFF, 0xFF, 0x07]) },
        { "string count above data", Raw([100, 3, (byte)'r']) },
        { "varint over 32 bits", Raw([0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01]) },
        { "varint above int range", Raw([0xFF, 0xFF, 0xFF, 0xFF, 0x0F]) },
        { "unterminated varint", Raw([0x80]) },
        { "string longer than data", Raw([1, 50, (byte)'r', (byte)'u']) },
        { "string length above int range", Raw([1, 0xFF, 0xFF, 0xFF, 0xFF, 0x07]) },
        { "huge detail count", Raw(OneString, [0xFF, 0xFF, 0xFF, 0x7F]) },
        { "detail truncated", Raw(OneString, [1, 0, 0]) },
        { "unknown part of speech", Raw(OneString, [1, 0, 9, 0, 0]) },
        { "meaning out of range", Raw(OneString, [1, 0, 0, 2, 0]) },
        { "respelling out of range", Raw(OneString, [1, 0, 0, 0, 2]) },
        { "huge entry count", Raw(OneString, OneDetail, [0xFF, 0xFF, 0xFF, 0x7F]) },
        { "entry key out of range", Raw(OneString, OneDetail, [1, 1, 1, 0]) },
        { "entry without senses", Raw(OneString, OneDetail, [1, 0, 0]) },
        { "entry sense count above data", Raw(OneString, OneDetail, [1, 0, 50, 0]) },
        { "entry sense out of range", Raw(OneString, OneDetail, [1, 0, 1, 1]) },
        { "duplicate key", Raw(OneString, OneDetail, [2, 0, 1, 0, 0, 1, 0]) },
        { "trailing data", Raw(OneString, OneDetail, [1, 0, 1, 0, 0xAA]) },
    };

    /// <summary>
    ///     The smallest valid blob, one key with one sense, is accepted, so the broken cases above differ from it only
    ///     in the broken part.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_MinimalValidBlob_IsAccepted()
    {
        var lexicon = WordsBlob.Read(Compress(Raw(OneString, OneDetail, [1, 0, 1, 0])));

        Assert.Single(lexicon);
        Assert.Equal("run", lexicon["run"][0].Lemma);
    }

    /// <summary>
    ///     Two keys that point to the same sense share one instance of it.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_ValidBlob_SharesEqualSenses()
    {
        // Strings "run" and "ran"; one sense (lemma "run", noun, no meaning, no respelling); two keys using it
        var blob = Compress([.. Magic, 2, 3, (byte)'r', (byte)'u', (byte)'n', 3, (byte)'r', (byte)'a', (byte)'n', 1, 0, 0, 0, 0, 2, 0, 1, 0, 1, 1, 0]);

        var lexicon = WordsBlob.Read(blob);

        Assert.Equal(2, lexicon.Count);
        Assert.Same(lexicon["run"][0], lexicon["ran"][0]);
        Assert.Equal(new WordDetails("run", PartOfSpeech.Noun, null, null), lexicon["run"][0]);
    }
}
