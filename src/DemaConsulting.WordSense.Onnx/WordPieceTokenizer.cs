using System.Globalization;
using System.Text;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     A managed implementation of the BERT uncased WordPiece tokenizer used by the MiniLM encoder.
/// </summary>
/// <remarks>
///     The tokenizer reproduces the Hugging Face <c>tokenizers</c> BERT pipeline (clean text, space
///     out CJK ideographs, strip accents, lower-case, split on whitespace and punctuation, then greedy
///     longest-match WordPiece) so that no native tokenizer dependency is needed. Every token also
///     carries its span in the original text, expressed in UTF-16 code units, so callers can locate a
///     word's tokens. Parity with the reference implementation is proven by a committed fixture of
///     sentences and expected ids. Instances are immutable after construction and thread-safe.
/// </remarks>
internal sealed class WordPieceTokenizer
{
    /// <summary>
    ///     The longest word, in characters, that is split into pieces; longer words become <c>[UNK]</c>.
    /// </summary>
    private const int MaxCharsPerWord = 100;

    /// <summary>
    ///     The prefix that marks a WordPiece continuation piece in the vocabulary.
    /// </summary>
    private const string ContinuationPrefix = "##";

    /// <summary>
    ///     Maps each vocabulary entry to its id.
    /// </summary>
    private readonly Dictionary<string, int> _vocabulary;

    /// <summary>
    ///     Whether text is lower-cased and stripped of accents (an uncased model) or kept as written (a cased model).
    /// </summary>
    private readonly bool _lowerCase;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordPieceTokenizer"/> class.
    /// </summary>
    /// <param name="vocabulary">The vocabulary entries; the id of an entry is its zero-based position.</param>
    /// <param name="lowerCase">True for an uncased model: lower-case and strip accents; false for a cased model.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="vocabulary"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the vocabulary lacks <c>[UNK]</c>, <c>[CLS]</c> or <c>[SEP]</c>.
    /// </exception>
    public WordPieceTokenizer(IEnumerable<string> vocabulary, bool lowerCase = true)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        _lowerCase = lowerCase;

        // Index the vocabulary; the first occurrence of an entry wins like the reference implementation
        _vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        var id = 0;
        foreach (var entry in vocabulary)
        {
            _vocabulary.TryAdd(entry, id);
            id++;
        }

