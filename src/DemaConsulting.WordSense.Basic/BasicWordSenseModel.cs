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
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
public sealed class BasicWordSenseModel : IWordSenseModel
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicWordSenseModel"/> class.
    /// </summary>
    /// <remarks>
    ///     The model needs no arguments, no files and no network, and it holds no resources: it does not implement
    ///     <see cref="IDisposable"/> and never needs disposal. One instance may be shared by any number of threads.
    /// </remarks>
    public BasicWordSenseModel()
    {
    }

    /// <summary>
    ///     Completes immediately: the model needs no files, so nothing is checked or downloaded.
    /// </summary>
    /// <param name="mirror">Ignored.</param>
    /// <param name="cancellationToken">A token; a task that is already cancelled is returned when it is cancelled.</param>
    /// <returns>A completed task, or a cancelled task when <paramref name="cancellationToken"/> is already cancelled.</returns>
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;

    /// <summary>
    ///     Creates a classifier that applies the rules.
    /// </summary>
    /// <returns>A new classifier, safe for concurrent use; it can be created at any time, even without calling <see cref="EnsureModelAsync"/>.</returns>
    public IWordSenseClassifier Create() => new BasicClassifier();
}
