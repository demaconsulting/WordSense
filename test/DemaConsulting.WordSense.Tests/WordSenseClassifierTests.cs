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
            [new SenseDefinition(PartOfSpeech.Noun, "animal"), new SenseDefinition(PartOfSpeech.Verb, "tool")],
            LemmaModelKind.Binary,
            [1f, 0f, 0f],
            [0f],
            margin,
            3);
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["bats"] = "bat" };
        return new WordSenseTable("all-MiniLM-L6-v2", TableSha, 3, [0f, 0f, 0f], [model], forms);
    }

    /// <summary>
    ///     A token sequence built from the segments of a text tiles it, each token has the text at its offsets and
    ///     only words may carry a sense.
    /// </summary>
    [Fact]
    public void WordSenseToken_Tiles_FollowSegmentSpans()
    {
        const string text = "They record it, don't.";
        var details = new WordDetails("record", PartOfSpeech.Verb, null);
        var tokens = TextSplitter.SplitSegments(text)
            .Select(s =>
            {
                var piece = text.Substring(s.Start, s.Length);
                var analysis = s.IsWord && piece == "record"
                    ? new WordAnalysis(piece, details, 1.0, false, null, 0.0)
                    : new WordAnalysis(piece, null, 0.0, false, null, 0.0);
                return new WordSenseToken(s.Start, s.Length, piece, analysis);
            })
            .ToList();

        var position = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(position, token.Start);
            Assert.True(token.Length > 0);
            Assert.Equal(token.Length, token.Text.Length);
            Assert.Equal(text.Substring(token.Start, token.Length), token.Text);
            Assert.Equal(token.Text, token.Analysis.Word);
            position += token.Length;
        }

        Assert.Equal(text.Length, position);
        Assert.Equal(text, string.Concat(tokens.Select(t => t.Text)));
        var tagged = Assert.Single(tokens, t => t.Analysis.Details is not null);
        Assert.Equal("record", tagged.Text);
        Assert.Same(details, tagged.Analysis.Details);
        Assert.Null(tokens[0].Analysis.Details);
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

        var senses = tokens.Where(t => t.Analysis.Details is not null).ToList();
        Assert.Equal(2, senses.Count);
        Assert.Equal(PartOfSpeech.Verb, senses[0].Analysis.Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, senses[1].Analysis.Details!.PartOfSpeech);
        Assert.Equal("bat", senses[0].Analysis.Details!.Lemma);
        Assert.Same(senses[0].Analysis.Details, classifier.GetCandidates("bat")[1]);
        Assert.Equal("bat", senses[0].Text);
        Assert.Equal("bat", senses[0].Analysis.Word);
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
        Assert.All(tokens, t => Assert.Null(t.Analysis.Details));
        Assert.All(tokens, t => Assert.Equal(0.0, t.Analysis.Margin));
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
        Assert.Null(result[0].Details);
        Assert.Equal(word, result[1].Word);
        Assert.NotNull(result[1].Details);
        Assert.Equal("bat", result[1].Details!.Lemma);
        Assert.Equal(PartOfSpeech.Verb, result[1].Details!.PartOfSpeech);
    }

    /// <summary>
    ///     The result is parallel to the input; unknown words give an empty analysis and invalid input throws.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Words_EdgeCases()
    {
        using var classifier = Create();
        var token = TestContext.Current.CancellationToken;

        Assert.Empty(await classifier.ClassifyAsync(Array.Empty<string>(), token));
        var unknown = await classifier.ClassifyAsync(["the", "flies", ""], token);
        Assert.Equal(["the", "flies", ""], unknown.Select(a => a.Word));
        Assert.All(unknown, a =>
        {
            Assert.Null(a.Details);
            Assert.Null(a.RunnerUp);
            Assert.False(a.Abstained);
            Assert.Equal(0.0, a.Margin);
            Assert.Equal(0.0, a.RunnerUpMargin);
        });
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync((IReadOnlyList<string>)null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync(new string[] { "a", null! }, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => classifier.ClassifyAsync((string)null!, token));
    }

    /// <summary>
    ///     A low-confidence classification yields no details but still reports the margin and the runner-up.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_LowMargin_Abstains()
    {
        using var strict = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 10 });
        using var never = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });
        var words = new[] { "the", "bat" };

        var abstained = (await strict.ClassifyAsync(words, TestContext.Current.CancellationToken))[1];
        Assert.Null(abstained.Details);
        Assert.True(abstained.Abstained);
        Assert.Equal(Math.Sqrt(2), abstained.Margin, 4);
        Assert.Equal(PartOfSpeech.Noun, abstained.RunnerUp!.PartOfSpeech);
        Assert.NotNull((await never.ClassifyAsync(words, TestContext.Current.CancellationToken))[1].Details);
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

        Assert.Equal(PartOfSpeech.Verb, Assert.Single(tokens, t => t.Analysis.Details is not null).Analysis.Details!.PartOfSpeech);
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
            Assert.Equal(i % 2 == 0 ? PartOfSpeech.Verb : PartOfSpeech.Noun, results[i][1].Details!.PartOfSpeech);
        }
    }

    /// <summary>
    ///     Known lemmas and candidates are exposed as shared records.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_KnownLemmas_And_GetCandidates_AreExposed()
    {
        using var classifier = Create();

        Assert.Equal(["bat"], classifier.KnownLemmas);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], classifier.GetCandidates("bat").Select(s => s.PartOfSpeech));
        Assert.Equal(["animal", "tool"], classifier.GetCandidates("bat").Select(s => s.Pronunciation!.Meaning));
        Assert.Same(classifier.GetCandidates("bat")[0], classifier.GetCandidates("bat")[0]);
        Assert.Empty(classifier.GetCandidates("zzz"));
        Assert.Throws<ArgumentNullException>(() => classifier.GetCandidates(null!));
    }

    /// <summary>
    ///     An infinite abstain margin scale makes every known word abstain.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_InfiniteAbstainMarginScale_AlwaysAbstains()
    {
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = double.PositiveInfinity });

        var analyses = await classifier.ClassifyAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        Assert.All(analyses, a => Assert.Null(a.Details));
        Assert.True(analyses[1].Abstained);
        Assert.True(analyses[3].Abstained);
        Assert.False(analyses[0].Abstained);
    }

    /// <summary>
    ///     The analysis reports the best answer, its margin and the runner-up on every call.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp()
    {
        using var classifier = Create();

        var analyses = await classifier.ClassifyAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        Assert.Null(analyses[0].Details);
        var first = analyses[1];
        Assert.Equal(PartOfSpeech.Verb, first.Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, first.RunnerUp!.PartOfSpeech);
        Assert.Equal(Math.Sqrt(2), first.Margin, 4);
        Assert.Equal(0.0, first.RunnerUpMargin);
        Assert.False(first.Abstained);
        Assert.Equal(PartOfSpeech.Noun, analyses[3].Details!.PartOfSpeech);
    }

    /// <summary>
    ///     For a three-sense lemma the runner-up is measured against the third-best sense; a tie for the best score
    ///     gives a zero margin and the sense listed first wins.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_ThreeSenses_ReportsRunnerUpMargin()
    {
        var model = new LemmaModel(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb, "a"), new SenseDefinition(PartOfSpeech.Adjective, "b")],
            LemmaModelKind.OneVersusRest,
            [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f],
            [0f, 0f, 0f],
            0.1f,
            3);
        var table = new WordSenseTable(
            "all-MiniLM-L6-v2", TableSha, 3, [0f, 0f, 0f], [model], new Dictionary<string, string> { ["bat"] = "bat" });
        using var classifier = new WordSenseClassifier(new FakeEncoder(), table, new WordPieceTokenizer(Vocabulary), null, false);

        // After "the" the feature is (0.707, 0.707, 0): the first two senses tie; after "a" it is (-0.707, 0.707, 0)
        var analyses = await classifier.ClassifyAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        var tie = analyses[1];
        Assert.True(tie.Abstained);
        Assert.Null(tie.Details);
        Assert.Equal(PartOfSpeech.Verb, tie.RunnerUp!.PartOfSpeech);
        Assert.Equal("a", tie.RunnerUp.Pronunciation!.Meaning);
        Assert.Equal(0.0, tie.Margin, 4);
        Assert.Equal(Math.Sqrt(0.5), tie.RunnerUpMargin, 4);
        var clear = analyses[3];
        Assert.False(clear.Abstained);
        Assert.Equal(PartOfSpeech.Verb, clear.Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Adjective, clear.RunnerUp!.PartOfSpeech);
        Assert.Equal(Math.Sqrt(0.5), clear.Margin, 4);
        Assert.Equal(Math.Sqrt(0.5), clear.RunnerUpMargin, 4);
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
