using System.Globalization;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     Settings of the real-text calibration of the thresholds.
/// </summary>
/// <param name="TargetPrecision">The precision the answers of a level must reach on the calibration rows.</param>
/// <param name="MinOwnRows">The number of calibration rows a lemma needs for a fine threshold of its own.</param>
/// <param name="MinOwnAnswered">The number of answers a threshold of its own must keep.</param>
/// <param name="MinPooledAnswered">The number of answers the pooled shift of a level must keep at least, and the number of group answers that are needed to verify the group thresholds.</param>
/// <param name="MinPooledShare">The share of all pooled answers the pooled shift of a level must keep at least (the larger of this and <paramref name="MinPooledAnswered"/> counts): the target is checked, and a fallback chosen, only on a sample so large that a handful of rows cannot decide it.</param>
internal sealed record CalibrationOptions(double TargetPrecision = 0.95, int MinOwnRows = 20, int MinOwnAnswered = 5, int MinPooledAnswered = 30, double MinPooledShare = 0.3);

/// <summary>
///     What a real-text calibration did.
/// </summary>
/// <param name="Rows">The calibration rows scored (rows of trained lemmas).</param>
/// <param name="Lemmas">The lemmas that have calibration rows.</param>
/// <param name="OwnThresholds">The lemmas whose threshold came from their own rows.</param>
/// <param name="FineShift">The pooled shift of the fine thresholds.</param>
/// <param name="FineReached">Whether the shift reached the precision target.</param>
/// <param name="AnswersBefore">The sense answers of the calibration rows with the first thresholds.</param>
/// <param name="CorrectBefore">The correct ones among them.</param>
/// <param name="AnswersAfter">The sense answers with the final thresholds.</param>
/// <param name="CorrectAfter">The correct ones among them.</param>
/// <param name="GroupShift">The pooled shift of the group thresholds.</param>
/// <param name="GroupAnswers">The calibration group answers that could verify the group thresholds.</param>
/// <param name="GroupVerified">Whether the group thresholds were moved (enough answers) or kept unverified.</param>
/// <param name="GroupReached">Whether the group shift reached the precision target (false when the group thresholds were kept unverified).</param>
internal sealed record CalibrationReport(
    int Rows,
    int Lemmas,
    int OwnThresholds,
    double FineShift,
    bool FineReached,
    int AnswersBefore,
    int CorrectBefore,
    int AnswersAfter,
    int CorrectAfter,
    double GroupShift,
    int GroupAnswers,
    bool GroupVerified,
    bool GroupReached)
{
    /// <summary>
    ///     Describes the calibration in one line for the training summary. The sense answers before and after cover every
    ///     observed calibration row, the lemmas with a threshold of their own included, not only the pooled rows.
    /// </summary>
    /// <returns>The line.</returns>
    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        string P(int correct, int answers) => answers == 0 ? "n/a" : (100.0 * correct / answers).ToString("0.00", inv) + "%";
        string group;
        if (!GroupVerified)
        {
            group = "unverified, kept";
        }
        else
        {
            group = GroupReached ? "verified" : "verified, target not reached";
        }
        return $"real-text calibration: {Rows.ToString(inv)} rows of {Lemmas.ToString(inv)} lemmas; fine pooled shift {FineShift.ToString("0.000", inv)}" +
               $" ({(FineReached ? "reaches the target" : "target not reached")}), {OwnThresholds.ToString(inv)} lemmas with a threshold of their own;" +
               $" sense answers {AnswersBefore.ToString(inv)} at {P(CorrectBefore, AnswersBefore)} before, {AnswersAfter.ToString(inv)} at {P(CorrectAfter, AnswersAfter)} after (all calibration rows);" +
               $" group shift {GroupShift.ToString("0.000", inv)} ({group} on {GroupAnswers.ToString(inv)} answers).";
    }
}

/// <summary>
///     Moves the thresholds of the trained lemmas to the precision target measured on real-text calibration sentences.
/// </summary>
/// <remarks>
///     <para>
///     The first thresholds (<see cref="Thresholds"/>) come from cross-validation on the anchors, which are balanced
///     and partly written, so on real text the answers above them fall short of the target. The calibration rows are real
///     held-out sentences of the calibration documents (<see cref="RealSplit"/>); they are scored by the final weights
///     and never trained on. A lemma with at least <see cref="CalibrationOptions.MinOwnRows"/> rows gets a fine threshold
///     of its own; every lemma with a finite first threshold gets the first threshold plus one pooled shift, the lowest
///     non-negative shift at which the answers of all lemmas without a threshold of their own reach the target while at
///     least <see cref="CalibrationOptions.MinPooledShare"/> of those answers are kept (or, when none does, the largest
///     shift that keeps that share: the target is then reported as not reached). The share makes the choice
///     robust: the precision of the answers in the far tail rests on a few dozen rows, so one wrong row there used to
///     move the shift by several units, while a quarter or more of the answers does not depend on a handful of rows
///     and the fallback is an order statistic of the distances. The final
///     threshold is the higher of the shifted first threshold and the own one, so calibration only raises a threshold
///     and a level that was never answered stays never answered. A calibration row whose sense has no anchor is a wrong
///     answer. The group thresholds move by the same rule (the same minimum count and share, the same fallback, reported
///     as "target not reached" in the same way) over the rows that fall through the final fine threshold and have a
///     best sense in a group, only when enough such answers exist to verify them.
///     </para>
///     <para>
///     The function is deterministic and free of side effects.
///     </para>
/// </remarks>
internal static class RealCalibrator
{
    /// <summary>
    ///     The lemma-level precision target the trainer calibrates to by default: two points above the 95 percent aimed at on
    ///     real test text, because the precision of the whole pipeline (analysis and chooser errors) measured on the same
    ///     sentences is about two points below the lemma-level precision.
    /// </summary>
    public const double DefaultLemmaTarget = 0.97;

