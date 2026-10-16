namespace DemaConsulting.WordSense;

/// <summary>
///     Classifies ambiguous words in context: which lemma, part of speech and (for a heteronym) pronunciation a word has.
/// </summary>
/// <remarks>
///     <para>
///     The primary input is a list of words that the caller has already tokenized; the result array is parallel to
///     that list. Every call returns a full <see cref="WordAnalysis"/> for every word: the best answer, the runner-up and
///     the confidence figures. There are no options, flags or overloads that select how much detail is returned.
///     </para>
///     <para>
///     Terminology: a <em>lemma</em> is the dictionary base form of a word (for example <c>record</c> for
///     <c>records</c>); a <em>word</em> is the input as written; a <em>candidate</em> (<see cref="WordDetails"/>) is one
///     possible answer for a lemma: a part of speech and, for a heteronym, a pronunciation. Every lemma is an entry
///     with one or more candidates (its senses) and, for the lemmas the classifier was trained on, weights that choose
///     between them. Words are matched to their lemma through the table's known forms (inflections), so
///     <c>records</c> and <c>recorded</c> resolve to <c>record</c>; <see cref="WordAnalysis.Word"/> is the input and
///     <see cref="WordDetails.Lemma"/> the base form.
///     A <em>heteronym</em> is a lemma with at least two distinct meanings that are pronounced differently; only for a
///     heteronym is <see cref="WordDetails.Pronunciation"/> set. Create an instance with
///     <see cref="WordSenseClassifier.Create"/> (lexicon only) or a model package's factory.
///     </para>
///     <para>
///     <b>One flow.</b> Each word is resolved to an entry. An entry with exactly one candidate is a
///     <em>dictionary answer</em> (<see cref="AnswerBasis.Dictionary"/>): no context is needed, so the margins are 0.
///     An entry with several candidates and weights is a <em>context answer</em> (<see cref="AnswerBasis.Context"/>):
///     each candidate gets a score, the <em>margin</em> is the best score minus the runner-up's score, and the classifier
///     <em>abstains</em> when the margin is below the lemma's abstain threshold. An entry with several candidates and
///     no weights abstains with <see cref="AnswerBasis.Dictionary"/> and lists its candidates. A word that is not found
///     is unknown (<see cref="AnswerBasis.None"/>). The encoder runs only when at least one word of the input needs
///     context. <see cref="WordSenseClassifierOptions.UseLexicon"/> set to false restricts the answers to the lemmas that have a trained
///     classifier.
///     </para>
///     <para>
///     <b>Catalog.</b> The context classifier answers the parts of speech <see cref="PartOfSpeech.Noun"/>,
///     <see cref="PartOfSpeech.Verb"/>, <see cref="PartOfSpeech.Adjective"/> and <see cref="PartOfSpeech.Adverb"/>. Discover
///     what it can disambiguate with <see cref="ContextLemmas"/> and the possible answers of any lemma with
///     <see cref="GetCandidates"/>: for <c>lead</c> the candidates are
///     a noun and a verb with the meaning <c>guide</c> and a noun with the meaning <c>metal</c>. The complete list is the
///     user guide appendix
///     <c>https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md</c>.
///     </para>
///     <para>
///     <b>Results.</b> A word that is not known (not in the table, rejected by the case rule below, or a trained lemma
///     that yields no tokens) has <see cref="WordAnalysis.Details"/> null, <see cref="WordAnalysis.Abstained"/> false,
///     <see cref="WordAnalysis.Basis"/> <see cref="AnswerBasis.None"/> and no candidates. A known word whose margin is
///     below the threshold abstains: <see cref="WordAnalysis.Details"/> is null, <see cref="WordAnalysis.Abstained"/> is true
///     and <see cref="WordAnalysis.Margin"/> and <see cref="WordAnalysis.RunnerUp"/> are still set. Every known word
///     reports its <see cref="WordAnalysis.Candidates"/> (for a word derived from a dictionary word by morphology,
///     the one derived sense). See <see cref="WordAnalysis"/> for the field values of each case.
///     </para>
///     <para>
///     <b>Running text and context.</b> The list overload reads the whole list as <em>one running text</em> in which
///     the words are joined by single spaces. Several sentences in one list are therefore one text, not separate
///     inputs: they are encoded together and each sentence provides context for the words of the others. To classify
///     independent sentences, make one call per sentence. The same holds for the string overload. How much text the
///     model reads at once is a property of the model: the MiniLM package reads at most 256 tokens by default and
///     classifies a longer text per target from a window centered on it, so text outside the window is ignored.
///     </para>
///     <para>
///     <b>One target word.</b> To classify one word of one sentence, pass the sentence and take the entry of the target.
///     With the string overload, find the target's token by its offset. With the list overload, you must tokenize
///     yourself so that every word, and every punctuation mark, is its own element: the lookup is by the whole
///     element, so <c>"race."</c> is not a known word and must be passed as <c>"race"</c> and <c>"."</c>. The string
///     overload does this for you: it splits a text into words (letters and digits, with an inner apostrophe or hyphen
///     kept inside the word, so <c>don't</c> and <c>well-known</c> are one word) and into whitespace and single
///     punctuation characters.
///     </para>
///     <para>
///     <b>Thread safety and disposal.</b> One classifier may be shared by many threads and tasks, and its methods may
///     be called concurrently. Dispose it once, after all calls have finished; disposing while a call is in flight is
///     not supported. After <see cref="IDisposable.Dispose"/> of a classifier that owns its model (one from a model
///     package's factory), a call that has to run the model (any text
///     containing a word that needs context) throws <see cref="ObjectDisposedException"/>; a call whose words need none can still
///     return normally, and <see cref="ContextLemmas"/> and <see cref="GetCandidates"/> keep working. Disposing twice is
///     harmless.
///     </para>
///     <para>
///     Each word is matched against the table's known surface forms after lower-casing it (invariant
///     culture) and replacing the typographic apostrophe (U+2019) with <c>'</c>, so inflected forms listed in the table
///     resolve to their lemma. A possessive <c>'s</c> is also accepted for a lemma with a trained classifier: when the word
///     is not itself a known form, <c>'s</c> is removed and the rest is looked up (so <c>record's</c> resolves like
///     <c>record</c>). A word absent from the table may also be derived from a dictionary base word by a few
///     conservative suffix rules (for example a regular plural of a noun), which gives a dictionary answer.
///     </para>
///     <para>
///     <b>Case.</b> A lemma with a trained classifier matches in any case. A dictionary word that has no trained
///     classifier matches in lower case; a word that starts with a capital letter is looked up only when it is the first
///     word of the input, follows a token that is exactly <c>.</c>, <c>!</c>, <c>?</c> or <c>:</c> or an opening quote or
///     bracket, or is exactly <c>I</c>. Any other capitalized word (usually a name) is unknown.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///
///     string[] words = ["They", "will", "record", "the", "record"];
///     WordAnalysis[] analyses = await classifier.ClassifyAsync(words);
///
///     foreach (var analysis in analyses)
///     {
///         // Details is null: not an ambiguous word, or the classifier abstained
///         Console.WriteLine($"{analysis.Word}: {analysis.Details?.PartOfSpeech.ToString() ?? "-"}");
///     }
///     </code>
///     To classify one target word of one sentence, use the string overload and pick the token at the target's offset:
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///
///     const string sentence = "They went to the race.";
///     var start = sentence.IndexOf("race", StringComparison.Ordinal);
///     var tokens = await classifier.ClassifyAsync(sentence);
///     WordDetails? details = tokens.Single(t => t.Start == start).Analysis.Details;
///     Console.WriteLine(details?.PartOfSpeech.ToString() ?? "-");
///     </code>
/// </example>
public interface IWordSenseClassifier : IDisposable
{
    /// <summary>
    ///     Gets the lemmas this classifier can disambiguate from context (those with a trained classifier), in ordinal
    ///     order. Dictionary-only lemmas are not listed; use <see cref="GetCandidates"/> for any lemma.
    /// </summary>
    /// <example>
    ///     <code>
    ///     using var classifier = WordSenseClassifier.Create();
    ///     bool canClassify = classifier.ContextLemmas.Contains("record");
    ///     </code>
    /// </example>
    IReadOnlyList<string> ContextLemmas { get; }

