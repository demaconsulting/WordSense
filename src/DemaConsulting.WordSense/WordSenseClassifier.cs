using System.Globalization;

namespace DemaConsulting.WordSense;

/// <summary>
///     Classifies the sense of ambiguous words in free text.
/// </summary>
public interface IWordSenseClassifier : IDisposable
{
    /// <summary>
    ///     Gets the lemmas this classifier can disambiguate, in ordinal order.
    /// </summary>
    IReadOnlyList<string> KnownLemmas { get; }

    /// <summary>
    ///     Gets the sense labels of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma, for example <c>record</c>.</param>
    /// <returns>The sense labels, or an empty list when the lemma is unknown.</returns>
    IReadOnlyList<string> GetSenseLabels(string lemma);

    /// <summary>
    ///     Splits a text into tokens and attaches a sense to every known ambiguous word.
    /// </summary>
    /// <param name="text">The text to classify.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Tokens that tile the text exactly; see <see cref="WordSenseToken"/>.</returns>
    Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Classifies the single word at a given span of a text.
    /// </summary>
    /// <param name="text">The text that provides the context.</param>
    /// <param name="targetStart">Inclusive UTF-16 start of the word.</param>
    /// <param name="targetLength">UTF-16 length of the word.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The sense, or null when the word is unknown or the classifier abstained.</returns>
    Task<WordSenseResult?> ClassifyAsync(string text, int targetStart, int targetLength, CancellationToken cancellationToken = default);
}

/// <summary>
///     Classifies the sense of ambiguous words using a BERT-style encoder and an embedded linear table.
/// </summary>
/// <remarks>
///     The sentence is encoded once and every known word in it is classified from the same hidden
///     states. Texts longer than the maximum sequence length are classified from a window centered on
///     each target. The instance is thread-safe when its encoder is.
/// </remarks>
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
    /// <exception cref="WordSenseModelMismatchException">Thrown when the table was trained with a different model.</exception>
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
    /// <returns>A classifier that owns its encoder and must be disposed.</returns>
    /// <exception cref="WordSenseModelDownloadException">Thrown when the model could not be downloaded.</exception>
    /// <exception cref="WordSenseModelMismatchException">Thrown when the embedded table does not match the model.</exception>
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
    public IReadOnlyList<string> GetSenseLabels(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        return _table.TryGetLemma(lemma, out var model) ? model.Senses : [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WordSenseToken>> ClassifyAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Split into tiles and find the words that have a classifier
        var segments = SplitSegments(text);
        var targets = new List<(int Segment, LemmaModel Model)>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].IsWord && TryResolve(text.Substring(segments[i].Start, segments[i].Length), out var model))
            {
                targets.Add((i, model));
            }
        }

        var senses = new WordSenseResult?[segments.Count];
        if (targets.Count > 0)
        {
            var tokens = _tokenizer.Tokenize(text);
            var whole = tokens.Count + 2 <= _maxLength ? await EncodeWholeAsync(tokens, cancellationToken).ConfigureAwait(false) : null;
            foreach (var (segment, model) in targets)
            {
                var span = segments[segment];
                senses[segment] = await ClassifyTargetAsync(tokens, whole, model, span.Start, span.Start + span.Length, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var result = new List<WordSenseToken>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            result.Add(new WordSenseToken(segments[i].Start, segments[i].Length, text.Substring(segments[i].Start, segments[i].Length), senses[i]));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WordSenseResult?> ClassifyAsync(
        string text,
        int targetStart,
        int targetLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(targetStart);
        ArgumentOutOfRangeException.ThrowIfNegative(targetLength);
        if (targetStart > text.Length - targetLength)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLength), "The target span lies outside the text.");
        }

        if (targetLength == 0 || !TryResolve(text.Substring(targetStart, targetLength), out var model))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        var tokens = _tokenizer.Tokenize(text);
        var whole = tokens.Count + 2 <= _maxLength ? await EncodeWholeAsync(tokens, cancellationToken).ConfigureAwait(false) : null;
        return await ClassifyTargetAsync(tokens, whole, model, targetStart, targetStart + targetLength, cancellationToken)
            .ConfigureAwait(false);
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
    ///     Encodes the whole text in one window.
    /// </summary>
    /// <param name="tokens">The tokenized text.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The hidden states of the whole text, including the special tokens.</returns>
    private async Task<float[]> EncodeWholeAsync(TokenizedText tokens, CancellationToken cancellationToken)
    {
        var (ids, _, _) = tokens.BuildWindow(_tokenizer.ClsId, _tokenizer.SepId, _maxLength, 0, 0);
        return await _encoder.EncodeAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Classifies one target span.
    /// </summary>
    /// <param name="tokens">The tokenized text.</param>
    /// <param name="whole">Hidden states of the whole text when it fits one window, otherwise null.</param>
    /// <param name="model">The lemma model.</param>
    /// <param name="start">Inclusive start of the target.</param>
    /// <param name="end">Exclusive end of the target.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The sense, or null when the classifier abstains.</returns>
    private async Task<WordSenseResult?> ClassifyTargetAsync(
        TokenizedText tokens,
        float[]? whole,
        LemmaModel model,
        int start,
        int end,
        CancellationToken cancellationToken)
    {
        var (first, last) = tokens.FindTokens(start, end);
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
        if (margin < model.AbstainMargin * _abstainScale)
        {
            return null;
        }

        return new WordSenseResult(model.Lemma, model.Senses[best], margin);
    }

    /// <summary>
    ///     A tile of the text.
    /// </summary>
    /// <param name="Start">Inclusive start offset.</param>
    /// <param name="Length">Length in UTF-16 units.</param>
    /// <param name="IsWord">Whether the tile is a word.</param>
    private readonly record struct Segment(int Start, int Length, bool IsWord);

    /// <summary>
    ///     Splits a text into words, whitespace runs and single other characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Tiles covering the text exactly.</returns>
    private static List<Segment> SplitSegments(string text)
    {
        var segments = new List<Segment>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (IsWordChar(text, i))
            {
                i += CharLength(text, i);
                while (i < text.Length)
                {
                    if (IsWordChar(text, i))
                    {
                        i += CharLength(text, i);
                    }
                    else if ((text[i] is '\'' or '\u2019' or '-') && i + 1 < text.Length && IsWordChar(text, i + 1))
                    {
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                segments.Add(new Segment(start, i - start, true));
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                segments.Add(new Segment(start, i - start, false));
            }
            else
            {
                i += CharLength(text, i);
                segments.Add(new Segment(start, i - start, false));
            }
        }

        return segments;
    }

    /// <summary>
    ///     Determines whether the character at an index is a letter or digit.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <returns>True for letters, digits and combining marks.</returns>
    private static bool IsWordChar(string text, int index)
    {
        if (char.IsLetterOrDigit(text, index))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    /// <summary>
    ///     Gets the UTF-16 length of the character at an index, keeping surrogate pairs together.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <returns>1 or 2.</returns>
    private static int CharLength(string text, int index)
    {
        return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
    }
}
