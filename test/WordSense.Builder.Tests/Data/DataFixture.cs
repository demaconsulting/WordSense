using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Shared helpers of the data-layer tests: the OEWN slice fixture, the repository <c>data</c> folder and small
///     readers and editors of table files.
/// </summary>
internal static class DataFixture
{
    /// <summary>
    ///     Gets the path of the hand-written OEWN slice.
    /// </summary>
    public static string SlicePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "oewn-slice.xml");

    /// <summary>
    ///     Finds the repository root by walking up to the solution file.
    /// </summary>
    /// <returns>The repository root directory.</returns>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WordSense.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>
    ///     Imports the slice into a new data folder below a test directory, with the schema of the repository.
    /// </summary>
    /// <param name="dir">The test directory.</param>
    /// <param name="name">The name of the data folder below the test directory.</param>
    /// <param name="threshold">The split threshold.</param>
    /// <returns>The data folder.</returns>
    public static string ImportSlice(TestDirectory dir, string name = "data", int threshold = OewnImporter.DefaultSplitThreshold)
    {
        var data = Path.Combine(dir.Root, name);
        Directory.CreateDirectory(data);
        File.Copy(Path.Combine(RepositoryRoot(), "data", "schema.yaml"), Path.Combine(data, "schema.yaml"));
        OewnImporter.Import(SlicePath, data, Path.Combine(data, "schema.yaml"), null, threshold);
        return data;
    }

    /// <summary>
    ///     Reads all rows of a table (header skipped), in file-name order.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="table">The table name.</param>
    /// <returns>The rows as arrays of cells.</returns>
    public static List<string[]> Rows(string data, string table)
    {
        var single = Path.Combine(data, table + ".tsv");
        var files = File.Exists(single) ? [single] : Directory.GetFiles(Path.Combine(data, table), "*.tsv").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        return files.SelectMany(f => File.ReadAllLines(f, Encoding.UTF8).Skip(1)).Select(l => l.Split('\t')).ToList();
    }

    /// <summary>
    ///     Replaces text in a file of the data folder and writes it back as UTF-8 without a byte-order mark.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="relative">The path of the file below the data folder.</param>
    /// <param name="oldText">The text to replace; it must occur in the file.</param>
    /// <param name="newText">The replacement.</param>
    public static void Replace(string data, string relative, string oldText, string newText)
    {
        var path = Path.Combine(data, relative);
        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains(oldText, text);
        File.WriteAllText(path, text.Replace(oldText, newText, StringComparison.Ordinal), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Writes a UTF-8 file without a byte-order mark below the data folder, creating its directory.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="relative">The path below the data folder.</param>
    /// <param name="content">The content.</param>
    public static void Write(string data, string relative, string content)
    {
        var path = Path.Combine(data, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
