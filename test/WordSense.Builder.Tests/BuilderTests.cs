using System.Text;
using IWordSenseEncoder = DemaConsulting.WordSense.IWordSenseEncoder;
using WordPieceTokenizer = DemaConsulting.WordSense.WordPieceTokenizer;

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
    ///     Only part-of-speech labels can be imported; a descriptive label makes the batch invalid.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_BatchImporter_PartOfSpeechLabels_AreTagged()
    {
        var corpus = PrepareCorpus();
        Write("source/batches/batch1.tsv", "lemma\tlabels\nbank\tnoun,verb\n");
        Write("source/anchors/batch1.tsv", Anchors("bank"));
        Write("source/holdout/batch1.tsv", "bank\tnoun\tHold {bank} one.\n");

        var result = Assert.Single(BatchImporter.Import(Path.Combine(_root, "source"), corpus, 1));

        Assert.Equal("Imported", result.Status);
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        Assert.Equal(["noun"], inventory.DefinitionsOf("bank")[0].Tags);
        Assert.Equal(["verb"], inventory.DefinitionsOf("bank")[1].Tags);
    }

    /// <summary>
    ///     The part-of-speech rule needs exactly one part of speech and allows only tense and degree extras.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosRules_Validate_FlagsMixedAndNonPartOfSpeechTags()
    {
        var inventory = new SenseInventory(
        [
            new InventoryRow("a", "noun", ["noun"], "s.tsv"),
            new InventoryRow("a", "past", ["past", "verb"], "s.tsv"),
            new InventoryRow("a", "adj", ["adj", "comparative"], "s.tsv"),
            new InventoryRow("b", "bend", ["noun", "verb"], "s.tsv"),
            new InventoryRow("b", "music", ["music"], "s.tsv"),
        ]);

        var errors = PosRules.Validate(inventory);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("'b/bend' must have exactly one part of speech", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'b/music' must have exactly one part of speech", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("non part-of-speech tags: music", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The validate-pos command succeeds for an honest inventory and fails for a stand-in tag.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Program_ValidatePos_ReportsViolations()
    {
        SenseInventory.WriteFile(Path.Combine(_root, "good", "senses", "a.tsv"), [new InventoryRow("bank", "noun", ["noun"], "a.tsv")]);
        SenseInventory.WriteFile(Path.Combine(_root, "bad", "senses", "a.tsv"), [new InventoryRow("bass", "music", ["music"], "a.tsv")]);

        Assert.Equal(0, Program.Run(["validate-pos", "--corpus", Path.Combine(_root, "good")], new StringWriter(), new StringWriter()));
        var error = new StringWriter();
        Assert.Equal(1, Program.Run(["validate-pos", "--corpus", Path.Combine(_root, "bad")], new StringWriter(), error));
        Assert.Contains("bass/music", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The group column is optional: it round-trips, defaults to empty and a three-column file still reads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Group_RoundTripsAndIsOptional()
    {
        SenseInventory.WriteFile(
            Path.Combine(_root, "grp", "x.tsv"),
            [new InventoryRow("bow", "bend-noun", ["noun"], "x.tsv", "bend"), new InventoryRow("bow", "bend-verb", ["verb"], "x.tsv", "bend"), new InventoryRow("bow", "weapon", ["noun"], "x.tsv")]);
        Write("grp/old.tsv", "lemma\tsense\ttags\nbank\tnoun\tnoun\nbank\tverb\tverb\n");

        var inventory = SenseInventory.ReadDirectory(Path.Combine(_root, "grp"));

        Assert.Equal("bend", inventory.DefinitionsOf("bow")[0].Group);
        Assert.Equal("bend", inventory.DefinitionsOf("bow")[1].Group);
        Assert.Null(inventory.DefinitionsOf("bow")[2].Group);
        Assert.Null(inventory.DefinitionsOf("bank")[0].Group);
        Assert.StartsWith("lemma\tsense\ttags\tgroup\n", File.ReadAllText(Path.Combine(_root, "grp", "x.tsv")), StringComparison.Ordinal);
    }

    /// <summary>
    ///     An invalid group is rejected with the file and line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_InvalidGroup_IsRejected()
    {
        Write("bad-group/a.tsv", "lemma\tsense\ttags\tgroup\nbank\tnoun\tnoun\tBad Group\n");

        var ex = Assert.Throws<InvalidDataException>(() => SenseInventory.ReadDirectory(Path.Combine(_root, "bad-group")));

        Assert.Contains("a.tsv:2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("group", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The audit resolves rows: agreement is accepted, a low-confidence or disagreeing row needs the third judge, and a low-confidence third judge leaves it undecided.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosAudit_Resolve_FollowsTheRules()
    {
        var noun = new JudgeAnswer("x", "noun", "singular", "H", string.Empty);
        var nounLow = new JudgeAnswer("x", "noun", "singular", "L", "unsure");
        var verb = new JudgeAnswer("x", "verb", "base", "H", string.Empty);
        var other = new JudgeAnswer("x", "other", "none", "H", "proper noun");

        Assert.Equal("agree", PosAudit.Resolve(noun, noun, null).Resolution);
        Assert.True(PosAudit.AutoAccept(noun, noun));
        Assert.False(PosAudit.AutoAccept(noun, nounLow));
        Assert.False(PosAudit.AutoAccept(other, other));
        Assert.Equal(("verb", "adjudicated"), (PosAudit.Resolve(noun, verb, verb).Pos, PosAudit.Resolve(noun, verb, verb).Resolution));
        Assert.Equal(PosAudit.Undecided, PosAudit.Resolve(noun, verb, other).Resolution);
        Assert.Equal(PosAudit.Undecided, PosAudit.Resolve(noun, nounLow, new JudgeAnswer("x", "noun", "singular", "L", "still unsure")).Resolution);
    }

    /// <summary>
    ///     The row id is stable and depends on the lemma and the sentence.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosAudit_RowId_IsStable()
    {
        Assert.Equal(PosAudit.RowId("bank", "A {bank}."), PosAudit.RowId("bank", "A {bank}."));
        Assert.NotEqual(PosAudit.RowId("bank", "A {bank}."), PosAudit.RowId("bank", "B {bank}."));
        Assert.Equal(12, PosAudit.RowId("bank", "A {bank}.").Length);
    }

    /// <summary>
    ///     The relabel retags a uniformly mis-tagged sense, drops noise and undecided rows, excludes senses and lemmas that fall below the floors,
    ///     rewrites the forms and refuses to run twice.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Relabeler_Apply_RelabelsDropsAndExcludes()
    {
        var corpus = Path.Combine(_root, "rel");
        var audit = new StringBuilder("id\tlemma\tsense\tsplit\ta_pos\ta_form\ta_conf\tb_pos\tb_form\tb_conf\tc_pos\tc_form\tc_conf\tfinal_pos\tfinal_form\tfinal_conf\tresolution\n");
        var anchors = new StringBuilder();
        var holdout = new StringBuilder();
        void Add(string lemma, string sense, string kind, int n, string pos, string resolution = "agree")
        {
            for (var i = 0; i < n; i++)
            {
                var sentence = $"Sentence {lemma} {sense} {kind} {pos} {i} with {{{lemma}}}.";
                audit.Append(string.Join('\t', PosAudit.RowId(lemma, sentence), lemma, sense, kind, pos, "x", "H", pos, "x", "H", "-", "-", "-", pos, "x", "H", resolution)).Append('\n');
                (kind == "anchors" ? anchors : holdout).Append(lemma).Append('\t').Append(sense).Append('\t').Append(sentence).Append('\n');
            }
        }

        Add("clever", "noun", "anchors", 14, "adj");
        Add("clever", "verb", "anchors", 14, "verb");
        Add("fine", "adj", "anchors", 14, "adj");
        Add("fine", "adj", "anchors", 1, "noun");
        Add("fine", "verb", "anchors", 14, "verb");
        Add("fine", "verb", "holdout", 1, "verb", PosAudit.Undecided);
        Add("bow", "bend", "anchors", 5, "noun");
        Add("bow", "bend", "anchors", 9, "verb");
        Add("bow", "weapon", "anchors", 14, "noun");
        Write("rel/audit/pos-audit.tsv", audit.ToString());
        Write("rel/anchors/a.tsv", anchors.ToString());
        Write("rel/holdout/a.tsv", holdout.ToString());
        Write("rel/senses/a.tsv", "lemma\tsense\ttags\nbow\tbend\tnoun|verb\nbow\tweapon\tnoun\nclever\tnoun\tnoun\nclever\tverb\tverb\nfine\tadj\tadj\nfine\tverb\tverb\n");
        Write("rel/forms/speech.tsv", "form\tlemma\tlabels\nbows\tbow\tbend,weapon\nclever\tclever\tnoun,verb\n");

        var summary = Relabeler.Apply(corpus);

        Assert.Contains("Relabeled 14 rows", summary, StringComparison.Ordinal);
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        Assert.Equal(["adj", "verb"], inventory.DefinitionsOf("clever").Select(d => d.Name));
        Assert.Equal(["adj"], inventory.DefinitionsOf("clever")[0].Tags);
        Assert.False(inventory.ContainsLemma("bow"));
        Assert.Equal(28, File.ReadAllLines(Path.Combine(corpus, "anchors", "a.tsv")).Count(l => l.StartsWith("fine\t", StringComparison.Ordinal)));
        var forms = File.ReadAllText(Path.Combine(corpus, "forms", "speech.tsv"));
        Assert.Contains("clever\tclever\tadj,verb", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("bows", forms, StringComparison.Ordinal);
        var decisions = File.ReadAllText(Path.Combine(corpus, "audit", "relabel-decisions.tsv"));
        Assert.Contains("clever\tnoun\tadj", decisions, StringComparison.Ordinal);
        Assert.Contains("bow\tbend\t-", decisions, StringComparison.Ordinal);
        Assert.Contains("noise: noun", File.ReadAllText(Path.Combine(corpus, "audit", "dropped-rows.tsv")), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Relabeler.Apply(corpus));
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
        Assert.Contains("3 or 4 tab-separated columns", ex.Message, StringComparison.Ordinal);
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
    [InlineData("learning-curve", "--corpus", "x")]
    [InlineData("error-diagnosis", "--corpus", "x", "--model", "m")]
    [InlineData("weak-lemmas")]
    [InlineData("weak-lemmas", "--analysis", "missing-analysis-dir")]
    public void WordSenseBuilder_Program_Run_ReportsErrors(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, Program.Run(args, output, error));
        Assert.NotEmpty(error.ToString());
    }

    /// <summary>
    ///     A non-numeric integer option is reported as a usage error naming the option, not as a crash.
    /// </summary>
    /// <param name="option">The option name.</param>
    /// <param name="command">The command and its required options.</param>
    [Theory]
    [InlineData("max", "import-batches", "--source", "s", "--corpus", "c")]
    [InlineData("size", "audit-export", "--corpus", "c", "--work", "w")]
    [InlineData("size", "audit-adjudicate", "--corpus", "c", "--work", "w")]
    [InlineData("size", "real-judge-export", "--corpus", "c", "--work", "w")]
    [InlineData("size", "real-judge-adjudicate", "--corpus", "c", "--work", "w")]
    [InlineData("per-sense", "real-eval-import", "--corpus", "c", "--candidates", "x", "--audit", "a")]
    [InlineData("subsets", "learning-curve", "--corpus", "c", "--model", "m", "--out", "o")]
    [InlineData("seed", "learning-curve", "--corpus", "c", "--model", "m", "--out", "o")]
    public void WordSenseBuilder_Program_Run_NonNumericOption_ReportsErrorNamingOption(string option, params string[] command)
    {
        // Arrange: a command line whose integer option is not a number
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = [.. command, "--" + option, "abc"];

        // Act
        var code = Program.Run(args, output, error);

        // Assert: exit code 1 and a message naming the option
        Assert.Equal(1, code);
        Assert.Contains($"--{option} must be an integer", error.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    ///     The sense judges auto-accept confident agreement, adjudicate otherwise, and leave low confidence undecided.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealJudge_Resolve_FollowsTheRules()
    {
        var a = new SenseAnswer("x", "bend-noun", "H", string.Empty);
        var b = new SenseAnswer("x", "bend-noun", "M", string.Empty);
        var other = new SenseAnswer("x", "weapon", "H", string.Empty);
        var low = new SenseAnswer("x", "weapon", "L", "unsure");
        Assert.Equal(("bend-noun", "agree"), RealJudge.Resolve(a, b, null));
        Assert.Equal(("weapon", "adjudicated"), RealJudge.Resolve(a, other, other));
        Assert.Equal(("undecided", "undecided"), RealJudge.Resolve(a, other, low));
        Assert.Equal(1, RealImport.LicenceRank("CC BY 2.0 FR"));
        Assert.Equal(0, RealImport.LicenceRank("CC0 1.0"));
        Assert.Equal(2, RealImport.LicenceRank("web-excerpt"));
    }

    /// <summary>
    ///     The provenance gate flags a real sentence without a provenance row and accepts a complete sidecar.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealImport_Validate_RequiresProvenance()
    {
        Write("corpus/anchors/real-restore.tsv", "bow\tbend-noun\tHe made a low {bow}.\n");
        Assert.Contains(RealImport.Validate(Path.Combine(_root, "corpus")), e => e.Contains("no provenance", StringComparison.Ordinal));
        var id = PosAudit.RowId("bow", "He made a low {bow}.");
        var header = string.Join('\t', "id", "lemma", "sense", "split", "source", "licence", "source_key", "url", "contributor", "origin");
        var row = string.Join('\t', id, "bow", "bend-noun", "anchors", "tatoeba-cc0", "CC0 1.0", "1", "https://tatoeba.org/en/sentences/show/1", "someone", "real");
        Write("corpus/provenance/real-restore.tsv", header + "\n" + row + "\n");
        Assert.Empty(RealImport.Validate(Path.Combine(_root, "corpus")));
    }

    /// <summary>
    ///     The provenance gate rejects rows with a disallowed licence or origin and rows that lack required attribution.
    /// </summary>
    [Theory]
    [InlineData("GPL-3.0", "real", "tatoeba-cc0", "1", "someone", "https://tatoeba.org/1", "licence 'GPL-3.0' is not allowed")]
    [InlineData("CC0 1.0", "invented", "tatoeba-cc0", "1", "someone", "https://tatoeba.org/1", "origin 'invented' is not real")]
    [InlineData("CC BY 2.0 FR", "real", "tatoeba-ccby", "1", "-", "https://tatoeba.org/1", "a CC BY row needs a contributor")]
    [InlineData("CC BY 2.0 FR", "real", "tatoeba-ccby", "1", "unknown", "https://tatoeba.org/1", "a CC BY row needs a contributor")]
    [InlineData("Public domain", "real", "gutenberg", "1342", "someone", "https://gutenberg.org/1342", "a Gutenberg row needs a pgN key")]
    [InlineData("CC0 1.0", "real", "gutenberg", "pg1342", "someone", "https://gutenberg.org/1342", "a Gutenberg row needs a pgN key")]
    [InlineData("web-excerpt", "real", "web", "1", "someone", "-", "a web excerpt needs its URL")]
    public void WordSenseBuilder_RealImport_Validate_RejectsBadProvenanceRows(
        string licence, string origin, string source, string key, string contributor, string url, string expected)
    {
        const string sentence = "He made a low {bow}.";
        Write("corpus/anchors/real-restore.tsv", $"bow\tbend-noun\t{sentence}\n");
        var id = PosAudit.RowId("bow", sentence);
        var header = string.Join('\t', "id", "lemma", "sense", "split", "source", "licence", "source_key", "url", "contributor", "origin");
        var row = string.Join('\t', id, "bow", "bend-noun", "anchors", source, licence, key, url, contributor, origin);
        Write("corpus/provenance/real-restore.tsv", header + "\n" + row + "\n");

        var errors = RealImport.Validate(Path.Combine(_root, "corpus"));

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A sentence of more than 35 words and a sentence that mentions Gutenberg are rejected.
    /// </summary>
    [Theory]
    [InlineData(36, "words", "has more than 35 words")]
    [InlineData(35, "words", "")]
    [InlineData(5, "Gutenberg", "mentions Gutenberg")]
    public void WordSenseBuilder_RealImport_Validate_RejectsLongAndGutenbergSentences(int wordCount, string filler, string expected)
    {
        var sentence = filler == "words"
            ? "{bow} " + string.Join(' ', Enumerable.Repeat("word", wordCount - 1)) + "."
            : "A {bow} from Project Gutenberg " + string.Join(' ', Enumerable.Repeat("word", wordCount - 5)) + ".";
        Write("corpus/anchors/real-restore.tsv", $"bow\tbend-noun\t{sentence}\n");
        var id = PosAudit.RowId("bow", sentence);
        var header = string.Join('\t', "id", "lemma", "sense", "split", "source", "licence", "source_key", "url", "contributor", "origin");
        var row = string.Join('\t', id, "bow", "bend-noun", "anchors", "tatoeba-cc0", "CC0 1.0", "1", "https://tatoeba.org/1", "someone", "real");
        Write("corpus/provenance/real-restore.tsv", header + "\n" + row + "\n");

        var errors = RealImport.Validate(Path.Combine(_root, "corpus"));

        if (expected.Length == 0)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     A small build reports the best-group accuracy and the tag-level sections.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TableBuilder_Build_ReportHasGroupAndTagSections()
    {
        // Arrange: a tiny corpus of two senses and a deterministic fake encoder
        string[] vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "bank", "the", "money", "river", "deep", "rich", "wide", "tall", "high", "low", "old", "new", "."];
        string[] fillers = ["deep", "rich", "wide", "tall", "high", "low"];
        var anchors = new List<CorpusRow>();
        foreach (var filler in fillers)
        {
            anchors.Add(Row("bank", "noun", $"the {{bank}} money {filler}.", "a.tsv"));
            anchors.Add(Row("bank", "verb", $"the {{bank}} river {filler}.", "a.tsv"));
        }

        var holdout = new[]
        {
            Row("bank", "noun", "the {bank} money old.", "h.tsv"),
            Row("bank", "verb", "the {bank} river new.", "h.tsv"),
        };
        var inventory = new SenseInventory([new InventoryRow("bank", "noun", ["noun"], "s.tsv"), new InventoryRow("bank", "verb", ["verb"], "s.tsv")]);
        var forms = new List<(string Form, string Lemma, string[] Labels)> { ("bank", "bank", ["noun", "verb"]) };
        using var encoder = new FakeEncoder();

        // Act
        var result = TableBuilder.Build(
            encoder, new WordPieceTokenizer(vocabulary), anchors, holdout, forms, inventory, new TableBuilderOptions(Parallelism: 1, MaxSequenceLength: 32));
        var text = result.Report.Render();

        // Assert: the table and both accuracy sections
        Assert.Single(result.Table.Lemmas);
        Assert.Equal(2, result.Report.Overall.Total);
        Assert.Contains("Group level:", text, StringComparison.Ordinal);
        Assert.Contains("best-group accuracy", text, StringComparison.Ordinal);
        Assert.Contains("Tag level, noun versus verb", text, StringComparison.Ordinal);
        Assert.Contains("Tag level, has-tag per tag", text, StringComparison.Ordinal);
        Assert.Contains("  noun: ", text, StringComparison.Ordinal);
        Assert.Contains("  verb: ", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A deterministic encoder whose hidden states depend on the token id and on the whole sequence.
    /// </summary>
    private sealed class FakeEncoder : IWordSenseEncoder
    {
        private const int Size = 8;

        /// <inheritdoc />
        public string ModelSha256 { get; } = new('a', 64);

        /// <inheritdoc />
        public int HiddenSize => Size;

        /// <inheritdoc />
        public ValueTask<float[]> EncodeAsync(int[] inputIds, CancellationToken cancellationToken)
        {
            var context = inputIds.Sum();
            var hidden = new float[inputIds.Length * Size];
            for (var p = 0; p < inputIds.Length; p++)
            {
                for (var d = 0; d < Size; d++)
                {
                    hidden[(p * Size) + d] = (float)(Math.Sin(inputIds[p] * (d + 1)) + (3 * Math.Cos(context * (d + 1))));
                }
            }

            return ValueTask.FromResult(hidden);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private const string BowSentence = "He took a {bow} from the rack.";

    private string PrepareRealCorpus(bool withDecisions = true)
    {
        var corpus = Path.Combine(_root, "corpus");
        Directory.CreateDirectory(Path.Combine(corpus, "anchors"));
        Directory.CreateDirectory(Path.Combine(corpus, "holdout"));
        Write("corpus/audit/restore-plan.tsv", "lemma\tsense\ttags\tgroup\tfrom\tdefinition\tstatus\nbow\tbend-noun\tnoun\t\told:noun\ta bend\texisting\n");
        var droppedId = PosAudit.RowId("bow", BowSentence);
        var audit = new string[17];
        Array.Fill(audit, "-");
        (audit[0], audit[1], audit[2], audit[3], audit[13], audit[14], audit[16]) = (droppedId, "bow", "old", "anchors", "noun", "-", "agree");
        Write("corpus/audit/pos-audit.tsv", "header\n" + string.Join('\t', audit) + "\n");
        Write("corpus/audit/dropped-rows.tsv", "header\n" + string.Join('\t', droppedId, "bow", "old", "anchors", "x", BowSentence) + "\n");
        if (withDecisions)
        {
            Write("corpus/audit/relabel-decisions.tsv", "header\n" + string.Join('\t', "bow", "old", "-", "-", "drop", "-", "-", "-", "note") + "\n");
        }

        SenseInventory.WriteFile(Path.Combine(corpus, "senses", "base.tsv"), [new InventoryRow("bow", "bend-noun", ["noun"], "base.tsv")]);
        return corpus;
    }

    private static (List<Candidate> Candidates, Dictionary<string, SenseJudgement> Judgements) RealCandidates(int count)
    {
        var candidates = new List<Candidate>();
        var judgements = new Dictionary<string, SenseJudgement>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var sentence = $"A low {{bow}} number {i} came.";
            var id = PosAudit.RowId("bow", sentence);
            candidates.Add(new Candidate(id, "bow", sentence, "tatoeba-cc0", "CC0 1.0", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "https://tatoeba.org/" + i, "person" + i));
            judgements[id] = new SenseJudgement(id, "bow", "bend-noun", "H", "bend-noun", "H", "-", "-", "bend-noun", "agree");
        }

        return (candidates, judgements);
    }

    /// <summary>
    ///     Importing real sentences gives every written sentence a provenance row and the result validates.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealImport_Import_EverySentenceHasProvenanceAndValidates()
    {
        var corpus = PrepareRealCorpus();
        var (candidates, judgements) = RealCandidates(3);

        var report = RealImport.Import(corpus, candidates, judgements);

        Assert.NotEmpty(report);
        var written = File.ReadAllLines(Path.Combine(corpus, "anchors", "real-restore.tsv")).Concat(File.ReadAllLines(Path.Combine(corpus, "holdout", "real-restore.tsv")))
            .Where(l => l.Length > 0).ToList();
        var provenance = RealImport.ReadProvenance(Path.Combine(corpus, "provenance", "real-restore.tsv"));
        Assert.Equal(4, written.Count);
        Assert.Equal(written.Count, provenance.Count);
        Assert.Contains(provenance, p => p.Origin == "llm-corpus");
        Assert.Equal(3, provenance.Count(p => p.Origin == "real"));
        Assert.Empty(RealImport.Validate(corpus));
        Assert.Contains("restore", File.ReadAllText(Path.Combine(corpus, "audit", "relabel-decisions.tsv")), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing relabel-decisions file fails the import before any output is written.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealImport_Import_MissingDecisions_WritesNothing()
    {
        var corpus = PrepareRealCorpus(withDecisions: false);
        var (candidates, judgements) = RealCandidates(3);

        Assert.ThrowsAny<IOException>(() => RealImport.Import(corpus, candidates, judgements));

        Assert.False(File.Exists(Path.Combine(corpus, "anchors", "real-restore.tsv")));
        Assert.False(File.Exists(Path.Combine(corpus, "provenance", "real-restore.tsv")));
    }

    /// <summary>
    ///     A gap batch adds real anchors and a separate real holdout without touching the existing files, and the result validates.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealGap_Import_WritesSeparateAnchorsAndHoldoutWithProvenance()
    {
        var corpus = PrepareRealCorpus();
        Write("corpus/audit/real-gap-1-plan.tsv", "lemma\tsense\tdefinition\tadd_anchors\treal_holdout\nbow\tbend-noun\ta bend\t3\t2\n");
        Write("corpus/anchors/seed.tsv", "bow\tbend-noun\tShe gave a deep {bow}.\n");
        var (candidates, judgements) = RealCandidates(8);

        var report = RealGap.Import(corpus, "real-gap-1", candidates, judgements);

        Assert.Contains("anchors 3, holdout 2.", report, StringComparison.Ordinal);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(corpus, "anchors", "real-gap-1.tsv")).Count(l => l.Length > 0));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(corpus, "holdout", "real-gap-1.tsv")).Count(l => l.Length > 0));
        var provenance = RealImport.ReadProvenance(Path.Combine(corpus, "provenance", "real-gap-1.tsv"));
        Assert.Equal(5, provenance.Count);
        Assert.All(provenance, p => Assert.Equal("real", p.Origin));
        Assert.Equal(5, provenance.Select(p => p.Id).Distinct().Count());
        Assert.Empty(RealImport.Validate(corpus));
        Assert.Equal("bow\tbend-noun\tShe gave a deep {bow}.\n", File.ReadAllText(Path.Combine(corpus, "anchors", "seed.tsv")));
    }

    /// <summary>
    ///     Real holdout rows that exist already count towards the holdout target, and a short pool is reported.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealGap_Import_CountsExistingRealHoldoutAndReportsShortfall()
    {
        var corpus = PrepareRealCorpus();
        Write("corpus/audit/real-gap-1-plan.tsv", "lemma\tsense\tdefinition\tadd_anchors\treal_holdout\nbow\tbend-noun\ta bend\t5\t2\n");
        var holdoutSentence = "He took a {bow} and left.";
        Write("corpus/holdout/real-restore.tsv", $"bow\tbend-noun\t{holdoutSentence}\n");
        RealImport.WriteProvenance(
            Path.Combine(corpus, "provenance", "real-restore.tsv"),
            [new ProvenanceRow(PosAudit.RowId("bow", holdoutSentence), "bow", "bend-noun", "holdout", "tatoeba-cc0", "CC0 1.0", "1", "https://tatoeba.org/1", "person", "real")]);
        var (candidates, judgements) = RealCandidates(3);

        var report = RealGap.Import(corpus, "real-gap-1", candidates, judgements);

        Assert.Contains("anchors 2, holdout 1.", report, StringComparison.Ordinal);
        Assert.Contains("SHORT", report, StringComparison.Ordinal);
        Assert.Empty(RealImport.Validate(corpus));
    }

    /// <summary>
    ///     A gap batch name and plan are validated.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealGap_ReadPlan_RejectsBadNamesAndRows()
    {
        var corpus = PrepareRealCorpus();
        Write("corpus/audit/real-gap-2-plan.tsv", "lemma\tsense\tdefinition\tadd_anchors\treal_holdout\nbow\tbend-noun\ta bend\tx\t2\n");
        Write("corpus/audit/real-gap-3-plan.tsv", "lemma\tsense\tdefinition\tadd_anchors\treal_holdout\nbow\tnope\ta bend\t1\t2\n");

        Assert.Throws<ArgumentException>(() => RealGap.ValidateName("real-restore"));
        Assert.Throws<ArgumentException>(() => RealGap.ValidateName("real-gap-"));
        Assert.Throws<InvalidDataException>(() => RealGap.ReadPlan(corpus, "real-gap-2"));
        Assert.Throws<InvalidDataException>(() => RealGap.Import(corpus, "real-gap-3", [], new Dictionary<string, SenseJudgement>()));
    }

    /// <summary>
    ///     The judge export freezes the menus of a gap plan, and the merge steps read them back.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealJudge_Export_GapPlanMenusAreFrozenInTheWorkDirectory()
    {
        var corpus = PrepareRealCorpus();
        Write("corpus/audit/real-gap-1-plan.tsv", "lemma\tsense\tdefinition\tadd_anchors\treal_holdout\nbow\tbend-noun\tinclined greeting\t3\t2\n");
        var (candidates, _) = RealCandidates(2);
        var work = Path.Combine(_root, "work");

        var (rows, _) = RealJudge.Export(corpus, candidates, work, 10, "real-gap-1");
        var menus = RealJudge.LoadMenus(corpus, work, ["bow"]);

        Assert.Equal(2, rows);
        Assert.Contains("inclined greeting", Assert.Single(menus["bow"]), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => RealJudge.LoadMenus(corpus, work, ["other"]));
    }

    /// <summary>
    ///     The judge answer parser rejects every kind of malformed answer.
    /// </summary>
    /// <param name="content">The answer file content.</param>
    /// <param name="expected">The expected message fragment.</param>
    [Theory]
    [InlineData("id1\tbend-noun\n", "expected 'id")]
    [InlineData("zzz\tbend-noun\tH\n", "unexpected id")]
    [InlineData("id1\tbogus\tH\n", "is not a sense")]
    [InlineData("id1\tbend-noun\tX\n", "confidence must")]
    [InlineData("id1\tbend-noun\tL\n", "confidence must")]
    [InlineData("id1\tnone\tH\n", "confidence must")]
    [InlineData("id1\tbend-noun\tH\nid1\tbend-noun\tH\n", "duplicate id")]
    [InlineData("", "not answered")]
    public void WordSenseBuilder_RealJudge_ParseAnswers_RejectsMalformedAnswers(string content, string expected)
    {
        var path = Write("work/a-001.out.tsv", content);
        var menu = new Dictionary<string, HashSet<string>> { ["bow"] = ["bend-noun", "weapon"] };

        var ex = Assert.Throws<InvalidDataException>(() => RealJudge.ParseAnswers(
            path, ["id1"], menu, new Dictionary<string, string> { ["id1"] = "bow" }));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    private string PrepareJudgeWork(string workName, bool withC)
    {
        var corpus = PrepareRealCorpus();
        var rows = $"id1\tbow\t{BowSentence}\n";
        Write($"{workName}/rows.tsv", rows);
        foreach (var judge in new[] { "a", "b" })
        {
            Write($"{workName}/{judge}-001.tsv", $"#menu\tbow\tbend-noun\tnoun\ta bend\t\n{rows}");
        }

        Write($"{workName}/a-001.out.tsv", "id1\tbend-noun\tH\n");
        Write($"{workName}/b-001.out.tsv", "id1\tbend-noun\tL\tunsure\n");
        if (withC)
        {
            Write($"{workName}/c-001.tsv", rows);
            Write($"{workName}/c-001.out.tsv", "id1\tbend-noun\tH\n");
        }

        return corpus;
    }

    /// <summary>
    ///     The merge fails when a row needs adjudication but has no judge C answer.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealJudge_Merge_MissingJudgeC_Throws()
    {
        var corpus = PrepareJudgeWork("work", withC: false);

        var ex = Assert.Throws<InvalidDataException>(() => RealJudge.Merge(corpus, Path.Combine(_root, "work")));

        Assert.Contains("judge C", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The merge resolves an adjudicated row, also when the work path itself contains ".out".
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealJudge_Merge_AdjudicatedRow_InWorkDirectoryNamedOut()
    {
        var corpus = PrepareJudgeWork("work.out", withC: true);

        var result = Assert.Single(RealJudge.Merge(corpus, Path.Combine(_root, "work.out")));

        Assert.Equal(("bend-noun", "adjudicated"), (result.Final, result.Resolution));
    }

    /// <summary>
    ///     Rows with too few columns in the judge files and the dropped rows are reported as data errors.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealJudge_MalformedRows_AreDataErrors()
    {
        var corpus = PrepareJudgeWork("work", withC: false);
        Write("work/rows.tsv", "id1\tbow\n");
        Assert.Contains("rows.tsv:1", Assert.Throws<InvalidDataException>(() => RealJudge.Merge(corpus, Path.Combine(_root, "work"))).Message, StringComparison.Ordinal);

        Write("work/rows.tsv", $"id1\tbow\t{BowSentence}\n");
        Write("work/a-001.tsv", "id1\tbow\n");
        Assert.Contains("a-001.tsv:1", Assert.Throws<InvalidDataException>(() => RealJudge.Merge(corpus, Path.Combine(_root, "work"))).Message, StringComparison.Ordinal);

        Write("corpus/audit/dropped-rows.tsv", "header\nid\tbow\told\n");
        Assert.Contains("dropped-rows.tsv:2", Assert.Throws<InvalidDataException>(() => RealPlan.ReadPool(corpus, RealPlan.ReadPlan(corpus))).Message, StringComparison.Ordinal);

        var candidates = Write("candidates.tsv", "header\nid\tbow\n");
        Assert.Contains("candidates.tsv:2", Assert.Throws<InvalidDataException>(() => RealJudge.ReadCandidates(candidates)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A relabel-decisions row with the wrong number of columns fails the import, naming file and line, before any output.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealImport_Import_MalformedDecisionsRow_Throws()
    {
        var corpus = PrepareRealCorpus();
        Write("corpus/audit/relabel-decisions.tsv", "header\nbow\told\tdrop\n");
        var (candidates, judgements) = RealCandidates(3);

        var ex = Assert.Throws<InvalidDataException>(() => RealImport.Import(corpus, candidates, judgements));

        Assert.Contains("relabel-decisions.tsv:2", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(corpus, "anchors", "real-restore.tsv")));
    }

    private const string GapSentence = "gap {bank} row.";
    private const string OverlapSentence = "overlap {bank} row.";
    private const string AmbiguousSentence = "ambiguous {bank} row.";

    private static double[] Polar(double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return [Math.Cos(radians), Math.Sin(radians)];
    }

    /// <summary>
    ///     Builds analysis data from handcrafted two-dimensional vectors: the senses a and b lie 15 degrees apart and
    ///     the sense c lies opposite. The holdout holds correct rows plus one row of each error class, the overlap
    ///     row marked as real text, and the real evaluation set holds one correct row.
    /// </summary>
    private AnalysisData SyntheticAnalysis()
    {
        var vectors = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var anchors = new List<CorpusRow>();
        var angles = new Dictionary<string, double> { ["a"] = 0, ["b"] = 15, ["c"] = 180 };
        foreach (var (sense, angle) in angles)
        {
            for (var i = 0; i < 8; i++)
            {
                var sentence = $"{sense} {{bank}} anchor {i}.";
                vectors[sentence] = Polar(angle + ((i - 3.5) * 0.8));
                anchors.Add(Row("bank", sense, sentence, "a.tsv"));
            }
        }

        var holdout = new List<CorpusRow>();
        foreach (var (sense, angle) in angles)
        {
            var sentence = $"{sense} {{bank}} holdout.";
            vectors[sentence] = Polar(angle + 0.2);
            holdout.Add(Row("bank", sense, sentence, "h.tsv"));
        }

        (string Sentence, string Sense, double Angle)[] errors = [(GapSentence, "a", 90), (OverlapSentence, "b", 0), (AmbiguousSentence, "c", 0)];
        foreach (var (sentence, sense, angle) in errors)
        {
            vectors[sentence] = Polar(angle);
            holdout.Add(Row("bank", sense, sentence, "h.tsv"));
        }

        vectors["eval {bank} row."] = Polar(0.1);
        var realEval = new List<CorpusRow> { Row("bank", "a", "eval {bank} row.", "eval-seed.tsv") };
        var origins = new Dictionary<string, string> { [PosAudit.RowId("bank", OverlapSentence)] = "real" };
        var inventory = new SenseInventory(angles.Keys.Select(s => new InventoryRow("bank", s, ["noun"], "s.tsv")));
        return AnalysisData.Create(
            rows => rows.Select(r => vectors[r.Sentence]).ToArray(), anchors, holdout, realEval, origins, inventory, new TableBuilderOptions(Parallelism: 1), null);
    }

    /// <summary>
    ///     The subset hash is a fixed FNV-1a value, not the randomized string hash code.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_StableHash_IsFixed()
    {
        Assert.Equal(2682200765u, LearningCurve.StableHash("bank", 2, 0));
        Assert.NotEqual(LearningCurve.StableHash("bank", 2, 0), LearningCurve.StableHash("bank", 2, 1));
        Assert.NotEqual(LearningCurve.StableHash("bank", 2, 0), LearningCurve.StableHash("bank", 4, 0));
    }

    /// <summary>
    ///     A subset has at most k anchors per sense, uses all anchors of a smaller sense, repeats for the same seed
    ///     and differs for another seed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_Draw_CapsPerSenseAndIsRepeatable()
    {
        int[][] bySense = [[0, 1, 2, 3, 4, 5, 6, 7, 8, 9], [10, 11, 12]];

        var first = LearningCurve.Draw(bySense, "bank", 4, 0, 1);
        var repeat = LearningCurve.Draw(bySense, "bank", 4, 0, 1);
        var other = Enumerable.Range(2, 30).Select(seed => LearningCurve.Draw(bySense, "bank", 4, 0, seed)).ToList();

        Assert.Equal(first, repeat);
        Assert.Equal(4, first.Count(i => i < 10));
        Assert.Equal([10, 11, 12], first.Where(i => i >= 10));
        Assert.Equal(first.Where(i => i < 10).Order(), first.Where(i => i < 10));
        Assert.Contains(other, subset => !subset.SequenceEqual(first));
        Assert.Equal(13, LearningCurve.Draw(bySense, "bank", 14, 0, 1).Length);
    }

    /// <summary>
    ///     The learning curve evaluates every anchor count, uses all anchors when k exceeds them and produces
    ///     byte-identical files on a repeat run.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_RunAndWrite_CapsAtAvailableAnchorsAndIsByteIdentical()
    {
        // Arrange and act: two complete runs from freshly built data
        var first = Path.Combine(_root, "run1");
        var second = Path.Combine(_root, "run2");
        Assert.Equal(1, LearningCurve.RunAndWrite(SyntheticAnalysis(), first, 3, 7));
        LearningCurve.RunAndWrite(SyntheticAnalysis(), second, 3, 7);

        // Assert: identical bytes
        foreach (var name in new[] { AnalysisFiles.Curve, AnalysisFiles.CurveSummary, AnalysisFiles.Plateau })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, name)), File.ReadAllBytes(Path.Combine(second, name)));
        }

        // Assert: k = 10 and 14 exceed the 8 anchors per sense, so they equal the full set with one subset
        var rows = File.ReadAllLines(Path.Combine(first, AnalysisFiles.Curve)).Skip(1).Select(l => l.Split('\t')).Where(c => c[1] == "holdout").ToList();
        Assert.Equal(["2", "4", "7", "10", "14", "all"], rows.Select(c => c[2]));
        Assert.Equal("3", rows[0][4]);
        Assert.Equal("1", rows[4][4]);
        Assert.Equal(rows[5][3], rows[4][3]);
        Assert.Equal("8.0000", rows[5][3]);
        Assert.Equal("6", rows[5][5]);
        Assert.Equal(rows[5][6], rows[4][6]);
        Assert.StartsWith("lemma\tset\tk\t", File.ReadAllText(Path.Combine(first, AnalysisFiles.Curve)), StringComparison.Ordinal);
        Assert.DoesNotContain('\r', File.ReadAllText(Path.Combine(first, AnalysisFiles.Curve)));
    }

    /// <summary>
    ///     A subset count below one is a usage error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_Run_RejectsNoSubsets()
    {
        Assert.Throws<ArgumentException>(() => LearningCurve.Run(SyntheticAnalysis(), 0, 1));
    }

    /// <summary>
    ///     The real set holds the holdout rows whose provenance origin is real, separate from the real evaluation set.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_Run_SeparatesRealAndHoldoutSets()
    {
        var points = LearningCurve.Run(SyntheticAnalysis(), 2, 1);

        var all = points.Where(p => p.K == LearningCurve.All).ToDictionary(p => p.Set, p => p.N);
        Assert.Equal(6, all[AnalysisFiles.SetHoldout]);
        Assert.Equal(1, all[AnalysisFiles.SetReal]);
        Assert.Equal(1, all[AnalysisFiles.SetRealEval]);
    }

    /// <summary>
    ///     The plateau estimate uses the last two points that are far enough apart and limits the gain.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LearningCurve_EstimatePlateau_ExtrapolatesAndFlagsPlateau()
    {
        var rising = LearningCurve.EstimatePlateau("x", "holdout", [(2, 0.6), (4, 0.7), (7, 0.8), (10, 0.9), (14, 0.92), (16, 0.93)]);
        var flat = LearningCurve.EstimatePlateau("x", "holdout", [(10, 0.95), (14, 0.951)]);
        var close = LearningCurve.EstimatePlateau("x", "holdout", [(14, 0.9), (15, 0.95)]);

        Assert.Equal("no", rising.Plateaued);
        Assert.Equal(10, rising.AnchorsPrevious);
        Assert.Equal(0.005, rising.Slope, 6);
        Assert.Equal(0.07, rising.Gain2x, 6);
        Assert.Equal("yes", flat.Plateaued);
        Assert.Equal(0.001 / 4 * 14, flat.Gain2x, 6);
        Assert.Equal("n/a", close.Plateaued);
        Assert.Equal(0, close.Gain2x);
    }

    /// <summary>
    ///     The nearest anchor is the highest cosine similarity of normalized vectors.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ErrorDiagnosis_Nearest_IsHighestDotProduct()
    {
        float[] feature = [1f, 0f];
        float[][] anchors = [[0.6f, 0.8f], [0f, 1f], [-1f, 0f]];

        Assert.Equal(0.6, ErrorDiagnosis.Nearest(feature, anchors, [0, 1]), 5);
        Assert.Equal(-1.0, ErrorDiagnosis.Nearest(feature, anchors, [2]), 5);
        Assert.Equal(-1.0, ErrorDiagnosis.Nearest(feature, anchors, []));
    }

    /// <summary>
    ///     Each class follows from the similarities: far from every anchor, both close with the other sense as near,
    ///     and everything else.
    /// </summary>
    /// <param name="same">The similarity to the nearest anchor of the labeled sense.</param>
    /// <param name="other">The similarity to the nearest anchor of another sense.</param>
    /// <param name="expected">The expected class.</param>
    [Theory]
    [InlineData(0.50, 0.60, ErrorDiagnosis.CoverageGap)]
    [InlineData(0.80, 0.78, ErrorDiagnosis.Overlap)]
    [InlineData(0.80, 0.90, ErrorDiagnosis.Overlap)]
    [InlineData(0.90, 0.80, ErrorDiagnosis.Ambiguous)]
    [InlineData(0.50, 0.90, ErrorDiagnosis.Ambiguous)]
    [InlineData(0.90, 0.50, ErrorDiagnosis.Ambiguous)]
    public void WordSenseBuilder_ErrorDiagnosis_Classify_FollowsThresholds(double same, double other, string expected)
    {
        Assert.Equal(expected, ErrorDiagnosis.Classify(same, other));
    }

    /// <summary>
    ///     Misclassified and abstained rows are diagnosed with their class, correct answered rows are not listed, and
    ///     the output is byte-identical on a repeat run.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ErrorDiagnosis_Run_ClassifiesEachErrorAndIsByteIdentical()
    {
        // Act
        var result = ErrorDiagnosis.RunAndWrite(SyntheticAnalysis(), Path.Combine(_root, "d1"));
        ErrorDiagnosis.RunAndWrite(SyntheticAnalysis(), Path.Combine(_root, "d2"));

        // Assert: one diagnosed row per error class, each with its sentence
        var gap = Assert.Single(result.Rows, r => r.Sentence == GapSentence);
        var overlap = Assert.Single(result.Rows, r => r.Sentence == OverlapSentence);
        var ambiguous = Assert.Single(result.Rows, r => r.Sentence == AmbiguousSentence);
        Assert.Equal(ErrorDiagnosis.CoverageGap, gap.Class);
        Assert.Equal(ErrorDiagnosis.Overlap, overlap.Class);
        Assert.Equal(ErrorDiagnosis.Ambiguous, ambiguous.Class);
        Assert.Equal("a", overlap.Predicted);
        Assert.Equal("a", ambiguous.Predicted);
        Assert.True(overlap.Answered);
        Assert.DoesNotContain(result.Rows, r => r.Sentence.Contains("holdout", StringComparison.Ordinal));

        // Assert: an abstained row counts as an error, and the counts split by set
        var holdout = Assert.Single(result.Lemmas, c => c.Set == AnalysisFiles.SetHoldout);
        Assert.Equal((6, 3, 1, 1, 1), (holdout.N, holdout.Errors, holdout.CoverageGap, holdout.Overlap, holdout.Ambiguous));
        var real = Assert.Single(result.Lemmas, c => c.Set == AnalysisFiles.SetReal);
        Assert.Equal((1, 1, 0, 1, 0), (real.N, real.Errors, real.CoverageGap, real.Overlap, real.Ambiguous));
        var eval = Assert.Single(result.Lemmas, c => c.Set == AnalysisFiles.SetRealEval);
        Assert.Equal((1, 0), (eval.N, eval.Errors));
        Assert.Equal("overlap", result.Rows.Single(r => r.Sentence == OverlapSentence).Class);
        Assert.Equal("real", overlap.Origin);

        // Assert: byte-identical files
        foreach (var name in new[] { AnalysisFiles.Errors, AnalysisFiles.ErrorLemmas, AnalysisFiles.ErrorSummary })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(_root, "d1", name)), File.ReadAllBytes(Path.Combine(_root, "d2", name)));
        }

        Assert.Contains(result.Statistics, s => s.Class == ErrorDiagnosis.Correct && s.Count == 4);
    }

    /// <summary>
    ///     The recommendation follows the dominant error class and the expected gain follows its documented rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeakLemmas_Recommend_MapsDominantClass()
    {
        var gap = WeakLemmas.Recommend(20, 6, 2, 1, "no", 0.10);
        var gapLimited = WeakLemmas.Recommend(20, 6, 2, 1, "yes", 0.01);
        var overlapRising = WeakLemmas.Recommend(20, 1, 4, 1, "no", 0.10);
        var overlapPlateaued = WeakLemmas.Recommend(20, 1, 4, 1, "yes", 0.0);
        var labels = WeakLemmas.Recommend(20, 0, 1, 3, "no", 0.10);
        var tie = WeakLemmas.Recommend(20, 2, 2, 2, "no", 0.10);

        Assert.Equal((WeakLemmas.MoreSentences, 0.1), (gap.Recommendation, Math.Round(gap.ExpectedGain, 6)));
        Assert.Equal(0.01, gapLimited.ExpectedGain, 6);
        Assert.Equal((WeakLemmas.ConsumerRule, 0.2), (overlapRising.Recommendation, Math.Round(overlapRising.ExpectedGain, 6)));
        Assert.Equal(WeakLemmas.StrongerEncoder, overlapPlateaued.Recommendation);
        Assert.Equal((WeakLemmas.FixLabels, 0.15), (labels.Recommendation, Math.Round(labels.ExpectedGain, 6)));
        Assert.Equal(WeakLemmas.MoreSentences, tie.Recommendation);
    }

    /// <summary>
    ///     Only weak lemmas are ranked, weakest first, and the ranking is repeatable.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeakLemmas_Rank_OrdersWeakLemmasByScore()
    {
        LemmaErrorCounts[] counts =
        [
            new("good", "holdout", 20, 20, 0, 0, 0, 0, 0),
            new("mid", "holdout", 20, 18, 1, 2, 2, 0, 0),
            new("worst", "holdout", 20, 12, 4, 8, 1, 6, 1),
            new("worst", "real", 5, 2, 2, 3, 0, 3, 0),
            new("fragile", "holdout", 20, 20, 0, 0, 0, 0, 0),
            new("fragile", "real-eval", 10, 7, 0, 3, 0, 0, 3),
        ];
        PlateauRow[] plateaus = [new("worst", "holdout", 14, 10, 0.6, 0.6, 0, 0, "yes"), new("mid", "holdout", 14, 10, 0.9, 0.8, 0.0025, 0.04, "no")];

        var ranked = WeakLemmas.Rank(counts, plateaus);

        Assert.Equal(["worst", "fragile", "mid"], ranked.Select(w => w.Lemma));
        Assert.Equal([1, 2, 3], ranked.Select(w => w.Rank));
        Assert.Equal(WeakLemmas.StrongerEncoder, ranked[0].Recommendation);
        Assert.Equal(WeakLemmas.FixLabels, ranked[1].Recommendation);
        Assert.Equal(WeakLemmas.MoreSentences, ranked[2].Recommendation);
        Assert.Equal(0.1, ranked[1].ExpectedGain, 6);
        Assert.Equal(0.04, ranked[2].ExpectedGain, 6);
        Assert.Equal(0.6, ranked[0].HoldoutAccuracy, 6);
        Assert.Equal(5, ranked[0].RealN);
        Assert.Equal(WeakLemmas.Render(ranked), WeakLemmas.Render(WeakLemmas.Rank(counts, plateaus)));
    }

    /// <summary>
    ///     The three commands run in sequence on an analysis directory and the report is byte-identical on a repeat.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeakLemmas_RunAndWrite_RanksFromFilesAndRendersStableReport()
    {
        // Arrange: the files of the other two commands
        var directory = Path.Combine(_root, "analysis");
        LearningCurve.RunAndWrite(SyntheticAnalysis(), directory, 2, 3);
        ErrorDiagnosis.RunAndWrite(SyntheticAnalysis(), directory);

        // Act
        var ranked = WeakLemmas.RunAndWrite(directory);
        var report = WeakLemmas.RenderReport(directory);
        var again = WeakLemmas.RenderReport(directory);

        // Assert
        var weak = Assert.Single(ranked);
        Assert.Equal("bank", weak.Lemma);
        Assert.Equal(report, again);
        Assert.StartsWith("rank\tlemma\t", File.ReadAllText(Path.Combine(directory, AnalysisFiles.Weak)), StringComparison.Ordinal);
        Assert.StartsWith("# Learning Curve and Error Diagnosis", report, StringComparison.Ordinal);
        Assert.Contains("## Conclusions", report, StringComparison.Ordinal);
        Assert.Contains("bank", report, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Loading the corpus from files encodes it with the encoder and reads origins from the provenance sidecar.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AnalysisData_Load_ReadsCorpusFilesAndProvenance()
    {
        // Arrange: a tiny corpus on disk, one holdout row marked real
        string[] vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "bank", "the", "money", "river", "deep", "rich", "wide", "tall", "high", "low", "old", "new", "."];
        string[] fillers = ["deep", "rich", "wide", "tall", "high", "low"];
        var corpus = PrepareCorpus();
        Write("corpus/anchors/a.tsv", string.Concat(fillers.Select(f => $"bank\tnoun\tthe {{bank}} money {f}.\nbank\tverb\tthe {{bank}} river {f}.\n")));
        Directory.CreateDirectory(Path.Combine(corpus, "senses"));
        Directory.CreateDirectory(Path.Combine(corpus, "provenance"));
        Write("corpus/holdout/a.tsv", "bank\tnoun\tthe {bank} money old.\nbank\tverb\tthe {bank} river new.\n");
        Write("corpus/realtext/eval-seed.tsv", "bank\tnoun\tthe {bank} money tall high.\nbank\tother\tthe {bank} river low.\n");
        SenseInventory.WriteFile(Path.Combine(corpus, "senses", "base.tsv"), [new InventoryRow("bank", "noun", ["noun"], "base.tsv"), new InventoryRow("bank", "verb", ["verb"], "base.tsv")]);
        RealImport.WriteProvenance(
            Path.Combine(corpus, "provenance", "p.tsv"),
            [new ProvenanceRow(PosAudit.RowId("bank", "the {bank} money old."), "bank", "noun", "holdout", "tatoeba-cc0", "CC0 1.0", "1", "https://tatoeba.org/1", "x", "real")]);
        using var encoder = new FakeEncoder();

        // Act
        var data = AnalysisData.Load(encoder, new WordPieceTokenizer(vocabulary), corpus, null, new TableBuilderOptions(Parallelism: 1, MaxSequenceLength: 32));
        var filtered = AnalysisData.Load(encoder, new WordPieceTokenizer(vocabulary), corpus, new HashSet<string> { "other" }, new TableBuilderOptions(Parallelism: 1, MaxSequenceLength: 32));

        // Assert: the unknown eval sense is skipped; origins come from the sidecar, eval rows default to real
        var lemma = Assert.Single(data.Lemmas);
        Assert.Equal(3, lemma.Eval.Count);
        Assert.Equal(["real", "llm-corpus", "real"], lemma.Eval.Select(e => e.Origin));
        Assert.Empty(filtered.Lemmas);
    }
}
