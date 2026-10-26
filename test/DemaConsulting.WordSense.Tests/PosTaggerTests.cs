namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="PosTagger"/> on a toy table.
/// </summary>
public class PosTaggerTests
{
    private const int Open = (1 << PosTag.Noun) | (1 << PosTag.Verb);

    private static double[] Run(TaggerToken[] sentence)
    {
        var posteriors = new double[sentence.Length * PosTag.Count];
        PosTagger.Posteriors(TestTables.Load(TestTables.Default()), sentence, posteriors);
        return posteriors;
    }

    private static double At(double[] posteriors, int position, int tag) => posteriors[(position * PosTag.Count) + tag];

    /// <summary>
    ///     An unknown word that is capitalized inside the sentence is a noun; at the sentence start it is not forced.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_CapitalizedUnknownWord_IsNounInsideSentenceOnly()
    {
        var inside = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("zorped", 0, true)]);
        var start = Run([TaggerToken.Word("zorped", 0, true), TaggerToken.Word("they", 0, false)]);

        Assert.Equal(1.0, At(inside, 1, PosTag.Noun), 9);
        Assert.True(At(start, 0, PosTag.Noun) < 1.0);
    }

    /// <summary>
    ///     A word after a determiner is a noun when the word could be either.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_AfterDeterminer_PrefersNoun()
    {
        var posteriors = Run([TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)]);

        Assert.True(At(posteriors, 1, PosTag.Noun) > 0.5);
        Assert.True(At(posteriors, 1, PosTag.Noun) > At(posteriors, 1, PosTag.Verb));
    }

    /// <summary>
    ///     A word after a pronoun is a verb when the word could be either.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_AfterPronoun_PrefersVerb()
    {
        var posteriors = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false)]);

        Assert.True(At(posteriors, 1, PosTag.Verb) > 0.5);
    }

    /// <summary>
    ///     Every row sums to one and only the possible tags have probability.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_RowsSumToOne()
    {
        var posteriors = Run([TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false), TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)]);

        for (var i = 0; i < 4; i++)
        {
            var row = Enumerable.Range(0, PosTag.Count).Select(t => At(posteriors, i, t)).ToArray();
            Assert.Equal(1.0, row.Sum(), 9);
            Assert.All(row, p => Assert.InRange(p, 0.0, 1.0));
        }

        Assert.Equal(0.0, At(posteriors, 0, PosTag.Noun));
    }

    /// <summary>
    ///     A token with a single possible tag has the posterior 1 for it.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_SingleTag_IsCertain()
    {
        var posteriors = Run([TaggerToken.Word("the", 0, false), TaggerToken.Fixed(PosTag.Punctuation)]);

        Assert.Equal(1.0, At(posteriors, 0, PosTag.Determiner), 9);
        Assert.Equal(1.0, At(posteriors, 1, PosTag.Punctuation), 9);
    }

    /// <summary>
    ///     An unknown capitalized word inside a sentence is a noun; at the start its suffix decides.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_UnknownWords_UseCapitalizationAndSuffix()
    {
        var inside = Run([TaggerToken.Word("the", 0, false), TaggerToken.Word("zed", 0, true)]);
        var suffix = Run([TaggerToken.Word("walked", 0, false)]);

        Assert.Equal(1.0, At(inside, 1, PosTag.Noun), 9);
        Assert.True(At(suffix, 0, PosTag.Verb) > At(suffix, 0, PosTag.Noun));
        Assert.Equal(0.0, At(suffix, 0, PosTag.Determiner));
    }

    /// <summary>
    ///     An empty sentence needs no result space; a buffer that is too small is rejected.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_EmptyAndSmallBuffer()
    {
        var tables = TestTables.Load(TestTables.Default());

        PosTagger.Posteriors(tables, [], []);

        Assert.Throws<ArgumentException>(() => PosTagger.Posteriors(tables, [TaggerToken.Fixed(PosTag.Other)], new double[PosTag.Count - 1]));
    }

    /// <summary>
    ///     The same input gives bit-identical posteriors.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_IsDeterministic()
    {
        TaggerToken[] sentence = [TaggerToken.Word("they", 0, false), TaggerToken.Word("record", Open, false), TaggerToken.Word("the", 0, false), TaggerToken.Word("record", Open, false)];

        Assert.Equal(Run(sentence), Run(sentence));
    }

    /// <summary>
    ///     Runs the tagger on a toy table whose counts come from the given training sentences, with the closed-class
    ///     words <c>the</c>, <c>they</c> and <c>and</c> and no clause smoothing, so that the counts decide.
    /// </summary>
    /// <param name="sentences">The tag sequences of the training sentences.</param>
    /// <param name="sentence">The tagger tokens to tag.</param>
    /// <returns>The posteriors, one row of <see cref="PosTag.Count"/> values for each token.</returns>
    private static double[] RunOn(int[][] sentences, TaggerToken[] sentence)
    {
        var tables = TestTables.Load(TestTables.Build(
            [("the", 1 << PosTag.Determiner, Counts((PosTag.Determiner, 40))), ("they", 1 << PosTag.Pronoun, Counts((PosTag.Pronoun, 40))), ("and", 1 << PosTag.Conjunction, Counts((PosTag.Conjunction, 40)))],
            [],
            [],
            sentences: sentences,
            clauseSmoothing: 0));
        var posteriors = new double[sentence.Length * PosTag.Count];
        PosTagger.Posteriors(tables, sentence, posteriors);
        return posteriors;
    }

    /// <summary>
    ///     Makes the per-tag counts of a toy word.
    /// </summary>
    /// <param name="values">The tags with their counts.</param>
    /// <returns>The counts, 0 for every other tag.</returns>
    private static int[] Counts(params (int Tag, int Count)[] values)
    {
        var counts = new int[PosTag.Count];
        foreach (var (tag, count) in values)
        {
            counts[tag] = count;
        }

        return counts;
    }

    /// <summary>
    ///     Repeats a training sentence.
    /// </summary>
    /// <param name="times">The number of copies.</param>
    /// <param name="sentence">The tag sequence.</param>
    /// <returns>The copies.</returns>
    private static int[][] Repeat(int times, params int[] sentence) => [.. Enumerable.Repeat(sentence, times)];

    /// <summary>
    ///     Makes the tagger tokens of a toy sentence: <c>the</c>, <c>they</c> and <c>and</c> are closed-class words, <c>go</c>
    ///     is a verb, <c>noun</c> a noun, a comma is punctuation and any other word is open.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <returns>The tokens.</returns>
    private static TaggerToken[] ClauseSentence(params string[] words) =>
        [.. words.Select(w => w switch
        {
            "the" or "they" or "and" => TaggerToken.Word(w, 0, false),
            "go" => TaggerToken.Word(w, 1 << PosTag.Verb, false),
            "noun" => TaggerToken.Word(w, 1 << PosTag.Noun, false),
            "," => TaggerToken.Fixed(PosTag.Punctuation),
            _ => TaggerToken.Word(w, Open, false),
        })];

    /// <summary>
    ///     A word that follows a determiner and a noun is a verb when the clause has no verb yet and a noun when it has
    ///     one: the transitions are counted per clause state.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_ClauseWithVerb_PrefersNounAfterDeterminerAndNoun()
    {
        // Arrange: a verb follows "D N" only in a clause that has no verb yet
        int[][] training =
        [
            .. Repeat(6, PosTag.Determiner, PosTag.Noun, PosTag.Verb),
            .. Repeat(6, PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun),
        ];

        // Act
        var first = RunOn(training, ClauseSentence("the", "noun", "x"));
        var later = RunOn(training, ClauseSentence("they", "go", "the", "noun", "x"));

        // Assert
        Assert.True(At(first, 2, PosTag.Verb) > 0.5);
        Assert.True(At(later, 4, PosTag.Noun) > 0.5);
        Assert.True(At(first, 2, PosTag.Verb) > At(later, 4, PosTag.Verb) + 0.3);
    }

    /// <summary>
    ///     A comma or a conjunction starts a new clause, so the word after a determiner and a noun is a verb again.
    /// </summary>
    [Theory]
    [InlineData(",")]
    [InlineData("and")]
    public void PosTagger_Posteriors_ClauseBreak_RestartsTheClause(string breaker)
    {
        // Arrange
        int[][] training =
        [
            .. Repeat(6, PosTag.Determiner, PosTag.Noun, PosTag.Verb),
            .. Repeat(6, PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun),
            .. Repeat(6, PosTag.Pronoun, PosTag.Verb, PosTag.Punctuation, PosTag.Determiner, PosTag.Noun, PosTag.Verb),
            .. Repeat(6, PosTag.Pronoun, PosTag.Verb, PosTag.Conjunction, PosTag.Determiner, PosTag.Noun, PosTag.Verb),
        ];

        // Act
        var posteriors = RunOn(training, ClauseSentence("they", "go", breaker, "the", "noun", "x"));

        // Assert
        Assert.True(At(posteriors, 5, PosTag.Verb) > 0.5);
    }

    /// <summary>
    ///     A first token that is listed only as a non-finite verb (a gerund such as <c>Resolving</c>) does not give its
    ///     clause a verb, so the word after a determiner and a noun can still be the verb of the clause, while a finite
    ///     verb at the start suppresses it.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_NonFiniteStart_DoesNotSetTheClauseState()
    {
        // Arrange: a verb follows "D N" only in a clause without a verb
        int[][] training =
        [
            .. Repeat(6, PosTag.Determiner, PosTag.Noun, PosTag.Verb),
            .. Repeat(6, PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun),
            .. Repeat(6, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun),
        ];
        TaggerToken Start(bool nonFinite) => TaggerToken.Word("go", 1 << PosTag.Verb, false, nonFinite);
        TaggerToken[] Sentence(bool nonFinite) => [Start(nonFinite), TaggerToken.Word("the", 0, false), TaggerToken.Word("noun", 1 << PosTag.Noun, false), TaggerToken.Word("x", Open, false)];

        // Act
        var finite = RunOn(training, Sentence(false));
        var gerund = RunOn(training, Sentence(true));

        // Assert
        Assert.True(At(finite, 3, PosTag.Noun) > 0.5);
        Assert.True(At(gerund, 3, PosTag.Verb) > 0.5);
    }

    /// <summary>
    ///     A possessive that is one token is read as the stem and the possessive marker, the pattern that the counts
    ///     contain: the word after it is read as after a noun and the marker.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_Possessive_IsReadAsStemAndMarker()
    {
        // Arrange: after "N 's" comes a noun, after a bare initial noun a verb
        int[][] training =
        [
            .. Repeat(6, PosTag.Noun, PosTag.Other, PosTag.Noun),
            .. Repeat(6, PosTag.Noun, PosTag.Verb),
        ];
        var noun = 1 << PosTag.Noun;

        // Act
        var joined = RunOn(training, [TaggerToken.PossessiveWord("dog", noun, false), TaggerToken.Word("x", Open, false)]);
        var bare = RunOn(training, [TaggerToken.Word("dog", noun, false), TaggerToken.Word("x", Open, false)]);

        // Assert
        Assert.True(At(joined, 1, PosTag.Noun) > 0.5);
        Assert.True(At(bare, 1, PosTag.Verb) > 0.5);
    }

    /// <summary>
    ///     Several possessives and other tokens give one row for each token of the caller, each a distribution, and the
    ///     possessive token carries its own stem row.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_SeveralPossessives_KeepOneRowPerToken()
    {
        // Arrange: two possessives among other tokens
        var noun = 1 << PosTag.Noun;
        TaggerToken[] sentence =
        [
            TaggerToken.PossessiveWord("dog", noun, false),
            TaggerToken.Word("x", Open, false),
            TaggerToken.PossessiveWord("cat", noun, true),
            TaggerToken.Word("the", 0, false),
            TaggerToken.Word("x", Open, false),
        ];

        // Act
        var posteriors = Run(sentence);

        // Assert
        Assert.Equal(sentence.Length * PosTag.Count, posteriors.Length);
        for (var i = 0; i < sentence.Length; i++)
        {
            Assert.Equal(1.0, Enumerable.Range(0, PosTag.Count).Sum(t => At(posteriors, i, t)), 9);
        }

        Assert.Equal(1.0, At(posteriors, 0, PosTag.Noun), 9);
        Assert.Equal(1.0, At(posteriors, 2, PosTag.Noun), 9);
        Assert.Equal(1.0, At(posteriors, 3, PosTag.Determiner), 9);
    }

    private static readonly WordDetails PriorNoun = new("pw", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails PriorVerb = new("pw", PartOfSpeech.Verb, "VerbForm=Inf");

    /// <summary>
    ///     Makes the catalog support of a toy word that is a noun and a verb with the given tag counts and one sense each.
    /// </summary>
    /// <param name="noun">The WordNet tag count of the noun.</param>
    /// <param name="verb">The WordNet tag count of the verb.</param>
    /// <returns>The support.</returns>
    private static WordSupport? SupportOf(int noun, int verb) => WordSupport.Compute(
        [PriorNoun, PriorVerb],
        (lemma, pos) => pos == PartOfSpeech.Noun ? (noun, 1) : (verb, 1));

    /// <summary>
    ///     Runs the tagger on single-word sentences with equal noun and verb counts in the transitions, so that only the
    ///     emission of the word decides, with a toy table of the given word prior and counts for the word.
    /// </summary>
    /// <param name="priorShare">The share of the word prior in the target of the pseudo counts.</param>
    /// <param name="counts">The counts of the word <c>pw</c>, or null for a word without counts.</param>
    /// <param name="support">The catalog support of the token.</param>
    /// <returns>The posterior of the noun and of the verb.</returns>
    private static (double Noun, double Verb) RunPrior(double priorShare, int[]? counts, WordSupport? support)
    {
        var tables = TestTables.Load(TestTables.Build(
            counts is null ? [("the", 1 << PosTag.Determiner, Counts((PosTag.Determiner, 40)))] : [("pw", 0, counts)],
            [],
            [],
            sentences: [.. Repeat(5, PosTag.Noun), .. Repeat(5, PosTag.Verb)],
            clauseSmoothing: 0,
            priorShare: priorShare,
            priorSmoothing: 0.5));
        var posteriors = new double[PosTag.Count];
        PosTagger.Posteriors(tables, [TaggerToken.Word("pw", Open, false, false, support)], posteriors);
        return (At(posteriors, 0, PosTag.Noun), At(posteriors, 0, PosTag.Verb));
    }

    /// <summary>
    ///     A word without counts takes its emission from the catalog: a word whose noun is far more frequent than its verb is
    ///     a noun, and the reverse.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_WordWithoutCounts_FollowsTheCatalogPrior()
    {
        // Arrange: a word without counts, with catalog support that favors the noun or the verb, and the prior on
        var nounSupport = SupportOf(40, 2);
        var verbSupport = SupportOf(2, 40);

        // Act: tag the word with the prior fully on, once for each support
        var nounHeavy = RunPrior(1.0, null, nounSupport);
        var verbHeavy = RunPrior(1.0, null, verbSupport);

        // Assert: the part of speech with the larger catalog support wins
        Assert.True(nounHeavy.Noun > 0.8);
        Assert.True(verbHeavy.Verb > 0.8);
    }

    /// <summary>
    ///     A token without catalog support gets the emission of the counts and the generic tag prior alone.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_NoSupport_IgnoresThePrior()
    {
        // Arrange: a word without counts and without catalog support, and the prior share 1

        // Act: tag the word without catalog support
        var noSupport = RunPrior(1.0, null, null);

        // Assert: the noun and the verb are equally likely, as the transitions are equal
        Assert.Equal(noSupport.Noun, noSupport.Verb, 6);
    }

    /// <summary>
    ///     A table whose prior share is 0 gives every token the emission of the counts and the generic tag prior alone.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_NoShare_IgnoresThePrior()
    {
        // Arrange: a word without counts, with catalog support that favors the noun, and a prior share of 0

        // Act: tag the word with a prior share of 0
        var noShare = RunPrior(0.0, null, SupportOf(40, 2));

        // Assert: the support has no effect, so the noun and the verb are equally likely
        Assert.Equal(noShare.Noun, noShare.Verb, 6);
    }

    /// <summary>
    ///     The training counts of a word outweigh the catalog once there are many of them.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_ManyCounts_OutweighTheCatalogPrior()
    {
        // Arrange: 400 verb counts against 4 noun counts, and catalog support that favors the noun
        var counts = Counts((PosTag.Verb, 400), (PosTag.Noun, 4));

        // Act: tag the word with a prior share of 0.8
        var many = RunPrior(0.8, counts, SupportOf(40, 2));

        // Assert: the verb of the counts wins
        Assert.True(many.Verb > 0.8);
    }

    /// <summary>
    ///     The catalog breaks a tie when the few training counts of a word do not separate its parts of speech.
    /// </summary>
    [Fact]
    public void PosTagger_Posteriors_FewCounts_TieIsBrokenByTheCatalogPrior()
    {
        // Arrange: one noun count and one verb count, and catalog support that favors the noun
        var counts = Counts((PosTag.Verb, 1), (PosTag.Noun, 1));

        // Act: tag the word with a prior share of 0.8
        var few = RunPrior(0.8, counts, SupportOf(40, 2));

        // Assert: the noun of the catalog wins
        Assert.True(few.Noun > few.Verb);
    }
}
