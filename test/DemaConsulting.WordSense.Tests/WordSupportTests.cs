using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordSupport"/>: the catalog evidence of the parts of speech of a word and the word prior
///     that the tagger blends into its emissions.
/// </summary>
public class WordSupportTests
{
    private static readonly WordDetails RankNoun = new("rank", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails RankVerb = new("rank", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly WordDetails RankVerbPast = new("rank", PartOfSpeech.Verb, "Tense=Past+VerbForm=Fin");
    private static readonly WordDetails ForPreposition = new("for", PartOfSpeech.Preposition, string.Empty);

    private static (int TagCount, int SenseCount) Usage(string lemma, PartOfSpeech pos) => (lemma, pos) switch
    {
        ("rank", PartOfSpeech.Noun) => (32, 4),
        ("rank", PartOfSpeech.Verb) => (3, 2),
        ("for", PartOfSpeech.Preposition) => (0, 1),
        _ => (0, 0),
    };

    /// <summary>
    ///     The tag counts, the senses and the slots of a word are totaled for each tag.
    /// </summary>
    [Fact]
    public void WordSupport_Compute_NounAndVerb_TotalsPerTag()
    {
        // Arrange: the analyses of rank as a noun and as a verb, whose catalog totals are in Usage

        // Act: total the support of the two analyses
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage);

        // Assert: each tag has its own totals and a tag without an analysis has none
        Assert.NotNull(support);
        Assert.Equal(32, support.TagCount(PosTag.Noun));
        Assert.Equal(4, support.SenseCount(PosTag.Noun));
        Assert.Equal(1, support.SlotCount(PosTag.Noun));
        Assert.Equal(3, support.TagCount(PosTag.Verb));
        Assert.Equal(2, support.SenseCount(PosTag.Verb));
        Assert.Equal(0, support.SlotCount(PosTag.Adjective));
    }

    /// <summary>
    ///     The analyses of one slot (the past and the infinitive of a verb) share their senses, so the slot counts once.
    /// </summary>
    [Fact]
    public void WordSupport_Compute_AnalysesOfOneSlot_CountOnce()
    {
        // Arrange: two analyses of the verb slot and one of the noun slot
        ImmutableArray<WordDetails> analyses = [RankVerb, RankNoun, RankVerbPast];

        // Act: total the support of the three analyses
        var support = WordSupport.Compute(analyses, Usage);

        // Assert: the verb slot and its tag count are counted once
        Assert.NotNull(support);
        Assert.Equal(3, support.TagCount(PosTag.Verb));
        Assert.Equal(1, support.SlotCount(PosTag.Verb));
    }

    /// <summary>
    ///     A closed-class analysis adds nothing, because WordNet does not count the closed classes: its project sense has no count.
    /// </summary>
    [Fact]
    public void WordSupport_Compute_ClosedClassAnalysis_IsIgnored()
    {
        // Arrange: a noun with a preposition, and a preposition alone

        // Act: total the support of each list
        var withClosed = WordSupport.Compute([RankNoun, ForPreposition], Usage);
        var onlyClosed = WordSupport.Compute([ForPreposition], Usage);

        // Assert: the preposition has no slot, and a word with nothing else has no support
        Assert.NotNull(withClosed);
        Assert.Equal(0, withClosed.SlotCount(PosTag.Preposition));
        Assert.Null(onlyClosed);
    }

    /// <summary>
    ///     A word whose slots have no sense in the catalog has no support, so the tagger falls back to its counts.
    /// </summary>
    [Fact]
    public void WordSupport_Compute_NoSenses_ReturnsNull()
    {
        // Arrange: an analysis that the catalog has no sense for
        ImmutableArray<WordDetails> analyses = [new WordDetails("zorp", PartOfSpeech.Noun, string.Empty)];

        // Act: total the support of the analysis
        var support = WordSupport.Compute(analyses, Usage);

        // Assert: there is no support
        Assert.Null(support);
    }

    /// <summary>
    ///     A word without analyses has no support.
    /// </summary>
    [Fact]
    public void WordSupport_Compute_NoAnalyses_ReturnsNull()
    {
        // Arrange: an empty list of analyses

        // Act: total the support of no analysis
        var support = WordSupport.Compute([], Usage);

        // Assert: there is no support
        Assert.Null(support);
    }

    /// <summary>
    ///     The prior of the possible tags is the normalized weight of each part of speech, scaled to the share that the generic
    ///     prior gives to the parts of speech of the catalog.
    /// </summary>
    [Fact]
    public void WordSupport_TryFillPrior_NounVerb_SumsToOneAndFollowsTheCounts()
    {
        // Arrange: the support of rank and an even generic share
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb];
        double[] generic = [0.5, 0.5];
        var prior = new double[2];

        // Act: fill the prior of the two possible tags
        var found = support.TryFillPrior(allowed, generic, 0.5, 0.0, 1.0, prior);

        // Assert: the prior sums to 1 and each tag has the share of its weight (tag count plus the smoothing of its slot)
        Assert.True(found);
        Assert.Equal(1.0, prior.Sum(), 9);
        Assert.Equal(32.5 / 36.0, prior[0], 9);
        Assert.Equal(3.5 / 36.0, prior[1], 9);
    }

