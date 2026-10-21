namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the model comparison table file: each command writes its own columns and keeps the others.
/// </summary>
public sealed class ModelTableTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     A command changes only its own cells: the other cells of the row and the rows of the other models stay, a cell that
    ///     was never measured reads <c>not measured</c>, and the models are listed from the simplest to the most accurate.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ModelTable_Update_ChangesOnlyTheCellsOfItsCommand()
    {
        // Arrange
        var path = Path.Join(_dir.Root, "table.md");
        ModelTable.Update(path, "bert-base-cased", new Dictionary<string, string> { [ModelTable.RealSenseColumn] = "20.00% @ 96.00%", [ModelTable.RamColumn] = "206 MiB" });
        ModelTable.Update(path, "basic", new Dictionary<string, string> { [ModelTable.RunningColumn] = "93.00% (ambiguous 80.00%)" });

        // Act
        ModelTable.Update(path, "bert-base-cased", new Dictionary<string, string> { [ModelTable.IndependentSenseColumn] = "10.00% @ 90.00% (top-1 50.00%)", [ModelTable.RealSenseColumn] = "21.00% @ 96.50%" });

        // Assert
        var rows = ModelTable.Read(path);
        Assert.Equal(["Basic", "BERT base cased"], rows.Keys);
        var bert = rows["BERT base cased"];
        Assert.Equal("21.00% @ 96.50%", bert[ModelTable.RealSenseColumn]);
        Assert.Equal("206 MiB", bert[ModelTable.RamColumn]);
        Assert.Equal("10.00% @ 90.00% (top-1 50.00%)", bert[ModelTable.IndependentSenseColumn]);
        Assert.Equal(ModelTable.NotMeasured, bert[ModelTable.RunningColumn]);
        Assert.Equal("93.00% (ambiguous 80.00%)", rows["Basic"][ModelTable.RunningColumn]);
        var text = File.ReadAllText(path);
        Assert.Contains(ModelTable.Begin, text, StringComparison.Ordinal);
        Assert.Contains("remeasured on a quiet machine", text.Replace("\n", " ", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(ModelTable.Columns, rows["Basic"].Keys);
    }

    /// <summary>
    ///     A column that the table does not have is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ModelTable_Update_UnknownColumn_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ModelTable.Update(Path.Join(_dir.Root, "t.md"), "basic", new Dictionary<string, string> { ["Speed"] = "fast" }));
    }
}
