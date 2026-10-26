using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="BasicTables"/>, <see cref="LemmaCue"/> and the feature hash of <see cref="FeatureRef"/>. The
///     tables are loaded against a toy analysis inventory, so these tests need no word image except the two that name
///     the embedded table.
/// </summary>
public class BasicTablesTests
{
    /// <summary>
    ///     A table reads back its header values, words, suffixes and cue tables.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ToyTable_ReadsContent()
    {
        var tables = TestTables.Load(TestTables.Default());

        Assert.Equal(0.75, tables.Gamma);
        Assert.Equal(5.0, tables.Beta);
        Assert.Equal(1.5, tables.HmmWeight);
        Assert.Equal(0.5, tables.InvTemperature);
        Assert.Equal(3, tables.Words.Count);
        Assert.Equal(1 << PosTag.Determiner, tables.Words["the"].ClosedMask);
        Assert.Equal(12, tables.Words["record"].Total);
        Assert.Equal(1, tables.SuffixCount);
        var cue = tables.Cues["read"];
        Assert.Equal(TestTables.ReadColumns(), cue.Analyses);
        Assert.Equal(2, cue.Hashes.Length);
    }

    /// <summary>
    ///     The interpolated transitions of an observed context sum to one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Transitions_AreDistributions()
    {
        var tables = TestTables.Load(TestTables.Default());

        var sum = 0.0;
        for (var c = 0; c < PosTag.Extended; c++)
        {
            sum += tables.Transition[(((PosTag.Determiner * PosTag.Extended) + PosTag.Noun) * PosTag.Extended) + c];
        }

        Assert.Equal(1.0, sum, 9);
    }

    /// <summary>
    ///     The sentences of the clause tests: a verb follows a determiner and a noun in a clause without a verb (the
    ///     first three) and a noun follows them in a clause that already has one (the last two).
    /// </summary>
    private static readonly int[][] ClauseSentences =
    [
        [PosTag.Determiner, PosTag.Noun, PosTag.Verb],
        [PosTag.Determiner, PosTag.Noun, PosTag.Verb],
        [PosTag.Determiner, PosTag.Noun, PosTag.Verb],
        [PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun],
        [PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Noun],
    ];

