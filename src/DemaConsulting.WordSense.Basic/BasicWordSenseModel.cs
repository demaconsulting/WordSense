namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     A word-sense model that needs no neural network: a statistical part-of-speech tagger and a context cue model
///     choose among the senses of an ambiguous word.
/// </summary>
/// <remarks>
///     <para>
///     The model looks every word up in <see cref="WordLexicon"/>. A word with one entry is answered with full
///     confidence. For a word with several entries, a hidden Markov model over the whole sentence estimates the part of
///     speech of the word, a naive Bayes model over the neighboring words separates senses that share a part of speech,
///     and the two are combined into one probability per sense; the most probable sense is returned with that
///     probability as the confidence. Its small statistical tables are embedded in the assembly: there are no model
///     files, no ONNX Runtime dependency, <see cref="EnsureModelAsync"/> returns immediately and nothing is ever
///     downloaded. It is small and fast, and less accurate than the encoder models of
///     <c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c>; its accuracy is
///     reported in the repository's build notes.
///     </para>
///     <para>
///     The model holds no disposable resources, so it needs no disposal; one instance and the classifiers it creates
///     can be shared by any number of threads. <see cref="EnsureModelAsync"/> is optional (it only returns a completed
///     task) and <see cref="Create"/> needs no preceding call; see the namespace documentation for first-use costs,
///     tokenization, the meaning of the confidence (a probability, with a scale of its own) and when this model is
///     enough.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
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
    ///     Creates a classifier that uses the embedded statistics.
    /// </summary>
    /// <returns>A new classifier, safe for concurrent use. Unlike the encoder models, it needs no preceding call of <see cref="EnsureModelAsync"/> and never throws <see cref="InvalidOperationException"/>; the first call loads the word lexicon (about 120 ms, approximate).</returns>
    public IWordSenseClassifier Create() => new BasicClassifier();
}
