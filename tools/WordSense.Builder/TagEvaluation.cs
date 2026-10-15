using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Counts of a tag-level evaluation over holdout sentences.
/// </summary>
/// <param name="Evaluated">Sentences evaluated.</param>
/// <param name="Correct">Sentences whose best group was right, ignoring abstention.</param>
/// <param name="Answered">Sentences answered (the library would return a tag).</param>
/// <param name="AnsweredCorrect">Answered sentences that were right.</param>
/// <param name="OtherWins">Sentences where a sense carrying none of the queried tags scored best.</param>
internal sealed record TagStats(int Evaluated, int Correct, int Answered, int AnsweredCorrect, int OtherWins);

/// <summary>
///     The tag-level evaluation of a table on the holdout set.
/// </summary>
/// <param name="Overall">Noun-versus-verb over every evaluated sentence.</param>
/// <param name="BothSenses">Noun-versus-verb restricted to lemmas that have both a noun and a verb sense.</param>
/// <param name="BySource">Noun-versus-verb per holdout file.</param>
/// <param name="ByGoldTag">Noun-versus-verb per gold tag (<c>noun</c> or <c>verb</c>).</param>
/// <param name="PerTag">Has-tag accuracy for every tag of the inventory (expected true or false per sentence).</param>
/// <param name="MixedRows">Sentences excluded because the gold sense carries both queried tags.</param>
/// <param name="OtherRows">Sentences excluded because the gold sense carries neither queried tag.</param>
/// <param name="MixedLemmas">Lemmas with a sense that carries both queried tags.</param>
internal sealed record TagReport(
    TagStats Overall,
    TagStats BothSenses,
    IReadOnlyDictionary<string, TagStats> BySource,
    IReadOnlyDictionary<string, TagStats> ByGoldTag,
    IReadOnlyDictionary<string, TagStats> PerTag,
    int MixedRows,
    int OtherRows,
    IReadOnlyList<string> MixedLemmas);

/// <summary>
///     Accumulates the tag-level evaluation using the library's tag rule.
/// </summary>
internal sealed class TagEvaluator
{
    /// <summary>The queried tags of the noun-versus-verb evaluation, sorted.</summary>
    public static readonly string[] Query = ["noun", "verb"];

    private readonly Counter _overall = new();
    private readonly Counter _both = new();
    private readonly Dictionary<string, Counter> _bySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _byGold = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _perTag = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _mixedLemmas = new(StringComparer.Ordinal);
    private int _mixedRows;
    private int _otherRows;

    /// <summary>
    ///     Adds one holdout sentence.
    /// </summary>
    /// <param name="source">The holdout file.</param>
    /// <param name="model">The lemma model.</param>
    /// <param name="scores">The score of every sense for the sentence.</param>
    /// <param name="gold">The index of the gold sense.</param>
    /// <param name="threshold">The abstain margin of the lemma.</param>
    public void Add(string source, LemmaModel model, ReadOnlySpan<float> scores, int gold, double threshold)
    {
        var senses = model.Senses;

        // Per-tag has-tag evaluation for every tag the lemma has
        foreach (var tag in senses.SelectMany(s => s.Tags).Distinct(StringComparer.Ordinal))
        {
            var expected = senses[gold].HasTag(tag);
            var best = TagScorer.IsTag(senses, scores, tag, double.NegativeInfinity);
            var answer = TagScorer.IsTag(senses, scores, tag, threshold);
            For(_perTag, tag).Add(best == expected, answer is not null, answer == expected, false);
        }

        // Noun-versus-verb evaluation on sentences whose gold sense has exactly one queried tag
        var goldTags = Query.Where(senses[gold].HasTag).ToArray();
        if (senses.Any(s => Query.All(s.HasTag)))
        {
            _mixedLemmas.Add(model.Lemma);
        }

        if (goldTags.Length == 2)
        {
            _mixedRows++;
            return;
        }

        if (goldTags.Length == 0)
        {
            _otherRows++;
            return;
        }

        var best1 = TagScorer.Classify(senses, scores, Query, double.NegativeInfinity);
        var answered = TagScorer.Classify(senses, scores, Query, threshold);
        var bothSenses = Query.All(q => senses.Any(s => s.HasTag(q)));
        var args = (best1 == goldTags[0], answered is not null, answered == goldTags[0], best1 is null);
        _overall.Add(args.Item1, args.Item2, args.Item3, args.Item4);
        For(_bySource, source).Add(args.Item1, args.Item2, args.Item3, args.Item4);
        For(_byGold, goldTags[0]).Add(args.Item1, args.Item2, args.Item3, args.Item4);
        if (bothSenses)
        {
            _both.Add(args.Item1, args.Item2, args.Item3, args.Item4);
        }
    }

    /// <summary>
    ///     Gets the accumulated report.
    /// </summary>
    /// <returns>The report.</returns>
    public TagReport ToReport()
    {
        return new TagReport(
            _overall.ToStats(),
            _both.ToStats(),
            _bySource.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal),
            _byGold.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal),
            _perTag.ToDictionary(p => p.Key, p => p.Value.ToStats(), StringComparer.Ordinal),
            _mixedRows,
            _otherRows,
            _mixedLemmas.ToList());
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
        private int _otherWins;

        public void Add(bool correct, bool answered, bool answeredCorrect, bool otherWins)
        {
            _evaluated++;
            _correct += correct ? 1 : 0;
            _answered += answered ? 1 : 0;
            _answeredCorrect += answered && answeredCorrect ? 1 : 0;
            _otherWins += otherWins ? 1 : 0;
        }

        public TagStats ToStats() => new(_evaluated, _correct, _answered, _answeredCorrect, _otherWins);
    }
}