    private const double Tolerance = 1e-9;

    /// <summary>
    ///     Calibrates the thresholds of every lemma on the calibration rows.
    /// </summary>
    /// <param name="data">The trained weights with their first thresholds.</param>
    /// <param name="rows">The calibration rows.</param>
    /// <param name="features">The centered, unit-length feature of each row.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The weights with the final thresholds, and the report.</returns>
    /// <exception cref="ArgumentException">Thrown when there is not one feature per row.</exception>
    public static (WeightsData Data, CalibrationReport Report) Apply(WeightsData data, IReadOnlyList<CorpusRow> rows, IReadOnlyList<float[]> features, CalibrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(options);
        if (rows.Count != features.Count)
        {
            throw new ArgumentException("There must be one feature per calibration row.", nameof(features));
        }

        var lemmas = data.Lemmas.ToDictionary(l => l.Lemma, StringComparer.Ordinal);
        var groupsOf = lemmas.ToDictionary(l => l.Key, l => GroupsOf(l.Value.SenseIds), StringComparer.Ordinal);
        var observed = new List<Observation>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (lemmas.TryGetValue(rows[i].Lemma, out var lemma))
            {
                observed.Add(Observe(lemma, groupsOf[lemma.Lemma], rows[i], features[i]));
            }
        }

