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
/// <param name="Stratum">The stratum of a lemma, or null for one stratum of all lemmas. The pooled shifts are fitted and applied within a stratum, so the rows of one stratum never move the thresholds of another.</param>
internal sealed record CalibrationOptions(double TargetPrecision = 0.95, int MinOwnRows = 20, int MinOwnAnswered = 5, int MinPooledAnswered = 30, double MinPooledShare = 0.3, Func<string, string>? Stratum = null);

/// <summary>
///     What the calibration did in one stratum of lemmas.
/// </summary>
/// <param name="Name">The name of the stratum (empty when there is one stratum).</param>
/// <param name="Rows">The calibration rows of the stratum.</param>
/// <param name="FineShift">The pooled shift of the fine thresholds.</param>
/// <param name="FineReached">Whether the shift reached the precision target.</param>
/// <param name="GroupShift">The pooled shift of the group thresholds.</param>
/// <param name="GroupAnswers">The calibration group answers that could verify the group thresholds.</param>
/// <param name="GroupVerified">Whether the group thresholds were moved (enough answers) or kept unverified.</param>
/// <param name="GroupReached">Whether the group shift reached the precision target (false when the group thresholds were kept unverified).</param>
internal sealed record StratumReport(string Name, int Rows, double FineShift, bool FineReached, double GroupShift, int GroupAnswers, bool GroupVerified, bool GroupReached);

