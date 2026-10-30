using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the all-words gold set (<c>corpus/allwords</c>) and of its evaluation: the committed files, the draw of the
///     tokens, the labeling batches and the merge of the two labelers, the measurement of a model and the baselines.
/// </summary>
public sealed class AllWordsTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private static string Corpus => Path.Join(DataFixture.RepositoryRoot(), "corpus");

    private static AllWordsItem Item(string id, string split, string lemma, PartOfSpeech pos, string senseId, string agreement = "high", params string[] words) =>
        new(id, split, "sentence-" + id, Array.IndexOf(words, lemma), lemma, lemma, pos, senseId, agreement, words);

    // The first two senses of a lemma and part of speech of the committed catalog, in inventory order
    private static (string First, string Second) TwoSenses(string lemma, PartOfSpeech pos)
    {
        var senses = WordLexicon.SensesOf(lemma, pos);
        return (senses[0].SenseId, senses[1].SenseId);
    }

    private static readonly string[] Splits = [AllWordsGold.Dev, AllWordsGold.Test];

    private static readonly Lazy<Dictionary<string, List<AllWordsItem>>> CommittedHalves = new(() => Splits.ToDictionary(s => s, s => AllWordsGold.Read(Corpus, s)));

    /// <summary>
    ///     The committed gold tokens are of lemmas that had no anchor when the set was drawn (the anchors of the later stages,
    ///     the waves 50 to 59, 98, 99 and 100 to 149, come later and are not counted) whose part of speech has two or more
    ///     senses, and each names a sense of its lemma and part of speech and a word that is at its index (the reader checks
    ///     the last two).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_CommittedGold_TokensAreOfUntrainedPolysemousLemmasWithTheirSense()
    {
        // Arrange: the lemmas of the later stages were untrained at the draw; their anchors are the point of the independent check
        var later = new System.Text.RegularExpressions.Regex(@"^quota-w(5\d|98|99|1[0-4]\d)-", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var trained = Directory.GetFiles(Path.Join(Corpus, "anchors"), "*.tsv")
            .Where(f => !later.IsMatch(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f))
            .Select(l => l.Split('\t')[0])
            .ToHashSet(StringComparer.Ordinal);

        // Act
        var halves = CommittedHalves.Value;

        // Assert
        foreach (var (split, items) in halves)
        {
            Assert.True(items.Count >= 300, $"The {split} half has {items.Count} tokens.");
            Assert.All(items, i => Assert.DoesNotContain(i.Lemma, trained));
            Assert.All(items, i => Assert.True(WordLexicon.SensesOf(i.Lemma, i.PartOfSpeech).Length >= 2));
            Assert.All(items, i => Assert.Contains(i.SenseId, WordLexicon.SensesOf(i.Lemma, i.PartOfSpeech).Select(s => s.SenseId)));
            Assert.All(items, i => Assert.Equal(i.Form, i.Words[i.Target]));
        }
    }

    /// <summary>
    ///     The committed gold set has at most three tokens for a lemma and at most one for a sentence.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_CommittedGold_HasAtMostThreeTokensPerLemmaAndOnePerSentence()
    {
        // Act
        var halves = CommittedHalves.Value;

        // Assert
        foreach (var items in halves.Values)
        {
            Assert.All(items.GroupBy(i => i.Lemma), g => Assert.True(g.Count() <= 3, $"{g.Key} has {g.Count()} tokens."));
            Assert.Equal(items.Count, items.Select(i => i.SentenceId).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(items.Count, items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>
    ///     The development tokens come only from the development split and the test tokens only from the test split of the
    ///     representative open corpus.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_CommittedGold_TokensComeFromTheirOwnSplit()
    {
        // Arrange
        var halves = CommittedHalves.Value;

        // Act
        var sentences = Splits.ToDictionary(s => s, s => OpenCorpus.ReadSplit(Corpus, OpenCorpus.Representative, s).Select(x => x.SentenceId).ToHashSet(StringComparer.Ordinal));

        // Assert
        foreach (var (split, items) in halves)
        {
            Assert.All(items, i => Assert.Contains(i.SentenceId, sentences[split]));
            Assert.All(items, i => Assert.Equal(split, i.Split));
            Assert.All(items, i => Assert.Equal(split[0], i.Id[0]));
        }
    }

    /// <summary>
    ///     The two committed halves share no document: no source of the development tokens is a source of the test tokens.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_CommittedGold_HalvesShareNoDocument()
    {
        // Arrange
        var halves = CommittedHalves.Value;

        // Act
        var documents = Splits.ToDictionary(
            s => s,
            s =>
            {
                var sentences = OpenCorpus.ReadSplit(Corpus, OpenCorpus.Representative, s).ToDictionary(x => x.SentenceId, x => x.SourceId, StringComparer.Ordinal);
                return new HashSet<string>(halves[s].Select(i => sentences[i.SentenceId]), StringComparer.Ordinal);
            });

        // Assert
        Assert.NotEmpty(documents[AllWordsGold.Dev]);
        Assert.NotEmpty(documents[AllWordsGold.Test]);
        Assert.Empty(documents[AllWordsGold.Dev].Intersect(documents[AllWordsGold.Test]));
    }

    /// <summary>
    ///     The draw is reproducible: the same seed gives the same tokens and another seed other sentences.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Draw_IsReproducibleFromTheSeed()
    {
        // Act
        var first = AllWordsGold.Draw(Corpus, AllWordsGold.Dev, 60, 2, 7);
        var second = AllWordsGold.Draw(Corpus, AllWordsGold.Dev, 60, 2, 7);
        var other = AllWordsGold.Draw(Corpus, AllWordsGold.Dev, 60, 2, 8);

        // Assert
        Assert.Equal(60, first.Count);
        Assert.Equal(first.Select(s => (s.Id, s.SentenceId, s.Target, s.Lemma, s.PartOfSpeech)), second.Select(s => (s.Id, s.SentenceId, s.Target, s.Lemma, s.PartOfSpeech)));
        Assert.Equal(first[0].Words, second[0].Words);
        Assert.NotEqual(first.Select(s => s.SentenceId), other.Select(s => s.SentenceId));
    }

    /// <summary>
    ///     A drawn token is a noun, verb, adjective or adverb of an untrained lemma whose part of speech has two or more
    ///     senses, and whose written form the lexicon lists with that lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Draw_TokensAreEligible()
    {
        // Arrange
        var trained = WordSense.Builder.Corpus.ReadLemmas(Path.Join(Corpus, "anchors"));

        // Act
        var drawn = AllWordsGold.Draw(Corpus, AllWordsGold.Dev, 60, 2, 7);

        // Assert
        Assert.All(drawn, s => Assert.True(s.PartOfSpeech is PartOfSpeech.Noun or PartOfSpeech.Verb or PartOfSpeech.Adjective or PartOfSpeech.Adverb));
        Assert.All(drawn, s => Assert.DoesNotContain(s.Lemma, trained));
        Assert.All(drawn, s => Assert.True(WordLexicon.SensesOf(s.Lemma, s.PartOfSpeech).Length >= 2));
        Assert.All(drawn, s => Assert.True(WordLexicon.TryLookup(s.Form, out var analyses) && analyses.Any(a => a.Lemma == s.Lemma && a.PartOfSpeech == s.PartOfSpeech)));
    }

    /// <summary>
    ///     The draw gives no lemma more tokens than the limit and no sentence more than one, and numbers the tokens in the
    ///     order of their sentences.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Draw_RespectsTheLimitsOfLemmaAndSentence()
    {
        // Act
        var drawn = AllWordsGold.Draw(Corpus, AllWordsGold.Dev, 60, 2, 7);

        // Assert
        Assert.All(drawn.GroupBy(s => s.Lemma), g => Assert.True(g.Count() <= 2));
        Assert.Equal(drawn.Count, drawn.Select(s => s.SentenceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(drawn.OrderBy(s => s.SentenceId, StringComparer.Ordinal).Select(s => s.Id), drawn.Select(s => s.Id));
        Assert.Equal("d-0001", drawn[0].Id);
    }

    private const string GoodLemma = "bank";

    // A written gold file with one token of the bank noun, a line to damage, and the sense that it names
    private static (string Line, string Sense) GoodLine()
    {
        var (sense, _) = TwoSenses(GoodLemma, PartOfSpeech.Noun);
        var item = Item("d-0001", AllWordsGold.Dev, GoodLemma, PartOfSpeech.Noun, sense, "low", "The", GoodLemma, "closed", ".");
        return (AllWordsGold.Render(AllWordsGold.Dev, [item]).Split('\n').First(l => l.StartsWith("d-0001", StringComparison.Ordinal)), sense);
    }

    private InvalidDataException ReadDamaged(string line)
    {
        _dir.Write("corpus/allwords/dev.tsv", line + "\n");
        return Assert.Throws<InvalidDataException>(() => AllWordsGold.Read(Path.Join(_dir.Root, "corpus"), AllWordsGold.Dev));
    }

    /// <summary>
    ///     A gold file is read back as it was rendered, and a file that does not exist reads as empty.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Gold_RoundTrips()
    {
        // Arrange
        var (sense, _) = TwoSenses(GoodLemma, PartOfSpeech.Noun);
        var item = Item("d-0001", AllWordsGold.Dev, GoodLemma, PartOfSpeech.Noun, sense, "low", "The", GoodLemma, "closed", ".");
        var corpus = Path.Join(_dir.Root, "corpus");
        _dir.Write("corpus/allwords/dev.tsv", AllWordsGold.Render(AllWordsGold.Dev, [item]));

        // Act
        var read = AllWordsGold.Read(corpus, AllWordsGold.Dev);

        // Assert
        Assert.Equal(item.Id, Assert.Single(read).Id);
        Assert.Equal(item.Words, read[0].Words);
        Assert.Equal((item.Target, item.Lemma, item.PartOfSpeech, item.SenseId, item.Agreement), (read[0].Target, read[0].Lemma, read[0].PartOfSpeech, read[0].SenseId, read[0].Agreement));
        Assert.Empty(AllWordsGold.Read(corpus, AllWordsGold.Test));
    }

    /// <summary>
    ///     A line with the wrong number of columns is rejected with the file and the line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Gold_WrongNumberOfColumns_IsRejected()
    {
        // Arrange
        var (good, _) = GoodLine();

        // Act
        var error = ReadDamaged(good + "\tx");

        // Assert
        Assert.Contains("columns", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev.tsv:1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A word that is not at its index is rejected with the file and the line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Gold_WordNotAtItsIndex_IsRejected()
    {
        // Arrange
        var (good, _) = GoodLine();

        // Act
        var error = ReadDamaged(good.Replace("\t1\tbank\t", "\t2\tbank\t", StringComparison.Ordinal));

        // Assert
        Assert.Contains("is not 'bank'", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev.tsv:1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An index outside the sentence (past its end, or negative) is rejected with the file and the line, as the data
    ///     error of the reader and not as an index error.
    /// </summary>
    /// <param name="index">The index that replaces the index of the target word.</param>
    [Theory]
    [InlineData("-1")]
    [InlineData("4")]
    public void WordSenseBuilder_AllWords_Gold_IndexOutsideTheSentence_IsRejected(string index)
    {
        // Arrange
        var (good, _) = GoodLine();

        // Act
        var error = ReadDamaged(good.Replace("\t1\tbank\t", "\t" + index + "\tbank\t", StringComparison.Ordinal));

        // Assert
        Assert.Contains("is not 'bank'", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev.tsv:1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An agreement that is not high or low is rejected with the file and the line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Gold_AgreementNotHighOrLow_IsRejected()
    {
        // Arrange
        var (good, _) = GoodLine();

        // Act
        var error = ReadDamaged(good.Replace("\tlow\t", "\tmaybe\t", StringComparison.Ordinal));

        // Assert
        Assert.Contains("high or low", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev.tsv:1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense that is not a sense of the lemma and part of speech is rejected with the file and the line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Gold_SenseOfAnotherLemma_IsRejected()
    {
        // Arrange
        var (good, sense) = GoodLine();

        // Act
        var error = ReadDamaged(good.Replace(sense, "river#bank", StringComparison.Ordinal));

        // Assert
        Assert.Contains("is not a sense of the lemma", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev.tsv:1", error.Message, StringComparison.Ordinal);
    }

    private static readonly string DataFolder = Path.Join(DataFixture.RepositoryRoot(), "data");

    private static List<AllWordsSample> FourSamples() =>
    [
        new("d-0001", "dev", "s1", 1, "bank", PartOfSpeech.Noun, ["The", "bank", "closed", "."]),
        new("d-0002", "dev", "s2", 4, "bank", PartOfSpeech.Noun, ["She", "sat", "on", "the", "bank", "."]),
        new("d-0003", "dev", "s3", 1, "bank", PartOfSpeech.Noun, ["A", "bank", "is", "here", "."]),
        new("d-0004", "dev", "s4", 1, "bank", PartOfSpeech.Noun, ["Which", "bank", "?"]),
    ];

    // Writes the labeling batch of four development tokens and the answer files of two labelers, and merges them
    private (Dictionary<string, List<AllWordsItem>> Gold, List<AllWordsGold.LabelAgreement> Agreement) MergeAnswers(string first, string second)
    {
        var directory = Path.Join(_dir.Root, "labels");
        AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 10);
        _dir.Write("labels/aw-d01.first.tsv", first);
        _dir.Write("labels/aw-d01.second.tsv", second);
        return AllWordsGold.Merge(directory, "first", "second");
    }

    private JsonDocument WriteBatchAndReadIt()
    {
        var directory = Path.Join(_dir.Root, "labels");
        AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 10);
        return JsonDocument.Parse(File.ReadAllText(Path.Join(directory, "batches", "aw-d01.json")));
    }

    /// <summary>
    ///     A labeling batch has a group for each lemma and part of speech with every sense of it (id, part of speech and
    ///     gloss) in the order of the ids, not in the inventory order, which is the order of frequency.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Batches_ListEverySenseInTheOrderOfTheIds()
    {
        // Act
        using var document = WriteBatchAndReadIt();

        // Assert
        var group = Assert.Single(document.RootElement.GetProperty("groups").EnumerateArray());
        var senseObjects = group.GetProperty("senses").EnumerateArray().ToList();
        var ids = senseObjects.Select(s => s.GetProperty("id").GetString()!).ToList();
        Assert.Equal(WordLexicon.SensesOf("bank", PartOfSpeech.Noun).Select(s => s.SenseId).Order(StringComparer.Ordinal), ids);
        Assert.All(senseObjects, s => Assert.False(string.IsNullOrEmpty(s.GetProperty("gloss").GetString())));
        Assert.All(senseObjects, s => Assert.Equal(JsonValueKind.String, s.GetProperty("pos").ValueKind));
    }

    /// <summary>
    ///     A labeling batch shows no tag count: a sense has an id, a part of speech and a gloss and nothing else.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Batches_HaveNoTagCount()
    {
        // Act
        using var document = WriteBatchAndReadIt();

        // Assert
        var group = Assert.Single(document.RootElement.GetProperty("groups").EnumerateArray());
        Assert.Equal(["senses", "items"], group.EnumerateObject().Select(p => p.Name));
        Assert.All(group.GetProperty("senses").EnumerateArray(), s => Assert.Equal(["id", "pos", "gloss"], s.EnumerateObject().Select(p => p.Name)));
    }

    /// <summary>
    ///     A labeling batch shows the sentences with the target word in braces, and the batches are written for each split.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Batches_MarkTheTargetWordAndSeparateTheSplits()
    {
        // Arrange
        var directory = Path.Join(_dir.Root, "labels");
        List<AllWordsSample> samples = [.. FourSamples(), new("d-0005", "test", "s5", 1, "bank", PartOfSpeech.Noun, ["One", "bank", "."])];

        // Act
        var batches = AllWordsGold.WriteBatches(DataFolder, samples, directory, 10);
        var batchNames = Directory.GetFiles(Path.Join(directory, "batches"), "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).ToList();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Join(directory, "batches", "aw-d01.json")));

        // Assert
        Assert.Equal(2, batches);
        Assert.Equal(["aw-d01", "aw-t02"], batchNames);
        var group = Assert.Single(document.RootElement.GetProperty("groups").EnumerateArray());
        Assert.Equal(["The {bank} closed .", "She sat on the {bank} .", "A {bank} is here .", "Which {bank} ?"], group.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sentence").GetString()));
    }

    /// <summary>
    ///     A lemma with more tokens than a batch holds is divided into batches of at most that many tokens, each with the
    ///     senses of the lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Batches_OversizedGroup_IsDividedToTheBatchSize()
    {
        // Arrange
        var directory = Path.Join(_dir.Root, "labels");

        // Act
        var batches = AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 3);
        var sizes = Directory.GetFiles(Path.Join(directory, "batches"), "*.json").Order(StringComparer.Ordinal).Select(f =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(f));
            var group = Assert.Single(document.RootElement.GetProperty("groups").EnumerateArray());
            Assert.True(group.GetProperty("senses").GetArrayLength() >= 2);
            return group.GetProperty("items").GetArrayLength();
        }).ToList();

        // Assert
        Assert.Equal(2, batches);
        Assert.Equal([3, 1], sizes);
    }

    /// <summary>
    ///     A split whose tokens are all dropped by the merge gets an empty gold file, so an earlier file of the split is not
    ///     read as gold.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_AllWordsMerge_SplitWithNoKeptToken_GetsAnEmptyFile()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Join(_dir.Root, "labels");
        AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 10);
        _dir.Write("labels/aw-d01.one.tsv", "1\tunsure\thigh\n2\tunsure\thigh\n3\tunsure\thigh\n4\tunsure\thigh\n");
        _dir.Write("labels/aw-d01.two.tsv", "1\tunsure\thigh\n2\tunsure\thigh\n3\tunsure\thigh\n4\tunsure\thigh\n");
        var target = Path.Join(_dir.Root, "corpus", "allwords");
        _dir.Write("corpus/allwords/dev.tsv", "# an earlier file\nstale\n");
        using var output = new StringWriter();

        // Act
        var code = await Program.RunAsync(["allwords-merge", "--batches", directory, "--first", "one", "--second", "two", "--target", target], output, new StringWriter(), ct);

        // Assert
        Assert.Equal(0, code);
        Assert.DoesNotContain("stale", await File.ReadAllTextAsync(Path.Join(target, "dev.tsv"), ct), StringComparison.Ordinal);
        Assert.Empty(AllWordsGold.Read(Path.Join(_dir.Root, "corpus"), AllWordsGold.Dev));
    }

    /// <summary>
    ///     The merge keeps a token when both labelers chose the same sense, also when one of them was not highly confident
    ///     (the agreement of the token is then low).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_KeepsTheTokensBothLabelersAgreeOn()
    {
        // Arrange
        var (a, _) = TwoSenses("bank", PartOfSpeech.Noun);

        // Act
        var (gold, _) = MergeAnswers($"1\t{a}\thigh\n2\t{a}\thigh\n", $"1\t{a}\tlow\n2\t{a}\thigh\n");

        // Assert
        Assert.Equal(["d-0001", "d-0002"], gold["dev"].Select(i => i.Id));
        Assert.Equal(["low", "high"], gold["dev"].Select(i => i.Agreement));
        Assert.All(gold["dev"], i => Assert.Equal(a, i.SenseId));
        Assert.Equal(["The", "bank", "closed", "."], gold["dev"][0].Words);
    }

    /// <summary>
    ///     A token on which the two labelers chose different senses is dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_Disagreement_DropsTheToken()
    {
        // Arrange
        var (a, b) = TwoSenses("bank", PartOfSpeech.Noun);

        // Act
        var (gold, _) = MergeAnswers($"1\t{a}\thigh\n2\t{a}\thigh\n", $"1\t{b}\thigh\n2\t{a}\thigh\n");

        // Assert
        Assert.Equal(["d-0002"], gold["dev"].Select(i => i.Id));
    }

    /// <summary>
    ///     A token that either labeler answered as unsure is dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_UnsureAnswer_DropsTheToken()
    {
        // Arrange
        var (a, _) = TwoSenses("bank", PartOfSpeech.Noun);

        // Act
        var (gold, _) = MergeAnswers($"1\tunsure\n2\t{a}\thigh\n3\t{a}\thigh\n", $"1\t{a}\thigh\n2\t{a}\thigh\n3\tunsure\n");

        // Assert
        Assert.Equal(["d-0002"], gold["dev"].Select(i => i.Id));
    }

    /// <summary>
    ///     A token for which a labeler has no answer line is dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_MissingAnswerLine_DropsTheToken()
    {
        // Arrange: the first labeler has no line for the token 2
        var (a, _) = TwoSenses("bank", PartOfSpeech.Noun);

        // Act
        var (gold, _) = MergeAnswers($"1\t{a}\thigh\n", $"1\t{a}\thigh\n2\t{a}\thigh\n");

        // Assert
        Assert.Equal(["d-0001"], gold["dev"].Select(i => i.Id));
    }

    /// <summary>
    ///     The merge reports the agreement of the labelers: the tokens sampled, answered by both, agreed on, agreed on with
    ///     a high confidence by both, and answered unsure by each, and the share of the agreement.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_ReportsTheAgreement()
    {
        // Arrange
        var (a, b) = TwoSenses("bank", PartOfSpeech.Noun);

        // Act
        var (_, agreement) = MergeAnswers($"1\t{a}\thigh\n2\t{b}\tlow\n3\tunsure\n4\t{a}\thigh\n", $"1\t{a}\tlow\n2\t{a}\thigh\n3\t{a}\thigh\n4\tunsure\n");

        // Assert
        var dev = agreement.Single(x => x.Split == "dev");
        Assert.Equal((4, 2, 1, 0, 1, 1), (dev.Sampled, dev.BothAnswered, dev.Agreed, dev.BothHigh, dev.UnsureFirst, dev.UnsureSecond));
        Assert.Contains("50.00%", AllWordsGold.RenderAgreement(agreement), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A labeler that answers with a sense the token cannot have makes the merge fail.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_WrongSense_Fails()
    {
        // Arrange
        var directory = Path.Join(_dir.Root, "labels");
        AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 10);
        _dir.Write("labels/aw-d01.first.tsv", "1\tcat#feline\thigh\n");
        _dir.Write("labels/aw-d01.second.tsv", "1\tunsure\n");

        // Act
        var error = Assert.Throws<InvalidDataException>(() => AllWordsGold.Merge(directory, "first", "second"));

        // Assert
        Assert.Contains("not a sense of bank", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing answer file makes the merge fail.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Merge_MissingAnswerFile_Fails()
    {
        // Arrange
        var directory = Path.Join(_dir.Root, "labels");
        AllWordsGold.WriteBatches(DataFolder, FourSamples(), directory, 10);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => AllWordsGold.Merge(directory, "first", "second"));

        // Assert
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    private static ClassifyResult[] Classify(IReadOnlyList<string> words, Func<int, ImmutableArray<AnalysisCandidate>> analysesOf) =>
        [.. words.Select((w, i) =>
        {
            var analyses = analysesOf(i);
            return analyses.IsDefaultOrEmpty
                ? ClassifyResult.Unknown(w)
                : new ClassifyResult(w, true, analyses, analyses[0].Details, SenseLevel.PartOfSpeech, 0.8, null, null);
        })];

    private static readonly WordDetails NounBank = new("bank", PartOfSpeech.Noun, "Number=Sing");

    private static readonly WordDetails VerbBank = new("bank", PartOfSpeech.Verb, "VerbForm=Inf");

    private static ImmutableArray<SenseCandidate> RankedSenses(string top, string other) => [new SenseCandidate(top, 0.7), new SenseCandidate(other, 0.3)];

    private static ClassifyResult AnswerAtSenseLevel(string word, string senseId) =>
        new(word, true, [new AnalysisCandidate(NounBank, 1, [])], NounBank, SenseLevel.Sense, 0.9, null, senseId);

    /// <summary>
    ///     The measurement counts the tokens answered at each level and those of an unknown word, as any other evaluation
    ///     does.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_AllWords_Measure_CountsTheTokensAnsweredAtEachLevel()
    {
        // Arrange: a token answered at the part-of-speech level, one answered at the sense level and one unknown
        var (first, _) = TwoSenses("bank", PartOfSpeech.Noun);
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "one"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "two"),
            Item("d-0003", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "three"),
        };
        using var model = new FakeModel(words => words[1] switch
        {
            "one" => Classify(words, _ => [new AnalysisCandidate(NounBank, 1, [])]),
            "two" => [AnswerAtSenseLevel(words[0], first), ClassifyResult.Unknown(words[1])],
            _ => [ClassifyResult.Unknown(words[0]), ClassifyResult.Unknown(words[1])],
        });

        // Act
        var score = await AllWordsEvaluator.MeasureAsync("fake", model, "dev", items, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, score.Set.Rows);
        Assert.Equal((1, 1), (score.Set.PartOfSpeech.Answered, score.Set.PartOfSpeech.Correct));
        Assert.Equal((1, 1), (score.Set.Sense.Answered, score.Set.Sense.Correct));
        Assert.Equal(1, score.Set.None);
    }

    /// <summary>
    ///     The measurement reports the precision of the sense answers: of the tokens answered at the sense level, the ones
    ///     whose answered sense is the gold sense.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_AllWords_Measure_ReportsThePrecisionOfTheSenseAnswers()
    {
        // Arrange: a right and a wrong sense answer
        var (first, second) = TwoSenses("bank", PartOfSpeech.Noun);
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "one"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "two"),
        };
        using var model = new FakeModel(words => [AnswerAtSenseLevel(words[0], words[1] == "one" ? first : second), ClassifyResult.Unknown(words[1])]);

        // Act
        var score = await AllWordsEvaluator.MeasureAsync("fake", model, "dev", items, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((2, 1), (score.Set.Sense.Answered, score.Set.Sense.Correct));
    }

    /// <summary>
    ///     The fine top-1 counts a token when the first ranked sense of the selected analysis is the gold sense, and not
    ///     when that sense is ranked second.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_AllWords_Measure_FineTop1CountsTheFirstRankedSenseOfTheSelectedAnalysis()
    {
        // Arrange: a model that ranks the gold sense first for one token and second for the other
        var (first, second) = TwoSenses("bank", PartOfSpeech.Noun);
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "one"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "two"),
        };
        using var model = new FakeModel(words => Classify(words, _ => words[1] == "one"
            ? [new AnalysisCandidate(NounBank, 1, RankedSenses(first, second))]
            : [new AnalysisCandidate(NounBank, 1, RankedSenses(second, first))]));

        // Act
        var score = await AllWordsEvaluator.MeasureAsync("fake", model, "dev", items, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((2, 1, 2, 1), (score.Set.Rows, score.FineTop1, score.Ranked, score.FineTop1GivenAnalysis));
    }

    /// <summary>
    ///     A token whose selected analysis ranks no sense is a miss of the fine top-1, also when the analysis is the right
    ///     one.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_AllWords_Measure_SelectedAnalysisWithoutRanking_IsAMiss()
    {
        // Arrange
        var (first, _) = TwoSenses("bank", PartOfSpeech.Noun);
        var items = new[] { Item("d-0001", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "one") };
        using var model = new FakeModel(words => Classify(words, _ => [new AnalysisCandidate(NounBank, 1, [])]));

        // Act
        var score = await AllWordsEvaluator.MeasureAsync("fake", model, "dev", items, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, 0, 0, 0), (score.Set.Rows, score.FineTop1, score.Ranked, score.FineTop1GivenAnalysis));
        Assert.Equal(1, score.Set.AnalysisCorrect);
    }

    /// <summary>
    ///     A token whose selected analysis is the wrong one is a miss of the fine top-1: the selected verb analysis ranks
    ///     the senses of the verb, none of which is the gold sense, although the unselected noun analysis lists the gold
    ///     sense first.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_AllWords_Measure_WrongSelectedAnalysis_IsAMiss()
    {
        // Arrange: the senses of the verb analysis are real verb senses of the catalog, not the gold noun sense
        var (first, second) = TwoSenses("bank", PartOfSpeech.Noun);
        var (verbFirst, verbSecond) = TwoSenses("bank", PartOfSpeech.Verb);
        var items = new[] { Item("d-0001", "dev", "bank", PartOfSpeech.Noun, first, "high", "bank", "one") };
        using var model = new FakeModel(words => Classify(words, _ =>
            [new AnalysisCandidate(VerbBank, 0.6, RankedSenses(verbFirst, verbSecond)), new AnalysisCandidate(NounBank, 0.4, RankedSenses(first, second))]));

        // Act
        var score = await AllWordsEvaluator.MeasureAsync("fake", model, "dev", items, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first, verbFirst);
        Assert.Equal((1, 0, 1, 0), (score.Set.Rows, score.FineTop1, score.Ranked, score.FineTop1GivenAnalysis));
        Assert.Equal(0, score.Set.AnalysisCorrect);
    }

    /// <summary>
    ///     The first-sense baseline is right for a token whose gold sense is the first sense in inventory order, and
    ///     knows the gold lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Baselines_FirstSenseIsTheFirstInInventoryOrder()
    {
        // Arrange: the first sense and the last one
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, usage[0].SenseId, "high", "bank"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, usage[^1].SenseId, "high", "bank"),
        };

        // Act
        var baselines = AllWordsEvaluator.Baselines("dev", items);

        // Assert
        Assert.Equal((2, 1), (baselines.Rows, baselines.FirstSense));
    }

    /// <summary>
    ///     The tag-count baseline is right for a token whose gold sense has the highest WordNet 3.0 tag count, the first in
    ///     inventory order on a tie.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Baselines_TagCountIsTheHighestCountWithInventoryOrderOnATie()
    {
        // Arrange: the most frequent sense (the first of them on a tie) and another sense
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var best = usage.Select((u, i) => (u, i)).MaxBy(p => (p.u.TagCount, -p.i)).u.SenseId;
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, best, "high", "bank"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, usage.First(u => u.SenseId != best).SenseId, "high", "bank"),
        };

        // Act
        var baselines = AllWordsEvaluator.Baselines("dev", items);

        // Assert
        Assert.Equal((2, 1), (baselines.Rows, baselines.TagCount));
    }

    /// <summary>
    ///     The random baseline is the mean of one over the number of senses of the gold lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Baselines_RandomIsOneOverTheNumberOfSenses()
    {
        // Arrange
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var items = new[]
        {
            Item("d-0001", "dev", "bank", PartOfSpeech.Noun, usage[0].SenseId, "high", "bank"),
            Item("d-0002", "dev", "bank", PartOfSpeech.Noun, usage[^1].SenseId, "high", "bank"),
            Item("d-0003", "dev", "bank", PartOfSpeech.Noun, usage[1].SenseId, "high", "bank"),
        };

        // Act
        var baselines = AllWordsEvaluator.Baselines("dev", items);

        // Assert
        Assert.Equal(3.0 / usage.Length, baselines.Random, 12);
    }

    /// <summary>
    ///     The smoothing sweep gives the mean log loss of the gold sense under the prior: for a lemma where the gold sense
    ///     is the most frequent one it falls with a smaller constant, and each value is the log loss of that constant.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AllWords_Sweep_ReportsTheMeanLogLossOfTheGoldSense()
    {
        // Arrange
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var best = usage.Select((u, i) => (u, i)).MaxBy(p => (p.u.TagCount, -p.i)).u.SenseId;
        var items = new[] { Item("d-0001", "dev", "bank", PartOfSpeech.Noun, best, "high", "bank") };

        // Act
        var rows = AllWordsEvaluator.Sweep(items, [0.1, 1.0]);

        // Assert
        var expected = -Math.Log(UsagePrior.Rank(usage, 1.0).First(s => s.SenseId == best).Probability);
        Assert.Equal(expected, rows[1].LogLoss, 12);
        Assert.True(rows[0].LogLoss < rows[1].LogLoss);
        Assert.Equal([0.1, 1.0], rows.Select(r => r.K));
        Assert.Equal(0, AllWordsEvaluator.Sweep([], [1.0])[0].LogLoss);
    }

    // Runs the evaluation command for the Basic model on the committed gold set and returns the text of its report
    private async Task<string> EvaluateBasicAsync()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var report = Path.Join(_dir.Root, "report.md");

        var code = await Program.RunAsync(["evaluate-allwords", "--model", "basic", "--repository", DataFixture.RepositoryRoot(), "--output", report], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
        return await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     The evaluation command measures the model on each half separately and reports the counts of both.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateAllWords_ReportsTheModelOnEachHalf()
    {
        // Act
        var text = await EvaluateBasicAsync();

        // Assert
        Assert.Contains("#### dev half", text, StringComparison.Ordinal);
        Assert.Contains("#### test half", text, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(text, @"\| Basic \|").Count);
    }

    /// <summary>
    ///     The evaluation command reports the three baselines on each half.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateAllWords_ReportsTheBaselinesOnEachHalf()
    {
        // Act
        var text = await EvaluateBasicAsync();

        // Assert
        Assert.Equal(2, Regex.Matches(text, @"\| first sense \(inventory order\) \|").Count);
        Assert.Equal(2, Regex.Matches(text, @"\| most frequent sense \(WordNet 3\.0 tag counts\) \|").Count);
        Assert.Equal(2, Regex.Matches(text, @"\| random sense \|").Count);
    }

    /// <summary>
    ///     The evaluation command reports the smoothing sweep once, for the development half only.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateAllWords_ReportsTheSweepOnTheDevelopmentHalfOnly()
    {
        // Act
        var text = await EvaluateBasicAsync();

        // Assert
        Assert.Contains("Smoothing constant of the prior (development half only)", text, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(text, "Smoothing constant"));
    }

    /// <summary>
    ///     Without a gold set the evaluation command fails with a message.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateAllWords_WithoutGoldSet_Fails()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["evaluate-allwords", "--model", "basic", "--repository", DataFixture.RepositoryRoot(), "--corpus", Path.Join(_dir.Root, "none")], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("all-words gold set is not in", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The sampling command writes the batches and the table of the tokens for both splits, and the merge command
    ///     writes the gold files and prints the agreement.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_AllWordsSampleAndMerge_WriteBatchesAndGoldFiles()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();
        var directory = Path.Join(_dir.Root, "labels");
        var root = DataFixture.RepositoryRoot();

        // Act: sample 12 tokens a split in batches of 8
        var sample = await Program.RunAsync(["allwords-sample", "--repository", root, "--batches", directory, "--per-split", "12", "--batch-size", "8"], output, error, TestContext.Current.CancellationToken);
        var table = (await File.ReadAllLinesAsync(Path.Join(directory, "sample.tsv"), TestContext.Current.CancellationToken)).Skip(1).Select(l => l.Split('\t')).ToList();

        // Both labelers choose the first sense listed by the catalog for every token, so every token is kept
        foreach (var batch in table.Select(r => r[0]).Distinct(StringComparer.Ordinal))
        {
            var lines = table.Where(r => r[0] == batch).Select(r => r[1] + "\t" + WordLexicon.SensesOf(r[6], Enum.Parse<PartOfSpeech>(r[7]))[0].SenseId + "\thigh");
            _dir.Write($"labels/{batch}.one.tsv", string.Join('\n', lines) + "\n");
            _dir.Write($"labels/{batch}.two.tsv", string.Join('\n', lines) + "\n");
        }

        var merge = await Program.RunAsync(["allwords-merge", "--batches", directory, "--first", "one", "--second", "two", "--target", Path.Join(_dir.Root, "corpus", "allwords")], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((0, 0), (sample, merge));
        Assert.Equal(24, table.Count);
        Assert.Equal(12, AllWordsGold.Read(Path.Join(_dir.Root, "corpus"), AllWordsGold.Dev).Count);
        Assert.Equal(12, AllWordsGold.Read(Path.Join(_dir.Root, "corpus"), AllWordsGold.Test).Count);
        Assert.Contains("100.00%", output.ToString(), StringComparison.Ordinal);
    }
}
