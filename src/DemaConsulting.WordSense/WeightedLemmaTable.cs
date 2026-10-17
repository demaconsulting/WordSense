namespace DemaConsulting.WordSense;

/// <summary>
///     One lemma that has trained weights, matched to its senses in the word table.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Details">The senses of the lemma as stored in the word table, in score order.</param>
/// <param name="Weights">The classifier that scores <paramref name="Details"/>.</param>
internal sealed record WeightedLemma(string Lemma, WordDetails[] Details, LemmaWeights Weights);

/// <summary>
///     The trained lemmas of a model, each matched to the word table.
/// </summary>
/// <remarks>
///     A lemma is matched all or nothing: its weights are kept only when the word table holds exactly the same senses
///     (same part of speech and meaning, same order) for the lemma. Otherwise the whole lemma is dropped and counted in
///     <see cref="DroppedLemmas"/>, so a stale or mismatched weights blob never gives a wrong answer. The tests assert
///     that the shipped blobs drop nothing.
/// </remarks>
internal sealed class WeightedLemmaTable
{
    private readonly Dictionary<string, WeightedLemma> _lemmas = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class.
    /// </summary>
    /// <param name="data">The trained weights.</param>
    /// <param name="words">The word table to match against.</param>
    public WeightedLemmaTable(WeightsData data, IReadOnlyDictionary<string, WordDetails[]> words)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(words);
        Data = data;
        var dropped = new List<string>();
        foreach (var entry in data.Lemmas)
        {
            if (words.TryGetValue(entry.Lemma, out var details) && Matches(entry, details))
            {
                _lemmas[entry.Lemma] = new WeightedLemma(entry.Lemma, details, entry.Weights);
            }
            else
            {
                dropped.Add(entry.Lemma);
            }
        }

        DroppedLemmaNames = dropped;
    }

    /// <summary>
    ///     Gets the weights this table was built from.
    /// </summary>
    public WeightsData Data { get; }

    /// <summary>
    ///     Gets the number of weights lemmas dropped because the word table disagreed.
    /// </summary>
    public int DroppedLemmas => DroppedLemmaNames.Count;

    /// <summary>
    ///     Gets the lemmas that were dropped.
    /// </summary>
    public IReadOnlyList<string> DroppedLemmaNames { get; }

    /// <summary>
    ///     Gets the number of lemmas that kept their weights.
    /// </summary>
    public int Count => _lemmas.Count;

    /// <summary>
    ///     Loads the weights embedded in an assembly and matches them to the word table.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the weights.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="modelId">The model identifier the weights must have been trained for.</param>
    /// <returns>The matched lemma table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing, corrupt or for another model.</exception>
    public static WeightedLemmaTable LoadEmbedded(System.Reflection.Assembly assembly, string resourceName, string modelId)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The embedded weights '{resourceName}' are missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = WeightsBlob.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        return data.ModelId == modelId
            ? new WeightedLemmaTable(data, Words.Entries)
            : throw new InvalidDataException($"The embedded weights were trained for '{data.ModelId}', not '{modelId}'.");
    }

    /// <summary>
    ///     Finds the weights of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The matched lemma, or null when it has no weights.</returns>
    public WeightedLemma? Find(string lemma) => _lemmas.GetValueOrDefault(lemma);

    /// <summary>
    ///     Checks whether the senses of a weights entry equal the senses in the word table.
    /// </summary>
    /// <param name="entry">The weights entry.</param>
    /// <param name="details">The senses from the word table.</param>
    /// <returns>True when the senses are identical in content and order.</returns>
    private static bool Matches(LemmaWeightsEntry entry, WordDetails[] details)
    {
        if (details.Length != entry.Senses.Count || details.Length != entry.Weights.SenseCount)
        {
            return false;
        }

        for (var i = 0; i < details.Length; i++)
        {
            if (details[i].Lemma != entry.Lemma ||
                details[i].PartOfSpeech != entry.Senses[i].PartOfSpeech ||
                details[i].Meaning != entry.Senses[i].Meaning)
            {
                return false;
            }
        }

        return true;
    }
}
