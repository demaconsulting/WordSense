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
