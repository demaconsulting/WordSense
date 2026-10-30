namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxEncoder"/> with the real MiniLM model files of the repository. They are skipped when
///     the files are not available (for example without Git LFS content).
/// </summary>
public class OnnxEncoderTests
{
    private static readonly string[] Sentence = ["They", "will", "record", "the", "record", "."];

    private static OnnxEncoder Open(int maxSequenceLength = 128, int intraOpThreads = 2)
    {
        var directory = ModelAssets.RequireModelDirectory();
        return new OnnxEncoder(
            Path.Join(directory, "model_int8.onnx"),
            Path.Join(directory, "vocab.txt"),
            true,
            maxSequenceLength,
            intraOpThreads);
    }

    private static double Distance(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    /// <summary>
    ///     The same text and targets always give the same vectors, one per target, of the hidden size.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors()
    {
        using var encoder = Open();

        var first = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);
        var second = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);

        Assert.Equal(384, encoder.HiddenSize);
        Assert.Equal(2, first.Length);
        Assert.All(first, v => Assert.Equal(384, v!.Length));
        Assert.Equal(first[0], second[0]);
        Assert.Equal(first[1], second[1]);

        // The same word in two contexts gets two different vectors
        Assert.True(Distance(first[0]!, first[1]!) > 1e-3);
    }

    /// <summary>
    ///     No targets give an empty result, and a word without word pieces gives no vector.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_NoTargetsOrNoTokens_ReturnsEmptyOrNull()
    {
        using var encoder = Open();

        var none = await encoder.EncodeAsync(Sentence, [], TestContext.Current.CancellationToken);
        var noTokens = await encoder.EncodeAsync(["record", "\u0001"], [0, 1], TestContext.Current.CancellationToken);

        Assert.Empty(none);
        Assert.NotNull(noTokens[0]);
        Assert.Null(noTokens[1]);
    }

    /// <summary>
    ///     Concurrent calls on one encoder return what sequential calls return.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_Concurrent_MatchesSequentialResults()
    {
        using var encoder = Open();
        var expected = await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(
            async () => await encoder.EncodeAsync(Sentence, [2, 4], TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        Assert.All(results, r =>
        {
            Assert.Equal(expected[0], r[0]);
            Assert.Equal(expected[1], r[1]);
        });
    }

    /// <summary>
    ///     A text longer than the window still gives a vector for every target.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_TextLongerThanWindow_ReturnsVectorsForTargets()
    {
        using var encoder = Open(maxSequenceLength: 16);
        var words = Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? "record" : "the").ToArray();

        var vectors = await encoder.EncodeAsync(words, [0, 30, 58], TestContext.Current.CancellationToken);

        Assert.All(vectors, v => Assert.Equal(384, v!.Length));
    }

    /// <summary>
    ///     The thread count does not change the vectors beyond rounding.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_Constructor_IntraOpThreads_ProducesSameVectors()
    {
        using var single = Open(intraOpThreads: 1);
        using var multiple = Open(intraOpThreads: 2);

        var a = await single.EncodeAsync(Sentence, [2], TestContext.Current.CancellationToken);
        var b = await multiple.EncodeAsync(Sentence, [2], TestContext.Current.CancellationToken);

        Assert.True(Distance(a[0]!, b[0]!) < 1e-3);
    }

    /// <summary>
    ///     For a text that fits one window every word gets exactly the vector that <see cref="OnnxEncoder.EncodeAsync"/>
    ///     gives it, in one pass.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_ShortText_EqualsSinglePassVectors()
    {
        using var encoder = Open();
        var all = Enumerable.Range(0, Sentence.Length).ToArray();
        var expected = await encoder.EncodeAsync(Sentence, all, TestContext.Current.CancellationToken);
        var before = encoder.PassCount;

        var vectors = await encoder.EncodeAllAsync(Sentence, TestContext.Current.CancellationToken);

        Assert.Equal(1, encoder.PassCount - before);
        Assert.Equal(expected.Length, vectors.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], vectors[i]);
        }
    }

    /// <summary>
    ///     An empty text gives no vectors and a word without word pieces gives null.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_EmptyOrNoTokens_ReturnsEmptyOrNull()
    {
        using var encoder = Open();

        var none = await encoder.EncodeAllAsync([], TestContext.Current.CancellationToken);
        var noTokens = await encoder.EncodeAllAsync(["record", "\u0001", "the"], TestContext.Current.CancellationToken);

        Assert.Empty(none);
        Assert.NotNull(noTokens[0]);
        Assert.Null(noTokens[1]);
        Assert.NotNull(noTokens[2]);
    }

    /// <summary>
    ///     A text longer than the window is encoded in overlapping windows, one pass per window and not one per word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_LongText_UsesOnePassPerWindow()
    {
        // Arrange: 60 one-piece words, a window of 14 word pieces that advances by 7
        using var encoder = Open(maxSequenceLength: 16);
        var words = Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? "record" : "the").ToArray();
        var before = encoder.PassCount;

        // Act
        var vectors = await encoder.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: windows start at 0, 7, ..., 42 and at the last possible start 46
        Assert.Equal(8, encoder.PassCount - before);
        Assert.Equal(60, vectors.Length);
        Assert.All(vectors, v => Assert.Equal(384, v!.Length));
    }

    /// <summary>
    ///     The vectors of a long text stay close to those of a single pass over the whole text, also for the words
    ///     next to the borders of the windows, and the first and last word keep the text edge as context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_LongText_StaysCloseToSinglePassAtWindowBorders()
    {
        // Arrange: about 50 words, so a window of 20 word pieces (stride 9) needs several windows
        string[] words =
        [
            "The", "engineer", "opened", "the", "old", "service", "manual", "and", "read", "the", "chapter", "on", "brake", "repairs", ".",
            "Then", "she", "wrote", "down", "the", "steps", "for", "the", "technician", "who", "would", "replace", "the", "worn", "pads", ".",
            "After", "the", "test", "drive", "they", "recorded", "the", "results", "in", "a", "report", "and", "sent", "it", "to", "the", "customer", "today", ".",
        ];
        using var windowed = Open(maxSequenceLength: 20);
        var all = Enumerable.Range(0, words.Length).ToArray();
        var centered = await windowed.EncodeAsync(words, all, TestContext.Current.CancellationToken);

        // Act
        var parts = await windowed.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: the windowed vectors are in the neighborhood of the vectors that a window centered on the word gives
        // (the shift of a word inside its window is at most a quarter of the window). MiniLM vectors of punctuation and
        // of the first word of a clause move a lot with a few tokens of context, so the bound is on the mean and a floor
        Assert.True(windowed.PassCount > words.Length);
        var similarities = centered.Zip(parts, (a, b) => Cosine(a!, b!)).ToArray();
        Assert.True(similarities.Average() > 0.85, $"The mean cosine is {similarities.Average():0.000}.");
        Assert.All(similarities, s => Assert.True(s > 0.5, $"A windowed vector is not close to its centered vector (cosine {s:0.000})."));

        // The first and the last word are pooled from windows that include the text edge, so they are as close as the others
        Assert.True(similarities[0] > 0.9);
        Assert.True(similarities[^1] > 0.9);
    }

    /// <summary>
    ///     Every word of a long text is pooled from the window that gives it the most context on its closer side: its
    ///     vector equals, bit for bit, the vector of that window encoded on its own.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_LongText_PoolsEachWordFromItsMostCentralWindow()
    {
        // Arrange: 60 one-piece words, windows of 14 pieces starting at 0, 7, ..., 42 and 46
        using var encoder = Open(maxSequenceLength: 16);
        var words = Enumerable.Range(0, 60).Select(i => (i * 7 % 5) switch { 0 => "record", 1 => "the", 2 => "of", 3 => "and", _ => "in" }).ToArray();
        int[] starts = [0, 7, 14, 21, 28, 35, 42, 46];

        // Act
        var vectors = await encoder.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: independent expectation of the window of every word, then the vector of that window alone
        for (var w = 0; w < words.Length; w++)
        {
            var start = starts
                .Select((s, i) => (Start: s, Index: i, Margin: Math.Min(s == 0 ? int.MaxValue : w - s, s + 14 >= 60 ? int.MaxValue : s + 14 - (w + 1))))
                .OrderByDescending(c => c.Margin)
                .ThenBy(c => c.Index)
                .First().Start;
            var window = words[start..(start + 14)];
            var alone = await encoder.EncodeAsync(window, [w - start], TestContext.Current.CancellationToken);
            Assert.Equal(alone[0], vectors[w]);
        }
    }

    /// <summary>
    ///     A word that fits a window is never cut by a window border: with a window of 14 pieces, a word that spans the
    ///     pieces 6 to 14 straddles the grid windows at 0 and 7, so the plan adds a window that starts at the word.
    /// </summary>
    [Fact]
    public void OnnxEncoder_PlanWindows_WordStraddlingGridBorder_GetsWindowThatContainsIt()
    {
        // Arrange: 6 one-piece words, one word of 9 pieces ([6,15)), then 20 one-piece words; 35 pieces
        var (first, end) = Spans(Enumerable.Repeat(1, 6).Append(9).Concat(Enumerable.Repeat(1, 20)));

        // Act
        var (starts, windowOf) = OnnxEncoder.PlanWindows(35, 14, first, end);

        // Assert: the grid has the windows 0, 7, 14 and 21; the added window starts at the word; every word is inside its window
        Assert.Equal([0, 6, 7, 14, 21], starts);
        Assert.Equal(6, starts[windowOf[6]]);
        AssertEveryWordInsideItsWindow(starts, windowOf, first, end, 14);
    }

    /// <summary>
    ///     A word that straddles a window border near the end of the text gets a window that starts at the word and stays
    ///     inside the text.
    /// </summary>
    [Fact]
    public void OnnxEncoder_PlanWindows_WordNearTextEnd_AddedWindowStaysInsideText()
    {
        // Arrange: 13 one-piece words, a word of 9 pieces ([13,22)) and 8 one-piece words; 30 pieces; the grid is 0, 7, 14, 16
        var (first, end) = Spans(Enumerable.Repeat(1, 13).Append(9).Concat(Enumerable.Repeat(1, 8)));

        // Act
        var (starts, windowOf) = OnnxEncoder.PlanWindows(30, 14, first, end);

        // Assert
        Assert.Equal([0, 7, 13, 14, 16], starts);
        AssertEveryWordInsideItsWindow(starts, windowOf, first, end, 14);
    }

    /// <summary>
    ///     A word of more pieces than a window holds gets a window that starts at the word and is pooled from its first
    ///     pieces; the other words keep a window that contains them, and a word without tokens has no window.
    /// </summary>
    [Fact]
    public void OnnxEncoder_PlanWindows_WordLongerThanWindow_StartsItsOwnWindow()
    {
        // Arrange: 3 one-piece words, a word without pieces, a word of 20 pieces ([3,23)) and 10 one-piece words; 33 pieces
        var (first, end) = Spans([1, 1, 1, 0, 20, .. Enumerable.Repeat(1, 10)]);

        // Act
        var (starts, windowOf) = OnnxEncoder.PlanWindows(33, 14, first, end);

        // Assert
        Assert.Equal(3, starts[windowOf[4]]);
        Assert.Equal(-1, windowOf[3]);
        AssertEveryWordInsideItsWindow(starts, windowOf, first, end, 14);
    }

    /// <summary>
    ///     For every mix of word lengths, each word that fits a window is inside its window and the windows cover the
    ///     whole text, in increasing order without repeats.
    /// </summary>
    [Fact]
    public void OnnxEncoder_PlanWindows_RandomWordLengths_EveryFittingWordIsContained()
    {
        // Arrange
        var random = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var capacity = random.Next(6, 40);
            var lengths = Enumerable.Range(0, random.Next(3, 60)).Select(_ => random.Next(0, 4) == 0 ? random.Next(1, capacity + 6) : random.Next(1, 3)).ToArray();
            var count = lengths.Sum();
            if (count <= capacity)
            {
                continue;
            }

            var (first, end) = Spans(lengths);

            // Act
            var (starts, windowOf) = OnnxEncoder.PlanWindows(count, capacity, first, end);

            // Assert
            AssertEveryWordInsideItsWindow(starts, windowOf, first, end, capacity);
            Assert.Equal(starts.Order().Distinct().ToArray(), starts);
            Assert.Equal(0, starts[0]);
            Assert.Equal(count - capacity, starts[^1]);
            Assert.All(starts, s => Assert.InRange(s, 0, count - capacity));
        }
    }

    /// <summary>
    ///     The vector of a word that straddles a window border of the grid is the pooled vector of all its pieces from a
    ///     window that contains it, bit for bit: with a window of 14 pieces and the word at the pieces 6 to 14, the
    ///     windows 0 and 7 hold only 8 of its 9 pieces.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_WordStraddlingWindowBorder_IsPooledOverAllItsPieces()
    {
        // Arrange: a word of 9 pieces after 6 one-piece words, then 20 one-piece words; windows of 14 pieces
        using var encoder = Open(maxSequenceLength: 16);
        var tokenizer = WordPieceTokenizer.FromFile(Path.Join(ModelAssets.RequireModelDirectory(), "vocab.txt"), true);
        var long9 = LongWord(tokenizer, 9);
        string[] words = [.. Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? "record" : "the"), long9, .. Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? "of" : "and")];
        var tokens = tokenizer.TokenizeWords(words);
        Assert.Equal((6, 15), (tokens.WordFirst[6], tokens.WordEnd[6]));
        Assert.Equal(35, tokens.Count);

        // Act
        var vectors = await encoder.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: the window that starts at the word holds the word and the five words after it; encoded alone, it gives
        // the same vector, bit for bit, whereas the grid windows hold only 8 of the 9 pieces
        var alone = await encoder.EncodeAsync(words[6..12], [0], TestContext.Current.CancellationToken);
        Assert.Equal(alone[0], vectors[6]);
    }

    /// <summary>
    ///     A word of more pieces than a window holds is pooled from its first pieces, as many as the window holds, from a
    ///     window that starts at the word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_WordLongerThanWindow_IsPooledOverItsFirstPieces()
    {
        // Arrange: 3 one-piece words, a word of 20 pieces and 10 one-piece words; windows of 14 pieces
        using var encoder = Open(maxSequenceLength: 16);
        var tokenizer = WordPieceTokenizer.FromFile(Path.Join(ModelAssets.RequireModelDirectory(), "vocab.txt"), true);
        var long14 = LongWord(tokenizer, 14);
        var long20 = LongWord(tokenizer, 20);
        string[] words = ["the", "of", "and", long20, .. Enumerable.Repeat("in", 10)];
        var tokens = tokenizer.TokenizeWords(words);
        Assert.Equal((3, 23), (tokens.WordFirst[3], tokens.WordEnd[3]));

        // The first 14 pieces of the long word are the 14 pieces of the shorter word
        Assert.Equal(tokenizer.TokenizeWords([long14]).Ids.Take(14), tokens.Ids.Skip(3).Take(14));

        // Act
        var vectors = await encoder.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: the window of 14 pieces that starts at the word is the whole text of the shorter word
        var alone = await encoder.EncodeAsync([long14], [0], TestContext.Current.CancellationToken);
        Assert.Equal(alone[0], vectors[3]);
    }

    private static (int[] First, int[] End) Spans(IEnumerable<int> lengths)
    {
        var first = new List<int>();
        var end = new List<int>();
        var at = 0;
        foreach (var length in lengths)
        {
            first.Add(at);
            at += length;
            end.Add(at);
        }

        return ([.. first], [.. end]);
    }

    private static void AssertEveryWordInsideItsWindow(int[] starts, int[] windowOf, int[] first, int[] end, int capacity)
    {
        for (var w = 0; w < first.Length; w++)
        {
            if (end[w] <= first[w])
            {
                Assert.Equal(-1, windowOf[w]);
                continue;
            }

            var start = starts[windowOf[w]];
            if (end[w] - first[w] <= capacity)
            {
                Assert.True(start <= first[w] && end[w] <= start + capacity, $"Word {w} [{first[w]},{end[w]}) is cut by the window [{start},{start + capacity}).");
            }
            else
            {
                Assert.Equal(first[w], start);
            }
        }
    }

    /// <summary>
    ///     Finds a made-up word that the tokenizer splits into exactly the given number of pieces.
    /// </summary>
    private static string LongWord(WordPieceTokenizer tokenizer, int pieces)
    {
        for (var length = 2; length < 80; length++)
        {
            var word = new string('q', length);
            var tokens = tokenizer.TokenizeWords([word]);
            if (tokens.Count == pieces)
            {
                return word;
            }
        }

        throw new InvalidOperationException($"No word of {pieces} pieces was found.");
    }

    /// <summary>
    ///     The possessive view splits a possessive into a stem and a marker, each with a vector of its own, and the
    ///     vector of the stem differs from the vector of the joined token.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_PossessiveView_PoolsStemAndMarkerApart()
    {
        // Arrange
        using var encoder = Open();
        string[] words = ["The", "technician's", "tool", "broke", "."];

        // Act
        var view = await encoder.EncodeAllAsync(words, PossessiveView.IsLexiconPossessive, TestContext.Current.CancellationToken);
        var split = await encoder.EncodeAllAsync(["The", "technician", "'s", "tool", "broke", "."], TestContext.Current.CancellationToken);
        var joined = await encoder.EncodeAllAsync(words, TestContext.Current.CancellationToken);

        // Assert: the view is the split text, the stem is a word of its own, and joining changes the pooled vector
        Assert.Equal(["The", "technician", "'s", "tool", "broke", "."], view.Tokens);
        Assert.Equal([0, 1, 3, 4, 5], view.Start);
        Assert.Equal(split.Length, view.Vectors.Length);
        for (var i = 0; i < split.Length; i++)
        {
            Assert.Equal(split[i], view.Vectors[i]);
        }

        Assert.True(Distance(view.Vectors[view.Start[1]]!, joined[1]!) > 1e-3);
    }

    /// <summary>
    ///     Bad arguments, a cancelled call and use after disposal are rejected by the windowed encode.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAllAsync_InvalidUse_Throws()
    {
        using var encoder = Open();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAllAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAllAsync([null!], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAllAsync(Sentence, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await encoder.EncodeAllAsync(Sentence, cancelled.Token));

        encoder.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await encoder.EncodeAllAsync(Sentence, TestContext.Current.CancellationToken));
    }

    private static double Cosine(double[] a, double[] b)
    {
        var dot = a.Zip(b, (x, y) => x * y).Sum();
        return dot / (Math.Sqrt(a.Sum(x => x * x)) * Math.Sqrt(b.Sum(y => y * y)));
    }

    /// <summary>
    ///     Bad arguments, a cancelled call and use after disposal are rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxEncoder_EncodeAsync_InvalidUse_Throws()
    {
        using var encoder = Open();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(null!, [0], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await encoder.EncodeAsync(Sentence, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await encoder.EncodeAsync(Sentence, [-1], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await encoder.EncodeAsync(Sentence, [6], TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await encoder.EncodeAsync(Sentence, [0], cancelled.Token));

        encoder.Dispose();
        encoder.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await encoder.EncodeAsync(Sentence, [0], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Null paths and out-of-range limits are rejected.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_InvalidArguments_Throws()
    {
        var directory = ModelAssets.RequireModelDirectory();
        var model = Path.Join(directory, "model_int8.onnx");
        var vocabulary = Path.Join(directory, "vocab.txt");

        Assert.Throws<ArgumentNullException>(() => new OnnxEncoder(null!, vocabulary, true, 128, 2));
        Assert.Throws<ArgumentNullException>(() => new OnnxEncoder(model, null!, true, 128, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 7, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 513, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxEncoder(model, vocabulary, true, 128, -1));
    }

    /// <summary>
    ///     A missing or unusable model file is rejected.
    /// </summary>
    [Fact]
    public void OnnxEncoder_Constructor_InvalidModelFile_Throws()
    {
        var directory = ModelAssets.RequireModelDirectory();
        var vocabulary = Path.Join(directory, "vocab.txt");
        var garbage = Path.Join(Path.GetTempPath(), "wordsense-garbage-" + Guid.NewGuid().ToString("N") + ".onnx");
        File.WriteAllText(garbage, "this is not an onnx model");

        try
        {
            Assert.Throws<FileNotFoundException>(() => new OnnxEncoder(garbage + ".missing", vocabulary, true, 128, 2));
            Assert.ThrowsAny<Exception>(() => new OnnxEncoder(garbage, vocabulary, true, 128, 2));
        }
        finally
        {
            File.Delete(garbage);
        }
    }
}
