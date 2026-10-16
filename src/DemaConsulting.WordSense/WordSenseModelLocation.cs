namespace DemaConsulting.WordSense;

/// <summary>
///     The location of a verified set of model files on disk.
/// </summary>
/// <param name="Directory">The directory that contains the model files.</param>
/// <param name="ModelPath">The full path of the ONNX encoder file.</param>
/// <param name="VocabularyPath">The full path of the vocabulary file.</param>
public sealed record WordSenseModelLocation(string Directory, string ModelPath, string VocabularyPath)
{
    /// <summary>
    ///     Verifies the model files in a directory and returns their location.
    /// </summary>
    /// <remarks>
    ///     Every file is checked against its pinned SHA-256, so a stale, truncated or tampered file is
    ///     rejected instead of silently producing wrong word senses.
    /// </remarks>
    /// <param name="directory">The directory that holds <c>model_int8.onnx</c> and <c>vocab.txt</c>.</param>
    /// <returns>The verified location.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a model file is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when a model file's SHA-256 does not match.</exception>
    /// <example>
    ///     <code>
    ///     // The directory holds model_int8.onnx and vocab.txt, for example copied from another machine
    ///     WordSenseModelLocation location = WordSenseModelLocation.FromDirectory("/models/all-MiniLM-L6-v2");
    ///     using var classifier = await WordSenseClassifier.CreateAsync(
    ///         new WordSenseClassifierOptions { ModelDirectory = location.Directory });
    ///     </code>
    /// </example>
    public static WordSenseModelLocation FromDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        // Check each pinned file in turn
        var full = Path.GetFullPath(directory);
        foreach (var file in WordSenseModelCatalog.Files)
        {
            var path = Path.Combine(full, file.Name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"The model file '{file.Name}' was not found in '{full}'.", path);
            }

            if (!string.Equals(WordSenseModelCatalog.HashFile(path), file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The model file '{file.Name}' in '{full}' does not match its pinned SHA-256.");
            }
        }

        return new WordSenseModelLocation(
            full,
            Path.Combine(full, WordSenseModelCatalog.OnnxFileName),
            Path.Combine(full, WordSenseModelCatalog.VocabularyFileName));
    }

    /// <summary>
    ///     Gets the default directory in which downloaded models are stored for the current user.
    /// </summary>
    /// <returns>A folder beneath the user's local application data directory.</returns>
    public static string DefaultStoreRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            local = AppContext.BaseDirectory;
        }

        return Path.Combine(local, "DemaConsulting", "WordSense", "models");
    }
}
