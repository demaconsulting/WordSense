namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="WeightedLemmaTable"/>: the checks of the weights against the word image, built over the
///     real senses of the shipped catalog.
/// </summary>
public class WeightedLemmaTableTests
{
    private const string Resource = "table.weights.bin";

    private static WeightsData Data(params WeightedLemma[] lemmas) => Fixtures.Data(Fixtures.Dimension, lemmas);

    private static WeightedLemma Subset(string lemma, int skip, int take)
    {
        var ids = Fixtures.SenseIds(lemma).Skip(skip).Take(take).ToArray();
        return new WeightedLemma(lemma, ids, Fixtures.Identity(ids.Length, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));
    }

    private static FormChooser LeftChooser(string[]? labels = null)
    {
        labels ??= ["leave", "left"];
        return Fixtures.Chooser("left", labels, 0.5f);
    }

    /// <summary>
    ///     Senses are found by id with their column, lemmas by name, and the group of a sense comes from the image.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_KnownSenses_AreFoundById()
    {
        // Arrange: one lemma over all its real senses
        var ids = Fixtures.SenseIds("just");
        var data = Data(Fixtures.Lemma("just"));

        // Act
        var table = new WeightedLemmaTable(data);

        // Assert: every sense has its column, the senses carry the catalog entry and the group, unknown lemmas are not found
        var just = table.Find("just");
        Assert.NotNull(just);
        Assert.Equal(ids.Length, just.Senses.Length);
        for (var column = 0; column < ids.Length; column++)
        {
            Assert.Equal(column, just.ColumnOf(ids[column]));
            Assert.Equal(ids[column], just.Senses[column].SenseId);
        }

        Assert.Equal(-1, just.ColumnOf("no#such-sense"));
        Assert.Contains(just.Senses, s => s.GroupId is not null);
        Assert.Null(table.Find("other"));
        Assert.Same(data, table.Data);
        Assert.Equal(0, table.ChooserCount);
    }

    /// <summary>
    ///     A table of a subset of the senses of a lemma is accepted: the image may hold more senses than were trained.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_SubsetOfTheSenses_IsAccepted()
    {
        // Arrange: the first three senses of the lemma only
        var data = Data(Subset("just", 0, 3));

        // Act
        var table = new WeightedLemmaTable(data);

        // Assert
        var just = table.Find("just");
        Assert.NotNull(just);
        Assert.Equal(3, just.Senses.Length);
        Assert.Equal(-1, just.ColumnOf(Fixtures.SenseIds("just")[5]));
    }

    /// <summary>
    ///     The table of no trained lemma finds nothing.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_Default_HasNoLemmas()
    {
        // Arrange / Act
        var table = new WeightedLemmaTable();

        // Assert
        Assert.Empty(table.Lemmas);
        Assert.Null(table.Find("just"));
        Assert.Null(table.FindChooser("left"));
    }

    /// <summary>
    ///     Weights trained for another inventory are refused with an error that names both hashes.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_OtherInventoryHash_ThrowsNamingBothHashes()
    {
        // Arrange: a hash that is not the one of the image
        var other = Enumerable.Repeat((byte)0xEE, 32).ToArray();
        var data = Data(Fixtures.Lemma("just")) with { InventoryHash = other };

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(data));

        // Assert
        Assert.Contains(Convert.ToHexString(other).ToLowerInvariant()[..16], ex.Message, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(Fixtures.InventoryHash()).ToLowerInvariant()[..16], ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense id that the word image does not have is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_UnknownSense_Throws()
    {
        // Arrange: a real sense and an id that no catalog has
        var real = Fixtures.SenseIds("just")[0];
        var lemma = new WeightedLemma("just", [real, "just#no-such-sense"], Fixtures.Identity(2, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(lemma)));

        // Assert
        Assert.Contains("just#no-such-sense", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense of another lemma is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_SenseOfAnotherLemma_Throws()
    {
        // Arrange: the lemma "like" names a sense of "just"
        var ids = new[] { Fixtures.SenseIds("like")[0], Fixtures.SenseIds("just")[0] };
        var lemma = new WeightedLemma("like", ids, Fixtures.Identity(2, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(lemma)));

        // Assert
        Assert.Contains("of the lemma 'just'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense that two columns or two lemmas name is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateSense_Throws()
    {
        // Arrange: the same sense twice in one lemma, and one sense in two entries of a lemma
        var id = Fixtures.SenseIds("just")[0];
        var twice = new WeightedLemma("just", [id, id], Fixtures.Identity(2, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(twice)));
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(Subset("just", 0, 3), Subset("just", 2, 3))));
    }

    /// <summary>
    ///     A lemma that appears twice is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateLemma_Throws()
    {
        // Arrange: two entries of "just" over different senses, so that no sense is repeated
        var data = Data(Subset("just", 0, 2), Subset("just", 2, 2));

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(data));

        // Assert
        Assert.Contains("lemma 'just' twice", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A lemma whose weights score a different number of senses than it names is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_SenseCountMismatch_Throws()
    {
        // Arrange: two weight rows but three sense ids, and three weight rows but two sense ids
        var ids = Fixtures.SenseIds("just");
        var tooManyIds = new WeightedLemma("just", ids[..3], Fixtures.Identity(2, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));
        var tooFewIds = new WeightedLemma("just", ids[..2], Fixtures.Identity(3, Fixtures.Dimension, Fixtures.Scale, 1f, LemmaWeights.Never));

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(tooManyIds)));
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(tooFewIds)));
    }

