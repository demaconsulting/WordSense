namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: an encoder model that turns a word in its context into a vector, so the classifier can choose
///     between the senses of a lemma. Each model has its own package, its own embedding space and its own
///     <see cref="WordSenseWeightsTable"/>.
/// </summary>
/// <remarks>
///     <para>
///     The core package has no encoder of its own: without a model the classifier still answers from the lexicon
///     (single-sense words and the candidates of ambiguous ones). A model package supplies an implementation, such as
///     the MiniLM package's <c>MiniLMWordSenseModel</c>, and the weights table trained for it. The model owns everything
///     that is specific to the encoder: its tokenizer, its sequence window and its pooling of sub-word vectors.
///     </para>
///     <para>
///     A weights table is only meaningful for the exact model it was trained with, so a model names itself by
///     <see cref="ModelId"/> and <see cref="ModelSha256"/>, which together are the identity a weights table must carry.
///     <see cref="EnsureWeightsMatch"/> compares that identity, and the classifier calls it before it accepts a table.
///     Implementations must be safe for concurrent calls, because one classifier shares its model between all of its callers.
///     </para>
/// </remarks>
public interface IWordSenseModel : IDisposable
{
    /// <summary>
    ///     Gets the identifier of the model, for example <c>all-MiniLM-L6-v2-int8</c>; it is the folder name of the model
    ///     everywhere and the <see cref="WordSenseWeightsTable.ModelId"/> of its weights table.
    /// </summary>
    string ModelId { get; }

    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the model file, which is the
    ///     <see cref="WordSenseWeightsTable.ModelSha256"/> of the weights table trained for it.
    /// </summary>
    string ModelSha256 { get; }

    /// <summary>
    ///     Gets the size of the vectors the model produces.
    /// </summary>
    int HiddenSize { get; }

    /// <summary>
    ///     Encodes the words as one running text and returns a vector for each requested target word in that context.
    /// </summary>
    /// <param name="words">All words of the text, in order; the context of every target.</param>
    /// <param name="targets">The indexes into <paramref name="words"/> of the words to return a vector for.</param>
    /// <param name="cancellationToken">A token that cancels the call before inference starts.</param>
    /// <returns>
    ///     One vector of <see cref="HiddenSize"/> values per target, parallel to <paramref name="targets"/>: the
    ///     contextual embedding of the target word, not centered or normalized. An entry is null when the model has no
    ///     embedding for that word, for example because it produces no tokens.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words"/> or <paramref name="targets"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a target is not an index of <paramref name="words"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    ValueTask<double[]?[]> EncodeAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken);

    /// <summary>
    ///     Checks that a weights table was trained with this model.
    /// </summary>
    /// <param name="weights">The weights table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="weights"/> is null.</exception>
    /// <exception cref="WordSenseModelMismatchException">
    ///     Thrown when the table names another model identifier or model file, or its dimension differs from
    ///     <see cref="HiddenSize"/>.
    /// </exception>
    void EnsureWeightsMatch(WordSenseWeightsTable weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (!string.Equals(weights.ModelId, ModelId, StringComparison.Ordinal) ||
            !string.Equals(weights.ModelSha256, ModelSha256, StringComparison.OrdinalIgnoreCase) ||
            weights.Dimension != HiddenSize)
        {
            throw new WordSenseModelMismatchException(
                "The weights table was trained with a different encoder model than the one supplied.");
        }
    }
}