        var own = new TrainingOptions(TargetPrecision: options.TargetPrecision, MinAnswered: options.MinOwnAnswered);
        var ownThreshold = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var group in observed.GroupBy(o => o.Lemma, StringComparer.Ordinal))
        {
            var list = group.ToList();
            if (list.Count >= options.MinOwnRows && lemmas[group.Key].Weights.FineThreshold < LemmaWeights.Never)
            {
                ownThreshold[group.Key] = Thresholds.ChooseThreshold(list.Select(o => ((double)o.Margin, o.Correct)).ToList(), own);
            }
        }

        // The pooled shift is fitted on the lemmas that have a finite first threshold and none of their own
        var pooled = observed.Where(o => !ownThreshold.ContainsKey(o.Lemma) && lemmas[o.Lemma].Weights.FineThreshold < LemmaWeights.Never)
            .Select(o => ((double)o.Margin - lemmas[o.Lemma].Weights.FineThreshold, o.Correct)).ToList();
        var (fineShift, fineReached) = ChooseShift(pooled, options.TargetPrecision, options.MinPooledAnswered, options.MinPooledShare);

        var fine = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var lemma in lemmas.Values)
        {
            var first = lemma.Weights.FineThreshold;
            if (first >= LemmaWeights.Never)
            {
                fine[lemma.Lemma] = first;
                continue;
            }

            var shifted = RoundDown((double)first + fineShift, first);
            fine[lemma.Lemma] = ownThreshold.TryGetValue(lemma.Lemma, out var o) ? Math.Max(shifted, o) : shifted;
        }

        // The group answers are the rows that fall through the final fine threshold and whose best sense is in a group
        var groupRows = observed.Where(o => !double.IsNaN(o.GroupMargin) && o.Margin < fine[o.Lemma] && lemmas[o.Lemma].Weights.GroupThreshold < LemmaWeights.Never)
            .Select(o => (Distance: o.GroupMargin - lemmas[o.Lemma].Weights.GroupThreshold, Correct: o.GroupCorrect)).Where(a => a.Distance >= 0).ToList();
        var groupVerified = groupRows.Count >= options.MinPooledAnswered;
        var (groupShift, groupReached) = groupVerified ? ChooseShift(groupRows, options.TargetPrecision, options.MinPooledAnswered, options.MinPooledShare) : (0.0, false);

        var updated = data.Lemmas.Select(l =>
        {
            var w = l.Weights;
            var group = w.GroupThreshold >= LemmaWeights.Never || !groupVerified ? w.GroupThreshold : RoundDown((double)w.GroupThreshold + groupShift, w.GroupThreshold);
            return l with { Weights = new LemmaWeights(w.Weights, w.Bias, w.Dimension, fine[l.Lemma], group) };
        }).ToList();

        int before = 0, correctBefore = 0, after = 0, correctAfter = 0;
        foreach (var o in observed)
        {
            if (o.Margin >= lemmas[o.Lemma].Weights.FineThreshold)
            {
                before++;
                correctBefore += o.Correct ? 1 : 0;
            }

            if (o.Margin >= fine[o.Lemma])
            {
                after++;
                correctAfter += o.Correct ? 1 : 0;
            }
        }

        var report = new CalibrationReport(
            observed.Count, observed.Select(o => o.Lemma).Distinct(StringComparer.Ordinal).Count(), ownThreshold.Count, fineShift, fineReached,
            before, correctBefore, after, correctAfter, groupShift, groupRows.Count, groupVerified, groupReached);
        return (data with { Lemmas = updated }, report);
    }

    /// <summary>
    ///     Chooses the lowest non-negative shift at which the answers reach the precision target, on enough answers.
    /// </summary>
    /// <remarks>
    ///     An answer has a distance <c>d</c> (its margin minus its first threshold) and is kept by a shift <c>s</c> when
    ///     <c>d &gt;= s</c>; only answers with a finite <c>d &gt;= 0</c> are answers at all (the usable answers, <c>n</c> of them). A shift counts only when it keeps at
    ///     least <c>max(minAnswered, ceil(minShare * n))</c> of the <c>n</c> answers: the precision of a thinner tail
    ///     depends on a few rows, so a crossing of the target there is an artifact of the noise. When no shift reaches
    ///     the target on that many answers, the largest shift that keeps them is returned and the second value is
    ///     false (the target is not reached; that shift is an order statistic of the distances and does not depend on
    ///     which of the answers are right). Fewer answers than <paramref name="minAnswered"/> give shift 0.
    /// </remarks>
    /// <param name="answers">The distance and the correctness of each answer.</param>
    /// <param name="target">The precision target.</param>
    /// <param name="minAnswered">The minimum number of answers a shift must keep.</param>
    /// <param name="minShare">The minimum share of all answers a shift must keep (0 for none).</param>
    /// <returns>The shift and whether it reaches the target.</returns>
    internal static (double Shift, bool Reached) ChooseShift(IReadOnlyList<(double Distance, bool Correct)> answers, double target, int minAnswered, double minShare = 0)
    {
        var usable = answers.Where(a => double.IsFinite(a.Distance) && a.Distance >= 0).OrderBy(a => a.Distance).ToList();
        if (usable.Count < minAnswered)
        {
            return (0, false);
        }

        var minimum = Math.Max(minAnswered, (int)Math.Ceiling((minShare * usable.Count) - Tolerance));
        var correct = usable.Count(a => a.Correct);
        var lastShift = 0.0;
        for (var i = 0; i < usable.Count; i++)
        {
            var total = usable.Count - i;
            if (total < minimum)
            {
                break;
            }

            var startsGroup = i == 0 || usable[i].Distance > usable[i - 1].Distance;
            if (startsGroup)
            {
                var shift = i == 0 ? 0 : usable[i].Distance;
                lastShift = shift;
                if (correct >= (target * total) - Tolerance)
                {
                    return (shift, true);
                }
            }

            correct -= usable[i].Correct ? 1 : 0;
        }

        return (lastShift, false);
    }

    private static Observation Observe(WeightedLemma lemma, int[] groups, CorpusRow row, float[] feature)
    {
        var scores = new float[lemma.SenseIds.Count];
        lemma.Weights.Scores(feature, scores);
        var (best, margin) = LemmaWeights.Rank(scores);
        var label = lemma.SenseIds.ToList().IndexOf(row.SenseId);
        var groupMargin = double.NaN;
        var groupCorrect = false;
        if (groups[best] >= 0)
        {
            var groupScores = LemmaWeights.GroupScores(LemmaWeights.Softmax(scores), groups);
            groupMargin = LemmaWeights.GroupMargin(groupScores, groups, best);
            groupCorrect = label >= 0 && groups[label] == groups[best];
        }

        return new Observation(lemma.Lemma, margin, best == label, groupMargin, groupCorrect);
    }

    private static int[] GroupsOf(IReadOnlyList<string> senseIds)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new int[senseIds.Count];
        for (var i = 0; i < senseIds.Count; i++)
        {
            string? groupId = WordLexicon.TryGetSense(senseIds[i], out var sense) ? sense.GroupId : null;
            if (groupId is null)
            {
                groups[i] = -1;
                continue;
            }

            if (!indexes.TryGetValue(groupId, out var index))
            {
                index = indexes.Count;
                indexes[groupId] = index;
            }

            groups[i] = index;
        }

        return groups;
    }

    /// <summary>
    ///     Converts a value to the float that is stored, never above the value and never below the first threshold.
    /// </summary>
    /// <param name="value">The shifted threshold.</param>
    /// <param name="floor">The first threshold.</param>
    /// <returns>The float.</returns>
    private static float RoundDown(double value, float floor)
    {
        var result = (float)value;
        if (result > value)
        {
            result = MathF.BitDecrement(result);
        }

        return Math.Max(result, floor);
    }

    private sealed record Observation(string Lemma, float Margin, bool Correct, double GroupMargin, bool GroupCorrect);
}
