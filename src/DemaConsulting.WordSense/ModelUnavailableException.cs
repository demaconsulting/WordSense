namespace DemaConsulting.WordSense;

/// <summary>
///     The exception thrown when the files of a model cannot be obtained: a file is missing from a model directory that
///     was supplied, or no download source delivered a file that verified.
/// </summary>
/// <remarks>
///     <para>
///     The <see cref="Exception.InnerException"/> is the original failure: a <see cref="FileNotFoundException"/> or
///     <see cref="DirectoryNotFoundException"/> for a model directory that does not exist or lacks a file, or an
///     <see cref="System.Net.Http.HttpRequestException"/> that lists every source that was tried when a download failed
///     (including a source that served bytes with a wrong SHA-256 digest). The usual response is to check the network,
///     proxy and mirror settings, or to supply the files from a directory; the call can be repeated, because a failed
///     ensure is retried.
///     </para>
///     <para>
///     A file that is present but fails its pinned SHA-256 digest in a model directory that you supplied is a
///     <see cref="ModelIntegrityException"/> instead.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static async Task&lt;bool&gt; TryPrepareAsync(IDownloadableWordSenseModel model)
///     {
///         try
///         {
///             await model.EnsureModelAsync();
///             return true;
///         }
///         catch (ModelUnavailableException ex)
///         {
///             Console.WriteLine($"Model files are not available: {ex.Message}");
///             return false;
///         }
///     }
///     </code>
/// </example>
public sealed class ModelUnavailableException : WordSenseException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelUnavailableException"/> class.
    /// </summary>
    public ModelUnavailableException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelUnavailableException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public ModelUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelUnavailableException"/> class with a message and the original exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The original exception.</param>
    public ModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