    /// <summary>
    ///     A lemma whose weights have another dimension than the blob is an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_LemmaOfAnotherDimension_Throws()
    {
        // Arrange: the identity rows of "just" have the dimension of the senses (smaller than the dimension of the blob)
        var ids = Fixtures.SenseIds("just");
        var narrow = new WeightedLemma("just", ids, Fixtures.Identity(ids.Length, ids.Length, Fixtures.Scale, 1f, LemmaWeights.Never));

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(narrow)));
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
    ///     The chooser of a form is found by its form once every analysis of the form has a label and every label
    ///     stands for an analysis.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_ValidChooser_IsFoundByForm()
    {
        // Arrange: "left" is the past participle of "leave" and an adjective, adverb and noun of "left"
        var data = Data(Fixtures.Lemma("leave", choosers: [LeftChooser()]), Fixtures.Lemma("left"));

        // Act
        var table = new WeightedLemmaTable(data);

        // Assert
        Assert.Equal(1, table.ChooserCount);
        Assert.Equal(["leave", "left"], table.FindChooser("left")!.Labels);
        Assert.Null(table.FindChooser("leave"));
    }

    /// <summary>
    ///     A chooser whose labels do not cover the analyses of its form is an error: an analysis without a label, a
    ///     label that stands for no analysis, a form the image does not have, and rows that differ from the labels.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_ChooserLabelsDisagreeWithTheForm_Throws()
    {
        // Arrange: the analyses of "left" belong to the lemmas "leave" and "left"
        var missing = Fixtures.Lemma("leave", choosers: [LeftChooser(["leave", "other"])]);
        var unused = Fixtures.Lemma("leave", choosers: [LeftChooser(["leave", "left", "other"])]);
        var unknownForm = Fixtures.Lemma("leave", choosers: [Fixtures.Chooser("zzqxjw", ["a", "b"], 0.5f)]);
        var rowMismatch = Fixtures.Lemma("leave", choosers: [new FormChooser("left", ["leave", "left", "other"], Fixtures.Identity(2, Fixtures.Dimension, Fixtures.Scale, 0.5f, LemmaWeights.Never))]);

        // Act
        var noLabel = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(missing)));
        var noAnalysis = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(unused)));

        // Assert: the messages name the analysis or the label that disagrees
        Assert.Contains("no label for the analysis left", noLabel.Message, StringComparison.Ordinal);
        Assert.Contains("'other'", noAnalysis.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(unknownForm)));
        Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(Data(rowMismatch)));
    }

    /// <summary>
    ///     Two lemmas that store a chooser for one form are an error.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_Constructor_DuplicateChooserForm_Throws()
    {
        // Arrange
        var data = Data(Fixtures.Lemma("leave", choosers: [LeftChooser()]), Fixtures.Lemma("left", choosers: [LeftChooser()]));

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => new WeightedLemmaTable(data));

        // Assert
        Assert.Contains("'left' twice", ex.Message, StringComparison.Ordinal);
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
        Assert.Throws<ArgumentNullException>(() => WeightedLemmaTable.LoadEmbedded(null!, "x", "x", 2));
    }

    /// <summary>
    ///     A valid embedded blob loads into a table that is checked against the word image.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_ValidResource_LoadsTheTable()
    {
        // Arrange
        var assembly = new FixtureAssembly(Resource, BlobWriter.Write(Data(Fixtures.Lemma("just"))));

        // Act
        var table = WeightedLemmaTable.LoadEmbedded(assembly, Resource, "fixture-model", Fixtures.Dimension);

        // Assert
        Assert.NotNull(table.Find("just"));
        Assert.Equal("fixture-model", table.Data.ModelId);
    }

    /// <summary>
    ///     Weights that were trained for another model, or for another hidden size, are rejected.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_WrongModelId_Throws()
    {
        // Arrange: embedded weights of the model "fixture-model" with dimension 40
        var assembly = new FixtureAssembly(Resource, BlobWriter.Write(Data(Fixtures.Lemma("just"))));

        // Act
        var model = Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "another-model", Fixtures.Dimension));
        var dimension = Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "fixture-model", Fixtures.Dimension + 1));

        // Assert: the errors name what is wrong
        Assert.Contains("'fixture-model', not 'another-model'", model.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 40", dimension.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An embedded blob that was trained for another inventory is rejected when it loads.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_OtherInventoryHash_Throws()
    {
        // Arrange
        var data = Data(Fixtures.Lemma("just")) with { InventoryHash = new byte[32] };
        var assembly = new FixtureAssembly(Resource, BlobWriter.Write(data));

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "fixture-model", Fixtures.Dimension));
    }

    /// <summary>
    ///     A resource that is not a weights blob is rejected.
    /// </summary>
    [Fact]
    public void WeightedLemmaTable_LoadEmbedded_CorruptResource_Throws()
    {
        // Arrange
        var assembly = new FixtureAssembly(Resource, [1, 2, 3, 4]);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WeightedLemmaTable.LoadEmbedded(assembly, Resource, "fixture-model", Fixtures.Dimension));
    }
}
