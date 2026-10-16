using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Counts of an evaluation level over holdout sentences.
/// </summary>
/// <param name="Evaluated">Sentences evaluated.</param>
/// <param name="Correct">Sentences whose best sense was right at this level, ignoring abstention.</param>
/// <param name="Answered">Sentences answered (the library would return an answer).</param>
/// <param name="AnsweredCorrect">Answered sentences that were right at this level.</param>
internal sealed record LevelStats(int Evaluated, int Correct, int Answered, int AnsweredCorrect);

/// <summary>
///     The part-of-speech and pronunciation evaluation of a table on the holdout set.
/// </summary>
/// <param name="Pos">Part-of-speech accuracy over every evaluated sentence.</param>
/// <param name="PosBySource">Part-of-speech accuracy per holdout file.</param>
/// <param name="PosByGold">Part-of-speech accuracy per gold part of speech.</param>
/// <param name="Pronunciation">Pronunciation (meaning) accuracy over the sentences of heteronym lemmas.</param>
/// <param name="PronunciationBySource">Pronunciation (meaning) accuracy per holdout file.</param>
internal sealed record AccuracyReport(
    LevelStats Pos,
    IReadOnlyDictionary<string, LevelStats> PosBySource,
    IReadOnlyDictionary<string, LevelStats> PosByGold,
    LevelStats Pronunciation,
    IReadOnlyDictionary<string, LevelStats> PronunciationBySource);

/// <summary>
///     Accumulates the part-of-speech and pronunciation evaluation.
/// </summary>
/// <remarks>
///     The part-of-speech level compares the part of speech of the best sense with that of the gold sense, over every
///     sentence. The pronunciation level compares the meanings, over the sentences of heteronym lemmas only. A sentence
///     counts as answered when the sense-level decision was not an abstention.
/// </remarks>
internal sealed class AccuracyEvaluator
{
    private readonly Counter _pos = new();
    private readonly Counter _pron = new();
    private readonly Dictionary<string, Counter> _posBySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _posByGold = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _pronBySource = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds one holdout sentence.
    /// </summary>
    /// <param name="source">The holdout file.</param>
    /// <param name="model">The lemma model.</param>
    /// <param name="best">The index of the best sense.</param>
    /// <param name="gold">The index of the gold sense.</param>
    /// <param name="answered">Whether the sense-level decision was an answer rather than an abstention.</param>
    public void Add(string source, LemmaModel model, int best, int gold, bool answered)
    {
        var posRight = model.Senses[best].Pos == model.Senses[gold].Pos;
        _pos.Add(posRight, answered);
        For(_posBySource, source).Add(posRight, answered);
        For(_posByGold, model.Senses[gold].Pos.ToString().ToLowerInvariant()).Add(posRight, answered);
        if (model.IsHeteronym)
        {
            var meaningRight = string.Equals(model.Senses[best].Meaning, model.Senses[gold].Meaning, StringComparison.Ordinal);
            _pron.Add(meaningRight, answered);
            For(_pronBySource, source).Add(meaningRight, answered);
        }
    }

    /// <summary>
    ///     Gets the accumulated report.
    /// </summary>
    /// <returns>The report.</returns>
    public AccuracyReport ToReport()
    {
        return new AccuracyReport(
            _pos.ToStats(),
            _posBySource.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal),
            _posByGold.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal),
            _pron.ToStats(),
            _pronBySource.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal));
    }

    private static Counter For(Dictionary<string, Counter> map, string key)
    {
        if (!map.TryGetValue(key, out var counter))
        {
            map[key] = counter = new Counter();
        }

        return counter;
    }

    /// <summary>
    ///     A mutable set of counts.
    /// </summary>
    private sealed class Counter
    {
        private int _evaluated;
        private int _correct;
        private int _answered;
        private int _answeredCorrect;

        public void Add(bool correct, bool answered)
        {
            _evaluated++;
            _correct += correct ? 1 : 0;
            _answered += answered ? 1 : 0;
            _answeredCorrect += answered && correct ? 1 : 0;
        }

        public LevelStats ToStats() => new(_evaluated, _correct, _answered, _answeredCorrect);
    }
}
