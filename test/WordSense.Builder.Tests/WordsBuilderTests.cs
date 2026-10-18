using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the generation of the word table from the corpus.
/// </summary>
public sealed class WordsBuilderTests : IDisposable
{
    private const string BowSenses = TestDirectory.SensesHeader
        + "bow\tbend-noun\tnoun\tbend\tbow\nbow\tbend-verb\tverb\tbend\tbow\nbow\tweapon\tnoun\tweapon\tboh\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Lemmas, their forms, dictionary words and enabled morphology all end up in the table.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_CombinesLemmasFormsLexiconAndMorphology()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbanks\tbank\tnoun,verb\n");
        _dir.Write("corpus/lexicon/lexicon.tsv", "bank\tNV\nquick\tJ\nrun\tV\n");
        _dir.Write("corpus/lexicon/morphology.tsv", "Plural\tenabled\tevidence\nThirdPerson\tenabled\tevidence\nPastTense\tdisabled\tevidence\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(new[] { "bank", "banks", "quick", "run", "runs" }, table.Keys.ToArray());
        Assert.Equal(2, table["bank"].Length);
        Assert.Same(table["bank"], table["banks"]);
        var quick = Assert.Single(table["quick"]);
        Assert.Equal(PartOfSpeech.Adjective, quick.PartOfSpeech);
        var runs = Assert.Single(table["runs"]);
        Assert.Equal("run", runs.Lemma);
        Assert.Equal(PartOfSpeech.Verb, runs.PartOfSpeech);
    }

    /// <summary>
    ///     A trained lemma or form keeps its trained senses first and gains the dictionary's other parts of speech in
    ///     dictionary order; a dictionary-only word, a determiner and a conjunction keep the dictionary order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_MergesDictionaryPartsOfSpeechAfterTrainedSenses()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbanks\tbank\tnoun,verb\n");
        _dir.Write("corpus/lexicon/lexicon.tsv", "albeit\tC\nbank\tBJNV\nbanks\tNR\nso\tBCRJIN\nthe\tD\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(
            [PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Adverb, PartOfSpeech.Adjective],
            table["bank"].Select(d => d.PartOfSpeech));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Pronoun], table["banks"].Select(d => d.PartOfSpeech));
        Assert.All(table["banks"], d => Assert.Equal("bank", d.Lemma));
        Assert.Equal(PartOfSpeech.Conjunction, Assert.Single(table["albeit"]).PartOfSpeech);
        Assert.Equal(PartOfSpeech.Determiner, Assert.Single(table["the"]).PartOfSpeech);
        Assert.Equal(
            [PartOfSpeech.Adverb, PartOfSpeech.Conjunction, PartOfSpeech.Pronoun, PartOfSpeech.Adjective, PartOfSpeech.Interjection, PartOfSpeech.Noun],
            table["so"].Select(d => d.PartOfSpeech));
    }

    /// <summary>
    ///     A listed form answers only with the senses its labels name, as the same instances as the lemma's senses, and the
    ///     lemma keeps all of them in label order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_ListedFormWithLabels_GetsOnlyThoseSenses()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbowed\tbow\tbend-verb\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(
            [PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Noun],
            table["bow"].Select(d => d.PartOfSpeech));
        Assert.Equal(["bend", "bend", "weapon"], table["bow"].Select(d => d.Meaning));
        var bowed = Assert.Single(table["bowed"]);
        Assert.Same(table["bow"][1], bowed);
        Assert.Equal(PartOfSpeech.Verb, bowed.PartOfSpeech);
    }

    /// <summary>
    ///     The labels of a form may be listed in any order and name a subset that is not a prefix: the senses come out in
    ///     the order of the lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_ListedFormLabelsOutOfOrder_UsesLemmaOrder()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbowers\tbow\tweapon,bend-noun\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(2, table["bowers"].Length);
        Assert.Same(table["bow"][0], table["bowers"][0]);
        Assert.Same(table["bow"][2], table["bowers"][1]);
    }

    /// <summary>
    ///     A form that names every sense, in any order, shares the array of its lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_ListedFormWithAllLabels_ReusesLemmaSenses()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbows\tbow\tweapon,bend-verb,bend-noun\nbow\tbow\tbend-noun,bend-verb,weapon\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Same(table["bow"], table["bows"]);
        Assert.Equal(3, table["bow"].Length);
    }

    /// <summary>
    ///     A restricted form still gains the parts of speech of the dictionary that it lacks, after its labeled senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_RestrictedFormWithDictionaryPos_AppendsUntrainedExtraAfterSubset()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbowed\tbow\tbend-verb\n");
        _dir.Write("corpus/lexicon/lexicon.tsv", "bow\tNV\nbowed\tVJ\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(2, table["bowed"].Length);
        Assert.Same(table["bow"][1], table["bowed"][0]);
        Assert.Equal(new WordDetails("bow", PartOfSpeech.Adjective, null, null), table["bowed"][1]);
        Assert.Equal(3, table["bow"].Length);
    }

    /// <summary>
    ///     An unknown, repeated or missing label and a restricted row for the lemma itself fail the build, and the message
    ///     lists every problem.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_InvalidFormLabels_ThrowsListingAllErrors()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write(
            "corpus/forms/f.tsv",
            "form\tlemma\tlabels\nbowed\tbow\tbend-verb,xyz\nbowing\tbow\tbend-verb,bend-verb\nbowers\tbow\t\nbow\tbow\tbend-verb\nbows\tbow\tweapon,qrs\nunrelated\tother\twhatever\n");

        var ex = Assert.Throws<InvalidDataException>(() => WordsBuilder.Build(Path.Combine(_dir.Root, "corpus")));

        Assert.Contains("form 'bowed' of lemma 'bow' lists label 'xyz', which is not a sense of the lemma", ex.Message, StringComparison.Ordinal);
        Assert.Contains("form 'bowing' of lemma 'bow' lists label 'bend-verb' more than once", ex.Message, StringComparison.Ordinal);
        Assert.Contains("form 'bowers' of lemma 'bow' lists no labels", ex.Message, StringComparison.Ordinal);
        Assert.Contains("the row of lemma 'bow' for itself must list every sense", ex.Message, StringComparison.Ordinal);
        Assert.Contains("form 'bows' of lemma 'bow' lists label 'qrs'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("whatever", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A row for the lemma itself that names every sense is allowed and leaves the entry of the lemma unchanged.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_SelfRowWithAllLabels_IsAccepted()
    {
        _dir.Write("corpus/senses/s.tsv", BowSenses);
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbow\tbow\tweapon,bend-verb,bend-noun\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(3, table["bow"].Length);
    }

    /// <summary>
    ///     A listed auxiliary gains an Auxiliary sense after all its other senses, a trained lemma keeps its trained
    ///     senses as a prefix, the forms generated from it do not gain the sense, and the shared arrays stay unchanged.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_AppendsAuxiliarySenseLast()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
        _dir.Write("corpus/forms/f.tsv", "form\tlemma\tlabels\nbanks\tbank\tnoun,verb\n");
        _dir.Write("corpus/lexicon/lexicon.tsv", "bank\tNV\nbanks\tNV\nwill\tVN\n");
        _dir.Write("corpus/lexicon/morphology.tsv", "Plural\tenabled\tevidence\nThirdPerson\tenabled\tevidence\n");
        _dir.Write("corpus/lexicon/auxiliaries.tsv", "will\tmodal\nbanks\tprimary\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal([PartOfSpeech.Verb, PartOfSpeech.Noun, PartOfSpeech.Auxiliary], table["will"].Select(d => d.PartOfSpeech));
        Assert.All(table["will"], d => Assert.Equal("will", d.Lemma));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Auxiliary], table["banks"].Select(d => d.PartOfSpeech));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], table["bank"].Select(d => d.PartOfSpeech));
        Assert.DoesNotContain(table["wills"], d => d.PartOfSpeech == PartOfSpeech.Auxiliary);
    }

    /// <summary>
    ///     An auxiliary that is not in the word table is an error.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_UnknownAuxiliary_Throws()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
        _dir.Write("corpus/lexicon/auxiliaries.tsv", "zzqxjw\tmodal\n");

        var ex = Assert.Throws<InvalidDataException>(() => WordsBuilder.Build(Path.Combine(_dir.Root, "corpus")));

        Assert.Contains("zzqxjw", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corpus without forms, lexicon and morphology still builds from the senses alone.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_SensesOnly()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");

        var table = WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"));

        Assert.Equal(new[] { "bank" }, table.Keys.ToArray());
    }

    /// <summary>
    ///     An inventory that violates the rules fails the build.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Build_InvalidInventory_Throws()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\n        bank\tverb\tbogus\t\t\n");

        Assert.Throws<InvalidDataException>(() => WordsBuilder.Build(Path.Combine(_dir.Root, "corpus")));
    }

    /// <summary>
    ///     Write returns the word count and the file is the table that Build produced.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_Write_WritesBuiltTable()
    {
        _dir.Write("corpus/senses/s.tsv", TestDirectory.SensesHeader + "bank\tnoun\tnoun\t\t\nbank\tverb\tverb\t\t\n");
        var target = Path.Combine(_dir.Root, "out", "words.bin");

        var count = WordsBuilder.Write(Path.Combine(_dir.Root, "corpus"), target);

        Assert.Equal(1, count);
        Assert.Equal(WordsBlobWriter.Write(WordsBuilder.Build(Path.Combine(_dir.Root, "corpus"))), File.ReadAllBytes(target));
    }

    /// <summary>
    ///     The default output is the resource of the core package.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WordsBuilder_DefaultOutput_IsCoreResource()
    {
        Assert.Equal("src/DemaConsulting.WordSense/Resources/words.bin", WordsBuilder.DefaultOutput);
    }
}
