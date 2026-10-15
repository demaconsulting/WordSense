namespace DemaConsulting.WordSense;

/// <summary>
///     The sense chosen for an ambiguous word.
/// </summary>
/// <param name="Lemma">The lemma of the word, for example <c>record</c>.</param>
/// <param name="Label">The sense label, for example <c>noun</c> or <c>verb</c>.</param>
/// <param name="Margin">
///     The confidence margin between the chosen sense and the runner-up; larger is more confident.
///     It is always at least the lemma's abstain threshold, because lower margins yield no result.
/// </param>
public sealed record WordSenseResult(string Lemma, string Label, double Margin);

/// <summary>
///     A segment of the classified text, with the sense of the word when one was determined.
/// </summary>
/// <remarks>
///     A sequence of tokens returned for a text tiles it exactly: concatenating every
///     <see cref="Text"/> reproduces the input, with no gaps or overlaps. Segments are words (letters
///     and digits, with internal apostrophes and hyphens), runs of whitespace, or single other
///     characters such as punctuation.
/// </remarks>
/// <param name="Start">Inclusive UTF-16 start offset of the segment in the input text.</param>
/// <param name="Length">Length of the segment in UTF-16 code units.</param>
/// <param name="Text">The segment text.</param>
/// <param name="Sense">
///     The sense of the word, or <see langword="null"/> when the segment is not a known ambiguous word or
///     the classifier abstained because its confidence was too low.
/// </param>
public sealed record WordSenseToken(int Start, int Length, string Text, WordSenseResult? Sense);
