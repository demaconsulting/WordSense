using System.Collections.Immutable;
using System.Security.Cryptography;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the training of the basic model and for the writer of its table.
/// </summary>
public class BasicTrainerTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly byte[] ToyHash = SHA256.HashData("builder toy inventory"u8);

    private static readonly Dictionary<string, ImmutableArray<WordDetails>> Lexicon = new(StringComparer.Ordinal)
    {
        ["record"] = [Noun, Verb],
        ["cat"] = [new("cat", PartOfSpeech.Noun, "Number=Sing")],
    };

    private static TaggerCounts Counts()
    {
        var sentences = new List<TaggedSentence>();
        for (var i = 0; i < 6; i++)
        {
            sentences.Add(new TaggedSentence(["the", "cat", "zorped", "the", "record", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]));
            sentences.Add(new TaggedSentence(["they", "record"], [PosTag.Pronoun, PosTag.Verb]));
        }

        return TaggerCounts.Count(sentences);
    }

    private static CorpusRow Row(string lemma, PartOfSpeech pos, params string[] words) =>
        new(lemma, $"{lemma}#{pos.ToString().ToLowerInvariant()}", pos, string.Join(' ', words), "a.tsv", words, Array.IndexOf(words, lemma));

    private static List<CorpusRow> Anchors() =>
    [
        Row("record", PartOfSpeech.Noun, "the", "river", "record", "swims"),
        Row("record", PartOfSpeech.Noun, "a", "river", "record", "bites"),
        Row("record", PartOfSpeech.Verb, "the", "guitar", "record", "thumps"),
        Row("record", PartOfSpeech.Verb, "a", "guitar", "record", "booms"),
        Row("record", PartOfSpeech.Verb, "a", "guitar", "record", "plays", ".", "river", "runs"),
    ];

    private static ChooserRow Chooser(string form, string label, params string[] words) =>
        new(form, label, string.Join(' ', words), "c.tsv", words, Array.IndexOf(words, form));

    private static BasicModelData Train(IReadOnlyList<CorpusRow>? anchors = null, BasicHyperparameters? hp = null, IReadOnlyList<ChooserRow>? choosers = null) =>
        BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, anchors ?? Anchors(), choosers ?? [], hp ?? BasicHyperparameters.Default);

    private static byte[] Write(BasicModelData data) => BasicTablesWriter.Write(data, ToyHash);

    private static BasicTables Read(byte[] blob) => BasicTables.Load(
        blob,
        ToyHash,
        (lemma, pos, _) => Lexicon.Values.SelectMany(a => a).Any(a => a.Lemma == lemma && a.PartOfSpeech == pos));

    /// <summary>
    ///     A word becomes closed-class only with enough occurrences and a dominant closed-class share.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_ClosedMaskOf_AppliesThresholds()
    {
        var hp = BasicHyperparameters.Default;
        var rare = new int[PosTag.Count];
        rare[PosTag.Determiner] = hp.ClosedMinCount - 1;
        var closed = new int[PosTag.Count];
        closed[PosTag.Determiner] = 10;
        var mixed = new int[PosTag.Count];
        mixed[PosTag.Noun] = 8;
        mixed[PosTag.Determiner] = 2;
        var open = new int[PosTag.Count];
        open[PosTag.Noun] = 9;
        open[PosTag.Determiner] = 1;

        Assert.Equal(0, BasicTrainer.ClosedMaskOf(rare, hp));
        Assert.Equal(1 << PosTag.Determiner, BasicTrainer.ClosedMaskOf(closed, hp));
        Assert.Equal(1 << PosTag.Determiner, BasicTrainer.ClosedMaskOf(mixed, hp));
        Assert.Equal(0, BasicTrainer.ClosedMaskOf(open, hp));
    }

    /// <summary>
    ///     The table holds the closed-class words and the ambiguous words, and the suffixes of unlisted words.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_SelectsWordsAndSuffixes()
    {
        var data = Train();

        Assert.Equal(["record", "the"], data.Words.Select(w => w.Word));
        Assert.Equal(0, data.Words.Single(w => w.Word == "record").ClosedMask);
        Assert.Equal(1 << PosTag.Determiner, data.Words.Single(w => w.Word == "the").ClosedMask);
        Assert.Equal([0, 6, 0, 0], data.Suffixes.Single(s => s.Suffix == "ed").Counts);
        Assert.DoesNotContain(data.Words, w => w.Word == "cat");
    }

    /// <summary>
    ///     The cue table of a lemma has a column per analysis and prefers the analysis that its context words occurred with.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_CueTablePrefersSeenAnalysis()
    {
        var data = Train();

        var cue = Assert.Single(data.Cues);
        Assert.Equal("record", cue.Lemma);
        Assert.Equal(
            [("record", PartOfSpeech.Noun, string.Empty), ("record", PartOfSpeech.Verb, string.Empty)],
            cue.Analyses.Select(a => (a.Lemma, a.PartOfSpeech, a.Features)));
        var river = Array.IndexOf(cue.Hashes, new FeatureRef(CueModel.Window, "river").Hash);
        Assert.True(river >= 0);
        Assert.True(cue.Weights[river * 2] > cue.Weights[(river * 2) + 1]);
        Assert.All(cue.Weights, w => Assert.True(w <= 0));
        Assert.Equal(cue.Hashes.Order().ToArray(), cue.Hashes);
    }

    /// <summary>
    ///     The anchors are a balanced classifier corpus, so the number of anchors of an analysis does not make it more
    ///     probable: every column of a cue table has the same prior.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_AnalysisPriorIgnoresAnchorCounts()
    {
        var data = Train();

        var cue = Assert.Single(data.Cues);
        Assert.Equal(cue.Analyses[0].Prior, cue.Analyses[1].Prior);
    }

    /// <summary>
    ///     The columns of a cue table are the lemma and part of speech of the senses of the rows, not the senses: two
    ///     noun senses share one column, and a lemma whose senses are all of one part of speech gets no table.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_ColumnsAreLemmaAndPartOfSpeech()
    {
        List<CorpusRow> anchors =
        [
            Row("record", PartOfSpeech.Noun, "the", "record", "pays"),
            Row("record", PartOfSpeech.Noun, "a", "record", "lends"),
            Row("record", PartOfSpeech.Verb, "they", "record", "left"),
            Row("cat", PartOfSpeech.Noun, "the", "cat", "sleeps"),
            Row("cat", PartOfSpeech.Noun, "a", "cat", "purrs"),
        ];

        var data = Train(anchors);

        var cue = Assert.Single(data.Cues);
        Assert.Equal("record", cue.Lemma);
        Assert.Equal(2, cue.Analyses.Length);
    }

    /// <summary>
    ///     A form-chooser label that names a tense of a verb makes a cue table keyed by the form, with a column per
    ///     tense, and a sense sentence of a lemma with the same key goes to the one column of its slot or is left out when
    ///     the tenses split the slot.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_ChooserRowsMakeFormTablesWithTenseColumns()
    {
        // Arrange: the form read in two tenses, one noun sentence and two verb sentences of the lemma, and a lemma label
        const string past = "read/Tense=Past+VerbForm=Part";
        const string present = "read/Tense=Pres";
        List<ChooserRow> choosers =
        [
            Chooser("read", past, "he", "read", "it", "yesterday"),
            Chooser("read", past, "she", "read", "it", "yesterday"),
            Chooser("read", present, "they", "read", "it", "daily"),
            Chooser("read", present, "we", "read", "it", "daily"),
        ];
        List<CorpusRow> anchors =
        [
            Row("read", PartOfSpeech.Noun, "a", "good", "read", "indeed"),
            Row("read", PartOfSpeech.Verb, "they", "read", "books"),
            Row("read", PartOfSpeech.Verb, "we", "read", "books"),
        ];

        // Act
        var data = Train(anchors, choosers: choosers);

        // Assert: one table keyed by the form with the noun column of the senses and the two tenses; the verb sentences of
        // the senses cannot say which tense and are counted as skipped
        var cue = Assert.Single(data.Cues);
        Assert.Equal("read", cue.Lemma);
        Assert.Equal(
            [("read", PartOfSpeech.Noun, string.Empty), ("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part"), ("read", PartOfSpeech.Verb, "Tense=Pres")],
            cue.Analyses.Select(a => (a.Lemma, a.PartOfSpeech, a.Features)));
        Assert.Equal(2, data.Skipped);
        var yesterday = Array.IndexOf(cue.Hashes, new FeatureRef(CueModel.Window, "yesterday").Hash);
        Assert.True(yesterday >= 0);
        Assert.True(cue.Weights[(yesterday * 3) + 1] > cue.Weights[(yesterday * 3) + 2], "yesterday goes with the past tense.");
    }

    /// <summary>
    ///     A chooser label that stands for no single analysis of the form is left out and counted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_KeyOf_ChooserLabel_StandsForOneAnalysisOrNone()
    {
        var past = BasicTrainer.KeyOf(Chooser("read", "read/Tense=Past+VerbForm=Part", "he", "read"));
        var unknown = BasicTrainer.KeyOf(Chooser("read", "read/Tense=Fut", "he", "read"));
        var split = BasicTrainer.KeyOf(Chooser("record", "record", "the", "record"));
        var single = BasicTrainer.KeyOf(Chooser("went", "go", "he", "went"));

        Assert.Equal(new AnalysisKey("read", PartOfSpeech.Verb, "Tense=Past+VerbForm=Part"), past);
        Assert.Null(unknown);
        Assert.Null(split);
        Assert.Equal(new AnalysisKey("go", PartOfSpeech.Verb, string.Empty), single);
    }

    /// <summary>
    ///     A fine-sense row stands for the analysis of its lemma and part of speech, whatever the features; a key with
    ///     features matches only an analysis that has them.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_KeyOf_RollsTheRowUpToLemmaAndPartOfSpeech()
    {
        var key = BasicTrainer.KeyOf(Row("record", PartOfSpeech.Noun, "the", "record"));

        Assert.Equal(new AnalysisKey("record", PartOfSpeech.Noun, string.Empty), key);
        Assert.True(key.Matches(Noun));
        Assert.False(key.Matches(Verb));
        Assert.True(new AnalysisKey("record", PartOfSpeech.Noun, "Number=Sing").Matches(Noun));
        Assert.False(new AnalysisKey("record", PartOfSpeech.Noun, "Number=Plur").Matches(Noun));
        Assert.Equal("zzyzx", AnalysisKey.Resolve(new AnalysisKey("zzyzx", PartOfSpeech.Noun, string.Empty)).Lemma);
    }

    /// <summary>
    ///     Equal inputs give identical table bytes, and a changed hyper-parameter changes the table header.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack()
    {
        var first = Write(Train());
        var second = Write(Train());
        var other = Write(Train(hp: BasicHyperparameters.Default with { HmmWeight = 0.5, Temperature = 4 }));

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        var tables = Read(first);
        Assert.Equal(1.5, tables.HmmWeight);
        Assert.Equal(0.5, tables.InvTemperature);
        Assert.Equal(0.5, Read(other).HmmWeight);
        Assert.Equal(0.25, Read(other).InvTemperature);
        Assert.True(tables.Words.TryGetValue("the", out var the));
        Assert.NotEqual(0, the.ClosedMask);
        Assert.Equal(2, tables.Cues["record"].Analyses.Length);
        Assert.True(tables.SuffixCount > 0);
    }

    /// <summary>
    ///     The writer rejects an inventory hash that is not 32 bytes long.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_ShortHash_Throws()
    {
        var data = Train();

        Assert.Throws<ArgumentException>(() => BasicTablesWriter.Write(data, new byte[31]));
    }

    /// <summary>
    ///     The summary names the sizes of the trained data.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicModelData_Summary_NamesSizes()
    {
        var summary = Train().Summary();

        Assert.Contains("2 words", summary, StringComparison.Ordinal);
        Assert.Contains("1 cue tables", summary, StringComparison.Ordinal);
        Assert.Contains("0 hash collisions", summary, StringComparison.Ordinal);
        Assert.Contains("0 cue sentences skipped", summary, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tagger score counts the tokens that have several parts of speech in the lexicon and a gold tag among them.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTuner_ScoreTagger_CountsAmbiguousTokens()
    {
        var tables = Read(Write(Train()));
        TaggedSentence[] sentences =
        [
            new(["the", "record", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]),
            new(["the", "cat", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]),
        ];

        var (rows, correct) = BasicTuner.ScoreTagger(tables, sentences, w => Lexicon.TryGetValue(w, out var found) ? found : default);

        Assert.Equal(1, rows);
        Assert.Equal(1, correct);
    }

    /// <summary>
    ///     The anchor split is deterministic, disjoint, and the hash does not change between runs.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTuner_SplitAnchors_IsDeterministicAndDisjoint()
    {
        var anchors = Enumerable.Range(0, 200)
            .Select(i => new CorpusRow("bass", "bass#fish", PartOfSpeech.Noun, $"sentence number {i} bass", "a.tsv", ["sentence", "number", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "bass"], 3))
            .ToList();

        var (train1, dev1) = BasicTuner.SplitAnchors(anchors);
        var (train2, dev2) = BasicTuner.SplitAnchors(anchors);

        Assert.Equal(train1, train2);
        Assert.Equal(dev1, dev2);
        Assert.Empty(train1.Intersect(dev1));
        Assert.Equal(Math.Min(anchors.Count, BasicHyperparameters.AnchorSample), train1.Count + dev1.Count);
        Assert.Equal(BasicTuner.StableHash("record"), BasicTuner.StableHash("record"));
        Assert.True(BasicTuner.StableHash("record") >= 0);
        Assert.NotEqual(BasicTuner.StableHash("record"), BasicTuner.StableHash("Record"));
    }

    /// <summary>
    ///     The tuning reports missing tag counts.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_BasicTuner_RunAsync_MissingCounts_Throws()
    {
        using var dir = new TestDirectory();

        await Assert.ThrowsAnyAsync<IOException>(() => BasicTuner.RunAsync(Path.Join(dir.Root, "corpus"), TextWriter.Null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The hyper-parameters carry the values that the runtime reads and a tuning grid that includes them.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicHyperparameters_Default_IsConsistent()
    {
        var hp = BasicHyperparameters.Default;

        Assert.Equal(1.0, hp.Lambda1 + hp.Lambda2 + hp.Lambda3, 9);
        Assert.Equal(1.0 / hp.Temperature, hp.InvTemperature);
        Assert.NotEmpty(BasicHyperparameters.Grid);
        Assert.All(BasicHyperparameters.Grid, g => Assert.NotEmpty(g.Variants));
    }
}
