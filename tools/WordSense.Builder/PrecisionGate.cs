using System.Globalization;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     The fine top-1 accuracy of the same rows scored with full-precision weights and with the weights as stored.
/// </summary>
/// <param name="Rows">The number of rows scored: the held-out rows whose lemma has weights.</param>
/// <param name="FullCorrect">The rows whose best sense is the true one with the full-precision weights.</param>
/// <param name="ReducedCorrect">The rows whose best sense is the true one with the half-precision weights read back from the blob.</param>
internal readonly record struct GateResult(int Rows, int FullCorrect, int ReducedCorrect)
{
    /// <summary>
    ///     Gets the fine accuracy of the full-precision weights, in percent (0 without rows).
    /// </summary>
    public double FullAccuracy => Rows == 0 ? 0 : 100.0 * FullCorrect / Rows;

    /// <summary>
    ///     Gets the fine accuracy of the half-precision weights, in percent (0 without rows).
    /// </summary>
    public double ReducedAccuracy => Rows == 0 ? 0 : 100.0 * ReducedCorrect / Rows;

    /// <summary>
    ///     Gets the accuracy that the reduction lost, in percentage points (negative when it gained).
    /// </summary>
    public double LossPoints => FullAccuracy - ReducedAccuracy;
}

/// <summary>
///     The accuracy gate of the half-precision weights: storing the weights as 16-bit floats must change the fine
///     top-1 accuracy by no more than <see cref="MaxDifferencePoints"/> percentage points.
/// </summary>
/// <remarks>
///     The weights are stored in half precision to halve the blob. The gate scores held-out rows with the
///     full-precision weights of the trainer and with the weights that the real reader returns from the written blob,
///     on the same features, and compares the share of rows whose best sense is the true one. The held-out rows are
///     read here only to measure the reduction; they never influence a weight or a threshold. A row counts only when the
///     classifier scores its form (the encoder runs for it and at least two senses of its lemma are candidates), and only
///     the candidate senses of the form are ranked, as at run time (no closed-class sense).
/// </remarks>
internal static class PrecisionGate
{
    /// <summary>
    ///     The largest accepted difference of the fine accuracy, in percentage points.
    /// </summary>
    public const double MaxDifferencePoints = 0.2;

    /// <summary>
    ///     Measures the fine accuracy of two sets of weights on the same rows.
    /// </summary>
    /// <param name="full">The full-precision weights.</param>
    /// <param name="reduced">The weights read back from the written blob.</param>
    /// <param name="rows">The held-out rows.</param>
    /// <param name="pooled">The pooled encoder vector of each row, parallel to <paramref name="rows"/>.</param>
    /// <returns>The counts; rows whose lemma has no weights, and rows whose form the classifier does not score, are left out. A sense without a column counts as wrong for both.</returns>
    /// <exception cref="ArgumentException">Thrown when the lists differ in length.</exception>
    public static GateResult Measure(WeightsData full, WeightsData reduced, IReadOnlyList<CorpusRow> rows, IReadOnlyList<double[]> pooled)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(reduced);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(pooled);
        if (rows.Count != pooled.Count)
        {
            throw new ArgumentException("There must be one vector per row.", nameof(pooled));
        }

        // The classifier reads the weights as a table; the form choosers decide which forms it scores at all
        var fullTable = new WeightedLemmaTable(full, strict: false);
        var reducedByLemma = reduced.Lemmas.ToDictionary(l => l.Lemma, StringComparer.Ordinal);
        int counted = 0, fullCorrect = 0, reducedCorrect = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (fullTable.Find(rows[i].Lemma) is not { } fullLemma || !reducedByLemma.TryGetValue(rows[i].Lemma, out var reducedLemma))
            {
                continue;
            }

            // A form that the classifier does not score (the encoder does not run, or fewer than two candidate senses) is not counted
            var (encoderRuns, columns) = OnnxClassifier.CandidatesOf(rows[i].Words[rows[i].TargetWordIndex], rows[i].Lemma, fullTable);
            if (!encoderRuns || columns.Count < 2)
            {
                continue;
            }

            counted++;
            fullCorrect += IsRight(fullLemma.Weights, FeatureExtractor.Center(pooled[i], full.Mean), columns, rows[i].SenseId) ? 1 : 0;
            reducedCorrect += IsRight(reducedLemma, FeatureExtractor.Center(pooled[i], reduced.Mean), columns, rows[i].SenseId) ? 1 : 0;
        }

        return new GateResult(counted, fullCorrect, reducedCorrect);
    }

    /// <summary>
    ///     Describes a measurement on one line.
    /// </summary>
    /// <param name="result">The measurement.</param>
    /// <returns>The text with both accuracies and the difference.</returns>
    public static string Describe(GateResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Rows} held-out rows: fine top-1 {result.FullAccuracy:F2}% with full-precision weights, {result.ReducedAccuracy:F2}% with the stored half-precision weights (difference {result.LossPoints:F2} points, gate {MaxDifferencePoints:F1}).");

    /// <summary>
    ///     Checks a measurement against the gate.
    /// </summary>
    /// <param name="result">The measurement.</param>
    /// <exception cref="InvalidDataException">Thrown when the accuracies differ by more than <see cref="MaxDifferencePoints"/>.</exception>
    public static void Enforce(GateResult result)
    {
        if (Math.Abs(result.LossPoints) > MaxDifferencePoints)
        {
            throw new InvalidDataException(
                "The half-precision gate failed: " + Describe(result) + " The weights are not written; store them in a wider format or retrain.");
        }
    }

    /// <summary>
    ///     Tells whether the best sense of a lemma for a feature is the true sense, ranking only the candidate senses that
    ///     the classifier scores for the form of the word (<see cref="OnnxClassifier.CandidatesOf"/>), as inference does: a
    ///     closed-class sense is never the best sense.
    /// </summary>
    /// <param name="lemma">The weights of the lemma.</param>
    /// <param name="feature">The feature, or null when the vector has no magnitude.</param>
    /// <param name="columns">The candidate columns of the form of the row, at least two.</param>
    /// <param name="senseId">The true sense.</param>
    /// <returns>True when the best candidate column is the true sense.</returns>
    private static bool IsRight(WeightedLemma lemma, float[]? feature, IReadOnlyList<int> columns, string senseId)
    {
        if (feature is null || feature.Length != lemma.Weights.Dimension)
        {
            return false;
        }

        var scores = new float[lemma.Weights.SenseCount];
        lemma.Weights.Scores(feature, scores);
        var best = columns[LemmaWeights.Rank(columns.Select(c => scores[c]).ToArray()).Best];
        return string.Equals(lemma.SenseIds[best], senseId, StringComparison.Ordinal);
    }
}
