namespace DemaConsulting.WordSense;

/// <summary>
///     Describes how the scores of a <see cref="LemmaModel"/> are computed from its weight rows.
/// </summary>
public enum LemmaModelKind
{
    /// <summary>
    ///     A single weight row for a two-sense lemma: the score of the second sense is the row's
    ///     activation and the score of the first sense is its negation.
    /// </summary>
    Binary = 1,

    /// <summary>
    ///     One weight row per sense (one-versus-rest): each sense scores its own row's activation.
    /// </summary>
    OneVersusRest = 2,
}

/// <summary>
///     The trained linear classifier for one ambiguous lemma.
/// </summary>
/// <remarks>
///     Instances are immutable after construction and thread-safe. The sense labels are kept in
///     ordinal-sorted order, which is also the order of the weight rows.
/// </remarks>
public sealed class LemmaModel
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaModel"/> class.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="senses">The sense labels, at least two.</param>
    /// <param name="kind">How scores derive from the weight rows.</param>
    /// <param name="weights">The weight rows, row-major, <c>rows × dimension</c> values.</param>
    /// <param name="bias">One bias per weight row.</param>
    /// <param name="abstainMargin">The minimum score margin below which the classifier abstains.</param>
    /// <param name="dimension">The feature dimension.</param>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent or a value is not finite.</exception>
    public LemmaModel(
        string lemma,
        IReadOnlyList<string> senses,
        LemmaModelKind kind,
        float[] weights,
        float[] bias,
        float abstainMargin,
        int dimension)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(senses);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(bias);

        // Validate shapes up front so a corrupt model can never index out of range at classification time
        var rows = kind == LemmaModelKind.Binary ? 1 : senses.Count;
        if (senses.Count < 2 || (kind == LemmaModelKind.Binary && senses.Count != 2))
        {
            throw new ArgumentException("A lemma needs two senses, or more for one-versus-rest.", nameof(senses));
        }

        if (dimension <= 0 || weights.Length != rows * dimension || bias.Length != rows)
        {
            throw new ArgumentException("Weight and bias shapes do not match the sense count and dimension.", nameof(weights));
        }

        if (!float.IsFinite(abstainMargin) || weights.Any(w => !float.IsFinite(w)) || bias.Any(b => !float.IsFinite(b)))
        {
            throw new ArgumentException("Model values must be finite.", nameof(weights));
        }

        Lemma = lemma;
        Senses = senses.ToArray();
        Kind = kind;
        Weights = weights;
        Bias = bias;
        AbstainMargin = abstainMargin;
        Dimension = dimension;
    }

    /// <summary>
    ///     Gets the lower-case lemma this model classifies.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the sense labels in ordinal-sorted order.
    /// </summary>
    public IReadOnlyList<string> Senses { get; }

    /// <summary>
    ///     Gets how scores derive from the weight rows.
    /// </summary>
    public LemmaModelKind Kind { get; }

    /// <summary>
    ///     Gets the weight rows, row-major.
    /// </summary>
    public float[] Weights { get; }

    /// <summary>
    ///     Gets one bias per weight row.
    /// </summary>
    public float[] Bias { get; }

    /// <summary>
    ///     Gets the minimum score margin below which the classifier reports no sense.
    /// </summary>
    public float AbstainMargin { get; }

    /// <summary>
    ///     Gets the feature dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Scores a normalized feature vector against every sense.
    /// </summary>
    /// <param name="feature">The centered, L2-normalized feature vector of length <see cref="Dimension"/>.</param>
    /// <returns>The index of the best sense and the margin between the best and the second-best score.</returns>
    /// <exception cref="ArgumentException">Thrown when the feature length differs from <see cref="Dimension"/>.</exception>
    public (int Best, float Margin) Score(ReadOnlySpan<float> feature)
    {
        if (feature.Length != Dimension)
        {
            throw new ArgumentException("Feature length does not match the model dimension.", nameof(feature));
        }

        // Binary models use one activation s with scores {-s, +s}, so the margin is 2|s|
        if (Kind == LemmaModelKind.Binary)
        {
            var activation = VectorMath.Dot(Weights, feature) + Bias[0];
            return (activation >= 0 ? 1 : 0, 2 * Math.Abs(activation));
        }

        // One-versus-rest: take the best and second-best of the per-sense activations
        var best = 0;
        var bestScore = float.NegativeInfinity;
        var second = float.NegativeInfinity;
        for (var row = 0; row < Senses.Count; row++)
        {
            var score = VectorMath.Dot(Weights.AsSpan(row * Dimension, Dimension), feature) + Bias[row];
            if (score > bestScore)
            {
                second = bestScore;
                bestScore = score;
                best = row;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        return (best, bestScore - second);
    }
}
