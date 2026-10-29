using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the trainer of the part-of-speech head, with a synthetic corpus and an encoder that returns a vector
///     for each word without a model file.
/// </summary>
public sealed class PosHeadTrainerTests : IDisposable
{
    private const int Dimension = 8;
    private const string Header = "# corpus = test\n# license = test\n# columns = id form lemma upos collapsed feats consensus\n\n";
    private const string ReadSetHeader = "id\tsplit\tlabel\ttokens\t" + "idx" + "\tfamily\tsentence\n";

    private static readonly (string Form, string Upos)[] Determiners = [("the", "DET"), ("a", "DET")];
    private static readonly (string Form, string Upos)[] Adjectives = [("old", "ADJ"), ("big", "ADJ"), ("small", "ADJ")];
    private static readonly (string Form, string Upos)[] Nouns = [("dog", "NOUN"), ("cat", "NOUN"), ("man", "NOUN"), ("tree", "NOUN")];
    private static readonly (string Form, string Upos)[] Verbs = [("runs", "VERB"), ("sleeps", "VERB"), ("eats", "VERB")];
    private static readonly (string Form, string Upos)[] Adverbs = [("quickly", "ADV"), ("slowly", "ADV")];

    private readonly TestDirectory _dir = new();
    private int _encodes;

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string SentenceOf(int id, params (string Form, string Upos)[] tokens) => SentenceFrom(id, tokens);

    private static string SentenceFrom(int id, IReadOnlyList<(string Form, string Upos)> tokens)
    {
        var text = new StringBuilder();
        text.Append("# sent_id = s").Append(id).Append("\n# source_id = d").Append(id / 10).Append("\n# text = ").Append(string.Join(' ', tokens.Select(t => t.Form))).Append('\n');
        for (var i = 0; i < tokens.Count; i++)
        {
            text.Append(i + 1).Append('\t').Append(tokens[i].Form).Append('\t').Append(tokens[i].Form).Append('\t').Append(tokens[i].Upos).Append("\t_\t_\tunanimous\n");
        }

        return text.Append('\n').ToString();
    }

    /// <summary>
    ///     Writes a corpus whose sentences are the combinations of the word lists; every fourth combination is a
    ///     development sentence and the others are training sentences.
    /// </summary>
    private string WriteCorpus()
    {
        var train = new StringBuilder(Header);
        var dev = new StringBuilder(Header);
        var id = 0;
        foreach (var d in Determiners)
        {
            foreach (var a in Adjectives)
            {
                foreach (var n in Nouns)
                {
                    foreach (var v in Verbs)
                    {
                        foreach (var r in Adverbs)
                        {
                            var sentence = SentenceOf(id, d, a, n, v, r, (".", "PUNCT"));
                            (id % 4 == 0 ? dev : train).Append(sentence);
                            id++;
                        }
                    }
                }
            }
        }

        _dir.Write("corpus/open/representative/data/train.tsv", train.ToString());
        _dir.Write("corpus/open/representative/data/dev.tsv", dev.ToString());
        return Path.Join(_dir.Root, "corpus");
    }

    private static double[] VectorOf(string word)
    {
        var random = new Random(StringComparer.Ordinal.GetHashCode(word.ToLowerInvariant()));
        return [.. Enumerable.Range(0, Dimension).Select(_ => (random.NextDouble() * 2) - 1)];
    }

    private ValueTask<double[]?[]> Encode(IReadOnlyList<string> words, CancellationToken _)
    {
        _encodes++;
        return ValueTask.FromResult(words.Select(w => (double[]?)VectorOf(w)).ToArray());
    }

    private static PosEncoderInfo Encoder() => new("synthetic", Dimension, new string('a', 64), new string('b', 64), true, "test");

    private PosHeadRequest Request(string corpus, string? cache = null, string? gold = null, string? readSet = null, string? train = null, int parallelism = 0, double[]? grid = null) =>
        new(corpus, Path.Join(_dir.Root, "data"), train, gold, readSet, cache, grid ?? [0.1, 1], false, parallelism);