/// <summary>
///     What a real-text calibration did.
/// </summary>
/// <param name="Rows">The calibration rows scored (rows of trained lemmas).</param>
/// <param name="Lemmas">The lemmas that have calibration rows.</param>
/// <param name="OwnThresholds">The lemmas whose threshold came from their own rows.</param>
/// <param name="AnswersBefore">The sense answers of the calibration rows with the first thresholds.</param>
/// <param name="CorrectBefore">The correct ones among them.</param>
/// <param name="AnswersAfter">The sense answers with the final thresholds.</param>
/// <param name="CorrectAfter">The correct ones among them.</param>
/// <param name="Strata">What was done in each stratum, in the order of their names.</param>
/// <param name="ClosedWins">The rows that were left out because the closed-class slot of Basic wins the split at run time (the answer is at the part-of-speech level, so no threshold applies).</param>
/// <param name="ClosedGoldRows">The rows observed whose true sense is a closed-class sense: the open-class slot wins, so a sense answer is wrong.</param>
internal sealed record CalibrationReport(
    int Rows,
    int Lemmas,
    int OwnThresholds,
    int AnswersBefore,
    int CorrectBefore,
    int AnswersAfter,
    int CorrectAfter,
    IReadOnlyList<StratumReport> Strata,
    int ClosedWins = 0,
    int ClosedGoldRows = 0)
{
    /// <summary>
    ///     Tells whether two reports hold the same values (the strata are compared one by one).
    /// </summary>
    /// <param name="other">The other report.</param>
    /// <returns>True when every value is equal.</returns>
    public bool Equals(CalibrationReport? other) =>
        other is not null && Rows == other.Rows && Lemmas == other.Lemmas && OwnThresholds == other.OwnThresholds
        && AnswersBefore == other.AnswersBefore && CorrectBefore == other.CorrectBefore && AnswersAfter == other.AnswersAfter
        && CorrectAfter == other.CorrectAfter && ClosedWins == other.ClosedWins && ClosedGoldRows == other.ClosedGoldRows && Strata.SequenceEqual(other.Strata);

    /// <summary>
    ///     Gets a hash code of the values.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Rows, Lemmas, OwnThresholds, AnswersBefore, CorrectBefore, AnswersAfter, CorrectAfter, Strata.Count);

    /// <summary>
    ///     Gets the pooled shift of the fine thresholds of the first stratum (the only one without strata).
    /// </summary>
    public double FineShift => Strata.Count > 0 ? Strata[0].FineShift : 0.0;

    /// <summary>
    ///     Gets a value that tells whether the fine shift of the first stratum reached the target.
    /// </summary>
    public bool FineReached => Strata.Count > 0 && Strata[0].FineReached;

    /// <summary>
    ///     Gets the pooled shift of the group thresholds of the first stratum.
    /// </summary>
    public double GroupShift => Strata.Count > 0 ? Strata[0].GroupShift : 0.0;

    /// <summary>
    ///     Gets the group answers that could verify the group thresholds of the first stratum.
    /// </summary>
    public int GroupAnswers => Strata.Count > 0 ? Strata[0].GroupAnswers : 0;

    /// <summary>
    ///     Gets a value that tells whether the group thresholds of the first stratum were moved.
    /// </summary>
    public bool GroupVerified => Strata.Count > 0 && Strata[0].GroupVerified;

    /// <summary>
    ///     Gets a value that tells whether the group shift of the first stratum reached the target.
    /// </summary>
    public bool GroupReached => Strata.Count > 0 && Strata[0].GroupReached;

    private static string GroupText(StratumReport s)
    {
        if (!s.GroupVerified)
        {
            return "unverified, kept";
        }

        return s.GroupReached ? "verified" : "verified, target not reached";
    }

    /// <summary>
    ///     Describes the calibration in one line for the training summary (one line more per stratum when there are
    ///     several). The sense answers before and after cover every observed calibration row, the lemmas with a threshold
    ///     of their own included, not only the pooled rows.
    /// </summary>
    /// <returns>The text.</returns>
    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        string P(int correct, int answers) => answers == 0 ? "n/a" : (100.0 * correct / answers).ToString("0.00", inv) + "%";
        var text = $"real-text calibration: {Rows.ToString(inv)} rows of {Lemmas.ToString(inv)} lemmas; fine pooled shift {FineShift.ToString("0.000", inv)}" +
               $" ({(FineReached ? "reaches the target" : "target not reached")}), {OwnThresholds.ToString(inv)} lemmas with a threshold of their own;" +
               $" sense answers {AnswersBefore.ToString(inv)} at {P(CorrectBefore, AnswersBefore)} before, {AnswersAfter.ToString(inv)} at {P(CorrectAfter, AnswersAfter)} after (all calibration rows);" +
               $" group shift {GroupShift.ToString("0.000", inv)} ({(Strata.Count > 0 ? GroupText(Strata[0]) : "unverified, kept")} on {GroupAnswers.ToString(inv)} answers)." +
               $" {ClosedWins.ToString(inv)} rows left out because the closed-class slot of Basic wins; {ClosedGoldRows.ToString(inv)} observed rows have a closed-class true sense (counted wrong).";
        if (Strata.Count <= 1)
        {
            return text;
        }

        var lines = Strata.Select(s => $"\n  stratum '{s.Name}': {s.Rows.ToString(inv)} rows; fine pooled shift {s.FineShift.ToString("0.000", inv)} ({(s.FineReached ? "reaches the target" : "target not reached")});" +
            $" group shift {s.GroupShift.ToString("0.000", inv)} ({GroupText(s)} on {s.GroupAnswers.ToString(inv)} answers).");
        return text + string.Concat(lines);
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
///     The shifts are fitted per stratum of lemmas (<see cref="CalibrationOptions.Stratum"/>) when a stratum function is
///     given: the lemmas of a stratum are calibrated with the rows of that stratum alone, so a lemma set added later, whose
///     rows were collected another way and whose precision at a margin differs, never moves the thresholds of the lemmas
///     trained before, and each stratum meets the target on its own rows. The trainer strata are the stages of the corpus
///     (<see cref="Corpus.StagesOf"/>).
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
    /// <param name="basic">The result of Basic for the sentence of each row (the target word is read from it), parallel to <paramref name="rows"/>, or null when it is not available; a null entry is a row without it. A row whose closed-class slot wins in the composite model is left out (see <see cref="Observe"/>); without Basic every row is kept.</param>
    /// <returns>The weights with the final thresholds, and the report.</returns>
    /// <exception cref="ArgumentException">Thrown when there is not one feature (or one Basic result) per row.</exception>
    public static (WeightsData Data, CalibrationReport Report) Apply(WeightsData data, IReadOnlyList<CorpusRow> rows, IReadOnlyList<float[]> features, CalibrationOptions options, IReadOnlyList<ClassifyResult?>? basic = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(options);
        if (rows.Count != features.Count)
        {
            throw new ArgumentException("There must be one feature per calibration row.", nameof(features));
        }

        if (basic is not null && basic.Count != rows.Count)
        {
            throw new ArgumentException("There must be one Basic result per calibration row.", nameof(basic));
        }

        var lemmas = data.Lemmas.ToDictionary(l => l.Lemma, StringComparer.Ordinal);
        var groupsOf = lemmas.ToDictionary(l => l.Key, l => GroupsOf(l.Value.SenseIds), StringComparer.Ordinal);
        var observed = new List<Observation>();
        var closedWins = 0;
        var closedGold = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!lemmas.TryGetValue(rows[i].Lemma, out var lemma))
            {
                continue;
            }

            var observation = Observe(lemma, groupsOf[lemma.Lemma], rows[i], features[i], basic?[i]);
            if (observation is null)
            {
                continue;
            }

            if (observation.ClosedWins)
            {
                closedWins++;
                continue;
            }

            closedGold += observation.GoldIsClosed ? 1 : 0;
            observed.Add(observation);
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

        // The pooled shift of every stratum is fitted on the lemmas of the stratum that have a finite first threshold and none
        // of their own; a lemma is never calibrated with the rows of another stratum
        var stratumOf = options.Stratum ?? (_ => string.Empty);
        var strata = observed.Select(o => stratumOf(o.Lemma)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var fineShifts = new Dictionary<string, (double Shift, bool Reached)>(StringComparer.Ordinal);
        foreach (var stratum in strata)
        {
            var pooled = observed.Where(o => stratumOf(o.Lemma) == stratum && !ownThreshold.ContainsKey(o.Lemma) && lemmas[o.Lemma].Weights.FineThreshold < LemmaWeights.Never)
                .Select(o => ((double)o.Margin - lemmas[o.Lemma].Weights.FineThreshold, o.Correct)).ToList();
            fineShifts[stratum] = ChooseShift(pooled, options.TargetPrecision, options.MinPooledAnswered, options.MinPooledShare);
        }

        var fine = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var lemma in lemmas.Values)
        {
            var first = lemma.Weights.FineThreshold;
            if (first >= LemmaWeights.Never)
            {
                fine[lemma.Lemma] = first;
                continue;
            }

            var shift = fineShifts.TryGetValue(stratumOf(lemma.Lemma), out var chosen) ? chosen.Shift : 0.0;
            var shifted = RoundDown((double)first + shift, first);
            fine[lemma.Lemma] = ownThreshold.TryGetValue(lemma.Lemma, out var o) ? Math.Max(shifted, o) : shifted;
        }

        // The group answers are the rows that fall through the final fine threshold and whose best sense is in a group;
        // only usable ones (a finite, non-negative distance, as in ChooseShift) count towards the verification, per stratum
        var groupShifts = new Dictionary<string, (double Shift, bool Reached, int Answers, bool Verified)>(StringComparer.Ordinal);
        foreach (var stratum in strata)
        {
            var groupRows = observed.Where(o => stratumOf(o.Lemma) == stratum && !double.IsNaN(o.GroupMargin) && o.Margin < fine[o.Lemma] && lemmas[o.Lemma].Weights.GroupThreshold < LemmaWeights.Never)
                .Select(o => (Distance: o.GroupMargin - lemmas[o.Lemma].Weights.GroupThreshold, Correct: o.GroupCorrect)).Where(a => double.IsFinite(a.Distance) && a.Distance >= 0).ToList();
            var verified = groupRows.Count >= options.MinPooledAnswered;
            var (shift, reached) = verified ? ChooseShift(groupRows, options.TargetPrecision, options.MinPooledAnswered, options.MinPooledShare) : (0.0, false);
            groupShifts[stratum] = (shift, reached, groupRows.Count, verified);
        }

        var updated = data.Lemmas.Select(l =>
        {
            var w = l.Weights;
            var verified = groupShifts.TryGetValue(stratumOf(l.Lemma), out var chosen) && chosen.Verified;
            var group = w.GroupThreshold >= LemmaWeights.Never || !verified
                ? w.GroupThreshold
                : RoundDown((double)w.GroupThreshold + chosen.Shift, w.GroupThreshold);
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

        var reports = strata.Select(s => new StratumReport(
            s,
            observed.Count(o => stratumOf(o.Lemma) == s),
            fineShifts[s].Shift,
            fineShifts[s].Reached,
            groupShifts[s].Shift,
            groupShifts[s].Answers,
            groupShifts[s].Verified,
            groupShifts[s].Reached)).ToList();
        var report = new CalibrationReport(
            observed.Count, observed.Select(o => o.Lemma).Distinct(StringComparer.Ordinal).Count(), ownThreshold.Count,
            before, correctBefore, after, correctAfter, reports, closedWins, closedGold);
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

    /// <summary>
    ///     Lists the columns that the classifier scores for the target word of a row: only the open-class senses of the
    ///     parts of speech that the word has for the lemma are candidates (a closed-class sense is not scored, and a sense of
    ///     another part of speech of the lemma that the word does not have is not a candidate either).
    /// </summary>
    /// <param name="lemma">The weights of the lemma of the row.</param>
    /// <param name="row">The row.</param>
    /// <returns>The columns in ascending order, or null when the word is not in the lexicon.</returns>
    internal static List<int>? CandidateColumns(WeightedLemma lemma, CorpusRow row)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(row);
        if (!WordLexicon.TryLookup(row.Words[row.TargetWordIndex], out var analyses))
        {
            return null;
        }

        var columnOf = lemma.SenseIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index, StringComparer.Ordinal);
        return OnnxClassifier.OpenClassColumns(id => columnOf.GetValueOrDefault(id, -1), lemma.Lemma, analyses.Where(a => string.Equals(a.Lemma, lemma.Lemma, StringComparison.Ordinal)).Select(a => a.PartOfSpeech));
    }

    /// <summary>
    ///     Scores a calibration row the way the classifier and the composite model do at run time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Only the candidate columns of <see cref="CandidateColumns"/> are scored. A row whose true sense is a closed-class
    ///     sense (no column among the candidates) is an observation that is wrong when it reaches the sense level.
    ///     </para>
    ///     <para>
    ///     At run time a row reaches the sense level only if an open-class slot wins the closed-class split of the
    ///     composite model: the closed-class slots take the probability of Basic, and the encoder decides inside the
    ///     open-class part (<c>FallbackWordSenseModel.SplitClosedClass</c>). When the closed-class slot of Basic wins, the
    ///     answer is at the part-of-speech level and no threshold applies, so the row says nothing about a threshold and
    ///     is left out; when an open-class slot wins, the answer is a sense answer and a row whose true sense is
    ///     closed-class is wrong. The check runs the split itself on a result built from the slot masses of the lemma and the
    ///     result of Basic for the sentence. Without the result of Basic the closed-class slots cannot be compared, and
    ///     the row is kept (a closed-class true sense counts as wrong), which can only make the thresholds more
    ///     conservative.
    ///     </para>
    /// </remarks>
    /// <param name="lemma">The weights of the lemma of the row.</param>
    /// <param name="groups">The group index of every column of the lemma.</param>
    /// <param name="row">The row.</param>
    /// <param name="feature">The feature of the row.</param>
    /// <param name="basic">The result of Basic for the sentence of the row, or null.</param>
    /// <returns>The observation, or null when the classifier would not score the row (the word is no form of the lemma, or fewer than two candidate senses).</returns>
    private static Observation? Observe(WeightedLemma lemma, int[] groups, CorpusRow row, float[] feature, ClassifyResult? basic)
    {
        if (CandidateColumns(lemma, row) is not { Count: >= 2 } columns)
        {
            return null;
        }

        var columnOf = lemma.SenseIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index, StringComparer.Ordinal);
        var all = new float[lemma.SenseIds.Count];
        lemma.Weights.Scores(feature, all);
        var scores = columns.Select(c => all[c]).ToArray();
        var (best, margin) = LemmaWeights.Rank(scores);
        var label = columnOf.GetValueOrDefault(row.SenseId, -1);
        var closedWins = basic is { } basicResult && ClosedSlotWins(lemma, row, columns, scores, best, basicResult);
        var groupMargin = double.NaN;
        var groupCorrect = false;
        var candidateGroups = columns.Select(c => groups[c]).ToArray();
        if (candidateGroups[best] >= 0)
        {
            var groupScores = LemmaWeights.GroupScores(LemmaWeights.Softmax(scores), candidateGroups);
            groupMargin = LemmaWeights.GroupMargin(groupScores, candidateGroups, best);
            groupCorrect = label >= 0 && groups[label] == candidateGroups[best];
        }

        var goldIsClosed = WordLexicon.TryGetSense(row.SenseId, out var gold) && SenseFusion.IsClosedClass(gold.PartOfSpeech);
        return new Observation(lemma.Lemma, margin, columns[best] == label, groupMargin, groupCorrect, closedWins, goldIsClosed);
    }

    /// <summary>
    ///     Tells whether a closed-class slot wins the closed-class split of the composite model for a row.
    /// </summary>
    /// <param name="lemma">The weights of the lemma of the row.</param>
    /// <param name="row">The row.</param>
    /// <param name="columns">The candidate columns of the row.</param>
    /// <param name="scores">The scores of the candidate columns.</param>
    /// <param name="best">The position of the best candidate column.</param>
    /// <param name="basic">The result of Basic for the sentence of the row.</param>
    /// <returns>True when the answer of the composite model for the row is at the part-of-speech level with a closed-class slot selected.</returns>
    private static bool ClosedSlotWins(WeightedLemma lemma, CorpusRow row, List<int> columns, float[] scores, int best, ClassifyResult basic)
    {
        if (!WordLexicon.TryLookup(row.Words[row.TargetWordIndex], out var analyses))
        {
            return false;
        }

        // The result of the encoder as the classifier builds it: the open-class slots of the lemma with the mass of their
        // senses, and the closed-class slots of the lemma without probability and without senses
        var probabilities = LemmaWeights.Softmax(scores);
        PartOfSpeech SlotOf(int position) => WordLexicon.TryGetSense(lemma.SenseIds[columns[position]], out var sense) ? sense.PartOfSpeech : PartOfSpeech.Noun;
        var candidates = new List<AnalysisCandidate>();
        foreach (var details in analyses.Where(a => string.Equals(a.Lemma, lemma.Lemma, StringComparison.Ordinal)))
        {
            if (candidates.Exists(c => SenseFusion.SameSlot(c.Details, details)))
            {
                continue;
            }

            var mass = SenseFusion.IsClosedClass(details.PartOfSpeech) ? 0.0 : Enumerable.Range(0, columns.Count).Where(p => SlotOf(p) == details.PartOfSpeech).Sum(p => probabilities[p]);
            candidates.Add(new AnalysisCandidate(details, mass, []));
        }

        var selectedIndex = candidates.FindIndex(c => c.Details.PartOfSpeech == SlotOf(best));
        if (selectedIndex < 0)
        {
            return false;
        }

        var primary = new ClassifyResult(row.Words[row.TargetWordIndex], true, [.. candidates], candidates[selectedIndex].Details, SenseLevel.Sense, 1.0, null, lemma.SenseIds[columns[best]]);
        var split = FallbackWordSenseModel.SplitClosedClass(primary, basic, out var applied);
        return applied && split.Details is { } winner && SenseFusion.IsClosedClass(winner.PartOfSpeech);
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

    private sealed record Observation(string Lemma, float Margin, bool Correct, double GroupMargin, bool GroupCorrect, bool ClosedWins = false, bool GoldIsClosed = false);
}
