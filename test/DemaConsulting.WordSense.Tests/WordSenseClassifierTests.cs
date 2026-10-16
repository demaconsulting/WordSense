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
        return CreateTable(margin, []);
    }

    private static WordSenseTable CreateTable(float margin, IReadOnlyList<LemmaEntry> dictionary)
    {
        var model = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "animal"), new SenseDefinition(PartOfSpeech.Verb, "tool")],
            new SenseWeights(LemmaModelKind.Binary, [1f, 0f, 0f], [0f], margin, 3));
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["bats"] = "bat" };
        var all = dictionary.Append(model).OrderBy(e => e.Lemma, StringComparer.Ordinal).ToArray();
        return new WordSenseTable("all-MiniLM-L6-v2", TableSha, 3, [0f, 0f, 0f], all, forms);
    }

    private static LemmaEntry Word(string lemma, params PartOfSpeech[] parts) =>
        new(lemma, parts.Select(p => new SenseDefinition(p)).ToArray());

    /// <summary>
    ///     Creates a classifier whose table has the trained lemma bat and a small dictionary.
    /// </summary>
    private static WordSenseClassifier CreateWithDictionary(FakeEncoder? encoder = null, WordSenseClassifierOptions? options = null)
    {
        var table = CreateTable(
            0.75f,
            [
                Word("cat", PartOfSpeech.Noun),
                Word("i", PartOfSpeech.Pronoun),
                Word("quick", PartOfSpeech.Adjective),
                Word("run", PartOfSpeech.Noun, PartOfSpeech.Verb),
            ]);
        return new WordSenseClassifier(encoder ?? new FakeEncoder(), table, new WordPieceTokenizer(Vocabulary), options, false);
    }

    /// <summary>
    ///     A dictionary word with one part of speech is answered from the table without running the encoder.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutEncoder()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder);

        var analyses = await classifier.ClassifyAsync(["a", "cat", "is", "quick"], TestContext.Current.CancellationToken);

        var cat = analyses[1];
        Assert.Equal(AnswerBasis.Dictionary, cat.Basis);
        Assert.Equal(new WordDetails("cat", PartOfSpeech.Noun, null), cat.Details);
        Assert.False(cat.Abstained);
        Assert.Equal(0.0, cat.Margin);
        Assert.Null(cat.RunnerUp);
        Assert.Equal(0.0, cat.RunnerUpMargin);
        Assert.Same(cat.Details, Assert.Single(cat.Candidates));
        Assert.Equal(PartOfSpeech.Adjective, analyses[3].Details!.PartOfSpeech);
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A dictionary word with several parts of speech and no classifier abstains and lists its candidates.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_DictionaryMultiSense_ReturnsCandidates()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder);

        var run = (await classifier.ClassifyAsync(["a", "run"], TestContext.Current.CancellationToken))[1];

        Assert.Null(run.Details);
        Assert.True(run.Abstained);
        Assert.Equal(AnswerBasis.Dictionary, run.Basis);
        Assert.Equal(0.0, run.Margin);
        Assert.Null(run.RunnerUp);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], run.Candidates.Select(c => c.PartOfSpeech));
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     An unknown word has no basis and an empty, non-null candidate list.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder);

        var analyses = await classifier.ClassifyAsync(["zzz", ".", ""], TestContext.Current.CancellationToken);

        Assert.All(analyses, a =>
        {
            Assert.Equal(AnswerBasis.None, a.Basis);
            Assert.NotNull(a.Candidates);
            Assert.Empty(a.Candidates);
            Assert.Null(a.Details);
            Assert.False(a.Abstained);
        });
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A word derived from a dictionary base by a morphology rule is a dictionary answer.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_DerivedPlural_AnswersFromDictionary()
    {
        using var classifier = CreateWithDictionary();

        var cats = (await classifier.ClassifyAsync(["a", "cats"], TestContext.Current.CancellationToken))[1];

        Assert.Equal(AnswerBasis.Dictionary, cats.Basis);
        Assert.Equal("cat", cats.Details!.Lemma);
        Assert.Equal(PartOfSpeech.Noun, cats.Details.PartOfSpeech);
        Assert.Equal("cats", cats.Word);
        Assert.Single(cats.Candidates);
    }

    /// <summary>
    ///     A context answer has the context basis and every sense of the lemma as candidates, also when it abstains.
    /// </summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(10.0, true)]
    public async Task WordSenseClassifier_ClassifyAsync_ContextAnswer_HasContextBasisAndCandidates(double scale, bool abstains)
    {
        using var classifier = CreateWithDictionary(options: new WordSenseClassifierOptions { AbstainMarginScale = scale });

        var bat = (await classifier.ClassifyAsync(["the", "bat"], TestContext.Current.CancellationToken))[1];

        Assert.Equal(AnswerBasis.Context, bat.Basis);
        Assert.Equal(abstains, bat.Abstained);
        Assert.Equal(abstains, bat.Details is null);
        Assert.Same(classifier.GetCandidates("bat"), bat.Candidates);
        Assert.Contains(bat.RunnerUp, bat.Candidates);
        Assert.True(abstains || bat.Candidates.Contains(bat.Details!));
    }

    /// <summary>
    ///     The encoder runs once for a sentence that mixes a context word with dictionary and unknown words, and the
    ///     dictionary words are answered from the same call; the text overload behaves the same.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder);
        var token = TestContext.Current.CancellationToken;

        var words = await classifier.ClassifyAsync(["the", "bat", "and", "a", "cat", "run"], token);
        Assert.Equal(1, encoder.Calls);
        Assert.Equal(
            [AnswerBasis.None, AnswerBasis.Context, AnswerBasis.None, AnswerBasis.None, AnswerBasis.Dictionary, AnswerBasis.Dictionary],
            words.Select(w => w.Basis));

        var tokens = await classifier.ClassifyAsync("the bat and a cat", token);
        Assert.Equal(2, encoder.Calls);
        Assert.Equal(1, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Context));
        Assert.Equal(1, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Dictionary));
    }

    /// <summary>
    ///     Text made only of dictionary, candidates-only and unknown words never reaches the encoder, whether it is
    ///     given as a list or as text, and whether it is short or longer than the window.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_NoContextWords_NeverEncodes()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder, new WordSenseClassifierOptions { MaxSequenceLength = 8 });
        var token = TestContext.Current.CancellationToken;

        await classifier.ClassifyAsync(["a", "cat", "run", "zzz", "quick"], token);
        var tokens = await classifier.ClassifyAsync("a cat can run. " + string.Join(' ', Enumerable.Repeat("quick cat", 30)), token);

        Assert.Equal(0, encoder.Calls);
        Assert.Equal(62, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Dictionary));
    }

    /// <summary>
    ///     A long text with context words encodes only for those words.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_LongTextWithDictionaryWords_EncodesOnlyContextTargets()
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder, new WordSenseClassifierOptions { MaxSequenceLength = 8 });
        var text = string.Join(' ', Enumerable.Repeat("quick cat", 20)) + " the bat " + string.Join(' ', Enumerable.Repeat("quick cat", 20));

        await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal(1, encoder.Calls);
    }

    /// <summary>
    ///     UseLexicon false answers only lemmas with a classifier: dictionary words are unknown and not encoded.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_UseLexiconFalse_AnswersOnlyContextLemmas()
    {
        var encoder = new FakeEncoder();
        using var off = CreateWithDictionary(encoder, new WordSenseClassifierOptions { UseLexicon = false });
        using var on = CreateWithDictionary();
        var token = TestContext.Current.CancellationToken;

        var dictionaryOnly = await off.ClassifyAsync(["a", "cat", "cats", "run"], token);
        Assert.Equal(0, encoder.Calls);
        Assert.All(dictionaryOnly, a => Assert.Equal(AnswerBasis.None, a.Basis));

        var mixed = await off.ClassifyAsync(["the", "bat", "cat"], token);
        Assert.Equal(AnswerBasis.Context, mixed[1].Basis);
        Assert.Equal(AnswerBasis.None, mixed[2].Basis);
        Assert.Equal(1, encoder.Calls);

        Assert.Empty(off.GetCandidates("cat"));
        Assert.Single(on.GetCandidates("cat"));
        Assert.Equal(2, off.GetCandidates("bat").Count);
        Assert.Equal(["bat"], off.ContextLemmas);
    }

    /// <summary>
    ///     With UseLexicon false a dictionary word, also one derived by a rule, has no basis and no candidates.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_UseLexiconFalse_DictionaryWordIsUnknown()
    {
        using var classifier = CreateWithDictionary(options: new WordSenseClassifierOptions { UseLexicon = false });

        var analyses = await classifier.ClassifyAsync(["a", "cat", "cats", "quick"], TestContext.Current.CancellationToken);

        Assert.All(analyses, a =>
        {
            Assert.Equal(AnswerBasis.None, a.Basis);
            Assert.Empty(a.Candidates);
            Assert.Null(a.Details);
        });
    }

    /// <summary>
    ///     A capitalized dictionary word at the start of a sentence is looked up.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_SentenceStartIsLookedUp()
    {
        using var classifier = CreateWithDictionary();

        var analyses = await classifier.ClassifyAsync(["Cat", "a", ".", "Quick"], TestContext.Current.CancellationToken);

        Assert.Equal(AnswerBasis.Dictionary, analyses[0].Basis);
        Assert.Equal(AnswerBasis.Dictionary, analyses[3].Basis);
    }

    /// <summary>
    ///     A capitalized dictionary word in the middle of a sentence is treated as a name and is unknown.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_MidSentenceCapitalIsUnknown()
    {
        using var classifier = CreateWithDictionary();

        var analyses = await classifier.ClassifyAsync(["a", "Cat", "Quick"], TestContext.Current.CancellationToken);

        Assert.Equal(AnswerBasis.None, analyses[1].Basis);
        Assert.Equal(AnswerBasis.None, analyses[2].Basis);
    }

    /// <summary>
    ///     The pronoun I is looked up wherever it stands.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_PronounIIsLookedUp()
    {
        using var classifier = CreateWithDictionary();

        var analyses = await classifier.ClassifyAsync(["a", "cat", "I"], TestContext.Current.CancellationToken);

        Assert.Equal(AnswerBasis.Dictionary, analyses[2].Basis);
        Assert.Equal(PartOfSpeech.Pronoun, analyses[2].Details!.PartOfSpeech);
    }

    /// <summary>
    ///     The case policy applies to dictionary words only: a capitalized word is looked up at the start of the input
    ///     or of a sentence, after an opening quote or bracket, or when it is I; otherwise it is unknown.
    /// </summary>
    [Theory]
    [InlineData("Cat", true)]
    [InlineData("a . Cat", true)]
    [InlineData("a ! Cat", true)]
    [InlineData("a ? Cat", true)]
    [InlineData("a : Cat", true)]
    [InlineData("a \" Cat", true)]
    [InlineData("a ( Cat", true)]
    [InlineData("a [ Cat", true)]
    [InlineData("a \u201C Cat", true)]
    [InlineData("a Cat", false)]
    [InlineData("a , Cat", false)]
    [InlineData("a cat", true)]
    [InlineData("a CAT", false)]
    [InlineData("a I", true)]
    [InlineData("a Quick", false)]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_AppliesToDictionaryWords(string sentence, bool answered)
    {
        var encoder = new FakeEncoder();
        using var classifier = CreateWithDictionary(encoder);
        var words = sentence.Split(' ');

        var analyses = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var last = analyses[^1];
        Assert.Equal(answered ? AnswerBasis.Dictionary : AnswerBasis.None, last.Basis);
        Assert.Equal(answered, last.Details is not null);
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     The case policy does not affect a lemma with a trained classifier, and the text overload uses the previous
    ///     non-space token.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_TrainedLemmasAndTextOverload()
    {
        using var classifier = CreateWithDictionary();
        var token = TestContext.Current.CancellationToken;

        var tokens = await classifier.ClassifyAsync("a cat. Cat and a Cat", token);

        var cats = tokens.Where(t => t.Text.Equals("cat", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(
            [AnswerBasis.Dictionary, AnswerBasis.Dictionary, AnswerBasis.None],
            cats.Select(t => t.Analysis.Basis));
        var bat = (await classifier.ClassifyAsync(["a", "BAT"], token))[1];
        Assert.Equal(AnswerBasis.Context, bat.Basis);
    }

    /// <summary>
    ///     GetCandidates works for a dictionary lemma and ContextLemmas lists only trained lemmas.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_GetCandidates_DictionaryLemma_ReturnsSenses()
    {
        using var classifier = CreateWithDictionary();

        Assert.Equal(["bat"], classifier.ContextLemmas);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], classifier.GetCandidates("run").Select(c => c.PartOfSpeech));
        Assert.Equal(new WordDetails("cat", PartOfSpeech.Noun, null), Assert.Single(classifier.GetCandidates("cat")));
        Assert.Empty(classifier.GetCandidates("zzz"));
    }

    /// <summary>
    ///     A token sequence returned for a text tiles it, each token has the text at its offsets and
    ///     only known words carry a sense.
    /// </summary>
    [Fact]
    public async Task WordSenseToken_Tiles_FollowSegmentSpans()
    {
        const string text = "They bat it, don't.";
        using var classifier = Create(options: new WordSenseClassifierOptions { AbstainMarginScale = 0 });

        var tokens = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

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
        Assert.Equal("bat", tagged.Text);
        Assert.Equal(5, tagged.Start);
        Assert.Same(classifier.GetCandidates("bat")[0], tagged.Analysis.Details);
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
            Assert.Equal(token.Text, token.Analysis.Word);
            if (string.IsNullOrWhiteSpace(token.Text))
            {
                Assert.Null(token.Analysis.Details);
                Assert.Equal(0.0, token.Analysis.Margin);
            }

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
        var text = string.Join(' ', Enumerable.Repeat("a flies", 20)) + " the bat " + string.Join(' ', Enumerable.Repeat("a flies", 20))
            + " a bat " + string.Join(' ', Enumerable.Repeat("a flies", 20));

        var tokens = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        var known = tokens.Where(t => t.Analysis.Details is not null).ToList();
        Assert.Equal(2, known.Count);
        Assert.Equal(PartOfSpeech.Verb, known[0].Analysis.Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, known[1].Analysis.Details!.PartOfSpeech);
        Assert.Equal(2, encoder.Calls);
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
        Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(new FakeEncoder(), null!, new WordPieceTokenizer(Vocabulary), null, false));
        Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(new FakeEncoder(), CreateTable(1), (WordPieceTokenizer)null!, null, false));
    }

    /// <summary>
    ///     The public constructor that takes a vocabulary path reads the file and reports the documented exceptions
    ///     for a null, empty or missing path and for a bad vocabulary or table.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_VocabularyPath_ReadsFileOrThrows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wordsense-classifier-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "vocab.txt");
            File.WriteAllLines(path, Vocabulary);

            // A valid vocabulary classifies like the in-memory tokenizer
            using (var classifier = new WordSenseClassifier(new FakeEncoder(), CreateTable(0.75f), path))
            {
                Assert.Equal(["bat"], classifier.ContextLemmas);
            }

            Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(new FakeEncoder(), CreateTable(0.75f), null!));
            Assert.Throws<ArgumentException>(() => new WordSenseClassifier(new FakeEncoder(), CreateTable(0.75f), string.Empty));
            Assert.Throws<FileNotFoundException>(() => new WordSenseClassifier(new FakeEncoder(), CreateTable(0.75f), Path.Combine(directory, "missing.txt")));

            // The vocabulary is read first, so a missing special token is reported as an argument error
            var incomplete = Path.Combine(directory, "incomplete.txt");
            File.WriteAllLines(incomplete, ["the", "bat"]);
            Assert.Throws<ArgumentException>(() => new WordSenseClassifier(new FakeEncoder(), CreateTable(0.75f), incomplete));

            // The other arguments are still checked
            Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(null!, CreateTable(0.75f), path));
            Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(new FakeEncoder(), null!, path));
            Assert.Throws<WordSenseModelMismatchException>(() => new WordSenseClassifier(new FakeEncoder { Hidden = 4 }, CreateTable(0.75f), path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A token already cancelled when the call starts is rejected at the start of the call, before any encoding.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Cancelled_Throws()
    {
        var encoder = new FakeEncoder();
        using var classifier = Create(encoder);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync("the bat", cts.Token));
        Assert.Equal(0, encoder.Calls);
    }

    /// <summary>
    ///     A token cancelled by the encoder during its first call stops the classification before the next target.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CancelledDuringEncode_StopsBeforeNextTarget()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var encoder = new FakeEncoder { CancelDuringEncode = cts };
        using var classifier = Create(encoder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync("the bat and a bat", cts.Token));

        Assert.Equal(1, encoder.Calls);
    }

    /// <summary>
    ///     A token cancelled during the encoding of the first window of a long text stops the classification at the
    ///     check before the second target, which is a real cancellation between two window encodings.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CancelledBetweenTargets_StopsAfterFirstWindow()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var encoder = new FakeEncoder { CancelDuringEncode = cts };
        using var classifier = Create(encoder, new WordSenseClassifierOptions { MaxSequenceLength = 8 });
        var filler = string.Join(' ', Enumerable.Repeat("a flies", 20));
        var text = filler + " the bat " + filler + " a bat " + filler;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync(text, cts.Token));

        // The first window was encoded (the first target passed its check); the second target never was
        Assert.Equal(1, encoder.Calls);
        Assert.True(encoder.Lengths[0] <= 8);
    }

    /// <summary>
    ///     A word with trained weights whose text yields no word pieces, or whose feature has no direction, stays
    ///     unknown, and so does a form derived by morphology from a trained lemma.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown()
    {
        var model = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "animal"), new SenseDefinition(PartOfSpeech.Verb, "tool")],
            new SenseWeights(LemmaModelKind.Binary, [1f, 0f, 0f], [0f], 0.75f, 3));
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["\u0001"] = "bat" };

        // The mean equals the hidden state of "bat" after "the", so its centered feature is zero
        var table = new WordSenseTable("all-MiniLM-L6-v2", TableSha, 3, [1f, 1f, 0f], [model], forms);
        var encoder = new FakeEncoder();
        using var classifier = new WordSenseClassifier(encoder, table, new WordPieceTokenizer(Vocabulary), null, false);

        var noPieces = await classifier.ClassifyAsync(["\u0001"], TestContext.Current.CancellationToken);
        Assert.Equal(AnswerBasis.None, noPieces[0].Basis);
        Assert.Null(noPieces[0].Details);
        Assert.False(noPieces[0].Abstained);
        Assert.Empty(noPieces[0].Candidates);

        var zeroNorm = await classifier.ClassifyAsync(["the", "bat"], TestContext.Current.CancellationToken);
        Assert.Equal(AnswerBasis.None, zeroNorm[1].Basis);
        Assert.Null(zeroNorm[1].Details);
        Assert.False(zeroNorm[1].Abstained);

        // Morphology never derives from a trained lemma, so no sense is chosen without the classifier
        var calls = encoder.Calls;
        var derived = await classifier.ClassifyAsync(["batted", "batly"], TestContext.Current.CancellationToken);
        Assert.All(derived, a => Assert.Equal(AnswerBasis.None, a.Basis));
        Assert.Equal(calls, encoder.Calls);
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
    public void WordSenseClassifier_ContextLemmas_And_GetCandidates_AreExposed()
    {
        using var classifier = Create();

        Assert.Equal(["bat"], classifier.ContextLemmas);
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
        var model = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb, "a"), new SenseDefinition(PartOfSpeech.Adjective, "b")],
            new SenseWeights(LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0f, 0f], 0.1f, 3));
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
    ///     After an owned classifier is disposed, a call with a known word throws, a call without one still returns
    ///     and disposing again is harmless.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_Dispose_OwnedEncoder_LaterCallsBehaveAsDocumented()
    {
        var encoder = new FakeEncoder();
        var classifier = Create(encoder, ownsEncoder: true);
        var token = TestContext.Current.CancellationToken;

        classifier.Dispose();
        classifier.Dispose();

        Assert.True(encoder.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(["the", "bat"], token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync("the bat", token));
        var tokens = await classifier.ClassifyAsync("nothing to see here", token);
        Assert.All(tokens, t => Assert.Null(t.Analysis.Details));
        var words = await classifier.ClassifyAsync(["nothing", "here"], token);
        Assert.All(words, a => Assert.Null(a.Details));
        Assert.Equal(0, encoder.Calls);
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

        public CancellationTokenSource? CancelDuringEncode { get; init; }

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
            ObjectDisposedException.ThrowIf(Disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            CancelDuringEncode?.Cancel();
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
