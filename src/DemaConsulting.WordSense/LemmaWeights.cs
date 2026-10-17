namespace DemaConsulting.WordSense;

/// <summary>
///     How the scores of a <see cref="LemmaWeights"/> derive from its weight rows.
/// </summary>
internal enum LemmaModelKind
{
    /// <summary>
    ///     A single weight row for a two-sense lemma: the score of the second sense is the row's activation and the
    ///     score of the first sense is its negation.
    /// </summary>
    Binary = 1,

    /// <summary>
    ///     One weight row per sense (one-versus-rest): each sense scores its own row's activation.
    /// </summary>
    OneVersusRest = 2,
}

/// <summary>
///     The trained linear classifier of one lemma, over either a dense feature vector or a sparse set of feature ids.
/// </summary>
/// <remarks>
///     A dense model has <c>rows × dimension</c> weights and scores a unit-length vector of that dimension. A sparse
///     model has <c>rows × featureIds.Length</c> weights; its feature space is the sorted <see cref="FeatureIds"/> and a
///     context scores the ids it contains, each with the same value. Instances are immutable once created and
///     thread-safe provided nobody modifies the arrays that are exposed for serialization.
/// </remarks>
internal sealed class LemmaWeights
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaWeights"/> class.
    /// </summary>
    /// <param name="kind">How scores derive from the weight rows.</param>
    /// <param name="weights">The weight rows, row-major.</param>
    /// <param name="bias">One bias per weight row.</param>
    /// <param name="abstainMargin">The abstain margin (finite, not negative).</param>
    /// <param name="dimension">The dense dimension, or the number of feature ids for a sparse model.</param>
    /// <param name="featureIds">The strictly ascending feature ids of a sparse model, or null for a dense model.</param>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent or a value is not finite.</exception>
    public LemmaWeights(LemmaModelKind kind, float[] weights, float[] bias, float abstainMargin, int dimension, int[]? featureIds = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(bias);
        var rows = bias.Length;
        if (kind == LemmaModelKind.Binary ? rows != 1 : kind != LemmaModelKind.OneVersusRest || rows < 2)
        {
            throw new ArgumentException("A binary model has one weight row; a one-versus-rest model at least two.", nameof(bias));
        }

        if (dimension < 0 || weights.Length != rows * dimension || (featureIds is not null && featureIds.Length != dimension))
        {
            throw new ArgumentException("Weight and bias shapes do not match the dimension.", nameof(weights));
        }

        if (!float.IsFinite(abstainMargin) || weights.Any(w => !float.IsFinite(w)) || bias.Any(b => !float.IsFinite(b)))
        {
            throw new ArgumentException("Model values must be finite.", nameof(weights));
        }

        Kind = kind;
        Weights = weights;
        Bias = bias;
        AbstainMargin = Math.Max(0f, abstainMargin);
        Dimension = dimension;
        FeatureIds = featureIds;
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
    ///     Gets the abstain margin: the score margin at which the confidence is 0.5.
    /// </summary>
    public float AbstainMargin { get; }

    /// <summary>
    ///     Gets the dense dimension, or the number of feature ids of a sparse model.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the strictly ascending feature ids of a sparse model, or null for a dense model.
    /// </summary>
    public int[]? FeatureIds { get; }

    /// <summary>
    ///     Gets the number of senses these weights score.
    /// </summary>
    public int SenseCount => Kind == LemmaModelKind.Binary ? 2 : Bias.Length;

    /// <summary>
    ///     Computes the score of every sense for a dense feature vector.
    /// </summary>
    /// <param name="feature">The unit-length feature vector of length <see cref="Dimension"/>.</param>
    /// <param name="scores">Receives one score per sense.</param>
    /// <exception cref="ArgumentException">Thrown when a length does not match or the model is sparse.</exception>
    public void Scores(ReadOnlySpan<float> feature, Span<float> scores)
    {
        if (FeatureIds is not null || feature.Length != Dimension || scores.Length != SenseCount)
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
    ///     Computes the score of every sense for a set of active sparse features.
    /// </summary>
    /// <param name="ids">The active feature ids; ids outside this model's feature space are ignored.</param>
    /// <param name="value">The value every active feature carries.</param>
    /// <param name="scores">Receives one score per sense.</param>
    /// <exception cref="ArgumentException">Thrown when the model is dense or the score length does not match.</exception>
    public void ScoresSparse(ReadOnlySpan<int> ids, float value, Span<float> scores)
    {
        if (FeatureIds is null || scores.Length != SenseCount)
        {
            throw new ArgumentException("The model is not sparse or the score length does not match.", nameof(scores));
        }

        var rows = Bias.Length;
        var activation = new float[rows];
        foreach (var id in ids)
        {
            var column = Array.BinarySearch(FeatureIds, id);
            if (column < 0)
            {
                continue;
            }

            for (var row = 0; row < rows; row++)
            {
                activation[row] += Weights[(row * Dimension) + column] * value;
            }
        }

        if (Kind == LemmaModelKind.Binary)
        {
            var a = activation[0] + Bias[0];
            scores[0] = -a;
            scores[1] = a;
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            scores[row] = activation[row] + Bias[row];
        }
    }

    /// <summary>
    ///     Ranks scores: the best sense and the margin to the second best.
    /// </summary>
    /// <param name="scores">The scores of at least two senses.</param>
    /// <returns>The index of the best sense and the margin between the best and the second-best score.</returns>
    public static (int Best, float Margin) Rank(ReadOnlySpan<float> scores)
    {
        // A binary model has the scores {-a, +a}; a non-negative activation selects the second sense
        if (scores.Length == 2)
        {
            return (scores[1] >= scores[0] ? 1 : 0, Math.Abs(scores[1] - scores[0]));
        }

        var best = 0;
        var bestScore = float.NegativeInfinity;
        var second = float.NegativeInfinity;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i] > bestScore)
            {
                second = bestScore;
                bestScore = scores[i];
                best = i;
            }
            else if (scores[i] > second)
            {
                second = scores[i];
            }
        }

        return (best, bestScore - second);
    }

    /// <summary>
    ///     Maps a score margin to a confidence in <c>[0, 1]</c>.
    /// </summary>
    /// <param name="margin">The margin between the best and the second-best score.</param>
    /// <param name="abstainMargin">The lemma's abstain margin.</param>
    /// <returns>
    ///     <c>clamp(0.5 + 0.5 * (margin - a) / max(a, 0.5), 0, 1)</c> with <c>a</c> the non-negative abstain margin; 0 for a NaN margin.
    /// </returns>
    public static double Confidence(double margin, double abstainMargin)
    {
        if (double.IsNaN(margin))
        {
            return 0;
        }

        var a = Math.Max(0, abstainMargin);
        var scale = Math.Max(a, 0.5);
        return Math.Clamp(0.5 + (0.5 * (margin - a) / scale), 0, 1);
    }
}
