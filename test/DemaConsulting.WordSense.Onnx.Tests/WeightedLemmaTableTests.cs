namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="WeightedLemmaTable"/>: the strict match of trained class ids to the word image.
/// </summary>
public class WeightedLemmaTableTests
{
    private static readonly WordImage Image = WordLexicon.Current;

    private static WeightedLemma Lemma(string lemma, params string[] classIds) => new(
        lemma,
        classIds,
        new LemmaWeights(LemmaModelKind.Binary, [1f, 0f], [0f], 0.1f, 2));

    private static WeightsData Data(params WeightedLemma[] lemmas) => new("test-model", Image.ClassHash.ToArray(), 2, [0f, 0f], lemmas);

    /// <summary>
    ///     Classes of the image are found by id, with their column.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_KnownClasses_AreFoundById()
    {
        var table = new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#verb")), Image);

        var weights = table.FindByClass("record#verb", out var column);

        Assert.NotNull(weights);
        Assert.Equal(1, column);
        Assert.Null(table.FindByClass("bass#fish", out var none));
        Assert.Equal(-1, none);
        Assert.NotNull(table.Find("record"));
        Assert.Null(table.Find("other"));
    }

    /// <summary>
    ///     Weights trained for another class inventory are rejected, and the message names both hashes.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_OtherClassHash_ThrowsNamingBothHashes()
    {
        var other = new byte[32];
        var data = Data(Lemma("record", "record#noun", "record#verb")) with { ClassHash = other };

        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(data, Image));

        Assert.Contains(Convert.ToHexString(other), ex.Message, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(Image.ClassHash), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A class id that the word image does not have is an error, never a dropped lemma.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_UnknownClass_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#no-such-class")), Image));

        Assert.Contains("record#no-such-class", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A class that two columns or two lemmas name is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateClass_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#noun")), Image));
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("a", "record#noun", "record#verb"), Lemma("b", "record#noun", "bass#fish")), Image));
    }

    /// <summary>
    ///     A lemma that appears twice is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateLemma_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#verb"), Lemma("record", "bass#fish", "bass#music")), Image));
    }

    /// <summary>
    ///     A lemma whose weights score a different number of senses than it names classes is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_ClassCountMismatch_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#verb", "bass#fish")), Image));
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
