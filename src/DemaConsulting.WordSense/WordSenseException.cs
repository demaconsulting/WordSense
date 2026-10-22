namespace DemaConsulting.WordSense;

/// <summary>
///     The base class of the exceptions that the WordSense packages report for a model that cannot be used: its files
///     are unavailable, damaged or wrongly configured for download.
/// </summary>
/// <remarks>
///     <para>
///     Catch this type to handle every such failure in one place, or catch a derived type to react to a specific cause:
///     <see cref="ModelUnavailableException"/> (the files could not be obtained), <see cref="ModelIntegrityException"/>
///     (the files or embedded resources are damaged or do not belong together) and
///     <see cref="DownloadConfigurationException"/> (the download settings are invalid). The class is abstract: only
///     the derived types are thrown.
///     </para>
///     <para>
///     The original failure (for example an <see cref="IOException"/>, an <see cref="InvalidDataException"/> or an
///     <see cref="ArgumentException"/>) is always the <see cref="Exception.InnerException"/>, so no detail is lost.
///     Mistakes in a call (a null argument, a disposed model, a cancelled token) are not reported with this type; they
///     keep the usual .NET exceptions (<see cref="ArgumentNullException"/>, <see cref="ObjectDisposedException"/>,
///     <see cref="OperationCanceledException"/>).
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static async Task PrepareAsync(IDownloadableWordSenseModel model)
///     {
///         try
///         {
///             await model.EnsureModelAsync();
///         }
///         catch (WordSenseException ex)
///         {
///             Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
///         }
///     }
///     </code>
/// </example>
public abstract class WordSenseException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseException"/> class.
    /// </summary>
    protected WordSenseException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    protected WordSenseException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseException"/> class with a message and the original exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The original exception.</param>
    protected WordSenseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
