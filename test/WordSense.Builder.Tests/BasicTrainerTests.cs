using System.Collections.Immutable;
using System.Security.Cryptography;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the training of the basic model and for the writer of its table.
/// </summary>
public class BasicTrainerTests
{
    private const string MigrationHeader = "class_id\tkind\tlemma\tpos\tmeaning\trespelling\tsense_count\tsense_id\tgroup_id\tentry_id\tentry_lemma\tentry_pos\tfeatures\n";

    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly byte[] ToyHash = SHA256.HashData("builder toy inventory"u8);

    private static readonly Dictionary<string, ImmutableArray<WordDetails>> Lexicon = new(StringComparer.Ordinal)
    {
        ["record"] = [Noun, Verb],
        ["cat"] = [new("cat", PartOfSpeech.Noun, "Number=Sing")],
    };

    private static UdCounts Counts()
    {
        var sentences = new List<UdSentence>();
        for (var i = 0; i < 6; i++)
        {
            sentences.Add(new UdSentence(["the", "cat", "zorped", "the", "record", "."], [PosTag.Determiner, PosTag.Noun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Punctuation]));
            sentences.Add(new UdSentence(["they", "record"], [PosTag.Pronoun, PosTag.Verb]));
        }

        return UdCounts.Count(sentences);
    }

    private static LegacySenseInventory Inventory() => new(
    [
        new InventoryRow("record", "noun", PartOfSpeech.Noun, "noun", null, "s.tsv"),
        new InventoryRow("record", "verb", PartOfSpeech.Verb, "verb", null, "s.tsv"),
    ]);

    /// <summary>
    ///     Reads the analysis labels of an inventory and the rows of a legacy class map written to a temporary corpus
    ///     directory, which is deleted again.
    /// </summary>
    /// <param name="inventory">The inventory.</param>
    /// <param name="migrationRows">The rows of the legacy class map below its header.</param>
    /// <returns>The labels.</returns>
    private static AnalysisLabels Labels(LegacySenseInventory inventory, params string[] migrationRows)
    {
        using var dir = new TestDirectory();
        dir.Write(Path.Join("corpus", AnalysisLabels.MigrationFile), MigrationHeader + string.Concat(migrationRows.Select(r => r + "\n")));
        return AnalysisLabels.Load(inventory, Path.Join(dir.Root, "corpus"));
    }

    private static LegacyRow Row(string lemma, string sense, params string[] words) =>
        new(lemma, sense, string.Join(' ', words), "a.tsv", words, Array.IndexOf(words, lemma));

    private static List<LegacyRow> Anchors() =>
    [
        Row("record", "noun", "the", "river", "record", "swims"),
        Row("record", "noun", "a", "river", "record", "bites"),
        Row("record", "verb", "the", "guitar", "record", "thumps"),
        Row("record", "verb", "a", "guitar", "record", "booms"),
        Row("record", "verb", "a", "guitar", "record", "plays", ".", "river", "runs"),
    ];

