using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Locates the fetched model files and skips tests that need them when they are unavailable
///     (for example a checkout where <c>fetch-models</c> has not run).
/// </summary>
internal static class ModelAssets
{
    /// <summary>
    ///     Finds the repository's <c>model/all-MiniLM-L6-v2/v1</c> directory by walking up from the test output directory.
    /// </summary>
    /// <returns>The directory, or null when the model folder of the repository was not found.</returns>
    public static string? FindModelDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            // The folder of the model holds the committed provenance; the fetched files are in its version folder
            if (File.Exists(Path.Join(dir.FullName, "model", "all-MiniLM-L6-v2", "SHA256SUMS")))
            {
                return Path.Join(dir.FullName, "model", "all-MiniLM-L6-v2", "v1");
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the verified model directory or skips the calling test.
    /// </summary>
    /// <returns>The directory containing the model files.</returns>
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

        return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(model)))!;
    }
}

