using System.Text.Json;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordPieceTokenizer"/> and <see cref="TokenizedText"/>.
/// </summary>
public class WordPieceTokenizerTests
{
    private static readonly string[] SmallVocabulary =
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "the", "record", "##s", "un", "##known", "."];

    /// <summary>
    ///     The tokenizer must reproduce the reference ids and UTF-16 offsets for every fixture sentence.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_Tokenize_Fixture_MatchesReferenceTokenizer()
    {
        var modelDirectory = ModelAssets.RequireModelDirectory();
        var tokenizer = WordPieceTokenizer.FromFile(Path.Combine(modelDirectory, "vocab.txt"));
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer-parity.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixture));

        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var text = item.GetProperty("text").GetString()!;
            var actual = tokenizer.Tokenize(text);

            var expectedIds = item.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            Assert.True(
                expectedIds.SequenceEqual(actual.Ids),
                $"ids differ for: {text}; expected [{string.Join(',', expectedIds)}] actual [{string.Join(',', actual.Ids)}]");
            Assert.True(
                item.GetProperty("starts").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(actual.Starts),
                $"starts differ for: {text}");
            Assert.True(
                item.GetProperty("ends").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(actual.Ends),
                $"ends differ for: {text}");
            count++;
        }

        Assert.True(count >= 150);
    }

    /// <summary>
    ///     Words are split into the longest matching pieces and unknown words map to the unknown id.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_Tokenize_SmallVocabulary_SplitsIntoWordPieces()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        var result = tokenizer.Tokenize("The Records unknown zzz.");

        Assert.Equal([4, 5, 6, 7, 8, 1, 9], result.Ids);
        Assert.Equal(0, result.Starts[0]);
        Assert.Equal(3, result.Ends[0]);
    }

    /// <summary>
    ///     Empty text produces no tokens.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_Tokenize_EmptyText_ReturnsNoTokens()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        Assert.Equal(0, tokenizer.Tokenize(string.Empty).Count);
    }

    /// <summary>
    ///     A target span selects exactly the tokens that overlap it.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_FindTokens_Span_ReturnsOverlappingTokens()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);
        var tokens = tokenizer.Tokenize("the records");

        var (first, end) = tokens.FindTokens(4, 11);

        Assert.Equal((1, 3), (first, end));
        Assert.Equal((0, 0), tokens.FindTokens(50, 60));
    }

    /// <summary>
    ///     A text that fits is returned whole, with special tokens around it.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_BuildWindow_ShortText_ContainsWholeText()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);
        var tokens = tokenizer.Tokenize("the record");

        var (ids, first, end) = tokens.BuildWindow(tokenizer.ClsId, tokenizer.SepId, 16, 1, 2);

        Assert.Equal([tokenizer.ClsId, 4, 5, tokenizer.SepId], ids);
        Assert.Equal((2, 3), (first, end));
    }

    /// <summary>
    ///     A long text is cut to a window that is centered on the target and clamped to the text.
    /// </summary>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(10, 11, 7)]
    [InlineData(19, 20, 12)]
    public void WordPieceTokenizer_BuildWindow_LongText_KeepsTargetInsideWindow(int targetFirst, int targetEnd, int expectedStart)
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);
        var tokens = new TokenizedText(
            Enumerable.Range(100, 20).ToArray(), Enumerable.Range(0, 20).ToArray(), Enumerable.Range(1, 20).ToArray());

        var (ids, first, end) = tokens.BuildWindow(tokenizer.ClsId, tokenizer.SepId, 10, targetFirst, targetEnd);

        Assert.Equal(10, ids.Length);
        Assert.Equal(100 + expectedStart, ids[1]);
        Assert.Equal(100 + targetFirst, ids[first]);
        Assert.Equal(first + 1, end);
    }
}