    /// <summary>
    ///     A possible tag that the catalog does not cover keeps its generic share, and the catalog tags share the rest.
    /// </summary>
    [Fact]
    public void WordSupport_TryFillPrior_ClosedClassTag_KeepsItsGenericShare()
    {
        // Arrange: the support of rank and a possible preposition tag that the catalog does not cover
        var support = WordSupport.Compute([RankNoun, RankVerb, ForPreposition], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb, PosTag.Preposition];
        double[] generic = [0.4, 0.2, 0.4];
        var prior = new double[3];

        // Act: fill the prior of the three possible tags
        var found = support.TryFillPrior(allowed, generic, 0.5, 0.0, 1.0, prior);

        // Assert: the preposition keeps 0.4 and the noun takes its part of the remaining 0.6
        Assert.True(found);
        Assert.Equal(0.4, prior[2], 9);
        Assert.Equal(1.0, prior.Sum(), 9);
        Assert.Equal(0.6 * 32.5 / 36.0, prior[0], 9);
    }

    /// <summary>
    ///     An exponent below 1 flattens the prior.
    /// </summary>
    [Fact]
    public void WordSupport_TryFillPrior_ExponentBelowOne_FlattensThePrior()
    {
        // Arrange: the support of rank and an even generic share
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb];
        double[] generic = [0.5, 0.5];
        var sharp = new double[2];
        var flat = new double[2];

        // Act: fill the prior with the exponent 1 and with the exponent 0.5
        support.TryFillPrior(allowed, generic, 0.5, 0.0, 1.0, sharp);
        support.TryFillPrior(allowed, generic, 0.5, 0.0, 0.5, flat);

