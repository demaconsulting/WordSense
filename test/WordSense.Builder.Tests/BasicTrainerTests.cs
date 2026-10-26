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

        return TaggerCounts.Count(sentences, _ => default);
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
        // Arrange
        var hp = BasicHyperparameters.Default;

        // Act
        var lambdas = hp.Lambda1 + hp.Lambda2 + hp.Lambda3;

        // Assert
        Assert.Equal(1.0, lambdas, 9);
        Assert.Equal(1.0 / hp.Temperature, hp.InvTemperature);
        Assert.NotEmpty(BasicHyperparameters.Grid);
        Assert.All(BasicHyperparameters.Grid, g => Assert.NotEmpty(g.Variants));
        Assert.Contains(hp.TenseWeight, BasicHyperparameters.TenseWeightGrid);
        Assert.True(hp.TenseWeight > 0);
    }

    private static TaggedSentence VerbSentence(string[] words, int index, string features)
    {
        var tags = new int[words.Length];
        Array.Fill(tags, PosTag.Pronoun);
        tags[index] = PosTag.Verb;
        var gold = new GoldToken[words.Length];
        Array.Fill(gold, new GoldToken("x", string.Empty, string.Empty));
        gold[index] = new GoldToken("v", string.Empty, features);
        return new TaggedSentence(words, tags, gold);
    }

    private const string PastFeatures = "Mood=Ind|Tense=Past|VerbForm=Fin";

    /// <summary>
    ///     The class that the tense table learns: past for every past tense and participle, non-past for the infinitive, the
    ///     imperative and the present of every person but the third singular, and no class for the forms that are spelled apart
    ///     from the past (third person singular, gerund, present participle) and for a token without features.
    /// </summary>
    /// <param name="features">The features of the open corpus.</param>
    /// <param name="expected">The class: 1 past, 0 non-past, -1 none.</param>
    [Theory]
    [InlineData("Mood=Ind|Tense=Past|VerbForm=Fin", 1)]
    [InlineData("Tense=Past|VerbForm=Part", 1)]
    [InlineData("Mood=Ind|Number=Sing|Person=3|Tense=Past|VerbForm=Fin", 1)]
    [InlineData("VerbForm=Inf", 0)]
    [InlineData("Mood=Imp|VerbForm=Fin", 0)]
    [InlineData("Mood=Ind|Number=Plur|Person=3|Tense=Pres|VerbForm=Fin", 0)]
    [InlineData("Mood=Ind|Number=Sing|Person=1|Tense=Pres|VerbForm=Fin", 0)]
    [InlineData("Mood=Ind|Person=2|Tense=Pres|VerbForm=Fin", 0)]
    [InlineData("Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin", -1)]
    [InlineData("VerbForm=Ger", -1)]
    [InlineData("Tense=Pres|VerbForm=Part", -1)]
    [InlineData("", -1)]
    public void WordSenseBuilder_BasicTrainer_TenseClassOf_SeparatesPastFromTheFormsThatShareItsSpelling(string features, int expected)
    {
        // Arrange: the features of the theory row on a gold token
        var token = new GoldToken("v", string.Empty, features);

        // Act
        var actual = BasicTrainer.TenseClassOf(token);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     The tense table learns from the context of every verb, whatever the verb: a feature of the past context gets a
    ///     positive weight and a feature of the infinitive context a negative one, the prior is the log odds of the classes, and
    ///     the third person singular does not count.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_TrainTense_LearnsTheContextOfPastAndNonPast()
    {
        // Arrange: "had X" is past in six different verbs, "to Y" is the infinitive in six others, "she Z" is a third singular
        // present that is left out, and a sentence without gold and a non-verb are skipped
        var sentences = new List<TaggedSentence>();
        foreach (var verb in new[] { "walked", "put", "ran", "cut", "came", "set" })
        {
            sentences.Add(VerbSentence(["they", "had", verb], 2, "Tense=Past|VerbForm=Part"));
            sentences.Add(VerbSentence(["they", "to", verb], 2, "VerbForm=Inf"));
            sentences.Add(VerbSentence(["she", verb], 1, "Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin"));
        }

        sentences.Add(new TaggedSentence(["a", "b"], [PosTag.Verb, PosTag.Verb]));
        sentences.Add(new TaggedSentence(["they", "had", "a"], [PosTag.Pronoun, PosTag.Verb, PosTag.Noun], [new("x", string.Empty, string.Empty), new("v", string.Empty, PastFeatures), new("x", string.Empty, string.Empty)]));
        sentences.Add(VerbSentence(["they", "had", "walked", "a", "mile"], 3, "VerbForm=Inf") with { Tags = [PosTag.Pronoun, PosTag.Pronoun, PosTag.Pronoun, PosTag.Noun, PosTag.Noun] });

        // Act
        var table = BasicTrainer.TrainTense(sentences, BasicHyperparameters.Default);

        // Assert: seven past (six plus the "had a" verb) and six non-past
        Assert.NotNull(table);
        Assert.Equal((7, 6), (table.Past, table.NonPast));
        Assert.Equal((sbyte)Math.Round(Math.Log(7.0 / 6.0) / CueModel.Quantum), table.Prior);
        Assert.Equal(table.Hashes.Order().ToArray(), table.Hashes);
        Assert.Equal(table.Hashes.Length, table.Weights.Length);
        var had = Array.IndexOf(table.Hashes, new FeatureRef(CueModel.Left, "had").Hash);
        var to = Array.IndexOf(table.Hashes, new FeatureRef(CueModel.Left, "to").Hash);
        var she = Array.IndexOf(table.Hashes, new FeatureRef(CueModel.Left, "she").Hash);
        Assert.True(had >= 0 && to >= 0);
        Assert.True(table.Weights[had] > 0);
        Assert.True(table.Weights[to] < 0);
        Assert.True(she < 0, "A third-person singular present is no training verb, so its context is not learned.");
    }

    /// <summary>
    ///     There is no table without both classes or without a feature that occurs often enough, and the training is
    ///     deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_TrainTense_NeedsBothClassesAndIsDeterministic()
    {
        // Arrange
        TaggedSentence[] pastOnly = [VerbSentence(["they", "had", "walked"], 2, PastFeatures)];
        List<TaggedSentence> both =
        [
            VerbSentence(["they", "had", "walked"], 2, PastFeatures),
            VerbSentence(["they", "had", "talked"], 2, PastFeatures),
            VerbSentence(["we", "to", "walk"], 2, "VerbForm=Inf"),
            VerbSentence(["we", "to", "talk"], 2, "VerbForm=Inf"),
        ];

        // Act
        var none = BasicTrainer.TrainTense(pastOnly, BasicHyperparameters.Default);
        var first = BasicTrainer.TrainTense(both, BasicHyperparameters.Default);
        var again = BasicTrainer.TrainTense(both, BasicHyperparameters.Default);
        var strict = BasicTrainer.TrainTense(both, BasicHyperparameters.Default with { TenseMinFeatureCount = 5 });

        // Assert: with a minimum of five occurrences no feature of these four verbs is learned, so there is no table
        Assert.Null(none);
        Assert.NotNull(first);
        Assert.NotNull(again);
        Assert.Equal(first.Hashes, again.Hashes);
        Assert.Equal(first.Weights, again.Weights);
        Assert.Null(strict);
    }

    /// <summary>
    ///     A model trained with verbs carries the tense table and the weight of the model in the table it writes, and the
    ///     reader gives both back; a model trained without verbs has none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_WithVerbs_WritesTheTenseTableAndWeight()
    {
        // Arrange
        List<TaggedSentence> verbs =
        [
            VerbSentence(["they", "had", "walked"], 2, PastFeatures),
            VerbSentence(["they", "had", "talked"], 2, PastFeatures),
            VerbSentence(["we", "to", "walk"], 2, "VerbForm=Inf"),
            VerbSentence(["we", "to", "talk"], 2, "VerbForm=Inf"),
        ];
        var hp = BasicHyperparameters.Default with { TenseWeight = 0.5 };

        // Act
        var withVerbs = BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, Anchors(), [], hp, verbs);
        var without = Train();
        var tables = Read(Write(withVerbs));
        var plain = Read(Write(without));

        // Assert
        Assert.NotNull(withVerbs.Tense);
        Assert.Contains("tense table", withVerbs.Summary(), StringComparison.Ordinal);
        Assert.Equal(0.5, tables.TenseWeight);
        var tense = Assert.IsType<TenseCue>(tables.Tense);
        Assert.Equal(withVerbs.Tense.Hashes, tense.Hashes.ToArray());
        Assert.Equal(withVerbs.Tense.Weights, tense.Weights.ToArray());
        Assert.Equal(withVerbs.Tense.Prior, tense.Prior);
        Assert.Null(without.Tense);
        Assert.Null(plain.Tense);
        Assert.DoesNotContain("tense table", without.Summary(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The same data gives the same bytes: the table with the tense section is reproducible, and it starts with the
    ///     current magic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_WithTenseTable_IsDeterministicAndCurrentFormat()
    {
        // Arrange: verbs of the training split with a past and a non-past context, and trained data that include them
        List<TaggedSentence> verbs = [VerbSentence(["they", "had", "walked"], 2, PastFeatures), VerbSentence(["we", "to", "walk"], 2, "VerbForm=Inf")];
        var data = BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, Anchors(), [], BasicHyperparameters.Default, verbs);

        // Act: write the table twice and read the magic of the first
        var first = Write(data);
        var second = Write(data);
        using var input = new MemoryStream(first);
        using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
        var head = new byte[4];
        brotli.ReadExactly(head);

        // Assert: the two writes are equal and the magic is WSB6
        Assert.Equal(first, second);
        Assert.Equal("WSB6"u8.ToArray(), head);
    }

    /// <summary>
    ///     The clause counts travel from the counts through the data and the writer into the table that the runtime reads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_CarriesTheClauseCounts()
    {
        // Arrange: the toy counts, in which the second determiner of "the cat zorped the record" follows a verb
        var counts = Counts();

        // Act
        var data = BasicTrainer.Train(counts, w => Lexicon.TryGetValue(w, out var found) ? found : default, Anchors(), [], BasicHyperparameters.Default);
        var tables = Read(Write(data));

        // Assert: the table has the clause counts of the toy counts, and a determiner after a verb is counted in the clause
        // state with a verb and not at the start of a sentence
        Assert.Equal(counts.ClauseBigrams, tables.ClauseBigrams.ToArray());
        Assert.Equal(counts.ClauseTrigrams, tables.ClauseTrigrams.ToArray());
        Assert.Equal(counts.ClauseBigrams, data.ClauseBigrams);
        Assert.True(tables.ClauseBigrams[(PosTag.Verb * PosTag.Extended) + PosTag.Determiner] > 0);
        Assert.Equal(0, tables.ClauseBigrams[(PosTag.Boundary * PosTag.Extended) + PosTag.Determiner]);
    }

    /// <summary>
    ///     The smoothing of the clause state, a hyper-parameter, is written to the header of the table that the runtime
    ///     reads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_CarriesTheClauseSmoothing()
    {
        // Arrange: hyper-parameters with a smoothing that is not the default
        var hp = BasicHyperparameters.Default with { ClauseSmoothing = 7.5 };

        // Act
        var data = BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, Anchors(), [], hp);
        var tables = Read(Write(data));

        // Assert
        Assert.Equal(7.5, tables.ClauseSmoothing);
    }

    /// <summary>
    ///     The table starts with the magic <c>WSB6</c>, the format revision that has the word prior and the reliability of
    ///     the cue tables.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_StartsWithTheMagicWsb6()
    {
        // Arrange: trained data
        var data = Train();

        // Act: decompress the first four bytes of the table
        using var input = new MemoryStream(Write(data));
        using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
        var head = new byte[4];
        brotli.ReadExactly(head);

        // Assert: the magic is WSB6
        Assert.Equal("WSB6"u8.ToArray(), head);
    }

    /// <summary>
    ///     The same data with clause counts gives the same bytes each time it is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_WithClauseCounts_IsDeterministic()
    {
        // Arrange: trained data whose counts have clause counts
        var data = Train();
        Assert.Contains(data.ClauseBigrams, count => count > 0);

        // Act
        var first = Write(data);
        var second = Write(data);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     The grid of the tuner includes the smoothing of the clause state, and the default is one of its values.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicHyperparameters_ClauseSmoothing_IsTuned()
    {
        // Arrange: the grid of the smoothing of the clause state
        var grid = BasicHyperparameters.Grid.Single(g => g.Name == "clause-smoothing");

        // Act
        var values = grid.Variants.Select(v => v.ClauseSmoothing).ToArray();

        // Assert: the default is a grid value and no value is negative
        Assert.Contains(BasicHyperparameters.Default.ClauseSmoothing, values);
        Assert.All(values, value => Assert.True(value >= 0));
    }

    /// <summary>
    ///     The numbers of the word prior are written to the header of the table that the runtime reads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_CarriesTheWordPrior()
    {
        // Arrange: hyper-parameters with a word prior that is not the default
        var hp = BasicHyperparameters.Default with { PriorShare = 0.35, PriorSmoothing = 0.75, PriorSenses = 0.125, PriorExponent = 0.5 };

        // Act: write the table and read it back
        var tables = Read(Write(Train(hp: hp)));

        // Assert: the four numbers are in the header
        Assert.Equal(0.35, tables.PriorShare);
        Assert.Equal(0.75, tables.PriorSmoothing);
        Assert.Equal(0.125, tables.PriorSenses);
        Assert.Equal(0.5, tables.PriorExponent);
    }

    /// <summary>
    ///     The shipped hyper-parameters have a word prior that is on and that the runtime accepts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicHyperparameters_WordPrior_IsOnAndAccepted()
    {
        // Arrange: the shipped hyper-parameters
        var hp = BasicHyperparameters.Default;

        // Act: write the table and read it back
        var tables = Read(Write(Train(hp: hp)));

        // Assert: the prior is on, its values are valid, and the runtime loaded the table with the share
        Assert.True(hp.PriorShare is > 0 and <= 1);
        Assert.True(hp.PriorSmoothing + hp.PriorSenses > 0);
        Assert.Equal(hp.PriorShare, tables.PriorShare);
    }

    /// <summary>
    ///     The grid of the word prior has the values without the prior as its baseline.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicHyperparameters_PriorGrid_HasTheBaseline()
    {
        // Arrange: the grid of the word prior

        // Act: list the variants of the grid
        var variants = BasicHyperparameters.PriorGrid.SelectMany(g => g.Variants).ToList();

        // Assert: the baseline has no prior and is a variant of the grid, and no row of the grid is empty
        Assert.Equal(0.0, BasicHyperparameters.Previous.PriorShare);
        Assert.Contains(BasicHyperparameters.Previous, variants);
        Assert.All(BasicHyperparameters.PriorGrid, g => Assert.NotEmpty(g.Variants));
    }

    /// <summary>
    ///     The shipped hyper-parameters set no limit on the number of ambiguous words that are tabled.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicHyperparameters_TopAmbiguousWords_IsUnlimitedByDefault()
    {
        // Arrange: the shipped hyper-parameters
        var hp = BasicHyperparameters.Default;

        // Act: read the limit
        var limit = hp.TopAmbiguousWords;

        // Assert: the limit is the largest integer, which sets none
        Assert.Equal(int.MaxValue, limit);
    }

    /// <summary>
    ///     Without a limit the ambiguous word of the counts is tabled with the closed-class word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_NoLimit_TablesEveryAmbiguousWord()
    {
        // Arrange: no limit on the number of ambiguous words
        var unlimited = BasicHyperparameters.Default with { TopAmbiguousWords = int.MaxValue };

        // Act: train with no limit
        var all = Train(hp: unlimited);

        // Assert: the ambiguous word and the closed-class word are tabled
        Assert.Equal(["record", "the"], all.Words.Select(w => w.Word));
    }

    /// <summary>
    ///     With a limit of zero no ambiguous word is tabled, only the closed-class words are.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_LimitZero_TablesNoAmbiguousWord()
    {
        // Arrange: a limit of zero
        var zero = BasicHyperparameters.Default with { TopAmbiguousWords = 0 };

        // Act: train with a limit of zero
        var none = Train(hp: zero);

        // Assert: only the closed-class word is tabled
        Assert.Equal(["the"], none.Words.Select(w => w.Word));
    }

    /// <summary>
    ///     With a limit between zero and the number of ambiguous words the most frequent words are tabled, and words of the
    ///     same frequency are told apart by their spelling.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_TopAmbiguousWordsBetween_KeepsTheMostFrequentThenBySpelling()
    {
        // Arrange: four ambiguous words with 5, 3, 3 and 1 occurrences; beta and alpha tie for the second place
        var sentences = new List<TaggedSentence>();
        foreach (var (word, occurrences) in new[] { ("gamma", 5), ("beta", 3), ("alpha", 3), ("delta", 1) })
        {
            sentences.AddRange(Enumerable.Repeat(new TaggedSentence([word], [PosTag.Noun]), occurrences));
        }

        var lexicon = new Dictionary<string, ImmutableArray<WordDetails>>(StringComparer.Ordinal);
        foreach (var word in new[] { "gamma", "beta", "alpha", "delta" })
        {
            lexicon[word] = [new(word, PartOfSpeech.Noun, "Number=Sing"), new(word, PartOfSpeech.Verb, "VerbForm=Inf")];
        }

        ImmutableArray<WordDetails> Lookup(string word) => lexicon.TryGetValue(word, out var found) ? found : default;
        var counts = TaggerCounts.Count(sentences, Lookup);

        // Act: select with a limit of two and of three, and select again with a limit of two
        var two = BasicTrainer.Train(counts, Lookup, [], [], BasicHyperparameters.Default with { TopAmbiguousWords = 2 });
        var again = BasicTrainer.Train(counts, Lookup, [], [], BasicHyperparameters.Default with { TopAmbiguousWords = 2 });
        var three = BasicTrainer.Train(counts, Lookup, [], [], BasicHyperparameters.Default with { TopAmbiguousWords = 3 });

        // Assert: gamma is the most frequent; the tie of alpha and beta is broken by spelling, alpha first, and the same
        // counts give the same selection
        Assert.Equal(["alpha", "gamma"], two.Words.Select(w => w.Word));
        Assert.Equal(two.Words.Select(w => w.Word), again.Words.Select(w => w.Word));
        Assert.Equal(["alpha", "beta", "gamma"], three.Words.Select(w => w.Word));
    }

    /// <summary>
    ///     The reliability of a cue table is 255 at or above the target accuracy, 0 at chance and in proportion to the skill
    ///     over chance between, and the damping is off without a target or without a measurement.
    /// </summary>
    /// <param name="accuracy">The balanced held-out accuracy.</param>
    /// <param name="columns">The number of analyses of the table.</param>
    /// <param name="target">The target accuracy.</param>
    /// <param name="expected">The reliability byte.</param>
    [Theory]
    [InlineData(0.95, 2, 0.9, 255)]
    [InlineData(0.9, 2, 0.9, 255)]
    [InlineData(0.75, 2, 1.0, 128)]
    [InlineData(0.5, 2, 0.9, 0)]
    [InlineData(0.4, 2, 0.9, 0)]
    [InlineData(0.8, 4, 0.9, 216)]
    [InlineData(0.6, 2, 0.0, 255)]
    [InlineData(double.NaN, 2, 0.9, 255)]
    [InlineData(0.6, 2, 0.4, 255)]
    public void WordSenseBuilder_BasicTrainer_ReliabilityOf_ScalesTheSkillOverChance(double accuracy, int columns, double target, int expected)
    {
        // Arrange: the accuracy, the number of analyses and the target are the parameters of the case

        // Act: compute the reliability of the case
        var reliability = BasicTrainer.ReliabilityOf(accuracy, columns, target);

        // Assert: the reliability byte is the expected one
        Assert.Equal((byte)expected, reliability);
    }

    private static List<CorpusRow> SeparableAnchors() =>
    [
        .. Enumerable.Range(0, 20).Select(i => Row("record", PartOfSpeech.Noun, "the", "river", "record", $"swims{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
        .. Enumerable.Range(0, 20).Select(i => Row("record", PartOfSpeech.Verb, "a", "guitar", "record", $"plays{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
    ];

    private static List<CorpusRow> NoiseAnchors() =>
    [
        .. Enumerable.Range(0, 20).Select(i => Row("record", PartOfSpeech.Noun, $"n{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}a", $"n{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}b", "record", $"n{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}c")),
        .. Enumerable.Range(0, 20).Select(i => Row("record", PartOfSpeech.Verb, $"v{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}a", $"v{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}b", "record", $"v{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}c")),
    ];

    /// <summary>
    ///     The accuracy of a cue table is measured on sentences that the table was not trained on: a table whose context
    ///     features separate its analyses predicts them, and a table whose sentences share no feature predicts them at
    ///     chance, which it could not do on its own training sentences.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_HeldOutAccuracy_IsMeasuredOnSentencesOutsideTheTable()
    {
        // Arrange: the damping is on at a target of 0.9
        var hp = BasicHyperparameters.Default with { CueSkillTarget = 0.9 };

        // Act: train a table with separable features and one with features that share nothing
        var separable = Assert.Single(Train(SeparableAnchors(), hp).Cues);
        var noise = Assert.Single(Train(NoiseAnchors(), hp).Cues);

        // Assert: the held-out accuracy is high for the first table and at chance for the second
        Assert.True(separable.HeldOutAccuracy >= 0.95);
        Assert.Equal(0.5, noise.HeldOutAccuracy, 9);
        Assert.Equal(40, noise.Sentences);
    }

    /// <summary>
    ///     A table whose held-out accuracy reaches the target is fully reliable.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_AccurateTable_IsFullyReliable()
    {
        // Arrange: a target of 0.9 and anchors whose context features separate the analyses
        var hp = BasicHyperparameters.Default with { CueSkillTarget = 0.9 };

        // Act: train the table with separable features
        var separable = Assert.Single(Train(SeparableAnchors(), hp).Cues);

        // Assert: the table is fully reliable
        Assert.Equal((byte)255, separable.Reliability);
    }

    /// <summary>
    ///     A table that predicts its held-out sentences at chance has a reliability of 0.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_TableAtChance_HasNoReliability()
    {
        // Arrange: a target of 0.9 and anchors whose sentences share no feature
        var hp = BasicHyperparameters.Default with { CueSkillTarget = 0.9 };

        // Act: train the table with sentences that share no feature
        var noise = Assert.Single(Train(NoiseAnchors(), hp).Cues);

        // Assert: the table has no reliability
        Assert.Equal((byte)0, noise.Reliability);
    }

    /// <summary>
    ///     With the damping off every table is fully reliable, whatever its held-out accuracy.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_DampingOff_LeavesTheTableFullyReliable()
    {
        // Arrange: a target of 0, which switches the damping off, and anchors whose sentences share no feature
        var hp = BasicHyperparameters.Default with { CueSkillTarget = 0.0 };

        // Act: train the table with the damping off
        var off = Assert.Single(Train(NoiseAnchors(), hp).Cues);

        // Assert: the table is fully reliable
        Assert.Equal((byte)255, off.Reliability);
    }

    /// <summary>
    ///     The reliability of a table is written to the table that the runtime reads, which scores the table with it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_CarriesTheCueReliability()
    {
        // Arrange: trained data whose only table is damped to 0
        var data = Train(NoiseAnchors(), BasicHyperparameters.Default with { CueSkillTarget = 0.9 });

        // Act: write the table and read it back
        var tables = Read(Write(data));

        // Assert: the reliability is in the table that the runtime reads
        Assert.Equal((byte)0, tables.Cues["record"].ReliabilityByte);
        Assert.Equal(0.0, tables.Cues["record"].Reliability);
    }

    /// <summary>
    ///     The tuner refuses a section that it does not know.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_BasicTuner_RunAsync_UnknownSection_Throws()
    {
        // Arrange: an empty corpus directory and a section that does not exist
        using var dir = new TestDirectory();

        // Act: run the tuner with the unknown section
        var run = () => BasicTuner.RunAsync(Path.Join(dir.Root, "corpus"), TextWriter.Null, TestContext.Current.CancellationToken, null, "bogus");

        // Assert: the tuner throws before it reads anything
        await Assert.ThrowsAsync<ArgumentException>(run);
    }
}
