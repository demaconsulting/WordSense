using System.Buffers;

namespace DemaConsulting.WordSense;

/// <summary>
///     One token of a sentence as the tagger sees it.
/// </summary>
/// <param name="Key">The lower-case lookup key of the word; ignored when <paramref name="ForcedTag"/> is set.</param>
/// <param name="DictionaryMask">The bit mask (bit t is tag t) of the parts of speech the dictionary lists for the word; 0 when the word is not in the dictionary.</param>
/// <param name="Capitalized">Whether the word starts with an upper-case letter.</param>
/// <param name="ForcedTag">The only possible tag of a token that is not a word (a number or punctuation), or -1 for a word.</param>
internal readonly record struct TaggerToken(string Key, int DictionaryMask, bool Capitalized, int ForcedTag)
{
    /// <summary>
    ///     Creates a word token.
    /// </summary>
    /// <param name="key">The lower-case key.</param>
    /// <param name="dictionaryMask">The tag mask of the dictionary entries, or 0 for an unknown word.</param>
    /// <param name="capitalized">Whether the written word starts with an upper-case letter.</param>
    /// <returns>The token.</returns>
    public static TaggerToken Word(string key, int dictionaryMask, bool capitalized) => new(key, dictionaryMask, capitalized, -1);

    /// <summary>
    ///     Creates a token that has exactly one possible tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The token.</returns>
    public static TaggerToken Fixed(int tag) => new(string.Empty, 0, false, tag);
}

/// <summary>
///     The part-of-speech tagger: a second-order hidden Markov model over thirteen tags that returns, for every token of
///     one sentence, the posterior probability of each tag given the whole sentence.
/// </summary>
/// <remarks>
///     <para>
///     The possible tags of a word are those the dictionary lists for it plus its closed-class tags from the table; a
///     word the dictionary does not know is a noun when it is capitalized inside the sentence, otherwise an open-class
///     word whose distribution follows its longest known suffix. The emission weight of a possible tag is
///     <c>(p(tag | word) / p(tag)) ^ gamma</c>, where <c>p(tag | word)</c> mixes the training counts of the word with the
///     prior of the possible tags (beta pseudo counts); a word without counts has equal weights. Transitions are the
///     interpolation of trigram, bigram and unigram tag probabilities. The posterior comes from the scaled
///     forward-backward algorithm restricted to the possible tags of each position, so the work per word is the product
///     of the possible-tag counts of three neighbors rather than the full tag set. The decoding does not allocate
///     per word: scratch space is rented from the shared array pool.
///     </para>
/// </remarks>
internal static class PosTagger
{
    private const int K = PosTag.Count;
    private const int B = PosTag.Boundary;
    private const int X = PosTag.Extended;

    /// <summary>
    ///     Computes the tag posteriors of every token of a sentence.
    /// </summary>
    /// <param name="tables">The statistics.</param>
    /// <param name="sentence">The tokens of one sentence, in order.</param>
    /// <param name="posteriors">Receives <see cref="PosTag.Count"/> probabilities per token, row-major; each row sums to 1 over the possible tags and is 0 elsewhere.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="posteriors"/> is too small.</exception>
    public static void Posteriors(BasicTables tables, ReadOnlySpan<TaggerToken> sentence, Span<double> posteriors)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var n = sentence.Length;
        if (posteriors.Length < n * K)
        {
            throw new ArgumentException("The result buffer is too small.", nameof(posteriors));
        }

        if (n == 0)
        {
            return;
        }

        posteriors[..(n * K)].Clear();
        var ints = ArrayPool<int>.Shared.Rent((n * K) + (2 * n) + 1);
        var emissions = ArrayPool<double>.Shared.Rent(n * K);
        double[]? alpha = null;
        double[]? beta = null;
        try
        {
            var allowed = ints.AsSpan(0, n * K);
            var counts = ints.AsSpan(n * K, n);
            var offsets = ints.AsSpan((n * K) + n, n + 1);
            var emission = emissions.AsSpan(0, n * K);
            emission.Clear();
            var total = 0;
            for (var i = 0; i < n; i++)
            {
                counts[i] = FillEmission(tables, sentence[i], i, emission.Slice(i * K, K), allowed.Slice(i * K, K));
                offsets[i] = total;
                total += (i == 0 ? 1 : counts[i - 1]) * counts[i];
            }

            offsets[n] = total;
            alpha = ArrayPool<double>.Shared.Rent(total);
            beta = ArrayPool<double>.Shared.Rent(total);
            var lattice = new Lattice(tables.Transition.AsSpan(), emission, allowed, counts, offsets, alpha.AsSpan(0, total), beta.AsSpan(0, total));
            lattice.Forward();
            lattice.Backward();
            lattice.Combine(posteriors);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ints);
            ArrayPool<double>.Shared.Return(emissions);
            if (alpha is not null)
            {
                ArrayPool<double>.Shared.Return(alpha);
            }

