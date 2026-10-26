using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the running-text reliability of the cue tables of the basic model.
/// </summary>
public class CueRunningReliabilityTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly byte[] ToyHash = SHA256.HashData("running toy inventory"u8);

    private static readonly Dictionary<string, ImmutableArray<WordDetails>> Lexicon = new(StringComparer.Ordinal)
    {
        ["record"] = [Noun, Verb],
    };

    private static ImmutableArray<WordDetails> Lookup(string word) => Lexicon.TryGetValue(word, out var found) ? found : default;

    private static readonly CueRunningRuntime Toy = new(
        data => BasicTables.Load(
            BasicTablesWriter.Write(data, ToyHash, CompressionLevel.Fastest),
            ToyHash,
            (lemma, pos, _) => Lexicon.Values.SelectMany(a => a).Any(a => a.Lemma == lemma && a.PartOfSpeech == pos)),
        tables => new BasicClassifier(tables, Lookup, (_, _) => 1, (_, _) => ImmutableArray<SenseInfo>.Empty));

    /// <summary>
    ///     The training sentences: <c>record</c> is a noun after <c>river</c> and a verb after <c>guitar</c>, half each, so
    ///     the tagger alone cannot tell the two apart.
    /// </summary>
    private static List<TaggedSentence> Sentences()
    {
        var sentences = new List<TaggedSentence>();
        for (var i = 0; i < 40; i++)
        {
            var unique = "w" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sentences.Add(new TaggedSentence(["the", "river", "record", "swims", unique], [PosTag.Determiner, PosTag.Noun, PosTag.Noun, PosTag.Verb, PosTag.Noun]));
            sentences.Add(new TaggedSentence(["the", "guitar", "record", "thumps", unique], [PosTag.Determiner, PosTag.Noun, PosTag.Verb, PosTag.Verb, PosTag.Noun]));
        }

        return sentences;
    }

    private static CorpusRow Row(PartOfSpeech pos, params string[] words) =>
        new("record", $"record#{pos.ToString().ToLowerInvariant()}", pos, string.Join(' ', words), "a.tsv", words, Array.IndexOf(words, "record"));

    /// <summary>
    ///     Anchors whose context says the truth (<c>river</c> noun, <c>guitar</c> verb) or its opposite.
    /// </summary>
    private static List<CorpusRow> Anchors(bool truthful)
    {
        var (first, second) = truthful ? (PartOfSpeech.Noun, PartOfSpeech.Verb) : (PartOfSpeech.Verb, PartOfSpeech.Noun);
        return
        [
            Row(first, "the", "river", "record", "swims"),
            Row(first, "a", "river", "record", "bites"),
            Row(first, "that", "river", "record", "runs"),
            Row(second, "the", "guitar", "record", "thumps"),
            Row(second, "a", "guitar", "record", "booms"),
            Row(second, "that", "guitar", "record", "plays"),
        ];
    }

    private static BasicHyperparameters Hyperparameters(int folds = 2, double pseudo = 1.0, bool minimum = false) =>
        BasicHyperparameters.Default with { PriorShare = 0.0, RunningFolds = folds, RunningPseudo = pseudo, RunningMinimum = minimum, CueSkillTarget = 0.0 };

    private static BasicModelData Train(bool truthful, BasicHyperparameters hp) =>
        BasicTrainer.Train(TaggerCounts.Count(Sentences(), Lookup), Lookup, Anchors(truthful), [], hp);

    /// <summary>
    ///     A table that helps at least as often as it hurts keeps its factor of 1, and the pseudo count pulls a few tokens
    ///     toward 1.
    /// </summary>
    /// <param name="helped">The tokens turned right.</param>
    /// <param name="hurt">The tokens turned wrong.</param>
    /// <param name="pseudo">The pseudo count.</param>
    /// <param name="expected">The factor.</param>
    [Theory]
    [InlineData(5, 3, 5.0, 1.0)]
    [InlineData(0, 0, 5.0, 1.0)]
    [InlineData(4, 4, 1.0, 1.0)]
    [InlineData(0, 10, 5.0, 5.0 / 15.0)]
    [InlineData(2, 10, 2.0, 4.0 / 12.0)]
    [InlineData(0, 1, 5.0, 5.0 / 6.0)]
    [InlineData(0, 4, 0.0, 0.0)]
    public void WordSenseBuilder_CueRunningReliability_FactorOf_ShrinksTheNetBenefitTowardOne(int helped, int hurt, double pseudo, double expected)
    {
        // Arrange: the counts of the helped and hurt tokens and the pseudo count are the parameters of the case

        // Act: compute the factor
        var factor = CueRunningReliability.FactorOf(helped, hurt, pseudo);

        // Assert: the factor is the expected shrunk ratio, at most 1
        Assert.Equal(expected, factor, 9);
    }

    /// <summary>
    ///     The reliability of the anchors and the running-text factor combine as a product or as the smaller of the two.
    /// </summary>
    /// <param name="anchor">The reliability of the anchors.</param>
    /// <param name="factor">The running-text factor.</param>
    /// <param name="minimum">True for the smaller of the two.</param>
    /// <param name="expected">The combined reliability.</param>
    [Theory]
    [InlineData(255, 0.5, false, 128)]
    [InlineData(255, 0.5, true, 128)]
    [InlineData(100, 0.5, false, 50)]
    [InlineData(100, 0.5, true, 100)]
    [InlineData(200, 0.5, true, 128)]
    [InlineData(0, 1.0, false, 0)]
    [InlineData(255, 2.0, false, 255)]
    public void WordSenseBuilder_CueRunningReliability_Combine_UsesTheProductOrTheMinimum(int anchor, double factor, bool minimum, int expected)
    {
        // Arrange: the anchor reliability, the factor and the combination rule are the parameters of the case

        // Act: combine the anchor reliability with the factor
        var combined = CueRunningReliability.Combine((byte)anchor, factor, minimum);

        // Assert: the product, or the smaller of the two, rounded to a byte and never above 255
        Assert.Equal((byte)expected, combined);
    }

    /// <summary>
    ///     A table with measured tokens gets the combined reliability and the statistics.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CueRunningReliability_Apply_MeasuredTable_CombinesTheFactorWithTheAnchorReliability()
    {
        // Arrange: a table with an anchor reliability of 200 and the statistics of its covered tokens
        var hp = Hyperparameters(pseudo: 5.0);
        var measured = new BasicCueRow("measured", [], [], [], Reliability: 255, AnchorReliability: 200);
        var data = new BasicModelData(hp, [], [], [], [], [], [], [], [measured], 0, 0);
        var stats = new Dictionary<string, CueRunningStat> { ["measured"] = new(30, 1, 11) };

        // Act: apply the statistics to the model
        var applied = CueRunningReliability.Apply(data, stats);

        // Assert: the table combines its factor with its anchor reliability and carries the statistics
        var first = Assert.Single(applied.Cues);
        Assert.Equal(CueRunningReliability.Combine(200, 6.0 / 16.0, false), first.Reliability);
        Assert.Equal((30, 1, 11), (first.RunningTokens, first.RunningHelped, first.RunningHurt));
    }

    /// <summary>
    ///     A table that covers no training token keeps the reliability of its anchors.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CueRunningReliability_Apply_UnmeasuredTable_KeepsTheAnchorReliability()
    {
        // Arrange: a table with an anchor reliability of 100 and no statistics
        var hp = Hyperparameters(pseudo: 5.0);
        var unmeasured = new BasicCueRow("unmeasured", [], [], [], Reliability: 255, AnchorReliability: 100);
        var data = new BasicModelData(hp, [], [], [], [], [], [], [], [unmeasured], 0, 0);

        // Act: apply no statistics to the model
        var applied = CueRunningReliability.Apply(data, new Dictionary<string, CueRunningStat>());

        // Assert: the reliability is that of the anchors and no token is counted
        var second = Assert.Single(applied.Cues);
        Assert.Equal((byte)100, second.Reliability);
        Assert.Equal(0, second.RunningTokens);
    }

    /// <summary>
    ///     With fewer than two folds the measurement is off and the model comes back as it is.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_CueRunningReliability_ApplyAsync_WithoutFolds_ReturnsTheModel()
    {
        // Arrange: a trained model and a measurement that is switched off by zero folds
        var data = Train(truthful: true, Hyperparameters(folds: 0));

        // Act: apply and measure the model without folds
        var applied = await CueRunningReliability.ApplyAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var stats = await CueRunningReliability.MeasureAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert: the model is returned as it is and nothing is measured
        Assert.Same(data, applied);
        Assert.Empty(stats);
    }

    /// <summary>
    ///     A table whose context says the truth turns tokens right that the tagger alone cannot decide and hurts none, so it
    ///     keeps its full reliability.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_CueRunningReliability_MeasureAsync_TruthfulCues_HelpAndDoNotHurt()
    {
        // Arrange: a table whose anchors say the truth about the training sentences
        var data = Train(truthful: true, Hyperparameters());

        // Act: measure the table on the training sentences and apply the statistics
        var stats = await CueRunningReliability.MeasureAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var applied = CueRunningReliability.Apply(data, stats);

        // Assert: the cues help, never hurt, and the table stays fully reliable
        var stat = Assert.Single(stats).Value;
        Assert.Equal(80, stat.Tokens);
        Assert.True(stat.Helped > 0);
        Assert.Equal(0, stat.Hurt);
        Assert.Equal((byte)255, Assert.Single(applied.Cues).Reliability);
    }

    /// <summary>
    ///     A table whose context says the opposite hurts tokens that the tagger alone has no way to get wrong more than
    ///     half the time, and it is damped by its net benefit.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_CueRunningReliability_MeasureAsync_MisleadingCues_HurtAndAreDamped()
    {
        // Arrange: a table whose anchors say the opposite of the training sentences
        var data = Train(truthful: false, Hyperparameters());

        // Act: measure the table on the training sentences and apply the statistics
        var stats = await CueRunningReliability.MeasureAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var applied = CueRunningReliability.Apply(data, stats);

        // Assert: the cues hurt and never help, and the table is damped
        var stat = Assert.Single(stats).Value;
        Assert.Equal(80, stat.Tokens);
        Assert.Equal(0, stat.Helped);
        Assert.True(stat.Hurt > 0);
        var cue = Assert.Single(applied.Cues);
        Assert.True(cue.Reliability < 255);
        Assert.Equal(stat.Hurt, cue.RunningHurt);
    }

    /// <summary>
    ///     The same inputs give the same statistics.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_CueRunningReliability_MeasureAsync_IsDeterministic()
    {
        // Arrange: a trained model
        var data = Train(truthful: true, Hyperparameters());

        // Act: measure twice
        var first = await CueRunningReliability.MeasureAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var second = await CueRunningReliability.MeasureAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert: the two measurements are equal
        Assert.Equal(first.OrderBy(p => p.Key).ToList(), second.OrderBy(p => p.Key).ToList());
    }

    /// <summary>
    ///     A model with the counts of other sentences keeps its cue tables and drops the tense table.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BasicTrainer_WithCounts_ReplacesTheStatisticsOnly()
    {
        // Arrange: a trained model and the counts of ten other sentences
        var data = Train(truthful: true, Hyperparameters());
        var other = TaggerCounts.Count(Sentences().Take(10), Lookup);

        // Act: replace the counts of the model
        var changed = BasicTrainer.WithCounts(data, other, Lookup);

        // Assert: the cue tables stay, the statistics and the tense table follow the new counts
        Assert.Same(data.Cues, changed.Cues);
        Assert.Equal(other.Unigrams, changed.Unigrams);
        Assert.Null(changed.Tense);
        Assert.NotEqual(data.Unigrams, changed.Unigrams);
    }
}
