using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     A temporary directory that is deleted when the instance is disposed.
/// </summary>
internal sealed class TestDirectory : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TestDirectory"/> class.
    /// </summary>
    public TestDirectory()
    {
        Root = Path.Join(Path.GetTempPath(), "wordsense-builder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    ///     Gets the full path of the directory.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Writes a UTF-8 file without a byte-order mark, creating its directory.
    /// </summary>
    /// <param name="relative">The path below the root.</param>
    /// <param name="content">The content.</param>
    /// <returns>The full path of the file.</returns>
    public string Write(string relative, string content)
    {
        var path = Path.Join(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(Root, true);
    }
}
