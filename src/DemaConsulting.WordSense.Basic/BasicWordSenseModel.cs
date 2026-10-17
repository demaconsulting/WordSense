namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     A word-sense model that needs no neural network: it picks the sense of an ambiguous word from the words around it.
/// </summary>
/// <remarks>
///     <para>
///     The model uses a window of three words on each side of the target, with the function-word classes, the possible
///     parts of speech of the neighbors, word endings, capitalization and a few word pairs as features, and a linear
///     classifier trained per lemma on the same corpus as the encoder models. It has no model files and no ONNX Runtime
///     dependency: <see cref="EnsureModelAsync"/> returns immediately and nothing is ever downloaded. It is small and
///     fast, and less accurate than the encoder models of <c>DemaConsulting.WordSense.MiniLM</c> and
///     <c>DemaConsulting.WordSense.BertBaseCased</c>; its accuracy is reported in the repository's build notes.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync(null, CancellationToken.None);
///     IWordSenseClassifier classifier = model.Create();
///
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], CancellationToken.None);
///     </code>
/// </example>
public sealed class BasicWordSenseModel : IWordSenseModel
{
    /// <summary>
    ///     The identifier recorded in the weights of this model.
    /// </summary>
    internal const string ModelId = "basic";

    private readonly object _gate = new();
    private WeightedLemmaTable? _table;
    private bool _disposed;

    /// <summary>
    ///     Completes immediately: the model has no files to obtain.
    /// </summary>
    /// <param name="mirror">Ignored.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the model has been disposed.</exception>
    public Task EnsureModelAsync(MirrorOptions? mirror, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public IWordSenseClassifier Create()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _table ??= WeightedLemmaTable.LoadEmbedded(typeof(BasicWordSenseModel).Assembly, "DemaConsulting.WordSense.Basic.weights.bin", ModelId);
            return new ClassificationEngine(Words.Entries, _table, new BasicFeatureSource(Words.Entries));
        }
    }

    /// <summary>
    ///     Marks the model as disposed. Calling it more than once is harmless.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }
}

/// <summary>
///     Computes the sparse context features of the basic model.
/// </summary>
internal sealed class BasicFeatureSource : IFeatureSource
{
    private readonly IReadOnlyDictionary<string, WordDetails[]> _table;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicFeatureSource"/> class.
    /// </summary>
    /// <param name="table">The word table that supplies the parts of speech of the neighbors.</param>
    public BasicFeatureSource(IReadOnlyDictionary<string, WordDetails[]> table)
    {
        _table = table;
    }

    /// <inheritdoc />
    public Task<ContextFeature?[]> GetFeaturesAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken cancellationToken)
    {
        var result = new ContextFeature?[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (ids, value) = BasicFeatures.Extract(words, targets[i], _table);
            result[i] = new ContextFeature(null, ids, value);
        }

        return Task.FromResult(result);
    }
}
