using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     The thresholds chosen for one lemma.
/// </summary>
/// <param name="Fine">The fine threshold: the margin of the best sense that is answered at the sense level, or <see cref="LemmaWeights.Never"/>.</param>
/// <param name="Group">The group threshold: the margin of a group that is answered at the group level, or <see cref="LemmaWeights.Never"/>.</param>
internal readonly record struct LemmaThresholds(float Fine, float Group);

/// <summary>
///     Chooses the fine threshold and the group threshold of a lemma by cross-validation on its anchors.
/// </summary>
/// <remarks>
///     <para>
///     The anchors of the lemma are split into <see cref="TrainingOptions.Folds"/> folds by a seeded, stratified
///     shuffle: the rows of every sense are ordered by a hash of the index and the lemma (an FNV-1a seed, so the order
///     does not depend on the process) and dealt to the folds in turn. A sense with a single anchor is never tested:
///     it stays in the training part of every fold. Each fold is scored by the one-versus-rest classifier fitted on the
///     other folds, which gives every tested anchor out-of-fold scores over all the columns of the lemma. Nothing but
///     the anchors of the lemma is read: the held-out, real-text, suite and independent evaluation sentences cannot
///     influence a threshold, so the accuracy measured on them stays independent of the thresholds.
///     </para>
///     <para>
///     The fine threshold is the lowest observed out-of-fold fine margin (the score of the best sense minus the score
///     of the second best, in score units) such that the anchors whose margin is at least that value, taken together
///     with all anchors of equal margin, are answered with at least <see cref="TrainingOptions.TargetPrecision"/> and
///     there are at least <see cref="TrainingOptions.MinAnswered"/> of them; no such margin gives
///     <see cref="LemmaWeights.Never"/>, and the lemma then never answers at the sense level.
///     </para>
///     <para>
///     The group threshold exists only for a lemma whose columns include a sense that is in a group. Its answers are
///     the anchors that fall through the fine threshold (the fine margin is below it, every anchor when the fine
///     threshold is <see cref="LemmaWeights.Never"/>) and whose best sense is in a group. The group answer of such an
///     anchor is the group of the best sense; it is right when the true sense of the anchor is in that group. Its
///     margin is <see cref="LemmaWeights.GroupMargin"/> of the group scores that <see cref="LemmaWeights.Softmax"/> and
///     <see cref="LemmaWeights.GroupScores"/> derive from the fine scores, the same functions the run time uses, so
///     training and inference define the margin identically. Only finite margins that are not negative can be
///     thresholds; the rule that picks one is the rule of the fine threshold.
///     </para>
///     <para>
///     The functions are deterministic, thread-safe and have no side effects.
///     </para>
/// </remarks>
internal static class Thresholds
{
    private const double Tolerance = 1e-9;

    /// <summary>
    ///     Chooses the thresholds of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma; it seeds the fold assignment.</param>
    /// <param name="features">The unit-length features of the anchors of the lemma.</param>
    /// <param name="labels">The column (sense) index of each anchor.</param>
    /// <param name="senseCount">The number of columns.</param>
    /// <param name="groups">The group index of each column, or -1 for a sense that is in no group.</param>
    /// <param name="options">The training settings: C, the folds, the precision target and the minimum number of answers.</param>
    /// <returns>The fine and the group threshold; either is <see cref="LemmaWeights.Never"/> when its level never reaches the target.</returns>
    /// <exception cref="ArgumentException">Thrown when the arrays disagree in length.</exception>
    public static LemmaThresholds Choose(string lemma, float[][] features, int[] labels, int senseCount, int[] groups, TrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(options);
        if (features.Length != labels.Length || groups.Length != senseCount)
        {
            throw new ArgumentException("There must be one label per feature and one group index per column.", nameof(labels));
        }

        var outOfFold = ScoreOutOfFold(lemma, features, labels, senseCount, options);
        var fine = ChooseThreshold(outOfFold.Select(o => ((double)o.FineMargin, o.FineCorrect)).ToList(), options);
        if (!groups.Any(g => g >= 0))
        {
            return new LemmaThresholds(fine, LemmaWeights.Never);
        }

        // The anchors that fall through the fine level are the ones a group answer is for
        var fallThrough = outOfFold.Where(o => o.FineMargin < fine && groups[o.Best] >= 0).ToList();
        var answers = new List<(double Margin, bool Correct)>(fallThrough.Count);
        foreach (var anchor in fallThrough)
        {
            var probabilities = LemmaWeights.Softmax(anchor.Scores);
            var groupScores = LemmaWeights.GroupScores(probabilities, groups);
            var margin = LemmaWeights.GroupMargin(groupScores, groups, anchor.Best);
            answers.Add((margin, groups[anchor.Label] == groups[anchor.Best]));
        }

        return new LemmaThresholds(fine, ChooseThreshold(answers, options));
    }

