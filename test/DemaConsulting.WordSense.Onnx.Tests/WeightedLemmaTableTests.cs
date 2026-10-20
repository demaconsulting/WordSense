namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="WeightedLemmaTable"/>: the structural checks of the trained lemma groups and class ids.
/// </summary>
public class WeightedLemmaTableTests
{
    private static WeightedLemma Lemma(string lemma, params string[] classIds) => new(
        lemma,
        classIds,
        new LemmaWeights(LemmaModelKind.Binary, [1f, 0f], [0f], 0.1f, 2));

    private static WeightsData Data(params WeightedLemma[] lemmas) => new("test-model", new byte[32], 2, [0f, 0f], lemmas);

    /// <summary>
    ///     Classes are found by id, with their column, and lemmas by name.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_KnownClasses_AreFoundById()
    {
        // Arrange: one lemma with two classes
        var data = Data(Lemma("record", "record#noun", "record#verb"));

        // Act: build the table and look classes and lemmas up
        var table = new WeightedLemmaTable(data);
        var weights = table.FindByClass("record#verb", out var column);
        var none = table.FindByClass("bass#fish", out var noneColumn);

        // Assert: the trained class is found with its column, the others are not
        Assert.NotNull(weights);
        Assert.Equal(1, column);
        Assert.Null(none);
        Assert.Equal(-1, noneColumn);
        Assert.NotNull(table.Find("record"));
        Assert.Null(table.Find("other"));
    }

    /// <summary>
    ///     The table does not check the class ids against the word lexicon: it is built from the weights alone, whatever
    ///     the hash field holds.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_ClassesOutsideTheLexicon_AreAccepted()
    {
        // Arrange: a class id that no lexicon has and a hash that is not the lexicon's
        var data = Data(Lemma("record", "record#noun", "no-such-class")) with { ClassHash = [.. Enumerable.Repeat((byte)0xEE, 32)] };

        // Act
        var table = new WeightedLemmaTable(data);

        // Assert: both classes are found
        Assert.NotNull(table.FindByClass("no-such-class", out _));
        Assert.Same(data, table.Data);
    }

    /// <summary>
    ///     A class that two columns or two lemmas name is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateClass_Throws()
    {
        // Arrange / Act / Assert: the same class twice in one lemma, and in two lemmas
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#noun"))));
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("a", "record#noun", "record#verb"), Lemma("b", "record#noun", "bass#fish"))));
    }

    /// <summary>
    ///     A lemma that appears twice is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateLemma_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#verb"), Lemma("record", "bass#fish", "bass#music"))));
    }

    /// <summary>
    ///     A lemma whose weights score a different number of senses than it names classes is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_ClassCountMismatch_Throws()
    {
        // Arrange / Act / Assert: two weight rows but three class ids
        Assert.Throws<InvalidDataException>(() =>
            new WeightedLemmaTable(Data(Lemma("record", "record#noun", "record#verb", "bass#fish"))));
    }

    /// <summary>
    ///     A null weights object is rejected.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_NullData_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => new WeightedLemmaTable(null!));
    }

    /// <summary>
    ///     Loading from an assembly without the resource fails with a clear error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_MissingResource_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() =>
            WeightedLemmaTable.LoadEmbedded(typeof(WeightedLemmaTableTests).Assembly, "no.such.resource", "x", 2));
    }

    /// <summary>
    ///     Weights that were trained for another model, or for another hidden size, are rejected.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_WrongModelId_Throws()
    {
        // Arrange: the embedded MiniLM weights
        var assembly = typeof(MiniLM.MiniLMWordSenseModel).Assembly;
        const string Resource = "DemaConsulting.WordSense.MiniLM.weights.bin";

        // Act / Assert: another model id, and another dimension
        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "another-model", 384));
        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "all-MiniLM-L6-v2-int8", 768));
    }
}
