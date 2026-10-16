using System.IO.Compression;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the internal front-coded <see cref="LexicalIndex"/>.
/// </summary>
public class LexicalIndexTests
{
    /// <summary>
    ///     Compresses bytes as raw DEFLATE, the way the table stores the dictionary section.
    /// </summary>
    /// <param name="raw">The bytes.</param>
    /// <returns>The compressed bytes.</returns>
    internal static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Generates sorted unique synthetic words.
    /// </summary>
    /// <param name="count">The number of words.</param>
    /// <returns>The words with masks, in ordinal order.</returns>
    private static List<(string Word, byte Mask)> Words(int count)
    {
        // Distinct base-26 spellings of consecutive numbers share long prefixes once sorted
        var set = Enumerable.Range(1, count)
            .Select(n =>
            {
                var chars = new Stack<char>();
                for (var v = n * 7; v > 0; v /= 26)
                {
                    chars.Push((char)('a' + (v % 26)));
                }

                return new string([.. chars]);
            })
            .Order(StringComparer.Ordinal)
            .ToList();

        return set.Select((w, i) => (w, (byte)(1 << (i % 8)))).ToList();
    }

    /// <summary>
    ///     Every word is found with its mask, for sizes around block boundaries.
    /// </summary>
    /// <param name="count">The word count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    public void LexicalIndex_BuildAndFind_FindsEveryWord(int count)
    {
        var words = Words(count);

        var index = LexicalIndex.Build(words);

        Assert.Equal(count, index.Count);
        foreach (var (word, mask) in words)
        {
            Assert.True(index.TryFind(word, out var found), word);
            Assert.Equal(mask, found);
        }

        Assert.Equal(words, index.Enumerate());
    }

    /// <summary>
    ///     Absent words, words with other characters and the empty word are not found.
    /// </summary>
    [Fact]
    public void LexicalIndex_TryFind_AbsentOrInvalid_ReturnsFalse()
    {
        var index = LexicalIndex.Build([("cat", 1), ("catalog", 1), ("dog", 1)]);

        Assert.False(index.TryFind("ca", out _));
        Assert.False(index.TryFind("cats", out _));
        Assert.False(index.TryFind("zzz", out _));
        Assert.False(index.TryFind("a", out _));
        Assert.False(index.TryFind("Cat", out _));
        Assert.False(index.TryFind("c-t", out _));
        Assert.False(index.TryFind(string.Empty, out _));
        Assert.False(index.TryFind(new string('a', 300), out _));
        Assert.False(LexicalIndex.Empty.TryFind("cat", out _));
        Assert.Empty(LexicalIndex.Empty.Enumerate());
        Assert.Empty(LexicalIndex.Empty.Compress());
    }

    /// <summary>
    ///     Compression round-trips and front coding makes related words small.
    /// </summary>
    [Fact]
    public void LexicalIndex_CompressDecompress_RoundTrips()
    {
        var words = Words(500);
        var index = LexicalIndex.Build(words);

        var copy = LexicalIndex.Decompress(index.Count, index.RawLength, index.Compress());

        Assert.Equal(words, copy.Enumerate());
        Assert.True(index.Compress().Length > 0);
    }

    /// <summary>
    ///     Words that are not in strictly ascending order of lower-case letters, or have a bad mask, are rejected.
    /// </summary>
    [Fact]
    public void LexicalIndex_Build_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LexicalIndex.Build(null!));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([("b", 1), ("a", 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([("a", 1), ("a", 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([("A", 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([("a1", 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([(string.Empty, 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([(new string('a', 256), 1)]));
        Assert.Throws<ArgumentException>(() => LexicalIndex.Build([("a", 0)]));
    }

    /// <summary>
    ///     Decoding rejects non-canonical or inconsistent data.
    /// </summary>
    [Fact]
    public void LexicalIndex_Decompress_InvalidData_Throws()
    {
        byte[] ok = [0, 1, (byte)'a', 1, 0, 1, (byte)'b', 1];
        Assert.Equal(2, LexicalIndex.Decompress(2, ok.Length, Deflate(ok)).Count);

        // Declared length wrong in either direction, wrong count, not DEFLATE, and a bad record: always a format exception
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(2, ok.Length - 1, Deflate(ok)));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(2, ok.Length + 1, Deflate(ok)));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(3, ok.Length, Deflate(ok)));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(1, ok.Length, Deflate(ok)));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(2, ok.Length, [0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(1, 4, Deflate([0, 1, (byte)'A', 1])));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(1, 4, Deflate([0, 1, (byte)'a', 0])));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(1, 4, Deflate([1, 1, (byte)'a', 1])));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(2, 8, Deflate([0, 1, (byte)'b', 1, 0, 1, (byte)'a', 1])));

        // These two reach the record rules: the declared size is at least four bytes per word
        // An empty suffix (padded to four bytes) and a count larger than the records (12 bytes hold only two words)
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(1, 4, Deflate([0, 0, 1, 0])));
        byte[] twoWords = [0, 1, (byte)'a', 1, 0, 5, (byte)'b', (byte)'b', (byte)'b', (byte)'b', (byte)'b', 1];
        Assert.Equal(2, LexicalIndex.Decompress(2, twoWords.Length, Deflate(twoWords)).Count);
        var truncated = Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(3, twoWords.Length, Deflate(twoWords)));
        Assert.Contains("truncated", truncated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(LexicalIndex.MaxWords + 1, 8, Deflate(ok)));
        Assert.Throws<WordSenseTableFormatException>(() => LexicalIndex.Decompress(2, LexicalIndex.MaxRawLength + 1, Deflate(ok)));
    }
}
