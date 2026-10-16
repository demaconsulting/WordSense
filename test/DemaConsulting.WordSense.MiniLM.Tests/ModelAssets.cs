using System.Text;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Locates the committed model files and skips tests that need them when they are unavailable
///     (for example a checkout without Git LFS content).
/// </summary>
internal static class ModelAssets
{
    private static readonly string[] Names = ["model_int8.onnx", "vocab.txt"];

    /// <summary>
    ///     Finds the repository's <c>model</c> directory by walking up from the test output directory.
    /// </summary>
    /// <returns>The directory, or null when not found.</returns>
    public static string? FindModelDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "model");
            if (File.Exists(Path.Combine(candidate, "SHA256SUMS")))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the verified model directory or skips the calling test.
    /// </summary>
    /// <returns>The directory containing real (non-pointer) model files.</returns>
    public static string RequireModelDirectory()
    {
        var directory = FindModelDirectory();
        if (directory is null)
        {
            Assert.Skip("The model directory was not found.");
        }

        foreach (var name in Names)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) || IsLfsPointer(path))
            {
                Assert.Skip($"The model file '{name}' is missing or is a Git LFS pointer; run 'git lfs pull'.");
            }
        }

        return directory;
    }

    /// <summary>
    ///     Finds the repository root (the parent of the model directory) or skips the calling test.
    /// </summary>
    /// <returns>The repository root.</returns>
    public static string RequireRepositoryRoot()
    {
        var model = FindModelDirectory();
        if (model is null)
        {
            Assert.Skip("The repository root was not found.");
        }

        return Path.GetDirectoryName(model)!;
    }

    /// <summary>
    ///     Determines whether a file is a Git LFS pointer rather than real content.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>True for a pointer file.</returns>
    private static bool IsLfsPointer(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[64];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.ASCII.GetString(buffer, 0, read).StartsWith("version https://git-lfs", StringComparison.Ordinal);
    }
}