        // The three special tokens are mandatory for building encoder input
        UnknownId = Require("[UNK]");
        ClsId = Require("[CLS]");
        SepId = Require("[SEP]");
    }

    /// <summary>
    ///     Gets the id of the <c>[UNK]</c> token.
    /// </summary>
    public int UnknownId { get; }

    /// <summary>
    ///     Gets the id of the <c>[CLS]</c> token.
    /// </summary>
    public int ClsId { get; }

    /// <summary>
    ///     Gets the id of the <c>[SEP]</c> token.
    /// </summary>
    public int SepId { get; }

    /// <summary>
    ///     Creates a tokenizer from a <c>vocab.txt</c> file with one entry per line.
    /// </summary>
    /// <param name="path">Path of the vocabulary file.</param>
    /// <param name="lowerCase">True for an uncased model; false for a cased model.</param>
    /// <returns>A new tokenizer.</returns>
    /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
    public static WordPieceTokenizer FromFile(string path, bool lowerCase = true)
    {
        // File.ReadLines strips both CRLF and LF so a checkout with either line ending behaves identically
        return new WordPieceTokenizer(File.ReadLines(path, Encoding.UTF8), lowerCase);
    }

    /// <summary>
    ///     Tokenizes text into WordPiece ids with offsets into the original text.
    /// </summary>
    /// <param name="text">The text to tokenize; may be empty.</param>
    /// <returns>The tokens of the text, excluding special tokens.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    public TokenizedText Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ids = new List<int>();
        var starts = new List<int>();
        var ends = new List<int>();

        // Normalize into code points that remember their original span, then split into words
        var elements = Normalize(text);
        var word = new List<Element>();
        foreach (var element in elements)
        {
            if (Rune.IsWhiteSpace(element.Rune))
            {
                FlushWord(word, ids, starts, ends);
            }
            else if (IsPunctuation(element.Rune))
            {
                // Every punctuation character is a word of its own
                FlushWord(word, ids, starts, ends);
                word.Add(element);
                FlushWord(word, ids, starts, ends);
            }
            else
            {
                word.Add(element);
            }
        }

        FlushWord(word, ids, starts, ends);
        return new TokenizedText(ids.ToArray(), starts.ToArray(), ends.ToArray());
    }

    /// <summary>
    ///     Tokenizes a list of words, remembering which tokens belong to each word.
    /// </summary>
    /// <remarks>
    ///     The result equals tokenizing the words joined by single spaces: BERT pre-tokenization splits on
    ///     whitespace, so each word is tokenized independently. A word that yields no tokens (for example
    ///     control characters only) gets an empty token range.
    /// </remarks>
    /// <param name="words">The words; none may be null.</param>
    /// <returns>The tokens with a token range per word.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or a word is null.</exception>
    public TokenizedText TokenizeWords(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var ids = new List<int>();
        var starts = new List<int>();
        var ends = new List<int>();
        var wordFirst = new int[words.Count];
        var wordEnd = new int[words.Count];
        var offset = 0;
        for (var w = 0; w < words.Count; w++)
        {
            ArgumentNullException.ThrowIfNull(words[w]);
            var tokens = Tokenize(words[w]);
            wordFirst[w] = ids.Count;
            ids.AddRange(tokens.Ids);
            starts.AddRange(tokens.Starts.Select(s => s + offset));
            ends.AddRange(tokens.Ends.Select(e => e + offset));
            wordEnd[w] = ids.Count;
            offset += words[w].Length + 1;
        }

        return new TokenizedText(ids.ToArray(), starts.ToArray(), ends.ToArray(), wordFirst, wordEnd);
    }

    /// <summary>
    ///     Looks up a required special token.
    /// </summary>
    /// <param name="token">The token text.</param>
    /// <returns>The token id.</returns>
    /// <exception cref="ArgumentException">Thrown when the token is not in the vocabulary.</exception>
    private int Require(string token)
    {
        if (!_vocabulary.TryGetValue(token, out var id))
        {
            throw new ArgumentException($"The vocabulary does not contain the required token {token}.");
        }

        return id;
    }

    /// <summary>
    ///     Applies BERT text cleaning and normalization while tracking original spans.
    /// </summary>
    /// <remarks>
    ///     Each original code point yields zero or more normalized code points that all carry the span of
    ///     that original code point. This is what lets token offsets point at the caller's original text.
    /// </remarks>
    /// <param name="text">The original text.</param>
    /// <returns>The normalized code points with their original spans.</returns>
    private List<Element> Normalize(string text)
    {
        var result = new List<Element>(text.Length + 8);
        var index = 0;
        while (index < text.Length)
        {
            // Decode one code point; a lone surrogate is an invalid scalar and is dropped as noise
            if (!Rune.TryGetRuneAt(text, index, out var rune))
            {
                index++;
                continue;
            }

            var start = index;
            index += rune.Utf16SequenceLength;
            var end = index;

            if (rune.Value == 0 || rune.Value == 0xFFFD || IsControl(rune))
            {
                // Control characters carry no content
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                result.Add(new Element(new Rune(' '), start, end));
            }
            else if (IsCjk(rune.Value))
            {
                // Ideographs are surrounded by spaces so each becomes its own word
                result.Add(new Element(new Rune(' '), start, end));
                result.Add(new Element(_lowerCase ? Rune.ToLowerInvariant(rune) : rune, start, end));
                result.Add(new Element(new Rune(' '), start, end));
            }
            else
            {
                AddStripped(result, rune, start, end);
            }
        }

        return result;
    }

    /// <summary>
    ///     Decomposes a code point, drops combining marks, and lower-cases what remains.
    /// </summary>
    /// <param name="result">The list receiving the normalized code points.</param>
    /// <param name="rune">The original code point.</param>
    /// <param name="start">The original start offset.</param>
    /// <param name="end">The original end offset.</param>
    private void AddStripped(List<Element> result, Rune rune, int start, int end)
    {
        // A cased model keeps the text as written, accents included
        if (!_lowerCase)
        {
            result.Add(new Element(rune, start, end));
            return;
        }

        // Fast path: ASCII has no combining marks and lower-casing is trivial
        if (rune.IsAscii)
        {
            result.Add(new Element(Rune.ToLowerInvariant(rune), start, end));
            return;
        }

        foreach (var part in rune.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            // Mn is the "nonspacing mark" category that accent stripping removes
            if (Rune.GetUnicodeCategory(part) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Add(new Element(Rune.ToLowerInvariant(part), start, end));
        }
    }

    /// <summary>
    ///     Converts one pre-tokenized word into WordPiece tokens and clears the word buffer.
    /// </summary>
    /// <param name="word">The buffered word; cleared by this call.</param>
    /// <param name="ids">The id list receiving tokens.</param>
    /// <param name="starts">The start offset list receiving tokens.</param>
    /// <param name="ends">The end offset list receiving tokens.</param>
    private void FlushWord(List<Element> word, List<int> ids, List<int> starts, List<int> ends)
    {
        if (word.Count == 0)
        {
            return;
        }

        var wordStart = word[0].Start;
        var wordEnd = word[^1].End;
        var pieces = word.Count > MaxCharsPerWord ? null : Split(word);
        if (pieces is null)
        {
            // Overlong or unmatched words become a single unknown token spanning the whole word
            ids.Add(UnknownId);
            starts.Add(wordStart);
            ends.Add(wordEnd);
        }
        else
        {
            foreach (var (id, first, last) in pieces)
            {
                ids.Add(id);
                starts.Add(word[first].Start);
                ends.Add(word[last].End);
            }
        }

        word.Clear();
    }

    /// <summary>
    ///     Splits a word into the longest-match WordPiece pieces.
    /// </summary>
    /// <param name="word">The word as normalized code points.</param>
    /// <returns>The pieces (id, first element, last element), or null when some part cannot be matched.</returns>
    private List<(int Id, int First, int Last)>? Split(List<Element> word)
    {
        var pieces = new List<(int, int, int)>();
        var start = 0;
        var builder = new StringBuilder();
        while (start < word.Count)
        {
            // Greedy longest match: shrink the candidate from the right until it is in the vocabulary
            var end = word.Count;
            var found = -1;
            while (start < end)
            {
                builder.Clear();
                if (start > 0)
                {
                    builder.Append(ContinuationPrefix);
                }

                for (var i = start; i < end; i++)
                {
                    builder.Append(word[i].Rune.ToString());
                }

                if (_vocabulary.TryGetValue(builder.ToString(), out found))
                {
                    break;
                }

                found = -1;
                end--;
            }

            if (found < 0)
            {
                return null;
            }

            pieces.Add((found, start, end - 1));
            start = end;
        }

        return pieces;
    }

    /// <summary>
    ///     Determines whether a code point is removed by BERT text cleaning.
    /// </summary>
    /// <param name="rune">The code point.</param>
    /// <returns>True for control, format, unassigned, private-use and surrogate code points other than tab, line feed and carriage return.</returns>
    private static bool IsControl(Rune rune)
    {
        if (rune.Value is '\t' or '\n' or '\r')
        {
            return false;
        }

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate;
    }

    /// <summary>
    ///     Determines whether a code point is split off as its own word by the BERT pre-tokenizer.
    /// </summary>
    /// <param name="rune">The code point.</param>
    /// <returns>True for ASCII punctuation and symbols and any Unicode punctuation category.</returns>
    private static bool IsPunctuation(Rune rune)
    {
        var value = rune.Value;
        if ((value >= 33 && value <= 47) || (value >= 58 && value <= 64) || (value >= 91 && value <= 96) ||
            (value >= 123 && value <= 126))
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
    }

    /// <summary>
    ///     Determines whether a code point lies in a CJK ideograph block.
    /// </summary>
    /// <param name="value">The code point value.</param>
    /// <returns>True for the CJK Unified Ideograph and compatibility ranges the BERT tokenizer spaces out.</returns>
    private static bool IsCjk(int value)
    {
        return (value >= 0x4E00 && value <= 0x9FFF) || (value >= 0x3400 && value <= 0x4DBF) ||
               (value >= 0x20000 && value <= 0x2A6DF) || (value >= 0x2A700 && value <= 0x2B73F) ||
               (value >= 0x2B740 && value <= 0x2B81F) || (value >= 0x2B820 && value <= 0x2CEAF) ||
               (value >= 0xF900 && value <= 0xFAFF) || (value >= 0x2F800 && value <= 0x2FA1F);
    }

    /// <summary>
    ///     A normalized code point together with the original text span it came from.
    /// </summary>
    /// <param name="Rune">The normalized code point.</param>
    /// <param name="Start">Inclusive UTF-16 start offset in the original text.</param>
    /// <param name="End">Exclusive UTF-16 end offset in the original text.</param>
    private readonly record struct Element(Rune Rune, int Start, int End);
}
