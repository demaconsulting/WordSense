using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One sentence of the pool of new documents.
/// </summary>
/// <param name="SentenceId">The sentence id (<c>&lt;doc id&gt;-s0012</c>).</param>
/// <param name="DocId">The document id.</param>
/// <param name="Family">The source family.</param>
/// <param name="Words">The tokens of the sentence as a model receives them.</param>
internal sealed record Gold2Sentence(string SentenceId, string DocId, string Family, IReadOnlyList<string> Words);

/// <summary>
///     The plan of the draw of the second all-words gold set: how many tokens of each stratum each split gets and the caps
///     that keep a document, a sentence or a form from dominating.
/// </summary>
/// <param name="TestQuotas">The number of tokens that bear each stratum flag in the test split (the control quota counts the tokens that bear none).</param>
/// <param name="DevShare">The share of the pool sentences (by family) whose documents go to the development split.</param>
/// <param name="PerSentence">The most tokens of one sentence; two tokens of a sentence are never adjacent.</param>
/// <param name="FormCaps">The most tokens of one written form that the pass of a stratum takes (counting the form's tokens already drawn).</param>
/// <param name="DocumentCaps">The most tokens of one document by source family; <see cref="DefaultDocumentCap"/> for a family not listed.</param>
/// <param name="GroupWeights">The share of each source group (<see cref="Gold2Strata.GroupOf"/>) in the quota of a stratum.</param>
internal sealed record Gold2Plan(IReadOnlyDictionary<string, int> TestQuotas, double DevShare, int PerSentence, IReadOnlyDictionary<string, int> FormCaps, IReadOnlyDictionary<string, int> DocumentCaps, IReadOnlyDictionary<string, double> GroupWeights)
{
    /// <summary>The cap of a document of a family without an entry in <see cref="DocumentCaps"/>.</summary>
    public const int DefaultDocumentCap = 8;

    /// <summary>
    ///     Gets the plan of the second gold set: the quotas follow from the power calculation (<see cref="Gold2Power"/>).
    /// </summary>
    public static Gold2Plan Default { get; } = new(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Gold2Strata.Tense] = 480,
            [Gold2Strata.Lemma] = 380,
            [Gold2Strata.Heteronym] = 200,
            [Gold2Strata.NounVerb] = 550,
            [Gold2Strata.Untrained] = 1200,
            [Gold2Strata.Trained] = 630,
            [Gold2Strata.Control] = 1100,
        },
        0.4,
        2,
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Gold2Strata.Tense] = 40,
            [Gold2Strata.Lemma] = 12,
            [Gold2Strata.Heteronym] = 12,
            [Gold2Strata.NounVerb] = 6,
            [Gold2Strata.Untrained] = 3,
            [Gold2Strata.Trained] = 3,
            [Gold2Strata.Control] = 4,
        },
        new Dictionary<string, int>(StringComparer.Ordinal) { ["gutenberg"] = 18 },
        new Dictionary<string, double>(StringComparer.Ordinal) { ["technical"] = 0.4, ["news"] = 0.3, ["literary"] = 0.3 });

    /// <summary>
    ///     Gets the quotas of a split: the test quotas for the test split and two thirds of them for the development split
    ///     (the development split is 40% of the whole set).
    /// </summary>
    /// <param name="split">The split.</param>
    /// <returns>The quotas by stratum.</returns>
    public IReadOnlyDictionary<string, int> QuotasOf(string split) =>
        split == Gold2.Test ? TestQuotas : TestQuotas.ToDictionary(p => p.Key, p => (int)Math.Round(p.Value * 2.0 / 3.0, MidpointRounding.AwayFromZero), StringComparer.Ordinal);

    /// <summary>
    ///     Gets the document cap of a family.
    /// </summary>
    /// <param name="family">The source family.</param>
    /// <returns>The most tokens of one document.</returns>
    public int DocumentCapOf(string family) => DocumentCaps.TryGetValue(family, out var cap) ? cap : DefaultDocumentCap;
}

