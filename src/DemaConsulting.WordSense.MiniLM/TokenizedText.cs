namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The result of tokenizing a text: WordPiece ids together with the span of original text each
///     token covers.
/// </summary>
/// <remarks>
///     Offsets are UTF-16 code unit positions into the original (un-normalized) text so callers can
///     map a word in the caller's string to the tokens that represent it. The special tokens
///     <c>[CLS]</c> and <c>[SEP]</c> are not part of this structure; they are added when an encoder
///     input window is built with <see cref="BuildWindow"/>. Instances are immutable after construction
///     and safe for concurrent reads.
/// </remarks>
internal sealed class TokenizedText
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TokenizedText"/> class.
    /// </summary>
    /// <param name="ids">Vocabulary ids of every WordPiece token, in text order.</param>
    /// <param name="starts">Inclusive UTF-16 start offset of each token in the original text.</param>
    /// <param name="ends">Exclusive UTF-16 end offset of each token in the original text.</param>
    public TokenizedText(int[] ids, int[] starts, int[] ends)
        : this(ids, starts, ends, [], [])
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TokenizedText"/> class for a tokenized word list.
    /// </summary>
    /// <param name="ids">Vocabulary ids of every WordPiece token, in word order.</param>
    /// <param name="starts">Inclusive offset of each token in the words joined by single spaces.</param>
    /// <param name="ends">Exclusive offset of each token in the words joined by single spaces.</param>
    /// <param name="wordFirst">The first token index of each word.</param>
    /// <param name="wordEnd">The exclusive end token index of each word; equal to the first for a word without tokens.</param>
    public TokenizedText(int[] ids, int[] starts, int[] ends, int[] wordFirst, int[] wordEnd)
    {
        Ids = ids;
        Starts = starts;
        Ends = ends;
        WordFirst = wordFirst;
        WordEnd = wordEnd;
    }

    /// <summary>
    ///     Gets the first token index of each word; empty unless produced by <c>TokenizeWords</c>.
    /// </summary>
    public int[] WordFirst { get; }

    /// <summary>
    ///     Gets the exclusive end token index of each word; empty unless produced by <c>TokenizeWords</c>.
    /// </summary>
    public int[] WordEnd { get; }

    /// <summary>
    ///     Gets the vocabulary id of every token, excluding special tokens.
    /// </summary>
    public int[] Ids { get; }

    /// <summary>
    ///     Gets the inclusive UTF-16 start offset of each token in the original text.
    /// </summary>
    public int[] Starts { get; }

    /// <summary>
    ///     Gets the exclusive UTF-16 end offset of each token in the original text.
    /// </summary>
    public int[] Ends { get; }

    /// <summary>
    ///     Gets the number of tokens, excluding special tokens.
    /// </summary>
    public int Count => Ids.Length;

    /// <summary>
    ///     Builds the encoder input for a window of tokens that contains a target token range.
    /// </summary>
    /// <remarks>
    ///     When the whole text fits within <paramref name="maxLength"/> (including the two special
    ///     tokens) the window is the entire text. Otherwise the window is centered on the target so the
    ///     target always keeps as much surrounding context as possible, and is clamped to the text bounds.
    /// </remarks>
    /// <param name="clsId">Id of the <c>[CLS]</c> token.</param>
    /// <param name="sepId">Id of the <c>[SEP]</c> token.</param>
    /// <param name="maxLength">Maximum encoder sequence length including special tokens; at least 8.</param>
    /// <param name="targetFirst">First token index of the target.</param>
    /// <param name="targetEnd">Exclusive end token index of the target.</param>
    /// <returns>
    ///     The encoder input ids and the half-open range of the target within the encoder sequence
    ///     (that is, shifted by one for <c>[CLS]</c> and clipped to the window).
    /// </returns>
    public (int[] InputIds, int TargetFirst, int TargetEnd) BuildWindow(
        int clsId, int sepId, int maxLength, int targetFirst, int targetEnd)
    {
        // Choose the window of text tokens, leaving room for the two special tokens
        var capacity = maxLength - 2;
        var windowStart = 0;
        var windowLength = Count;
        if (Count > capacity)
        {
            // Center the target in the window, then clamp so the window stays inside the text
            var targetLength = Math.Min(targetEnd - targetFirst, capacity);
            windowStart = targetFirst - ((capacity - targetLength) / 2);
            windowStart = Math.Clamp(windowStart, 0, Count - capacity);
            windowLength = capacity;
        }

        // Assemble [CLS] window [SEP]
        var ids = new int[windowLength + 2];
        ids[0] = clsId;
        Array.Copy(Ids, windowStart, ids, 1, windowLength);
        ids[windowLength + 1] = sepId;

        // Translate the target range into encoder-sequence coordinates, clipped to the window
        var first = Math.Clamp(targetFirst - windowStart, 0, windowLength) + 1;
        var end = Math.Clamp(targetEnd - windowStart, 0, windowLength) + 1;
        return (ids, first, end);
    }
}
