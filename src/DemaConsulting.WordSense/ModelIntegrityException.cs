namespace DemaConsulting.WordSense;

/// <summary>
///     The exception thrown when the files or embedded resources of a model are damaged, do not match their pinned
///     SHA-256 digests, or do not belong to this version of the library.
/// </summary>
/// <remarks>
///     <para>
///     Causes: a file of a model directory that you supplied does not match its pinned SHA-256 digest; an embedded
///     resource (the word image, the statistics table of the Basic model, the lemma weights of an encoder model) is
///     missing, corrupt or not for the word image of the package; the ONNX file or the vocabulary cannot be used (for
///     example a vocabulary without a required special token); or the weights do not fit the word image. The
///     <see cref="Exception.InnerException"/> is the original failure, usually an <see cref="InvalidDataException"/>. Retrying does not help: replace the files (download them again), or
///     reinstall the NuGet packages, making sure that all WordSense packages have the same version.
///     </para>
///     <para>
///     A downloaded file that fails verification is not reported with this type: the download then continues with the
///     next source and, when no source delivers a verified file, the failure is a
///     <see cref="ModelUnavailableException"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static async Task ClassifyAsync(IWordSenseModel model)
///     {
///         try
///         {
///             ClassifyResult[] results = await model.ClassifyAsync(["to", "record", "the", "record"]);
///             Console.WriteLine(results[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///         }
///         catch (ModelIntegrityException ex)
///         {
///             Console.WriteLine($"Reinstall the packages or the model files: {ex.Message}");
///         }
///     }
///     </code>
/// </example>
public sealed class ModelIntegrityException : WordSenseException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelIntegrityException"/> class.
    /// </summary>
    public ModelIntegrityException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelIntegrityException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public ModelIntegrityException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelIntegrityException"/> class with a message and the original exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The original exception.</param>
    public ModelIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
