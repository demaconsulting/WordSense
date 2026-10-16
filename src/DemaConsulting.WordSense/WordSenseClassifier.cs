namespace DemaConsulting.WordSense;

/// <summary>
///     Classifies the sense of ambiguous words in context.
/// </summary>
/// <remarks>
///     <para>
///     The primary input is a list of words that the caller has already tokenized; every result array
///     is parallel to that list. Results are null for words that have no classifier and for answers
///     withheld because the confidence is too low.
///     </para>
///     <para>
///     Terminology: a <em>lemma</em> is the base form of an ambiguous word (for example <c>record</c>); a
///     <em>sense</em> is one meaning of a lemma (<see cref="WordSense"/>); senses that share a meaning but differ
///     in part of speech share a <em>group</em>; a <em>tag</em> is a lower-case property of a sense such as
///     <c>noun</c> or <c>verb</c>. Each sense gets a score; the <em>margin</em> is the best score minus the
///     runner-up's score, and the classifier <em>abstains</em> (returns null) when the margin is below the
///     lemma's abstain threshold. Create an instance with <see cref="WordSenseClassifier.CreateAsync"/>.
///     </para>
///     <para>
///     A sense has one name: <see cref="WordSense.Sense"/> on a result is the same string as
///     <see cref="SenseDefinition.Name"/> on the definition it was built from.
///     </para>
///     <para>
///     Each word is matched against the table's known surface forms after lower-casing it (invariant
///     culture) and replacing the typographic apostrophe (U+2019) with <c>'</c>, so case does not matter and
///     inflected forms listed in the table (for example a plural or past tense) resolve to their lemma. A
///     possessive <c>'s</c> is also accepted: when the word is not itself a known form, <c>'s</c> is removed
///     and the rest is looked up (so <c>record's</c> resolves like <c>record</c>).
///     </para>
/// </remarks>
/// <example>
///     Pick the method that matches the question being asked: <c>ClassifyAsync</c> for the sense,
///     <c>ClassifyTagAsync</c> or <c>IsTagAsync</c> for a part-of-speech style answer, and
///     <c>ClassifyDetailAsync</c> when the confidence matters.
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///
///     string[] words = ["They", "will", "record", "the", "record"];
///     WordSense?[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         // null: not an ambiguous word, or the classifier abstained
///         Console.WriteLine($"{words[i]}: {senses[i]?.Sense ?? "-"}");
///     }
///     </code>
/// </example>
public interface IWordSenseClassifier : IDisposable
{
    /// <summary>
    ///     Gets the lemmas this classifier can disambiguate, in ordinal order.
    /// </summary>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     bool canClassify = classifier.KnownLemmas.Contains("record");
    ///     </code>
    /// </example>
    IReadOnlyList<string> KnownLemmas { get; }

