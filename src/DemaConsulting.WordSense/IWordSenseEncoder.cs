namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: produces contextual token embeddings for a sequence of WordPiece ids.
/// </summary>
/// <remarks>
///     The abstraction exists so the classifier can be tested without a model and so alternative
///     encoders can be supplied. The production implementation is <see cref="OnnxWordSenseEncoder"/>.
///     Implementations must be safe for concurrent calls, because one classifier shares its encoder between
///     all of its callers.
/// </remarks>
public interface IWordSenseEncoder : IDisposable
{
    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the model file, used to confirm that the encoder
    ///     matches the model a <see cref="WordSenseTable"/> was trained with.
    /// </summary>
    string ModelSha256 { get; }

    /// <summary>
    ///     Gets the size of each token embedding.
    /// </summary>
    int HiddenSize { get; }

    /// <summary>
    ///     Encodes one sequence of WordPiece ids, which already include the special tokens.
    /// </summary>
    /// <param name="inputIds">The WordPiece ids; at least one.</param>
    /// <param name="cancellationToken">A token that cancels the call before inference starts.</param>
    /// <returns>
    ///     The hidden states, row-major, <c>inputIds.Length × HiddenSize</c> values.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputIds"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="inputIds"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the encoder has been disposed.</exception>
    ValueTask<float[]> EncodeAsync(int[] inputIds, CancellationToken cancellationToken);
}
