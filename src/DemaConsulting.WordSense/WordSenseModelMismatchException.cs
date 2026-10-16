namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: thrown when the encoder model does not match the model a classifier table was trained against.
/// </summary>
/// <remarks>
///     The table stores the SHA-256 of the model it was trained with. A different model produces a
///     different embedding space, so using the table with it would silently give wrong answers; the
///     classifier refuses to be created instead.
/// </remarks>
public sealed class WordSenseModelMismatchException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelMismatchException"/> class.
    /// </summary>
    public WordSenseModelMismatchException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelMismatchException"/> class with a message.
    /// </summary>
    /// <param name="message">Describes the mismatch.</param>
    public WordSenseModelMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseModelMismatchException"/> class with a message and cause.
    /// </summary>
    /// <param name="message">Describes the mismatch.</param>
    /// <param name="innerException">The underlying cause.</param>
    public WordSenseModelMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
