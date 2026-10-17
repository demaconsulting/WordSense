using System.IO.Compression;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the reader of the embedded word lexicon blob. Round trips with the writer are tested in the build tool's
///     tests.
/// </summary>
public class WordsBlobTests
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
    public void WordsBlob_Read_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WordsBlob.Read([1, 2, 3, 4, 5]));
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
    ///     A blob with no strings, no details and no keys reads as an empty lexicon.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_EmptyTables_ReturnsEmptyLexicon()
    {
        // Magic, string count 0, details count 0, key count 0
        var blob = Compress([.. "WSW1"u8.ToArray(), 0, 0, 0]);

        Assert.Empty(WordsBlob.Read(blob));
    }

    /// <summary>
    ///     A truncated blob is rejected.
    /// </summary>
    [Fact]
    public void WordsBlob_Read_Truncated_Throws()
    {
        // Magic and a string count of 5 with no strings following
        var blob = Compress([.. "WSW1"u8.ToArray(), 5]);

        Assert.Throws<InvalidDataException>(() => WordsBlob.Read(blob));
    }
}
