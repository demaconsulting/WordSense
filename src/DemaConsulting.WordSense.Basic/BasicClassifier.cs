using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The classifier of the basic model: a statistical part-of-speech tagger and a context cue model choose among the
///     lexicon analyses of each ambiguous word; the classifier scores analyses, not fine senses.
/// </summary>
/// <remarks>
///     <para>
///     A word with no lexicon analysis (or no letter) is <see cref="ClassifyResult.Unknown"/>: it is not known, has no
///     analysis, and is at the <see cref="SenseLevel.None"/> level with confidence 0. A word with one analysis lists it
///     with probability 1. For a word with several analyses, the text is split into sentence
///     segments at the tokens ".", "!" and "?"; the tagger computes the part-of-speech posterior of every word of the
///     segment, the cue model scores the analyses from the neighboring words, and <see cref="SenseFusion"/> turns both
///     into one probability per analysis. The senses belong to a lemma and part of speech (a <i>slot</i>), not to the
///     analyses of a slot that differ only in features (<c>cut</c> as an infinitive and as a past tense), so the
///     probability of a slot is the sum of the probabilities of its analyses, the context cues alone split it among
///     the analyses (the analyses without a cue score share it equally, and together weigh as one), and the split never
///     reaches the level or the confidence. <see cref="ClassifyResult.Analyses"/> lists every analysis with its
///     probability, the most probable slot first and in a slot the most probable analysis first (ties keep the order of
///     the lexicon), and the selected analysis is the first one: the best analysis of the most probable slot.
///     The posterior of the auxiliary state decides the
///     <see cref="PartOfSpeech.Auxiliary"/> analysis of the modal and helping verbs (<c>will</c>, <c>is</c>), except right
///     after an article or a possessive, where the word is a noun or a main verb (<c>the can</c>, <c>his will</c>).
///     </para>
///     <para>
///     The statistics carry no evidence that separates fine senses or groups, so the classifier never scores the
///     senses of an analysis that has several. An analysis whose lemma and part of speech have exactly one sense lists
///     that sense with probability 1 (no evidence is needed to choose it); every other analysis lists no senses. When
///     the selected analysis has exactly one sense the result is at the <see cref="SenseLevel.Sense"/> level with that
///     sense (and its group, normally none) and the confidence is the probability of its slot; otherwise the result
///     is at the <see cref="SenseLevel.PartOfSpeech"/> level with the probability of the slot as the confidence, so
///     the confidence of an ambiguous word is at least one over the number of its slots and a known word never goes
///     unanswered. The classifier never answers at the <see cref="SenseLevel.Group"/> level.
///     </para>
///     <para>
///     The analyses array of a word with a single analysis is built once and shared by every result for that word
///     (a small thread-safe cache), so classifying common words does not allocate it again.
///     </para>
///     <para>
///     The classifier holds no mutable state, so one instance may be used from many threads at once, and the result
///     depends only on the input words. It is internal: <see cref="BasicWordSenseModel"/> delegates to it.
///     </para>
/// </remarks>
internal sealed class BasicClassifier
{
    private const int SingleCacheSize = 1024;

    private readonly BasicTables? _tables;
    private readonly Func<string, ImmutableArray<WordDetails>> _lexicon;
    private readonly Func<string, PartOfSpeech, int> _senseCount;
    private readonly Func<string, PartOfSpeech, ImmutableArray<SenseInfo>> _sensesOf;

    // A small direct-mapped cache of the answer for a word with a single analysis. The entries are immutable, so a
    // racing writer can only replace one valid entry with another.
    private readonly SingleEntry?[] _singles = new SingleEntry?[SingleCacheSize];

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicClassifier"/> class that uses the embedded table and the
    ///     lexicon of the core library.
    /// </summary>
    public BasicClassifier()
        : this(null, Lookup, CountSenses, WordLexicon.SensesOf)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicClassifier"/> class with explicit statistics and lexicon.
    /// </summary>
    /// <param name="tables">The statistics, or null for the embedded table (loaded on first use).</param>
    /// <param name="lexicon">The lexicon: the analyses of a lower-case word, or the default array when it is unknown.</param>
    /// <param name="senseCount">The number of senses of a lemma and part of speech (0 when it has none).</param>
    /// <param name="sensesOf">The senses of a lemma and part of speech; asked only for a lemma and part of speech that have exactly one sense.</param>
    internal BasicClassifier(
        BasicTables? tables,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        Func<string, PartOfSpeech, int> senseCount,
        Func<string, PartOfSpeech, ImmutableArray<SenseInfo>> sensesOf)
    {
        _tables = tables;
        _lexicon = lexicon;
        _senseCount = senseCount;
        _sensesOf = sensesOf;
    }