    /// <summary>
    ///     Gets the senses of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma, for example <c>record</c>.</param>
    /// <returns>The shared sense records, or an empty list when the lemma is unknown.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     foreach (var sense in classifier.GetSenses("record"))
    ///     {
    ///         Console.WriteLine($"{sense.Sense} group={sense.Group} tags={string.Join(",", sense.Tags.Order())}");
    ///     }
    ///     </code>
    /// </example>
    IReadOnlyList<WordSense> GetSenses(string lemma);

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">
    ///     The words in order, including punctuation tokens. They are read as one running text in which
    ///     the words are separated by single spaces.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     An array parallel to <paramref name="words"/>: the sense of each word, or null when the word is
    ///     not a known ambiguous word, the word yields no tokens or features (for example an empty or
    ///     whitespace-only string), or the classifier abstained.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     string[] words = ["The", "record", "was", "broken"];
    ///     WordSense?[] senses = await classifier.ClassifyAsync(words);
    ///     if (senses[1] is { } sense)
    ///     {
    ///         Console.WriteLine($"{sense.Lemma}: {sense.Sense} tags={string.Join(",", sense.Tags.Order())}");
    ///     }
    ///     </code>
    /// </example>
    Task<WordSense?[]> ClassifyAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Splits a raw text into tokens and attaches a sense to every known ambiguous word.
    /// </summary>
    /// <param name="text">The text to classify.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Tokens that tile the text exactly; see <see cref="WordSenseToken"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     var tokens = await classifier.ClassifyAsync("They will record the record.");
    ///     foreach (var token in tokens.Where(t => t.Sense is not null))
    ///     {
    ///         Console.WriteLine($"{token.Start}: {token.Text} is {token.Sense}");
    ///     }
    ///     </code>
    /// </example>
    Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies every word and reports the confidence figures behind each decision.
    /// </summary>
    /// <param name="words">The words in order.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     An array parallel to <paramref name="words"/>: the detail of each word that has a classifier
    ///     (also when it abstained, see <see cref="WordSenseDetail.Abstained"/>), or null for other words,
    ///     including a known word that yields no tokens or features.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     string[] words = ["They", "will", "record", "the", "record"];
    ///     WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(words);
    ///     foreach (var detail in details.OfType&lt;WordSenseDetail&gt;())
    ///     {
    ///         Console.WriteLine($"{detail.Sense} margin={detail.Margin:F2} abstained={detail.Abstained}");
    ///     }
    ///     </code>
    /// </example>
    Task<WordSenseDetail?[]> ClassifyDetailAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies every word at the level of tags: which of the given tags describes the word.
    /// </summary>
    /// <remarks>
    ///     The score of a tag is the best score among the senses that carry it. The tag with the highest
    ///     score wins. The result is null when the lemma has no sense carrying any queried tag, when the
    ///     best sense carries none of them, or when the winner's margin over the runner-up tag (or over
    ///     the senses carrying none of the tags) is below the lemma's abstain threshold.
    /// </remarks>
    /// <param name="words">The words in order.</param>
    /// <param name="tags">The tags to choose between; at least one, none null or empty.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An array parallel to <paramref name="words"/>: the winning tag, or null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument or element is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tags"/> is empty or contains an empty tag.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     string[] words = ["They", "will", "record", "the", "record"];
    ///     string?[] partOfSpeech = await classifier.ClassifyTagAsync(words, ["noun", "verb"]);
    ///     // Each entry is "noun", "verb", or null (unknown word or the classifier abstained)
    ///     </code>
    /// </example>
    Task<string?[]> ClassifyTagAsync(
        IReadOnlyList<string> words,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies every word as having or not having a tag.
    /// </summary>
    /// <param name="words">The words in order.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     An array parallel to <paramref name="words"/>: true when the word has the tag, false when it
    ///     does not, null when the lemma has no sense with the tag, the word is unknown, or the
    ///     classifier abstained.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument or element is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tag"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     using var classifier = await WordSenseClassifier.CreateAsync();
    ///     string[] words = ["They", "will", "record", "the", "record"];
    ///     bool?[] isVerb = await classifier.IsTagAsync(words, "verb");
    ///     // true for a verb use, false for another use, null when unknown or abstained
    ///     </code>
    /// </example>
    Task<bool?[]> IsTagAsync(IReadOnlyList<string> words, string tag, CancellationToken cancellationToken = default);
}

/// <summary>
///     Classifies the sense of ambiguous words using a BERT-style encoder and an embedded linear table.
/// </summary>
/// <remarks>
///     The words are encoded once and every known word in them is classified from the same hidden
///     states. Inputs longer than the maximum sequence length are classified from a window centered on
///     each target. The instance is thread-safe when its encoder is. Instances created by
///     <see cref="CreateAsync"/> own their ONNX session, so dispose them (for example with <c>using</c>);
///     create one classifier and share it rather than creating one per request. The classification
///     methods, and how words are matched to lemmas, are described on <see cref="IWordSenseClassifier"/>.
/// </remarks>
/// <example>
///     Create the classifier once, then classify as many texts as needed. With no options the model is
///     downloaded on first use, verified, and cached in the per-user store.
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     var tokens = await classifier.ClassifyAsync("Please record the record.");
///     Console.WriteLine(string.Join(" ", tokens.Select(t => t.Sense?.ToString() ?? t.Text)));
///     </code>
/// </example>
public sealed class WordSenseClassifier : IWordSenseClassifier
{
    private readonly IWordSenseEncoder _encoder;
    private readonly WordSenseTable _table;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly double _abstainScale;
    private readonly bool _ownsEncoder;
    private readonly string[] _lemmas;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseClassifier"/> class from its parts.
    /// </summary>
    /// <param name="encoder">The encoder; it is not disposed by this classifier.</param>
    /// <param name="table">The classifier table.</param>
    /// <param name="vocabularyPath">The path of the vocabulary file that matches the encoder.</param>
    /// <param name="options">Classifier options, or null for defaults.</param>
    /// <remarks>
    ///     The vocabulary file is read first, so a problem with it is reported before the other arguments
    ///     are checked.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="encoder"/>, <paramref name="table"/> or <paramref name="vocabularyPath"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="WordSenseClassifierOptions.MaxSequenceLength"/> is outside 8 to 512, or
    ///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/> is negative or NaN.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="vocabularyPath"/> is empty or the vocabulary lacks a required special token.
    /// </exception>
    /// <exception cref="FileNotFoundException">Thrown when the vocabulary file does not exist.</exception>
    /// <exception cref="IOException">Thrown when the vocabulary file cannot be read.</exception>
    /// <exception cref="WordSenseModelMismatchException">
    ///     Thrown when the table was trained with a different model (SHA-256 or dimension) than the encoder.
    /// </exception>
    public WordSenseClassifier(
        IWordSenseEncoder encoder,
        WordSenseTable table,
        string vocabularyPath,
        WordSenseClassifierOptions? options = null)
        : this(encoder, table, WordPieceTokenizer.FromFile(vocabularyPath), options, false)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseClassifier"/> class.
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="table">The classifier table.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="options">Classifier options, or null for defaults.</param>
    /// <param name="ownsEncoder">Whether disposing this classifier disposes the encoder.</param>
    internal WordSenseClassifier(
        IWordSenseEncoder encoder,
        WordSenseTable table,
        WordPieceTokenizer tokenizer,
        WordSenseClassifierOptions? options,
        bool ownsEncoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(tokenizer);
        options ??= new WordSenseClassifierOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxSequenceLength, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxSequenceLength, 512);
        ArgumentOutOfRangeException.ThrowIfNegative(options.AbstainMarginScale);