    private static BasicModelData Train(BasicHyperparameters? hp = null)
    {
        var inventory = Inventory();
        return BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, Anchors(), inventory, Labels(inventory), hp ?? BasicHyperparameters.Default);
    }

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
        Assert.True(cue.Analyses[1].Prior > cue.Analyses[0].Prior, "The verb has three anchors, the noun two.");
        Assert.Equal(cue.Hashes.Order().ToArray(), cue.Hashes);
        Assert.Equal(0, data.SkippedAnchors);
    }

    /// <summary>
    ///     The labels of the anchors are rolled up to analyses: classes of senses stand for the analysis of the lemma and
    ///     part of speech, a class of one tense stands for the verb with that tense, a class that stands for exactly one
    ///     entry of a lemma split stands for that entry, and the anchors of a class that stands for several entries are
    ///     skipped and counted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_AnalysisLabelsAreRolledUpFromSenses()
    {
        // Arrange: a lemma with two classes of senses (two parts of speech), a verb with two tenses, and a form that
        // spans two lemmas with one class that stands for both
        var inventory = new LegacySenseInventory(
        [
            new InventoryRow("bank", "money", PartOfSpeech.Noun, "money", null, "s.tsv"),
            new InventoryRow("bank", "slope", PartOfSpeech.Noun, "slope", null, "s.tsv"),
            new InventoryRow("bank", "tilt", PartOfSpeech.Verb, "tilt", null, "s.tsv"),
            new InventoryRow("read", "past", PartOfSpeech.Verb, "past", null, "s.tsv"),
            new InventoryRow("read", "present", PartOfSpeech.Verb, "present", null, "s.tsv"),
            new InventoryRow("bases", "base", PartOfSpeech.Noun, "base", null, "s.tsv"),
            new InventoryRow("bases", "basis", PartOfSpeech.Noun, "basis", null, "s.tsv"),
            new InventoryRow("bases", "both", PartOfSpeech.Noun, "both", null, "s.tsv"),
        ]);
        var labels = Labels(
            inventory,
            "read#past\tfeature_only\tread\tv\tverb\t\t0\t\t\toewn-read-v\tread\tv\tTense=Past",
            "read#present\tfeature_only\tread\tv\tverb\t\t0\t\t\toewn-read-v\tread\tv\tTense=Pres",
            "bases#base\tlemma_split\tbases\tn\tnoun\t\t0\t\t\toewn-base-n\tbase\tn\t",
            "bases#basis\tlemma_split\tbases\tn\tnoun\t\t0\t\t\toewn-basis-n\tbasis\tn\t",
            "bases#both\tlemma_split\tbases\tn\tnoun\t\t0\t\t\toewn-base-n\tbase\tn\t",
            "bases#both\tlemma_split\tbases\tn\tnoun\t\t0\t\t\toewn-basis-n\tbasis\tn\t");
        List<LegacyRow> anchors =
        [
            Row("bank", "money", "the", "bank", "pays"),
            Row("bank", "money", "a", "bank", "lends"),
            Row("bank", "slope", "the", "bank", "erodes"),
            Row("bank", "tilt", "they", "bank", "left"),
            Row("read", "past", "he", "read", "yesterday"),
            Row("read", "past", "she", "read", "yesterday"),
            Row("read", "present", "he", "read", "tomorrow"),
            Row("bases", "base", "the", "bases", "ached"),
            Row("bases", "basis", "the", "bases", "argue"),
            Row("bases", "both", "the", "bases", "of"),
            Row("bases", "both", "all", "bases", "covered"),
        ];

        // Act
        var data = BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, anchors, inventory, labels, BasicHyperparameters.Default);

        // Assert: one table per lemma in ordinal order, with the columns the labels roll up to
        Assert.Equal(["bank", "bases", "read"], data.Cues.Select(c => c.Lemma));
        Assert.Equal(
            [("bank", PartOfSpeech.Noun, string.Empty), ("bank", PartOfSpeech.Verb, string.Empty)],
            data.Cues[0].Analyses.Select(a => (a.Lemma, a.PartOfSpeech, a.Features)));
        Assert.Equal(
            [("base", PartOfSpeech.Noun, string.Empty), ("basis", PartOfSpeech.Noun, string.Empty)],
            data.Cues[1].Analyses.Select(a => (a.Lemma, a.PartOfSpeech, a.Features)));
        Assert.Equal(
            [("read", PartOfSpeech.Verb, "Tense=Past"), ("read", PartOfSpeech.Verb, "Tense=Pres")],
            data.Cues[2].Analyses.Select(a => (a.Lemma, a.PartOfSpeech, a.Features)));

        // The two anchors of the class that stands for two entries are skipped and counted; the others are counted by analysis
        Assert.Equal(2, data.SkippedAnchors);
        Assert.Contains("2 anchors skipped", data.Summary(), StringComparison.Ordinal);
        Assert.True(data.Cues[0].Analyses[0].Prior > data.Cues[0].Analyses[1].Prior, "Three bank anchors are nouns, one is a verb.");
        Assert.True(data.Cues[2].Analyses[0].Prior > data.Cues[2].Analyses[1].Prior, "Two read anchors are past, one is present.");
    }

    /// <summary>
    ///     A lemma that has fewer than two distinct analyses gets no cue table.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_SingleAnalysisLemma_HasNoCueTable()
    {
        var inventory = new LegacySenseInventory(
        [
            new InventoryRow("bank", "money", PartOfSpeech.Noun, "money", null, "s.tsv"),
            new InventoryRow("bank", "slope", PartOfSpeech.Noun, "slope", null, "s.tsv"),
        ]);
        List<LegacyRow> anchors = [Row("bank", "money", "the", "bank", "pays"), Row("bank", "slope", "the", "bank", "erodes")];

        var data = BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, anchors, inventory, Labels(inventory), BasicHyperparameters.Default);

        Assert.Empty(data.Cues);
    }

    /// <summary>
    ///     An anchor whose label the inventory does not know is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_UnknownLabel_Throws()
    {
        var inventory = Inventory();
        var labels = Labels(inventory);
        List<LegacyRow> anchors = [.. Anchors(), Row("record", "bogus", "the", "record")];

        Assert.Throws<InvalidDataException>(() =>
            BasicTrainer.Train(Counts(), w => Lexicon.TryGetValue(w, out var found) ? found : default, anchors, inventory, labels, BasicHyperparameters.Default));
    }

    /// <summary>
    ///     Equal inputs give identical table bytes, and a changed hyper-parameter changes the table header.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack()
    {
        var first = Write(Train());
        var second = Write(Train());
        var other = Write(Train(BasicHyperparameters.Default with { HmmWeight = 0.5, Temperature = 4 }));

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
        Assert.Contains("1 cue lemmas", summary, StringComparison.Ordinal);
        Assert.Contains("0 anchors skipped", summary, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The anchor split is deterministic, disjoint, and the hash does not change between runs.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTuner_SplitAnchors_IsDeterministicAndDisjoint()
    {
        var anchors = Enumerable.Range(0, 200)
            .Select(i => new LegacyRow("bass", "fish", $"sentence number {i} bass", "a.tsv", ["sentence", "number", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "bass"], 3))
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
    ///     The tuning reports a missing treebank development file.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_BasicTuner_RunAsync_MissingDevFile_Throws()
    {
        using var dir = new TestDirectory();
        var corpus = Path.Join(dir.Root, "corpus");
        UdCounts.Count([new UdSentence(["the", "cat"], [PosTag.Determiner, PosTag.Noun])]).Write(Path.Join(corpus, "ud"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => BasicTuner.RunAsync(corpus, Path.Join(dir.Root, "cache"), TextWriter.Null, TestContext.Current.CancellationToken));
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