/// <summary>
///     Draws the tokens of the second all-words gold set from a pool of sentences of new documents, before any labeler or
///     model has seen them.
/// </summary>
/// <remarks>
///     <para>
///     The documents are divided between the splits first (<see cref="AssignSplits"/>), so the two splits never share a
///     document. Each split is then drawn on its own: the strata are filled one after the other, the rarest first (see
///     <see cref="Gold2Strata.DrawOrder"/>), each up to its quota of tokens that bear the flag (a token drawn for one stratum
///     counts for every flag it bears), with the source groups in the weights of the plan and a fixed pseudo-random order
///     of the candidates (a hash of the seed, the sentence and the position). Candidates are the tokens made of letters that
///     the lexicon knows and that are not capitalized inside a sentence (proper names are left out).
///     </para>
/// </remarks>
internal static class Gold2Sampler
{
    /// <summary>
    ///     Divides the documents between the splits: for each family the documents are put in the order of a hash and the
    ///     development split takes documents until it holds its share of the pooled sentences of the family.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="devShare">The share of the sentences of each family for the development split.</param>
    /// <param name="seed">The seed of the order.</param>
    /// <returns>The split of each document.</returns>
    public static Dictionary<string, string> AssignSplits(IReadOnlyList<Gold2Sentence> pool, double devShare, int seed)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var split = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var family in pool.GroupBy(s => s.Family, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var sizes = family.GroupBy(s => s.DocId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var target = devShare * sizes.Values.Sum();
            var taken = 0;
            foreach (var doc in sizes.Keys.OrderBy(d => HashOf(seed, "split", d)).ThenBy(d => d, StringComparer.Ordinal))
            {
                var dev = taken < target;
                split[doc] = dev ? Gold2.Dev : Gold2.Test;
                taken += dev ? sizes[doc] : 0;
            }
        }

        return split;
    }

    /// <summary>
    ///     Draws the tokens of both splits.
    /// </summary>
    /// <param name="pool">The pool of sentences (new documents only, deduplicated).</param>
    /// <param name="plan">The plan.</param>
    /// <param name="trained">The trained lemmas.</param>
    /// <param name="heteronyms">The normalized heteronym spellings.</param>
    /// <param name="seed">The seed of the order.</param>
    /// <returns>The tokens of both splits, numbered in the order of their sentences (<c>d-0001</c>, <c>t-0001</c>).</returns>
    public static List<Gold2Token> Draw(IReadOnlyList<Gold2Sentence> pool, Gold2Plan plan, IReadOnlySet<string> trained, IReadOnlySet<string> heteronyms, int seed)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(plan);
        var splits = AssignSplits(pool, plan.DevShare, seed);
        var strata = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
        var all = new List<Gold2Token>();
        foreach (var split in new[] { Gold2.Dev, Gold2.Test })
        {
            var candidates = new List<Candidate>();
            foreach (var sentence in pool.Where(s => splits[s.DocId] == split))
            {
                for (var i = 0; i < sentence.Words.Count; i++)
                {
                    var word = sentence.Words[i];
                    if (!IsCandidateWord(word, i))
                    {
                        continue;
                    }

                    var key = word.ToLowerInvariant();
                    if (!strata.TryGetValue(key, out var flags))
                    {
                        flags = Gold2Strata.Of(word, trained, heteronyms);
                        strata[key] = flags;
                    }

                    if (flags is not null)
                    {
                        candidates.Add(new Candidate(sentence, i, key, flags, HashOf(seed, "token", sentence.SentenceId + "|" + i.ToString(CultureInfo.InvariantCulture))));
                    }
                }
            }

            var drawn = DrawSplit(candidates, plan, plan.QuotasOf(split));
            var letter = split[0];
            all.AddRange(drawn.OrderBy(c => c.Sentence.SentenceId, StringComparer.Ordinal).ThenBy(c => c.Index)
                .Select((c, n) => new Gold2Token(
                    $"{letter}-{(n + 1).ToString("D4", CultureInfo.InvariantCulture)}",
                    split,
                    c.Sentence.DocId,
                    c.Sentence.Family,
                    c.Sentence.SentenceId,
                    c.Index,
                    c.Sentence.Words[c.Index],
                    string.Join(',', c.Flags),
                    c.Sentence.Words)));
        }

