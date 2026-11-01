using System.Globalization;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The layout of the sense feature: which encoder layers the pooled vector of a word is made of.
/// </summary>
/// <remarks>
///     <para>
///     A layout is either <see cref="Last"/> (the last layer, <c>last_hidden_state</c>) or the mean of several layers
///     (<c>mean:9-12</c> for consecutive layers, <c>mean:9,11,12</c> otherwise). The layers are the layers of the
///     encoder, counted from 1; the last one of a mean is the last layer of the encoder, which the graph exposes as
///     <c>last_hidden_state</c>, and every other layer <c>n</c> is a tap that the graph exposes as <c>hidden_n</c>.
///     </para>
///     <para>
///     The layout is part of the identity of the embedded weights: the weights file records it, and a file whose
///     layout is not the one of the model is rejected.
///     </para>
/// </remarks>
internal static class FeatureLayout
{
    /// <summary>
    ///     The layout of a model without layer taps: the last layer.
    /// </summary>
    public const string Last = "last";

    /// <summary>
    ///     The name of the graph output of the last layer.
    /// </summary>
    public const string LastOutputName = "last_hidden_state";

    private const string MeanPrefix = "mean:";

    /// <summary>
    ///     Gets the layout of a list of sense layers.
    /// </summary>
    /// <param name="layers">The layers of the mean in increasing order, or null for the last layer.</param>
    /// <returns>The layout text.</returns>
    /// <exception cref="ArgumentException">Thrown when the list has fewer than two layers, a layer below 1 or layers that do not increase.</exception>
    public static string Describe(IReadOnlyList<int>? layers)
    {
        if (layers is null)
        {
            return Last;
        }

        Validate(layers);
        var consecutive = layers[^1] - layers[0] == layers.Count - 1;
        return consecutive
            ? string.Create(CultureInfo.InvariantCulture, $"{MeanPrefix}{layers[0]}-{layers[^1]}")
            : MeanPrefix + string.Join(',', layers.Select(l => l.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    ///     Checks a list of sense layers.
    /// </summary>
    /// <param name="layers">The layers of the mean.</param>
    /// <exception cref="ArgumentException">Thrown when the list has fewer than two layers, a layer below 1 or layers that do not increase.</exception>
    public static void Validate(IReadOnlyList<int> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count < 2)
        {
            throw new ArgumentException("A mean needs at least two layers; the last layer alone is the layout 'last'.", nameof(layers));
        }

        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i] < 1 || (i > 0 && layers[i] <= layers[i - 1]))
            {
                throw new ArgumentException("The sense layers must be at least 1 and increase.", nameof(layers));
            }
        }
    }

    /// <summary>
    ///     Gets the names of the graph outputs of the layers, in the order of the layers.
    /// </summary>
    /// <param name="layers">The validated layers; the last one is the last layer of the encoder.</param>
    /// <returns>The output names: <c>hidden_n</c> for every layer but the last, then <c>last_hidden_state</c>.</returns>
    public static string[] OutputNames(IReadOnlyList<int> layers) =>
        [.. layers.Take(layers.Count - 1).Select(l => string.Create(CultureInfo.InvariantCulture, $"hidden_{l}")), LastOutputName];
}
