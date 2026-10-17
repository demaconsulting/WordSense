using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the generation of the word table from the corpus.
/// </summary>
public sealed class WordsBuilderTests : IDisposable
{
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
        _dir.Write("corpus/lexicon/morphology.tsv", "Plural\tenabled\tevidence\nPastTense\tdisabled\tevidence\n");

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
