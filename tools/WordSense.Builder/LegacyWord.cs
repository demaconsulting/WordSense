using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One answer of the legacy word table that the corpus builders produce: lemma, part of speech, pronunciation meaning
///     and respelling. It is the input of <c>bootstrap-forms</c> and of the model trainers; the public
///     <see cref="WordDetails"/> no longer carries a meaning or a respelling.
/// </summary>
/// <param name="Lemma">The lower-case lemma.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Meaning">The pronunciation meaning, or null.</param>
/// <param name="Respelling">The respelling, or null.</param>
internal sealed record LegacyWord(string Lemma, PartOfSpeech PartOfSpeech, string? Meaning, string? Respelling);
