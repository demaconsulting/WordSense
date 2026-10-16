namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     Entry point of the MiniLM package: creates a <see cref="WordSenseClassifier"/> that chooses between the senses of
///     a word with the all-MiniLM-L6-v2 (int8) encoder.
/// </summary>
/// <remarks>
///     The weights table trained for this model is embedded in the assembly under a resource named for the model
///     identifier and the SHA-256 of the model file. The encoder model itself (the 22.97 MB <c>model_int8.onnx</c> and
///     a 232 KB <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached
///     per user, or it is read from a directory you supply (<see cref="MiniLMWordSenseOptions.ModelDirectory"/>), in
///     which case no network access happens at all.
/// </remarks>
public static class MiniLMWordSense
{
    /// <summary>
    ///     Gets the identifier of the model, <c>all-MiniLM-L6-v2-int8</c>: the folder name of the model beneath
    ///     <c>model/</c> in the repository, beneath a mirror base and beneath the store root.
    /// </summary>
    public static string ModelId => WordSenseModelCatalog.ModelId;

    /// <summary>
    ///     Loads the weights table trained for the MiniLM model from the embedded resource.
    /// </summary>
    /// <returns>The weights table.</returns>
    /// <exception cref="WordSenseTableFormatException">Thrown when the embedded resource is missing or malformed.</exception>
    public static WordSenseWeightsTable LoadWeights()
    {
        var name = $"DemaConsulting.WordSense.MiniLM.weights.{WordSenseModelCatalog.ModelId}.{WordSenseModelCatalog.OnnxSha256}.bin";
        using var stream = typeof(MiniLMWordSense).Assembly.GetManifestResourceStream(name)
            ?? throw new WordSenseTableFormatException($"The embedded weights table '{name}' was not found.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return WordSenseWeightsTable.Read(data);
    }

    /// <summary>
    ///     Creates a classifier from the embedded lexicon and the embedded MiniLM weights, obtaining and verifying the
    ///     model first.
    /// </summary>
    /// <param name="options">The MiniLM options, or null for defaults.</param>
    /// <param name="classifierOptions">The classifier options, or null for defaults.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <remarks>
    ///     When <see cref="MiniLMWordSenseOptions.ModelDirectory"/> is set the files in it are verified and no network
    ///     access happens; otherwise <see cref="WordSenseModelDownloader.EnsureModelAsync"/> obtains them (see that method
    ///     for its additional exceptions; <see cref="MiniLMWordSenseOptions.Downloader"/> describes the download
    ///     settings). The options are validated first: an invalid <c>MaxSequenceLength</c>, <c>IntraOpThreads</c> or
    ///     <c>AbstainMarginScale</c> is reported before anything is downloaded or verified. The first call without a
    ///     <c>ModelDirectory</c> downloads about 23 MB into the per-user store; later calls reuse the verified files
    ///     with no network access.
    /// </remarks>
    /// <returns>A classifier that owns its model and must be disposed.</returns>
    /// <exception cref="WordSenseModelDownloadException">Thrown when the model could not be downloaded.</exception>
    /// <exception cref="WordSenseModelMismatchException">Thrown when the embedded tables do not match the model.</exception>
    /// <exception cref="WordSenseTableFormatException">Thrown when an embedded table is missing or malformed.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a file of the model directory is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when a model file's SHA-256 does not match the pinned value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <c>MaxSequenceLength</c> is outside 8 to 512, <c>IntraOpThreads</c> is negative,
    ///     <c>AbstainMarginScale</c> is negative or NaN, or the downloader's <c>RequestTimeout</c> is invalid.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <c>WORDSENSE_MIRROR_*</c> environment configuration is invalid, or a directory or store path
    ///     is not a valid path.
    /// </exception>
    /// <exception cref="IOException">Thrown when the store directory cannot be created or a model file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the store directory or a model file cannot be accessed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     // Default: download on first use, verify, and cache
    ///     using var online = await MiniLMWordSense.CreateClassifierAsync();
    ///
    ///     // Offline: use a directory that already holds model_int8.onnx and vocab.txt
    ///     var options = new MiniLMWordSenseOptions { ModelDirectory = "/models/all-MiniLM-L6-v2-int8" };
    ///     using var offline = await MiniLMWordSense.CreateClassifierAsync(options);
    ///     </code>
    /// </example>
    public static async Task<WordSenseClassifier> CreateClassifierAsync(
        MiniLMWordSenseOptions? options = null,
        WordSenseClassifierOptions? classifierOptions = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MiniLMWordSenseOptions();

        // Reject invalid option values before any download or file verification happens
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxSequenceLength, 512);
        ArgumentOutOfRangeException.ThrowIfNegative(options.IntraOpThreads);
        ValidateClassifierOptions(classifierOptions);

        var location = options.ModelDirectory is not null
            ? WordSenseModelLocation.FromDirectory(options.ModelDirectory)
            : await WordSenseModelDownloader.EnsureModelAsync(options.Downloader, cancellationToken).ConfigureAwait(false);

        var table = WordSenseTable.LoadEmbedded();
        var weights = LoadWeights();
        var model = new MiniLMWordSenseModel(location.ModelPath, location.VocabularyPath, options.MaxSequenceLength, options.IntraOpThreads);
        try
        {
            return new WordSenseClassifier(table, model, weights, classifierOptions, ownsModel: true);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Validates the classifier options up front, so a bad value is reported before any download starts.
    /// </summary>
    /// <param name="options">The classifier options, or null.</param>
    private static void ValidateClassifierOptions(WordSenseClassifierOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (double.IsNaN(options.AbstainMarginScale))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AbstainMarginScale must not be NaN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.AbstainMarginScale);
    }
}
