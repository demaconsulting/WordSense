using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     A trained lemma whose sense ids were resolved against the word image.
/// </summary>
internal sealed class ResolvedLemma
{
    private readonly FrozenDictionary<string, int> _columns;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ResolvedLemma"/> class.
    /// </summary>
    /// <param name="weights">The trained lemma.</param>
    /// <param name="senses">The catalog entry of every sense, in the column order of the weights.</param>
    public ResolvedLemma(WeightedLemma weights, ImmutableArray<SenseInfo> senses)
    {
        Weights = weights;
        Senses = senses;
        _columns = Enumerable.Range(0, senses.Length).ToFrozenDictionary(i => senses[i].SenseId, i => i, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the trained lemma.
    /// </summary>
    public WeightedLemma Weights { get; }

    /// <summary>
    ///     Gets the catalog entry of every sense, in the column order of the weights; it carries the group of the sense.
    /// </summary>
    public ImmutableArray<SenseInfo> Senses { get; }

    /// <summary>
    ///     Finds the column of a sense in the scores of the lemma.
    /// </summary>
    /// <param name="senseId">The sense id.</param>
    /// <returns>The column, or -1 when the lemma has no weights for the sense.</returns>
    public int ColumnOf(string senseId) => _columns.GetValueOrDefault(senseId, -1);
}

/// <summary>
///     The trained lemmas of a model, with every sense resolved against the word image and every form chooser found by
///     its form.
/// </summary>
/// <remarks>
///     <para>
///     The weights name their senses by sense id. The table checks the weights against the word image when it is built
///     and fails when they do not belong to it: another inventory hash, a lemma twice, a sense id that is unknown, used
///     twice or a sense of another lemma, or a lemma whose number of sense ids differs from the number of its weight
///     rows. There is no silent fallback: weights that do not match the image are an error
///     (<see cref="InvalidDataException"/>), never a wrong answer and never a quietly weaker model. The group of a
///     sense is not stored in the weights; it is taken from the image.
///     </para>
///     <para>
///     The image may hold more senses of a lemma than the weights were trained for; the model then chooses among the
///     trained senses only (a sense without a column is never answered).
///     </para>
/// </remarks>
internal sealed class WeightedLemmaTable
{
    private readonly FrozenDictionary<string, FormChooser> _choosers;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class with no trained lemma.
    /// </summary>
    public WeightedLemmaTable()
    {
        Data = new WeightsData(string.Empty, new byte[WeightsBlob.HashSize], 1, [0f], []);
        Lemmas = FrozenDictionary<string, ResolvedLemma>.Empty;
        _choosers = FrozenDictionary<string, FormChooser>.Empty;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemmaTable"/> class.
    /// </summary>
    /// <param name="data">The trained weights.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the weights were trained for another inventory, a lemma or a sense occurs twice, a sense is
    ///     unknown to the word image or belongs to another lemma, the sense ids and the weights of a lemma disagree, or a
    ///     lemma has a group threshold although none of its senses is in a group.
    /// </exception>
    public WeightedLemmaTable(WeightsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        var image = WordLexicon.Current.InventoryHash;
        if (!image.SequenceEqual(data.InventoryHash))
        {
            throw new InvalidDataException(
                $"The weights were trained for the inventory {Convert.ToHexString(data.InventoryHash).ToLowerInvariant()[..16]}, but the word image has the inventory {Convert.ToHexString(image).ToLowerInvariant()[..16]}.");
        }

        var lemmas = new Dictionary<string, ResolvedLemma>(StringComparer.Ordinal);
        var senses = new HashSet<string>(StringComparer.Ordinal);
        var choosers = new Dictionary<string, FormChooser>(StringComparer.Ordinal);
        foreach (var entry in data.Lemmas)
        {
            if (entry.SenseIds.Count != entry.Weights.SenseCount)
            {
                throw new InvalidDataException($"The weights of '{entry.Lemma}' have {entry.Weights.SenseCount} rows but name {entry.SenseIds.Count} senses.");
            }

            var resolved = ImmutableArray.CreateBuilder<SenseInfo>(entry.SenseIds.Count);
            if (entry.Weights.Dimension != data.Dimension)
            {
                throw new InvalidDataException($"The weights of '{entry.Lemma}' have the dimension {entry.Weights.Dimension}, not {data.Dimension}.");
            }
            foreach (var id in entry.SenseIds)
            {
                if (!WordLexicon.TryGetSense(id, out var sense))
                {
                    throw new InvalidDataException($"The weights of '{entry.Lemma}' name the sense '{id}', which the word image does not have.");
                }

                if (!string.Equals(sense.Lemma, entry.Lemma, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"The weights of '{entry.Lemma}' name the sense '{id}' of the lemma '{sense.Lemma}'.");
                }

                if (!senses.Add(id))
                {
                    throw new InvalidDataException($"The weights name the sense '{id}' twice.");
                }

                resolved.Add(sense);
            }

            if (entry.Weights.GroupThreshold < LemmaWeights.Never && resolved.All(s => s.GroupId is null))
            {
                throw new InvalidDataException($"The weights of '{entry.Lemma}' have a group threshold, but none of its senses is in a group.");
            }
            if (!lemmas.TryAdd(entry.Lemma, new ResolvedLemma(entry, resolved.MoveToImmutable())))
            {
                throw new InvalidDataException($"The weights have the lemma '{entry.Lemma}' twice.");
            }

            foreach (var chooser in entry.Choosers)
            {
                if (!choosers.TryAdd(chooser.Form, chooser))
                {
                    throw new InvalidDataException($"The weights have the form chooser '{chooser.Form}' twice.");
                }

                CheckChooser(chooser);
            }
        }

        Lemmas = lemmas.ToFrozenDictionary(StringComparer.Ordinal);
        _choosers = choosers.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Checks that the labels of a chooser stand for the analyses of its form: every analysis of the form has a label
    ///     (its lemma and features, else its lemma) and every label stands for an analysis.
    /// </summary>
    /// <param name="chooser">The chooser.</param>
    /// <exception cref="InvalidDataException">Thrown when the labels and the analyses of the form disagree.</exception>
    private static void CheckChooser(FormChooser chooser)
    {
        if (chooser.Labels.Count != chooser.Weights.SenseCount)
        {
            throw new InvalidDataException($"The chooser of '{chooser.Form}' has {chooser.Weights.SenseCount} rows but {chooser.Labels.Count} labels.");
        }

        if (!WordLexicon.TryLookup(chooser.Form, out var analyses))
        {
            throw new InvalidDataException($"The chooser of '{chooser.Form}' is for a form that the word image does not have.");
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var analysis in analyses)
        {
            var exact = analysis.Lemma + "/" + analysis.Features;
            var label = chooser.Labels.Contains(exact, StringComparer.Ordinal) ? exact : analysis.Lemma;
            if (!chooser.Labels.Contains(label, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"The chooser of '{chooser.Form}' has no label for the analysis {exact}.");
            }

            used.Add(label);
        }

        if (chooser.Labels.FirstOrDefault(l => !used.Contains(l)) is { } unused)
        {
            throw new InvalidDataException($"The chooser of '{chooser.Form}' has the label '{unused}', which stands for no analysis of the form.");
        }
    }

    /// <summary>
    ///     Gets the weights this table was built from.
    /// </summary>
    public WeightsData Data { get; }

    /// <summary>
    ///     Gets the trained lemmas by name.
    /// </summary>
    public FrozenDictionary<string, ResolvedLemma> Lemmas { get; }

    /// <summary>
    ///     Gets the number of form choosers.
    /// </summary>
    public int ChooserCount => _choosers.Count;

    /// <summary>
    ///     Loads the weights embedded in an assembly and checks them for the model and the word image they must belong to.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the weights.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="modelId">The model identifier the weights must have been trained for.</param>
    /// <param name="hiddenSize">The hidden size of the encoder the weights must match.</param>
    /// <returns>The checked table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the resource is missing, corrupt, for another model, dimension or inventory.</exception>
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
    ///     Finds the weights of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The resolved lemma, or null when it has no weights.</returns>
    public ResolvedLemma? Find(string lemma) => Lemmas.GetValueOrDefault(lemma);

    /// <summary>
    ///     Finds the chooser of a form.
    /// </summary>
    /// <param name="form">The lower-case form.</param>
    /// <returns>The chooser, or null when the form has none.</returns>
    public FormChooser? FindChooser(string form) => _choosers.GetValueOrDefault(form);
}
