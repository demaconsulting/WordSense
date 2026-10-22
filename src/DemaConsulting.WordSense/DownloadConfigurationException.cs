namespace DemaConsulting.WordSense;

/// <summary>
///     The exception thrown when the settings for downloading model files are invalid: the mirror address, the
///     credentials, the timeout, the store location, or the <c>WORDSENSE_MIRROR_*</c> environment variables.
/// </summary>
/// <remarks>
///     <para>
///     The <see cref="Exception.InnerException"/> is the original <see cref="ArgumentException"/> (or its subclass
///     <see cref="ArgumentOutOfRangeException"/> for a timeout), which names the invalid setting. This type derives from
///     <see cref="WordSenseException"/>, not from <see cref="ArgumentException"/>: code that caught
///     <see cref="ArgumentException"/> around <c>EnsureModelAsync</c> to detect an invalid mirror must catch this
///     type instead. A null argument (<see cref="ArgumentNullException"/>) is a mistake in the call and is not
///     reported with this type.
///     </para>
///     <para>
///     The settings are validated when a model uses them, so the exception comes from <c>EnsureModelAsync</c> or from the
///     lazy ensure of <c>ClassifyAsync</c>, not from the constructor of <see cref="DownloadOptions"/>. No exception is
///     thrown when the model was constructed with a model directory, because the settings are then ignored. Fix the
///     setting and call again; retrying with the same settings fails again.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static async Task PrepareAsync(IDownloadableWordSenseModel model, Uri mirror)
///     {
///         try
///         {
///             await model.EnsureModelAsync(new DownloadOptions(mirror, Timeout: TimeSpan.FromMinutes(2)));
///         }
///         catch (DownloadConfigurationException ex)
///         {
///             Console.WriteLine($"Invalid download settings: {ex.InnerException?.Message ?? ex.Message}");
///         }
///     }
///     </code>
/// </example>
public sealed class DownloadConfigurationException : WordSenseException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DownloadConfigurationException"/> class.
    /// </summary>
    public DownloadConfigurationException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DownloadConfigurationException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the invalid setting.</param>
    public DownloadConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DownloadConfigurationException"/> class with a message and the original exception.
    /// </summary>
    /// <param name="message">The message that describes the invalid setting.</param>
    /// <param name="innerException">The original exception.</param>
    public DownloadConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
