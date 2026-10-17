namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Locates the committed BertBaseCased model files and skips tests that need them when they are unavailable
///     (for example a checkout without Git LFS content).
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
            var candidate = Path.Combine(dir.FullName, "model", "bert-base-cased-int8");
            if (File.Exists(Path.Combine(candidate, "SHA256SUMS")))
            {
                // A Git LFS pointer is a few hundred bytes; the real model is megabytes
                if (new FileInfo(Path.Combine(candidate, "model_int8.onnx")) is { Exists: true, Length: > 1_000_000 })
                {
                    return candidate;
                }

                Assert.Skip("The model file is missing or is a Git LFS pointer; run 'git lfs pull'.");
            }
        }

        Assert.Skip("The model directory was not found.");
        return string.Empty;
    }
}
