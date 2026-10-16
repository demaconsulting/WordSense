namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: thrown when a classifier table is malformed, truncated, corrupted, or of an unsupported version.
/// </summary>
public sealed class WordSenseTableFormatException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTableFormatException"/> class.
    /// </summary>
    public WordSenseTableFormatException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTableFormatException"/> class with a message.
    /// </summary>
    /// <param name="message">Describes what is wrong with the table.</param>
    public WordSenseTableFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTableFormatException"/> class with a message and cause.
    /// </summary>
    /// <param name="message">Describes what is wrong with the table.</param>
    /// <param name="innerException">The underlying cause.</param>
    public WordSenseTableFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
