namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: describes how the scores of a <see cref="LemmaModel"/> are computed from its weight rows.
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
///     Advanced: the trained linear classifier for one ambiguous lemma. Applications normally use
///     <see cref="IWordSenseClassifier"/>; read this to see a lemma's <see cref="AbstainMargin"/>.
/// </summary>
/// <remarks>
///     Instances are thread-safe provided no caller modifies <see cref="Weights"/> or <see cref="Bias"/>, which
///     are exposed as arrays for speed and must be treated as read-only; the constructor copies the arrays it
///     is given. The order in which the senses are
///     supplied defines the order of the weight rows; sense names (<see cref="SenseDefinition.Name"/>,
///     exposed as <see cref="WordSense.Sense"/>) must be unique.
/// </remarks>
public sealed class LemmaModel
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaModel"/> class.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="senses">The sense definitions (name and tags), at least two; their order defines the weight-row order.</param>
    /// <param name="kind">How scores derive from the weight rows.</param>
    /// <param name="weights">The weight rows, row-major, <c>rows × dimension</c> values.</param>
    /// <param name="bias">One bias per weight row.</param>
    /// <param name="abstainMargin">
    ///     The base abstain margin. A word is answered only when its score margin is at least
    ///     <c>AbstainMargin * AbstainMarginScale</c> (see <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>);
    ///     otherwise the classifier abstains and reports null. Must be finite.
    /// </param>
    /// <param name="dimension">The feature dimension.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when an argument or an element of <paramref name="senses"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="kind"/> is not a defined <see cref="LemmaModelKind"/> value.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent or a value is not finite.</exception>
    public LemmaModel(
        string lemma,
        IReadOnlyList<SenseDefinition> senses,
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

        // Reject undefined kinds, then validate shapes up front so a corrupt model can never index out of range
        // at classification time
        if (kind is not (LemmaModelKind.Binary or LemmaModelKind.OneVersusRest))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The model kind is not defined.");
        }

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

        // Every sense needs valid tags, and names must be unique
        foreach (var definition in senses)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();
        }

        if (senses.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != senses.Count)
        {
            throw new ArgumentException("Sense names must be unique.", nameof(senses));
        }

        Lemma = lemma;

        // Exactly one shared record per (lemma, sense), created here
        Senses = senses.Select(s => new WordSense(lemma, s.Name, s.Tags, s.Group)).ToArray();
        Kind = kind;
        Weights = (float[])weights.Clone();
        Bias = (float[])bias.Clone();
        AbstainMargin = abstainMargin;
        Dimension = dimension;
    }

    /// <summary>
    ///     Gets the lower-case lemma this model classifies.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the senses, one shared record each, in the supplied order (which defines the order of the weight rows).
    /// </summary>
    public IReadOnlyList<WordSense> Senses { get; }

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
    ///     1 by default); a smaller margin makes the classifier abstain, so the result is null.
    /// </summary>
    public float AbstainMargin { get; }

    /// <summary>
    ///     Gets the feature dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Computes the score of every sense for a normalized feature vector.
    /// </summary>
    /// <remarks>
    ///     A one-versus-rest model yields each row's activation; a binary model with activation
    ///     <c>a</c> yields <c>{-a, +a}</c>. The margin of <see cref="Score"/> is the difference between
    ///     the two highest of these scores.
    /// </remarks>
    /// <param name="feature">The centered, L2-normalized feature vector of length <see cref="Dimension"/>.</param>
    /// <param name="scores">Receives one score per sense; its length must equal the sense count.</param>
    /// <exception cref="ArgumentException">Thrown when a length does not match.</exception>
    public void Scores(ReadOnlySpan<float> feature, Span<float> scores)
    {
        if (feature.Length != Dimension || scores.Length != Senses.Count)
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

        for (var row = 0; row < Senses.Count; row++)
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
