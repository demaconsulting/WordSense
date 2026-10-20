namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The kind of answer that the margins of a word's fine scores support for a lemma.
/// </summary>
internal enum AnswerKind
{
    /// <summary>
    ///     Neither a sense nor a group can be named with confidence: the answer is the part of speech of the selected
    ///     analysis. The ranked senses of the analysis still show which senses and groups are likely.
    /// </summary>
    PartOfSpeech = 1,

    /// <summary>
    ///     The fine threshold is not met but the best sense is in a group and the margin of that group reaches the group threshold.
    /// </summary>
    Group = 2,

    /// <summary>
    ///     The margin of the best fine sense reaches the fine threshold.
    /// </summary>
    Sense = 3,
}

/// <summary>
///     The trained one-versus-rest linear classifier of one lemma (or of one form chooser) over a dense, unit-length
///     feature vector, with the thresholds that decide the level of an answer.
/// </summary>
/// <remarks>
///     <para>
///     The model has one weight row and one bias per sense (<c>rows × dimension</c> weights, at least two rows). The
///     scores of a feature are the one-versus-rest activations. The fine threshold is the margin (best score minus the
///     second-best score) that the best fine sense needs to be answered at the sense level; the group threshold is the
///     margin of a group, in probability, that the group needs to be answered. Both are measured per
///     lemma by cross-validation and are never negative; the largest finite float (<see cref="Never"/>) means that the
///     level never reached its precision target and is never answered.
///     </para>
///     <para>
///     Instances are immutable once created and thread-safe provided nobody modifies the arrays that are exposed for
///     serialization.
///     </para>
/// </remarks>
internal sealed class LemmaWeights
{
    /// <summary>
    ///     The threshold of a level that never reached its precision target: no finite margin meets it.
    /// </summary>
    public const float Never = float.MaxValue;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaWeights"/> class.
    /// </summary>
    /// <param name="weights">The weight rows, row-major.</param>
    /// <param name="bias">One bias per weight row; at least two rows.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="fineThreshold">The fine threshold: a finite margin that is not negative, or <see cref="Never"/>.</param>
    /// <param name="groupThreshold">The group threshold: a finite margin that is not negative, or <see cref="Never"/>.</param>
    /// <exception cref="ArgumentException">Thrown when the shapes are inconsistent, a value is not finite or a threshold is negative.</exception>
    public LemmaWeights(float[] weights, float[] bias, int dimension, float fineThreshold = Never, float groupThreshold = Never)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(bias);
        if (bias.Length < 2)
        {
            throw new ArgumentException("A lemma needs at least two weight rows.", nameof(bias));
        }

        if (dimension < 0 || weights.Length != bias.Length * dimension)
        {
            throw new ArgumentException("Weight and bias shapes do not match the dimension.", nameof(weights));
        }

        if (weights.Any(w => !float.IsFinite(w)) || bias.Any(b => !float.IsFinite(b)))
        {
            throw new ArgumentException("Model values must be finite.", nameof(weights));
        }

        if (!float.IsFinite(fineThreshold) || fineThreshold < 0 || !float.IsFinite(groupThreshold) || groupThreshold < 0)
        {
            throw new ArgumentException("A threshold must be a finite margin that is not negative.", nameof(fineThreshold));
        }