    /// <summary>
    ///     Picks the lowest margin whose answers reach the precision target with enough answers.
    /// </summary>
    /// <remarks>
    ///     The candidates are the distinct finite margins that are not negative. The answers at a candidate are all
    ///     answers whose margin is at least the candidate, so equal margins are always taken together. The returned
    ///     value is never above the margin it was chosen from, so the answer that defined it still reaches it when the
    ///     run time compares in the same precision.
    /// </remarks>
    /// <param name="answers">The margin and the correctness of every answer.</param>
    /// <param name="options">The settings that give the target and the minimum number of answers.</param>
    /// <returns>The threshold, or <see cref="LemmaWeights.Never"/> when no candidate reaches the target.</returns>
    internal static float ChooseThreshold(IReadOnlyList<(double Margin, bool Correct)> answers, TrainingOptions options)
    {
        var usable = answers.Where(a => double.IsFinite(a.Margin) && a.Margin >= 0).OrderBy(a => a.Margin).ToList();

        // Walk from the lowest margin upward; the correct answers from a position to the end are kept as a running count
        var correct = usable.Count(a => a.Correct);
        for (var i = 0; i < usable.Count; i++)
        {
            var total = usable.Count - i;
            if (total < options.MinAnswered)
            {
                break;
            }

            var startsGroup = i == 0 || usable[i].Margin > usable[i - 1].Margin;
            if (startsGroup && correct >= (options.TargetPrecision * total) - Tolerance)
            {
                return RoundDown(usable[i].Margin);
            }

            correct -= usable[i].Correct ? 1 : 0;
        }

        return LemmaWeights.Never;
    }

    /// <summary>
    ///     Scores every testable anchor with a classifier fitted without its fold.
    /// </summary>
    /// <param name="lemma">The lemma, which seeds the folds.</param>
    /// <param name="features">The features of the anchors.</param>
    /// <param name="labels">The column of each anchor.</param>
    /// <param name="senseCount">The number of columns.</param>
    /// <param name="options">The settings.</param>
    /// <returns>For each tested anchor its scores over all columns, the best column, the fine margin and whether the best column is the true one.</returns>
    private static List<OutOfFold> ScoreOutOfFold(string lemma, float[][] features, int[] labels, int senseCount, TrainingOptions options)
    {
        var folds = Math.Max(2, options.Folds);
        var assignment = AssignFolds(lemma, labels, senseCount, folds);
        var observed = new List<OutOfFold>();
        var scores = new float[senseCount];
        for (var fold = 0; fold < folds; fold++)
        {
            var test = Enumerable.Range(0, features.Length).Where(i => assignment[i] == fold).ToArray();
            if (test.Length == 0)
            {
                continue;
            }

            var train = Enumerable.Range(0, features.Length).Where(i => assignment[i] != fold).ToArray();
            var (weights, bias) = WeightsTrainer.Fit(train.Select(i => features[i]).ToArray(), train.Select(i => labels[i]).ToArray(), senseCount, options.C);
            var model = new LemmaWeights(weights, bias, features[0].Length);
            foreach (var i in test)
            {
                model.Scores(features[i], scores);
                var (best, margin) = LemmaWeights.Rank(scores);
                observed.Add(new OutOfFold((float[])scores.Clone(), best, margin, labels[i], best == labels[i]));
            }
        }

        return observed;
    }

    /// <summary>
    ///     Assigns the anchors to folds: stratified by sense, shuffled by a seeded hash; a sense with one anchor gets -1
    ///     and is never tested.
    /// </summary>
    /// <param name="lemma">The lemma, which gives the seed.</param>
    /// <param name="labels">The column of each anchor.</param>
    /// <param name="senseCount">The number of columns.</param>
    /// <param name="folds">The number of folds.</param>
    /// <returns>The fold of each anchor, or -1.</returns>
    private static int[] AssignFolds(string lemma, int[] labels, int senseCount, int folds)
    {
        // string.GetHashCode is randomized per process, so derive the seed with FNV-1a instead
        var seed = 2166136261u;
        foreach (var ch in lemma)
        {
            seed = (seed ^ ch) * 16777619u;
        }

        var assignment = new int[labels.Length];
        Array.Fill(assignment, -1);
        for (var sense = 0; sense < senseCount; sense++)
        {
            // Shuffle by a seeded integer hash of the index; deterministic and independent of any RNG
            var order = Enumerable.Range(0, labels.Length).Where(i => labels[i] == sense).OrderBy(i => Mix(seed, (uint)i)).ThenBy(i => i).ToArray();
            if (order.Length < 2)
            {
                continue;
            }

            for (var position = 0; position < order.Length; position++)
            {
                assignment[order[position]] = position % folds;
            }
        }

        return assignment;
    }

    private static uint Mix(uint seed, uint value)
    {
        unchecked
        {
            var h = seed ^ (value * 0x9E3779B1u);
            h ^= h >> 16;
            h *= 0x85EB_CA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>
    ///     Converts a margin to the float that is stored, never above the margin.
    /// </summary>
    /// <param name="margin">The margin.</param>
    /// <returns>The largest float that is not above the margin.</returns>
    private static float RoundDown(double margin)
    {
        var value = (float)margin;
        return value > margin ? MathF.BitDecrement(value) : value;
    }

    /// <summary>
    ///     The out-of-fold result of one anchor.
    /// </summary>
    /// <param name="Scores">The scores of all the columns.</param>
    /// <param name="Best">The best column.</param>
    /// <param name="FineMargin">The margin of the best score over the second best.</param>
    /// <param name="Label">The true column.</param>
    /// <param name="FineCorrect">Whether the best column is the true one.</param>
    private sealed record OutOfFold(float[] Scores, int Best, float FineMargin, int Label, bool FineCorrect);
}
