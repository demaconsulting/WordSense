using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="WeightedLemmaTable"/>: the all-or-nothing match of trained senses to the word lexicon.
/// </summary>
public class WeightedLemmaTableTests
{
    private static readonly ImmutableArray<WordDetails> Classes = WordLexicon.ClassesOf("record");
    private static readonly WordDetails Noun = Classes.First(c => c.PartOfSpeech == PartOfSpeech.Noun);
    private static readonly WordDetails Verb = Classes.First(c => c.PartOfSpeech == PartOfSpeech.Verb);

    private static Func<string, ImmutableArray<WordDetails>> Words(params WordDetails[] classes) =>
        lemma => lemma == "record" ? [.. classes] : [];

    private static WeightedLemma Lemma(string lemma, params WordDetails[] senses) => new(
        lemma,
        [.. senses.Select(s => new WeightedWord(s.Lemma, s.PartOfSpeech, WordLexicon.MeaningOf(s)))],
        new LemmaWeights(LemmaModelKind.Binary, [1f, 0f], [0f], 0.1f, 2));

    private static WeightsData Data(params WeightedLemma[] lemmas) => new("test-model", 2, [0f, 0f], lemmas);

    /// <summary>
    ///     A lemma whose senses equal the lexicon's classes is kept.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_MatchingSenses_KeepsLemma()
    {
        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), Words(Noun, Verb));

        Assert.Equal(0, table.DroppedLemmas);
        Assert.NotNull(table.Find("record"));
        Assert.Null(table.Find("other"));
    }

    /// <summary>
    ///     A lemma that is missing from the lexicon is dropped.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_UnknownLemma_DropsLemma()
    {
        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), _ => []);

        Assert.Equal(["record"], table.DroppedLemmaNames);
        Assert.Null(table.Find("record"));
    }

    /// <summary>
    ///     A lemma whose senses differ in any part is dropped as a whole.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_MismatchedSenses_DropsWholeLemma()
    {
        var changed = Lemma("record", Noun, Verb) with { Senses = [new WeightedWord("record", PartOfSpeech.Noun, WordLexicon.MeaningOf(Noun)), new WeightedWord("record", PartOfSpeech.Verb, "changed")] };

        var table = new WeightedLemmaTable(Data(changed), Words(Noun, Verb));

        Assert.Equal(1, table.DroppedLemmas);
        Assert.Empty(table.Lemmas);
    }

    /// <summary>
    ///     A lemma whose trained senses are the first classes of a longer list is kept.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_TrainedSensesArePrefix_KeepsLemma()
    {
        var extra = new WordDetails(null, "record", PartOfSpeech.Adjective, string.Empty);

        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), Words(Noun, Verb, extra));

        Assert.Equal(0, table.DroppedLemmas);
        Assert.NotNull(table.Find("record"));
    }

    /// <summary>
    ///     A lemma with a different number of senses is dropped.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DifferentSenseCount_DropsLemma()
    {
        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), Words(Noun));

        Assert.Equal(1, table.DroppedLemmas);
    }

    /// <summary>
    ///     Loading from an assembly without the resource fails with a clear error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_MissingResource_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            WeightedLemmaTable.LoadEmbedded(typeof(WeightedLemmaTableTests).Assembly, "no.such.resource", "x", 2));
    }

    /// <summary>
    ///     Weights that were trained for another model, or for another hidden size, are rejected.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_WrongModelId_Throws()
    {
        var assembly = typeof(MiniLM.MiniLMWordSenseModel).Assembly;
        const string Resource = "DemaConsulting.WordSense.MiniLM.weights.bin";

        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "another-model", 384));
        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "all-MiniLM-L6-v2-int8", 768));
    }
}
