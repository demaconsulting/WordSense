using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One lemma of the word table, with its senses and whether it needs context to choose between them.
/// </summary>
/// <remarks>
///     <para>
///     An entry with one sense that needs no context is a dictionary word; an entry with several senses that needs no
///     context is a dictionary word whose candidates are known but which nobody can resolve; a context entry
///     (<see cref="IsContextual"/>) is a lemma that a model's weights can disambiguate.
///     </para>
///     <para>
///     The senses must agree: a (part of speech, meaning) pair is unique; either all or none carry a meaning; a lemma
///     with meanings has at least two distinct ones; a meaning has one respelling and distinct meanings have distinct
///     respellings. A dictionary entry carries no meanings. A context entry needs at least two senses. The order in
///     which the senses are supplied defines the order of the weight rows of every model. Instances are immutable
///     and thread-safe.
///     </para>
/// </remarks>
internal sealed class LemmaEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaEntry"/> class.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="senses">The sense definitions, at least one; their order defines the weight-row order.</param>
    /// <param name="isContextual">True for a lemma that needs context; false for a dictionary lemma.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="lemma"/> or <paramref name="senses"/> is null, or an element of
    ///     <paramref name="senses"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the senses are inconsistent.</exception>
    public LemmaEntry(string lemma, IReadOnlyList<SenseDefinition> senses, bool isContextual = false)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        ArgumentNullException.ThrowIfNull(senses);

        if (senses.Count < 1)
        {
            throw new ArgumentException("A lemma needs at least one sense.", nameof(senses));
        }

        // Every sense must be valid, and the senses must be consistent as a set
        foreach (var definition in senses)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();
        }

        ValidateSet(senses);

        if (!isContextual)
        {
            if (senses.Any(s => s.Meaning is not null))
            {
                throw new ArgumentException("A dictionary lemma cannot carry meanings.", nameof(senses));
            }
        }
        else if (senses.Count < 2)
        {
            throw new ArgumentException("A lemma that needs context has at least two senses.", nameof(senses));
        }

        Lemma = lemma;
        Senses = senses.ToArray();
        IsContextual = isContextual;

        // One shared answer per sense, created once
        Candidates = Senses
            .Select(s => new LegacyWord(lemma, s.Pos, s.Meaning, s.Respelling))
            .ToArray();
    }

    /// <summary>
    ///     Checks that the senses of a lemma agree with each other.
    /// </summary>
    /// <param name="senses">The senses.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when meanings are given for only some senses, a heteronym has fewer than two distinct meanings, a
    ///     (part of speech, meaning) pair repeats, a meaning has inconsistent respellings, or two distinct meanings
    ///     share a respelling.
    /// </exception>
    internal static void ValidateSet(IReadOnlyList<SenseDefinition> senses)
    {
        var withMeaning = senses.Count(s => s.Meaning is not null);
        if (withMeaning != 0 && withMeaning != senses.Count)
        {
            throw new ArgumentException("Either every sense or no sense of a lemma carries a meaning.", nameof(senses));
        }

        if (senses.Select(s => (s.Pos, s.Meaning)).Distinct().Count() != senses.Count)
        {
            throw new ArgumentException("A (part of speech, meaning) pair must be unique within a lemma.", nameof(senses));
        }

        if (withMeaning == 0)
        {
            return;
        }

        var byMeaning = senses.GroupBy(s => s.Meaning, StringComparer.Ordinal).ToList();
        if (byMeaning.Count < 2)
        {
            throw new ArgumentException("A lemma with meanings needs at least two distinct meanings.", nameof(senses));
        }

        // One respelling per meaning, and distinct meanings sound different
        var respellings = new List<string>();
        foreach (var group in byMeaning)
        {
            var spelled = group.Select(s => s.Respelling).Distinct(StringComparer.Ordinal).ToList();
            if (spelled.Count != 1)
            {
                throw new ArgumentException($"Meaning '{group.Key}' has inconsistent respellings.", nameof(senses));
            }

            if (spelled[0] is { } respelling)
            {
                respellings.Add(respelling);
            }
        }

        if (respellings.Distinct(StringComparer.Ordinal).Count() != respellings.Count)
        {
            throw new ArgumentException("Distinct meanings of a lemma must have distinct respellings.", nameof(senses));
        }
    }

    /// <summary>
    ///     Gets the lower-case lemma.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the sense definitions in the supplied order (which defines the order of the weight rows).
    /// </summary>
    public IReadOnlyList<SenseDefinition> Senses { get; }

    /// <summary>
    ///     Gets the answer each sense stands for, parallel to <see cref="Senses"/>: the lemma, the part of speech and,
    ///     for a heteronym, the pronunciation. Exactly one shared instance exists per sense of this entry instance.
    /// </summary>
    public IReadOnlyList<LegacyWord> Candidates { get; }

    /// <summary>
    ///     Gets a value indicating whether the lemma is a heteronym, that is, whether its senses have at least two
    ///     distinct meanings.
    /// </summary>
    public bool IsHeteronym => Senses[0].Meaning is not null;

    /// <summary>
    ///     Gets a value indicating whether the lemma needs context to choose between its senses, so that an encoder
    ///     model can classify it. False for a dictionary lemma.
    /// </summary>
    public bool IsContextual { get; }
}