        // Assert: the verb, the less frequent tag, gets more of the prior with the smaller exponent
        Assert.True(flat[1] > sharp[1]);
    }

    /// <summary>
    ///     The weight of the senses lifts a part of speech that has many senses and no tag count.
    /// </summary>
    [Fact]
    public void WordSupport_TryFillPrior_SenseWeight_LiftsAPartOfSpeechWithManySenses()
    {
        // Arrange: the support of rank and an even generic share
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb];
        double[] generic = [0.5, 0.5];
        var withoutSenses = new double[2];
        var withSenses = new double[2];

        // Act: fill the prior with the sense weight 0 and with the sense weight 10
        support.TryFillPrior(allowed, generic, 0.5, 0.0, 1.0, withoutSenses);
        support.TryFillPrior(allowed, generic, 0.5, 10.0, 1.0, withSenses);

        // Assert: the verb gets more of the prior when the senses weigh
        Assert.True(withSenses[1] > withoutSenses[1]);
    }

    /// <summary>
    ///     When no possible tag has a slot the prior is the generic share and the call reports that there was no evidence.
    /// </summary>
    [Fact]
    public void WordSupport_TryFillPrior_NoPossibleTagHasASlot_ReturnsFalseWithTheGenericShare()
    {
        // Arrange: the support of a noun and possible tags that are an adjective and an adverb
        var support = WordSupport.Compute([RankNoun], Usage)!;
        int[] allowed = [PosTag.Adjective, PosTag.Adverb];
        double[] generic = [0.3, 0.7];
        var prior = new double[2];

        // Act: fill the prior of the two possible tags
        var found = support.TryFillPrior(allowed, generic, 0.5, 0.0, 1.0, prior);

        // Assert: no evidence, and the prior is the generic share
        Assert.False(found);
        Assert.Equal(generic, prior);
    }

    /// <summary>
    ///     The prior stays finite and sums to 1 for the largest exponent that a table accepts and for exponents far above
    ///     it, where the weights themselves would overflow a double.
    /// </summary>
    /// <param name="exponent">The exponent of the weights.</param>
    [Theory]
    [InlineData(16.0)]
    [InlineData(1000.0)]
    [InlineData(1e6)]
    public void WordSupport_TryFillPrior_LargeExponent_StaysFiniteAndNormalized(double exponent)
    {
        // Arrange: the support of rank, whose noun weighs far more than its verb
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb];
        double[] generic = [0.5, 0.5];
        var prior = new double[2];

        // Act: fill the prior with the exponent of the case
        var found = support.TryFillPrior(allowed, generic, 0.5, 0.0, exponent, prior);

        // Assert: both shares are finite, they sum to 1 and the frequent noun takes the share
        Assert.True(found);
        Assert.All(prior, p => Assert.True(double.IsFinite(p)));
        Assert.Equal(1.0, prior.Sum(), 9);
        Assert.True(prior[0] > prior[1]);
    }

    /// <summary>
    ///     A weight that is not a finite number is rejected as invalid data, not turned into a probability.
    /// </summary>
    /// <param name="smoothing">The smoothing of the slots.</param>
    /// <param name="exponent">The exponent of the weights.</param>
    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.5, double.NaN)]
    public void WordSupport_TryFillPrior_NonFiniteWeight_Throws(double smoothing, double exponent)
    {
        // Arrange: the support of rank and a parameter that makes the weight non-finite
        var support = WordSupport.Compute([RankNoun, RankVerb], Usage)!;
        int[] allowed = [PosTag.Noun, PosTag.Verb];
        double[] generic = [0.5, 0.5];
        var prior = new double[2];

        // Act: prepare the call that fills the prior with the non-finite parameter
        var fill = () =>
        {
            support.TryFillPrior(allowed, generic, smoothing, 0.0, exponent, prior);
        };

        // Assert: the call throws InvalidDataException
        Assert.Throws<InvalidDataException>(fill);
    }

    /// <summary>
    ///     The support of a word of the embedded image reads its tag counts: <c>rank</c> is a noun far more often than a verb.
    /// </summary>
    [Fact]
    public void WordSupport_Of_EmbeddedImage_ReadsTagCounts()
    {
        // Arrange: the analyses of rank in the embedded lexicon
        var analyses = WordLexicon.Lookup("rank");

        // Act: read the support of rank from the embedded image
        var support = WordSupport.Of("rank", analyses);

        // Assert: the noun has more tag counts than the verb
        Assert.NotNull(support);
        Assert.True(support.TagCount(PosTag.Noun) > support.TagCount(PosTag.Verb));
    }

    /// <summary>
    ///     A second request for the same word gives the same support.
    /// </summary>
    [Fact]
    public void WordSupport_Of_SameWordTwice_ReturnsTheSameSupport()
    {
        // Arrange: the analyses of rank in the embedded lexicon
        var analyses = WordLexicon.Lookup("rank");

        // Act: ask for the support of rank twice
        var first = WordSupport.Of("rank", analyses);
        var second = WordSupport.Of("rank", analyses);

        // Assert: both requests give the same instance
        Assert.Same(first, second);
    }

    /// <summary>
    ///     A word without analyses has no support.
    /// </summary>
    [Fact]
    public void WordSupport_Of_NoAnalyses_ReturnsNull()
    {
        // Arrange: an empty list and the default list of analyses

        // Act: ask for the support with each kind of empty list
        var empty = WordSupport.Of("zzzz", []);
        var missing = WordSupport.Of("zzzz", default);

        // Assert: neither has support
        Assert.Null(empty);
        Assert.Null(missing);
    }

    /// <summary>
    ///     A missing key is rejected.
    /// </summary>
    [Fact]
    public void WordSupport_Of_NullKey_Throws()
    {
        // Arrange: no key is needed, because the key is the missing part

        // Act: prepare the call without a key
        var call = () => WordSupport.Of(null!, []);

        // Assert: the call throws ArgumentNullException
        Assert.Throws<ArgumentNullException>(call);
    }

    /// <summary>
    ///     The usage totals of the word image are the sums over the senses of a lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordImage_UsageTotalsOf_MatchesTheUsageOfTheSenses()
    {
        // Arrange: the usage of the senses of rank as a noun
        var image = WordLexicon.Current;
        var usage = image.UsageOf("rank", PartOfSpeech.Noun);

        // Act: read the usage totals of rank as a noun
        var (tagCount, senseCount) = image.UsageTotalsOf("rank", PartOfSpeech.Noun);

        // Assert: the totals are the sums, and an unknown lemma has none
        Assert.Equal(usage.Length, senseCount);
        Assert.Equal(usage.Sum(u => u.TagCount), tagCount);
        Assert.Equal((0, 0), image.UsageTotalsOf("zzzz", PartOfSpeech.Noun));
    }
}
