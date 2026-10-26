using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     What the sense catalog says about the parts of speech of a word: for each tag the number of lemma and part of
///     speech pairs (slots) among the analyses of the word, the number of their senses and the sum of their WordNet 3.0
///     tag counts. It is the evidence of the word prior of the tagger.
/// </summary>
/// <remarks>
///     <para>
///     The prior estimates <c>P(tag | word)</c> from the dictionary alone, for every word, so that a word that the training
///     counts never saw still gets an emission that says which of its parts of speech is usual: the tag counts of
///     <c>rank</c> are 32 as a noun and 3 as a verb, the ones of <c>pump</c> 4 and 8. The counts are those of the word
///     image (they are sums over the senses of a lemma and part of speech, see <see cref="UsagePrior"/>), so the prior
///     needs no table of its own: it is computed from the image that the model is bound to, and an analysis that has no
///     sense (a closed-class word, a form without a catalog entry) adds nothing.
///     </para>
///     <para>
///     The weight of a part of speech with at least one slot is <c>(tag count + a * slots + s * senses) ^ e</c>, where
///     <c>a</c> is the smoothing of every slot (a part of speech that was never tagged is not impossible), <c>s</c> a weight
///     of the number of senses (the proxy of a word whose lemma has no tag count at all) and <c>e</c> the exponent (the
///     temperature of the prior, below 1 to flatten it). The weights are normalized over the parts of speech that have a
///     slot and scaled to the share that the generic tag prior gives them among the possible tags of the word; a possible
///     tag without a slot (a closed-class tag) keeps its generic share, so the prior never changes the odds between a
///     catalog part of speech and a closed class.
///     </para>
///     <para>
///     The instance is immutable. <see cref="Of"/> keeps the last answers in a small direct-mapped cache of immutable
///     entries, so a racing writer can only replace one valid entry with another; the class is safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class WordSupport
{
    private const int CacheSize = 4096;

    private static readonly CacheEntry?[] Cache = new CacheEntry?[CacheSize];

    private readonly int[] _tagCounts;
    private readonly int[] _senses;
    private readonly int[] _slots;

    private WordSupport(int[] tagCounts, int[] senses, int[] slots)
    {
        _tagCounts = tagCounts;
        _senses = senses;
        _slots = slots;
    }

    /// <summary>
    ///     Gets the sum of the WordNet 3.0 tag counts of the slots with a tag, indexed by tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The sum of the tag counts.</returns>
    public int TagCount(int tag) => _tagCounts[tag];

    /// <summary>
    ///     Gets the number of senses of the slots with a tag, indexed by tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The number of senses.</returns>
    public int SenseCount(int tag) => _senses[tag];

    /// <summary>
    ///     Gets the number of slots (lemma and part of speech pairs with at least one sense) with a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The number of slots.</returns>
    public int SlotCount(int tag) => _slots[tag];

    /// <summary>
    ///     Gets the support of the analyses of a word, or null when no analysis has a sense in the catalog.
    /// </summary>
    /// <param name="key">The lower-case lookup key of the word.</param>
    /// <param name="analyses">The analyses of the word.</param>
    /// <returns>The support, or null when the catalog knows no slot of the word (the prior is then absent).</returns>
    /// <exception cref="ModelIntegrityException">Thrown when the embedded lexicon or its sense catalog is missing or corrupt.</exception>
    public static WordSupport? Of(string key, ImmutableArray<WordDetails> analyses)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (analyses.IsDefaultOrEmpty)
        {
            return null;
        }

        var slot = (key.GetHashCode() & int.MaxValue) % CacheSize;
        var cached = Volatile.Read(ref Cache[slot]);
        if (cached is not null && string.Equals(cached.Key, key, StringComparison.Ordinal) && cached.Analyses == analyses)
        {
            return cached.Support;
        }

        var support = WordLexicon.ReadCatalog(image => Compute(image, analyses));
        Volatile.Write(ref Cache[slot], new CacheEntry(key, analyses, support));
        return support;
    }

    /// <summary>
    ///     Totals the catalog usage of the distinct slots of a list of analyses.
    /// </summary>
    /// <param name="analyses">The analyses.</param>
    /// <param name="usageOf">Gives the sum of the tag counts and the number of senses of a lemma and part of speech.</param>
    /// <returns>The support, or null when no slot has a sense.</returns>
    internal static WordSupport? Compute(ImmutableArray<WordDetails> analyses, Func<string, PartOfSpeech, (int TagCount, int SenseCount)> usageOf)
    {
        ArgumentNullException.ThrowIfNull(usageOf);
        var tagCounts = new int[PosTag.Count];
        var senses = new int[PosTag.Count];
        var slots = new int[PosTag.Count];
        var any = false;
        for (var j = 0; j < analyses.Length; j++)
        {
            var seen = false;
            for (var k = 0; k < j && !seen; k++)
            {
                seen = SenseFusion.SameSlot(analyses[k], analyses[j]);
            }

            if (seen)
            {
                continue;
            }

            var tag = PosTag.Of(analyses[j].PartOfSpeech);

            // WordNet tags only nouns, verbs, adjectives and adverbs; the closed classes have a project sense without a
            // count, which says nothing about how often they occur, so they keep their generic share
            if (tag is not (PosTag.Noun or PosTag.Verb or PosTag.Adjective or PosTag.Adverb))
            {
                continue;
            }

            var (tagCount, senseCount) = usageOf(analyses[j].Lemma, analyses[j].PartOfSpeech);
            if (senseCount == 0)
            {
                continue;
            }

            tagCounts[tag] += tagCount;
            senses[tag] += senseCount;
            slots[tag]++;
            any = true;
        }

        return any ? new WordSupport(tagCounts, senses, slots) : null;
    }

    private static WordSupport? Compute(WordImage image, ImmutableArray<WordDetails> analyses) => Compute(analyses, image.UsageTotalsOf);

    /// <summary>
    ///     Computes the prior <c>P(tag | word)</c> of the possible tags of a word.
    /// </summary>
    /// <param name="allowed">The possible tags of the word.</param>
    /// <param name="generic">The generic share of each possible tag (the tag prior renormalized over the possible tags; the values sum to 1), parallel to <paramref name="allowed"/>.</param>
    /// <param name="slotSmoothing">The constant added to the weight of every slot.</param>
    /// <param name="senseWeight">The weight of every sense.</param>
    /// <param name="exponent">The exponent of the weight of a part of speech.</param>
    /// <param name="prior">Receives the prior of each possible tag, parallel to <paramref name="allowed"/>; the values sum to 1.</param>
    /// <returns>True when at least one possible tag has a slot; false when the word has no evidence (then <paramref name="prior"/> is the generic share).</returns>
    /// <exception cref="InvalidDataException">Thrown when a weight or the sum of the weights is not a finite number (a parameter outside its range).</exception>
    public bool TryFillPrior(ReadOnlySpan<int> allowed, ReadOnlySpan<double> generic, double slotSmoothing, double senseWeight, double exponent, Span<double> prior)
    {
        // The weights are kept as logarithms and shifted by the largest one before they are exponentiated, so that a large
        // exponent cannot overflow the sum: the largest weight is 1 and the sum lies between 1 and the number of tags
        var catalogShare = 0.0;
        var maxLog = double.NegativeInfinity;
        for (var j = 0; j < allowed.Length; j++)
        {
            var tag = allowed[j];
            prior[j] = double.NegativeInfinity;
            if (_slots[tag] == 0)
            {
                continue;
            }

            catalogShare += generic[j];
            var weight = _tagCounts[tag] + (slotSmoothing * _slots[tag]) + (senseWeight * _senses[tag]);
            if (weight == 0)
            {
                continue;
            }

            var logWeight = exponent * Math.Log(weight);
            if (!double.IsFinite(logWeight))
            {
                throw new InvalidDataException("The word prior has a weight that is not a finite number.");
            }

            prior[j] = logWeight;
            maxLog = Math.Max(maxLog, logWeight);
        }

        if (double.IsNegativeInfinity(maxLog))
        {
            generic[..allowed.Length].CopyTo(prior);
            return false;
        }

        var weightSum = 0.0;
        for (var j = 0; j < allowed.Length; j++)
        {
            if (_slots[allowed[j]] == 0)
            {
                prior[j] = 0;
                continue;
            }

            prior[j] = Math.Exp(prior[j] - maxLog);
            weightSum += prior[j];
        }

        if (!double.IsFinite(weightSum) || weightSum < 1)
        {
            throw new InvalidDataException("The word prior has a sum of weights that is not a finite number.");
        }

        for (var j = 0; j < allowed.Length; j++)
        {
            prior[j] = _slots[allowed[j]] == 0 ? generic[j] : catalogShare * prior[j] / weightSum;
        }

        return true;
    }

    private sealed record CacheEntry(string Key, ImmutableArray<WordDetails> Analyses, WordSupport? Support);
}
