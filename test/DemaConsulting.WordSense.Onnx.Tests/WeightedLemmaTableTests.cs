namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="WeightedLemmaTable"/>: the all-or-nothing match of trained senses to the word lexicon.
/// </summary>
public class WeightedLemmaTableTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "noun", "REK-urd");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "verb", "rih-KORD");

    private static WeightedLemma Lemma(string lemma, params WordDetails[] senses) => new(
        lemma,
        [.. senses.Select(s => new WeightedWord(s.Lemma, s.PartOfSpeech, s.Meaning))],
        new LemmaWeights(LemmaModelKind.Binary, [1f, 0f], [0f], 0.1f, 2));

    private static WeightsData Data(params WeightedLemma[] lemmas) => new("test-model", 2, [0f, 0f], lemmas);

    /// <summary>
    ///     A lemma whose senses equal the lexicon's senses is kept.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_MatchingSenses_KeepsLemma()
    {
        var words = new Dictionary<string, WordDetails[]> { ["record"] = [Noun, Verb] };

        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), words);

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
        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), new Dictionary<string, WordDetails[]>());

        Assert.Equal(["record"], table.DroppedLemmaNames);
        Assert.Null(table.Find("record"));
    }

    /// <summary>
    ///     A lemma whose sense differs in any part is dropped as a whole.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_MismatchedSenses_DropsWholeLemma()
    {
        var changed = Verb with { Meaning = "changed" };
        var words = new Dictionary<string, WordDetails[]> { ["record"] = [Noun, changed] };

        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), words);

        Assert.Equal(1, table.DroppedLemmas);
        Assert.Empty(table.Lemmas);
    }

    /// <summary>
    ///     A lemma with a different number of senses is dropped.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DifferentSenseCount_DropsLemma()
    {
        var words = new Dictionary<string, WordDetails[]> { ["record"] = [Noun] };

        var table = new WeightedLemmaTable(Data(Lemma("record", Noun, Verb)), words);

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