        Weights = weights;
        Bias = bias;
        Dimension = dimension;
        FineThreshold = fineThreshold;
        GroupThreshold = groupThreshold;
    }

    /// <summary>
    ///     Gets the weight rows, row-major.
    /// </summary>
    public float[] Weights { get; }

    /// <summary>
    ///     Gets one bias per weight row.
    /// </summary>
    public float[] Bias { get; }

    /// <summary>
    ///     Gets the dense dimension.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the fine threshold of the lemma: the margin of the best fine sense that is answered at the sense level.
    /// </summary>
    public float FineThreshold { get; }

    /// <summary>
    ///     Gets the group threshold of the lemma: the margin of a group (in probability) that is answered at the group level.
    /// </summary>
    public float GroupThreshold { get; }

    /// <summary>
    ///     Gets the number of senses these weights score.
    /// </summary>
    public int SenseCount => Bias.Length;

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

        for (var row = 0; row < Bias.Length; row++)
        {
            scores[row] = VectorMath.Dot(Weights.AsSpan(row * Dimension, Dimension), feature) + Bias[row];
        }
    }

    /// <summary>
    ///     Ranks scores: the best sense and the margin to the second best.
    /// </summary>
    /// <param name="scores">The scores of at least one sense.</param>
    /// <returns>
    ///     The index of the best sense (the first one on equal scores, so the inventory order decides) and the margin
    ///     between the best and the second-best score; a single score has an infinite margin.
    /// </returns>
    public static (int Best, float Margin) Rank(ReadOnlySpan<float> scores)
    {
        if (scores.IsEmpty)
        {
            throw new ArgumentException("There is nothing to rank.", nameof(scores));
        }

        var best = 0;
        var bestScore = scores[0];
        var second = float.NegativeInfinity;
        for (var i = 1; i < scores.Length; i++)
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

        return (best, float.IsNegativeInfinity(second) ? float.PositiveInfinity : bestScore - second);
    }

    /// <summary>
    ///     Normalizes scores to probabilities with the softmax.
    /// </summary>
    /// <param name="scores">The scores of at least one sense.</param>
    /// <returns>One probability per score; they are positive and sum to one.</returns>
    public static double[] Softmax(ReadOnlySpan<float> scores)
    {
        if (scores.IsEmpty)
        {
            throw new ArgumentException("There is nothing to normalize.", nameof(scores));
        }

        var max = float.NegativeInfinity;
        foreach (var score in scores)
        {
            max = Math.Max(max, score);
        }

        var probabilities = new double[scores.Length];
        double sum = 0;
        for (var i = 0; i < scores.Length; i++)
        {
            probabilities[i] = Math.Exp(scores[i] - (double)max);
            sum += probabilities[i];
        }

        for (var i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] /= sum;
        }

        return probabilities;
    }

    /// <summary>
    ///     Derives the score of the group of every sense from the probabilities of the senses.
    /// </summary>
    /// <param name="probabilities">The normalized fine probabilities of the candidate senses.</param>
    /// <param name="groups">The group index of each sense (any non-negative number identifies one group), or -1 for a sense that is in no group.</param>
    /// <returns>
    ///     For each sense the score of its group: the sum of the probabilities of the senses of that group. A sense that
    ///     is in no group stands alone, so its score is its own probability.
    /// </returns>
    public static double[] GroupScores(ReadOnlySpan<double> probabilities, ReadOnlySpan<int> groups)
    {
        if (probabilities.Length != groups.Length)
        {
            throw new ArgumentException("There must be one group index per probability.", nameof(groups));
        }

        var scores = new double[probabilities.Length];
        for (var i = 0; i < scores.Length; i++)
        {
            if (groups[i] < 0)
            {
                scores[i] = probabilities[i];
                continue;
            }

            for (var j = 0; j < scores.Length; j++)
            {
                if (groups[j] == groups[i])
                {
                    scores[i] += probabilities[j];
                }
            }
        }

        return scores;
    }

    /// <summary>
    ///     Computes the margin of the group of a sense: its score minus the best score of any other group.
    /// </summary>
    /// <param name="groupScores">The scores from <see cref="GroupScores"/>.</param>
    /// <param name="groups">The group index of each sense, or -1 for a sense that is in no group.</param>
    /// <param name="sense">The index of the sense whose group is measured.</param>
    /// <returns>
    ///     The margin, which is negative when another group is more probable; infinite when no other group exists.
    /// </returns>
    public static double GroupMargin(ReadOnlySpan<double> groupScores, ReadOnlySpan<int> groups, int sense)
    {
        var other = double.NegativeInfinity;
        for (var i = 0; i < groupScores.Length; i++)
        {
            var sameGroup = i == sense || (groups[sense] >= 0 && groups[i] == groups[sense]);
            if (!sameGroup && groupScores[i] > other)
            {
                other = groupScores[i];
            }
        }

        return double.IsNegativeInfinity(other) ? double.PositiveInfinity : groupScores[sense] - other;
    }

    /// <summary>
    ///     Maps a margin to a confidence in <c>[0, 1]</c>, with 0.5 at a threshold.
    /// </summary>
    /// <param name="margin">The margin between the best and the second-best candidate of a level.</param>
    /// <param name="threshold">The threshold of that level.</param>
    /// <returns>
    ///     <c>clamp(0.5 + 0.5 * (margin - t) / max(t, 0.5), 0, 1)</c> with <c>t</c> the non-negative threshold; 0 for a
    ///     NaN margin. The result grows monotonically with the margin and is exactly 0.5 at the threshold. It orders
    ///     margins; the confidence of a result is the probability of its answer, not this value.
    /// </returns>
    public static double Confidence(double margin, double threshold)
    {
        if (double.IsNaN(margin))
        {
            return 0;
        }

        var t = Math.Max(0, threshold);
        var scale = Math.Max(t, 0.5);
        return Math.Clamp(0.5 + (0.5 * (margin - t) / scale), 0, 1);
    }

    /// <summary>
    ///     Decides the kind of answer of a word with the thresholds of this lemma (never shared ones).
    /// </summary>
    /// <param name="fineMargin">The margin of the best fine sense over the best other sense.</param>
    /// <param name="bestInSelectedAnalysis">Whether the best sense lies in the selected analysis.</param>
    /// <param name="bestHasGroup">Whether the best sense is in a group.</param>
    /// <param name="groupMargin">The margin of the group of the best sense (see <see cref="GroupMargin"/>).</param>
    /// <returns>
    ///     <see cref="AnswerKind.Sense"/> when <paramref name="fineMargin"/> reaches <see cref="FineThreshold"/> and the
    ///     best sense is in the selected analysis; otherwise <see cref="AnswerKind.Group"/> when the best sense is in
    ///     that analysis and in a group and <paramref name="groupMargin"/> reaches <see cref="GroupThreshold"/>;
    ///     otherwise <see cref="AnswerKind.PartOfSpeech"/>. A threshold that is <see cref="Never"/> is not reached by
    ///     any finite margin, so its level is never answered.
    /// </returns>
    public AnswerKind Level(double fineMargin, bool bestInSelectedAnalysis, bool bestHasGroup, double groupMargin)
    {
        if (!bestInSelectedAnalysis)
        {
            return AnswerKind.PartOfSpeech;
        }

        if (FineThreshold < Never && fineMargin >= FineThreshold)
        {
            return AnswerKind.Sense;
        }

        return bestHasGroup && GroupThreshold < Never && groupMargin >= GroupThreshold ? AnswerKind.Group : AnswerKind.PartOfSpeech;
    }
}

