using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="BasicClassifier"/> on a toy table and lexicon. Only the number-word test reads the embedded
///     word lexicon.
/// </summary>
public class BasicClassifierTests
{
    private static readonly WordDetails Bank = new("bank", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails DoNot = new("don't", PartOfSpeech.Verb, "VerbForm=Fin");
    private static readonly WordDetails DeerSing = new("deer", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails DeerPlur = new("deer", PartOfSpeech.Noun, "Number=Plur");

    // The two lemmas of the spelling polish (the language, the shine) and the lemmas that only the exact spelling Jack has
    private static readonly WordDetails PolishLanguage = new("Polish", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails PolishShine = new("polish", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails PolishVerb = new("polish", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails JackName = new("Jack", PartOfSpeech.Noun, "Capitalization=Title+Number=Sing");
    private static readonly WordDetails JackTool = new("jack", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails JackLift = new("jack", PartOfSpeech.Verb, "VerbForm=Inf");

    // A name with capitalized lemmas only, a word with lower-case lemmas only, the pronoun and the capital letter I
    private static readonly WordDetails ZorroNoun = new("Zorro", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails ZorroVerb = new("Zorro", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails ParisNoun = new("Paris", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails TurkeyNoun = new("turkey", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails TurkeyVerb = new("turkey", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails IPronoun = new("i", PartOfSpeech.Pronoun, string.Empty);
    private static readonly WordDetails ILetter = new("I", PartOfSpeech.Noun, "Number=Sing");

    private static readonly SenseInfo CatSense = new("cat#feline", null, "cat", PartOfSpeech.Noun, "cat%1:05:00::");

    private static readonly Dictionary<string, ImmutableArray<WordDetails>> Lexicon = new(StringComparer.Ordinal)
    {
        ["cat"] = [TestTables.CatNoun],
        ["bank"] = [Bank],
        ["don't"] = [DoNot],
        ["record"] = [TestTables.RecordNoun, TestTables.RecordVerb],
        ["read"] = [TestTables.ReadPres, TestTables.ReadPast],
        ["tied"] = [TestTables.RecordVerb, TestTables.RecordNoun, TestTables.ReadPast],
        ["puss"] = [TestTables.CatNoun, TestTables.RecordVerb],
        ["claw"] = [TestTables.RecordVerb, TestTables.CatNoun],
        ["varied"] = [TestTables.ReadPres, TestTables.RecordNoun, TestTables.ReadPast],
        ["deer"] = [DeerSing, DeerPlur],
        ["polish"] = [PolishLanguage, PolishShine, PolishVerb],
        ["jack"] = [JackTool, JackLift],
        ["Jack"] = [JackName, JackTool, JackLift],
        ["zorro"] = [ZorroNoun, ZorroVerb],
        ["paris"] = [ParisNoun],
        ["turkey"] = [TurkeyNoun, TurkeyVerb],
        ["i"] = [IPronoun, ILetter],
    };

    private static readonly Dictionary<(string Lemma, PartOfSpeech Pos), ImmutableArray<SenseInfo>> Senses = new()
    {
        [("cat", PartOfSpeech.Noun)] = [CatSense],
        [("deer", PartOfSpeech.Noun)] = [new("deer#animal", null, "deer", PartOfSpeech.Noun, "deer%1:05:00::")],
        [("bank", PartOfSpeech.Noun)] =
        [
            new("bank#institution", "bank#money", "bank", PartOfSpeech.Noun, null),
            new("bank#building", "bank#money", "bank", PartOfSpeech.Noun, null),
            new("bank#slope", null, "bank", PartOfSpeech.Noun, null),
        ],
    };

    private static readonly BasicTables Tables = TestTables.Load(TestTables.Default());

    // The tagger has no weight and no cue table is known, so every analysis of a word has the same probability
    private static readonly BasicTables TieTables = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], hmmWeight: 0));

    private static BasicClassifier Create(BasicTables? tables = null) => new(tables ?? Tables, Find, Count, SensesOf);

    private static ImmutableArray<WordDetails> Find(string word) => Lexicon.GetValueOrDefault(word);

    private static ImmutableArray<SenseInfo> SensesOf(string lemma, PartOfSpeech pos) =>
        Senses.TryGetValue((lemma, pos), out var found) ? found : [];

    private static int Count(string lemma, PartOfSpeech pos) => SensesOf(lemma, pos).Length;

    private static Task<ClassifyResult[]> Run(params string[] words) =>
        Create().ClassifyAsync(words, TestContext.Current.CancellationToken);

    /// <summary>
    ///     A word that is not in the lexicon is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails()
    {
        var results = await Run("zzqxjw");

        Assert.Equal(ClassifyResult.Unknown("zzqxjw"), results[0]);
        Assert.False(results[0].IsKnown);
    }

    /// <summary>
    ///     A word that is in the lexicon, a number word such as <c>one</c> or <c>twenty</c> included, is answered with
    ///     details; only a token without a letter is unknown. It reads the embedded word lexicon and therefore needs the
    ///     regenerated word image.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NumberWord_ReturnsDetails()
    {
        // Arrange: the toy statistics with the lexicon of the core package
        var classifier = new BasicClassifier(Tables, key => WordLexicon.TryLookup(key, out var found) ? found : [], (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf);

        // Act
        var results = await classifier.ClassifyAsync(["one", "Twenty", "20"], TestContext.Current.CancellationToken);

        // Assert: the number words are known and have details and a level, the digit string is unknown
        Assert.All(results.Take(2), r => Assert.True(r.IsKnown));
        Assert.NotNull(results[0].Details);
        Assert.NotNull(results[1].Details);
        Assert.All(results.Take(2), r => Assert.NotEqual(SenseLevel.None, r.Level));
        Assert.All(results.Take(2), r => Assert.InRange(r.Confidence, 0.0001, 1.0));
        Assert.All(results.Take(2), r => Assert.False(r.Analyses.IsEmpty));
        Assert.Null(results[2].Details);
        Assert.Equal(SenseLevel.None, results[2].Level);
    }

    /// <summary>
    ///     A word whose only analysis has exactly one sense is answered at the sense level with full confidence,
    ///     whatever the case of the word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleSenseWord_ReturnsEntryWithFullConfidence()
    {
        var results = await Run("The", "CAT");

        var expected = new ClassifyResult(
            "CAT",
            true,
            [new AnalysisCandidate(TestTables.CatNoun, 1.0, [new SenseCandidate("cat#feline", 1.0)])],
            TestTables.CatNoun,
            SenseLevel.Sense,
            1.0,
            null,
            "cat#feline");
        Assert.Equal(expected, results[1]);
    }

    /// <summary>
    ///     A word whose lemma and part of speech have one sense is answered at the sense level with that sense and
    ///     without a group, in lower case, capitalized and upper case.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleSensePart_AnswersSenseLevel()
    {
        var results = await Run("cat", "Cat", "CAT");

        Assert.All(results, r =>
        {
            Assert.True(r.IsKnown);
            Assert.Equal(SenseLevel.Sense, r.Level);
            Assert.Equal("cat#feline", r.SenseId);
            Assert.Null(r.GroupId);
            Assert.Equal(TestTables.CatNoun, r.Details);
            Assert.Equal(1.0, r.Confidence);
        });
    }

    /// <summary>
    ///     A word with a single analysis but several senses is answered with that analysis at the part-of-speech level
    ///     and confidence 1, because the statistics cannot choose a sense; so is a word whose lemma and part of speech
    ///     have no sense in the catalog. The analysis lists no senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleAnalysisWithManySenses_AnswersItWithConfidenceOne()
    {
        var results = await Run("bank", "don't");

        Assert.Equal(new ClassifyResult("bank", true, [new AnalysisCandidate(Bank, 1.0, [])], Bank, SenseLevel.PartOfSpeech, 1.0, null, null), results[0]);
        Assert.Equal(new ClassifyResult("don't", true, [new AnalysisCandidate(DoNot, 1.0, [])], DoNot, SenseLevel.PartOfSpeech, 1.0, null, null), results[1]);
    }

    /// <summary>
    ///     An ambiguous word is answered with one of its analyses at the part-of-speech level, without a group or a sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AmbiguousWord_AnswersPartOfSpeechLevel()
    {
        var results = await Run("they", "record", "the", "record", "they", "read");

        foreach (var index in new[] { 1, 3, 5 })
        {
            Assert.True(results[index].IsKnown);
            Assert.Equal(SenseLevel.PartOfSpeech, results[index].Level);
            Assert.Null(results[index].GroupId);
            Assert.Null(results[index].SenseId);
            Assert.Contains(results[index].Details!, Lexicon[results[index].Word]);
            Assert.Equal(SlotProbability(results[index]), results[index].Confidence, 12);
        }
    }

    // The probability of the lemma and part of speech of the selected analysis: the sum over its feature variants
    private static double SlotProbability(ClassifyResult result) =>
        result.Analyses.Where(a => a.Details.Lemma == result.Details!.Lemma && a.Details.PartOfSpeech == result.Details.PartOfSpeech).Sum(a => a.Probability);

    /// <summary>
    ///     No answer is ever at the group level, whatever the word.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NeverAnswersGroupLevel()
    {
        var results = await Run("They", "record", "the", "cat", "bank", "don't", "read", ".", "zzqxjw", "123");

        Assert.DoesNotContain(results, r => r.Level == SenseLevel.Group);
        Assert.Equal(
            [SenseLevel.None, SenseLevel.PartOfSpeech, SenseLevel.None, SenseLevel.Sense, SenseLevel.PartOfSpeech, SenseLevel.PartOfSpeech, SenseLevel.PartOfSpeech, SenseLevel.None, SenseLevel.None, SenseLevel.None],
            results.Select(r => r.Level));
    }

    /// <summary>
    ///     A word without an analysis, or a token without a letter, is answered at the level none: not known, without
    ///     analyses, details, group or sense, with confidence 0.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UnknownWord_AnswersLevelNone()
    {
        var results = await Run("zzqxjw", ".", "123");

        Assert.All(results, r =>
        {
            Assert.False(r.IsKnown);
            Assert.Equal(SenseLevel.None, r.Level);
            Assert.Null(r.Details);
            Assert.Null(r.GroupId);
            Assert.Null(r.SenseId);
            Assert.Equal(0.0, r.Confidence);
        });
    }

    /// <summary>
    ///     An unknown word has no analyses (the shared empty array, never the default array).
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UnknownWord_HasNoAnalyses()
    {
        var results = await Run("zzqxjw", ".", "123");

        Assert.All(results, r =>
        {
            Assert.False(r.Analyses.IsDefault);
            Assert.True(r.Analyses.IsEmpty);
        });
    }

    /// <summary>
    ///     An ambiguous word lists every analysis with its probability, the largest first, summing to one, the selected
    ///     analysis (<c>Details</c>) first; the analyses with several senses list no senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AmbiguousWord_ListsAnalysesAndNoSenses()
    {
        var results = await Run("They", "record", "the", "record", "yesterday", "they", "read", ".", "zzqxjw");

        foreach (var index in new[] { 1, 3, 6 })
        {
            var result = results[index];
            var analyses = result.Analyses;

            // Every analysis of the lexicon is listed once, none is truncated
            Assert.Equal(Lexicon[result.Word].Length, analyses.Length);
            Assert.All(Lexicon[result.Word], details => Assert.Contains(analyses, a => a.Details == details));

            // The probabilities sum to one and are sorted, the largest first
            Assert.Equal(1.0, analyses.Sum(a => a.Probability), 9);
            Assert.Equal(analyses.OrderByDescending(a => a.Probability).Select(a => a.Probability), analyses.Select(a => a.Probability));

            // The selected analysis is the first one and the confidence is the probability of its lemma and part of speech
            Assert.Equal(analyses[0].Details, result.Details);
            Assert.Equal(SlotProbability(result), result.Confidence, 12);

            // The statistics do not score senses: record and read have no sense in the toy catalog
            Assert.All(analyses, a => Assert.True(a.Senses.IsEmpty));
        }
    }

    /// <summary>
    ///     The selected analysis is the most probable one of the analysis probabilities: the context decides, and the list is
    ///     reordered accordingly.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Context_SelectsTheMostProbableAnalysis()
    {
        var noun = await Run("the", "record");
        var verb = await Run("they", "record");

        Assert.Equal(TestTables.RecordNoun, noun[1].Analyses[0].Details);
        Assert.Equal(TestTables.RecordVerb, verb[1].Analyses[0].Details);
        Assert.Equal(noun[1].Details, noun[1].Analyses.MaxBy(a => a.Probability).Details);
        Assert.Equal(verb[1].Details, verb[1].Analyses.MaxBy(a => a.Probability).Details);
        Assert.Equal(TestTables.RecordVerb, noun[1].Analyses[1].Details);
    }

    /// <summary>
    ///     An analysis whose lemma and part of speech have exactly one sense lists that sense with probability 1, also
    ///     inside an ambiguous word; the other analyses of the word list none. When the selected analysis has the one
    ///     sense, the result is at the sense level with the probability of the analysis as the confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleSenseAnalysis_ListsThatSenseWithProbabilityOne()
    {
        var classifier = Create(TieTables);

        var results = await classifier.ClassifyAsync(["cat", "puss", "claw"], TestContext.Current.CancellationToken);

        // A word with one analysis and one sense
        Assert.Equal([new SenseCandidate("cat#feline", 1.0)], results[0].Analyses[0].Senses);

        // Every analysis has the same probability, so the lexicon order decides: the cat sense is selected
        var puss = results[1];
        Assert.Equal(2, puss.Analyses.Length);
        Assert.Equal([new SenseCandidate("cat#feline", 1.0)], puss.Analyses[0].Senses);
        Assert.True(puss.Analyses[1].Senses.IsEmpty);
        Assert.Equal(SenseLevel.Sense, puss.Level);
        Assert.Equal("cat#feline", puss.SenseId);
        Assert.Null(puss.GroupId);
        Assert.Equal(0.5, puss.Confidence, 9);

        // The verb is selected: the part-of-speech level, although the second analysis lists its one sense
        var claw = results[2];
        Assert.True(claw.Analyses[0].Senses.IsEmpty);
        Assert.Equal([new SenseCandidate("cat#feline", 1.0)], claw.Analyses[1].Senses);
        Assert.Equal(SenseLevel.PartOfSpeech, claw.Level);
        Assert.Null(claw.SenseId);
        Assert.Equal(0.5, claw.Confidence, 9);
    }

    /// <summary>
    ///     The analyses array of a word with a single analysis is built once and reused for the next result for the word
    ///     (a cache hit), also in different case; this is an optimization and not part of the contract, so the words
    ///     are classified one after the other and no other word is in between that could evict the entry.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleAnalysisWord_SharesItsAnalysesArray()
    {
        var results = await Run("cat", "Cat", "bank", "bank", "zzqxjw", "zzqxjw");

        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(results[0].Analyses),
            ImmutableCollectionsMarshal.AsArray(results[1].Analyses));
        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(results[2].Analyses),
            ImmutableCollectionsMarshal.AsArray(results[3].Analyses));
        Assert.NotSame(
            ImmutableCollectionsMarshal.AsArray(results[0].Analyses),
            ImmutableCollectionsMarshal.AsArray(results[2].Analyses));
        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(results[4].Analyses),
            ImmutableCollectionsMarshal.AsArray(results[5].Analyses));
        Assert.Equal("Cat", results[1].Word);
    }

    /// <summary>
    ///     Analyses with the same probability keep the order of the lexicon, also when the order is not the order of the
    ///     analyses of the parts of speech.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_TiesKeepLexiconOrder()
    {
        var classifier = Create(TieTables);

        var results = await classifier.ClassifyAsync(["tied", "puss", "claw"], TestContext.Current.CancellationToken);

        Assert.Equal(Lexicon["tied"], results[0].Analyses.Select(a => a.Details));
        Assert.All(results[0].Analyses, a => Assert.Equal(1.0 / 3.0, a.Probability, 9));
        Assert.Equal(TestTables.RecordVerb, results[0].Details);
        Assert.Equal(Lexicon["puss"], results[1].Analyses.Select(a => a.Details));
        Assert.Equal(Lexicon["claw"], results[2].Analyses.Select(a => a.Details));
        Assert.Equal(TestTables.CatNoun, results[1].Details);
        Assert.Equal(TestTables.RecordVerb, results[2].Details);
    }

    /// <summary>
    ///     The analyses of one lemma and part of speech that differ only in features share the probability of their
    ///     slot: they never make the slot more probable than another one, are listed together with their slot, and the
    ///     confidence of the answer is the probability of the slot, not of one variant.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_VariantsOfASlot_ShareItsProbabilityAndStayTogether()
    {
        // Arrange: no tagger weight and no cue table, so the context decides nothing; "varied" has the two variants of
        // the verb read around the one analysis of the noun record
        var classifier = Create(TieTables);

        // Act
        var results = await classifier.ClassifyAsync(["varied"], TestContext.Current.CancellationToken);

        // Assert: two slots of probability 1/2, the variants of the verb 1/4 each (not three times 1/3), listed together
        // in the order of the lexicon, the first slot first
        var result = results[0];
        Assert.Equal([TestTables.ReadPres, TestTables.ReadPast, TestTables.RecordNoun], result.Analyses.Select(a => a.Details));
        Assert.Equal(0.25, result.Analyses[0].Probability, 9);
        Assert.Equal(0.25, result.Analyses[1].Probability, 9);
        Assert.Equal(0.5, result.Analyses[2].Probability, 9);
        Assert.Equal(TestTables.ReadPres, result.Details);
        Assert.Equal(SenseLevel.PartOfSpeech, result.Level);
        Assert.Equal(0.5, result.Confidence, 9);
    }

    /// <summary>
    ///     A word whose analyses all share a lemma and a part of speech is certain about it: the variants split the
    ///     probability 1 and the confidence is 1, at the sense level when the lemma and part of speech have one sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_VariantsOfTheOnlySlot_AreAnsweredWithConfidenceOne()
    {
        // Arrange: "deer" is a singular and a plural of one noun with one sense
        var classifier = Create(TieTables);

        // Act
        var results = await classifier.ClassifyAsync(["deer", "read"], TestContext.Current.CancellationToken);

        // Assert: both variants have 1/2, and the answer has the whole probability of the slot
        Assert.All(results[0].Analyses, a => Assert.Equal(0.5, a.Probability, 9));
        Assert.Equal(SenseLevel.Sense, results[0].Level);
        Assert.Equal("deer#animal", results[0].SenseId);
        Assert.Equal(1.0, results[0].Confidence, 9);
        Assert.Equal(SenseLevel.PartOfSpeech, results[1].Level);
        Assert.Equal(1.0, results[1].Confidence, 9);
    }

    /// <summary>
    ///     An ambiguous word is always answered and its confidence is a probability among the candidates.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AmbiguousWord_AlwaysAnswersWithProbability()
    {
        var results = await Run("record", "the", "record", "they", "record", "read");

        foreach (var index in new[] { 0, 2, 4, 5 })
        {
            Assert.NotNull(results[index].Details);
            Assert.InRange(results[index].Confidence, 0.5, 1.0);
        }
    }

    /// <summary>
    ///     The sentence context decides between a noun and a verb.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Context_ChoosesPartOfSpeech()
    {
        var noun = await Run("the", "record");
        var verb = await Run("they", "record");

        Assert.Equal(PartOfSpeech.Noun, noun[1].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Verb, verb[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     Analyses with the same part of speech, the past and the present of <c>read</c>, are told apart by the words
    ///     around the target.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SamePartOfSpeechAnalyses_UsesCues()
    {
        var past = await Run("yesterday", "they", "read");
        var present = await Run("they", "read", "tomorrow");

        Assert.Equal(TestTables.ReadPast, past[2].Details);
        Assert.Equal(TestTables.ReadPres, present[1].Details);
    }

    /// <summary>
    ///     The cue table of a form that spans lemmas is used before the table of the lemma of its first analysis: the
    ///     cues choose between the lemmas of <c>left</c>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_FormThatSpansLemmas_UsesTheCueTableOfTheForm()
    {
        // Arrange: a table keyed by the form "left" with a column per lemma, and no weight for the tagger
        var hashes = TestTables.CueHashes();
        var yesterday = new FeatureRef(CueModel.Window, "yesterday").Hash;
        CueAnalysis[] columns = [new("leave", PartOfSpeech.Verb, "Tense=Past", -8), new("left", PartOfSpeech.Adjective, string.Empty, -8)];
        sbyte[] yesterdayRow = [-2, -40];
        sbyte[] tomorrowRow = [-40, -2];
        sbyte[] weights = hashes[0] == yesterday ? [.. yesterdayRow, .. tomorrowRow] : [.. tomorrowRow, .. yesterdayRow];
        var tables = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("left", columns, hashes, weights)], hmmWeight: 0));
        var lexicon = new Dictionary<string, ImmutableArray<WordDetails>>(StringComparer.Ordinal)
        {
            ["left"] = [TestTables.LeftAdjective, TestTables.LeavePast],
        };
        var classifier = new BasicClassifier(tables, word => lexicon.GetValueOrDefault(word), Count, SensesOf);

        // Act
        var verb = await classifier.ClassifyAsync(["yesterday", "they", "left"], TestContext.Current.CancellationToken);
        var adjective = await classifier.ClassifyAsync(["tomorrow", "the", "left"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TestTables.LeavePast, verb[2].Details);
        Assert.Equal(TestTables.LeftAdjective, adjective[2].Details);
    }

    /// <summary>
    ///     A sentence-ending token separates the text into segments that do not influence each other.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Segments_AreIndependent()
    {
        var alone = await Run("yesterday", "they", "read");
        var inText = await Run("They", "record", "the", "tomorrow", ".", "yesterday", "they", "read");

        Assert.Equal(alone, inText[5..]);
    }

    /// <summary>
    ///     The segment of a position ends after a terminator and starts after the previous one.
    /// </summary>
    [Fact]
    public void BasicClassifier_SegmentOf_FindsBounds()
    {
        string[] words = ["a", ".", "b", "c", "!", "d"];

        Assert.Equal((2, 3), BasicClassifier.SegmentOf(words, 3));
        Assert.Equal((0, 2), BasicClassifier.SegmentOf(words, 1));
        Assert.Equal((5, 1), BasicClassifier.SegmentOf(words, 5));
    }

    // The probability of one analysis in the result; with the tie table every analysis of a word starts at 1/(number of
    // slots), so the written case alone moves it
    private static double ProbabilityOf(ClassifyResult result, WordDetails details) =>
        result.Analyses.Single(a => a.Details == details).Probability;

    // Every analysis of the toy word polish has the probability 1/3 when the written case says nothing
    private static void AssertCaseNeutral(ClassifyResult polish)
    {
        Assert.Equal(PolishLanguage, polish.Details);
        Assert.Equal(3, polish.Analyses.Length);
        Assert.All(polish.Analyses, a => Assert.Equal(1.0 / 3.0, a.Probability, 9));
    }

    private static Task<ClassifyResult[]> RunNeutral(params string[] words) =>
        Create(TieTables).ClassifyAsync(words, TestContext.Current.CancellationToken);

    /// <summary>
    ///     A lower-case word inside a sentence weighs the analyses of a capitalized lemma down to a tenth, so the
    ///     lower-case lemma is selected although the lexicon lists the capitalized one first.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_LowerCaseWordInsideSentence_PrefersTheLowerCaseLemma()
    {
        // Arrange: the tie table gives the three analyses of polish 1/3 each without the case

        // Act
        var results = await RunNeutral("The", "shop", "sells", "silver", "polish", ".");

        // Assert: the language has a tenth of the weight of each of the other two
        var polish = results[4];
        Assert.Equal(PolishShine, polish.Details);
        Assert.Equal(0.1 / 2.1, ProbabilityOf(polish, PolishLanguage), 9);
        Assert.Equal(1.0 / 2.1, ProbabilityOf(polish, PolishShine), 9);
        Assert.Equal(1.0 / 2.1, ProbabilityOf(polish, PolishVerb), 9);
        Assert.Equal(1.0, polish.Analyses.Sum(a => a.Probability), 9);
    }

    /// <summary>
    ///     After the case weights are applied the analyses are listed as always: the most probable first and equal probabilities in the
    ///     order of the lexicon.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_LowerCaseWordInsideSentence_ListsTiedAnalysesInLexiconOrder()
    {
        // Arrange: the lexicon order of polish is the language, the shine, the verb

        // Act
        var results = await RunNeutral("The", "shop", "sells", "silver", "polish", ".");

        // Assert: the shine and the verb tie at 1/2.1 and keep the lexicon order, the language (0.1/2.1) comes last
        Assert.Equal([PolishShine, PolishVerb, PolishLanguage], results[4].Analyses.Select(a => a.Details));
    }

    /// <summary>
    ///     A lower-case word is evidence also at the start of a segment: a proper name is not written without its capital.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_LowerCaseWordAtTheStart_PrefersTheLowerCaseLemma()
    {
        // Arrange: the lower-case word is the first word of the segment

        // Act
        var results = await RunNeutral("polish", "the", "silver", ".");

        // Assert
        Assert.Equal(PolishShine, results[0].Details);
        Assert.Equal(0.1 / 2.1, ProbabilityOf(results[0], PolishLanguage), 9);
    }

    /// <summary>
    ///     An isolated capitalized word inside a sentence weighs the analyses of a lower-case lemma down to a tenth, so
    ///     the capitalized lemma is selected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordInsideSentence_PrefersTheCapitalizedLemma()
    {
        // Arrange: Polish is not the first word and its neighbors are lower-case words

        // Act
        var results = await RunNeutral("My", "neighbor", "speaks", "fluent", "Polish", ".");

        // Assert: the lower-case lemmas have a tenth of the weight of the language
        var polish = results[4];
        Assert.Equal(PolishLanguage, polish.Details);
        Assert.Equal(1.0 / 1.2, ProbabilityOf(polish, PolishLanguage), 9);
        Assert.Equal(0.1 / 1.2, ProbabilityOf(polish, PolishShine), 9);
        Assert.Equal(0.1 / 1.2, ProbabilityOf(polish, PolishVerb), 9);
    }

    /// <summary>
    ///     A punctuation token next to a capitalized word is not a capitalized neighbor, so the word stays isolated.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordNextToPunctuation_PrefersTheCapitalizedLemma()
    {
        // Arrange: commas on both sides of the capitalized word

        // Act
        var results = await RunNeutral("He", "spoke", ",", "Polish", ",", "too", ".");

        // Assert
        Assert.Equal(1.0 / 1.2, ProbabilityOf(results[3], PolishLanguage), 9);
    }

    /// <summary>
    ///     A capitalized word is looked up in lower case like every word: a capitalized lemma that only the exact spelling
    ///     has (<c>Jack</c>, <c>State</c>) is not a candidate, because a capitalized common word (<c>each State</c>) would
    ///     then lose its common lemma; the lemmas that the lower-case spelling carries (<c>Polish</c>) are the candidates.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordInsideSentence_IsLookedUpInLowerCase()
    {
        // Arrange: the toy lexicon has the capitalized lemma Jack for the exact spelling Jack only

        // Act
        var name = await RunNeutral("We", "met", "Jack", ".");
        var tool = await RunNeutral("We", "met", "jack", ".");

        // Assert: Jack has the analyses of jack and no capitalized lemma
        Assert.Equal(JackTool, name[2].Details);
        Assert.DoesNotContain(name[2].Analyses, a => a.Details == JackName);
        Assert.Equal(tool[2].Analyses, name[2].Analyses);
    }

    /// <summary>
    ///     A capital letter on the first word of the text says nothing about the lemma: the result is the same as without
    ///     the case and the lexicon order decides the tie.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SentenceInitialCapital_AtTheStartOfTheText_CarriesNoCaseEvidence()
    {
        // Arrange: Polish is the first word

        // Act
        var results = await RunNeutral("Polish", "is", "hard", ".");

        // Assert
        AssertCaseNeutral(results[0]);
    }

    /// <summary>
    ///     A quotation mark before the first word does not make it a later word: its capital is still the capital of a
    ///     sentence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SentenceInitialCapital_AfterQuotationMark_CarriesNoCaseEvidence()
    {
        // Arrange: a quotation mark comes first

        // Act
        var results = await RunNeutral("\"", "Polish", "is", "hard", ".", "\"");

        // Assert
        AssertCaseNeutral(results[1]);
    }

    /// <summary>
    ///     The first word of a later segment is a sentence start too.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SentenceInitialCapital_InLaterSegment_CarriesNoCaseEvidence()
    {
        // Arrange: a sentence-ending token comes before the word

        // Act
        var results = await RunNeutral("It", "ended", ".", "Polish", "is", "hard", ".");

        // Assert
        AssertCaseNeutral(results[3]);
    }

    /// <summary>
    ///     The capital of the first word of a sentence does not make a run: <c>Polish</c> after the sentence-initial
    ///     <c>The</c> is isolated and takes the evidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SentenceInitialWord_DoesNotMakeARun()
    {
        // Arrange: the capitalized neighbor before Polish is the first word of the segment

        // Act
        var results = await RunNeutral("The", "Polish", "delegation", "left", ".");

        // Assert
        Assert.Equal(PolishLanguage, results[1].Details);
        Assert.Equal(1.0 / 1.2, ProbabilityOf(results[1], PolishLanguage), 9);
    }

    /// <summary>
    ///     The first word is the first word with a letter, so a quotation mark before it does not turn it into a
    ///     neighbor that counts.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SentenceInitialWordAfterQuotationMark_DoesNotMakeARun()
    {
        // Arrange: a quotation mark, then the first word, then the capitalized word

        // Act
        var results = await RunNeutral("\"", "The", "Polish", "delegation", "left", ".", "\"");

        // Assert
        Assert.Equal(1.0 / 1.2, ProbabilityOf(results[2], PolishLanguage), 9);
    }

    /// <summary>
    ///     A capitalized word followed by another capitalized word (not the first word of the segment) is part of a name or
    ///     a title (<c>Polish Prime Minister</c>) and carries no evidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordBeforeCapitalizedWord_CarriesNoCaseEvidence()
    {
        // Arrange: Prime follows Polish

        // Act
        var results = await RunNeutral("He", "likes", "Polish", "Prime", "Minister", ".");

        // Assert
        AssertCaseNeutral(results[2]);
    }

    /// <summary>
    ///     A capitalized word that follows another capitalized word carries no evidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordAfterCapitalizedWord_CarriesNoCaseEvidence()
    {
        // Arrange: Prime precedes Polish

        // Act
        var results = await RunNeutral("He", "likes", "Prime", "Polish", "Soup", ".");

        // Assert
        AssertCaseNeutral(results[3]);
    }

    /// <summary>
    ///     An upper-case neighbor counts as part of a run, as a capitalized one does.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordNextToUpperCaseWord_CarriesNoCaseEvidence()
    {
        // Arrange: USA follows Polish

        // Act
        var results = await RunNeutral("He", "likes", "Polish", "USA", "soup", ".");

        // Assert
        AssertCaseNeutral(results[2]);
    }

    /// <summary>
    ///     A mixed-case neighbor counts as part of a run too.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordNextToMixedCaseWord_CarriesNoCaseEvidence()
    {
        // Arrange: iPhone follows Polish

        // Act
        var results = await RunNeutral("He", "likes", "Polish", "iPhone", "soup", ".");

        // Assert
        AssertCaseNeutral(results[2]);
    }

    /// <summary>
    ///     A word in upper case says nothing about the lemma: it is emphasis or an acronym.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AllCapsWord_CarriesNoCaseEvidence()
    {
        // Arrange: POLISH is not the first word and its neighbors are lower-case words

        // Act
        var results = await RunNeutral("He", "likes", "POLISH", "today", ".");

        // Assert
        AssertCaseNeutral(results[2]);
    }

    /// <summary>
    ///     The capital letter <c>I</c> is spelled that way in every sentence, so it never selects a capitalized lemma over
    ///     the pronoun.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleCapitalLetter_CarriesNoCaseEvidence()
    {
        // Arrange: the toy word i has the pronoun i and the letter I

        // Act
        var results = await RunNeutral("Then", "I", "left", ".");

        // Assert
        Assert.All(results[1].Analyses, a => Assert.Equal(0.5, a.Probability, 9));
        Assert.Equal(IPronoun, results[1].Details);
    }

    /// <summary>
    ///     A word with upper-case letters in the middle (<c>pOlIsh</c>) is neither lower case nor capitalized and carries no
    ///     evidence: its analyses keep the probabilities of the case-neutral result.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_MixedCaseWord_CarriesNoCaseEvidence()
    {
        // Arrange: pOlIsh is not the first word and its neighbors are lower-case words

        // Act
        var results = await RunNeutral("He", "likes", "pOlIsh", "today", ".");

        // Assert
        AssertCaseNeutral(results[2]);
    }

    /// <summary>
    ///     A segment written in title case (every word capitalized except the small words of a title) carries no case
    ///     evidence for any of its words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_TitleCaseSegment_CarriesNoCaseEvidence()
    {
        // Arrange: every word is capitalized and in is a small word of a title

        // Act
        var results = await RunNeutral("Silver", "Polish", "in", "Small", "Tins");

        // Assert
        AssertCaseNeutral(results[1]);
    }

    /// <summary>
    ///     The same words with a lower-case content word are a sentence, not a title, and the capitalized word takes the
    ///     evidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SegmentWithLowerCaseContentWord_IsNotTitleCase()
    {
        // Arrange: small and tins are lower-case content words

        // Act
        var results = await RunNeutral("Silver", "Polish", "in", "small", "tins");

        // Assert
        Assert.Equal(1.0 / 1.2, ProbabilityOf(results[1], PolishLanguage), 9);
    }

    /// <summary>
    ///     A word whose lemmas are all capitalized keeps every analysis when it is written in lower case: the case only
    ///     separates lemmas that differ in case, so a name typed in lower case does not lose its analyses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_OnlyCapitalizedLemmas_KeepEveryAnalysisInLowerCase()
    {
        // Arrange: the toy word zorro has two analyses, both with the capitalized lemma Zorro

        // Act
        var results = await RunNeutral("We", "met", "zorro", ".");

        // Assert
        Assert.Equal(2, results[2].Analyses.Length);
        Assert.All(results[2].Analyses, a => Assert.Equal(0.5, a.Probability, 9));
    }

    /// <summary>
    ///     A name with a single capitalized analysis stays known and keeps it, probability 1, written in lower case.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_OnlyCapitalizedLemma_StaysKnownInLowerCase()
    {
        // Arrange: the toy word paris has the single analysis Paris

        // Act
        var results = await RunNeutral("We", "flew", "to", "paris", ".");

        // Assert
        Assert.True(results[3].IsKnown);
        Assert.Equal(ParisNoun, results[3].Details);
        Assert.Equal(1.0, results[3].Confidence, 9);
    }

    /// <summary>
    ///     A capitalized word with no capitalized lemma (the lexicon has the lower-case lemma only) is answered as its
    ///     lower-case form is, without any change of the probabilities.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordWithoutCapitalizedLemma_IsAnsweredLikeLowerCase()
    {
        // Arrange: the toy word turkey has lower-case lemmas only

        // Act
        var capitalized = await RunNeutral("We", "flew", "to", "Turkey", ".");
        var lower = await RunNeutral("We", "flew", "to", "turkey", ".");

        // Assert
        Assert.Equal(lower[3].Analyses, capitalized[3].Analyses);
        Assert.All(capitalized[3].Analyses, a => Assert.Equal(0.5, a.Probability, 9));
    }

    /// <summary>
    ///     A capitalized word that the lexicon does not have in any case is unknown, whatever the case.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CapitalizedWordNotInLexicon_IsUnknown()
    {
        // Arrange: Zzqxjw is not in the toy lexicon in any case

        // Act
        var results = await RunNeutral("We", "met", "Zzqxjw", ".");

        // Assert
        Assert.Equal(ClassifyResult.Unknown("Zzqxjw"), results[2]);
    }

    /// <summary>
    ///     With the embedded lexicon, the written case selects the lemma of the month and of the language against the common
    ///     word with the same spelling, a capitalized common word in a run (<c>State Department</c>) and a lower-case
    ///     modal verb keep their common lemma, and no other evidence is needed (the tie table has
    ///     no tagger weight and no cue). It reads the embedded word lexicon and therefore needs the regenerated word image.
    /// </summary>
    /// <param name="sentence">The words of the sentence, separated by spaces.</param>
    /// <param name="index">The index of the word that is checked.</param>
    /// <param name="lemma">The lemma that is selected.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("We usually take our vacation in August .", 6, "August")]
    [InlineData("The old professor had an august presence .", 5, "august")]
    [InlineData("My neighbor speaks fluent Polish .", 4, "Polish")]
    [InlineData("The shop sells silver polish .", 4, "polish")]
    [InlineData("We may leave .", 1, "may")]
    [InlineData("He works for the State Department .", 4, "state")]
    public async Task BasicClassifier_ClassifyAsync_EmbeddedLexicon_SelectsTheLemmaOfTheWrittenCase(string sentence, int index, string lemma)
    {
        // Arrange: the tie table and the lexicon of the core package
        var classifier = new BasicClassifier(TieTables, key => WordLexicon.TryLookup(key, out var found) ? found : [], (l, pos) => WordLexicon.Current.SenseCountOf(l, pos), WordLexicon.SensesOf);

        // Act
        var results = await classifier.ClassifyAsync(sentence.Split(' '), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(lemma, results[index].Details!.Lemma);
    }

    /// <summary>
    ///     Lookup ignores case and reads the curly apostrophe as a straight one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_MixedCaseAndCurlyApostrophe_AreNormalized()
    {
        // Arrange: the same words in mixed case with a curly apostrophe, and in plain form
        var mixed = await Run("They", "RECORD", "Don\u2019t");
        var plain = await Run("they", "record", "don't");

        // Act: the normalization of the word with the curly apostrophe
        var key = BasicClassifier.Normalize("Don\u2019t");

        // Assert
        Assert.Equal(plain[1].Details, mixed[1].Details);
        Assert.Equal(plain[2].Details, mixed[2].Details);
        Assert.Equal("Don\u2019t", mixed[2].Word);
        Assert.Equal("don't", key);
    }

    /// <summary>
    ///     A token without a letter is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Punctuation_IsUnknown()
    {
        // Arrange: a punctuation token and a digit string

        // Act
        var results = await Run(".", "123");

        // Assert
        Assert.All(results, r => Assert.Null(r.Details));
        Assert.All(results, r => Assert.Equal(0.0, r.Confidence));
        Assert.Equal(string.Empty, BasicClassifier.Normalize("..."));
    }

    /// <summary>
    ///     An empty list gives an empty array.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_EmptyList_ReturnsEmpty()
    {
        // Arrange: no words

        // Act
        var results = await Run();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    ///     The results map onto the input by index.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_ResultsAreParallelToInput()
    {
        // Arrange
        string[] words = ["They", "record", "the", "record", "."];

        // Act
        var results = await Run(words);

        // Assert
        Assert.Equal(words, results.Select(r => r.Word));
    }

    /// <summary>
    ///     The same text gives the same results every time, also from many threads at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_IsDeterministicAndThreadSafe()
    {
        // Arrange: one classifier, one text and the result of a first call
        var classifier = Create();
        string[] words = ["They", "record", "the", "tomorrow", "read", ".", "the", "record"];
        var expected = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        // Act: sixteen concurrent calls on the same classifier
        var runs = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => classifier.ClassifyAsync(words, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        // Assert
        Assert.All(runs, run => Assert.Equal(expected, run));
    }

    /// <summary>
    ///     A null list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NullWords_Throws()
    {
        // Arrange: a classifier and a null list

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Create().ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null element is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NullElement_Throws()
    {
        // Arrange: a list with a null element

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentException>(() => Run("a", null!));
    }

    /// <summary>
    ///     A cancelled token is honored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Cancelled_Throws()
    {
        // Arrange: a token that is already cancelled
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act and Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().ClassifyAsync(["a"], cancelled.Token));
    }

    /// <summary>
    ///     A cancellation that happens while a lazy sequence is materialized is honored before any word is classified.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_CancelledWhileReadingWords_Throws()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        IEnumerable<string> Lazy()
        {
            yield return "the";
            source.Cancel();
            yield return "cat";
        }

        // Act and Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().ClassifyAsync(Lazy(), source.Token));
    }
}