    /// <summary>
    ///     Loads a toy table that has the given training sentences and clause smoothing and no words, suffixes or cues.
    /// </summary>
    /// <param name="sentences">The tag sequences of the training sentences.</param>
    /// <param name="clauseSmoothing">The smoothing of the clause state written to the header.</param>
    /// <returns>The table.</returns>
    private static BasicTables LoadClauseTable(int[][] sentences, double clauseSmoothing) =>
        TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], sentences: sentences, clauseSmoothing: clauseSmoothing));

    /// <summary>
    ///     Gets the transition probability of a tag after a determiner and a noun in one clause state.
    /// </summary>
    /// <param name="tables">The table.</param>
    /// <param name="state">The clause state: 0 for a clause without a verb, 1 for a clause that has one.</param>
    /// <param name="tag">The tag that follows.</param>
    /// <returns>The probability.</returns>
    private static double AfterDeterminerAndNoun(BasicTables tables, int state, int tag) =>
        tables.ClauseTransition[(state * PosTag.Extended * PosTag.Extended * PosTag.Extended) + (((PosTag.Determiner * PosTag.Extended) + PosTag.Noun) * PosTag.Extended) + tag];

    /// <summary>
    ///     Gets the largest difference between the transition probabilities of a clause state after a determiner and a
    ///     noun and the unconditioned transition probabilities of the same context.
    /// </summary>
    /// <param name="tables">The table.</param>
    /// <param name="state">The clause state: 0 for a clause without a verb, 1 for a clause that has one.</param>
    /// <returns>The largest absolute difference over the next tag.</returns>
    private static double DistanceToTheUnconditioned(BasicTables tables, int state)
    {
        var context = ((PosTag.Determiner * PosTag.Extended) + PosTag.Noun) * PosTag.Extended;
        var largest = 0.0;
        for (var c = 0; c < PosTag.Extended; c++)
        {
            largest = Math.Max(largest, Math.Abs(AfterDeterminerAndNoun(tables, state, c) - tables.Transition[context + c]));
        }

        return largest;
    }

    /// <summary>
    ///     Reads the decompressed table of a table without any clause count and raises the first count of one clause
    ///     block above its total, which is 0.
    /// </summary>
    /// <param name="trigram">True to raise the first clause trigram count, false to raise the first clause bigram count.</param>
    /// <returns>The compressed table with the raised count.</returns>
    private static byte[] WithClauseCountAboveTotal(bool trigram)
    {
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], sentences: [[PosTag.Noun]], clauseSmoothing: 0));
        var header = 4 + 32 + (14 * 8) + 1;
        var counts = PosTag.Extended + (PosTag.Extended * PosTag.Extended) + (PosTag.Extended * PosTag.Extended * PosTag.Extended);
        var offset = header + counts + (trigram ? PosTag.Extended * PosTag.Extended : 0);

        // Every count is below 128, so each is one byte
        Assert.Equal((byte)0, raw[offset]);
        raw[offset] = 100;
        return TestTables.Compress(raw);
    }

    /// <summary>
    ///     The bigram and trigram counts of the positions whose clause already has a verb are exposed next to the totals.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseCounts_AreExposed()
    {
        // Arrange: in "they go the dog dog" the determiner and the nouns come after the verb, so their clause has one
        const int k = PosTag.Extended;
        var tables = LoadClauseTable(ClauseSentences, 0);

        // Act
        var bigrams = tables.ClauseBigrams;
        var trigrams = tables.ClauseTrigrams;

        // Assert: the layout of the totals, and the counts of the two sentences with a verb before the determiner
        Assert.Equal(k * k, bigrams.Length);
        Assert.Equal(k * k * k, trigrams.Length);
        Assert.Equal(2, bigrams[(PosTag.Verb * k) + PosTag.Determiner]);
        Assert.Equal(2, bigrams[(PosTag.Determiner * k) + PosTag.Noun]);
        Assert.Equal(2, trigrams[(((PosTag.Verb * k) + PosTag.Determiner) * k) + PosTag.Noun]);
        Assert.Equal(5, tables.Bigrams[(PosTag.Determiner * k) + PosTag.Noun]);
        Assert.Equal(0, bigrams[(PosTag.Pronoun * k) + PosTag.Verb]);
    }

    /// <summary>
    ///     The smoothing of the clause state is read from the header.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseSmoothing_IsRead()
    {
        // Arrange: a table with a smoothing that differs from the default of the toy table
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], clauseSmoothing: 7.5);

        // Act
        var tables = TestTables.Load(blob);
        var defaultTables = TestTables.Load(TestTables.Default());

        // Assert
        Assert.Equal(7.5, tables.ClauseSmoothing);
        Assert.Equal(4.0, defaultTables.ClauseSmoothing);
    }

    /// <summary>
    ///     The transition probabilities of both clause states are distributions over an observed context.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseTransitions_AreDistributions()
    {
        // Arrange: the default toy table, which has an observed context of a determiner and a noun in both states
        var tables = TestTables.Load(TestTables.Default());

        // Act: add up the probabilities of the next tag in each state
        var sums = new[] { 0, 1 }.Select(state => Enumerable.Range(0, PosTag.Extended).Sum(c => AfterDeterminerAndNoun(tables, state, c)));

        // Assert
        Assert.All(sums, sum => Assert.Equal(1.0, sum, 9));
        Assert.Equal(2 * PosTag.Extended * PosTag.Extended * PosTag.Extended, tables.ClauseTransition.Length);
    }

    /// <summary>
    ///     The transitions of a clause state follow the counts of that state: a verb after a determiner and a noun is
    ///     more probable in the state in which it was counted more often, a noun in the other.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseTransitions_FollowTheClauseCounts()
    {
        // Arrange: the verb after "D N" only occurs in a clause without a verb; no smoothing, so the counts decide
        var tables = LoadClauseTable(ClauseSentences, 0);

        // Act
        var verbWithout = AfterDeterminerAndNoun(tables, 0, PosTag.Verb);
        var verbWith = AfterDeterminerAndNoun(tables, 1, PosTag.Verb);
        var nounWithout = AfterDeterminerAndNoun(tables, 0, PosTag.Noun);
        var nounWith = AfterDeterminerAndNoun(tables, 1, PosTag.Noun);

        // Assert
        Assert.True(verbWithout > verbWith);
        Assert.True(nounWith > nounWithout);
    }

    /// <summary>
    ///     A table without clause counts gives both clause states the unconditioned transitions.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseTransitions_WithoutClauseCounts_AreTheUnconditioned()
    {
        // Arrange: no verb in the sentences means no clause counts at all
        var plain = LoadClauseTable([[PosTag.Noun, PosTag.Noun]], 0);
        var size = PosTag.Extended * PosTag.Extended * PosTag.Extended;

        // Act / Assert: both states equal the unconditioned transition at every index
        for (var i = 0; i < size; i++)
        {
            Assert.Equal(plain.Transition[i], plain.ClauseTransition[i], 12);
            Assert.Equal(plain.Transition[i], plain.ClauseTransition[size + i], 12);
        }
    }

    /// <summary>
    ///     A larger smoothing moves the transitions of both clause states closer to the unconditioned transitions
    ///     without ever making them equal where the context has counts in the state; the distance shrinks toward zero.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseSmoothing_MovesTheTransitionsTowardTheUnconditioned()
    {
        // Arrange: the same counts with a growing smoothing
        double[] values = [0, 4, 64, 1e6];
        var tables = values.Select(s => LoadClauseTable(ClauseSentences, s)).ToArray();

        // Act: the distance to the unconditioned transitions in each state
        var distances = new[] { 0, 1 }.Select(state => tables.Select(t => DistanceToTheUnconditioned(t, state)).ToArray()).ToArray();

        // Assert: strictly smaller for each larger smoothing, close to zero for the largest and not exactly zero
        Assert.All(distances, d =>
        {
            Assert.True(d[0] > d[1]);
            Assert.True(d[1] > d[2]);
            Assert.True(d[2] > d[3]);
            Assert.True(d[3] < 1e-3);
            Assert.True(d[3] > 0);
        });
    }

    /// <summary>
    ///     A clause state is mixed with the unconditioned transitions by the weight <c>m / (m + smoothing)</c>, where
    ///     <c>m</c> is the number of trigram counts of the context in that state.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseSmoothing_WeighsTheContextByItsCountInTheState()
    {
        // Arrange: the counts without smoothing give the state-only transitions; smoothing 4 mixes them in
        const int k = PosTag.Extended;
        var counted = LoadClauseTable(ClauseSentences, 0);
        var mixed = LoadClauseTable(ClauseSentences, 4);
        var context = ((PosTag.Determiner * k) + PosTag.Noun) * k;
        var withVerb = Enumerable.Range(0, k).Sum(c => counted.ClauseTrigrams[context + c]);
        var withoutVerb = Enumerable.Range(0, k).Sum(c => counted.Trigrams[context + c] - counted.ClauseTrigrams[context + c]);
        Assert.Equal(2, withVerb);
        Assert.Equal(3, withoutVerb);

        // Act / Assert: the mixed value is the weighted mean of the counted value and the unconditioned value
        foreach (var (state, m) in new[] { (0, withoutVerb), (1, withVerb) })
        {
            var weight = m / (m + 4.0);
            for (var c = 0; c < k; c++)
            {
                var expected = (weight * AfterDeterminerAndNoun(counted, state, c)) + ((1 - weight) * counted.Transition[context + c]);
                Assert.Equal(expected, AfterDeterminerAndNoun(mixed, state, c), 12);
            }
        }
    }

    /// <summary>
    ///     A clause bigram count that is larger than the total count of the same bigram is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseBigramAboveTotal_Throws()
    {
        // Arrange: a table without a clause count, with the first clause bigram count raised above its total
        var blob = WithClauseCountAboveTotal(trigram: false);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     A clause trigram count that is larger than the total count of the same trigram is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_ClauseTrigramAboveTotal_Throws()
    {
        // Arrange: a table without a clause count, with the first clause trigram count raised above its total
        var blob = WithClauseCountAboveTotal(trigram: true);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     The longest known suffix gives a distribution over the open classes; an unknown ending gives none.
    /// </summary>
    [Fact]
    public void BasicTables_TryFindSuffix_KnownAndUnknown()
    {
        var tables = TestTables.Load(TestTables.Default());

        var found = tables.TryFindSuffix("walked", out var known);

        Assert.True(found);
        Assert.False(known.IsDefault);
        Assert.Equal(1.0, known.Sum(), 9);
        Assert.True(known[1] > known[0]);
        Assert.False(tables.TryFindSuffix("xyz", out var unknown));
        Assert.True(unknown.IsDefault);
    }

    /// <summary>
    ///     A blob with a wrong magic number is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_BadMagic_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());
        raw[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));
    }

    /// <summary>
    ///     A blob that is not compressed data is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NotCompressed_Throws()
    {
        Assert.Throws<InvalidDataException>(() => TestTables.Load("not a table at all"u8));
    }

    /// <summary>
    ///     A string with malformed UTF-8 is rejected as corrupt data, not decoded with replacement characters.
    /// </summary>
    [Fact]
    public void BasicTables_Load_MalformedUtf8String_ThrowsInvalidData()
    {
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], []));
        var at = raw.AsSpan().IndexOf("record"u8);
        Assert.True(at >= 0);
        raw[at] = 0xFF; // never valid in UTF-8

        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        Assert.IsType<System.Text.DecoderFallbackException>(ex.InnerException);
    }

    /// <summary>
    ///     A truncated blob is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Truncated_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw[..(raw.Length - 3)])));
    }

    /// <summary>
    ///     A blob cut at any byte, so at every section boundary (header, tag counts, words, suffixes, cue tables) and
    ///     inside every section, is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TestTables.Decompress(TestTables.Default());

        // Act / Assert: every proper prefix is rejected
        for (var length = 0; length < raw.Length; length++)
        {
            var blob = TestTables.Compress(raw[..length]);
            Assert.True(
                Throws(() => TestTables.Load(blob)),
                $"A table cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     The retired class-keyed format and a tag count that differs from the tag set are rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_WrongVersionOrTagCount_Throws()
    {
        // Arrange: the tag count is a single-byte varint after the magic, the hash and the fourteen doubles
        var version = TestTables.Decompress(TestTables.Default());
        version[3] = (byte)'2';
        var tags = TestTables.Decompress(TestTables.Default());
        const int tagCountOffset = 4 + 32 + (14 * 8);
        Assert.Equal(PosTag.Count, tags[tagCountOffset]);
        tags[tagCountOffset]++;

        // Act / Assert
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(version))), "Version WSB2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(tags))), "Tag count.");
    }

    /// <summary>
    ///     The previous format <c>WSB3</c>, which has no tense weight and no tense table, is rejected as an unknown format
    ///     whatever else it holds, so a table that was trained before the tense model is never read as a current one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PreviousFormatWsb3_ThrowsUnknownFormat()
    {
        // Arrange: a current table whose magic is changed back to the revision before the tense model
        var raw = TestTables.Decompress(TestTables.Default());
        Assert.Equal((byte)'7', raw[3]);
        raw[3] = (byte)'3';

        // Act: load the table whose magic is WSB3
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        // Assert: the load names an unknown format
        Assert.Contains("unknown format", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The previous format <c>WSB4</c>, which has no clause counts and no clause smoothing, is rejected as an unknown
    ///     format whatever else it holds, so a table that was trained before the clause state is never read as a current
    ///     one.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PreviousFormatWsb4_ThrowsUnknownFormat()
    {
        // Arrange: a current table whose magic is changed back to the revision before the clause state
        var raw = TestTables.Decompress(TestTables.Default());
        Assert.Equal((byte)'7', raw[3]);
        raw[3] = (byte)'4';

        // Act: load the table whose magic is WSB4
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        // Assert: the load names an unknown format
        Assert.Contains("unknown format", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The previous format <c>WSB5</c>, which has no word prior and no reliability of the cue tables, is rejected as an
    ///     unknown format whatever else it holds, so a table that was trained before the word prior is never read as a current
    ///     one (its header is shorter, so reading it as a current table would shift every later value).
    /// </summary>
    [Fact]
    public void BasicTables_Load_PreviousFormatWsb5_ThrowsUnknownFormat()
    {
        // Arrange: a current table whose magic is changed back to the revision before the word prior
        var raw = TestTables.Decompress(TestTables.Default());
        Assert.Equal((byte)'7', raw[3]);
        raw[3] = (byte)'5';

        // Act: load the table whose magic is WSB5
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        // Assert: the load names an unknown format
        Assert.Contains("unknown format", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The previous format <c>WSB6</c>, which has no noun/verb weight and no noun/verb table, is rejected as an unknown
    ///     format whatever else it holds, so a table that was trained before the noun/verb model is never read as a current
    ///     one (its header is shorter, so reading it as a current table would shift every later value).
    /// </summary>
    [Fact]
    public void BasicTables_Load_PreviousFormatWsb6_ThrowsUnknownFormat()
    {
        // Arrange: a current table whose magic is changed back to the revision before the noun/verb model
        var raw = TestTables.Decompress(TestTables.Default());
        Assert.Equal((byte)'7', raw[3]);
        raw[3] = (byte)'6';

        // Act: load the table whose magic is WSB6
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress(raw)));

        // Assert: the load names an unknown format
        Assert.Contains("unknown format", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The share of the word prior reads back from the header.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorShare_ReadBack()
    {
        // Arrange: a table whose header has a prior share of 0.35
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 0.35);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the share is the one in the header
        Assert.Equal(0.35, tables.PriorShare);
    }

    /// <summary>
    ///     The smoothing of each slot of the word prior reads back from the header.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorSmoothing_ReadBack()
    {
        // Arrange: a table whose header has a prior smoothing of 0.25
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorSmoothing: 0.25);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the smoothing is the one in the header
        Assert.Equal(0.25, tables.PriorSmoothing);
    }

    /// <summary>
    ///     The weight of each sense of the word prior reads back from the header.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorSenses_ReadBack()
    {
        // Arrange: a table whose header has a sense weight of 0.75
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorSenses: 0.75);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the sense weight is the one in the header
        Assert.Equal(0.75, tables.PriorSenses);
    }

    /// <summary>
    ///     The exponent of the word prior reads back from the header.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorExponent_ReadBack()
    {
        // Arrange: a table whose header has a prior exponent of 0.5
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorExponent: 0.5);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the exponent is the one in the header
        Assert.Equal(0.5, tables.PriorExponent);
    }

    /// <summary>
    ///     The reliability of a cue table reads back from the cue section as a factor from 0 to 1.
    /// </summary>
    [Fact]
    public void BasicTables_Load_CueReliability_ReadBack()
    {
        // Arrange: a table whose cue table has the reliability byte 51
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("read", TestTables.ReadColumns(), TestTables.CueHashes(), TestTables.CueWeights())],
            reliability: 51);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the byte and its factor, 51 over 255
        Assert.Equal((byte)51, tables.Cues["read"].ReliabilityByte);
        Assert.Equal(0.2, tables.Cues["read"].Reliability, 9);
    }

    /// <summary>
    ///     A table whose word prior share is outside 0 to 1 is rejected.
    /// </summary>
    /// <param name="share">The share.</param>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void BasicTables_Load_PriorShareOutOfRange_Throws(double share)
    {
        // Arrange: a table whose header carries the share
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: share, priorSmoothing: 0.5);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A table whose word prior has a negative smoothing is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorSmoothingNegative_Throws()
    {
        // Arrange: a table whose header carries a negative smoothing
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 1.0, priorSmoothing: -0.5);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A table whose word prior has a negative sense weight is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorSensesNegative_Throws()
    {
        // Arrange: a table whose header carries a negative sense weight
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 1.0, priorSmoothing: 0.5, priorSenses: -1.0);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A table whose word prior exponent is not positive or is above 16 is rejected.
    /// </summary>
    /// <param name="exponent">The exponent.</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(17.0)]
    [InlineData(1e6)]
    public void BasicTables_Load_PriorExponentOutOfRange_Throws(double exponent)
    {
        // Arrange: a table whose header carries the exponent
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 1.0, priorSmoothing: 0.5, priorExponent: exponent);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A table whose word prior is on but whose slot smoothing and sense weight are both zero is rejected, because a part
    ///     of speech could weigh 0.
    /// </summary>
    [Fact]
    public void BasicTables_Load_PriorOnWithoutWeight_Throws()
    {
        // Arrange: a table whose prior is on with a smoothing of 0 and a sense weight of 0
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 1.0, priorSmoothing: 0.0, priorSenses: 0.0);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The largest accepted prior exponent loads, so the table is not rejected for a value that the tagger can still use.
    /// </summary>
    [Fact]
    public void BasicTables_Load_LargestPriorExponent_Loads()
    {
        // Arrange: a table whose word prior exponent is the upper bound
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], priorShare: 1.0, priorSmoothing: 0.5, priorSenses: 0.0, priorExponent: 16.0);

        // Act: load the table
        var tables = TestTables.Load(blob);

        // Assert: the exponent 16 is accepted and read back
        Assert.Equal(16.0, tables.PriorExponent);
    }

    /// <summary>
    ///     The tense section reads back: the weight of the model from the header, and the prior, the ascending hashes and the
    ///     weights of the table.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TenseTable_ReadsWeightPriorHashesAndWeights()
    {
        // Arrange
        ushort[] hashes = [3, 70, 40000];
        sbyte[] weights = [-12, 7, 127];
        var with = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (-2, hashes, weights), tenseWeight: 0.75);

        // Act
        var tables = TestTables.Load(with);

        // Assert
        Assert.Equal(0.75, tables.TenseWeight);
        var tense = Assert.IsType<TenseCue>(tables.Tense);
        Assert.Equal((sbyte)-2, tense.Prior);
        Assert.Equal(hashes, tense.Hashes.ToArray());
        Assert.Equal(weights, tense.Weights.ToArray());
    }

    /// <summary>
    ///     A table without the tense section has no tense table and the default weight of the model.
    /// </summary>
    [Fact]
    public void BasicTables_Load_WithoutTenseSection_HasNoTenseTableAndTheDefaultWeight()
    {
        // Arrange
        var without = TestTables.Build([("record", 0, new int[PosTag.Count])], [], []);

        // Act
        var plain = TestTables.Load(without);

        // Assert
        Assert.Null(plain.Tense);
        Assert.Equal(1.0, plain.TenseWeight);
    }

    /// <summary>
    ///     A tense table cut at any byte is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TenseTableTruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70, 400], [1, -2, 3])));

        // Act / Assert: every proper prefix is rejected
        for (var length = 0; length < raw.Length; length++)
        {
            var blob = TestTables.Compress(raw[..length]);
            Assert.True(Throws(() => TestTables.Load(blob)), $"A table with a tense section cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     A tense section with an unknown flag, without features or with unordered hashes is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_InvalidTenseSection_Throws()
    {
        // Arrange: the section before the noun/verb flag is the tense section: flag, prior, count, deltas and weights
        var good = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70], [1, -2])));
        var flag = (byte[])good.Clone();
        flag[^8] = 2;
        var noFeatures = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [], [])));

        // Act / Assert
        Assert.Equal(1, good[^8]);
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(flag))), "Flag 2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(noFeatures))), "No features.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [70, 3], [1, -2])))), "Unordered hashes.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], tense: (0, [3, 70], [1])))), "Fewer weights than hashes.");
    }

    /// <summary>
    ///     A tense table refuses the default (uninitialized) immutable arrays and arrays of different lengths.
    /// </summary>
    [Fact]
    public void TenseCue_Constructor_DefaultOrUnequalArrays_Throws()
    {
        // Arrange: a default array, and arrays of different lengths
        ImmutableArray<ushort> noHashes = default;
        ImmutableArray<sbyte> noWeights = default;

        // Act
        var defaultHashes = Record.Exception(() => new TenseCue(0, noHashes, [1]));
        var defaultWeights = Record.Exception(() => new TenseCue(0, [1], noWeights));
        var unequal = Record.Exception(() => new TenseCue(0, [1, 2], [1]));

        // Assert
        Assert.IsType<ArgumentException>(defaultHashes);
        Assert.IsType<ArgumentException>(defaultWeights);
        Assert.IsType<ArgumentException>(unequal);
    }

    /// <summary>
    ///     The noun/verb section reads back: the weight of the model from the header, and the ascending hashes and the
    ///     weights of the table.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NounVerbTable_ReadsWeightHashesAndWeights()
    {
        // Arrange: a table with a noun/verb section and a weight of 0.5 in the header
        ushort[] hashes = [3, 70, 40000];
        sbyte[] weights = [-12, 7, 127];
        var with = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: (hashes, weights), nounVerbWeight: 0.5);

        // Act
        var tables = TestTables.Load(with);

        // Assert: the weight, the hashes and the weights read back
        Assert.Equal(0.5, tables.NounVerbWeight);
        var nounVerb = Assert.IsType<NounVerbCue>(tables.NounVerb);
        Assert.Equal(hashes, nounVerb.Hashes.ToArray());
        Assert.Equal(weights, nounVerb.Weights.ToArray());
    }

    /// <summary>
    ///     A table without the noun/verb section has no noun/verb table.
    /// </summary>
    [Fact]
    public void BasicTables_Load_WithoutNounVerbSection_HasNoNounVerbTable()
    {
        // Arrange
        var without = TestTables.Build([("record", 0, new int[PosTag.Count])], [], []);

        // Act
        var plain = TestTables.Load(without);

        // Assert
        Assert.Null(plain.NounVerb);
    }

    /// <summary>
    ///     A noun/verb table cut at any byte is rejected as corrupt data.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NounVerbTableTruncatedAtEveryLength_Throws()
    {
        // Arrange
        var raw = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: ([3, 70, 400], [1, -2, 3])));

        // Act / Assert: every proper prefix is rejected
        for (var length = 0; length < raw.Length; length++)
        {
            var blob = TestTables.Compress(raw[..length]);
            Assert.True(Throws(() => TestTables.Load(blob)), $"A table with a noun/verb section cut at {length} of {raw.Length} bytes was accepted.");
        }
    }

    /// <summary>
    ///     A noun/verb section with an unknown flag, without features or with unordered hashes is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_InvalidNounVerbSection_Throws()
    {
        // Arrange: the section is the last bytes of the table: flag, count, deltas and weights
        var good = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: ([3, 70], [1, -2])));
        var flag = (byte[])good.Clone();
        flag[^6] = 2;
        var noFeatures = TestTables.Decompress(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: ([], [])));

        // Act / Assert
        Assert.Equal(1, good[^6]);
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(flag))), "Flag 2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(noFeatures))), "No features.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: ([70, 3], [1, -2])))), "Unordered hashes.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerb: ([3, 70], [1])))), "Fewer weights than hashes.");
    }

    /// <summary>
    ///     A table whose header carries a negative weight of the noun/verb model is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_NounVerbWeightNegative_Throws()
    {
        // Arrange: a table whose header carries a negative weight
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], nounVerbWeight: -0.5);

        // Act: load the table
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the load names the invalid hyper-parameters
        Assert.Contains("invalid hyper-parameters", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A noun/verb table refuses the default (uninitialized) immutable arrays and arrays of different lengths.
    /// </summary>
    [Fact]
    public void NounVerbCue_Constructor_DefaultOrUnequalArrays_Throws()
    {
        // Arrange: a default array, and arrays of different lengths
        ImmutableArray<ushort> noHashes = default;
        ImmutableArray<sbyte> noWeights = default;

        // Act
        var defaultHashes = Record.Exception(() => new NounVerbCue(noHashes, [1]));
        var defaultWeights = Record.Exception(() => new NounVerbCue([1], noWeights));
        var unequal = Record.Exception(() => new NounVerbCue([1, 2], [1]));

        // Assert
        Assert.IsType<ArgumentException>(defaultHashes);
        Assert.IsType<ArgumentException>(defaultWeights);
        Assert.IsType<ArgumentException>(unequal);
    }

    /// <summary>
    ///     Runs an action and reports whether it threw <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <returns><c>true</c> when the action threw <see cref="InvalidDataException"/>; otherwise <c>false</c>.</returns>
    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>
    ///     A blob with extra bytes at the end is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_TrailingBytes_Throws()
    {
        var raw = TestTables.Decompress(TestTables.Default());

        Assert.Throws<InvalidDataException>(() => TestTables.Load(TestTables.Compress([.. raw, 0])));
    }

    /// <summary>
    ///     A blob with bytes after the end of its Brotli stream is rejected, naming the trailing bytes.
    /// </summary>
    [Fact]
    public void BasicTables_Load_BytesAfterCompressedStream_Throws()
    {
        // Arrange: a complete stream followed by one more byte
        byte[] blob = [.. TestTables.Default(), 0];

        // Act
        var error = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("trailing bytes", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The real embedded table with bytes after its compressed stream, with one inventory-hash byte changed and cut
    ///     short is rejected at the public boundary as <see cref="ModelIntegrityException"/> that carries the original
    ///     <see cref="InvalidDataException"/>, and the unaltered table still loads.
    /// </summary>
    /// <param name="alteration">Which region of the real table to alter.</param>
    [Theory]
    [InlineData("trailing")]
    [InlineData("hash")]
    [InlineData("truncated")]
    public void BasicTables_LoadEmbedded_RealTableWithAlteredRegion_ThrowsModelIntegrity(string alteration)
    {
        // Arrange: the real blob; the hash is the 32 bytes after the four-byte magic of the decompressed table
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(BasicTables.ResourceName)!;
        var real = new byte[stream.Length];
        stream.ReadExactly(real);
        Assert.NotEmpty(BasicTables.LoadEmbedded(new MemoryStream(real)).Words);
        byte[] altered;
        if (alteration == "trailing")
        {
            altered = [.. real, 0x00];
        }
        else if (alteration == "hash")
        {
            var raw = TestTables.Decompress(real);
            raw[4] ^= 0xFF;
            altered = TestTables.Compress(raw);
        }
        else
        {
            altered = real[..(real.Length - 8)];
        }

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(new MemoryStream(altered)));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    ///     The table, its word entries and its cue tables expose no array: every statistic is a read-only view, so a
    ///     consumer cannot change what another consumer sees.
    /// </summary>
    [Fact]
    public void BasicTables_Properties_ExposeNoArrays()
    {
        // Arrange
        Type[] types = [typeof(BasicTables), typeof(WordEntry), typeof(LemmaCue), typeof(TenseCue), typeof(NounVerbCue)];

        // Act
        var arrays = types
            .SelectMany(t => t.GetProperties().Select(p => (Name: $"{t.Name}.{p.Name}", p.PropertyType)))
            .Where(p => p.PropertyType.IsArray)
            .Select(p => p.Name)
            .ToArray();

        // Assert
        Assert.Empty(arrays);
    }

    /// <summary>
    ///     The statistics of a loaded table do not change when the blob it was read from is changed afterwards, and the
    ///     views of the table agree with the values that were written.
    /// </summary>
    [Fact]
    public void BasicTables_Load_Views_AreIndependentOfTheBlob()
    {
        // Arrange
        var blob = TestTables.Default();
        var tables = TestTables.Load(blob);
        var before = tables.Cues["read"].Weights.ToArray();
        var transition = tables.Transition.ToArray();

        // Act: damage the blob after the load
        Array.Fill(blob, (byte)0);

        // Assert
        Assert.Equal(before, tables.Cues["read"].Weights.ToArray());
        Assert.Equal(transition, tables.Transition.ToArray());
        Assert.Equal(PosTag.Extended, tables.Unigrams.Length);
        Assert.Equal(PosTag.Extended * PosTag.Extended, tables.Bigrams.Length);
        Assert.Equal(PosTag.Extended * PosTag.Extended * PosTag.Extended, tables.Trigrams.Length);
        Assert.Equal(PosTag.Count, tables.TagPrior.Length);
        Assert.Equal(PosTag.Count, tables.Words["record"].Counts.Length);
    }

    /// <summary>
    ///     A word entry and a cue table refuse the default (uninitialized) immutable array.
    /// </summary>
    [Fact]
    public void BasicTables_Constructors_DefaultArray_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => new WordEntry(0, default));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", default, [1], [0]));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", [], default, [0]));
        Assert.Throws<ArgumentException>(() => new LemmaCue("read", [], [1], default));
    }

    /// <summary>
    ///     Feature hashes that are not ascending are rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnorderedHashes_Throws()
    {
        var hashes = TestTables.CueHashes();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("read", TestTables.ReadColumns(), [hashes[1], hashes[0]], [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     A cue whose declared feature hashes need more weight bytes than the table holds is rejected as corrupt
    ///     data before any weight block is allocated.
    /// </summary>
    [Fact]
    public void BasicTables_Load_CueWeightsPastEnd_ThrowsInvalidData()
    {
        // Arrange: the last cue declares 300 ascending feature hashes for two analyses but carries only four weights
        var hashes = Enumerable.Range(1, 300).Select(h => (ushort)h).ToArray();
        var blob = TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("read", TestTables.ReadColumns(), hashes, [0, 0, 0, 0])]);

        // Act and Assert: the size check reports the truncation as invalid data
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     Word counts whose sum exceeds the 32-bit range are rejected as corrupt data, not as an overflow.
    /// </summary>
    [Fact]
    public void BasicTables_Load_CountSumOverflow_ThrowsInvalidData()
    {
        // Arrange: a word whose two counts together exceed int.MaxValue
        var counts = new int[PosTag.Count];
        counts[0] = int.MaxValue;
        counts[1] = int.MaxValue;
        var blob = TestTables.Build([("record", 0, counts)], [], []);

        // Act and Assert
        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     The table embedded in the assembly loads and has statistics. It needs the embedded word image and the
    ///     regenerated table.
    /// </summary>
    [Fact]
    public void BasicTables_Default_LoadsEmbeddedTable()
    {
        var tables = BasicTables.Default;

        Assert.NotEmpty(tables.Words);
        Assert.NotEmpty(tables.Cues);
        Assert.Same(tables, BasicTables.Default);
    }

    /// <summary>
    ///     The embedded table carries the shared tense table and the shared noun/verb table, each with a positive weight.
    /// </summary>
    [Fact]
    public void BasicTables_Default_CarriesTheTenseAndNounVerbTables()
    {
        // Arrange / Act
        var tables = BasicTables.Default;

        // Assert
        Assert.NotNull(tables.Tense);
        Assert.True(tables.TenseWeight > 0);
        Assert.NotNull(tables.NounVerb);
        Assert.NotEmpty(tables.NounVerb.Hashes);
        Assert.True(tables.NounVerbWeight > 0);
    }

    /// <summary>
    ///     The embedded table loads from its own resource stream.
    /// </summary>
    [Fact]
    public void BasicTables_LoadEmbedded_EmbeddedResource_Loads()
    {
        // Arrange
        using var stream = typeof(BasicTables).Assembly.GetManifestResourceStream(BasicTables.ResourceName);

        // Act
        var tables = BasicTables.LoadEmbedded(stream);

        // Assert
        Assert.NotEmpty(tables.Words);
    }

    /// <summary>
    ///     A missing embedded table is reported as <see cref="ModelIntegrityException"/> that carries the original
    ///     <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void BasicTables_LoadEmbedded_MissingResource_ThrowsModelIntegrity()
    {
        // Arrange / Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(null));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corrupt or foreign embedded table (bytes that are not a table, and a table of another inventory) is reported
    ///     as <see cref="ModelIntegrityException"/> that carries the original <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="toyTable">Whether to use a well-formed table for another inventory, instead of bytes that are not a table.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BasicTables_LoadEmbedded_CorruptOrForeignResource_ThrowsModelIntegrity(bool toyTable)
    {
        // Arrange
        using var corrupt = new MemoryStream(toyTable ? TestTables.Default() : "not a table at all"u8.ToArray());

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => BasicTables.LoadEmbedded(corrupt));

        // Assert: the original exception is kept
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    ///     A table trained for another analysis inventory is rejected, and the message names both hashes.
    /// </summary>
    [Fact]
    public void BasicTables_Load_OtherInventoryHash_ThrowsNamingBothHashes()
    {
        // Arrange: a table that carries the all-zero hash, loaded against the toy inventory
        var other = new byte[32];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [], inventoryHash: other);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert: the hash of the table and the hash of the inventory are in the message
        Assert.Contains(Convert.ToHexString(other), ex.Message, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(TestTables.ToyHash), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A cue column that names an analysis the inventory does not have is rejected, and the message names it.
    /// </summary>
    [Fact]
    public void BasicTables_Load_UnknownAnalysis_Throws()
    {
        // Arrange: the second column is a noun "read", which the toy inventory lacks
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns =
        [
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
            new("read", PartOfSpeech.Noun, "Number=Sing", 0),
        ];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("'read'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Noun", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A column with a part-of-speech byte that is not a part of speech is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_InvalidPartOfSpeech_Throws()
    {
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns = [new("read", PartOfSpeech.Verb, "Tense=Past", 0), new("read", (PartOfSpeech)99, "Tense=Pres", 0)];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));
    }

    /// <summary>
    ///     An analysis that two columns of one cue table name is rejected.
    /// </summary>
    [Fact]
    public void BasicTables_Load_DuplicateAnalysis_Throws()
    {
        // Arrange: the same lemma, part of speech and features twice
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns = [new("read", PartOfSpeech.Verb, "Tense=Past", 0), new("read", PartOfSpeech.Verb, "Tense=Past", 0)];
        var blob = TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, [0, 0, 0, 0])]);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => TestTables.Load(blob));

        // Assert
        Assert.Contains("twice", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An analysis is scored by the column of its own features when there is one, and otherwise by the column with
    ///     empty features, which stands for every analysis of its lemma and part of speech; an analysis of another
    ///     lemma or part of speech has no column.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_Analysis_UsesTheColumnOfItsFeatures()
    {
        // Arrange: the empty-features column, the past tense and the past participle of read
        var hashes = TestTables.CueHashes();
        CueAnalysis[] columns =
        [
            new("read", PartOfSpeech.Verb, string.Empty, 0),
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
            new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part", 0),
        ];
        var cue = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", columns, hashes, new sbyte[6])])).Cues["read"];

        // Act / Assert: the column of the exact features, the fallback column and no column
        Assert.Equal(1, cue.RowOf(TestTables.ReadPast));
        Assert.Equal(2, cue.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(0, cue.RowOf(TestTables.ReadPres));
        Assert.Equal(0, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, string.Empty)));
        Assert.Equal(-1, cue.RowOf(TestTables.RecordVerb));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Noun, string.Empty)));
    }

    /// <summary>
    ///     Without an empty-features column, an analysis whose features contain none of the column items has no column.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_NoEmptyFeaturesColumn_UnmatchedAnalysisHasNoColumn()
    {
        var cue = TestTables.Load(TestTables.Default()).Cues["read"];

        Assert.Equal(0, cue.RowOf(TestTables.ReadPast));
        Assert.Equal(1, cue.RowOf(TestTables.ReadPres));
        Assert.Equal(0, cue.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, "VerbForm=Inf")));
        Assert.Equal(-1, cue.RowOf(new WordDetails("read", PartOfSpeech.Verb, string.Empty)));
    }

    /// <summary>
    ///     Of the columns whose feature items are all contained in the analysis, the one with the most items wins and the
    ///     first of equally specific columns is taken.
    /// </summary>
    [Fact]
    public void LemmaCue_RowOf_SeveralContainedColumns_TakesTheMostSpecificThenTheFirst()
    {
        // Arrange: two one-item columns that the participle contains, then a two-item column
        var hashes = TestTables.CueHashes();
        CueAnalysis[] ties =
        [
            new("read", PartOfSpeech.Verb, "VerbForm=Part", 0),
            new("read", PartOfSpeech.Verb, "Tense=Past", 0),
        ];
        CueAnalysis[] specific = [.. ties, new("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part", 0)];
        var tied = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", ties, hashes, new sbyte[4])])).Cues["read"];
        var most = TestTables.Load(TestTables.Build([("record", 0, new int[PosTag.Count])], [], [("read", specific, hashes, new sbyte[6])])).Cues["read"];

        // Act / Assert
        Assert.Equal(0, tied.RowOf(TestTables.ReadPastParticiple));
        Assert.Equal(2, most.RowOf(TestTables.ReadPastParticiple));
    }

    /// <summary>
    ///     The cue table of a word is the table named by the normalized word first, then the table of the lemma of its
    ///     first analysis, and none when neither exists.
    /// </summary>
    [Fact]
    public void BasicTables_FindCue_WordTableBeforeLemmaTable()
    {
        // Arrange: a table of the form "left" that spans two lemmas, and the table of the lemma "read"
        var hashes = TestTables.CueHashes();
        CueAnalysis[] leftColumns =
        [
            new("leave", PartOfSpeech.Verb, "Tense=Past", 0),
            new("left", PartOfSpeech.Adjective, string.Empty, 0),
        ];
        var tables = TestTables.Load(TestTables.Build(
            [("record", 0, new int[PosTag.Count])],
            [],
            [("left", leftColumns, hashes, new sbyte[4]), ("read", TestTables.ReadColumns(), hashes, new sbyte[4])]));
        ImmutableArray<WordDetails> leftAnalyses = [TestTables.LeftAdjective, TestTables.LeavePast];
        ImmutableArray<WordDetails> readAnalyses = [TestTables.ReadPres, TestTables.ReadPast];
        ImmutableArray<WordDetails> readFormAnalyses = [TestTables.ReadPast, TestTables.ReadPres];

        // Act
        var byWord = tables.FindCue("left", leftAnalyses);
        var byLemma = tables.FindCue("reads", readAnalyses);
        var byWordFirst = tables.FindCue("read", readFormAnalyses);
        var none = tables.FindCue("record", [TestTables.RecordNoun, TestTables.RecordVerb]);

        // Assert
        Assert.Same(tables.Cues["left"], byWord);
        Assert.Same(tables.Cues["read"], byLemma);
        Assert.Same(tables.Cues["read"], byWordFirst);
        Assert.Null(none);
        Assert.Equal(1, byWord!.RowOf(TestTables.LeftAdjective));
        Assert.Equal(0, byWord.RowOf(TestTables.LeavePast));
    }

    /// <summary>
    ///     The shipped table loads against the shipped word image (the load checks the inventory hash) and every analysis
    ///     it names exists there. It needs the regenerated word image and table.
    /// </summary>
    [Fact]
    public void BasicTables_Default_ShippedTable_ResolvesEveryCueClass()
    {
        var tables = BasicTables.Default;

        Assert.All(
            tables.Cues.Values,
            cue => Assert.All(cue.Analyses, a => Assert.True(WordLexicon.Current.AnalysisExists(a.Lemma, a.PartOfSpeech, a.Features), $"{a.Lemma} {a.PartOfSpeech} '{a.Features}'")));
    }

    /// <summary>
    ///     The hash of an immediate-neighbor feature equals the hash of the window token that spells its prefix, and the hash is stable.
    /// </summary>
    [Fact]
    public void FeatureRef_Hash_PrefixAndDeterminism()
    {
        var left = new FeatureRef(CueModel.Left, "dog").Hash;
        var right = new FeatureRef(CueModel.Right, "dog").Hash;

        Assert.Equal(new FeatureRef(CueModel.Window, "L1:dog").Hash, left);
        Assert.Equal(new FeatureRef(CueModel.Window, "R1:dog").Hash, right);
        Assert.Equal(left, new FeatureRef(CueModel.Left, "dog").Hash);
    }

    /// <summary>
    ///     The feature kinds of the tense model hash with their own prefixes, so the same token in another place has another
    ///     feature, and the prefixes of the cue kinds are unchanged (the cue tables stay valid).
    /// </summary>
    [Fact]
    public void FeatureRef_Hash_TenseKindsHaveOwnPrefixes()
    {
        // Arrange: the same token as a plain window feature and as each kind of the tense model
        var window = new FeatureRef(CueModel.Window, "have").Hash;
        var leftWindow = new FeatureRef(CueModel.LeftWindow, "have").Hash;
        var rightWindow = new FeatureRef(CueModel.RightWindow, "have").Hash;
        var governor = new FeatureRef(CueModel.Governor, "have").Hash;

        // Act
        var prefixed = new[]
        {
            new FeatureRef(CueModel.Window, "LW:have").Hash,
            new FeatureRef(CueModel.Window, "RW:have").Hash,
            new FeatureRef(CueModel.Window, "NA:have").Hash,
            new FeatureRef(CueModel.Window, "L1:have").Hash,
        };

        // Assert: each kind hashes as its prefix and the token, the cue kinds keep theirs, and the kinds differ
        Assert.Equal(prefixed[0], leftWindow);
        Assert.Equal(prefixed[1], rightWindow);
        Assert.Equal(prefixed[2], governor);
        Assert.Equal(prefixed[3], new FeatureRef(CueModel.Left, "have").Hash);
        Assert.Equal(4, new[] { window, leftWindow, rightWindow, governor }.Distinct().Count());
    }

    /// <summary>
    ///     The feature kinds of the noun/verb model hash with their own prefixes: the second neighbors and the classes of the
    ///     neighbors are told apart from the immediate words and from each other, and the cue and tense kinds keep theirs.
    /// </summary>
    [Fact]
    public void FeatureRef_Hash_NounVerbKindsHaveOwnPrefixes()
    {
        // Arrange: the same token in each place of the noun/verb model
        var kinds = new[] { CueModel.Left, CueModel.Right, CueModel.Left2, CueModel.Right2, CueModel.LeftClass, CueModel.Left2Class, CueModel.RightClass, CueModel.Right2Class };
        string[] prefixes = ["L1:", "R1:", "L2:", "R2:", "L1C:", "L2C:", "R1C:", "R2C:"];

        // Act
        var hashes = kinds.Select(kind => new FeatureRef(kind, "noun").Hash).ToArray();
        var prefixed = prefixes.Select(prefix => new FeatureRef(CueModel.Window, prefix + "noun").Hash).ToArray();

        // Assert: each kind hashes as its prefix and the token, and the eight features differ
        Assert.Equal(prefixed, hashes);
        Assert.Equal(8, hashes.Distinct().Count());
    }
}
