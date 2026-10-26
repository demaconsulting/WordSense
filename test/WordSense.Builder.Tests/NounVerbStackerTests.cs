using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the cross-fitted fit of the shared noun/verb table of the basic model.
/// </summary>
public class NounVerbStackerTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");
    private static readonly byte[] ToyHash = SHA256.HashData("noun verb toy inventory"u8);

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
    ///     the tagger alone cannot tell the two apart and only the neighbor word does.
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

    private static BasicHyperparameters Hyperparameters(int folds = 2, double weight = 1.0, double l2 = 1.0, int minimum = 2) =>
        BasicHyperparameters.Default with { PriorShare = 0.0, RunningFolds = folds, CueSkillTarget = 0.0, NounVerbWeight = weight, NounVerbL2 = l2, NounVerbMinFeatureCount = minimum };

    private static BasicModelData Train(BasicHyperparameters hp) =>
        BasicTrainer.Train(TaggerCounts.Count(Sentences(), Lookup), Lookup, [], [], hp);

    private static ushort Hash(int kind, string token) => new FeatureRef(kind, token).Hash;

    private static NounVerbSample Sample(bool isVerb, double offset, params ushort[] hashes) => new(hashes, offset, isVerb);

    private static List<NounVerbSample> Repeat(int count, bool isVerb, double offset, params ushort[] hashes) =>
        [.. Enumerable.Range(0, count).Select(_ => Sample(isVerb, offset, hashes))];

    private static int WeightOf(BasicNounVerbRow row, ushort hash) => row.Weights[Array.IndexOf(row.Hashes, hash)];

    /// <summary>
    ///     A feature that goes with the verbs gets a positive weight and one that goes with the nouns a negative weight,
    ///     the hashes are ascending, and the counts of the two classes are reported.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_FeaturesOfTheClassesGetTheirSign()
    {
        // Arrange: twenty verbs that share one feature and twenty nouns that share another, with a neutral offset
        var verbFeature = Hash(CueModel.Left, "to");
        var nounFeature = Hash(CueModel.Left, "the");
        List<NounVerbSample> samples = [.. Repeat(20, true, 0.0, verbFeature), .. Repeat(20, false, 0.0, nounFeature)];

        // Act
        var row = NounVerbStacker.Fit(samples, Hyperparameters());

        // Assert
        Assert.NotNull(row);
        Assert.True(WeightOf(row, verbFeature) > 0);
        Assert.True(WeightOf(row, nounFeature) < 0);
        Assert.Equal(row.Hashes.Order().ToArray(), row.Hashes);
        Assert.Equal(20, row.Nouns);
        Assert.Equal(20, row.Verbs);
    }

    /// <summary>
    ///     The odds that the rest of the model already gave a word are an offset of the fit: a feature of words that the rest
    ///     of the model got right gets a weight near 0, and the same feature of words it got wrong gets a large one.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_LearnsOnlyWhatTheOffsetMisses()
    {
        // Arrange: the verbs and nouns carry the same feature; once the offset says the truth, once it says nothing
        var feature = Hash(CueModel.Right, "is");
        List<NounVerbSample> right = [.. Repeat(20, true, 6.0, feature), .. Repeat(20, false, -6.0, feature)];
        List<NounVerbSample> wrong = [.. Repeat(20, true, 0.0, feature), .. Repeat(20, false, 0.0, feature), .. Repeat(20, false, 0.0, feature)];

        // Act
        var informed = NounVerbStacker.Fit(right, Hyperparameters());
        var uninformed = NounVerbStacker.Fit(wrong, Hyperparameters());

        // Assert: nothing is left to learn when the offset is right; the feature goes with the nouns when it is not
        Assert.NotNull(informed);
        Assert.NotNull(uninformed);
        Assert.InRange(WeightOf(informed, feature), -2, 2);
        Assert.True(WeightOf(uninformed, feature) < -2);
    }

    /// <summary>
    ///     A stronger L2 penalty shrinks the weights toward zero.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_PenaltyShrinksTheWeights()
    {
        // Arrange: a feature that goes with the verbs
        var feature = Hash(CueModel.Left, "to");
        List<NounVerbSample> samples = [.. Repeat(20, true, 0.0, feature), .. Repeat(5, false, 0.0, feature)];

        // Act
        var loose = NounVerbStacker.Fit(samples, Hyperparameters(l2: 0.1));
        var tight = NounVerbStacker.Fit(samples, Hyperparameters(l2: 100));

        // Assert
        Assert.NotNull(loose);
        Assert.NotNull(tight);
        Assert.True(WeightOf(loose, feature) > WeightOf(tight, feature));
        Assert.True(WeightOf(tight, feature) >= 0);
    }

    /// <summary>
    ///     A feature that occurs in fewer words than the minimum is left out, and so is the whole table when no feature is
    ///     left or when the words are all of one class.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_RareFeaturesAndOneClassGiveNothing()
    {
        // Arrange: one feature in two words and one in a single word; and words of one class only
        var common = Hash(CueModel.Left, "to");
        var rare = Hash(CueModel.Left, "unto");
        List<NounVerbSample> mixed = [Sample(true, 0.0, common, rare), Sample(false, 0.0, common), .. Repeat(3, true, 0.0, common)];

        // Act
        var row = NounVerbStacker.Fit(mixed, Hyperparameters());
        var strict = NounVerbStacker.Fit(mixed, Hyperparameters(minimum: 100));
        var verbsOnly = NounVerbStacker.Fit(Repeat(5, true, 0.0, common), Hyperparameters());
        var none = NounVerbStacker.Fit([], Hyperparameters());

        // Assert
        Assert.NotNull(row);
        Assert.Equal([common], row.Hashes);
        Assert.Null(strict);
        Assert.Null(verbsOnly);
        Assert.Null(none);
    }

    /// <summary>
    ///     The same samples give the same table, bit for bit.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_IsDeterministic()
    {
        // Arrange
        var feature = Hash(CueModel.Left, "to");
        var other = Hash(CueModel.Right2, "end");
        List<NounVerbSample> samples = [.. Repeat(7, true, 0.3, feature, other), .. Repeat(9, false, -0.2, feature)];

        // Act
        var first = NounVerbStacker.Fit(samples, Hyperparameters());
        var second = NounVerbStacker.Fit(samples, Hyperparameters());

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Hashes, second.Hashes);
        Assert.Equal(first.Weights, second.Weights);
    }

    /// <summary>
    ///     Null arguments are rejected with an argument-null error.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_NullArguments_Throw()
    {
        // Arrange
        var data = Train(Hyperparameters());

        // Act
        var fit = Record.Exception(() => NounVerbStacker.Fit(null!, Hyperparameters()));
        var fitHyperparameters = Record.Exception(() => NounVerbStacker.Fit([], null!));
        var apply = await Record.ExceptionAsync(() => NounVerbStacker.ApplyAsync(null!, [], Lookup, TestContext.Current.CancellationToken, Toy));
        var collect = await Record.ExceptionAsync(() => NounVerbStacker.CollectAsync(data, null!, Lookup, TestContext.Current.CancellationToken, Toy));

        // Assert
        Assert.IsType<ArgumentNullException>(fit);
        Assert.IsType<ArgumentNullException>(fitHyperparameters);
        Assert.IsType<ArgumentNullException>(apply);
        Assert.IsType<ArgumentNullException>(collect);
    }

    /// <summary>
    ///     Every training word that is a gold noun or verb and has both analyses is a sample, with the features of its
    ///     context and the class of its gold tag; the words of the sentences that the table has no analysis for are not.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Collect_OneSampleForEveryNounOrVerbWithBothAnalyses()
    {
        // Arrange: eighty sentences with one such word each (forty nouns and forty verbs), and other words that are not
        var data = Train(Hyperparameters());

        // Act
        var samples = await NounVerbStacker.CollectAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert
        Assert.Equal(80, samples.Count);
        Assert.Equal(40, samples.Count(s => s.IsVerb));
        Assert.All(samples, s => Assert.Equal(CueModel.MaxNounVerbFeatures, s.Hashes.Length));
        Assert.All(samples, s => Assert.True(double.IsFinite(s.Offset)));
    }

    /// <summary>
    ///     The fitted table separates what the tagger alone cannot know: <c>record</c> after <c>river</c> is a noun and after
    ///     <c>guitar</c> a verb, and the table goes into the model data with the number of its training words.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Apply_FitsATableThatSeparatesTheNeighborWords()
    {
        // Arrange: the tagger counts alone know nothing about the neighbor words
        var data = Train(Hyperparameters(weight: 1.0));

        // Act
        var fitted = await NounVerbStacker.ApplyAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var classifier = Toy.Classifier(Toy.Load(fitted));
        var river = (await classifier.ClassifyAsync(["the", "river", "record", "swims", "x"], TestContext.Current.CancellationToken))[2];
        var guitar = (await classifier.ClassifyAsync(["the", "guitar", "record", "thumps", "x"], TestContext.Current.CancellationToken))[2];

        // Assert
        Assert.Null(data.NounVerb);
        Assert.NotNull(fitted.NounVerb);
        Assert.Equal(40, fitted.NounVerb.Nouns);
        Assert.Equal(40, fitted.NounVerb.Verbs);
        Assert.Equal(PartOfSpeech.Noun, river.Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Verb, guitar.Details?.PartOfSpeech);
    }

    /// <summary>
    ///     With fewer than two folds nothing is measured and the model is returned as it is.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Apply_WithoutFolds_ReturnsTheModelUnchanged()
    {
        // Arrange
        var data = Train(Hyperparameters(folds: 0));

        // Act
        var result = await NounVerbStacker.ApplyAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert
        Assert.Same(data, result);
        Assert.Null(result.NounVerb);
    }

    /// <summary>
    ///     The same model and sentences give the same table, so equal inputs give equal bytes.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Apply_IsDeterministic()
    {
        // Arrange
        var data = Train(Hyperparameters());

        // Act
        var first = await NounVerbStacker.ApplyAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var second = await NounVerbStacker.ApplyAsync(data, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert
        Assert.NotNull(first.NounVerb);
        Assert.NotNull(second.NounVerb);
        Assert.Equal(first.NounVerb.Hashes, second.NounVerb.Hashes);
        Assert.Equal(first.NounVerb.Weights, second.NounVerb.Weights);
        Assert.Equal(BasicTablesWriter.Write(first, ToyHash), BasicTablesWriter.Write(second, ToyHash));
    }

    /// <summary>
    ///     Two different features whose hashes collide are two features of the word, as they are at run time, where the weight
    ///     of the shared hash is added once for each: the fit and the score of the same word agree.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_NounVerbStacker_Fit_CollidingFeaturesCountOnceEach()
    {
        // Arrange: the left word "to" and the second right word "instead" hash alike; four of five words are verbs
        var left = new FeatureRef(CueModel.Left, "to");
        var right = new FeatureRef(CueModel.Right2, "instead");
        Assert.Equal(left.Hash, right.Hash);
        List<NounVerbSample> samples = [.. Repeat(16, true, 0.0, left.Hash, right.Hash), .. Repeat(4, false, 0.0, left.Hash, right.Hash)];

        // Act: fit the table, and score a word that has both features as the runtime does
        var row = NounVerbStacker.Fit(samples, Hyperparameters(l2: 0.01));
        Assert.NotNull(row);
        var score = CueModel.ScoreNounVerb(new NounVerbCue([.. row.Hashes], [.. row.Weights]), [left, right]);

        // Assert: the score is the log odds of the verbs (ln 4 = 1.39 nats), not twice that, which one count would give
        Assert.Equal(Math.Log(4.0), score, 0.3);
    }

    private const int ThreeFolds = 3;

    private static int FoldOf(TaggedSentence sentence) => BasicTuner.StableHash(string.Join(' ', sentence.Words)) % ThreeFolds;

    private static CorpusRow Anchor(PartOfSpeech pos, params string[] words) =>
        new("record", $"record#{pos.ToString().ToLowerInvariant()}", pos, string.Join(' ', words), "a.tsv", words, Array.IndexOf(words, "record"));

    /// <summary>
    ///     The sentences with the gold tag of <c>record</c> turned into the other class in every sentence of one fold.
    /// </summary>
    private static List<TaggedSentence> FlipFold(List<TaggedSentence> sentences, int fold) =>
        [.. sentences.Select(s =>
        {
            if (FoldOf(s) != fold)
            {
                return s;
            }

            var tags = (int[])s.Tags.Clone();
            tags[2] = tags[2] == PosTag.Noun ? PosTag.Verb : PosTag.Noun;
            return new TaggedSentence(s.Words, tags);
        })];

    /// <summary>
    ///     The model with the cue table of truthful anchors (<c>river</c> noun, <c>guitar</c> verb) and three folds.
    /// </summary>
    private static BasicModelData TrainedWithAnchors()
    {
        var hp = Hyperparameters(folds: ThreeFolds) with { RunningPseudo = 0.5 };
        List<CorpusRow> anchors =
        [
            Anchor(PartOfSpeech.Noun, "the", "river", "record", "swims"),
            Anchor(PartOfSpeech.Noun, "a", "river", "record", "bites"),
            Anchor(PartOfSpeech.Noun, "that", "river", "record", "runs"),
            Anchor(PartOfSpeech.Verb, "the", "guitar", "record", "thumps"),
            Anchor(PartOfSpeech.Verb, "a", "guitar", "record", "booms"),
            Anchor(PartOfSpeech.Verb, "that", "guitar", "record", "plays"),
        ];
        return BasicTrainer.Train(TaggerCounts.Count(Sentences(), Lookup), Lookup, anchors, [], hp);
    }

    /// <summary>
    ///     The whole path of the training: the running-text reliability of the cue tables measured on the sentences, and the
    ///     offsets of the words of every fold.
    /// </summary>
    private static async Task<IReadOnlyList<NounVerbSample>> OffsetsOfAsync(List<TaggedSentence> sentences)
    {
        var reliable = await CueRunningReliability.ApplyAsync(TrainedWithAnchors(), sentences, Lookup, TestContext.Current.CancellationToken, Toy);
        return await NounVerbStacker.CollectAsync(reliable, sentences, Lookup, TestContext.Current.CancellationToken, Toy);
    }

    /// <summary>
    ///     The offset of a word that was held out does not depend on any gold tag of its own fold, through the tagger counts
    ///     and through the running-text reliability of the cue tables (measured from the gold tags of the whole split). The
    ///     offsets of the other folds do depend on them, so the check can see a dependence.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Collect_OffsetsOfAFoldDoNotDependOnItsOwnGoldTags()
    {
        // Arrange: the sentences, and the same sentences with the gold tag of the target turned around in every sentence of fold 0
        var original = Sentences();
        var flipped = FlipFold(original, 0);
        var inFoldZero = original.Count(s => FoldOf(s) == 0);
        Assert.InRange(inFoldZero, 1, original.Count - 1);

        // Act
        var before = await OffsetsOfAsync(original);
        var after = await OffsetsOfAsync(flipped);

        // Assert: the samples come fold by fold; fold 0 is scored alike whatever its tags say, and has the flipped classes
        Assert.Equal(original.Count, before.Count);
        Assert.Equal(before.Take(inFoldZero).Select(s => s.Offset), after.Take(inFoldZero).Select(s => s.Offset));
        Assert.Equal(before.Take(inFoldZero).Select(s => !s.IsVerb), after.Take(inFoldZero).Select(s => s.IsVerb));
        Assert.NotEqual(before.Skip(inFoldZero).Select(s => s.Offset), after.Skip(inFoldZero).Select(s => s.Offset));
    }

    /// <summary>
    ///     The offsets do not depend on the running-text reliabilities that the model carries, which were measured from the
    ///     gold tags of every fold: each fold is scored with reliabilities measured on the other folds.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_NounVerbStacker_Collect_IgnoresTheReliabilitiesMeasuredOnTheWholeSplit()
    {
        // Arrange: the same model once with its anchor reliabilities and once with reliabilities and statistics that say the cues are useless
        var anchorOnly = TrainedWithAnchors();
        var distrusted = anchorOnly with { Cues = [.. anchorOnly.Cues.Select(c => c with { Reliability = 0, RunningTokens = 999, RunningHurt = 999 })] };

        // Act
        var first = await NounVerbStacker.CollectAsync(anchorOnly, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);
        var second = await NounVerbStacker.CollectAsync(distrusted, Sentences(), Lookup, TestContext.Current.CancellationToken, Toy);

        // Assert
        Assert.Equal(first.Select(s => s.Offset), second.Select(s => s.Offset));
    }
}
