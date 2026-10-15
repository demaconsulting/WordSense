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
        var model = new LemmaModel("bat", ["animal", "tool"], LemmaModelKind.Binary, [1f, 0f, 0f], [0f], margin, 3);
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
        Assert.Equal("tool", senses[0].Sense!.Label);
        Assert.Equal("animal", senses[1].Sense!.Label);
        Assert.Equal("bat", senses[0].Sense!.Lemma);
        Assert.True(senses[0].Sense!.Margin >= 0.75);
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
    [InlineData("the BAT", 4, 3)]
    [InlineData("the bats", 4, 4)]
    [InlineData("the bat's", 4, 5)]
    [InlineData("the bat\u2019s", 4, 5)]
    public async Task WordSenseClassifier_ClassifyAsync_Span_ResolvesForms(string text, int start, int length)
    {
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });

        var result = await classifier.ClassifyAsync(text, start, length, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("bat", result.Lemma);
        Assert.Equal("tool", result.Label);
    }

    /// <summary>
    ///     Unknown words, empty spans and spans outside the text are handled.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Span_EdgeCases()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;

        Assert.Null(await classifier.ClassifyAsync("the flies", 4, 5, token));
        Assert.Null(await classifier.ClassifyAsync("the bat", 4, 0, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => classifier.ClassifyAsync("the bat", 5, 9, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => classifier.ClassifyAsync("the bat", -1, 2, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync(null!, token));
    }

    /// <summary>
    ///     A low-confidence classification yields no sense.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_LowMargin_Abstains()
    {
        using var strict = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 10 });
        using var never = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });

        Assert.Null(await strict.ClassifyAsync("the bat", 4, 3, TestContext.Current.CancellationToken));
        Assert.NotNull(await never.ClassifyAsync("the bat", 4, 3, TestContext.Current.CancellationToken));
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

        Assert.Equal("tool", Assert.Single(tokens, t => t.Sense is not null).Sense!.Label);
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

        var tasks = Enumerable.Range(0, 64).Select(i => classifier.ClassifyAsync(i % 2 == 0 ? "the bat" : "a bat", 4 - (i % 2 * 2), 3, TestContext.Current.CancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? "tool" : "animal", results[i]!.Label);
        }
    }

    /// <summary>
    ///     Known lemmas and labels are exposed.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_KnownLemmas_And_SenseLabels_AreExposed()
    {
        using var classifier = Create();

        Assert.Equal(["bat"], classifier.KnownLemmas);
        Assert.Equal(["animal", "tool"], classifier.GetSenseLabels("bat"));
        Assert.Empty(classifier.GetSenseLabels("zzz"));
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
