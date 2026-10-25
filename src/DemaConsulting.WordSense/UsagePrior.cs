using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     The usage prior of the senses of a lemma and part of speech: a ranking by how often the senses are used, for the
///     lemmas whose senses no model scores.
/// </summary>
/// <remarks>
///     <para>
///     The prior is the WordNet 3.0 tag count of each sense (the number of times the sense was tagged in the sense-annotated
///     texts of the WordNet project), smoothed by adding the constant <see cref="Smoothing"/> to every count of the lemma and part
///     of speech and divided by the sum, so the probability of a sense is <c>(count + k) / sum(count + k)</c> and the
///     probabilities of the senses of a slot sum to one. A sense without a count (a project sense has no sense key, and a
///     key that WordNet 3.0 does not list has none either) counts 0. The senses are ranked by probability, the
///     most probable first, and equal probabilities keep the inventory order (the order of Open English WordNet, which puts
///     the commonly used senses first, and the project senses last), so a lemma without any count is ranked by the
///     inventory order alone and has the uniform probability <c>1/n</c>.
///     </para>
///     <para>
///     The prior is evidence about the lemma, not about the context: it ranks the senses of a slot the same way in every
///     sentence, and it never answers a sense or a group (see <see cref="BasicClassifier"/>). A slot with fewer than two
///     senses has no ranking to give. The counts come from the embedded word image, so no file and no model is needed.
///     </para>
///     <para>
///     The functions are deterministic and free of side effects; the cache of the ranked senses holds immutable entries
///     and a racing writer can only replace one valid entry with another, so the class is safe for concurrent use.
///     </para>
/// </remarks>
internal static class UsagePrior
{
    /// <summary>
    ///     The smoothing constant k that is added to the tag count of every sense of a lemma and part of speech (add-k
    ///     smoothing). It was chosen on the development half of the independent all-words gold set (see
    ///     <c>docs/user_guide/accuracy.md</c>): the ranking does not depend on it, the probabilities do.
    /// </summary>
    public const double Smoothing = 1.0;

    private const int CacheSize = 4096;

    private static readonly CacheEntry?[] Cache = new CacheEntry?[CacheSize];

    /// <summary>
    ///     Ranks the senses of a lemma and part of speech by the usage prior.
    /// </summary>
    /// <param name="lemma">The lemma exactly as it is written in the catalog.</param>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The senses with their probabilities, the most probable first and equal probabilities in inventory order; empty when the lemma and part of speech have fewer than two senses.</returns>
    /// <exception cref="ModelIntegrityException">Thrown when the embedded lexicon or its sense catalog is missing or corrupt.</exception>
    public static ImmutableArray<SenseCandidate> RankOf(string lemma, PartOfSpeech pos)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        var slot = (HashCode.Combine(lemma, pos) & int.MaxValue) % CacheSize;
        var cached = Volatile.Read(ref Cache[slot]);
        if (cached is not null && cached.Pos == pos && string.Equals(cached.Lemma, lemma, StringComparison.Ordinal))
        {
            return cached.Senses;
        }

        var usage = WordLexicon.ReadCatalog(image => image.UsageOf(lemma, pos));
        var senses = usage.Length < 2 ? [] : Rank(usage, Smoothing);
        Volatile.Write(ref Cache[slot], new CacheEntry(lemma, pos, senses));
        return senses;
    }

    /// <summary>
    ///     Ranks senses by their tag counts with add-k smoothing.
    /// </summary>
    /// <param name="usage">The id and the tag count of each sense, in inventory order.</param>
    /// <param name="smoothing">The constant k added to every count; it must be positive.</param>
    /// <returns>The senses with the probability <c>(count + k) / sum(count + k)</c>, the most probable first, equal probabilities in the order of <paramref name="usage"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the smoothing constant is not a positive finite number or a count is negative.</exception>
    public static ImmutableArray<SenseCandidate> Rank(IReadOnlyList<(string SenseId, int TagCount)> usage, double smoothing)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (!double.IsFinite(smoothing) || smoothing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothing), smoothing, "The smoothing constant must be a positive number.");
        }

        var total = 0.0;
        foreach (var (_, count) in usage)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            total += count + smoothing;
        }

        // The sort is stable by construction: the index of the sense in the inventory order breaks a tie
        var order = Enumerable.Range(0, usage.Count)
            .OrderByDescending(i => usage[i].TagCount)
            .ThenBy(i => i)
            .ToArray();
        var ranked = ImmutableArray.CreateBuilder<SenseCandidate>(usage.Count);
        foreach (var i in order)
        {
            ranked.Add(new SenseCandidate(usage[i].SenseId, (usage[i].TagCount + smoothing) / total));
        }

        return ranked.MoveToImmutable();
    }

    private sealed record CacheEntry(string Lemma, PartOfSpeech Pos, ImmutableArray<SenseCandidate> Senses);
}
