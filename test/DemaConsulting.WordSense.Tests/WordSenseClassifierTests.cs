namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseClassifier"/> using a deterministic fake encoder.
/// </summary>
public class WordSenseClassifierTests
{
    private static readonly string[] Vocabulary =
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "the", "bat", "##s", "a", "flies", ".", "!", "'", "s"];

    private const string TableSha = "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1";

    private static WordSenseClassifier Create(
        FakeEncoder? encoder = null,
        WordSenseClassifierOptions? options = null,
        bool ownsEncoder = false)
    {
        var table = CreateTable(0.75f);
        return new WordSenseClassifier(encoder ?? new FakeEncoder(), table, new WordPieceTokenizer(Vocabulary), options, ownsEncoder);
    }

    private static WordSenseTable CreateTable(float margin)
    {
        var model = new LemmaModel(
            "bat",
            [new SenseDefinition("animal", ["noun"]), new SenseDefinition("tool", ["verb"])],
            LemmaModelKind.Binary,
            [1f, 0f, 0f],
            [0f],
            margin,
            3);
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["bats"] = "bat" };
        return new WordSenseTable("all-MiniLM-L6-v2", TableSha, 3, [0f, 0f, 0f], [model], forms);
    }

    /// <summary>
    ///     The returned tokens tile the input exactly.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("the bat flies.")]
    [InlineData("  The BAT   flies!!  A bat's wing\t\nends — here 😀 now.  ")]
    [InlineData("don't well-known x1 café")]
    public async Task WordSenseClassifier_ClassifyAsync_Text_TokensTileInput(string text)
    {
        using var classifier = Create();

        var tokens = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal(text, string.Concat(tokens.Select(t => t.Text)));
        var position = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(position, token.Start);
            Assert.Equal(token.Text.Length, token.Length);
            position += token.Length;
        }
    }

    /// <summary>
    ///     The sense depends on context and is attached only to the known word.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly()
    {
        using var classifier = Create();

        var tokens = await classifier.ClassifyAsync("the bat flies. a bat!", TestContext.Current.CancellationToken);

        var senses = tokens.Where(t => t.Sense is not null).ToList();
        Assert.Equal(2, senses.Count);
        Assert.Equal("tool", senses[0].Sense!.Sense);
        Assert.Equal("animal", senses[1].Sense!.Sense);
        Assert.Equal("bat", senses[0].Sense!.Lemma);
        Assert.Same(senses[0].Sense, classifier.GetSenses("bat")[1]);
        Assert.Equal("bat", senses[0].Text);
    }

    /// <summary>
    ///     A sentence is encoded once, however many known words it has.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_ManyTargets_EncodesOnce()
    {
        var encoder = new FakeEncoder();
        using var classifier = Create(encoder);

        await classifier.ClassifyAsync("the bat and a bat and the bats", TestContext.Current.CancellationToken);

        Assert.Equal(1, encoder.Calls);
    }

    /// <summary>
    ///     Text without known words is not encoded at all.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode()
    {
        var encoder = new FakeEncoder();
        using var classifier = Create(encoder);

        var tokens = await classifier.ClassifyAsync("nothing to see here", TestContext.Current.CancellationToken);

        Assert.Equal(0, encoder.Calls);
        Assert.All(tokens, t => Assert.Null(t.Sense));
    }

    /// <summary>
    ///     Case, inflected forms, possessives and curly apostrophes resolve to the lemma.
    /// </summary>
    [Theory]
    [InlineData("BAT")]
    [InlineData("bats")]
    [InlineData("bat's")]
    [InlineData("bat\u2019s")]
    public async Task WordSenseClassifier_ClassifyAsync_Words_ResolvesForms(string word)
    {
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });

        var result = await classifier.ClassifyAsync(["the", word], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Length);
        Assert.Null(result[0]);
        Assert.NotNull(result[1]);
        Assert.Equal("bat", result[1]!.Lemma);
        Assert.Equal("tool", result[1]!.Sense);
    }

    /// <summary>
    ///     The result is parallel to the input; unknown words give null and invalid input throws.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Words_EdgeCases()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;

        Assert.Empty(await classifier.ClassifyAsync(Array.Empty<string>(), token));
        Assert.Equal([null, null], await classifier.ClassifyAsync(["the", "flies"], token));
        Assert.Null((await classifier.ClassifyAsync([""], token))[0]);
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync((IReadOnlyList<string>)null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync(new string[] { "a", null! }, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync((string)null!, token));
    }

    /// <summary>
    ///     A low-confidence classification yields no sense.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_LowMargin_Abstains()
    {
        using var strict = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 10 });
        using var never = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });
        var words = new[] { "the", "bat" };

        Assert.Null((await strict.ClassifyAsync(words, TestContext.Current.CancellationToken))[1]);
        Assert.NotNull((await never.ClassifyAsync(words, TestContext.Current.CancellationToken))[1]);
    }

    /// <summary>
    ///     Long text is classified from a window around each target, with the same answer.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_LongText_UsesWindowPerTarget()
    {
        var encoder = new FakeEncoder();
        using var classifier = Create(encoder, new WordSenseClassifierOptions { MaxSequenceLength = 8 });
        var text = string.Join(' ', Enumerable.Repeat("a flies", 20)) + " the bat " + string.Join(' ', Enumerable.Repeat("a flies", 20));

        var tokens = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal("tool", Assert.Single(tokens, t => t.Sense is not null).Sense!.Sense);
        Assert.All(encoder.Lengths, length => Assert.True(length <= 8));
    }

    /// <summary>
    ///     A table trained with another model is rejected.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_ModelMismatch_Throws()
    {
        var other = new FakeEncoder { Sha = new string('0', 64) };

        Assert.Throws<WordSenseModelMismatchException>(() => Create(other));
        Assert.Throws<WordSenseModelMismatchException>(() => Create(new FakeEncoder { Hidden = 4 }));
    }

    /// <summary>
    ///     Invalid constructor arguments and options are rejected.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { MaxSequenceLength = 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { MaxSequenceLength = 1000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { AbstainMarginScale = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { AbstainMarginScale = double.NaN }));
        Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(null!, CreateTable(1), new WordPieceTokenizer(Vocabulary), null, false));
    }

    /// <summary>
    ///     Cancellation reaches the encoder.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Cancelled_Throws()
    {
        using var classifier = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync("the bat", cts.Token));
    }

    /// <summary>
    ///     Many concurrent calls give consistent answers.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Concurrent_IsConsistent()
    {
        using var classifier = Create();

        var tasks = Enumerable.Range(0, 64).Select(i => classifier.ClassifyAsync(i % 2 == 0 ? new[] { "the", "bat" } : new[] { "a", "bat" }, TestContext.Current.CancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? "tool" : "animal", results[i][1]!.Sense);
        }
    }

    /// <summary>
    ///     Known lemmas and senses are exposed as shared records.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_KnownLemmas_And_GetSenses_AreExposed()
    {
        using var classifier = Create();

        Assert.Equal(["bat"], classifier.KnownLemmas);
        Assert.Equal(["animal", "tool"], classifier.GetSenses("bat").Select(s => s.Sense));
        Assert.Same(classifier.GetSenses("bat")[0], classifier.GetSenses("bat")[0]);
        Assert.Equal(["verb"], classifier.GetSenses("bat")[1].Tags);
        Assert.Empty(classifier.GetSenses("zzz"));
        Assert.Throws<ArgumentNullException>(() => classifier.GetSenses(null!));
    }

    /// <summary>
    ///     An infinite abstain margin scale makes every known word abstain.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_InfiniteAbstainMarginScale_AlwaysAbstains()
    {
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = double.PositiveInfinity });

        var senses = await classifier.ClassifyAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        Assert.All(senses, s => Assert.Null(s));
    }

    /// <summary>
    ///     The detail reports the best sense, its margin and the runner-up.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyDetailAsync_ReportsMarginsAndRunnerUp()
    {
        using var classifier = Create();

        var details = await classifier.ClassifyDetailAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        Assert.Null(details[0]);
        var first = details[1]!.Value;
        Assert.Equal("tool", first.Sense.Sense);
        Assert.Equal("animal", first.RunnerUp.Sense);
        Assert.Equal(Math.Sqrt(2), first.Margin, 4);
        Assert.Equal(0.0, first.RunnerUpMargin);
        Assert.False(first.Abstained);
        Assert.Equal("animal", details[3]!.Value.Sense.Sense);
    }

    /// <summary>
    ///     The detail is still given for an abstained word, flagged as abstained.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyDetailAsync_LowMargin_IsFlaggedAbstained()
    {
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 10 });

        var details = await classifier.ClassifyDetailAsync(["the", "bat"], TestContext.Current.CancellationToken);

        Assert.True(details[1]!.Value.Abstained);
        Assert.Equal("tool", details[1]!.Value.Sense.Sense);
    }

    /// <summary>
    ///     For a three-sense lemma the detail names the runner-up and measures it against the third-best sense; a tie
    ///     for the best score gives a zero margin and the sense listed first wins.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyDetailAsync_ThreeSenses_ReportsRunnerUpMargin()
    {
        var model = new LemmaModel(
            "bat",
            [new SenseDefinition("a", ["noun"]), new SenseDefinition("b", ["noun"]), new SenseDefinition("c", ["verb"])],
            LemmaModelKind.OneVersusRest,
            [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f],
            [0f, 0f, 0f],
            0.1f,
            3);
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", TableSha, 3, [0f, 0f, 0f], [model], new Dictionary<string, string> { ["bat"] = "bat" });
        using var classifier = new WordSenseClassifier(new FakeEncoder(), table, new WordPieceTokenizer(Vocabulary), null, false);

        // After "the" the feature is (0.707, 0.707, 0): senses a and b tie; after "a" it is (-0.707, 0.707, 0)
        var details = await classifier.ClassifyDetailAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        var tie = details[1]!.Value;
        Assert.Equal("a", tie.Sense.Sense);
        Assert.Equal("b", tie.RunnerUp.Sense);
        Assert.Equal(0.0, tie.Margin, 4);
        Assert.Equal(Math.Sqrt(0.5), tie.RunnerUpMargin, 4);
        Assert.True(tie.Abstained);
        var clear = details[3]!.Value;
        Assert.Equal("b", clear.Sense.Sense);
        Assert.Equal("c", clear.RunnerUp.Sense);
        Assert.Equal(Math.Sqrt(0.5), clear.Margin, 4);
        Assert.Equal(Math.Sqrt(0.5), clear.RunnerUpMargin, 4);
        Assert.False(clear.Abstained);
    }

    /// <summary>
    ///     A tag query answers with the best matching tag, or null when the tag is absent or abstained.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyTagAsync_ReturnsTagOrNull()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;
        string[] words = ["the", "bat", "a", "bat", "flies"];

        var result = await classifier.ClassifyTagAsync(words, ["noun", "verb"], token);
        var absent = await classifier.ClassifyTagAsync(words, ["adj"], token);
        using var strict = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 10 });
        var abstained = await strict.ClassifyTagAsync(words, ["noun", "verb"], token);

        Assert.Equal([null, "verb", null, "noun", null], result);
        Assert.All(absent, t => Assert.Null(t));
        Assert.All(abstained, t => Assert.Null(t));
    }

    /// <summary>
    ///     A has-tag query is true or false, and null when absent or abstained.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_IsTagAsync_ReturnsBoolOrNull()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;
        string[] words = ["the", "bat", "a", "bat", "flies"];

        Assert.Equal([null, true, null, false, null], await classifier.IsTagAsync(words, "verb", token));
        Assert.Equal([null, false, null, true, null], await classifier.IsTagAsync(words, "noun", token));
        Assert.All(await classifier.IsTagAsync(words, "adj", token), t => Assert.Null(t));
    }

    /// <summary>
    ///     Invalid tag queries are rejected.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_TagQueries_InvalidArguments_Throw()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => classifier.ClassifyTagAsync(["bat"], Array.Empty<string>(), token));
        await Assert.ThrowsAsync<ArgumentException>(() => classifier.ClassifyTagAsync(["bat"], [""], token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyTagAsync(["bat"], null!, token));
        await Assert.ThrowsAsync<ArgumentException>(() => classifier.IsTagAsync(["bat"], "", token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.IsTagAsync(["bat"], null!, token));
    }

    /// <summary>
    ///     Disposing disposes the encoder only when the classifier owns it.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Dispose_OwnedEncoder_IsDisposed()
    {
        var owned = new FakeEncoder();
        var borrowed = new FakeEncoder();

        Create(owned, ownsEncoder: true).Dispose();
        Create(borrowed, ownsEncoder: false).Dispose();

        Assert.True(owned.Disposed);
        Assert.False(borrowed.Disposed);
    }

    /// <summary>
    ///     Returns hidden states whose first component depends on whether "bat" follows "the".
    /// </summary>
    private sealed class FakeEncoder : IWordSenseEncoder
    {
        private int _calls;
        private readonly List<int> _lengths = [];

        public string Sha { get; init; } = TableSha;

        public int Hidden { get; init; } = 3;

        public string ModelSha256 => Sha;

        public int HiddenSize => Hidden;

        public int Calls => Volatile.Read(ref _calls);

        public bool Disposed { get; private set; }

        public int[] Lengths
        {
            get
            {
                lock (_lengths)
                {
                    return _lengths.ToArray();
                }
            }
        }

        public ValueTask<float[]> EncodeAsync(int[] inputIds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            lock (_lengths)
            {
                _lengths.Add(inputIds.Length);
            }

            const int theId = 4;
            const int batId = 5;
            var rows = new float[inputIds.Length * Hidden];
            for (var i = 0; i < inputIds.Length; i++)
            {
                if (inputIds[i] == batId)
                {
                    rows[i * Hidden] = i > 0 && inputIds[i - 1] == theId ? 1f : -1f;
                }

                rows[(i * Hidden) + 1] = 1f;
            }

            return ValueTask.FromResult(rows);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
