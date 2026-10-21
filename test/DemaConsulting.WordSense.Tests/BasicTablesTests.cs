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
    ///     The longest known suffix gives a distribution over the open classes; an unknown ending gives none.
    /// </summary>
    [Fact]
    public void BasicTables_FindSuffix_KnownAndUnknown()
    {
        var tables = TestTables.Load(TestTables.Default());

        var known = tables.FindSuffix("walked");

        Assert.NotNull(known);
        Assert.Equal(1.0, known.Sum(), 9);
        Assert.True(known[1] > known[0]);
        Assert.Null(tables.FindSuffix("xyz"));
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
        // Arrange: the tag count is a single-byte varint after the magic, the hash and seven doubles
        var version = TestTables.Decompress(TestTables.Default());
        version[3] = (byte)'2';
        var tags = TestTables.Decompress(TestTables.Default());
        const int tagCountOffset = 4 + 32 + (7 * 8);
        Assert.Equal(PosTag.Count, tags[tagCountOffset]);
        tags[tagCountOffset]++;

        // Act / Assert
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(version))), "Version WSB2.");
        Assert.True(Throws(() => TestTables.Load(TestTables.Compress(tags))), "Tag count.");
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
}
