namespace DemaConsulting.WordSense;

/// <summary>
///     How a heteronym (a word spelled one way but pronounced differently depending on its meaning) is pronounced
///     in its sentence.
/// </summary>
/// <remarks>
///     A word has a pronunciation only when its lemma is a heteronym, that is, when the lemma has at least two
///     distinct meanings that are pronounced differently, such as <c>lead</c> (to guide) and <c>lead</c> (the metal).
///     For every other word <see cref="WordDetails.Pronunciation"/> is <see langword="null"/>. The library holds no
///     audio or phoneme data; the respelling is our own plain-letter spelling of the sound, offered as a convenience.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordAnalysis[] words = await classifier.ClassifyAsync(["The", "pipe", "is", "made", "of", "lead"]);
///
///     if (words[5].Details?.Pronunciation is { } pronunciation)
///     {
///         Console.WriteLine($"{pronunciation.Meaning}: {pronunciation.Respelling ?? "(no respelling)"}");
///     }
///     </code>
/// </example>
/// <param name="Meaning">
///     A short identifier of the meaning that determines the pronunciation, for example <c>bend</c> or <c>weapon</c>
///     for <c>bow</c>, <c>past</c> or <c>present</c> for <c>read</c>, or <c>noun</c> or <c>verb</c> for a word whose
///     stress depends on its part of speech. Lower-case letters, digits and hyphens.
/// </param>
/// <param name="Respelling">
///     Our own plain respelling of the pronunciation (letters, hyphens and apostrophes; stressed syllables in capitals
///     when there are several), or <see langword="null"/> when none has been authored.
/// </param>
public sealed record Pronunciation(string Meaning, string? Respelling);