    /// <summary>
    ///     Gets every possible answer for a lemma: each (part of speech, pronunciation) it can have. This works for any
    ///     lemma of the table, with or without a trained classifier; a dictionary-only lemma is hidden when
    ///     <see cref="WordSenseClassifierOptions.UseLexicon"/> is false.
    /// </summary>
    /// <param name="lemma">The lemma, lower-case, for example <c>record</c>; an inflected form such as <c>records</c> is not a lemma.</param>
    /// <returns>The candidates, or an empty list when the lemma is unknown.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = WordSenseClassifier.Create();
    ///     foreach (var candidate in classifier.GetCandidates("lead"))
    ///     {
    ///         Console.WriteLine($"{candidate.PartOfSpeech} {candidate.Pronunciation?.Meaning}");
    ///     }
    ///     </code>
    /// </example>
    IReadOnlyList<WordDetails> GetCandidates(string lemma);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">
    ///     The words in order, including punctuation tokens, each as its own element (<c>"race"</c> then
    ///     <c>"."</c>, never <c>"race."</c>, which is not a known word). They are read as one running
    ///     text in which the words are separated by single spaces, so several sentences in one list share context.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     A non-null array parallel to <paramref name="words"/> (empty for an empty list); see
    ///     <see cref="WordAnalysis"/> for the unknown, answered and abstained cases.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the classifier has been disposed and the encoder has to run.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = WordSenseClassifier.Create();
    ///     string[] words = ["The", "record", "was", "broken"];
    ///     WordAnalysis[] analyses = await classifier.ClassifyAsync(words);
    ///     if (analyses[1].Details is { } details)
    ///     {
    ///         Console.WriteLine($"{details.Lemma}: {details.PartOfSpeech}, margin {analyses[1].Margin:F2}");
    ///     }
    ///     </code>
    /// </example>
    Task<WordAnalysis[]> ClassifyAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Splits a raw text into tokens and attaches an analysis to every one.
    /// </summary>
    /// <param name="text">The text to classify.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Tokens that tile the text exactly (none for an empty text); see <see cref="WordSenseToken"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the classifier has been disposed and the encoder has to run.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = WordSenseClassifier.Create();
    ///     var tokens = await classifier.ClassifyAsync("They will record the record.");
    ///     foreach (var token in tokens.Where(t => t.Analysis.Details is not null))
    ///     {
    ///         Console.WriteLine($"{token.Start}: {token.Text} is {token.Analysis.Details!.PartOfSpeech}");
    ///     }
    ///     </code>
    /// </example>
    Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
///     A segment of a raw text, with its analysis.
/// </summary>
/// <remarks>
///     A sequence of tokens returned for a text tiles it exactly: concatenating every
///     <see cref="Text"/> reproduces the input, with no gaps or overlaps. Segments are words (letters
///     and digits, with internal apostrophes and hyphens), runs of whitespace, or single other
///     characters such as punctuation. For whitespace, punctuation and unknown words
///     <see cref="WordAnalysis.Details"/> is null, <see cref="WordAnalysis.Abstained"/> is false and
///     <see cref="WordAnalysis.Basis"/> is <see cref="AnswerBasis.None"/>.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///     const string text = "They will record the record.";
///     var tokens = await classifier.ClassifyAsync(text);
///
///     // The tokens tile the text, so the original can be rebuilt and offsets index into it
///     bool tiles = string.Concat(tokens.Select(t => t.Text)) == text;
///     foreach (var token in tokens.Where(t => t.Analysis.Details is not null))
///     {
///         Console.WriteLine($"{text.Substring(token.Start, token.Length)} -> {token.Analysis.Details!.PartOfSpeech}");
///     }
///     </code>
/// </example>
/// <param name="Start">Inclusive UTF-16 start offset of the segment in the input text.</param>
/// <param name="Length">Length of the segment in UTF-16 code units.</param>
/// <param name="Text">The segment text.</param>
/// <param name="Analysis">The analysis of the segment; its <see cref="WordAnalysis.Word"/> equals <paramref name="Text"/>.</param>
public sealed record WordSenseToken(int Start, int Length, string Text, WordAnalysis Analysis);

/// <summary>
///     Classifies ambiguous words using a model abstraction and an embedded lexicon, or from the lexicon alone.
/// </summary>
/// <remarks>
///     The words are resolved first; they are encoded once, and only when at least one needs context, and then every
///     such word is classified from the same pooled vectors. Without a model (<see cref="Create"/>) the lexicon
///     answers alone. The instance is thread-safe for concurrent classification calls when its model is.
///     A classifier that owns its model (for example one from the MiniLM package's factory) must be disposed (for
///     example with <c>using</c>) after every call has finished; later calls that need the model throw
///     <see cref="ObjectDisposedException"/>. Create one classifier and share it rather than creating one per request.
///     The classification methods, and how words are matched to lemmas, are described on
///     <see cref="IWordSenseClassifier"/>.
/// </remarks>
/// <example>
///     Create the classifier once, then classify as many texts as needed. This one needs no model and no network.
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///     var tokens = await classifier.ClassifyAsync("Please record the record.");
///     Console.WriteLine(string.Join(" ", tokens.Select(t => t.Analysis.Details?.PartOfSpeech.ToString() ?? t.Text)));
///     </code>
/// </example>
public sealed class WordSenseClassifier : IWordSenseClassifier
{
    private readonly IWordSenseModel? _model;
    private readonly WordSenseWeightsTable? _weights;
    private readonly WordSenseTable _table;
    private readonly double _abstainScale;
    private readonly bool _ownsModel;
    private readonly bool _useLexicon;
    private readonly string[] _lemmas;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseClassifier"/> class from its parts. Advanced: most
    ///     applications use <see cref="Create"/> or a model package's factory instead.
    /// </summary>
    /// <param name="table">The lexicon table.</param>
    /// <param name="model">The encoder model, or null to answer from the lexicon alone.</param>
    /// <param name="weights">
    ///     The weights table trained for <paramref name="model"/> and <paramref name="table"/>; null exactly when
    ///     <paramref name="model"/> is null.
    /// </param>
    /// <param name="options">Classifier options, or null for defaults.</param>
    /// <param name="ownsModel">Whether disposing this classifier disposes the model.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="table"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when only one of <paramref name="model"/> and <paramref name="weights"/> is supplied.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="WordSenseClassifierOptions.AbstainMarginScale"/> is negative or NaN.
    /// </exception>
    /// <exception cref="WordSenseModelMismatchException">
    ///     Thrown when the weights were trained with a different model than <paramref name="model"/> (identifier,
    ///     SHA-256 or dimension), or for a different lexicon table, or do not cover the table's context lemmas.
    /// </exception>
    public WordSenseClassifier(
        WordSenseTable table,
        IWordSenseModel? model = null,
        WordSenseWeightsTable? weights = null,
        WordSenseClassifierOptions? options = null,
        bool ownsModel = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        options ??= new WordSenseClassifierOptions();
        ValidateOptions(options);
        if ((model is null) != (weights is null))
        {
            throw new ArgumentException("A model and its weights table must be supplied together or not at all.", nameof(weights));
        }

        // Weights are only meaningful for the exact model and lexicon table they were trained with
        if (model is not null && weights is not null)
        {
            model.EnsureWeightsMatch(weights);
            weights.EnsureMatches(table);
        }

        _model = model;
        _weights = weights;
        _table = table;
        _abstainScale = options.AbstainMarginScale;
        _ownsModel = ownsModel && model is not null;
        _useLexicon = options.UseLexicon;
        _lemmas = model is null ? [] : table.ContextLemmas.Select(l => l.Lemma).OrderBy(l => l, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ContextLemmas => _lemmas;

    /// <summary>
    ///     Validates the range of the options.
    /// </summary>
    /// <param name="options">The options.</param>
    private static void ValidateOptions(WordSenseClassifierOptions options)
    {
        if (double.IsNaN(options.AbstainMarginScale))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AbstainMarginScale must not be NaN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.AbstainMarginScale);
    }

    /// <summary>
    ///     Creates a classifier that answers from the embedded lexicon alone: it needs no encoder model, no model files
    ///     and no network access, and it loads no ONNX Runtime.
    /// </summary>
    /// <param name="options">The options, or null for defaults.</param>
    /// <remarks>
    ///     A word with one part of speech is answered from the lexicon (<see cref="AnswerBasis.Dictionary"/>); a word
    ///     with several abstains and lists its candidates. To choose between the senses of a word from its sentence,
    ///     add a model package, for example <c>MiniLMWordSense.CreateClassifierAsync</c> of the
    ///     <c>DemaConsulting.WordSense.MiniLM</c> package. The classifier owns nothing that needs disposing, but it is
    ///     <see cref="IDisposable"/> like every classifier.
    /// </remarks>
    /// <returns>A lexicon-only classifier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <c>AbstainMarginScale</c> is negative or NaN.</exception>
    /// <exception cref="WordSenseTableFormatException">Thrown when the embedded lexicon table is missing or malformed.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = WordSenseClassifier.Create();
    ///     var tokens = await classifier.ClassifyAsync("Please record the cat.");
    ///     Console.WriteLine(string.Join(" ", tokens.Select(t => t.Analysis.Details?.PartOfSpeech.ToString() ?? t.Text)));
    ///     </code>
    /// </example>
    public static WordSenseClassifier Create(WordSenseClassifierOptions? options = null)
    {
        return new WordSenseClassifier(WordSenseTable.LoadEmbedded(), null, null, options);
    }

    /// <inheritdoc />
    public IReadOnlyList<WordDetails> GetCandidates(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        if (!_table.TryGetLemma(lemma, out var entry) || (!entry.IsContextual && !_useLexicon))
        {
            return [];
        }

        return entry.Candidates;
    }

    /// <inheritdoc />
    public async Task<WordAnalysis[]> ClassifyAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        foreach (var word in words)
        {
            ArgumentNullException.ThrowIfNull(word);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Resolve every word first, so the encoder only runs when some word needs context
        var resolutions = new LemmaResolution?[words.Count];
        for (var i = 0; i < resolutions.Length; i++)
        {
            resolutions[i] = Resolve(words, i);
        }

        var scored = await ScoreAsync(words, resolutions, cancellationToken).ConfigureAwait(false);
        var result = new WordAnalysis[words.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = resolutions[i] is not { } resolution ? Unknown(words[i]) : Answer(words[i], resolution, scored[i]);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Words are the non-whitespace tiles; whitespace tiles never carry an answer
        var segments = TextSplitter.SplitSegments(text);
        var wordSegments = segments.Where(s => !s.IsSpace).ToList();
        var analyses = await ClassifyAsync(wordSegments.Select(s => text.Substring(s.Start, s.Length)).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var result = new List<WordSenseToken>(segments.Count);
        var next = 0;
        foreach (var segment in segments)
        {
            var segmentText = text.Substring(segment.Start, segment.Length);
            var analysis = segment.IsSpace ? Unknown(segmentText) : analyses[next++];
            result.Add(new WordSenseToken(segment.Start, segment.Length, segmentText, analysis));
        }

        return result;
    }

    /// <summary>
    ///     Releases the model when this classifier owns it; otherwise
    ///     it does nothing. Calling it more than once is harmless. Dispose only after every classification call has
    ///     finished; afterwards calls that need the model throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (_ownsModel)
        {
            _model!.Dispose();
        }
    }

    /// <summary>
    ///     The scores of one word that has a classifier.
    /// </summary>
    /// <param name="Model">The lemma entry, which needs context.</param>
    /// <param name="Weights">The classifier of the lemma.</param>
    /// <param name="Scores">The score of every sense.</param>
    /// <param name="Best">The index of the best sense.</param>
    /// <param name="Margin">The margin between the best and the second-best score.</param>
    private sealed record Scored(LemmaEntry Model, SenseWeights Weights, float[] Scores, int Best, float Margin);

    /// <summary>
    ///     Gets the margin below which a lemma gives no answer.
    /// </summary>
    /// <param name="weights">The classifier of the lemma.</param>
    /// <returns>The scaled abstain margin.</returns>
    private double Threshold(SenseWeights weights) => weights.AbstainMargin * _abstainScale;

    /// <summary>
    ///     Builds the analysis of a word that is not known.
    /// </summary>
    /// <param name="word">The word as given.</param>
    /// <returns>The analysis with no answer, no abstention and zero margins.</returns>
    private static WordAnalysis Unknown(string word) => new(word, null, 0.0, false, null, 0.0, AnswerBasis.None, []);

    /// <summary>
    ///     Builds the analysis of a resolved word from its dictionary entry or its context scores.
    /// </summary>
    /// <param name="word">The word as given.</param>
    /// <param name="resolution">The resolution of the word.</param>
    /// <param name="scored">The context scores, or null when the word was not scored.</param>
    /// <returns>The analysis.</returns>
    private WordAnalysis Answer(string word, LemmaResolution resolution, Scored? scored)
    {
        var entry = resolution.Entry;
        if (resolution.IsNarrowed)
        {
            // Morphology narrowed the entry to one sense, which is therefore the answer
            var sense = entry.Candidates[resolution.SenseIndex];
            return new WordAnalysis(word, sense, 0.0, false, null, 0.0, AnswerBasis.Dictionary, [sense]);
        }

        if (entry.IsContextual)
        {
            // Without a model nobody can score the senses, so the word abstains with its candidates
            if (_model is null)
            {
                return new WordAnalysis(word, null, 0.0, true, null, 0.0, AnswerBasis.Dictionary, entry.Candidates);
            }

            // A word the model could not score stays unknown
            return scored is null ? Unknown(word) : Analyze(word, scored);
        }

        return entry.Candidates.Count == 1
            ? new WordAnalysis(word, entry.Candidates[0], 0.0, false, null, 0.0, AnswerBasis.Dictionary, entry.Candidates)
            : new WordAnalysis(word, null, 0.0, true, null, 0.0, AnswerBasis.Dictionary, entry.Candidates);
    }

    /// <summary>
    ///     Builds the analysis of one scored word.
    /// </summary>
    /// <param name="word">The word as given.</param>
    /// <param name="scored">The scored word.</param>
    /// <returns>The analysis.</returns>
    private WordAnalysis Analyze(string word, Scored scored)
    {
        // Rank the senses after the best one: the runner-up and then the third-best
        var scores = scored.Scores;
        var runner = -1;
        for (var i = 0; i < scores.Length; i++)
        {
            if (i != scored.Best && (runner < 0 || scores[i] > scores[runner]))
            {
                runner = i;
            }
        }

        var third = -1;
        for (var i = 0; i < scores.Length; i++)
        {
            if (i != scored.Best && i != runner && (third < 0 || scores[i] > scores[third]))
            {
                third = i;
            }
        }

        var runnerMargin = third < 0 ? 0.0 : (double)scores[runner] - scores[third];
        var abstained = AbstainRule.Abstains(scored.Margin, Threshold(scored.Weights));
        var candidates = scored.Model.Candidates;
        return new WordAnalysis(
            word,
            abstained ? null : candidates[scored.Best],
            scored.Margin,
            abstained,
            candidates[runner],
            runnerMargin,
            AnswerBasis.Context,
            candidates);
    }

    /// <summary>
    ///     Resolves the word at an index to a lemma entry, applying the case policy to dictionary entries.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>The resolution, or null when the word is unknown.</returns>
    private LemmaResolution? Resolve(IReadOnlyList<string> words, int index)
    {
        var word = words[index];
        if (!_table.TryResolve(word, _useLexicon, out var resolution))
        {
            return null;
        }

        // Only dictionary entries are subject to the case policy; context lemmas match in any case
        if (!resolution.Entry.IsContextual && !CaseAllowed(words, index))
        {
            return null;
        }

        return resolution;
    }

    /// <summary>
    ///     Applies the case policy: a word that starts with an upper-case letter may be looked up in the dictionary only
    ///     at the start of the text, after a sentence end (<c>.</c>, <c>!</c>, <c>?</c>), after a colon, after an
    ///     opening quote or bracket (<c>"</c>, <c>'</c>, <c>(</c>, <c>[</c>, <c>{</c> and the typographic opening and
    ///     low quotes), or when it is the pronoun <c>I</c>.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>True when the word may be looked up.</returns>
    internal static bool CaseAllowed(IReadOnlyList<string> words, int index)
    {
        var word = words[index];
        if (word.Length == 0 || !char.IsUpper(word[0]) || index == 0 || word == "I")
        {
            return true;
        }

        return words[index - 1] is "." or "!" or "?" or ":" or "\"" or "'" or "(" or "[" or "{"
            or "\u2018" or "\u201C" or "\u201A" or "\u201E" or "\u00AB" or "\u2039";
    }

    /// <summary>
    ///     Scores every word that needs context.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="resolutions">The resolution of every word, null for an unknown word.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>An array parallel to the words; null where a word needs no context or has no tokens.</returns>
    private async Task<Scored?[]> ScoreAsync(
        IReadOnlyList<string> words,
        LemmaResolution?[] resolutions,
        CancellationToken cancellationToken)
    {
        var result = new Scored?[words.Count];
        if (_model is null || _weights is null)
        {
            return result;
        }

        var targets = new List<int>();
        for (var i = 0; i < words.Count; i++)
        {
            if (resolutions[i] is { IsNarrowed: false, Entry.IsContextual: true })
            {
                targets.Add(i);
            }
        }

        // Dictionary, unknown and candidates-only words never reach the model
        if (targets.Count == 0)
        {
            return result;
        }

        var pooled = await _model.EncodeAsync(words, targets, cancellationToken).ConfigureAwait(false);
        for (var t = 0; t < targets.Count; t++)
        {
            var entry = resolutions[targets[t]]!.Value.Entry;
            if (pooled[t] is not { } vector || !_weights.TryGet(entry.Lemma, out var weights))
            {
                continue;
            }

            var feature = FeatureExtractor.Center(vector, _weights.Mean);
            if (feature is null)
            {
                continue;
            }

            var (best, margin) = weights.Score(feature);
            var scores = new float[entry.Senses.Count];
            weights.Scores(feature, scores);
            result[targets[t]] = new Scored(entry, weights, scores, best, margin);
        }

        return result;
    }
}
