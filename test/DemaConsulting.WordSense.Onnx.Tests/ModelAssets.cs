using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Locates the committed model files and skips tests that need them when they are unavailable
///     (for example a checkout without Git LFS content).
/// </summary>
internal static class ModelAssets
{
    /// <summary>
    ///     Finds the repository's <c>model/all-MiniLM-L6-v2-int8</c> directory by walking up from the test output directory.
    /// </summary>
    /// <returns>The directory, or null when not found.</returns>
    public static string? FindModelDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Join(dir.FullName, "model", "all-MiniLM-L6-v2-int8");
            if (File.Exists(Path.Join(candidate, "SHA256SUMS")))
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
            ModelFileCheck.Unavailable("The model directory was not found.");
        }

        using var model = new MiniLMEncoderModel(directory, 128);
        return ModelFileCheck.Require(model.Spec, directory);
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
            ModelFileCheck.Unavailable("The repository root was not found.");
        }

        return Path.GetDirectoryName(Path.GetDirectoryName(model))!;
    }
}