            if (beta is not null)
            {
                ArrayPool<double>.Shared.Return(beta);
            }
        }
    }

    /// <summary>
    ///     Fills the emission weights and the possible tags of one token.
    /// </summary>
    /// <param name="tables">The statistics.</param>
    /// <param name="token">The token.</param>
    /// <param name="position">The position of the token in its sentence.</param>
    /// <param name="row">Receives the emission weight of each possible tag; the span is zeroed by the caller.</param>
    /// <param name="allowed">Receives the possible tags in ascending order.</param>
    /// <returns>The number of possible tags, at least 1.</returns>
    private static int FillEmission(BasicTables tables, TaggerToken token, int position, Span<double> row, Span<int> allowed)
    {
        if (token.ForcedTag >= 0)
        {
            row[token.ForcedTag] = 1;
            allowed[0] = token.ForcedTag;
            return 1;
        }

        tables.Words.TryGetValue(token.Key, out var entry);
        var mask = token.DictionaryMask | (entry?.ClosedMask ?? 0);
        if (mask == 0)
        {
            return FillUnknown(tables, token, position, row, allowed);
        }

        var count = 0;
        var priorSum = 0.0;
        for (var t = 0; t < K; t++)
        {
            if ((mask & (1 << t)) != 0)
            {
                allowed[count++] = t;
                priorSum += tables.TagPrior[t];
            }
        }

        var total = entry?.Total ?? 0;
        for (var j = 0; j < count; j++)
        {
            var t = allowed[j];
            var q = tables.TagPrior[t] / priorSum;
            var seen = entry?.Counts[t] ?? 0;
            var probability = (seen + (tables.Beta * q)) / (total + tables.Beta);
            row[t] = Math.Pow(probability / tables.TagPrior[t], tables.Gamma);
        }

        return count;
    }

    private static int FillUnknown(BasicTables tables, TaggerToken token, int position, Span<double> row, Span<int> allowed)
    {
        if (token.Capitalized && position > 0)
        {
            row[PosTag.Noun] = 1;
            allowed[0] = PosTag.Noun;
            return 1;
        }

        var distribution = tables.TryFindSuffix(token.Key, out var known) ? known : tables.SuffixFallback;
        for (var j = 0; j < PosTag.OpenClasses.Length; j++)
        {
            var t = PosTag.OpenClasses[j];
            allowed[j] = t;
            row[t] = distribution[j] / tables.TagPrior[t];
        }

        return PosTag.OpenClasses.Length;
    }

    /// <summary>
    ///     The sparse forward-backward lattice of one sentence: for each position a matrix over the possible tags of the
    ///     previous position (the boundary for the first position) and of the position itself.
    /// </summary>
    private readonly ref struct Lattice
    {
        private readonly ReadOnlySpan<double> _transition;
        private readonly Span<double> _emission;
        private readonly Span<int> _allowed;
        private readonly Span<int> _counts;
        private readonly Span<int> _offsets;
        private readonly Span<double> _alpha;
        private readonly Span<double> _beta;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Lattice"/> struct.
        /// </summary>
        /// <param name="transition">The transition probabilities.</param>
        /// <param name="emission">The emission weights, <see cref="PosTag.Count"/> per position.</param>
        /// <param name="allowed">The possible tags, <see cref="PosTag.Count"/> slots per position.</param>
        /// <param name="counts">The number of possible tags of each position.</param>
        /// <param name="offsets">The start of each position's matrix in the forward and backward buffers.</param>
        /// <param name="alpha">The forward buffer.</param>
        /// <param name="beta">The backward buffer.</param>
        public Lattice(
            ReadOnlySpan<double> transition,
            Span<double> emission,
            Span<int> allowed,
            Span<int> counts,
            Span<int> offsets,
            Span<double> alpha,
            Span<double> beta)
        {
            _transition = transition;
            _emission = emission;
            _allowed = allowed;
            _counts = counts;
            _offsets = offsets;
            _alpha = alpha;
            _beta = beta;
        }

        /// <summary>
        ///     Runs the scaled forward pass.
        /// </summary>
        public void Forward()
        {
            var first = _alpha.Slice(_offsets[0], _counts[0]);
            for (var c = 0; c < first.Length; c++)
            {
                var tag = Tag(0, c);
                first[c] = Probability(B, B, tag) * Emission(0, tag);
            }

            Normalize(first);
            for (var i = 1; i < _counts.Length; i++)
            {
                var previousCount = _counts[i - 1];
                var count = _counts[i];
                var beforeCount = PreviousCount(i - 1);
                var current = _alpha.Slice(_offsets[i], previousCount * count);
                var previous = _alpha.Slice(_offsets[i - 1], beforeCount * previousCount);
                for (var bi = 0; bi < previousCount; bi++)
                {
                    var b = Tag(i - 1, bi);
                    for (var ci = 0; ci < count; ci++)
                    {
                        var c = Tag(i, ci);
                        var sum = 0.0;
                        for (var ai = 0; ai < beforeCount; ai++)
                        {
                            sum += previous[(ai * previousCount) + bi] * Probability(PreviousTag(i - 1, ai), b, c);
                        }

                        current[(bi * count) + ci] = sum * Emission(i, c);
                    }
                }

                Normalize(current);
            }
        }

        /// <summary>
        ///     Runs the scaled backward pass.
        /// </summary>
        public void Backward()
        {
            var last = _counts.Length - 1;
            var lastCount = _counts[last];
            var lastPrevious = PreviousCount(last);
            var end = _beta.Slice(_offsets[last], lastPrevious * lastCount);
            for (var bi = 0; bi < lastPrevious; bi++)
            {
                for (var ci = 0; ci < lastCount; ci++)
                {
                    end[(bi * lastCount) + ci] = Probability(PreviousTag(last, bi), Tag(last, ci), B);
                }
            }

            for (var i = last - 1; i >= 0; i--)
            {
                var previousCount = PreviousCount(i);
                var count = _counts[i];
                var nextCount = _counts[i + 1];
                var current = _beta.Slice(_offsets[i], previousCount * count);
                var next = _beta.Slice(_offsets[i + 1], count * nextCount);
                for (var bi = 0; bi < previousCount; bi++)
                {
                    var b = PreviousTag(i, bi);
                    for (var ci = 0; ci < count; ci++)
                    {
                        var c = Tag(i, ci);
                        var sum = 0.0;
                        for (var di = 0; di < nextCount; di++)
                        {
                            var d = Tag(i + 1, di);
                            sum += Probability(b, c, d) * Emission(i + 1, d) * next[(ci * nextCount) + di];
                        }

                        current[(bi * count) + ci] = sum;
                    }
                }

                Normalize(current);
            }
        }

        /// <summary>
        ///     Combines both passes into the tag posteriors of every position.
        /// </summary>
        /// <param name="posteriors">Receives <see cref="PosTag.Count"/> probabilities per position, zeroed by the caller.</param>
        public void Combine(Span<double> posteriors)
        {
            for (var i = 0; i < _counts.Length; i++)
            {
                var previousCount = PreviousCount(i);
                var count = _counts[i];
                var forward = _alpha.Slice(_offsets[i], previousCount * count);
                var backward = _beta.Slice(_offsets[i], previousCount * count);
                var row = posteriors.Slice(i * K, K);
                var total = 0.0;
                for (var ci = 0; ci < count; ci++)
                {
                    var sum = 0.0;
                    for (var bi = 0; bi < previousCount; bi++)
                    {
                        sum += forward[(bi * count) + ci] * backward[(bi * count) + ci];
                    }

                    row[Tag(i, ci)] = sum;
                    total += sum;
                }

                if (total > 0)
                {
                    for (var ci = 0; ci < count; ci++)
                    {
                        row[Tag(i, ci)] /= total;
                    }

                    continue;
                }

                // The path is impossible under the transitions: fall back to the normalized emissions
                FillFromEmissions(i, count, row);
            }
        }

        private void FillFromEmissions(int position, int count, Span<double> row)
        {
            var emissionTotal = 0.0;
            for (var ci = 0; ci < count; ci++)
            {
                emissionTotal += Emission(position, Tag(position, ci));
            }

            for (var ci = 0; ci < count; ci++)
            {
                var tag = Tag(position, ci);
                row[tag] = emissionTotal > 0 ? Emission(position, tag) / emissionTotal : 1.0 / count;
            }
        }

        private static void Normalize(Span<double> values)
        {
            var sum = 0.0;
            foreach (var value in values)
            {
                sum += value;
            }

            if (sum > 0)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] /= sum;
                }
            }
        }

        private int Tag(int position, int index) => _allowed[(position * K) + index];

        private int PreviousCount(int position) => position == 0 ? 1 : _counts[position - 1];

        private int PreviousTag(int position, int index) => position == 0 ? B : Tag(position - 1, index);

        private double Emission(int position, int tag) => _emission[(position * K) + tag];

        private double Probability(int first, int second, int third) => _transition[(((first * X) + second) * X) + third];
    }
}
