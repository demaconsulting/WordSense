using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     End-to-end tests of the data commands: import a slice through the command line, validate it, and validate the
///     committed <c>data</c> folder of the repository.
/// </summary>
public sealed class DataEndToEndTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await Program.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    private string EmptyDataFolder()
    {
        var data = Path.Combine(_dir.Root, "data");
        Directory.CreateDirectory(data);
        File.Copy(Path.Combine(DataFixture.RepositoryRoot(), "data", "schema.yaml"), Path.Combine(data, "schema.yaml"));
        return data;
    }

    /// <summary>
    ///     The two commands import the slice and validate the result.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_ImportThenValidate_Succeeds()
    {
        // Arrange
        var data = EmptyDataFolder();

        // Act
        var import = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        var validate = await RunAsync("validate-data", "--data", data);

        // Assert
        Assert.Equal(0, import.Code);
        Assert.Contains("entry: 14 rows", import.Output, StringComparison.Ordinal);
        Assert.Equal(0, validate.Code);
        Assert.Contains("is valid", validate.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Without the pin option the pinned release hash is enforced.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_PinnedHash_RejectsOtherFile()
    {
        // Arrange
        var data = EmptyDataFolder();

        // Act
        var import = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data);

        // Assert
        Assert.Equal(1, import.Code);
        Assert.Contains("SHA-256", import.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The import refuses to overwrite existing tables unless asked.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_ExistingTables_NeedsOverwrite()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");

        // Act
        var refused = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        var allowed = await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none", "--overwrite", "true");

        // Assert
        Assert.Equal(1, refused.Code);
        Assert.Equal(0, allowed.Code);
    }

    /// <summary>
    ///     The import needs an input file.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Import_NoInput_ReportsUsage()
    {
        // Arrange / Act
        var result = await RunAsync("import-oewn");

        // Assert
        Assert.Equal(1, result.Code);
        Assert.Contains("--input", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Invalid data makes the validator exit with 1 and list the violation.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Validate_InvalidData_ListsViolations()
    {
        // Arrange
        var data = EmptyDataFolder();
        await RunAsync("import-oewn", "--input", DataFixture.SlicePath, "--output", data, "--sha256", "none");
        File.Delete(Path.Combine(data, "source.tsv"));

        // Act
        var result = await RunAsync("validate-data", "--data", data);

        // Assert
        Assert.Equal(1, result.Code);
        Assert.Contains("[file-set]", result.Error, StringComparison.Ordinal);
        Assert.Contains("violation(s)", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The usage lists the data commands.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_DataCommands_Usage_ListsCommands()
    {
        // Arrange / Act
        var result = await RunAsync();

        // Assert
        Assert.Contains("import-oewn", result.Error, StringComparison.Ordinal);
        Assert.Contains("validate-data", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The committed <c>data</c> folder of the repository passes every validator rule.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_IsValid()
    {
        // Arrange
        var data = Path.Combine(DataFixture.RepositoryRoot(), "data");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.True(report.RowCounts["entry"] > 100_000);
    }

    /// <summary>
    ///     The committed coarse mapping is present and partitions the senses of the mapped lemmas.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_CommittedData_HasCoarseMapping()
    {
        // Arrange
        var data = Path.Combine(DataFixture.RepositoryRoot(), "data");

        // Act
        var report = DataValidator.Validate(data);

        // Assert
        Assert.True(report.IsValid, string.Join("\n", report.Violations));
        Assert.True(report.RowCounts["coarse_class"] >= 1521);
        Assert.True(report.RowCounts["class_sense"] > 6000);
        Assert.Equal(2, report.RowCounts["source"]);
    }

    /// <summary>
    ///     The README files and the provenance record credit Open English WordNet and its license.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataEndToEnd_Attribution_NamesLicenseAndSource()
    {
        // Arrange
        var root = DataFixture.RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "README.md"),
            Path.Combine(root, "data", "README.md"),
            Path.Combine(root, "data", "PROVENANCE.md")
        };

        // Act / Assert
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.Contains("Open English WordNet", text, StringComparison.Ordinal);
            Assert.Contains("CC BY 4.0", text, StringComparison.Ordinal);
        }
    }
}
