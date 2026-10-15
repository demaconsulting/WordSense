using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the corpus reader, the logistic trainer, the batch importer and the command line.
/// </summary>
public sealed class BuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wordsense-builder-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Initializes a new instance of the <see cref="BuilderTests"/> class.
    /// </summary>
    public BuilderTests()
    {
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    /// <summary>
    ///     A well-formed row is parsed with its span and plain text.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentences_ParsesRow()
    {
        var path = Write("a.tsv", "bank\tmoney\tShe went to the {bank} today.\n");
        var rows = Corpus.ReadSentences(path);
        var row = Assert.Single(rows);
        Assert.Equal("bank", row.Lemma);
        Assert.Equal("She went to the bank today.", row.PlainText);
        Assert.Equal("bank", row.PlainText[row.TargetSpan.Start..row.TargetSpan.End]);
    }

    /// <summary>
    ///     Malformed rows are rejected.
    /// </summary>
    /// <param name="line">The malformed line.</param>
    [Theory]
    [InlineData("bank\tmoney")]
    [InlineData("Bank\tmoney\tA {bank}.")]
    [InlineData("bank\tmoney\tNo target here.")]
    [InlineData("bank\tmoney\tTwo {bank} {bank}.")]
    [InlineData("bank\tmoney\tEmpty {} pair.")]
    public void WordSenseBuilder_Corpus_ReadSentences_RejectsMalformedRow(string line)
    {
        var path = Write("bad.tsv", line + "\n");
        Assert.Throws<InvalidDataException>(() => Corpus.ReadSentences(path));
    }

    /// <summary>
    ///     A missing directory yields no rows, and duplicates across sets are detected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_DirectoryAndDisjointness()
    {
        Assert.Empty(Corpus.ReadDirectory(Path.Combine(_root, "none")));
        var a = new CorpusRow("bank", "money", "A {bank}.", "a.tsv");
        var b = new CorpusRow("bank", "river", "A {bank}.", "b.tsv");
        Assert.Throws<InvalidDataException>(() => Corpus.EnsureDisjoint([a], [b]));
        Corpus.EnsureDisjoint([a], [new CorpusRow("bank", "river", "Another {bank}.", "b.tsv")]);
    }

    /// <summary>
    ///     The forms file is read with its header skipped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadForms_ParsesLabels()
    {
        var path = Write("forms.tsv", "form\tlemma\tlabels\nBanks\tbank\tmoney,river\nbank\tbank\n");
        var forms = Corpus.ReadForms(path);
        Assert.Equal(2, forms.Count);
        Assert.Equal(("banks", "bank"), (forms[0].Form, forms[0].Lemma));
        Assert.Equal(["money", "river"], forms[0].Labels);
        Assert.Empty(forms[1].Labels);
    }

    /// <summary>
    ///     The trainer separates linearly separable data and is deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic()
    {
        var features = new List<float[]>();
        var labels = new List<bool>();
        for (var i = 0; i < 20; i++)
        {
            var offset = i * 0.01f;
            features.Add([1f + offset, 0.1f]);
            labels.Add(true);
            features.Add([-1f - offset, 0.1f]);
            labels.Add(false);
        }

        var first = LogisticTrainer.Train(features, labels, 100);
        var second = LogisticTrainer.Train(features, labels, 100);
        Assert.Equal(first.Weights, second.Weights);
        Assert.Equal(first.Bias, second.Bias);
        for (var i = 0; i < features.Count; i++)
        {
            var theta = first.Weights.Append(first.Bias).ToArray();
            Assert.Equal(labels[i], LogisticTrainer.Activation(features[i], theta) > 0);
        }
    }

    /// <summary>
    ///     Batches are imported once, reported as missing or invalid when they are not ready, and are idempotent.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_ImportsValidatesAndIsIdempotent()
    {
        var source = Path.Combine(_root, "source");
        var corpus = Path.Combine(_root, "corpus");
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tmoney,river\n");
        Directory.CreateDirectory(Path.Combine(corpus, "anchors"));
        Directory.CreateDirectory(Path.Combine(corpus, "holdout"));
        Write("source/anchors/batch1.tsv", "bank\tmoney\tOne {bank}.\n");
        Write("source/holdout/batch1.tsv", "bank\tmoney\tTwo {bank}.\n");
        Write("source/anchors/batch2.tsv", "river\tmoney\tThree {river}.\n");
        Write("source/holdout/batch2.tsv", "river\tmoney\tFour {river}.\n");
        Write("source/anchors/batch3.tsv", "bank\tmoney\tFive {bank}.\n");

        var results = BatchImporter.Import(source, corpus, 3);
        Assert.Equal(["Imported", "Invalid", "Missing"], results.Select(r => r.Status));
        Assert.True(File.Exists(Path.Combine(corpus, "anchors", "speech-batch1.tsv")));
        Assert.False(File.Exists(Path.Combine(corpus, "anchors", "speech-batch2.tsv")));
        Assert.DoesNotContain("\r", File.ReadAllText(Path.Combine(corpus, "holdout", "speech-batch1.tsv")), StringComparison.Ordinal);

        var again = BatchImporter.Import(source, corpus, 1);
        Assert.Equal("AlreadyImported", Assert.Single(again).Status);
    }

    /// <summary>
    ///     A specified lemma without sentences is a warning, not a batch failure.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_MissingLemmaIsWarning()
    {
        var corpus = PrepareCorpus();
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tmoney,river\nrare\tnoun,verb\n");
        Write("source/anchors/batch1.tsv", Anchors("bank"));
        Write("source/holdout/batch1.tsv", "bank\tmoney\tHold {bank} one.\nbank\triver\tHold {bank} two.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));
        Assert.Equal("Imported", result.Status);
        Assert.Contains(result.Notes!, n => n.StartsWith("WARNING", StringComparison.Ordinal) && n.Contains("rare", StringComparison.Ordinal));
        Assert.Equal(8, File.ReadAllLines(Path.Combine(corpus, "anchors", "speech-batch1.tsv")).Length);
    }

    /// <summary>
    ///     Row-level errors drop only the offending rows when they are few.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_DropsFewBadRows()
    {
        var corpus = PrepareCorpus();
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tmoney,river\n");
        var anchors = new StringBuilder(Anchors("bank"));
        for (var i = 0; i < 100; i++)
        {
            anchors.Append("bank\tmoney\tFiller {bank} ").Append(i).Append(".\n");
        }

        // Two bad rows in 100+ rows: a disallowed label and a malformed line
        anchors.Append("bank\tfinance\tBad {bank} label.\n").Append("bank\tmoney\tNo target.\n");
        Write("source/anchors/batch1.tsv", anchors.ToString());
        Write("source/holdout/batch1.tsv", "bank\tmoney\tFiller {bank} 1.\nbank\tmoney\tFresh {bank} one.\nnovel\tmoney\tOdd {novel} one.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));
        Assert.Equal("Imported", result.Status);
        Assert.Equal(4, result.Notes!.Count(n => n.StartsWith("dropped", StringComparison.Ordinal)));
        var holdout = File.ReadAllText(Path.Combine(corpus, "holdout", "speech-batch1.tsv"));
        Assert.Contains("Fresh {bank} one.", holdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Filler", holdout, StringComparison.Ordinal);
        Assert.DoesNotContain("novel", holdout, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Too many row errors reject the whole batch.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_RejectsBatchWithManyBadRows()
    {
        var corpus = PrepareCorpus();
        Write("source/anchors/batch1.tsv", Anchors("bank") + "bank\tmoney\tBroken.\n");
        Write("source/holdout/batch1.tsv", "bank\tmoney\tHold {bank} one.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));
        Assert.Equal("Invalid", result.Status);
        Assert.False(File.Exists(Path.Combine(corpus, "anchors", "speech-batch1.tsv")));
    }

    /// <summary>
    ///     Lemmas with fewer than two senses or fewer than four anchors for a sense are excluded; so are holdout-only lemmas.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_ExcludesThinLemmas()
    {
        var corpus = PrepareCorpus();
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tmoney,river\nthin\tthin\tmoney,river\nmono\tmono\tmoney\nhold\thold\tmoney,river\n");
        var anchors = new StringBuilder(Anchors("bank"));
        for (var i = 0; i < 3; i++)
        {
            anchors.Append("thin\tmoney\tThin {thin} m").Append(i).Append(".\nthin\triver\tThin {thin} r").Append(i).Append(".\n");
            anchors.Append("mono\tmoney\tMono {mono} m").Append(i).Append(".\n");
        }

        Write("source/anchors/batch1.tsv", anchors.ToString());
        Write("source/holdout/batch1.tsv", "bank\tmoney\tHold {bank} one.\nthin\tmoney\tHold {thin} one.\nhold\tmoney\tHold {hold} one.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));
        Assert.Equal("Imported", result.Status);
        Assert.Equal(3, result.Notes!.Count(n => n.StartsWith("EXCLUDED", StringComparison.Ordinal)));
        var text = File.ReadAllText(Path.Combine(corpus, "anchors", "speech-batch1.tsv")) + File.ReadAllText(Path.Combine(corpus, "holdout", "speech-batch1.tsv"));
        Assert.DoesNotContain("thin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mono", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{hold}", text, StringComparison.Ordinal);
        Assert.Contains("{bank}", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command line reports usage and data errors with exit code 1.
    /// </summary>
    /// <param name="args">The arguments.</param>
    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("build", "--model")]
    [InlineData("build", "--corpus", "x")]
    [InlineData("import-batches", "--source", "missing-dir", "--corpus", "missing-dir")]
    public void WordSenseBuilder_Program_Run_ReportsErrors(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, Program.Run(args, output, error));
        Assert.NotEmpty(error.ToString());
    }

    private string PrepareCorpus()
    {
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tmoney,river\nnovel\tnovel\tmoney\nhold\thold\tmoney,river\n");
        Directory.CreateDirectory(Path.Combine(_root, "corpus", "anchors"));
        Directory.CreateDirectory(Path.Combine(_root, "corpus", "holdout"));
        return Path.Combine(_root, "corpus");
    }

    private static string Anchors(string lemma)
    {
        var text = new StringBuilder();
        for (var i = 0; i < 4; i++)
        {
            text.Append(lemma).Append("\tmoney\tMoney {").Append(lemma).Append("} ").Append(i).Append(".\n");
            text.Append(lemma).Append("\triver\tRiver {").Append(lemma).Append("} ").Append(i).Append(".\n");
        }

        return text.ToString();
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
