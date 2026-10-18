using System.Collections.Frozen;
using System.Reflection;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained lemma groups of a model, each class of them resolved to a class of the word image.
/// </summary>
/// <remarks>
///     <para>
///     The weights name their senses by class id, the stable sense id of the public API. The table checks the weights
///     against the word image when it is built and fails when they do not belong together: the class hash of the weights
///     must equal the class hash of the image, and every class id must be a class of the image. There is no silent
///     fallback: a stale or mismatched weights blob is an error (<see cref="InvalidDataException"/>), never a wrong answer
///     and never a quietly weaker model. A class is found by its id with <see cref="FindByClass"/>; no id is parsed and
///     no class is derived from a name.
///     </para>
///     <para>
///     The lexicon may hold more classes of a lemma than the weights were trained for (for example a part of speech
///     the model has no weights for); the model then chooses among the trained classes only.
///     </para>
/// </remarks>
internal sealed class WeightedLemmaTable
{
    private readonly FrozenDictionary<string, (WeightedLemma Lemma, int Column)> _byClass;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class.
    /// </summary>
    /// <param name="data">The trained weights.</param>
    /// <param name="image">The word image the weights must belong to.</param>
    /// <exception cref="InvalidDataException">Thrown when the class hash differs from the image, a class id is not in the image, or the senses and the weights of a lemma disagree.</exception>
    public WeightedLemmaTable(WeightsData data, WordImage image)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(image);
        if (!data.ClassHash.AsSpan().SequenceEqual(image.ClassHash))
        {
            throw new InvalidDataException(
                $"The weights were trained for another class inventory: their class hash is {Convert.ToHexString(data.ClassHash)}, the word image has {Convert.ToHexString(image.ClassHash)}. Rebuild the weights.");
        }

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
                if (image.ClassIndex(id) < 0)
                {
                    throw new InvalidDataException($"The weights of '{entry.Lemma}' name the class '{id}', which is not in the word image.");
                }

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
    ///     Loads the weights embedded in an assembly and checks them against the word lexicon.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the weights.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="modelId">The model identifier the weights must have been trained for.</param>
    /// <param name="hiddenSize">The hidden size of the encoder the weights must match.</param>
    /// <returns>The checked table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing, corrupt, for another model or not for the word lexicon.</exception>
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
            ? new WeightedLemmaTable(data, WordLexicon.Current)
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
