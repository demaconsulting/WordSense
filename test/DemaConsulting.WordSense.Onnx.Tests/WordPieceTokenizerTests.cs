using System.Text.Json;

namespace DemaConsulting.WordSense.Onnx.Tests;

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

        Assert.Equal(176, count);
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
    ///     A word of 100 normalized characters is split into pieces, but one of 101 becomes a single unknown token
    ///     that spans the whole word.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_Tokenize_OverlongWord_BecomesSingleUnknown()
    {
        var tokenizer = new WordPieceTokenizer(["[PAD]", "[UNK]", "[CLS]", "[SEP]", "a", "##a"]);

        var atLimit = tokenizer.Tokenize(new string('a', 100));
        var overLimit = tokenizer.Tokenize(new string('a', 101));

        Assert.Equal(100, atLimit.Count);
        Assert.Equal(4, atLimit.Ids[0]);
        Assert.All(atLimit.Ids.Skip(1), id => Assert.Equal(5, id));
        Assert.Equal([1], overLimit.Ids);
        Assert.Equal(0, overLimit.Starts[0]);
        Assert.Equal(101, overLimit.Ends[0]);
    }

    /// <summary>
    ///     A vocabulary file is read line by line whatever its line endings, and an unreadable file is an I/O error.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_FromFile_ReadsLinesAndReportsIoErrors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordsense-vocab-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "[PAD]\r\n[UNK]\r\n[CLS]\n[SEP]\nthe\n");

            var tokenizer = WordPieceTokenizer.FromFile(path);

            Assert.Equal(1, tokenizer.UnknownId);
            Assert.Equal(2, tokenizer.ClsId);
            Assert.Equal(3, tokenizer.SepId);
            Assert.Equal([4], tokenizer.Tokenize("The").Ids);
            Assert.ThrowsAny<IOException>(() => WordPieceTokenizer.FromFile(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
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
    ///     Words are tokenized independently and report their piece ranges, with the same pieces as the joined text.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_TokenizeWords_ReturnsPieceRangePerWord()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        var words = tokenizer.TokenizeWords(["the", "records", "", "zzz."]);
        var joined = tokenizer.Tokenize("the records  zzz.");

        Assert.Equal(joined.Ids, words.Ids);
        Assert.Equal([0, 1, 3, 3], words.WordFirst);
        Assert.Equal([1, 3, 3, 5], words.WordEnd);
        Assert.Equal((4, 11), (words.Starts[1], words.Ends[2]));
    }

    /// <summary>
    ///     No words give no tokens.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_TokenizeWords_NoWords_ReturnsNoTokens()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        var result = tokenizer.TokenizeWords([]);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.WordFirst);
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

    /// <summary>
    ///     A target that is longer than the window capacity is clipped to the window.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_BuildWindow_TargetLongerThanCapacity_ClipsTargetToWindow()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);
        var tokens = new TokenizedText(
            Enumerable.Range(100, 20).ToArray(), Enumerable.Range(0, 20).ToArray(), Enumerable.Range(1, 20).ToArray());

        var (ids, first, end) = tokens.BuildWindow(tokenizer.ClsId, tokenizer.SepId, 10, 2, 18);

        Assert.Equal(10, ids.Length);
        Assert.Equal(tokenizer.ClsId, ids[0]);
        Assert.Equal(tokenizer.SepId, ids[^1]);
        Assert.Equal((1, 9), (first, end));
        Assert.Equal(102, ids[1]);
    }

    /// <summary>
    ///     Null arguments are rejected.
    /// </summary>
    [Fact]
    public void WordPieceTokenizer_NullArguments_ThrowArgumentNullException()
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        Assert.Throws<ArgumentNullException>(() => new WordPieceTokenizer(null!));
        Assert.Throws<ArgumentNullException>(() => tokenizer.Tokenize(null!));
        Assert.Throws<ArgumentNullException>(() => tokenizer.TokenizeWords(null!));
        Assert.Throws<ArgumentNullException>(() => tokenizer.TokenizeWords(["the", null!]));
    }

    /// <summary>
    ///     A vocabulary lacking a required special token is rejected.
    /// </summary>
    [Theory]
    [InlineData("[UNK]")]
    [InlineData("[CLS]")]
    [InlineData("[SEP]")]
    public void WordPieceTokenizer_VocabularyMissingSpecialToken_ThrowsArgumentException(string missing)
    {
        var vocabulary = SmallVocabulary.Where(t => t != missing).ToArray();

        Assert.Throws<ArgumentException>(() => new WordPieceTokenizer(vocabulary));
    }

    /// <summary>
    ///     Lone surrogates and control characters carry no content and are dropped.
    /// </summary>
    [Theory]
    [InlineData("th\uD800e")]
    [InlineData("th\uDC00e")]
    [InlineData("th\u0001e")]
    [InlineData("t\u0000h\u0007e")]
    public void WordPieceTokenizer_Tokenize_LoneSurrogatesAndControlCharacters_AreDropped(string text)
    {
        var tokenizer = new WordPieceTokenizer(SmallVocabulary);

        var result = tokenizer.Tokenize(text);

        Assert.Equal([4], result.Ids);
    }
}