    /// <summary>
    ///     A head is trained, reads back through the library reader, learns the part of speech of the words and writes the
    ///     same bytes on every run, whatever the number of threads.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_ProducesALoadableDeterministicHead()
    {
        // Arrange
        var corpus = WriteCorpus();

        // Act
        var first = await PosHeadTrainer.TrainAsync(Request(corpus, parallelism: 1), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);
        var second = await PosHeadTrainer.TrainAsync(Request(corpus, parallelism: 6), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Blob, second.Blob);
        var head = new PosHead(PosHeadBlob.Read(first.Blob));
        Assert.Equal("synthetic", head.Data.ModelId);
        Assert.Equal(Enum.GetNames<PartOfSpeech>(), head.Data.ClassNames);
        Assert.Equal("word+-1", head.Data.Variant);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Join(corpus, "open", "representative", "data", "train.tsv"), TestContext.Current.CancellationToken)), head.Data.TrainSha256);
        Assert.Equal(PartOfSpeech.Noun, BestClass(head.Posterior(VectorOf("old"), VectorOf("dog"), VectorOf("runs"))));
        Assert.Equal(PartOfSpeech.Verb, BestClass(head.Posterior(VectorOf("dog"), VectorOf("runs"), VectorOf("quickly"))));
        Assert.Equal(PartOfSpeech.Adjective, BestClass(head.Posterior(VectorOf("a"), VectorOf("small"), VectorOf("tree"))));
        Assert.Contains("Selected C", first.Report, StringComparison.Ordinal);
        Assert.Contains("Half precision", first.Report, StringComparison.Ordinal);
    }

    private static PartOfSpeech BestClass(double[] posterior) => (PartOfSpeech)Array.IndexOf(posterior, posterior.Max());

    /// <summary>
    ///     The vector cache is used on a second run, which then writes the same bytes without encoding anything.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_SecondRunUsesTheCache()
    {
        var corpus = WriteCorpus();
        var cache = Path.Join(_dir.Root, "cache");

        var first = await PosHeadTrainer.TrainAsync(Request(corpus, cache), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);
        var encodedOnce = _encodes;
        var second = await PosHeadTrainer.TrainAsync(Request(corpus, cache), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.True(encodedOnce > 0);
        Assert.Equal(encodedOnce, _encodes);
        Assert.Equal(first.Blob, second.Blob);
    }

    /// <summary>
    ///     Every C of the grid is fitted and reported; when several give the same dev accuracy the smallest wins.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_TieGoesToTheSmallestC()
    {
        var corpus = WriteCorpus();

        var outcome = await PosHeadTrainer.TrainAsync(Request(corpus, grid: [1, 0.5, 2]), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(0.5, outcome.C);
        Assert.Equal(0.5f, new PosHead(PosHeadBlob.Read(outcome.Blob)).Data.C);
        Assert.Contains("| 0.5 |", outcome.Report, StringComparison.Ordinal);
        Assert.Contains("| 1 |", outcome.Report, StringComparison.Ordinal);
        Assert.Contains("| 2 |", outcome.Report, StringComparison.Ordinal);
        Assert.Equal(3, outcome.Report.Split('\n').Count(l => l.Contains("100.00%", StringComparison.Ordinal) && l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| C", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     A training sentence that is in a treebank file, in the read set or in the development split stops the run
    ///     before anything is encoded.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_TrainingSentenceInAHeldOutPart_Aborts()
    {
        // Arrange: the first training sentence, as a treebank gold sentence, as a read-set sentence and, last, in the dev split
        var corpus = WriteCorpus();
        var sentence = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "train")[0];
        var gold = _dir.Write("gold/ud_dev.tsv", string.Concat(sentence.Tokens.Select(t => "1\t" + t.Form + "\tN\n")));
        var readSet = _dir.Write("read-set.tsv", ReadSetHeader + "r1\tdev\tPAST\tx\t0\tf\t" + sentence.Text + "\n");

        // Act
        var viaGold = await Assert.ThrowsAsync<InvalidDataException>(() => PosHeadTrainer.TrainAsync(Request(corpus, gold: Path.GetDirectoryName(gold)), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));
        var viaRead = await Assert.ThrowsAsync<InvalidDataException>(() => PosHeadTrainer.TrainAsync(Request(corpus, readSet: readSet), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));
        _dir.Write("corpus/open/representative/data/dev.tsv", Header + SentenceFrom(1, [.. sentence.Tokens.Select(t => (t.Form, t.Upos))]));
        var viaDev = await Assert.ThrowsAsync<InvalidDataException>(() => PosHeadTrainer.TrainAsync(Request(corpus), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("UD-EWT dev", viaGold.Message, StringComparison.Ordinal);
        Assert.Contains("read set", viaRead.Message, StringComparison.Ordinal);
        Assert.Contains("open corpus dev", viaDev.Message, StringComparison.Ordinal);
        Assert.Equal(0, _encodes);
    }

    /// <summary>
    ///     A run of eight words that a training sentence shares with a treebank sentence also stops the run.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_SharedRunOfWordsWithTheTreebank_Aborts()
    {
        // Arrange: a long training sentence and a treebank sentence that contains nine of its words
        var corpus = WriteCorpus();
        (string, string)[] tokens = [.. "the old dog runs and the big cat sleeps today".Split(' ').Select(w => (w, "NOUN"))];
        await File.AppendAllTextAsync(Path.Join(corpus, "open", "representative", "data", "train.tsv"), SentenceOf(9000, tokens), TestContext.Current.CancellationToken);
        var gold = _dir.Write("gold/ud_test.tsv", string.Concat("x runs and the old dog runs and the big cat sleeps today there".Split(' ').Select(w => "7\t" + w + "\tN\n")));

        // Act
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PosHeadTrainer.TrainAsync(Request(corpus, gold: Path.GetDirectoryName(gold)), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("run of 8 words", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A training file other than the representative training split is refused.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_PosHeadTrainer_TrainAsync_OtherTrainingFile_Refused()
    {
        var corpus = WriteCorpus();
        var other = _dir.Write("other/train.tsv", Header);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PosHeadTrainer.TrainAsync(Request(corpus, train: other), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));

        Assert.Contains("representative training split", error.Message, StringComparison.Ordinal);
        var own = Path.Join(corpus, "open", "representative", "data", "train.tsv");
        Assert.NotNull(await PosHeadTrainer.TrainAsync(Request(corpus, train: own), Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The gold class of a token is its part of speech: the infinitive marker is a preposition, punctuation and other
    ///     tokens are excluded.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadTrainer_ReadSentences_MapsTheGoldClasses()
    {
        _dir.Write(
            "corpus/open/representative/data/train.tsv",
            Header + SentenceOf(1, ("They", "PRON"), ("want", "VERB"), ("to", "PART"), ("go", "VERB"), ("2", "NUM"), (".", "PUNCT")));

        var sentence = Assert.Single(PosHeadTrainer.ReadSentences(Path.Join(_dir.Root, "corpus"), "train"));

        Assert.Equal([1, 2, 5, 2, -1, -1], sentence.Gold);
    }

    /// <summary>
    ///     Scoring restricts a posterior to the parts of speech of the lexicon, counts ambiguous words and skips tokens
    ///     that are not scored.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadTrainer_Score_AppliesTheLexiconRestriction()
    {
        // Arrange: three tokens; the first is a noun/verb word the head thinks is an adjective, the second an unambiguous noun, the third unknown
        var table = new PosTokenTable(1, [0, 0, 0], [0, 0, 0], [(int)PartOfSpeech.Verb, (int)PartOfSpeech.Noun, (int)PartOfSpeech.Noun], [true, true, true]);
        double[][] posteriors =
        [
            [0.2, 0, 0.3, 0.5, 0, 0, 0, 0, 0, 0],
            [0.9, 0, 0.1, 0, 0, 0, 0, 0, 0, 0],
            [1, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ];
        int[] masks = [(1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb), 1 << (int)PartOfSpeech.Noun, 0];

        // Act
        var score = PosHeadTrainer.Score(posteriors, table, masks);

        // Assert: restricted to noun and verb the first token is a verb (0.3 against 0.2)
        Assert.Equal(2, score.Scored);
        Assert.Equal(2, score.Correct);
        Assert.Equal(1, score.UnrestrictedCorrect);
        Assert.Equal(1, score.Ambiguous);
        Assert.Equal(1, score.AmbiguousCorrect);
        Assert.Equal([2, 0, -1], score.Predicted);
        Assert.Equal(100.0, score.Accuracy);
        Assert.Equal(50.0, score.UnrestrictedAccuracy);
    }

    /// <summary>
    ///     The half-precision gate accepts a change of the dev accuracy up to 0.02 points and stops the run beyond it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadTrainer_CheckHalfPrecision_StopsBeyondTheGate()
    {
        PosHeadTrainer.CheckHalfPrecision(96.99, 96.99);
        PosHeadTrainer.CheckHalfPrecision(96.99, 96.97);
        PosHeadTrainer.CheckHalfPrecision(96.97, 96.99);

        var error = Assert.Throws<InvalidDataException>(() => PosHeadTrainer.CheckHalfPrecision(96.99, 96.90));
        Assert.Throws<InvalidDataException>(() => PosHeadTrainer.CheckHalfPrecision(96.0, 96.5));
        Assert.Contains("0.090 points", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The lexicon mask of a word lists its parts of speech, and is zero for an unknown word and for punctuation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosHeadTrainer_LexiconMask_ListsThePartsOfSpeech()
    {
        var record = PosHeadTrainer.LexiconMask("Record");

        Assert.Equal((1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb), record & ((1 << (int)PartOfSpeech.Noun) | (1 << (int)PartOfSpeech.Verb)));
        Assert.Equal(0, PosHeadTrainer.LexiconMask("xyzzyq"));
        Assert.Equal(0, PosHeadTrainer.LexiconMask("."));
    }
}