        // A table is only meaningful for the exact model it was trained with
        if (!string.Equals(table.ModelSha256, encoder.ModelSha256, StringComparison.OrdinalIgnoreCase) ||
            table.Dimension != encoder.HiddenSize)
        {
            throw new WordSenseModelMismatchException(
                "The classifier table was trained with a different encoder model than the one supplied.");
        }

        _encoder = encoder;
        _table = table;
        _tokenizer = tokenizer;
        _maxLength = options.MaxSequenceLength;
        _abstainScale = options.AbstainMarginScale;
        _ownsEncoder = ownsEncoder;
        _lemmas = table.Lemmas.Select(l => l.Lemma).OrderBy(l => l, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> KnownLemmas => _lemmas;

    /// <summary>
    ///     Creates a classifier from the embedded table, obtaining and verifying the model first.
    /// </summary>
    /// <param name="options">The options, or null for defaults.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <remarks>
    ///     When <see cref="WordSenseClassifierOptions.ModelDirectory"/> is set the files in it are verified
    ///     and no network access happens; otherwise <see cref="WordSenseModelDownloader.EnsureModelAsync"/> obtains
    ///     them (see that method for its additional exceptions, such as <see cref="ArgumentException"/> for an
    ///     invalid <c>WORDSENSE_MIRROR_*</c> environment configuration; <see cref="WordSenseClassifierOptions.Downloader"/>
    ///     describes the download settings). The embedded table is loaded with <see cref="WordSenseTable.LoadEmbedded"/>.
    ///     The model is obtained before <see cref="WordSenseClassifierOptions.MaxSequenceLength"/> and
    ///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/> are validated, so an invalid value of either
    ///     is reported only after the model has been downloaded or verified. <c>AbstainMarginScale</c> is
    ///     rejected when negative or NaN; positive infinity is accepted.
    /// </remarks>
    /// <returns>A classifier that owns its encoder and must be disposed.</returns>
    /// <exception cref="WordSenseModelDownloadException">Thrown when the model could not be downloaded.</exception>
    /// <exception cref="WordSenseModelMismatchException">Thrown when the embedded table does not match the model.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a file of the model directory is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when a model file's SHA-256 does not match the pinned value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an option value is out of range (checked after the model is obtained).</exception>
    /// <exception cref="ArgumentException">Thrown when the <c>WORDSENSE_MIRROR_*</c> environment configuration is invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <example>
    ///     <code>
    ///     // Default: download on first use, verify, and cache
    ///     using var online = await WordSenseClassifier.CreateAsync();
    ///
    ///     // Offline: use a directory that already holds model_int8.onnx and vocab.txt
    ///     var options = new WordSenseClassifierOptions { ModelDirectory = "/models/all-MiniLM-L6-v2" };
    ///     using var offline = await WordSenseClassifier.CreateAsync(options);
    ///     </code>
    /// </example>
    public static async Task<WordSenseClassifier> CreateAsync(
        WordSenseClassifierOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WordSenseClassifierOptions();

        var location = options.ModelDirectory is not null
            ? WordSenseModelLocation.FromDirectory(options.ModelDirectory)
            : await WordSenseModelDownloader.EnsureModelAsync(options.Downloader, cancellationToken).ConfigureAwait(false);

        var table = WordSenseTable.LoadEmbedded();
        var tokenizer = WordPieceTokenizer.FromFile(location.VocabularyPath);
        var encoder = new OnnxWordSenseEncoder(location.ModelPath, options.IntraOpThreads);
        try
        {
            return new WordSenseClassifier(encoder, table, tokenizer, options, true);
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WordSense> GetSenses(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        return _table.TryGetLemma(lemma, out var model) ? model.Senses : [];
    }

    /// <inheritdoc />
    public async Task<WordSense?[]> ClassifyAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default)
    {
        var scored = await ScoreAsync(words, cancellationToken).ConfigureAwait(false);
        var result = new WordSense?[words.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (scored[i] is { } s && s.Margin >= Threshold(s.Model))
            {
                result[i] = s.Model.Senses[s.Best];
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Words are the non-whitespace tiles; whitespace tiles never carry a sense
        var segments = TextSplitter.SplitSegments(text);
        var wordSegments = segments.Where(s => !s.IsSpace).ToList();
        var senses = await ClassifyAsync(wordSegments.Select(s => text.Substring(s.Start, s.Length)).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var result = new List<WordSenseToken>(segments.Count);
        var next = 0;
        foreach (var segment in segments)
        {
            var sense = segment.IsSpace ? null : senses[next++];
            result.Add(new WordSenseToken(segment.Start, segment.Length, text.Substring(segment.Start, segment.Length), sense));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WordSenseDetail?[]> ClassifyDetailAsync(IReadOnlyList<string> words, CancellationToken cancellationToken = default)
    {
        var scored = await ScoreAsync(words, cancellationToken).ConfigureAwait(false);
        var result = new WordSenseDetail?[words.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (scored[i] is { } s)
            {
                result[i] = BuildDetail(s);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<string?[]> ClassifyTagAsync(
        IReadOnlyList<string> words,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(tags);
        foreach (var item in tags)
        {
            ArgumentNullException.ThrowIfNull(item);
        }

        var query = tags.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (query.Length == 0 || query.Any(t => t.Length == 0))
        {
            throw new ArgumentException("At least one tag is required and no tag may be empty.", nameof(tags));
        }

        var scored = await ScoreAsync(words, cancellationToken).ConfigureAwait(false);
        var result = new string?[words.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (scored[i] is { } s)
            {
                result[i] = TagScorer.Classify(s.Model.Senses, s.Scores, query, Threshold(s.Model));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool?[]> IsTagAsync(IReadOnlyList<string> words, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length == 0)
        {
            throw new ArgumentException("The tag must not be empty.", nameof(tag));
        }

        var scored = await ScoreAsync(words, cancellationToken).ConfigureAwait(false);
        var result = new bool?[words.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (scored[i] is { } s)
            {
                result[i] = TagScorer.IsTag(s.Model.Senses, s.Scores, tag, Threshold(s.Model));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsEncoder)
        {
            _encoder.Dispose();
        }
    }

    /// <summary>
    ///     The scores of one word that has a classifier.
    /// </summary>
    /// <param name="Model">The lemma model.</param>
    /// <param name="Scores">The score of every sense.</param>
    /// <param name="Best">The index of the best sense.</param>
    /// <param name="Margin">The margin between the best and the second-best score.</param>
    private sealed record Scored(LemmaModel Model, float[] Scores, int Best, float Margin);

    /// <summary>
    ///     Gets the margin below which a lemma gives no answer.
    /// </summary>
    /// <param name="model">The lemma model.</param>
    /// <returns>The scaled abstain margin.</returns>
    private double Threshold(LemmaModel model) => model.AbstainMargin * _abstainScale;

    /// <summary>
    ///     Builds the detail of one scored word.
    /// </summary>
    /// <param name="scored">The scored word.</param>
    /// <returns>The detail.</returns>
    private WordSenseDetail BuildDetail(Scored scored)
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
        return new WordSenseDetail(
            scored.Model.Senses[scored.Best],
            scored.Margin,
            scored.Model.Senses[runner],
            runnerMargin,
            scored.Margin < Threshold(scored.Model));
    }

    /// <summary>
    ///     Resolves a surface word to the model of its lemma.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="model">The lemma model when found.</param>
    /// <returns>True when the word belongs to a known lemma.</returns>
    private bool TryResolve(string word, out LemmaModel model)
    {
        var lower = word.ToLowerInvariant().Replace('\u2019', '\'');
        if (_table.Forms.TryGetValue(lower, out var lemma) && _table.TryGetLemma(lemma, out model))
        {
            return true;
        }

        // Possessives share the lemma of the bare word
        if (lower.EndsWith("'s", StringComparison.Ordinal) &&
            _table.Forms.TryGetValue(lower[..^2], out lemma) &&
            _table.TryGetLemma(lemma, out model))
        {
            return true;
        }

        model = null!;
        return false;
    }

    /// <summary>
    ///     Scores every word that has a classifier.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>An array parallel to the words; null where a word has no classifier or no tokens.</returns>
    private async Task<Scored?[]> ScoreAsync(IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        foreach (var word in words)
        {
            ArgumentNullException.ThrowIfNull(word);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new Scored?[words.Count];
        var models = new LemmaModel?[words.Count];
        var any = false;
        for (var i = 0; i < words.Count; i++)
        {
            if (TryResolve(words[i], out var model))
            {
                models[i] = model;
                any = true;
            }
        }

        if (!any)
        {
            return result;
        }

        var tokens = _tokenizer.TokenizeWords(words);
        var whole = tokens.Count + 2 <= _maxLength ? await EncodeWholeAsync(tokens, cancellationToken).ConfigureAwait(false) : null;
        for (var i = 0; i < words.Count; i++)
        {
            if (models[i] is { } model)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result[i] = await ScoreTargetAsync(tokens, whole, model, tokens.WordFirst[i], tokens.WordEnd[i], cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    ///     Encodes the whole input in one window.
    /// </summary>
    /// <param name="tokens">The tokenized words.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The hidden states of the whole input, including the special tokens.</returns>
    private async Task<float[]> EncodeWholeAsync(TokenizedText tokens, CancellationToken cancellationToken)
    {
        var (ids, _, _) = tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, 0, 0);
        return await _encoder.EncodeAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Scores one target word.
    /// </summary>
    /// <param name="tokens">The tokenized words.</param>
    /// <param name="whole">Hidden states of the whole input when it fits one window, otherwise null.</param>
    /// <param name="model">The lemma model.</param>
    /// <param name="first">First token of the target.</param>
    /// <param name="last">Exclusive end token of the target.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The scores, or null when the word has no tokens or no feature.</returns>
    private async Task<Scored?> ScoreTargetAsync(
        TokenizedText tokens,
        float[]? whole,
        LemmaModel model,
        int first,
        int last,
        CancellationToken cancellationToken)
    {
        if (last <= first)
        {
            return null;
        }

        float[] hidden;
        int rowFirst;
        int rowEnd;
        if (whole is not null)
        {
            hidden = whole;
            rowFirst = first + 1;
            rowEnd = last + 1;
        }
        else
        {
            var window = tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, first, last);
            hidden = await _encoder.EncodeAsync(window.InputIds, cancellationToken).ConfigureAwait(false);
            rowFirst = window.TargetFirst;
            rowEnd = window.TargetEnd;
        }

        var feature = FeatureExtractor.Extract(hidden, _table.Dimension, rowFirst, rowEnd, _table.Mean);
        if (feature is null)
        {
            return null;
        }

        var (best, margin) = model.Score(feature);
        var scores = new float[model.Senses.Count];
        model.Scores(feature, scores);
        return new Scored(model, scores, best, margin);
    }
}
