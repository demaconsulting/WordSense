namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     A word-sense model that needs no neural network: it picks the part of speech of an ambiguous word from the
///     words around it with hand-written rules.
/// </summary>
/// <remarks>
///     <para>
///     The model looks every word up in <see cref="WordLexicon"/>. A word with one entry is answered with full
///     confidence. For a word with several entries, closed-class word lists (determiners, modals, pronouns,
///     prepositions) and the possible parts of speech of the neighbors select a part of speech with a fixed confidence
///     per rule; when no rule applies, the word is not classified. It has no model files, no trained weights and no ONNX
///     Runtime dependency: <see cref="EnsureModelAsync"/> returns immediately and nothing is ever downloaded. It is
///     small and fast, and less accurate than the encoder models of <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c>; its accuracy is reported in the repository's build notes.
///     </para>
///     <para>
///     The model holds no resources, so it needs no disposal.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
public sealed class BasicWordSenseModel : IWordSenseModel
{
    /// <inheritdoc />
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public IWordSenseClassifier Create() => new BasicClassifier();
}