    private static ImmutableArray<WordDetails> Lookup(string key) => WordLexicon.TryLookup(key, out var analyses) ? analyses : [];

    private static int CountSenses(string lemma, PartOfSpeech pos) => WordLexicon.Current.SenseCountOf(lemma, pos);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An array parallel to <paramref name="words"/>.</returns>
    public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var results = new ClassifyResult[list.Count];
        if (list.Count == 0)
        {
            return Task.FromResult(results);
        }

        var tables = _tables ?? BasicTables.Default;
        var lower = new string[list.Count];
        for (var i = 0; i < lower.Length; i++)
        {
            lower[i] = Lower(list[i]);
        }

        var tokens = ArrayPool<TaggerToken>.Shared.Rent(list.Count);
        var posteriors = ArrayPool<double>.Shared.Rent(list.Count * PosTag.Count);
        var features = new FeatureRef[CueModel.MaxFeatures];
        try
        {
            var start = 0;
            while (start < list.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = start;
                while (end < list.Count - 1 && !IsTerminator(list[end]))
                {
                    end++;
                }

                var length = end - start + 1;
                ClassifySegment(tables, list, lower, start, length, tokens, posteriors, features, results);
                start = end + 1;
            }
        }
        finally
        {
            ArrayPool<TaggerToken>.Shared.Return(tokens, clearArray: true);
            ArrayPool<double>.Shared.Return(posteriors);
        }

