using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The trained lemmas of a model, keyed by lemma and each matched to the word lexicon.
/// </summary>
/// <remarks>
///     A lemma is matched all or nothing: its weights are kept only when the trained senses (same lemma, part of speech
///     and meaning, in the same order) are the first senses the lexicon holds for the lemma; the lexicon may append
///     senses after them (parts of speech the dictionary lists that the model was not trained on), and the model then
///     chooses among the trained senses only. Otherwise the whole lemma is dropped
///     and counted in <see cref="DroppedLemmaNames"/>, so a stale or mismatched weights blob never gives a wrong answer.
///     The tests assert that the shipped blobs drop nothing.
/// </remarks>
internal sealed class WeightedLemmaTable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class.
    /// </summary>
    /// <param name="data">The trained weights.</param>
    /// <param name="words">The word lexicon to match against.</param>
    public WeightedLemmaTable(WeightsData data, IReadOnlyDictionary<string, ImmutableArray<WordDetails>> words)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(words);
        Data = data;
        var kept = new Dictionary<string, WeightedLemma>(StringComparer.Ordinal);
        var dropped = new List<string>();
        foreach (var entry in data.Lemmas)
        {
            if (words.TryGetValue(entry.Lemma, out var details) && Matches(entry, details))
            {
                kept[entry.Lemma] = entry;
            }
            else
            {
                dropped.Add(entry.Lemma);
            }
        }

        Lemmas = kept.ToFrozenDictionary(StringComparer.Ordinal);
        DroppedLemmaNames = dropped;
    }

    /// <summary>
    ///     Gets the weights this table was built from.
    /// </summary>
    public WeightsData Data { get; }

    /// <summary>
    ///     Gets the lemmas that kept their weights, by lemma.
    /// </summary>
    public FrozenDictionary<string, WeightedLemma> Lemmas { get; }

    /// <summary>
    ///     Gets the lemmas that were dropped because the lexicon disagreed.
    /// </summary>
    public IReadOnlyList<string> DroppedLemmaNames { get; }

    /// <summary>
    ///     Gets the number of lemmas dropped because the lexicon disagreed.
    /// </summary>
    public int DroppedLemmas => DroppedLemmaNames.Count;

    /// <summary>
    ///     Loads the weights embedded in an assembly and matches them to the word lexicon.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the weights.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="modelId">The model identifier the weights must have been trained for.</param>
    /// <param name="hiddenSize">The hidden size of the encoder the weights must match.</param>
    /// <returns>The matched lemma table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing, corrupt or for another model.</exception>
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
            ? new WeightedLemmaTable(data, WordLexicon.Words)
            : throw new InvalidDataException($"The embedded weights have dimension {data.Dimension}, not {hiddenSize}.");
    }

    /// <summary>
    ///     Finds the weights of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The matched lemma, or null when it has no (intact) weights.</returns>
    public WeightedLemma? Find(string lemma) => Lemmas.GetValueOrDefault(lemma);

    /// <summary>
    ///     Checks whether the senses of a weights entry are the first senses of the word lexicon entry.
    /// </summary>
    /// <param name="entry">The weights entry.</param>
    /// <param name="details">The senses from the word lexicon.</param>
    /// <returns>True when the trained senses are identical in content and order to the first lexicon senses.</returns>
    private static bool Matches(WeightedLemma entry, ImmutableArray<WordDetails> details)
    {
        if (details.Length < entry.Senses.Count || entry.Senses.Count != entry.Weights.SenseCount)
        {
            return false;
        }

        // The trained senses are a prefix of the lexicon senses; the lexicon may append senses the model has no weights for
        for (var i = 0; i < entry.Senses.Count; i++)
        {
            if (details[i].Lemma != entry.Senses[i].Lemma ||
                details[i].PartOfSpeech != entry.Senses[i].PartOfSpeech ||
                details[i].Meaning != entry.Senses[i].Meaning)
            {
                return false;
            }
        }

        return true;
    }
}
