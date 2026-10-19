using WordSense.Builder.Data;
using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the corpus reader, the logistic trainer, the sense inventory, the evaluator, the appendix and the committed data.
/// </summary>
public sealed class BuilderTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
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
    ///     The lenient reader returns the valid rows and one message per malformed row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentencesLenient_CollectsErrors()
    {
        var path = Write("l.tsv", "bank\tmoney\tA {bank}.\nbank\tmoney\tNo target.\n");

        var (rows, errors) = Corpus.ReadSentencesLenient(path);

        Assert.Single(rows);
        Assert.Single(errors);
    }

    /// <summary>
    ///     A missing directory yields no rows, and duplicates across sets are detected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_DirectoryAndDisjointness()
    {
        Assert.Empty(Corpus.ReadDirectory(Path.Combine(_dir.Root, "none")));
        var a = Row("bank", "money", "A {bank}.", "a.tsv");
        var b = Row("bank", "river", "A {bank}.", "b.tsv");
        Assert.Throws<InvalidDataException>(() => Corpus.EnsureDisjoint([a], [b]));
        Corpus.EnsureDisjoint([a], [Row("bank", "river", "Another {bank}.", "b.tsv")]);
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
    ///     The rules require meanings all-or-none, two distinct meanings for a heteronym, unique (part of speech, meaning) pairs and consistent respellings.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ValidateRules_FlagsViolations()
    {
        var inventory = new SenseInventory(
        [
            new InventoryRow("good", "a", PartOfSpeech.Noun, null, null, "s.tsv"),
            new InventoryRow("good", "b", PartOfSpeech.Verb, null, null, "s.tsv"),
            new InventoryRow("hetero", "a", PartOfSpeech.Noun, "bend", "bou", "s.tsv"),
            new InventoryRow("hetero", "b", PartOfSpeech.Verb, "bend", "bou", "s.tsv"),
            new InventoryRow("hetero", "c", PartOfSpeech.Noun, "weapon", null, "s.tsv"),
            new InventoryRow("mixed", "a", PartOfSpeech.Noun, "x", null, "s.tsv"),
            new InventoryRow("mixed", "b", PartOfSpeech.Verb, null, null, "s.tsv"),
            new InventoryRow("single", "a", PartOfSpeech.Noun, "x", null, "s.tsv"),
            new InventoryRow("single", "b", PartOfSpeech.Verb, "x", null, "s.tsv"),
            new InventoryRow("dup", "a", PartOfSpeech.Noun, null, null, "s.tsv"),
            new InventoryRow("dup", "b", PartOfSpeech.Noun, null, null, "s.tsv"),
            new InventoryRow("wobble", "a", PartOfSpeech.Noun, "x", "bou", "s.tsv"),
            new InventoryRow("wobble", "b", PartOfSpeech.Verb, "x", "bow", "s.tsv"),
            new InventoryRow("wobble", "c", PartOfSpeech.Noun, "y", "bo", "s.tsv"),
            new InventoryRow("shared", "a", PartOfSpeech.Noun, "x", "bou", "s.tsv"),
            new InventoryRow("shared", "b", PartOfSpeech.Noun, "y", "bou", "s.tsv"),
            new InventoryRow("undef", "a", (PartOfSpeech)999, null, null, "s.tsv"),
            new InventoryRow("undef", "b", PartOfSpeech.Verb, null, null, "s.tsv"),
            new InventoryRow("lonely", "a", PartOfSpeech.Noun, null, null, "s.tsv"),
        ]);

        var errors = inventory.ValidateRules();

        Assert.Equal(["dup", "lonely", "mixed", "shared", "single", "undef", "wobble"], errors.Select(e => e.Split('\'')[1]).Order(StringComparer.Ordinal));
        Assert.Contains(errors, e => e.Contains("inconsistent respellings", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("distinct respellings", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("undefined part of speech", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("at least two senses", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Two senses of one part of speech are valid when they carry different meanings.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ValidateRules_AllowsTwoSensesOfOnePartOfSpeechWithDifferentMeanings()
    {
        var inventory = new SenseInventory(
        [
            new InventoryRow("sewer", "drain", PartOfSpeech.Noun, "drain", null, "s.tsv"),
            new InventoryRow("sewer", "stitcher", PartOfSpeech.Noun, "stitcher", null, "s.tsv"),
        ]);

        Assert.Empty(inventory.ValidateRules());
        Assert.Equal(["drain", "stitcher"], inventory.DefinitionsOf("sewer").Select(d => d.Meaning).ToArray());
    }

    /// <summary>
    ///     The meaning and respelling columns round-trip, are optional (empty) and the header is the five-column one.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_MeaningAndRespelling_RoundTripAndAreOptional()
    {
        SenseInventory.WriteFile(
            Path.Combine(_dir.Root, "grp", "x.tsv"),
            [
                new InventoryRow("bow", "bend-noun", PartOfSpeech.Noun, "bend", "bou", "x.tsv"),
                new InventoryRow("bow", "bend-verb", PartOfSpeech.Verb, "bend", "bou", "x.tsv"),
                new InventoryRow("bow", "weapon", PartOfSpeech.Noun, "weapon", null, "x.tsv"),
                new InventoryRow("bank", "noun", PartOfSpeech.Noun, null, null, "x.tsv"),
            ]);

        var inventory = SenseInventory.ReadDirectory(Path.Combine(_dir.Root, "grp"));

        Assert.Equal(new SenseDefinition(PartOfSpeech.Noun, "bend", "bou"), inventory.DefinitionsOf("bow")[0]);
        Assert.Equal(new SenseDefinition(PartOfSpeech.Noun, "weapon"), inventory.DefinitionsOf("bow")[2]);
        Assert.Equal(new SenseDefinition(PartOfSpeech.Noun), inventory.DefinitionsOf("bank")[0]);
        Assert.StartsWith("lemma\tlabel\tpos\tmeaning\trespelling\n", File.ReadAllText(Path.Combine(_dir.Root, "grp", "x.tsv")), StringComparison.Ordinal);
    }

    /// <summary>
    ///     An invalid part of speech, meaning or respelling is rejected with the file and line.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_InvalidColumns_AreRejected()
    {
        Write("bad-col/a.tsv", "lemma\tlabel\tpos\tmeaning\trespelling\nbank\tnoun\tNoun\t\t\nbank\tb\tverb\tBad Meaning\t\nbank\tc\tverb\t\tbow\n");

        var ex = Assert.Throws<InvalidDataException>(() => SenseInventory.ReadDirectory(Path.Combine(_dir.Root, "bad-col")));

        Assert.Contains("a.tsv:2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a.tsv:3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a.tsv:4", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The inventory rejects malformed files with every problem found.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ReadDirectory_RejectsMalformedRows()
    {
        var dir = Path.Combine(_dir.Root, "inv");
        Write("inv/a.tsv", "lemma\tlabel\tpos\tmeaning\trespelling\nbank\tnoun\tnoun\t\t\nbank\tnoun\tnoun\t\t\nbank\tadj\tAdj\t\t\nBank\tx\tnoun\t\t\nbank\ty\nwill\taux\tauxiliary\t\t\n");
        Write("inv/b.tsv", "wrong header\n");

        var ex = Assert.Throws<InvalidDataException>(() => SenseInventory.ReadDirectory(dir));

        Assert.Contains("already listed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pos must be one of", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'auxiliary' cannot be trained", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lemma must be lower-case", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected 5 tab-separated columns", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b.tsv:1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A file can be left out of the inventory, and a missing directory is an empty inventory.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ReadDirectory_SkipsFileAndToleratesMissingDirectory()
    {
        Write("skip/a.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\n");
        Write("skip/b.tsv", "garbage\n");

        var inventory = SenseInventory.ReadDirectory(Path.Combine(_dir.Root, "skip"), "b.tsv");

        Assert.Equal(1, inventory.LemmaCount);
        Assert.True(inventory.ContainsLemma("bank"));
        Assert.False(inventory.ContainsLemma("other"));
        Assert.Equal(0, SenseInventory.ReadDirectory(Path.Combine(_dir.Root, "missing")).LemmaCount);
    }

    /// <summary>
    ///     The inventory round-trips through its file format.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_WriteAndRead_RoundTrips()
    {
        var path = Path.Combine(_dir.Root, "inv", "x.tsv");
        SenseInventory.WriteFile(
            path,
            [new InventoryRow("bow", "bend", PartOfSpeech.Noun, "bend", null, "x.tsv"), new InventoryRow("bow", "weapon", PartOfSpeech.Noun, "weapon", null, "x.tsv")]);

        var inventory = SenseInventory.ReadDirectory(Path.Combine(_dir.Root, "inv"));

        Assert.Equal(2, inventory.Rows.Count());
        Assert.Equal(1, inventory.LemmaCount);
        Assert.True(inventory.Contains("bow", "bend"));
        Assert.False(inventory.Contains("bow", "x"));
        Assert.Equal(["bend", "weapon"], inventory.LabelsOf("bow"));
        Assert.Equal(["bend", "weapon"], inventory.DefinitionsOf("bow").Select(d => d.Meaning));
        Assert.Equal(PartOfSpeech.Noun, inventory.DefinitionsOf("bow")[0].Pos);
    }

    /// <summary>
    ///     Validation reports anchors without an inventory entry, entries without anchors, holdout senses and forms labels that are no senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_ChecksBothDirections()
    {
        var inventory = new SenseInventory([Inv("bank", "noun"), Inv("bank", "orphan")]);
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
    ///     The labels of the explicit forms are checked: unknown, repeated and missing labels and a restricted row for the
    ///     lemma itself are reported, and forms of untrained lemmas are ignored.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_CheckForms_ReportsEveryViolation()
    {
        var inventory = new SenseInventory([Inv("bank", "noun"), Inv("bank", "verb", PartOfSpeech.Verb)]);
        var forms = new List<(string Form, string Lemma, string[] Labels)>
        {
            ("banks", "bank", ["noun", "money"]),
            ("banked", "bank", ["verb", "verb"]),
            ("banker", "bank", []),
            ("bank", "bank", ["noun"]),
            ("other", "other", ["whatever"]),
            ("bankers", "bank", ["verb", "noun"]),
            ("bank", "bank", ["verb", "noun"]),
        };

        var errors = inventory.CheckForms(forms);

        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Contains("form 'banks' of lemma 'bank' lists label 'money'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("form 'banked' of lemma 'bank' lists label 'verb' more than once", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("form 'banker' of lemma 'bank' lists no labels", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("the row of lemma 'bank' for itself must list every sense", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("whatever", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The senses a form names are the positions of its labels in the order of the lemma's labels.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_FormSenseIndexes_FollowsLemmaOrder()
    {
        var inventory = new SenseInventory([Inv("bow", "weapon"), Inv("bow", "bend-noun"), Inv("bow", "bend-verb", PartOfSpeech.Verb)]);

        Assert.Equal([0, 2], inventory.FormSenseIndexes("bow", ["weapon", "bend-noun"]));
        Assert.Equal([1], inventory.FormSenseIndexes("bow", ["bend-verb"]));
        Assert.Equal([0, 1, 2], inventory.FormSenseIndexes("bow", ["weapon", "bend-verb", "bend-noun"]));
        Assert.Empty(inventory.FormSenseIndexes("bow", ["unknown"]));
    }

    /// <summary>
    ///     A sentence of the anchors or the holdout that uses a listed form as a sense the form's labels do not name is
    ///     reported; a self row and a form of another lemma are not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_SentenceWithSenseOutsideFormLabels_IsReported()
    {
        var inventory = new SenseInventory([Inv("bank", "noun"), Inv("bank", "verb", PartOfSpeech.Verb)]);
        var anchors = new[]
        {
            Row("bank", "noun", "A {bank}.", "a.tsv"),
            Row("bank", "verb", "They {banks} it.", "a.tsv"),
            Row("bank", "verb", "He {Banks} here.", "a.tsv"),
            Row("bank", "noun", "Two {banks}.", "a.tsv"),
        };
        var holdout = new[] { Row("bank", "verb", "She {banks} early.", "h.tsv") };
        var forms = new List<(string Form, string Lemma, string[] Labels)> { ("banks", "bank", ["noun"]), ("bank", "bank", ["noun", "verb"]) };

        var ex = Assert.Throws<InvalidDataException>(() => inventory.Validate(anchors, holdout, forms));

        Assert.Contains("anchors/a.tsv: 2 sentence(s) use the form 'banks' as sense 'bank/verb'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("holdout/h.tsv: 1 sentence(s) use the form 'banks' as sense 'bank/verb'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bank/noun", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The committed corpus is consistent: every form row is valid and no anchor or holdout sentence uses a listed
    ///     form as a sense outside its labels.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_CommittedCorpus_IsConsistent()
    {
        var corpus = Path.Combine(FindRepositoryRoot().FullName, "corpus");
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var forms = Directory.GetFiles(Path.Combine(corpus, "forms"), "*.tsv").OrderBy(f => f, StringComparer.Ordinal).SelectMany(Corpus.ReadForms).ToList();

        inventory.Validate(Corpus.ReadDirectory(Path.Combine(corpus, "anchors")), Corpus.ReadDirectory(Path.Combine(corpus, "holdout")), forms);
    }

    /// <summary>
    ///     A consistent corpus passes validation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_ConsistentCorpus_Passes()
    {
        var inventory = new SenseInventory([Inv("bank", "noun")]);

        inventory.Validate([Row("bank", "noun", "A {bank}.", "a.tsv")], [Row("bank", "noun", "B {bank}.", "h.tsv")], [("banks", "bank", ["noun"])]);
    }

    /// <summary>
    ///     A plan row takes the part of speech from its tags and the meaning from the group, else the label.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InventoryRow_FromPlan_DerivesPartOfSpeechAndMeaning()
    {
        var grouped = InventoryRow.FromPlan("bow", "bend-noun", ["bend", "noun"], "bend", "p.tsv");
        var labelled = InventoryRow.FromPlan("fast", "adj", ["adj"], null, "p.tsv");

        Assert.Equal(PartOfSpeech.Noun, grouped.Pos);
        Assert.Equal("bend", grouped.Meaning);
        Assert.Equal(PartOfSpeech.Adjective, labelled.Pos);
        Assert.Equal("adjective", labelled.Meaning);
        Assert.Throws<InvalidDataException>(() => InventoryRow.FromPlan("bow", "x", ["other"], null, "p.tsv"));
        Assert.Null(InventoryRow.PosOfTag("other"));
        Assert.Equal(PartOfSpeech.Adverb, InventoryRow.PosOfTag("adv"));
    }

    /// <summary>
    ///     Meanings and respellings follow their patterns.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseDefinition_ValidatesMeaningAndRespelling()
    {
        Assert.True(SenseDefinition.IsValidMeaning("bend-2"));
        Assert.False(SenseDefinition.IsValidMeaning("Bend"));
        Assert.False(SenseDefinition.IsValidMeaning("2bend"));
        Assert.False(SenseDefinition.IsValidMeaning(null));
        Assert.False(SenseDefinition.IsValidMeaning(new string('a', 33)));
        Assert.True(SenseDefinition.IsValidRespelling("bou-it's"));
        Assert.False(SenseDefinition.IsValidRespelling("bow!"));
        Assert.False(SenseDefinition.IsValidRespelling(string.Empty));

        new SenseDefinition(PartOfSpeech.Noun, "bend", "bou").Validate();
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, null, "bou").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition(PartOfSpeech.Noun, "Bad").Validate());
        Assert.Throws<ArgumentException>(() => new SenseDefinition((PartOfSpeech)999).Validate());
    }

    /// <summary>
    ///     A lemma entry has one shared answer per sense and rejects inconsistent sense sets.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LemmaEntry_BuildsCandidatesAndRejectsInconsistentSenses()
    {
        var bank = new LemmaEntry("bank", [new SenseDefinition(PartOfSpeech.Noun), new SenseDefinition(PartOfSpeech.Verb)], true);
        var bow = new LemmaEntry(
            "bow",
            [new SenseDefinition(PartOfSpeech.Noun, "bend", "bou"), new SenseDefinition(PartOfSpeech.Noun, "weapon")],
            true);

        Assert.False(bank.IsHeteronym);
        Assert.True(bank.IsContextual);
        Assert.Equal(2, bank.Candidates.Count);
        Assert.Equal(PartOfSpeech.Verb, bank.Candidates[1].PartOfSpeech);
        Assert.Equal("bank", bank.Candidates[1].Lemma);
        Assert.True(bow.IsHeteronym);
        Assert.Equal("weapon", bow.Candidates[1].Meaning);

        Assert.Throws<ArgumentException>(() => new LemmaEntry("x", [], true));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("x", [new SenseDefinition(PartOfSpeech.Noun)], true));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("x", [new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb, "b")]));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("x", [new SenseDefinition(PartOfSpeech.Noun, "a"), new SenseDefinition(PartOfSpeech.Verb)], true));
        Assert.Throws<ArgumentException>(() => new LemmaEntry("x", [new SenseDefinition(PartOfSpeech.Noun), new SenseDefinition(PartOfSpeech.Noun)], true));
        Assert.Throws<ArgumentNullException>(() => new LemmaEntry(null!, [new SenseDefinition(PartOfSpeech.Noun)]));
        var single = new LemmaEntry("run", [new SenseDefinition(PartOfSpeech.Verb)]);
        Assert.False(single.IsContextual);
    }

    /// <summary>
    ///     The evaluation renders one row per set with percentages, n/a for empty sets and the three measurements.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EvaluationResult_Render_FormatsSetsAndMeasurements()
    {
        var result = new EvaluationResult(
            "demo",
            [new SetResult("holdout", 4, 3, 4, 2, 1, 2, 2), new SetResult("real-text", 0, 0, 0, 0, 0, 0, 0)],
            2048,
            1.5,
            12,
            2097152);

        var text = result.Render();

        Assert.StartsWith("### demo\n\n| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 4 | 75.00% | 100.00% | 50.00% | 50.00% | 100.00% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| real-text | 0 | n/a | n/a | n/a | n/a | n/a |\n", text, StringComparison.Ordinal);
        Assert.Contains("- Weights size: 2 KiB\n", text, StringComparison.Ordinal);
        Assert.Contains("- Classification latency: 1.50 ms per sentence\n", text, StringComparison.Ordinal);
        Assert.Contains("- Word table first use: 12 ms, 2.0 MiB\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    /// <summary>
    ///     An encoder evaluation renders the confidence calibration buckets and the threshold table of its sets.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EvaluationResult_Render_EncoderModel_IncludesCalibrationAndThresholds()
    {
        (double Confidence, bool Correct)[] outcomes = [(0.95, true), (0.85, true), (0.65, false), (0.2, false)];
        var result = new EvaluationResult(
            "demo",
            [new SetResult("real-text", 4, 2, 4, 2, 1, 2, 2, outcomes)],
            2048,
            1.5,
            12,
            2097152);

        var text = result.Render();

        Assert.Contains("#### Confidence calibration (rows; best-sense accuracy)\n", text, StringComparison.Ordinal);
        Assert.Contains("| real-text | 1 / 0.00% | 0 / n/a | 1 / 0.00% | 0 / n/a | 1 / 100.00% | 1 / 100.00% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| real-text | 0.6 | 75.00% | 66.67% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| real-text | 0.9 | 25.00% | 100.00% |\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#### Comparison", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The statistical model is evaluated on the holdout without any model files.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_BasicModel_CountsHoldoutRows()
    {
        Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "walk\tnoun\tnoun\t\t\nwalk\tverb\tverb\t\t\n");
        Write("corpus/holdout/h.tsv", "walk\tverb\tYou should {walk} more.\nwalk\tnoun\tThe {walk} was long.\n");
        using var progress = new StringWriter();

        var result = await Evaluator.RunAsync(_dir.Root, Path.Combine(_dir.Root, "corpus"), ModelCatalog.Find("basic"), progress, TestContext.Current.CancellationToken);

        Assert.Equal(["holdout", "real-text"], result.Sets.Select(s => s.Name));
        Assert.Equal(2, result.Sets[0].Rows);
        Assert.Equal(0, result.Sets[1].Rows);
        Assert.Equal(0, result.WeightsBytes);
        Assert.Contains("holdout done", progress.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The appendix explains the parts of speech and meanings, lists lemmas alphabetically with senses, meanings, respellings,
    ///     bold examples and forms (restricted forms in parentheses), keeps every line within the limit and is byte-identical on repeat.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Generate_ListsLemmasSensesAndForms()
    {
        // Arrange: two lemmas written in reverse order, one heteronym with a respelling, a restricted form and a long example
        var corpus = Path.Combine(_dir.Root, "corpus");
        var longSentence = "He {took} " + string.Join(' ', Enumerable.Repeat("another remarkably long stretch of words", 8)) + ".";
        Write("corpus/anchors/a.tsv", "zip\tnoun\tShe pulled the {zip} on the old bag.\n" +
            "zip\tnoun\tA {zip} is a fastener that joins two edges of cloth.\n" +
            "zip\tverb\tTo {zip}.\n" +
            "bow\tbend-verb\tThey {bowed} to the audience *twice* at the end.\n" +
            "bow\tweapon\tHe drew a {bow}.\n" +
            "bow\tbend-noun\t" + longSentence + "\n");
        Write("corpus/senses/s.tsv", "lemma\tlabel\tpos\tmeaning\trespelling\nzip\tnoun\tnoun\t\t\nzip\tverb\tverb\t\t\n" +
            "bow\tbend-verb\tverb\tbend\tbow\nbow\tbend-noun\tnoun\tbend\tbow\nbow\tweapon\tnoun\tweapon\t\n");
        Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nzips\tzip\tnoun,verb\nbowed\tbow\tbend-verb\nzip\tzip\tnoun,verb\nother\tunknown\tx\n");

        // Act
        var text = AppendixGenerator.Generate(corpus);
        var again = AppendixGenerator.Generate(corpus);

        // Assert
        Assert.Equal(text, again);
        Assert.StartsWith("# Appendix: Supported Senses\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", text, StringComparison.Ordinal);
        Assert.All(text.Split('\n'), line => Assert.True(line.Length <= AppendixGenerator.LineLimit, line));
        Assert.Contains($"| {"`Noun`",-14} | {3,6} |", text, StringComparison.Ordinal);
        Assert.Contains($"| {"`Verb`",-14} | {2,6} |", text, StringComparison.Ordinal);
        Assert.Contains("The library supports 2 lemmas with 5 senses in total", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("- **bow**;", StringComparison.Ordinal) < text.IndexOf("- **zip**;", StringComparison.Ordinal));
        Assert.Contains("- **bow**; parts of speech: noun, verb; heteronym, meanings: bend, weapon; forms: bowed (verb bend)\n", text, StringComparison.Ordinal);
        Assert.Contains("- **zip**; parts of speech: noun, verb; forms: zips\n", text, StringComparison.Ordinal);
        Assert.Contains("  - `noun`; meaning: `bend`; respelling: `bow`; example: He **took** ", text, StringComparison.Ordinal);
        Assert.Contains("  - `verb`; meaning: `bend`; respelling: `bow`; example: They **bowed** to the audience \\*twice\\* at the end.\n", text, StringComparison.Ordinal);
        Assert.Contains("  - `noun`; meaning: `weapon`; example: He drew a **bow**.\n", text, StringComparison.Ordinal);
        Assert.Contains("  - `noun`; example: She pulled the **zip** on the old bag.\n", text, StringComparison.Ordinal);
        Assert.Contains("  - `verb`; example: To **zip**.\n", text, StringComparison.Ordinal);
        Assert.Contains("### Lemmas Starting With B\n", text, StringComparison.Ordinal);
        Assert.Contains("### Lemmas Starting With Z\n", text, StringComparison.Ordinal);
        Assert.Contains(text.Split('\n'), line => line.StartsWith("    ", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The generator rejects a missing directory, a sense without an anchor and a form label that is not a sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Generate_RejectsIncompleteInput()
    {
        // Arrange: only the senses exist, and the forms file names an unknown label
        var corpus = Path.Combine(_dir.Root, "corpus");
        Write("corpus/senses/s.tsv", "lemma\tlabel\tpos\tmeaning\trespelling\nzip\tnoun\tnoun\t\t\nzip\tverb\tverb\t\t\n");

        // Act and assert: missing forms and anchors directories
        var missing = Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus));
        Assert.Contains("forms", missing.Message, StringComparison.Ordinal);
        Assert.Contains("anchors", missing.Message, StringComparison.Ordinal);

        // Act and assert: no anchor for the verb sense, and a form with an unknown label
        Write("corpus/anchors/a.tsv", "zip\tnoun\tShe pulled the {zip} on the old bag.\n");
        Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nzips\tzip\tnoun,bogus\n");
        var incomplete = Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus));
        Assert.Contains("'zip/verb' has no anchor sentence", incomplete.Message, StringComparison.Ordinal);
        Assert.Contains("label 'bogus'", incomplete.Message, StringComparison.Ordinal);

        // Act and assert: an empty inventory
        File.Delete(Path.Combine(corpus, "senses", "s.tsv"));
        Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus));
    }

    /// <summary>
    ///     The committed user-guide appendix equals what the generator produces from the committed corpus, so it
    ///     cannot go stale: regenerate it with <c>appendix</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Committed_MatchesCorpus()
    {
        var root = FindRepositoryRoot();
        var committedPath = Path.Combine(root.FullName, "docs", "user_guide", "appendix-senses.md");
        var regenerated = Path.Combine(_dir.Root, "regenerated", "appendix-senses.md");

        AppendixGenerator.Write(Path.Combine(root.FullName, "corpus"), regenerated);

        // Line endings are normalized, so a checkout with CRLF conversion does not matter
        var expected = File.ReadAllText(regenerated).ReplaceLineEndings("\n");
        var committed = File.ReadAllText(committedPath).ReplaceLineEndings("\n");
        Assert.True(
            expected == committed,
            "docs/user_guide/appendix-senses.md is stale. Run: dotnet run --project tools/WordSense.Builder -- appendix");
    }

    /// <summary>
    ///     The committed image was compiled from the committed data tables and corpus: its data hash equals the hash of
    ///     the current inputs, and its counts equal those of a fresh compilation, so it cannot go stale. Regenerate it
    ///     with <c>compile-image</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_MatchesData()
    {
        var root = FindRepositoryRoot();
        var committed = File.ReadAllBytes(Path.Combine(root.FullName, ImageCompiler.DefaultOutput.Replace('/', Path.DirectorySeparatorChar)));

        var (image, result) = ImageCompiler.Build(Path.Combine(root.FullName, "data"));

        var open = DemaConsulting.WordSense.WordImage.Open(committed);
        var fresh = DemaConsulting.WordSense.WordImage.Open(image);
        Assert.True(
            open.DataHash.SequenceEqual(ImageCompiler.DataHash(Path.Combine(root.FullName, "data"))),
            "src/DemaConsulting.WordSense/Resources/wordsense.img is stale; run the 'compile-image' command.");
        Assert.Equal(result.Spellings, open.SpellingCount);
        Assert.Equal(result.Entries, open.EntryCount);
        foreach (var id in fresh.SectionIds)
        {
            Assert.True(fresh.RawSection(id).AsSpan().SequenceEqual(open.RawSection(id)), $"Section {id} of the committed image differs from a fresh compilation.");
        }
    }

    /// <summary>
    ///     Every word of the committed dictionary list is a key of the embedded word table: the accuracy gate never decides
    ///     whether a word exists, so determiners, conjunction-only and interjection-only words and the words that a trained
    ///     lemma or form spells are all present, and a trained word keeps its trained senses first.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_HoldsEveryDictionaryWord()
    {
        var root = FindRepositoryRoot();
        var lexicon = LexiconSource.ReadWords(Path.Combine(root.FullName, "corpus", "lexicon", "lexicon.tsv"));
        var table = new LexiconView();

        var missing = lexicon.Where(w => !table.ContainsKey(w.Word)).Select(w => w.Word).Take(10).ToList();
        Assert.True(missing.Count == 0, "Words of lexicon.tsv missing from the word image: " + string.Join(", ", missing));
        Assert.True(lexicon.Count > 143_000);

        // Every part of speech the dictionary lists for a word is among the senses of that word, without exception, except
        // for the analyses that the forms audit dropped in form-corrections.tsv: our own sources show
        // the analysis wrong (a plural of a verb-only word, a past spelled with one consonant), so the word list entry
        // is not made true by a wrong analysis
        var dropped = File.ReadLines(Path.Combine(root.FullName, "corpus", "lexicon", "form-corrections.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'))
            .Where(p => p.Length > 4 && p[4] == "drop")
            .Select(p => p[0])
            .ToHashSet(StringComparer.Ordinal);
        var lost = lexicon
            .Where(w => !dropped.Contains(w.Word))
            .Where(w => w.Letters.Any(l => table[w.Word].All(d => (int)d.PartOfSpeech != LexiconSource.Letters.IndexOf(l, StringComparison.Ordinal))))
            .Select(w => w.Word)
            .Take(10)
            .ToList();
        Assert.True(lost.Count == 0, "Parts of speech of lexicon.tsv missing from the word image: " + string.Join(", ", lost));
        Assert.All(new[] { "the", "a", "an", "his", "albeit", "although", "alas", "ah", "two" }, w => Assert.True(table.ContainsKey(w), w));
    }

    /// <summary>
    ///     Every word of the committed closed list of auxiliaries has an Auxiliary sense in the embedded word table, and
    ///     that sense follows every other sense of the word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_AuxiliarySenseComesLast()
    {
        var root = FindRepositoryRoot();
        var table = new LexiconView();
        var auxiliaries = LexiconSource.ReadAuxiliaries(Path.Combine(root.FullName, "corpus", "lexicon", "auxiliaries.tsv"));

        var problems = auxiliaries
            .Where(w => !table.ContainsKey(w)
                || table[w].All(d => d.PartOfSpeech != PartOfSpeech.Auxiliary)
                || table[w][^1].PartOfSpeech != PartOfSpeech.Auxiliary)
            .Take(10)
            .ToList();

        Assert.True(problems.Count == 0, "Auxiliaries without a last Auxiliary sense: " + string.Join(", ", problems));
        Assert.True(auxiliaries.Count > 20);
    }

    /// <summary>
    ///     A listed form has the trained senses its labels name, carrying the lemma's lemma, meaning and respelling,
    ///     followed by one sense for each dictionary part of speech the labels do not cover; that sense is the form's
    ///     own (the form is its lemma, because a form may have several lemmas) and has no meaning and no respelling.
    ///     The one exception is the closed-list Auxiliary sense, which keeps the lemma of the form (<c>was</c> of
    ///     <c>be</c>). A generated analysis of another entry of the same lemma (a second verb entry <c>bow</c> of
    ///     <c>bowing</c>, or of another dictionary lemma whose generated form it is, <c>swinge</c> of <c>swinging</c>) or
    ///     an extra analysis of the form's own lemma may follow.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_FormSensesFollowLabelsThenOwnExtras()
    {
        var root = FindRepositoryRoot();
        var corpus = Path.Combine(root.FullName, "corpus");
        var table = new LexiconView();
        var dictionary = LexiconSource.ReadWords(Path.Combine(corpus, "lexicon", "lexicon.tsv")).ToDictionary(w => w.Word, w => w.Letters, StringComparer.Ordinal);
        var forms = Directory.GetFiles(Path.Combine(corpus, "forms"), "*.tsv").SelectMany(Corpus.ReadForms).ToList();
        var checkedForms = 0;
        var problems = new List<string>();
        foreach (var (form, _, _) in forms.Where(f => f.Form != f.Lemma && table.ContainsKey(f.Lemma) && table.ContainsKey(f.Form)))
        {
            var senses = table[form].Where(d => d.PartOfSpeech != PartOfSpeech.Auxiliary).ToList();
            var trained = senses.Where(d => d.SenseId is not null).ToList();
            var extras = senses.Where(d => d.SenseId is null).ToList();
            var covered = trained.Select(d => d.PartOfSpeech).ToHashSet();
            var expected = (dictionary.TryGetValue(form, out var letters) ? letters : string.Empty)
                .Select(l => (PartOfSpeech)LexiconSource.Letters.IndexOf(l, StringComparison.Ordinal))
                .Where(p => !covered.Contains(p))
                .ToList();
            if (trained.Count == 0
                || extras.Any(d => d.Lemma != form && !table.ContainsKey(d.Lemma))
                || expected.Any(p => extras.All(d => d.PartOfSpeech != p)))
            {
                problems.Add(form);
            }

            checkedForms++;
        }

        Assert.True(problems.Count == 0, "Forms that break the label/extra rules: " + string.Join(", ", problems.Take(10)));
        Assert.True(checkedForms > 1000);
    }

    /// <summary>
    ///     The eight verb-only forms (<c>bent</c>, <c>chanced</c>, <c>drove</c>, <c>flew</c>, <c>limited</c>, <c>rode</c>,
    ///     <c>scored</c>, <c>shook</c>) whose dictionary entry lists parts of speech that the labels do not cover: each keeps
    ///     the verb sense of its lemma and gains analyses of the form's own (an entry whose lemma is the form, without a
    ///     sense id): <c>drove</c> is the verb of <c>drive</c> and a noun of its own, not the noun sense of <c>drive</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_RestrictedFormsGainTheirOwnExtra()
    {
        var table = new LexiconView();
        var expected = new (string Form, string Lemma, PartOfSpeech[] Extras)[]
        {
            ("bent", "bend", [PartOfSpeech.Adjective, PartOfSpeech.Noun]),
            ("chanced", "chance", [PartOfSpeech.Noun]),
            ("drove", "drive", [PartOfSpeech.Noun]),
            ("flew", "fly", [PartOfSpeech.Noun]),
            ("limited", "limit", [PartOfSpeech.Adjective, PartOfSpeech.Noun]),
            ("rode", "ride", [PartOfSpeech.Noun]),
            ("scored", "score", [PartOfSpeech.Noun]),
            ("shook", "shake", [PartOfSpeech.Noun]),
        };

        foreach (var (form, lemma, extraPos) in expected)
        {
            var senses = table[form];
            var trained = senses.Take(senses.Length - extraPos.Length).ToList();
            Assert.NotEmpty(trained);
            Assert.All(trained, d =>
            {
                Assert.Equal(lemma, d.Lemma);
                Assert.Equal(PartOfSpeech.Verb, d.PartOfSpeech);
            });

            var extras = senses.Skip(trained.Count).ToList();
            Assert.Equal(extraPos, extras.Select(e => e.PartOfSpeech));
            Assert.All(extras, e =>
            {
                Assert.Equal(form, e.Lemma);
                Assert.Null(e.SenseId);
            });
        }
    }

    /// <summary>
    ///     The Basic row of the README model table equals the measurement of the evaluator on the committed holdout and
    ///     real-text sets (the accuracy columns; the timing columns depend on the machine).
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation()
    {
        var root = FindRepositoryRoot();
        var table = await File.ReadAllLinesAsync(Path.Combine(root.FullName, "docs", "build_notes", "model-table.md"), TestContext.Current.CancellationToken);
        var committed = Assert.Single(table, l => l.StartsWith("| Basic |", StringComparison.Ordinal)).Split('|')[..6];
        using var progress = new StringWriter();

        var result = await Evaluator.RunAsync(root.FullName, Path.Combine(root.FullName, "corpus"), ModelCatalog.Find("basic"), progress, TestContext.Current.CancellationToken);

        var measured = result.RenderTableRow().Split('|')[..6];
        Assert.Equal(measured, committed);
    }

    /// <summary>
    ///     A dictionary-style view of the word lexicon for the checks of this class.
    /// </summary>
    private sealed class LexiconView
    {
        /// <summary>
        ///     Gets the analyses of a spelling, or an empty array when the lexicon does not know it.
        /// </summary>
        /// <param name="word">The spelling.</param>
        /// <returns>The analyses.</returns>
        public System.Collections.Immutable.ImmutableArray<DemaConsulting.WordSense.WordDetails> this[string word] =>
            DemaConsulting.WordSense.WordLexicon.Lookup(word);

        /// <summary>
        ///     Checks whether the lexicon knows a spelling.
        /// </summary>
        /// <param name="word">The spelling.</param>
        /// <returns>True when the spelling is known.</returns>
        public bool ContainsKey(string word) => !this[word].IsDefaultOrEmpty;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    private static InventoryRow Inv(string lemma, string label, PartOfSpeech pos = PartOfSpeech.Noun, string source = "s.tsv") =>
        new(lemma, label, pos, label, null, source);

    private string Write(string relative, string content) => _dir.Write(relative, content);

    private CorpusRow Row(string lemma, string sense, string sentence, string file)
    {
        var path = Write("rows/" + Guid.NewGuid().ToString("N") + "/" + file, $"{lemma}\t{sense}\t{sentence}\n");
        return Assert.Single(Corpus.ReadSentences(path));
    }
}
