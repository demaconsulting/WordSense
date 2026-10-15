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
    ///     A well-formed row is parsed with its words and target word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentences_ParsesRow()
    {
        var path = Write("a.tsv", "bank\tmoney\tShe went to the {bank} today.\n");
        var rows = Corpus.ReadSentences(path);
        var row = Assert.Single(rows);
        Assert.Equal("bank", row.Lemma);
        Assert.Equal(["She", "went", "to", "the", "bank", "today", "."], row.Words);
        Assert.Equal("bank", row.Words[row.TargetWordIndex]);
    }

    /// <summary>
    ///     A target inside a hyphenated or possessive word splits that word at the braces.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="words">The expected words, joined by a space.</param>
    /// <param name="target">The expected target word.</param>
    [Theory]
    [InlineData("It was a big-{lunged} dog.", "It was a big- lunged dog .", "lunged")]
    [InlineData("The {regiment}'s flag fell.", "The regiment 's flag fell .", "regiment")]
    [InlineData("The y-{intercept} is two.", "The y- intercept is two .", "intercept")]
    public void WordSenseBuilder_Corpus_ReadSentences_SplitsCompoundTarget(string sentence, string words, string target)
    {
        var row = Assert.Single(Corpus.ReadSentences(Write("c.tsv", "bank\tmoney\t" + sentence + "\n")));

        Assert.Equal(words, string.Join(' ', row.Words));
        Assert.Equal(target, row.Words[row.TargetWordIndex]);
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
        var a = Row("bank", "money", "A {bank}.", "a.tsv");
        var b = Row("bank", "river", "A {bank}.", "b.tsv");
        Assert.Throws<InvalidDataException>(() => Corpus.EnsureDisjoint([a], [b]));
        Corpus.EnsureDisjoint([a], [Row("bank", "river", "Another {bank}.", "b.tsv")]);
    }

    private CorpusRow Row(string lemma, string sense, string sentence, string file)
    {
        var path = Write("rows/" + Guid.NewGuid().ToString("N") + "/" + file, $"{lemma}\t{sense}\t{sentence}\n");
        return Assert.Single(Corpus.ReadSentences(path));
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
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tnoun,verb\n");
        Directory.CreateDirectory(Path.Combine(corpus, "anchors"));
        Directory.CreateDirectory(Path.Combine(corpus, "holdout"));
        Write("source/anchors/batch1.tsv", "bank\tnoun\tOne {bank}.\n");
        Write("source/holdout/batch1.tsv", "bank\tnoun\tTwo {bank}.\n");
        Write("source/anchors/batch2.tsv", "river\tnoun\tThree {river}.\n");
        Write("source/holdout/batch2.tsv", "river\tnoun\tFour {river}.\n");
        Write("source/anchors/batch3.tsv", "bank\tnoun\tFive {bank}.\n");

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
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tnoun,verb\nrare\tnoun,verb\n");
        Write("source/anchors/batch1.tsv", Anchors("bank"));
        Write("source/holdout/batch1.tsv", "bank\tnoun\tHold {bank} one.\nbank\tverb\tHold {bank} two.\n");

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
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tnoun,verb\n");
        var anchors = new StringBuilder(Anchors("bank"));
        for (var i = 0; i < 100; i++)
        {
            anchors.Append("bank\tnoun\tFiller {bank} ").Append(i).Append(".\n");
        }

        // Two bad rows in 100+ rows: a disallowed label and a malformed line
        anchors.Append("bank\tadjective\tBad {bank} label.\n").Append("bank\tnoun\tNo target.\n");
        Write("source/anchors/batch1.tsv", anchors.ToString());
        Write("source/holdout/batch1.tsv", "bank\tnoun\tFiller {bank} 1.\nbank\tnoun\tFresh {bank} one.\nnovel\tnoun\tOdd {novel} one.\n");

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
        Write("source/anchors/batch1.tsv", Anchors("bank") + "bank\tnoun\tBroken.\n");
        Write("source/holdout/batch1.tsv", "bank\tnoun\tHold {bank} one.\n");

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
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tnoun,verb\nthin\tthin\tnoun,verb\nmono\tmono\tnoun\nhold\thold\tnoun,verb\n");
        var anchors = new StringBuilder(Anchors("bank"));
        for (var i = 0; i < 3; i++)
        {
            anchors.Append("thin\tnoun\tThin {thin} m").Append(i).Append(".\nthin\tverb\tThin {thin} r").Append(i).Append(".\n");
            anchors.Append("mono\tnoun\tMono {mono} m").Append(i).Append(".\n");
        }

        Write("source/anchors/batch1.tsv", anchors.ToString());
        Write("source/holdout/batch1.tsv", "bank\tnoun\tHold {bank} one.\nthin\tnoun\tHold {thin} one.\nhold\tnoun\tHold {hold} one.\n");

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
    ///     An imported batch also writes its senses, with derived tags, to the inventory.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_WritesDerivedInventory()
    {
        var corpus = PrepareCorpus();
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tnoun,verb\n");
        Write("source/anchors/batch1.tsv", Anchors("bank"));
        Write("source/holdout/batch1.tsv", "bank\tnoun\tHold {bank} one.\nbank\tverb\tHold {bank} two.\n");

        Assert.Equal("Imported", Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1)).Status);

        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        Assert.Equal(["bank/noun/noun", "bank/verb/verb"], inventory.Rows.Select(r => $"{r.Lemma}/{r.Sense}/{string.Join('|', r.Tags)}"));
        Assert.DoesNotContain("\r", File.ReadAllText(Path.Combine(corpus, "senses", "speech-batch1.tsv")), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense label without a derivation rule makes the batch invalid and writes nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_UnmappedLabel_IsInvalid()
    {
        var corpus = PrepareCorpus();
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tmoney,river\n");
        Write("source/anchors/batch1.tsv", Anchors("bank").Replace("noun", "money", StringComparison.Ordinal).Replace("verb", "river", StringComparison.Ordinal));
        Write("source/holdout/batch1.tsv", "bank\tmoney\tHold {bank} one.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));

        Assert.Equal("Invalid", result.Status);
        Assert.False(File.Exists(Path.Combine(corpus, "senses", "speech-batch1.tsv")));
        Assert.False(File.Exists(Path.Combine(corpus, "anchors", "speech-batch1.tsv")));
    }

    /// <summary>
    ///     Part-of-speech labels, descriptive labels and the lemma override derive the documented tags.
    /// </summary>
    [Theory]
    [InlineData("bank", "noun", "noun")]
    [InlineData("bank", "adv", "adv")]
    [InlineData("read", "past", "past|verb")]
    [InlineData("read", "present", "present|verb")]
    [InlineData("bass", "fish", "noun")]
    [InlineData("row", "paddle", "verb")]
    [InlineData("bow", "bend", "noun|verb")]
    [InlineData("lead", "guide", "noun|verb")]
    [InlineData("tear", "rip", "noun|verb")]
    [InlineData("used", "verb", "past|verb")]
    [InlineData("use", "verb", "verb")]
    public void WordSenseBuilder_SenseTagDerivation_Derive_AppliesRules(string lemma, string sense, string expected)
    {
        Assert.Equal(expected, string.Join('|', SenseTagDerivation.Derive(lemma, sense)));
    }

    /// <summary>
    ///     A label without a rule is an error, and derivation from the same anchors is deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseTagDerivation_UnmappedLabel_ThrowsAndDeriveFilesIsDeterministic()
    {
        Assert.Throws<InvalidDataException>(() => SenseTagDerivation.Derive("bank", "money"));

        var rows = new[] { Row("bank", "verb", "A {bank}.", "b.tsv"), Row("bank", "noun", "B {bank}.", "a.tsv"), Row("bank", "noun", "C {bank}.", "a.tsv") };
        var first = SenseTagDerivation.DeriveFiles(rows);
        var second = SenseTagDerivation.DeriveFiles(rows.Reverse());

        Assert.Equal(["a.tsv", "b.tsv"], first.Keys);
        Assert.Single(first["a.tsv"]);
        Assert.Equal(first.Keys, second.Keys);
    }

    /// <summary>
    ///     The inventory rejects malformed files with every problem found.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ReadDirectory_RejectsMalformedRows()
    {
        var dir = Path.Combine(_root, "inv");
        Write("inv/a.tsv", "lemma\tsense\ttags\nbank\tnoun\tnoun\nbank\tnoun\tnoun\nbank\tverb\tverb|noun\nbank\tadj\tAdj\nBank\tx\tnoun\nbank\ty\n");
        Write("inv/b.tsv", "wrong header\n");

        var ex = Assert.Throws<InvalidDataException>(() => SenseInventory.ReadDirectory(dir));

        Assert.Contains("already listed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sorted", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lower-case words", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lemma must be lower-case", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3 tab-separated columns", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b.tsv:1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The inventory round-trips through its file format.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_WriteAndRead_RoundTrips()
    {
        var path = Path.Combine(_root, "inv", "x.tsv");
        SenseInventory.WriteFile(path, [new InventoryRow("bow", "bend", ["noun", "verb"], "x.tsv"), new InventoryRow("bow", "weapon", ["noun"], "x.tsv")]);

        var inventory = SenseInventory.ReadDirectory(Path.Combine(_root, "inv"));

        Assert.Equal(2, inventory.Rows.Count());
        Assert.Equal(1, inventory.LemmaCount);
        Assert.True(inventory.Contains("bow", "bend"));
        Assert.False(inventory.Contains("bow", "x"));
        Assert.Equal(["bend", "weapon"], inventory.DefinitionsOf("bow").Select(d => d.Name));
        Assert.Equal(["noun", "verb"], inventory.DefinitionsOf("bow")[0].Tags);
    }

    /// <summary>
    ///     Validation reports anchors without an inventory entry, entries without anchors, holdout senses and forms labels that are no senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_ChecksBothDirections()
    {
        var inventory = new SenseInventory([new InventoryRow("bank", "noun", ["noun"], "s.tsv"), new InventoryRow("bank", "orphan", ["noun"], "s.tsv")]);
        var anchors = new[] { Row("bank", "noun", "A {bank}.", "a.tsv"), Row("bank", "verb", "B {bank}.", "a.tsv") };
        var holdout = new[] { Row("bank", "adj", "C {bank}.", "h.tsv") };
        var forms = new List<(string Form, string Lemma, string[] Labels)>
        {
            ("banks", "bank", ["noun", "money"]),
            ("other", "other", ["whatever"]),
        };

        var ex = Assert.Throws<InvalidDataException>(() => inventory.Validate(anchors, holdout, forms));

        Assert.Contains("'bank/verb' has anchor sentences but is not in the sense inventory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'bank/orphan' has no anchor sentences", ex.Message, StringComparison.Ordinal);
        Assert.Contains("holdout/h.tsv: sense 'bank/adj' is not in the sense inventory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lists label 'money'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("whatever", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A consistent corpus passes validation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_ConsistentCorpus_Passes()
    {
        var inventory = new SenseInventory([new InventoryRow("bank", "noun", ["noun"], "s.tsv")]);

        inventory.Validate([Row("bank", "noun", "A {bank}.", "a.tsv")], [Row("bank", "noun", "B {bank}.", "h.tsv")], [("banks", "bank", ["noun"])]);
    }

    /// <summary>
    ///     The tag evaluator applies the library rule: mixed and tagless gold senses are excluded, and answers are counted per tag.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TagEvaluator_CountsRowsPerRule()
    {
        var model = new DemaConsulting.WordSense.LemmaModel(
            "bow",
            [new DemaConsulting.WordSense.SenseDefinition("bend", ["noun", "verb"]), new DemaConsulting.WordSense.SenseDefinition("color", ["adj"]), new DemaConsulting.WordSense.SenseDefinition("weapon", ["noun"]), new DemaConsulting.WordSense.SenseDefinition("stoop", ["verb"])],
            DemaConsulting.WordSense.LemmaModelKind.OneVersusRest,
            new float[4], new float[4], 0.5f, 1);
        var evaluator = new TagEvaluator();

        evaluator.Add("h.tsv", model, new float[] { 0, 0, 3, 0 }, 2, 0.5);   // gold weapon, predicted noun: right
        evaluator.Add("h.tsv", model, new float[] { 0, 0, 0, 3 }, 2, 0.5);   // gold weapon, predicted verb: wrong
        evaluator.Add("h.tsv", model, new float[] { 3, 0, 0, 0 }, 0, 0.5);   // gold bend: mixed, excluded
        evaluator.Add("h.tsv", model, new float[] { 0, 3, 0, 0 }, 1, 0.5);   // gold color: neither, excluded
        evaluator.Add("g.tsv", model, new float[] { 0, 0, 1.0f, 0.9f }, 3, 0.5);  // gold stoop, margin too small: unanswered

        var report = evaluator.ToReport();

        Assert.Equal(3, report.Overall.Evaluated);
        Assert.Equal(1, report.Overall.Correct);
        Assert.Equal(2, report.Overall.Answered);
        Assert.Equal(1, report.Overall.AnsweredCorrect);
        Assert.Equal(1, report.MixedRows);
        Assert.Equal(1, report.OtherRows);
        Assert.Equal(["bow"], report.MixedLemmas);
        Assert.Equal(2, report.BySource["h.tsv"].Evaluated);
        Assert.Equal(1, report.ByGoldTag["verb"].Evaluated);
        Assert.Equal(["adj", "noun", "verb"], report.PerTag.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(5, report.PerTag["noun"].Evaluated);
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
        Write("corpus/forms/speech.tsv", "form\tlemma\tlabels\nbank\tbank\tnoun,verb\nnovel\tnovel\tnoun\nhold\thold\tnoun,verb\n");
        Directory.CreateDirectory(Path.Combine(_root, "corpus", "anchors"));
        Directory.CreateDirectory(Path.Combine(_root, "corpus", "holdout"));
        return Path.Combine(_root, "corpus");
    }

    private static string Anchors(string lemma)
    {
        var text = new StringBuilder();
        for (var i = 0; i < 4; i++)
        {
            text.Append(lemma).Append("\tnoun\tnoun {").Append(lemma).Append("} ").Append(i).Append(".\n");
            text.Append(lemma).Append("\tverb\tverb {").Append(lemma).Append("} ").Append(i).Append(".\n");
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
