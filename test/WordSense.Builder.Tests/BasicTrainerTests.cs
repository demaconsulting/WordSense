using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the training of the basic model and for the writer of its table.
/// </summary>
public class BasicTrainerTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, null, null);
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, null, null);

    private static readonly Dictionary<string, WordDetails[]> Lexicon = new(StringComparer.Ordinal)
    {
        ["record"] = [Noun, Verb],
        ["cat"] = [new("cat", PartOfSpeech.Noun, null, null)],
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

    private static SenseInventory Inventory() => new(
    [
        new InventoryRow("bass", "fish", PartOfSpeech.Noun, "fish", null, "s.tsv"),
        new InventoryRow("bass", "music", PartOfSpeech.Noun, "music", null, "s.tsv"),
    ]);

    private static List<CorpusRow> Anchors()
    {
        CorpusRow Row(string sense, params string[] words) => new("bass", sense, string.Join(' ', words), "a.tsv", words, Array.IndexOf(words, "bass"));
        return
        [
            Row("fish", "the", "river", "bass", "swims"),
            Row("fish", "a", "river", "bass", "bites"),
            Row("music", "the", "guitar", "bass", "thumps"),
            Row("music", "a", "guitar", "bass", "booms"),
            Row("music", "a", "guitar", "bass", "plays", ".", "river", "runs"),
        ];
    }

    private static BasicModelData Train(BasicHyperparameters? hp = null) =>
        BasicTrainer.Train(Counts(), Lexicon, Anchors(), Inventory(), hp ?? BasicHyperparameters.Default);

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
    ///     The cue table of a lemma prefers the sense that its context words occurred with.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_Train_CueTablePrefersSeenSense()
    {
        var data = Train();

        var cue = Assert.Single(data.Cues);
        Assert.Equal("bass", cue.Lemma);
        Assert.Equal(["fish", "music"], cue.Senses.Select(s => s.Meaning!));
        var river = Array.IndexOf(cue.Hashes, new FeatureRef(CueModel.Window, "river").Hash);
        Assert.True(river >= 0);
        Assert.True(cue.Weights[(river * 2)] > cue.Weights[(river * 2) + 1]);
        Assert.All(cue.Weights, w => Assert.True(w <= 0));
        Assert.Equal(cue.Hashes.Order().ToArray(), cue.Hashes);
    }

    /// <summary>
    ///     Equal inputs give identical table bytes, and a changed hyper-parameter changes the table header.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack()
    {
        var first = BasicTablesWriter.Write(Train());
        var second = BasicTablesWriter.Write(Train());
        var other = BasicTablesWriter.Write(Train(BasicHyperparameters.Default with { HmmWeight = 0.5, Temperature = 4 }));

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        var tables = BasicTables.Load(first);
        Assert.Equal(1.5, tables.HmmWeight);
        Assert.Equal(0.5, tables.InvTemperature);
        Assert.Equal(0.5, BasicTables.Load(other).HmmWeight);
        Assert.Equal(0.25, BasicTables.Load(other).InvTemperature);
        Assert.True(tables.Words.ContainsKey("the"));
        Assert.True(tables.Words["the"].ClosedMask != 0);
        Assert.Equal(2, tables.Cues["bass"].Senses.Length);
        Assert.True(tables.SuffixCount > 0);
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
