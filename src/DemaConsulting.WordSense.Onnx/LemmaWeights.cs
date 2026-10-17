namespace DemaConsulting.WordSense.Onnx;

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
///     The trained linear classifier of one lemma over a dense, unit-length feature vector.
/// </summary>
/// <remarks>
///     The model has <c>rows × dimension</c> weights. Instances are immutable once created and thread-safe provided
///     nobody modifies the arrays that are exposed for serialization.
/// </remarks>
internal sealed class LemmaWeights
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaWeights"/> class.
    /// </summary>
    /// <param name="kind">How scores derive from the weight rows.</param>
    /// <param name="weights">The weight rows, row-major.</param>
    /// <param name="bias">One bias per weight row.</param>
    /// <param name="abstainMargin">The abstain margin (finite); a negative value is treated as 0.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent or a value is not finite.</exception>
    public LemmaWeights(LemmaModelKind kind, float[] weights, float[] bias, float abstainMargin, int dimension)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(bias);
        var rows = bias.Length;
        if (kind == LemmaModelKind.Binary ? rows != 1 : kind != LemmaModelKind.OneVersusRest || rows < 2)
        {
            throw new ArgumentException("A binary model has one weight row; a one-versus-rest model at least two.", nameof(bias));
        }

        if (dimension < 0 || weights.Length != rows * dimension)
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
    ///     Gets the dense dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the number of senses these weights score.
    /// </summary>
    public int SenseCount => Kind == LemmaModelKind.Binary ? 2 : Bias.Length;

    /// <summary>
    ///     Computes the score of every sense for a dense feature vector.
    /// </summary>
    /// <param name="feature">The unit-length feature vector of length <see cref="Dimension"/>.</param>
    /// <param name="scores">Receives one score per sense.</param>
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
    ///     <c>clamp(0.5 + 0.5 * (margin - a) / max(a, 0.5), 0, 1)</c> with <c>a</c> the non-negative abstain margin; 0 for
    ///     a NaN margin. The result grows monotonically with the margin and is exactly 0.5 at the abstain margin.
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

/// <summary>
///     The identity of one weighted sense: the same triple that identifies a sense in the word lexicon.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Meaning">The pronunciation meaning, or null when the lemma is not a heteronym.</param>
internal sealed record WeightedWord(string Lemma, PartOfSpeech PartOfSpeech, string? Meaning);

/// <summary>
///     The senses and trained weights of one lemma, as stored in a weights blob.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Senses">The senses in score order.</param>
/// <param name="Weights">The trained classifier; it scores exactly these senses.</param>
internal sealed record WeightedLemma(string Lemma, IReadOnlyList<WeightedWord> Senses, LemmaWeights Weights);
