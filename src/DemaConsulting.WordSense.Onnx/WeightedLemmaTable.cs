using System.Collections.Frozen;
using System.Reflection;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained lemma groups of a model, with every trained class of them found by its id.
/// </summary>
/// <remarks>
///     <para>
///     The weights name their senses by class id. The table checks the structure of the weights when it is built and
///     fails when they are inconsistent: a lemma twice, a class id used twice (within a lemma or by two lemmas), or a
///     lemma whose number of class ids differs from the number of its weight rows. There is no silent fallback: an
///     inconsistent weights blob is an error (<see cref="InvalidDataException"/>), never a wrong answer and never a
///     quietly weaker model. A class is found by its id with <see cref="FindByClass"/>; no id is parsed and no class
///     is derived from a name. The table does not look at the word lexicon: whether the class ids belong to the
///     lexicon in use is the business of the code that scores with them.
///     </para>
///     <para>
///     The lexicon may hold more senses of a lemma than the weights were trained for; the model then chooses among the
///     trained classes only.
///     </para>
/// </remarks>
internal sealed class WeightedLemmaTable
{
    private readonly FrozenDictionary<string, (WeightedLemma Lemma, int Column)> _byClass;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class.
    /// </summary>
    /// <param name="data">The trained weights.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when a lemma or a class id occurs twice, or the class ids and the weights of a lemma disagree.</exception>
    public WeightedLemmaTable(WeightsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        var lemmas = new Dictionary<string, WeightedLemma>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, (WeightedLemma, int)>(StringComparer.Ordinal);
        foreach (var entry in data.Lemmas)
        {
            if (entry.ClassIds.Count != entry.Weights.SenseCount)
            {
                throw new InvalidDataException($"The weights of '{entry.Lemma}' have {entry.Weights.SenseCount} senses but name {entry.ClassIds.Count} classes.");
            }

            if (!lemmas.TryAdd(entry.Lemma, entry))
            {
                throw new InvalidDataException($"The weights have the lemma '{entry.Lemma}' twice.");
            }

            for (var column = 0; column < entry.ClassIds.Count; column++)
            {
                var id = entry.ClassIds[column];
                if (!byClass.TryAdd(id, (entry, column)))
                {
                    throw new InvalidDataException($"The weights name the class '{id}' twice.");
                }
            }
        }

        Lemmas = lemmas.ToFrozenDictionary(StringComparer.Ordinal);
        _byClass = byClass.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the weights this table was built from.
    /// </summary>
    public WeightsData Data { get; }

    /// <summary>
    ///     Gets the lemma groups by name.
    /// </summary>
    public FrozenDictionary<string, WeightedLemma> Lemmas { get; }

    /// <summary>
    ///     Loads the weights embedded in an assembly and checks them for the model they must belong to.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the weights.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="modelId">The model identifier the weights must have been trained for.</param>
    /// <param name="hiddenSize">The hidden size of the encoder the weights must match.</param>
    /// <returns>The checked table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing, corrupt, for another model or of another dimension.</exception>
    public static WeightedLemmaTable LoadEmbedded(Assembly assembly, string resourceName, string modelId, int hiddenSize)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The embedded weights '{resourceName}' are missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = WeightsBlob.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (data.ModelId != modelId)
        {
            throw new InvalidDataException($"The embedded weights were trained for '{data.ModelId}', not '{modelId}'.");
        }

        return data.Dimension == hiddenSize
            ? new WeightedLemmaTable(data)
            : throw new InvalidDataException($"The embedded weights have dimension {data.Dimension}, not {hiddenSize}.");
    }

    /// <summary>
    ///     Finds the weights that score a class.
    /// </summary>
    /// <param name="classId">The class id (the sense id of an analysis).</param>
    /// <param name="column">Receives the column of the class in the scores of the weights.</param>
    /// <returns>The weights of the lemma group of the class, or null when the class has no weights.</returns>
    public WeightedLemma? FindByClass(string classId, out int column)
    {
        if (_byClass.TryGetValue(classId, out var found))
        {
            column = found.Column;
            return found.Lemma;
        }

        column = -1;
        return null;
    }

    /// <summary>
    ///     Finds the weights of a lemma group by name.
    /// </summary>
    /// <param name="lemma">The name of the group.</param>
    /// <returns>The weights, or null when there are none.</returns>
    public WeightedLemma? Find(string lemma) => Lemmas.GetValueOrDefault(lemma);
}