        return Task.FromResult(results);
    }

    /// <summary>
    ///     Finds the sentence segment around a position.
    /// </summary>
    /// <param name="words">The words as written.</param>
    /// <param name="index">The position.</param>
    /// <returns>The index of the first word of the segment and its length; a segment ends after a ".", "!" or "?" token.</returns>
    internal static (int Start, int Length) SegmentOf(IReadOnlyList<string> words, int index)
    {
        ArgumentNullException.ThrowIfNull(words);
        var start = index;
        while (start > 0 && !IsTerminator(words[start - 1]))
        {
            start--;
        }

        var end = index;
        while (end < words.Count - 1 && !IsTerminator(words[end]))
        {
            end++;
        }

        return (start, end - start + 1);
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe; a token without a letter becomes empty.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lexicon key, or an empty string for punctuation.</returns>
    internal static string Normalize(string word)
    {
        var key = Lower(word);
        return CueModel.HasLetter(key) ? key : string.Empty;
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lower-case token.</returns>
    internal static string Lower(string word) => word.ToLowerInvariant().Replace('\u2019', '\'');

    private static bool IsTerminator(string word) => word is "." or "!" or "?";

    /// <summary>
    ///     Tells whether a word is an article or a possessive determiner, after which an auxiliary verb cannot follow
    ///     (<c>the can</c>, <c>his will</c>): the word that follows is a noun. Quantifiers such as <c>all</c> or
    ///     <c>some</c> are not in the set because they can precede an auxiliary (<c>all can see</c>).
    /// </summary>
    /// <param name="lowerWord">The lower-case previous word.</param>
    /// <returns>True when the word is an article or a possessive determiner.</returns>
    private static bool IsNounDeterminer(string lowerWord) =>
        lowerWord is "the" or "a" or "an" or "my" or "your" or "his" or "its" or "our" or "their";

    /// <summary>
    ///     Creates the tagger token of a written word.
    /// </summary>
    /// <param name="lexicon">The lexicon: the analyses of a lower-case word, or the default array when it is unknown.</param>
    /// <param name="word">The word as written.</param>
    /// <returns>A fixed token for a word without a letter (a number or punctuation), otherwise a word token with the parts of speech the lexicon lists.</returns>
    internal static TaggerToken MakeToken(Func<string, ImmutableArray<WordDetails>> lexicon, string word)
    {
        var key = Normalize(word);
        if (key.Length == 0)
        {
            return TaggerToken.Fixed(word.Any(char.IsDigit) ? PosTag.Other : PosTag.Punctuation);
        }

        var found = lexicon(key);
        var mask = found.IsDefaultOrEmpty ? 0 : MaskOf(found);
        return TaggerToken.Word(key, mask, char.IsUpper(word[0]));
    }

    private static int MaskOf(ImmutableArray<WordDetails> analyses)
    {
        var mask = 0;
        foreach (var analysis in analyses)
        {
            mask |= 1 << PosTag.Of(analysis.PartOfSpeech);
        }

        return mask;
    }

    private ClassifyResult Choose(BasicTables tables, string word, string key, ImmutableArray<WordDetails> analyses, ReadOnlySpan<double> posterior, ReadOnlySpan<FeatureRef> features, bool afterDeterminer)
    {
        var count = analyses.Length;
        Span<double> cues = count <= 32 ? stackalloc double[32] : new double[count];
        Span<double> probabilities = count <= 32 ? stackalloc double[32] : new double[count];
        cues = cues[..count];
        probabilities = probabilities[..count];

        // The cue table of the word itself (a form that spans lemmas) or of the lemma of its first analysis scores the
        // analyses; without a table no analysis has a cue score, so the context cannot separate the analyses of a
        // lemma and part of speech and the fusion relies on the tagger alone
        if (tables.FindCue(key, analyses) is { } cue)
        {
            CueModel.Score(cue, features, analyses, cues);
        }
        else
        {
            cues.Fill(double.NaN);
        }

        // After an article or a possessive the word is not an auxiliary: remove that mass from the posterior
        ReadOnlySpan<double> effective = posterior;
        if (afterDeterminer && posterior[PosTag.Auxiliary] > 0)
        {
            var adjusted = posterior.ToArray();
            adjusted[PosTag.Auxiliary] = 0;
            effective = adjusted;
        }

        SenseFusion.Fuse(effective, analyses, cues, tables.HmmWeight, tables.InvTemperature, probabilities);
        var list = ListAnalyses(analyses, probabilities, out var selectedGroup, out var slotProbability);
        return Answer(word, list, selectedGroup, slotProbability);
    }

    /// <summary>
    ///     Builds the candidates of a word: every analysis with its probability and with the one sense of an analysis
    ///     that has exactly one sense. The slots (lemma and part of speech) come in the order of their probability, the
    ///     largest first, and in a slot the analyses come in the order of their probability, so the first candidate is
    ///     the best analysis of the most probable slot; ties keep the order of the lexicon.
    /// </summary>
    /// <param name="analyses">The analyses of the word in lexicon order.</param>
    /// <param name="probabilities">The probability of each analysis, parallel to <paramref name="analyses"/>.</param>
    /// <param name="selectedGroup">Receives the group of the only sense of the first candidate, or null.</param>
    /// <param name="slotProbability">Receives the probability of the slot of the first candidate: the sum of the probabilities of its analyses.</param>
    /// <returns>The candidates, sorted.</returns>
    private ImmutableArray<AnalysisCandidate> ListAnalyses(ImmutableArray<WordDetails> analyses, ReadOnlySpan<double> probabilities, out string? selectedGroup, out double slotProbability)
    {
        var count = analyses.Length;
        var candidates = new AnalysisCandidate[count];
        string?[]? groups = null;
        Span<int> first = count <= 32 ? stackalloc int[32] : new int[count];
        Span<double> slotTotal = count <= 32 ? stackalloc double[32] : new double[count];
        slotTotal.Clear();
        for (var j = 0; j < count; j++)
        {
            var senses = OnlySenseOf(analyses[j], out var group);
            candidates[j] = new AnalysisCandidate(analyses[j], probabilities[j], senses);
            if (group is not null)
            {
                groups ??= new string?[count];
                groups[j] = group;
            }

            // The first analysis of the slot stands for the slot
            first[j] = j;
            for (var k = 0; k < j; k++)
            {
                if (SenseFusion.SameSlot(analyses[k], analyses[j]))
                {
                    first[j] = first[k];
                    break;
                }
            }

            slotTotal[first[j]] += probabilities[j];
        }

        // A stable insertion sort: the number of analyses is small and a tie keeps the lexicon order
        Span<int> order = count <= 32 ? stackalloc int[32] : new int[count];
        for (var j = 0; j < count; j++)
        {
            var at = j;
            while (at > 0 && Precedes(j, order[at - 1], first, slotTotal, probabilities))
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = j;
        }

        var sorted = new AnalysisCandidate[count];
        for (var j = 0; j < count; j++)
        {
            sorted[j] = candidates[order[j]];
        }

        selectedGroup = groups?[order[0]];
        slotProbability = slotTotal[first[order[0]]];
        return ImmutableCollectionsMarshal.AsImmutableArray(sorted);
    }

    // Tells whether the analysis a, which comes after b in the lexicon, must be listed before b: when its slot is more
    // probable, or when the slots are equally probable and its slot comes first in the lexicon, or when the slot is the
    // same and the analysis is more probable
    private static bool Precedes(int a, int b, ReadOnlySpan<int> first, ReadOnlySpan<double> slotTotal, ReadOnlySpan<double> probabilities)
    {
        if (first[a] == first[b])
        {
            return probabilities[a] > probabilities[b];
        }

        var byTotal = slotTotal[first[a]].CompareTo(slotTotal[first[b]]);
        return byTotal > 0 || (byTotal == 0 && first[a] < first[b]);
    }

    /// <summary>
    ///     Lists the only sense of an analysis, with probability 1, when its lemma and part of speech have exactly one
    ///     sense.
    /// </summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="group">Receives the group of that sense (normally none).</param>
    /// <returns>The one sense, or the empty array when the analysis has none or several.</returns>
    private ImmutableArray<SenseCandidate> OnlySenseOf(WordDetails analysis, out string? group)
    {
        group = null;
        if (_senseCount(analysis.Lemma, analysis.PartOfSpeech) == 1
            && _sensesOf(analysis.Lemma, analysis.PartOfSpeech) is { Length: 1 } senses)
        {
            group = senses[0].GroupId;
            return [new SenseCandidate(senses[0].SenseId, 1.0)];
        }

        return [];
    }

    /// <summary>
    ///     Answers with the first candidate: at the sense level when it has exactly one sense, otherwise at the
    ///     part-of-speech level; the confidence is the probability of the slot (lemma and part of speech) of the
    ///     candidate, times the probability 1 of its sense.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="list">The sorted candidates of the word.</param>
    /// <param name="selectedGroup">The group of the only sense of the first candidate, or null.</param>
    /// <param name="slotProbability">The probability of the slot of the first candidate.</param>
    /// <returns>The answer.</returns>
    private static ClassifyResult Answer(string word, ImmutableArray<AnalysisCandidate> list, string? selectedGroup, double slotProbability)
    {
        var selected = list[0];
        return selected.Senses.Length == 1
            ? new ClassifyResult(word, true, list, selected.Details, SenseLevel.Sense, slotProbability * selected.Senses[0].Probability, selectedGroup, selected.Senses[0].SenseId)
            : new ClassifyResult(word, true, list, selected.Details, SenseLevel.PartOfSpeech, slotProbability, null, null);
    }

    /// <summary>
    ///     Answers a word that has exactly one analysis. The analysis has probability 1, so the answer is the same for
    ///     every occurrence of the word: it is built once, with its analyses array, and then shared.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="key">The lower-case lexicon key of the word.</param>
    /// <param name="analysis">The only analysis of the word.</param>
    /// <returns>The answer.</returns>
    private ClassifyResult AnswerSingleAnalysis(string word, string key, WordDetails analysis)
    {
        var slot = (key.GetHashCode() & int.MaxValue) % SingleCacheSize;
        var cached = Volatile.Read(ref _singles[slot]);
        if (cached is null || !string.Equals(cached.Key, key, StringComparison.Ordinal))
        {
            var list = ListAnalyses([analysis], [1.0], out var group, out var slotProbability);
            cached = new SingleEntry(key, Answer(word, list, group, slotProbability));
            Volatile.Write(ref _singles[slot], cached);
        }

        var template = cached.Template;
        return new ClassifyResult(word, true, template.Analyses, template.Details, template.Level, template.Confidence, template.GroupId, template.SenseId);
    }

    private sealed record SingleEntry(string Key, ClassifyResult Template);

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Private hot path that passes pre-allocated scratch buffers; bundling them would allocate per call.")]
    private void ClassifySegment(
        BasicTables tables,
        List<string> words,
        string[] lower,
        int start,
        int length,
        TaggerToken[] tokens,
        double[] posteriors,
        FeatureRef[] features,
        ClassifyResult[] results)
    {
        for (var i = 0; i < length; i++)
        {
            tokens[i] = MakeToken(_lexicon, words[start + i]);
        }

        PosTagger.Posteriors(tables, tokens.AsSpan(0, length), posteriors.AsSpan(0, length * PosTag.Count));
        for (var i = 0; i < length; i++)
        {
            var word = words[start + i];
            var key = Normalize(word);
            var analyses = key.Length == 0 ? default : _lexicon(key);
            if (analyses.IsDefaultOrEmpty)
            {
                results[start + i] = ClassifyResult.Unknown(word);
            }
            else if (analyses.Length == 1)
            {
                results[start + i] = AnswerSingleAnalysis(word, key, analyses[0]);
            }
            else
            {
                var featureCount = CueModel.Extract(lower.AsSpan(start, length), i, features);
                results[start + i] = Choose(tables, word, key, analyses, posteriors.AsSpan(i * PosTag.Count, PosTag.Count), features.AsSpan(0, featureCount), i > 0 && IsNounDeterminer(lower[start + i - 1]));
            }
        }
    }
}
