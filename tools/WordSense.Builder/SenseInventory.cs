using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The outcome of checking corpus rows against the sense catalog.
/// </summary>
/// <param name="Errors">The violations that make the corpus unusable, one message each.</param>
/// <param name="Warnings">The suspicious rows that do not stop the training, one message each.</param>
internal sealed record CorpusValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>
    ///     Gets a value indicating whether the corpus has no error (warnings do not count).
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    ///     Throws when the corpus has an error.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown with every error, one per line.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, Errors));
        }
    }
}

/// <summary>
///     The sense inventory of the builder: the sense catalog of the word image (<see cref="WordLexicon"/>), which is the
///     authority on which fine senses exist, and the rules that tie a corpus row to it.
/// </summary>
/// <remarks>
///     <para>
///     There is no inventory file any more: the senses, their parts of speech, groups and order come from the word
///     image that the library ships, so the training data, the trainer and the run time read one inventory. The
///     inventory order of the senses of a lemma is the part of speech in enumeration order, and within a part of speech
///     the order of <see cref="WordLexicon.SensesOf"/> (the order of Open English WordNet).
///     </para>
///     <para>
///     A corpus row is valid when its lemma is lower-case, its sense exists in the catalog and belongs to the lemma of
///     the row, and the part of speech of the row is the one of the sense (errors); and when its braced target word,
///     lower-cased, is looked up in the lexicon and one analysis has the lemma and the part of speech of the row (a
///     warning when it does not, because the model then never meets the lemma at that word).
///     </para>
/// </remarks>
internal static class SenseInventory
{
    /// <summary>
    ///     Lists every sense of a lemma in inventory order.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The senses of every part of speech: the parts of speech in enumeration order, the senses of each in catalog order; empty for an unknown lemma.</returns>
    public static ImmutableArray<SenseInfo> SensesOf(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        var senses = ImmutableArray.CreateBuilder<SenseInfo>();
        foreach (var pos in Enum.GetValues<PartOfSpeech>())
        {
            senses.AddRange(WordLexicon.SensesOf(lemma, pos));
        }

        return senses.ToImmutable();
    }

    /// <summary>
    ///     Checks a row's sense against the catalog.
    /// </summary>
    /// <param name="lemma">The lemma of the row.</param>
    /// <param name="senseId">The sense id of the row.</param>
    /// <param name="pos">The part of speech of the row.</param>
    /// <returns>A message that describes the disagreement, or null when the row is consistent with the catalog.</returns>
    public static string? CheckSense(string lemma, string senseId, PartOfSpeech pos)
    {
        if (!WordLexicon.TryGetSense(senseId, out var sense))
        {
            return $"the sense '{senseId}' is not in the sense catalog.";
        }

        if (!string.Equals(sense.Lemma, lemma, StringComparison.Ordinal))
        {
            return $"the sense '{senseId}' belongs to the lemma '{sense.Lemma}', not to '{lemma}'.";
        }

        return sense.PartOfSpeech == pos
            ? null
            : $"the part of speech of the row is {pos}, but the sense '{senseId}' is a {sense.PartOfSpeech}.";
    }

    /// <summary>
    ///     Checks the rows of one set against all the rules.
    /// </summary>
    /// <param name="set">The name of the set (<c>anchors</c> or <c>holdout</c>), for the messages.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>
    ///     The errors (a row that disagrees with the catalog) and the warnings (a row whose only problem is that its
    ///     target word is no form of its lemma and part of speech in the lexicon, grouped by file, lemma and target).
    /// </returns>
    public static CorpusValidation ValidateRules(string set, IEnumerable<CorpusRow> rows)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(rows);
        var errors = new List<string>();
        var unresolved = new List<CorpusRow>();
        foreach (var row in rows)
        {
            if (!string.Equals(row.Lemma, row.Lemma.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errors.Add($"{set}/{row.Source}: the lemma '{row.Lemma}' must be lower-case: {row.Sentence}");
            }
            else if (CheckSense(row.Lemma, row.SenseId, row.PartOfSpeech) is { } disagreement)
            {
                errors.Add($"{set}/{row.Source}: {disagreement} {row.Sentence}");
            }
            else if (!TargetResolves(row))
            {
                unresolved.Add(row);
            }
        }

        var warnings = unresolved
            .GroupBy(r => (r.Source, r.Lemma, r.PartOfSpeech, Target: r.Words[r.TargetWordIndex].ToLowerInvariant()))
            .OrderBy(g => g.Key.Source, StringComparer.Ordinal).ThenBy(g => g.Key.Lemma, StringComparer.Ordinal)
            .ThenBy(g => g.Key.PartOfSpeech).ThenBy(g => g.Key.Target, StringComparer.Ordinal)
            .Select(g => $"{set}/{g.Key.Source}: {g.Count()} row(s) mark '{g.Key.Target}', which the lexicon does not analyze as the {g.Key.PartOfSpeech} '{g.Key.Lemma}' (first: {g.First().Sentence}).")
            .ToList();
        return new CorpusValidation(errors, warnings);
    }

    /// <summary>
    ///     Checks the anchors and the holdout against the catalog and against each other.
    /// </summary>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The evaluation rows.</param>
    /// <returns>The errors and the warnings of both sets; the anchors and the holdout must also be disjoint (an error).</returns>
    public static CorpusValidation Validate(IReadOnlyList<CorpusRow> anchors, IReadOnlyList<CorpusRow> holdout)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(holdout);
        var first = ValidateRules("anchors", anchors);
        var second = ValidateRules("holdout", holdout);
        var errors = first.Errors.Concat(second.Errors).ToList();
        try
        {
            Corpus.EnsureDisjoint(anchors, holdout);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(ex.Message);
        }

        return new CorpusValidation(errors, first.Warnings.Concat(second.Warnings).ToList());
    }

    /// <summary>
    ///     Determines whether the lower-cased target word of a row is analyzed by the lexicon as the lemma and the part
    ///     of speech of the row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>True when one analysis of the target word has the lemma and the part of speech of the row.</returns>
    private static bool TargetResolves(CorpusRow row) =>
        WordLexicon.TryLookup(row.Words[row.TargetWordIndex].ToLowerInvariant(), out var analyses)
        && analyses.Any(a => string.Equals(a.Lemma, row.Lemma, StringComparison.Ordinal) && a.PartOfSpeech == row.PartOfSpeech);
}
