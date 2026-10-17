using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;
using WordDetails = DemaConsulting.WordSense.WordDetails;
using WordsBlob = DemaConsulting.WordSense.WordsBlob;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the writer of the word table blob, checked against the reader of the core package.
/// </summary>
public sealed class WordsBlobWriterTests
{
    private static Dictionary<string, WordDetails[]> Sample()
    {
        WordDetails[] bow =
        [
            new("bow", PartOfSpeech.Noun, "bend", "bou"),
            new("bow", PartOfSpeech.Verb, "bend", "bou"),
            new("bow", PartOfSpeech.Noun, "weapon", null),
        ];
        return new Dictionary<string, WordDetails[]>(StringComparer.Ordinal)
        {
            ["bow"] = bow,
            ["bows"] = bow,
            ["run"] = [new("run", PartOfSpeech.Verb, null, null)],
            ["caf\u00E9"] = [new("caf\u00E9", PartOfSpeech.Noun, null, null)],
        };
    }

    /// <summary>
    ///     A written table is read back with equal content and shared sense instances.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBlobWriter_Write_RoundTripsThroughTheReader()
    {
        var entries = Sample();

        var read = WordsBlob.Read(WordsBlobWriter.Write(entries));

        Assert.Equal(entries.Count, read.Count);
        foreach (var (key, senses) in entries)
        {
            Assert.True(read.TryGetValue(key, out var actual), key);
            Assert.Equal(senses, actual);
        }

        Assert.Same(read["bow"][0], read["bows"][0]);
    }

    /// <summary>
    ///     Equal content gives equal bytes, whatever the insertion order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBlobWriter_Write_IsDeterministic()
    {
        var forward = Sample();
        var reverse = new Dictionary<string, WordDetails[]>(StringComparer.Ordinal);
        foreach (var pair in forward.Reverse())
        {
            reverse[pair.Key] = pair.Value;
        }

        Assert.Equal(WordsBlobWriter.Write(forward), WordsBlobWriter.Write(reverse));
    }

    /// <summary>
    ///     A key needs between one and 255 senses and the dictionary must not be null.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBlobWriter_Write_RejectsInvalidInput()
    {
        var sense = new WordDetails("run", PartOfSpeech.Verb, null, null);
        var none = new Dictionary<string, WordDetails[]> { ["run"] = [] };
        var many = new Dictionary<string, WordDetails[]> { ["run"] = Enumerable.Repeat(sense, 256).ToArray() };
        var most = new Dictionary<string, WordDetails[]> { ["run"] = Enumerable.Repeat(sense, 255).ToArray() };

        Assert.Throws<ArgumentException>(() => WordsBlobWriter.Write(none));
        Assert.Throws<ArgumentException>(() => WordsBlobWriter.Write(many));
        Assert.Throws<ArgumentNullException>(() => WordsBlobWriter.Write(null!));
        Assert.Equal(255, WordsBlob.Read(WordsBlobWriter.Write(most))["run"].Length);
    }

    /// <summary>
    ///     An empty table is a valid blob.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBlobWriter_Write_EmptyTable_RoundTrips()
    {
        var read = WordsBlob.Read(WordsBlobWriter.Write(new Dictionary<string, WordDetails[]>()));

        Assert.Empty(read);
    }

    /// <summary>
    ///     Integers are written as 7-bit little-endian varints.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="expected">The expected bytes as hexadecimal text.</param>
    [Theory]
    [InlineData(0, "00")]
    [InlineData(1, "01")]
    [InlineData(127, "7F")]
    [InlineData(128, "8001")]
    [InlineData(300, "AC02")]
    [InlineData(16384, "808001")]
    [InlineData(int.MaxValue, "FFFFFFFF07")]
    public void WordSenseBuilder_WordsBlobWriter_WriteVarint_WritesSevenBitGroups(int value, string expected)
    {
        using var stream = new MemoryStream();

        WordsBlobWriter.WriteVarint(stream, value);

        Assert.Equal(expected, Convert.ToHexString(stream.ToArray()));
    }
}
