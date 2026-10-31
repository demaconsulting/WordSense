using System.Text;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the trainer of the tense head, with a synthetic corpus in which the tense of a same-spelling verb is
///     given by the adverb after it, and an encoder that returns a vector for each word without a model file.
/// </summary>
public sealed class TenseHeadTrainerTests : IDisposable
{
    private const int Dimension = 8;
    private const string Header = "# corpus = test\n# license = test\n# columns = id form lemma upos collapsed feats consensus\n\n";

    private static readonly string[] Determiners = ["the", "a"];
    private static readonly string[] Nouns = ["dog", "cat", "man", "tree"];
    private static readonly string[] Verbs = ["put", "cut", "let"];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string Sentence(int id, string determiner, string noun, string verb, bool past)
    {
        (string Form, string Upos, string Feats)[] tokens =
        [
            (determiner, "DET", "_"),
            (noun, "NOUN", "_"),
            (verb, "VERB", past ? "Tense=Past|VerbForm=Fin" : "Tense=Pres|VerbForm=Fin"),
            (past ? "yesterday" : "today", "ADV", "_"),
            (".", "PUNCT", "_"),
        ];
        var text = new StringBuilder();
        text.Append("# sent_id = s").Append(id).Append("\n# source_id = d").Append(id / 10).Append("\n# text = ").Append(string.Join(' ', tokens.Select(t => t.Form))).Append('\n');
        for (var i = 0; i < tokens.Length; i++)
        {
            text.Append(i + 1).Append('\t').Append(tokens[i].Form).Append('\t').Append(tokens[i].Form).Append('\t').Append(tokens[i].Upos).Append("\t_\t").Append(tokens[i].Feats).Append("\tunanimous\n");
        }

        return text.Append('\n').ToString();
    }

