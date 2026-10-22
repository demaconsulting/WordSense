namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model that needs no neural network: a statistical part-of-speech tagger and a context cue model
///     choose the analysis (lemma, part of speech and features) of an ambiguous word.
/// </summary>
/// <remarks>
///     <para>
///     The model looks every word up in <see cref="WordLexicon"/>. A word with one analysis lists it with
///     probability 1: the result is at the <see cref="SenseLevel.Sense"/> level with its sense when its lemma and part of
///     speech have exactly one sense, otherwise at the <see cref="SenseLevel.PartOfSpeech"/> level; the confidence is 1
///     at either level (it is the probability of the lemma and part of speech, which has no alternative, so 1 does not mean
///     that a sense is known: test the level). For a word with
///     several analyses, a hidden Markov model over the whole sentence estimates the part of speech of the word, a naive
///     Bayes model over the neighboring words separates analyses that share a part of speech (the past and the present of
///     <c>read</c>), and the two are combined into one probability per analysis; <see cref="ClassifyResult.Analyses"/>
///     lists every analysis with that probability, the analyses of the most probable lemma and part of speech first and
///     the best of them the selected analysis
///     (<see cref="ClassifyResult.Details"/>). The result is at the part-of-speech level with the probability of the
///     lemma and part of speech of the selected analysis (the sum over its feature variants) as the
///     confidence, or at the sense level when the selected analysis has exactly one sense. The statistics carry no
///     evidence that separates fine senses or groups of senses, so the model never answers at the
///     <see cref="SenseLevel.Group"/> level, and the <see cref="AnalysisCandidate.Senses"/> of an analysis are empty
///     unless its lemma and part of speech have exactly one sense (then that sense, with probability 1). A word that is
///     not in the lexicon is not known (<see cref="ClassifyResult.IsKnown"/> is false). Its small statistical
///     tables are embedded in the assembly: there are no model
///     files, no ONNX Runtime dependency, <see cref="EnsureModelAsync"/> returns immediately and nothing is ever
///     downloaded. It is small and fast, and less accurate than the encoder models of
///     <c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c>; its accuracy is
///     reported in the model comparison table of the user guide (<c>docs/user_guide/model-table.md</c>).
///     </para>
///     <para>
///     The model holds no native resources, so its <see cref="Dispose"/> releases nothing; it only marks the model as
///     disposed, for the same rules as every <see cref="IWordSenseModel"/>: afterwards <see cref="EnsureModelAsync"/>
///     and <see cref="ClassifyAsync"/> throw <see cref="ObjectDisposedException"/> (from the call itself, not through
///     a returned task, and before any other check, so also when the token is already cancelled). Disposing while a
///     classification is running is safe: nothing is released, so the running call completes. Dispose it with <c>using</c> like
///     any other model. One instance can be shared by any number of threads. <see cref="EnsureModelAsync"/> is optional
///     (it only returns a completed task) and <see cref="ClassifyAsync"/> needs no preceding call: there is nothing
///     to ensure, so its lazy ensure is a no-op. If the token passed to <see cref="EnsureModelAsync"/> is
///     already cancelled, the returned task is always cancelled (awaiting it throws <see cref="OperationCanceledException"/>,
///     the same rule as for every model; see <see cref="IWordSenseModel.EnsureModelAsync"/>);
///     any other token is ignored. See the namespace documentation for first-use costs,
///     tokenization, the meaning of the confidence (a probability, with a scale of its own) and when this model is
///     enough.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using DemaConsulting.WordSense;
///
///     using var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     ClassifyResult[] results = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {results[i].Details?.PartOfSpeech.ToString() ?? "-"} ({results[i].Level}, {results[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
public sealed class BasicWordSenseModel : IWordSenseModel
{
    private readonly Lazy<BasicClassifier> _classifier = new(() => new BasicClassifier());
    private volatile bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicWordSenseModel"/> class.
    /// </summary>
    /// <remarks>
    ///     The model needs no arguments, no files and no network, and it holds no native resources. One instance may
    ///     be shared by any number of threads. Dispose it (with <c>using</c>) like any <see cref="IWordSenseModel"/>.
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
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text with the embedded statistics.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens, each as its own element.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A non-null array parallel to <paramref name="words"/>. Unlike the encoder models, it needs no preceding ensure (there is nothing to ensure); the first call loads the word lexicon (about 55 ms, approximate). Everything runs synchronously on the calling thread and the returned task is already complete (offload the call yourself if the caller must not block); the token is checked when the call starts and between sentence segments. An empty element is unknown (<see cref="ClassifyResult.IsKnown"/> is false, level none, no analysis, no details, confidence 0); an element that contains a space is not split and is unknown too.</returns>
    /// <exception cref="ArgumentNullException">Thrown (from the call itself) when the list is null.</exception>
    /// <exception cref="ArgumentException">Thrown (from the call itself) when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown (from the call itself) when <paramref name="cancellationToken"/> is already cancelled when the call starts, or is cancelled between two sentence segments.</exception>
    /// <exception cref="ObjectDisposedException">Thrown (from the call itself, before any other check) when the model has been disposed.</exception>
    public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _classifier.Value.ClassifyAsync(words, cancellationToken);
    }

    /// <summary>
    ///     Marks the model as disposed; it holds nothing to release. Calling it more than once, or from several
    ///     threads at once, is harmless.
    /// </summary>
    public void Dispose() => _disposed = true;

    /// <summary>
    ///     Throws when the model has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
