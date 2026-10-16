namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: describes how the scores of a <see cref="SenseWeights"/> are computed from its weight rows.
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
///     Advanced: the trained linear classifier of one ambiguous lemma. Applications normally use
///     <see cref="IWordSenseClassifier"/>; read this to see a lemma's <see cref="AbstainMargin"/>.
/// </summary>
/// <remarks>
///     Instances are thread-safe provided no caller modifies <see cref="Weights"/> or <see cref="Bias"/>, which
///     are exposed as arrays for speed and must be treated as read-only; the constructor copies the arrays it
///     is given. The number of weight rows is the number of bias values; the senses of the owning
///     <see cref="LemmaEntry"/> are scored in the order of the rows.
/// </remarks>
public sealed class SenseWeights
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SenseWeights"/> class.
    /// </summary>
    /// <param name="kind">How scores derive from the weight rows.</param>
    /// <param name="weights">The weight rows, row-major, <c>rows × dimension</c> values.</param>
    /// <param name="bias">One bias per weight row.</param>
    /// <param name="abstainMargin">
    ///     The base abstain margin. A word is answered only when its score margin is at least
    ///     <c>AbstainMargin * AbstainMarginScale</c> (see <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>);
    ///     otherwise the classifier abstains: <c>Details</c> is null and <c>Abstained</c> is true. Must be finite.
    /// </param>
    /// <param name="dimension">The feature dimension.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="weights"/> or <paramref name="bias"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="kind"/> is not a defined <see cref="LemmaModelKind"/> value.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent or a value is not finite.</exception>
    public SenseWeights(LemmaModelKind kind, float[] weights, float[] bias, float abstainMargin, int dimension)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(bias);

        // Reject undefined kinds, then validate shapes up front so a corrupt model can never index out of range
        // at classification time
        if (kind is not (LemmaModelKind.Binary or LemmaModelKind.OneVersusRest))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The model kind is not defined.");
        }

        var rows = bias.Length;
        if (kind == LemmaModelKind.Binary ? rows != 1 : rows < 2)
        {
            throw new ArgumentException("A binary model has one weight row; a one-versus-rest model at least two.", nameof(bias));
        }

        if (dimension <= 0 || weights.Length != rows * dimension)
        {
            throw new ArgumentException("Weight and bias shapes do not match the dimension.", nameof(weights));
        }

        if (!float.IsFinite(abstainMargin) || weights.Any(w => !float.IsFinite(w)) || bias.Any(b => !float.IsFinite(b)))
        {
            throw new ArgumentException("Model values must be finite.", nameof(weights));
        }

        Kind = kind;
        Weights = (float[])weights.Clone();
        Bias = (float[])bias.Clone();
        AbstainMargin = abstainMargin;
        Dimension = dimension;
    }

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
    ///     Gets the base abstain margin. A word is answered only when its score margin is at least
    ///     <c>AbstainMargin * AbstainMarginScale</c> (<see cref="WordSenseClassifierOptions.AbstainMarginScale"/>,
    ///     1 by default); a smaller margin makes the classifier abstain, so <c>Details</c> is null and <c>Abstained</c> is true.
    /// </summary>
    public float AbstainMargin { get; }

    /// <summary>
    ///     Gets the feature dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the number of senses these weights score: two for a binary model, otherwise the number of rows.
    /// </summary>
    public int SenseCount => Kind == LemmaModelKind.Binary ? 2 : Bias.Length;

    /// <summary>
    ///     Computes the score of every sense for a normalized feature vector.
    /// </summary>
    /// <remarks>
    ///     A one-versus-rest model yields each row's activation; a binary model with activation
    ///     <c>a</c> yields <c>{-a, +a}</c>. The margin of <see cref="Score"/> is the difference between
    ///     the two highest of these scores.
    /// </remarks>
    /// <param name="feature">The centered, L2-normalized feature vector of length <see cref="Dimension"/>.</param>
    /// <param name="scores">Receives one score per sense; its length must equal <see cref="SenseCount"/>.</param>
    /// <exception cref="ArgumentException">Thrown when a length does not match.</exception>
    public void Scores(ReadOnlySpan<float> feature, Span<float> scores)
    {
        if (feature.Length != Dimension || scores.Length != SenseCount)
        {
            throw new ArgumentException("Feature or score length does not match the model.", nameof(feature));
        }

        if (Kind == LemmaModelKind.Binary)
        {
            var activation = VectorMath.Dot(Weights, feature) + Bias[0];
            scores[0] = -activation;
            scores[1] = activation;
            return;
        }

        for (var row = 0; row < Bias.Length; row++)
        {
            scores[row] = VectorMath.Dot(Weights.AsSpan(row * Dimension, Dimension), feature) + Bias[row];
        }
    }

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
        for (var row = 0; row < Bias.Length; row++)
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
