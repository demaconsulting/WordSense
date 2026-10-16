namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSenseClassifier"/> using a deterministic fake model and fake weights.
/// </summary>
public class WordSenseClassifierTests
{
    private static WordSenseTable CreateTable(IReadOnlyList<LemmaEntry>? dictionary = null)
    {
        var bat = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "animal"), new SenseDefinition(PartOfSpeech.Verb, "tool")],
            true);
        var forms = new Dictionary<string, string> { ["bat"] = "bat", ["bats"] = "bat" };
        var all = (dictionary ?? []).Append(bat).OrderBy(e => e.Lemma, StringComparer.Ordinal).ToArray();
        return new WordSenseTable(all, forms);
    }

    private static WordSenseWeightsTable CreateWeights(WordSenseTable table, float margin = 0.75f, float[]? mean = null)
    {
        var weights = new Dictionary<string, SenseWeights>
        {
            ["bat"] = new(LemmaModelKind.Binary, [1f, 0f, 0f], [0f], margin, 3),
        };
        return new WordSenseWeightsTable(
            FakeWordSenseModel.Id, FakeWordSenseModel.Sha, table.Sha256, 3, mean ?? [0f, 0f, 0f], weights);
    }

    private static LemmaEntry Word(string lemma, params PartOfSpeech[] parts) =>
        new(lemma, parts.Select(p => new SenseDefinition(p)).ToArray());

    private static WordSenseClassifier Create(
        FakeWordSenseModel? model = null,
        WordSenseClassifierOptions? options = null,
        bool ownsModel = false)
    {
        var table = CreateTable();
        return new WordSenseClassifier(table, model ?? new FakeWordSenseModel(), CreateWeights(table), options, ownsModel);
    }

    private static WordSenseClassifier CreateWithDictionary(FakeWordSenseModel? model = null, WordSenseClassifierOptions? options = null)
    {
        var table = CreateTable(
            [
                Word("cat", PartOfSpeech.Noun),
                Word("i", PartOfSpeech.Pronoun),
                Word("quick", PartOfSpeech.Adjective),
                Word("run", PartOfSpeech.Noun, PartOfSpeech.Verb),
            ]);
        return new WordSenseClassifier(table, model ?? new FakeWordSenseModel(), CreateWeights(table), options, false);
    }

    /// <summary>
    ///     A lexicon-only classifier needs no model and answers a single-sense word from the dictionary.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_Create_LexiconOnly_AnswersSingleSenseWord()
    {
        using var classifier = WordSenseClassifier.Create();

        var cat = (await classifier.ClassifyAsync(["a", "hunter", "is", "quick"], TestContext.Current.CancellationToken))[1];

        Assert.Equal(AnswerBasis.Dictionary, cat.Basis);
        Assert.False(cat.Abstained);
        Assert.Equal("hunter", cat.Details!.Lemma);
        Assert.Equal(PartOfSpeech.Noun, cat.Details.PartOfSpeech);
        Assert.Empty(classifier.ContextLemmas);
    }

    /// <summary>
    ///     A lexicon-only classifier abstains on a word that needs context and lists its candidates.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_Create_LexiconOnly_ContextualWordAbstainsWithCandidates()
    {
        using var classifier = WordSenseClassifier.Create();

        var analyses = await classifier.ClassifyAsync(["the", "record", "will", "xyzzyq"], TestContext.Current.CancellationToken);

        foreach (var index in new[] { 1, 2 })
        {
            Assert.Equal(AnswerBasis.Dictionary, analyses[index].Basis);
            Assert.True(analyses[index].Abstained);
            Assert.Null(analyses[index].Details);
            Assert.True(analyses[index].Candidates.Count >= 2);
            Assert.Equal(0.0, analyses[index].Margin);
        }

        Assert.Equal(AnswerBasis.None, analyses[3].Basis);
    }

    /// <summary>
    ///     A lexicon-only classifier from a custom table abstains on its context lemma with the lemma's senses.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_Constructor_TableWithoutModel_ContextLemmaAbstains()
    {
        using var classifier = new WordSenseClassifier(CreateTable());

        var bat = (await classifier.ClassifyAsync(["the", "bats"], TestContext.Current.CancellationToken))[1];

        Assert.True(bat.Abstained);
        Assert.Equal(AnswerBasis.Dictionary, bat.Basis);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], bat.Candidates.Select(c => c.PartOfSpeech));
        Assert.Empty(classifier.ContextLemmas);
    }

    /// <summary>
    ///     A lexicon-only classifier rejects invalid options.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Create_InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WordSenseClassifier.Create(new WordSenseClassifierOptions { AbstainMarginScale = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WordSenseClassifier.Create(new WordSenseClassifierOptions { AbstainMarginScale = double.NaN }));
    }

    /// <summary>
    ///     A dictionary word with one part of speech is answered from the table without running the model.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutModel()
    {
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);

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
        Assert.Equal(0, model.Calls);
    }

    /// <summary>
    ///     A dictionary word with several parts of speech abstains and lists its candidates.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_DictionaryMultiSense_ReturnsCandidates()
    {
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);

        var run = (await classifier.ClassifyAsync(["a", "run"], TestContext.Current.CancellationToken))[1];

        Assert.Null(run.Details);
        Assert.True(run.Abstained);
        Assert.Equal(AnswerBasis.Dictionary, run.Basis);
        Assert.Equal(0.0, run.Margin);
        Assert.Null(run.RunnerUp);
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], run.Candidates.Select(c => c.PartOfSpeech));
        Assert.Equal(0, model.Calls);
    }

    /// <summary>
    ///     An unknown word has no basis and an empty, non-null candidate list.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone()
    {
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);

        var analyses = await classifier.ClassifyAsync(["zzz", ".", ""], TestContext.Current.CancellationToken);

        Assert.All(analyses, a =>
        {
            Assert.Equal(AnswerBasis.None, a.Basis);
            Assert.NotNull(a.Candidates);
            Assert.Empty(a.Candidates);
            Assert.Null(a.Details);
            Assert.False(a.Abstained);
        });
        Assert.Equal(0, model.Calls);
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
    ///     The model runs once for a sentence that mixes a context word with dictionary and unknown words, and the
    ///     dictionary words are answered from the same call; the text overload behaves the same.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce()
    {
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);
        var token = TestContext.Current.CancellationToken;

        var words = await classifier.ClassifyAsync(["the", "bat", "and", "a", "cat", "run"], token);
        Assert.Equal(1, model.Calls);
        Assert.Equal(
            [AnswerBasis.None, AnswerBasis.Context, AnswerBasis.None, AnswerBasis.None, AnswerBasis.Dictionary, AnswerBasis.Dictionary],
            words.Select(w => w.Basis));

        var tokens = await classifier.ClassifyAsync("the bat and a cat", token);
        Assert.Equal(2, model.Calls);
        Assert.Equal(1, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Context));
        Assert.Equal(1, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Dictionary));
    }

    /// <summary>
    ///     Text made only of dictionary and unknown words never reaches the model.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_NoContextWords_NeverEncodes()
    {
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);
        var token = TestContext.Current.CancellationToken;

        await classifier.ClassifyAsync(["a", "cat", "run", "zzz", "quick"], token);
        var tokens = await classifier.ClassifyAsync("a cat can run. " + string.Join(' ', Enumerable.Repeat("quick cat", 30)), token);

        Assert.Equal(0, model.Calls);
        Assert.Equal(62, tokens.Count(t => t.Analysis.Basis == AnswerBasis.Dictionary));
    }

    /// <summary>
    ///     UseLexicon false answers only lemmas with a classifier: dictionary words are unknown and not encoded.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_UseLexiconFalse_AnswersOnlyContextLemmas()
    {
        var model = new FakeWordSenseModel();
        using var off = CreateWithDictionary(model, new WordSenseClassifierOptions { UseLexicon = false });
        using var on = CreateWithDictionary();
        var token = TestContext.Current.CancellationToken;

        var dictionaryOnly = await off.ClassifyAsync(["a", "cat", "cats", "run"], token);
        Assert.Equal(0, model.Calls);
        Assert.All(dictionaryOnly, a => Assert.Equal(AnswerBasis.None, a.Basis));

        var mixed = await off.ClassifyAsync(["the", "bat", "cat"], token);
        Assert.Equal(AnswerBasis.Context, mixed[1].Basis);
        Assert.Equal(AnswerBasis.None, mixed[2].Basis);
        Assert.Equal(1, model.Calls);

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
        var model = new FakeWordSenseModel();
        using var classifier = CreateWithDictionary(model);
        var words = sentence.Split(' ');

        var analyses = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var last = analyses[^1];
        Assert.Equal(answered ? AnswerBasis.Dictionary : AnswerBasis.None, last.Basis);
        Assert.Equal(answered, last.Details is not null);
        Assert.Equal(0, model.Calls);
    }

    /// <summary>
    ///     The case policy does not affect a context lemma, and the text overload uses the previous non-space token.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CasePolicy_ContextLemmasAndTextOverload()
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
    ///     GetCandidates works for a dictionary lemma and ContextLemmas lists only lemmas with weights.
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
        var model = new FakeWordSenseModel();
        using var classifier = Create(model);

        await classifier.ClassifyAsync("the bat and a bat and the bats", TestContext.Current.CancellationToken);

        Assert.Equal(1, model.Calls);
    }

    /// <summary>
    ///     Text without known words is not encoded at all.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode()
    {
        var model = new FakeWordSenseModel();
        using var classifier = Create(model);

        var tokens = await classifier.ClassifyAsync("nothing to see here", TestContext.Current.CancellationToken);

        Assert.Equal(0, model.Calls);
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
    ///     A model that does not match the weights is rejected, and so is a weights table of another lexicon.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_Mismatch_Throws()
    {
        var table = CreateTable();
        var weights = CreateWeights(table);

        Assert.Throws<WordSenseModelMismatchException>(() =>
            new WordSenseClassifier(table, new FakeWordSenseModel { ModelSha256 = new string('0', 64) }, weights));
        Assert.Throws<WordSenseModelMismatchException>(() =>
            new WordSenseClassifier(table, new FakeWordSenseModel { ModelId = "other" }, weights));
        Assert.Throws<WordSenseModelMismatchException>(() =>
            new WordSenseClassifier(table, new FakeWordSenseModel { HiddenSize = 4 }, weights));

        var otherLexicon = CreateTable([Word("cat", PartOfSpeech.Noun)]);
        Assert.Throws<WordSenseModelMismatchException>(() =>
            new WordSenseClassifier(otherLexicon, new FakeWordSenseModel(), weights));
    }

    /// <summary>
    ///     A model and its weights must be given together.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_ModelWithoutWeights_Throws()
    {
        var table = CreateTable();
        var weights = CreateWeights(table);

        Assert.Throws<ArgumentException>(() => new WordSenseClassifier(table, new FakeWordSenseModel(), null));
        Assert.Throws<ArgumentException>(() => new WordSenseClassifier(table, null, weights));
    }

    /// <summary>
    ///     Invalid constructor arguments and options are rejected.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Constructor_InvalidArguments_Throw()
    {
        var table = CreateTable();
        Assert.Throws<ArgumentNullException>(() => new WordSenseClassifier(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { AbstainMarginScale = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(options: new WordSenseClassifierOptions { AbstainMarginScale = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordSenseClassifier(table, null, null, new WordSenseClassifierOptions { AbstainMarginScale = -1 }));
    }

    /// <summary>
    ///     A token already cancelled when the call starts is rejected before any encoding.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_Cancelled_Throws()
    {
        var model = new FakeWordSenseModel();
        using var classifier = Create(model);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync("the bat", cts.Token));
        Assert.Equal(0, model.Calls);
    }

    /// <summary>
    ///     A token cancelled by the model while it encodes stops the classification.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_CancelledDuringEncode_Throws()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var model = new FakeWordSenseModel { CancelDuringEncode = cts };
        using var classifier = Create(model);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync("the bat and a bat", cts.Token));
    }

    /// <summary>
    ///     A word the model gives no embedding for, or whose feature has no direction, stays unknown, and so does a
    ///     form derived by morphology from a context lemma.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown()
    {
        var bat = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "animal"), new SenseDefinition(PartOfSpeech.Verb, "tool")],
            true);
        var table = new WordSenseTable([bat], new Dictionary<string, string> { ["bat"] = "bat", ["\u0001"] = "bat" });

        // The mean equals the vector of "bat" after "the", so its centered feature is zero
        var weights = CreateWeights(table, mean: [1f, 1f, 0f]);
        var model = new FakeWordSenseModel();
        using var classifier = new WordSenseClassifier(table, model, weights);

        var noEmbedding = await classifier.ClassifyAsync(["\u0001"], TestContext.Current.CancellationToken);
        Assert.Equal(AnswerBasis.None, noEmbedding[0].Basis);
        Assert.Null(noEmbedding[0].Details);
        Assert.False(noEmbedding[0].Abstained);
        Assert.Empty(noEmbedding[0].Candidates);

        var zeroNorm = await classifier.ClassifyAsync(["the", "bat"], TestContext.Current.CancellationToken);
        Assert.Equal(AnswerBasis.None, zeroNorm[1].Basis);
        Assert.Null(zeroNorm[1].Details);
        Assert.False(zeroNorm[1].Abstained);

        var calls = model.Calls;
        var derived = await classifier.ClassifyAsync(["batted", "batly"], TestContext.Current.CancellationToken);
        Assert.All(derived, a => Assert.Equal(AnswerBasis.None, a.Basis));
        Assert.Equal(calls, model.Calls);
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
    ///     gives a zero margin.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_ClassifyAsync_ThreeSenses_ReportsRunnerUpMargin()
    {
        var bat = new LemmaEntry(
            "bat",
            [new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb, "a"), new SenseDefinition(PartOfSpeech.Adjective, "b")],
            true);
        var table = new WordSenseTable([bat], new Dictionary<string, string> { ["bat"] = "bat" });
        var weights = new WordSenseWeightsTable(
            FakeWordSenseModel.Id,
            FakeWordSenseModel.Sha,
            table.Sha256,
            3,
            [0f, 0f, 0f],
            new Dictionary<string, SenseWeights>
            {
                ["bat"] = new(LemmaModelKind.OneVersusRest, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], [0f, 0f, 0f], 0.1f, 3),
            });
        using var classifier = new WordSenseClassifier(table, new FakeWordSenseModel(), weights);

        // After "the" the feature is (0.707, 0.707, 0): the first two senses tie; after "a" it is (-0.707, 0.707, 0)
        var analyses = await classifier.ClassifyAsync(["the", "bat", "a", "bat"], TestContext.Current.CancellationToken);

        var tie = analyses[1];
        Assert.True(tie.Abstained);
        Assert.Null(tie.Details);
        Assert.Equal(PartOfSpeech.Verb, tie.RunnerUp!.PartOfSpeech);
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
    ///     Disposing disposes the model only when the classifier owns it.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Dispose_OwnedModel_IsDisposed()
    {
        var owned = new FakeWordSenseModel();
        var borrowed = new FakeWordSenseModel();

        Create(owned, ownsModel: true).Dispose();
        Create(borrowed, ownsModel: false).Dispose();

        Assert.True(owned.Disposed);
        Assert.False(borrowed.Disposed);
    }

    /// <summary>
    ///     After an owned classifier is disposed, a call with a context word throws, a call without one still returns
    ///     and disposing again is harmless.
    /// </summary>
    [Fact]
    public async Task WordSenseClassifier_Dispose_OwnedModel_LaterCallsBehaveAsDocumented()
    {
        var model = new FakeWordSenseModel();
        var classifier = Create(model, ownsModel: true);
        var token = TestContext.Current.CancellationToken;

        classifier.Dispose();
        classifier.Dispose();

        Assert.True(model.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(["the", "bat"], token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync("the bat", token));
        var tokens = await classifier.ClassifyAsync("nothing to see here", token);
        Assert.All(tokens, t => Assert.Null(t.Analysis.Details));
        Assert.Equal(0, model.Calls);
    }

    /// <summary>
    ///     A lexicon-only classifier owns no model, so disposing it twice is harmless.
    /// </summary>
    [Fact]
    public void WordSenseClassifier_Dispose_LexiconOnly_IsHarmless()
    {
        var classifier = WordSenseClassifier.Create();

        classifier.Dispose();
        classifier.Dispose();

        Assert.Empty(classifier.ContextLemmas);
    }
}
