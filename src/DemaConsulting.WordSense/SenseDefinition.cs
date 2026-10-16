using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense;

/// <summary>
///     The definition of one sense of a lemma: its part of speech and, for a heteronym, its pronunciation meaning
///     and respelling. Advanced: used to build classifier tables; an application that only classifies words reads
///     <see cref="WordDetails"/> instead.
/// </summary>
/// <remarks>
///     A lemma is a heteronym when its senses have at least two distinct meanings; then every sense carries a
///     meaning. The meaning is a short identifier (lower-case ASCII letters, digits and hyphens, starting with a letter, at
///     most <see cref="MaxMeaningLength"/> characters) and the optional respelling uses ASCII letters, hyphens and
///     apostrophes (at most <see cref="MaxRespellingLength"/> characters). A respelling needs a meaning.
/// </remarks>
/// <param name="Pos">The part of speech.</param>
/// <param name="Meaning">The pronunciation meaning, or null for a lemma that is not a heteronym.</param>
/// <param name="Respelling">The respelling of the pronunciation, or null when none is authored.</param>
public sealed partial record SenseDefinition(PartOfSpeech Pos, string? Meaning = null, string? Respelling = null)
{
    /// <summary>
    ///     The longest meaning identifier, in characters.
    /// </summary>
    public const int MaxMeaningLength = 32;

    /// <summary>
    ///     The longest respelling, in characters.
    /// </summary>
    public const int MaxRespellingLength = 32;

    /// <summary>
    ///     Determines whether a string is a valid meaning identifier.
    /// </summary>
    /// <param name="meaning">The candidate.</param>
    /// <returns>True when it uses lower-case ASCII letters, digits and hyphens, starts with a letter and is not too long.</returns>
    internal static bool IsValidMeaning(string? meaning)
    {
        return meaning is { Length: > 0 and <= MaxMeaningLength } && MeaningPattern().IsMatch(meaning);
    }

    /// <summary>
    ///     Determines whether a string is a valid respelling.
    /// </summary>
    /// <param name="respelling">The candidate.</param>
    /// <returns>True when it uses only ASCII letters, hyphens and apostrophes and is not too long.</returns>
    internal static bool IsValidRespelling(string? respelling)
    {
        return respelling is { Length: > 0 and <= MaxRespellingLength } && RespellingPattern().IsMatch(respelling);
    }

    /// <summary>
    ///     Validates the definition on its own.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the part of speech or meaning is invalid or the respelling has no meaning.</exception>
    internal void Validate()
    {
        if (!Enum.IsDefined(Pos))
        {
            throw new ArgumentException("A sense has an undefined part of speech.");
        }

        if (Meaning is not null && !IsValidMeaning(Meaning))
        {
            throw new ArgumentException(
                $"Meaning '{Meaning}' is invalid; it must start with a lower-case letter, have at most 32 characters " +
                "and use only lower-case ASCII letters, digits and hyphens.");
        }

        if (Respelling is not null && (Meaning is null || !IsValidRespelling(Respelling)))
        {
            throw new ArgumentException("A respelling needs a meaning and may only use ASCII letters, hyphens and apostrophes.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MeaningPattern();

    [GeneratedRegex("^[A-Za-z'-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RespellingPattern();
}