    private string WriteCorpus()
    {
        var train = new StringBuilder(Header);
        var dev = new StringBuilder(Header);
        var id = 0;
        foreach (var d in Determiners)
        {
            foreach (var n in Nouns)
            {
                foreach (var v in Verbs)
                {
                    foreach (var past in new[] { false, true })
                    {
                        (id % 4 == 0 ? dev : train).Append(Sentence(id, d, n, v, past));
                        id++;
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

    private static ValueTask<double[]?[]> Encode(IReadOnlyList<string> words, CancellationToken _) =>
        ValueTask.FromResult(words.Select(w => (double[]?)VectorOf(w)).ToArray());

    private static PosEncoderInfo Encoder() => new("synthetic", Dimension, new string('a', 64), new string('b', 64), true, "test");

    private async Task<PosHeadData> PosHeadAsync(string corpus)
    {
        var request = new PosHeadRequest(corpus, Path.Join(_dir.Root, "data"), null, null, null, null, [0.1], false, 1);
        var outcome = await PosHeadTrainer.TrainAsync(request, Encoder(), Encode, TextWriter.Null, TestContext.Current.CancellationToken);
        return PosHeadBlob.Read(outcome.Blob);
    }

    private TenseHeadRequest Request(string corpus, double[]? grid = null, bool fusion = false) =>
        new(corpus, Path.Join(_dir.Root, "data"), null, null, null, grid ?? [0.1, 1], fusion);

    /// <summary>
    ///     The tense head is trained, reads back through the library reader with the part-of-speech section unchanged,
    ///     chooses the layout with the neighbors (the word alone cannot tell the tense here), predicts the tense and writes the
    ///     same bytes on every run.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_TenseHeadTrainer_TrainAsync_ProducesALoadableDeterministicHead()
    {
        // Arrange
        var corpus = WriteCorpus();
        var existing = await PosHeadAsync(corpus);

        // Act
        var first = await TenseHeadTrainer.TrainAsync(Request(corpus, fusion: true), Encoder(), existing, Encode, TextWriter.Null, TestContext.Current.CancellationToken);
        var second = await TenseHeadTrainer.TrainAsync(Request(corpus), Encoder(), existing, Encode, TextWriter.Null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Blob, second.Blob);
        var data = PosHeadBlob.Read(first.Blob);
        Assert.NotNull(data.Tense);
        Assert.Equal(PosHeadBlob.TenseWordAndNeighbors, data.Tense.Layout);
        Assert.Equal(existing.Weights, data.Weights);
        Assert.Equal(existing.Biases, data.Biases);
        Assert.Equal(existing.TrainSha256, data.TrainSha256);
        var head = new PosHead(data).Tense!;
        Assert.True(head.PastProbability(VectorOf("dog"), VectorOf("put"), VectorOf("yesterday")) > 0.9);
        Assert.True(head.PastProbability(VectorOf("dog"), VectorOf("cut"), VectorOf("today")) < 0.1);
        Assert.Contains("Selected: word+-1", first.Report, StringComparison.Ordinal);
        Assert.Contains("Half precision", first.Report, StringComparison.Ordinal);
        Assert.Contains("Combination with the Basic model", first.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("Combination with the Basic model", second.Report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The rows of the head are the gold verbs whose form has a past and a non-past verb analysis, with their class.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TenseHeadTrainer_Rows_AreTheSameSpellingVerbsWithTheirGoldTense()
    {
        // Arrange: the training sentences of the synthetic corpus and a table of vectors for them
        var corpus = WriteCorpus();
        var sentences = PosHeadTrainer.ReadSentences(corpus, "train");
        var count = sentences.Sum(s => s.Words.Length);
        var table = new PosTokenTable(Dimension, new float[count * Dimension], new int[count], new int[count], [.. Enumerable.Repeat(true, count)]);

        // Act
        var rows = TenseHeadTrainer.Rows(table, sentences);

        // Assert: one row per sentence, at the verb, labeled by the feature of the gold
        Assert.Equal(sentences.Count, rows.Count);
        Assert.Equal(sentences.Count(s => s.Words.Contains("yesterday")), rows.PastCount);
        Assert.All(rows.Forms, f => Assert.Contains(f, Verbs));
        Assert.All(Enumerable.Range(0, rows.Count), r => Assert.Equal(sentences[r].Words.Contains("yesterday"), rows.Past[r]));
    }

    /// <summary>
    ///     A head file that was not trained on the representative training split of the corpus, or that belongs to another
    ///     encoder, is refused.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_TenseHeadTrainer_TrainAsync_ForeignHeadFile_Refused()
    {
        // Arrange
        var corpus = WriteCorpus();
        var existing = await PosHeadAsync(corpus);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => TenseHeadTrainer.TrainAsync(Request(corpus), Encoder(), existing with { TrainSha256 = new byte[PosHeadBlob.HashSize] }, Encode, TextWriter.Null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => TenseHeadTrainer.TrainAsync(Request(corpus), Encoder(), existing with { ModelId = "other" }, Encode, TextWriter.Null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A training sentence that is in the development split stops the run before it fits anything.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_TenseHeadTrainer_TrainAsync_TrainingSentenceInAHeldOutPart_Aborts()
    {
        // Arrange: the development split gets a copy of a training sentence (the same text under another id)
        var corpus = WriteCorpus();
        var existing = await PosHeadAsync(corpus);
        _dir.Write("corpus/open/representative/data/dev.tsv", Header + Sentence(1000, "the", "dog", "put", true));

        // Act
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TenseHeadTrainer.TrainAsync(Request(corpus), Encoder(), existing, Encode, TextWriter.Null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("open corpus dev", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corpus without a tense row on the development split cannot select anything and is refused.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_TenseHeadTrainer_TrainAsync_NoDevelopmentRows_Refused()
    {
        // Arrange: a development split without a same-spelling verb
        var corpus = WriteCorpus();
        var existing = await PosHeadAsync(corpus);
        _dir.Write("corpus/open/representative/data/dev.tsv", Header + "# sent_id = x1\n# source_id = dx\n# text = the dog sleeps .\n1\tthe\tthe\tDET\t_\t_\tunanimous\n2\tdog\tdog\tNOUN\t_\t_\tunanimous\n3\tsleeps\tsleep\tVERB\t_\t_\tunanimous\n4\t.\t.\tPUNCT\t_\t_\tunanimous\n\n");

        // Act / Assert: the run stops because there is nothing to select with
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TenseHeadTrainer.TrainAsync(Request(corpus), Encoder(), existing, Encode, TextWriter.Null, TestContext.Current.CancellationToken));
        Assert.Contains("development rows", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The training settings are validated: an empty grid, a non-positive C and a layout that does not exist.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TenseHeadTrainer_Fit_InvalidSettings_Throw()
    {
        // Arrange
        var table = new PosTokenTable(Dimension, new float[2 * Dimension], [0, 0], [0, 0], [true, true]);
        var rows = new TenseRows([0, 1], [true, false], ["put", "put"]);

        // Act / Assert
        Assert.Throws<ArgumentException>(() => TenseHeadTrainer.Fit(table, rows, PosHeadBlob.TenseWord, 0));
        Assert.Throws<ArgumentException>(() => TenseHeadTrainer.Fit(table, rows, PosHeadBlob.TenseWord, double.NaN));
        Assert.Throws<ArgumentException>(() => TenseHeadTrainer.Fit(table, rows, 7, 1));
        Assert.Throws<ArgumentException>(() => TenseHeadTrainer.Fit(table, new TenseRows([], [], []), PosHeadBlob.TenseWord, 1));
    }
}
