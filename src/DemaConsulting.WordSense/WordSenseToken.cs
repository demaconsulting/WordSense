namespace DemaConsulting.WordSense;

/// <summary>
///     A segment of a raw text, with the sense of the word when one was determined.
/// </summary>
/// <remarks>
///     A sequence of tokens returned for a text tiles it exactly: concatenating every
///     <see cref="Text"/> reproduces the input, with no gaps or overlaps. Segments are words (letters
///     and digits, with internal apostrophes and hyphens), runs of whitespace, or single other
///     characters such as punctuation.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     const string text = "They will record the record.";
///     var tokens = await classifier.ClassifyAsync(text);
///
///     // The tokens tile the text, so the original can be rebuilt and offsets index into it
///     bool tiles = string.Concat(tokens.Select(t => t.Text)) == text;
///     foreach (var token in tokens.Where(t => t.Sense is not null))
///     {
///         Console.WriteLine($"{text.Substring(token.Start, token.Length)} -> {token.Sense}");
///     }
///     </code>
/// </example>
/// <param name="Start">Inclusive UTF-16 start offset of the segment in the input text.</param>
/// <param name="Length">Length of the segment in UTF-16 code units.</param>
/// <param name="Text">The segment text.</param>
/// <param name="Sense">
///     The sense of the word, or <see langword="null"/> when the segment is not a known ambiguous word or
///     the classifier abstained because its confidence was too low.
/// </param>
public sealed record WordSenseToken(int Start, int Length, string Text, WordSense? Sense);