/// <summary>
///     The trained classifier of a form that chooses among the analyses of one word, as stored in a weights blob.
/// </summary>
/// <param name="Form">The lower-case form (a word that belongs to several lemmas or tenses: <c>left</c>, <c>read</c>).</param>
/// <param name="Labels">
///     The labels in score order: a label is the lemma of the analyses it stands for (<c>leave</c>), or, when analyses
///     of one lemma differ only in features, <c>lemma/features</c> (<c>read/Tense=Past+VerbForm=Part</c>).
/// </param>
/// <param name="Weights">The classifier over the labels; its fine threshold is the threshold of the chooser.</param>
internal sealed record FormChooser(string Form, IReadOnlyList<string> Labels, LemmaWeights Weights);

/// <summary>
///     The senses and trained weights of one lemma, as stored in a weights blob.
/// </summary>
/// <param name="Lemma">The trained lemma.</param>
/// <param name="SenseIds">The ids of the fine senses (the sense ids of the public API) in score order: row <c>i</c> of the weights scores sense <c>i</c>.</param>
/// <param name="Weights">The trained classifier with the thresholds of the lemma; it scores exactly these senses.</param>
/// <param name="Choosers">The choosers of the forms stored with this lemma (the first lemma of the form in ordinal order).</param>
internal sealed record WeightedLemma(string Lemma, IReadOnlyList<string> SenseIds, LemmaWeights Weights, IReadOnlyList<FormChooser> Choosers)
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WeightedLemma"/> record without form choosers.
    /// </summary>
    /// <param name="lemma">The trained lemma.</param>
    /// <param name="senseIds">The sense ids in score order.</param>
    /// <param name="weights">The trained classifier.</param>
    public WeightedLemma(string lemma, IReadOnlyList<string> senseIds, LemmaWeights weights)
        : this(lemma, senseIds, weights, [])
    {
    }
}
