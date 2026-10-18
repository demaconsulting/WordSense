using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicClassifier"/> on a toy table and lexicon.
/// </summary>
public class BasicClassifierTests
{
    private static readonly WordDetails Cat = new(null, "cat", PartOfSpeech.Noun, string.Empty);
    private static readonly WordDetails Fish = WordLexicon.Lookup("bass").First(c => c.SenseId == "bass#fish");
    private static readonly WordDetails Music = WordLexicon.Lookup("bass").First(c => c.SenseId == "bass#music");

    private static readonly Dictionary<string, ImmutableArray<WordDetails>> Lexicon = new(StringComparer.Ordinal)
    {
        ["cat"] = [Cat],
        ["don't"] = [new(null, "don't", PartOfSpeech.Verb, string.Empty)],
        ["record"] = [new(null, "record", PartOfSpeech.Noun, string.Empty), new(null, "record", PartOfSpeech.Verb, string.Empty)],
        ["bass"] = [Fish, Music],
    };

    private static readonly BasicTables Tables = BasicTables.Load(TestTables.Default());

    private static BasicClassifier Create() => new(Tables, Find);

    private static ImmutableArray<WordDetails> Find(string word) => Lexicon.GetValueOrDefault(word);

    private static Task<WordSense[]> Run(params string[] words) =>
        Create().ClassifyAsync(words, TestContext.Current.CancellationToken);

    /// <summary>
    ///     A word that is not in the lexicon has no details and no confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails()
    {
        var results = await Run("zzqxjw");

        Assert.Equal(new WordSense("zzqxjw", null, 0), results[0]);
    }

    /// <summary>
    ///     A word with exactly one lexicon entry is answered with full confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SingleSenseWord_ReturnsEntryWithFullConfidence()
    {
        var results = await Run("The", "CAT");

        Assert.Equal(new WordSense("CAT", Cat, 1.0), results[1]);
    }

    /// <summary>
    ///     An ambiguous word is always answered and its confidence is a probability among the candidates.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AmbiguousWord_AlwaysAnswersWithProbability()
    {
        var results = await Run("record", "the", "record", "they", "record", "bass");

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
    ///     Senses with the same part of speech are told apart by the words around the target.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_SamePartOfSpeech_UsesCues()
    {
        var fish = await Run("the", "river", "bass");
        var music = await Run("the", "guitar", "bass");

        Assert.Equal("bass#fish", fish[2].Details!.SenseId);
        Assert.Equal("bass#music", music[2].Details!.SenseId);
    }

    /// <summary>
    ///     A sentence-ending token separates the text into segments that do not influence each other.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Segments_AreIndependent()
    {
        var alone = await Run("the", "guitar", "bass");
        var inText = await Run("They", "record", "the", "river", ".", "the", "guitar", "bass");

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

    /// <summary>
    ///     A form that lists a non-prefix subset of the senses of its lemma is scored over that subset only, matched
    ///     by identity: a cue that prefers a sense the form lacks does not win, and a single listed sense is answered
    ///     directly with full confidence.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NonPrefixSubset_IsScoredOverItsOwnSenses()
    {
        var bow = WordLexicon.Lookup("bow").Where(b => b.SenseId is not null).ToImmutableArray();
        Assert.Equal(3, bow.Length);
        var voice = bow[2];
        var hashes = TestTables.HeteronymHashes();
        var river = new FeatureRef(CueModel.Window, "river").Hash;
        sbyte[] riverRow = [-2, -40, -40];
        sbyte[] guitarRow = [-40, -2, -6];
        var tables = BasicTables.Load(TestTables.Build(
            [("the", 1 << PosTag.Determiner, new int[PosTag.Count])],
            [],
            [(
                "bow",
                [.. bow.Select(b => new CueSense(b.SenseId!, -8) { HasMeaning = true })],
                hashes,
                hashes[0] == river ? [.. riverRow, .. guitarRow] : [.. guitarRow, .. riverRow])]));
        var lexicon = new Dictionary<string, ImmutableArray<WordDetails>>(StringComparer.Ordinal)
        {
            ["bow"] = [bow[0], bow[1], voice],
            ["bows"] = [bow[0], voice],
            ["bowed"] = [voice],
        };
        var classifier = new BasicClassifier(tables, word => lexicon.GetValueOrDefault(word));

        var whole = await classifier.ClassifyAsync(["the", "guitar", "bow"], TestContext.Current.CancellationToken);
        var subset = await classifier.ClassifyAsync(["the", "guitar", "bows"], TestContext.Current.CancellationToken);
        var riverSubset = await classifier.ClassifyAsync(["the", "river", "bows"], TestContext.Current.CancellationToken);
        var single = await classifier.ClassifyAsync(["the", "guitar", "bowed"], TestContext.Current.CancellationToken);

        Assert.Equal(bow[1], whole[2].Details);
        Assert.Equal(voice, subset[2].Details);
        Assert.Equal(bow[0], riverSubset[2].Details);
        Assert.Equal(new WordSense("bowed", voice, 1.0), single[2]);
    }

    /// <summary>
    ///     Lookup ignores case and reads the curly apostrophe as a straight one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_MixedCaseAndCurlyApostrophe_AreNormalized()
    {
        var mixed = await Run("They", "RECORD", "Don\u2019t");
        var plain = await Run("they", "record", "don't");

        Assert.Equal(plain[1].Details, mixed[1].Details);
        Assert.Equal(plain[2].Details, mixed[2].Details);
        Assert.Equal("Don\u2019t", mixed[2].Word);
        Assert.Equal("don't", BasicClassifier.Normalize("Don\u2019t"));
    }

    /// <summary>
    ///     A token without a letter is unknown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Punctuation_IsUnknown()
    {
        var results = await Run(".", "123");

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
        Assert.Empty(await Run());
    }

    /// <summary>
    ///     The results map onto the input by index.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_ResultsAreParallelToInput()
    {
        string[] words = ["They", "record", "the", "record", "."];

        var results = await Run(words);

        Assert.Equal(words, results.Select(r => r.Word));
    }

    /// <summary>
    ///     The same text gives the same results every time, also from many threads at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_IsDeterministicAndThreadSafe()
    {
        var classifier = Create();
        string[] words = ["They", "record", "the", "river", "bass", ".", "the", "record"];
        var expected = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var runs = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => classifier.ClassifyAsync(words, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(runs, run => Assert.Equal(expected, run));
    }

    /// <summary>
    ///     A null list is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_NullWords_Throws()
    {
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
        await Assert.ThrowsAsync<ArgumentException>(() => Run("a", null!));
    }

    /// <summary>
    ///     A cancelled token is honored.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_Cancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().ClassifyAsync(["a"], cancelled.Token));
    }
}
