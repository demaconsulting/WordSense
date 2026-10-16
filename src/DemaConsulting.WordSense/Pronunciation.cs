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
///     Every heteronym in the shipped table has a respelling, so <see cref="Respelling"/> is null only for tables
///     that were built without one.
///     <para>
///         For speech synthesis, key your own pronunciation lexicon on the pair (<see cref="WordDetails.Lemma"/>,
///         <see cref="Meaning"/>): the meaning id is the stable value, and the respelling is a human-readable hint you
///         can translate into your engine's phoneme notation, or feed to an engine that accepts plain respellings.
///         Treat a null <see cref="WordDetails.Pronunciation"/> as "use the engine's default pronunciation".
///     </para>
///     <para>
///         The part of speech and the pronunciation of a word always come from the same decision (the best
///         candidate), so they never contradict each other. When the pronunciation follows the part of speech, as for
///         <c>record</c>, the meaning ids repeat it (<c>noun</c>, <c>verb</c>).
///     </para>
///     <para>
///         Examples: <c>bow</c> as a gesture of respect, as in "take a bow", has the meaning <c>bend</c> (the
///         respelling <c>bow</c> rhymes with "now"); <c>bow</c> as in "bow and arrow" has the meaning <c>weapon</c>
///         (respelling <c>boh</c>).
///     </para>
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
///     stress depends on its part of speech. Lower-case ASCII letters, digits and hyphens; it must start with a
///     letter and is at most 32 characters.
/// </param>
/// <param name="Respelling">
///     Our own plain respelling of the pronunciation, or <see langword="null"/> when none has been authored. It uses
///     ASCII letters, hyphens and apostrophes only: syllables are separated by hyphens and, when there are several, the
///     stressed syllable is written in capitals (<c>REK-urd</c> for the noun <c>record</c>, <c>rih-KORD</c> for the
///     verb; <c>bow</c> and <c>boh</c> for the two meanings of <c>bow</c>). It is not IPA or any dictionary's scheme.
/// </param>
public sealed record Pronunciation(string Meaning, string? Respelling);
