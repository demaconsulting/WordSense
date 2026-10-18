using System.Globalization;

namespace WordSense.Builder;

/// <summary>
///     Splits a raw text into tiles: words, whitespace runs and single other characters.
/// </summary>
/// <remarks>
///     Shared by the builder and the tests so that every
///     component agrees on what a word is.
/// </remarks>
internal static class TextSplitter
{
    /// <summary>
    ///     A tile of the text.
    /// </summary>
    /// <param name="Start">Inclusive start offset.</param>
    /// <param name="Length">Length in UTF-16 units.</param>
    /// <param name="IsWord">Whether the tile is a word.</param>
    /// <param name="IsSpace">Whether the tile is a whitespace run.</param>
    public readonly record struct Segment(int Start, int Length, bool IsWord, bool IsSpace);

    /// <summary>
    ///     Splits a text into words, whitespace runs and single other characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Tiles covering the text exactly.</returns>
    public static List<Segment> SplitSegments(string text)
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

                segments.Add(new Segment(start, i - start, true, false));
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                segments.Add(new Segment(start, i - start, false, true));
            }
            else
            {
                i += CharLength(text, i);
                segments.Add(new Segment(start, i - start, false, false));
            }
        }

        return segments;
    }

    /// <summary>
    ///     Splits a text into its words: every non-whitespace tile (words and single punctuation characters).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The words with their offsets in the text.</returns>
    public static List<(int Start, int Length, string Text)> SplitWords(string text)
    {
        return SplitSegments(text).Where(s => !s.IsSpace).Select(s => (s.Start, s.Length, text.Substring(s.Start, s.Length))).ToList();
    }

    /// <summary>
    ///     Determines whether the character at an index is a letter, a digit or a combining mark.
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