        return all;
    }

    /// <summary>
    ///     Tells whether a token can be drawn: made of letters, not a capitalized word inside a sentence (a name or a title
    ///     word) and not written in capitals (an acronym), apart from <c>I</c>.
    /// </summary>
    /// <param name="word">The token.</param>
    /// <param name="index">The index of the token in its sentence.</param>
    /// <returns>True when the token is a candidate.</returns>
    public static bool IsCandidateWord(string word, int index)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length == 0 || !word.All(char.IsLetter))
        {
            return false;
        }

        if (word == "I")
        {
            return true;
        }

        return word.Length == 1 || (index == 0 || char.IsLower(word[0])) && word[1..].All(char.IsLower);
    }

    private sealed record Candidate(Gold2Sentence Sentence, int Index, string Form, IReadOnlyList<string> Flags, ulong Order);

    private static List<Candidate> DrawSplit(List<Candidate> candidates, Gold2Plan plan, IReadOnlyDictionary<string, int> quotas)
    {
        var ordered = candidates.OrderBy(c => c.Order).ThenBy(c => c.Sentence.SentenceId, StringComparer.Ordinal).ThenBy(c => c.Index).ToList();
        var drawn = new List<Candidate>();
        var taken = new HashSet<(string, int)>();
        var perSentence = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var perDocument = new Dictionary<string, int>(StringComparer.Ordinal);
        var perForm = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = Gold2Strata.DrawOrder.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);

        bool Fits(Candidate c, int formCap)
        {
            if (taken.Contains((c.Sentence.SentenceId, c.Index)) || perForm.GetValueOrDefault(c.Form) >= formCap || perDocument.GetValueOrDefault(c.Sentence.DocId) >= plan.DocumentCapOf(c.Sentence.Family))
            {
                return false;
            }

            return !perSentence.TryGetValue(c.Sentence.SentenceId, out var used) || (used.Count < plan.PerSentence && used.All(u => Math.Abs(u - c.Index) > 1));
        }

        void Take(Candidate c)
        {
            drawn.Add(c);
            taken.Add((c.Sentence.SentenceId, c.Index));
            if (!perSentence.TryGetValue(c.Sentence.SentenceId, out var list))
            {
                list = [];
                perSentence[c.Sentence.SentenceId] = list;
            }

            list.Add(c.Index);
            perDocument[c.Sentence.DocId] = perDocument.GetValueOrDefault(c.Sentence.DocId) + 1;
            perForm[c.Form] = perForm.GetValueOrDefault(c.Form) + 1;
            foreach (var flag in c.Flags)
            {
                counts[flag]++;
            }
        }

        foreach (var stratum in Gold2Strata.DrawOrder)
        {
            var quota = quotas.GetValueOrDefault(stratum);
            var formCap = plan.FormCaps.GetValueOrDefault(stratum, 3);
            var pool = ordered.Where(c => c.Flags.Contains(stratum)).ToList();
            var need = quota - counts[stratum];
            foreach (var (group, weight) in plan.GroupWeights.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var share = (int)Math.Ceiling(need * weight);
                var got = 0;
                foreach (var c in pool.Where(c => Gold2Strata.GroupOf(c.Sentence.Family) == group))
                {
                    if (got >= share || counts[stratum] >= quota)
                    {
                        break;
                    }

                    if (Fits(c, formCap))
                    {
                        Take(c);
                        got++;
                    }
                }
            }

            foreach (var c in pool)
            {
                if (counts[stratum] >= quota)
                {
                    break;
                }

                if (Fits(c, formCap))
                {
                    Take(c);
                }
            }
        }

        return drawn;
    }

    private static ulong HashOf(int seed, string purpose, string text) =>
        BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed.ToString(CultureInfo.InvariantCulture)}|{purpose}|{text}")));

    /// <summary>
    ///     Counts the tokens of each stratum flag, for the report of the draw.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The number of tokens that bear each flag (the control flag counts the tokens that bear none).</returns>
    public static Dictionary<string, int> CountStrata(IEnumerable<Gold2Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var counts = Gold2Strata.DrawOrder.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            foreach (var flag in token.Strata.Split(','))
            {
                counts[flag] = counts.GetValueOrDefault(flag) + 1;
            }
        }

        return counts;
    }
}
