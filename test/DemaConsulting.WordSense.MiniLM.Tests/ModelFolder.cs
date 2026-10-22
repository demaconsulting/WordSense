using DemaConsulting.WordSense.Onnx.Tests;

namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Locates the committed MiniLM model files and skips tests that need them when they are unavailable
///     (for example a checkout without Git LFS content). Every pinned file must be present, real and intact.
/// </summary>
internal static class ModelFolder
{
    /// <summary>
    ///     Gets the repository's model directory or skips the calling test.
    /// </summary>
    /// <returns>The directory containing the real model files.</returns>
    public static string Require()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Join(dir.FullName, "model", "all-MiniLM-L6-v2-int8");
            if (File.Exists(Path.Join(candidate, "SHA256SUMS")))
            {
                using var model = new MiniLMWordSenseModel(candidate);
                return ModelFileCheck.Require(model.Encoder.Spec, candidate);
            }
        }

        ModelFileCheck.Unavailable("The model directory was not found.");
        return string.Empty;
    }
}

