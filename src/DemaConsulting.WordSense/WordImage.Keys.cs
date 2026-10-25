using System.Collections.Immutable;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     The enumeration of every word key of the image (see <see cref="WordKey"/>).
/// </summary>
internal sealed partial class WordImage
{
    /// <summary>
    ///     Enumerates every key that a classification can return: for every normalized
    ///     spelling and every analysis a lookup of the spelling can give, the key without a label and one key for each
    ///     sense of the lemma and part of speech of the analysis.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The execution is deferred and streaming after one pre-pass: nothing runs until the sequence is read; the first
    ///     read makes one pass over all spellings to group those that differ only in case (the fold groups), and then
    ///     every key is produced while the sequence is read, without keeping a list of the keys.
    ///     </para>
    ///     <para>
    ///     The spelling of a key is the normalized form of the word (lower case, straight apostrophe), and a lookup merges
    ///     the stored spellings that differ only in case, so the analyses of a normalized spelling are those of all its
    ///     stored case variants. A possessive (<c>dog's</c>, <c>dogs'</c>) that is not stored is answered with the
    ///     noun and pronoun analyses of its stem plus <c>Poss=Yes</c>, so for every normalized spelling with such analyses
    ///     the possessive spellings that the lookup would derive are enumerated too. No key is listed twice.
    ///     </para>
    ///     <para>
    ///     The catalog is read lazily; it must have been validated (<see cref="ValidateCatalog"/>) before the first call.
    ///     </para>
    /// </remarks>
    /// <returns>The keys in a fixed order: by spelling, then by analysis in answer order, the key without a label first.</returns>
    public IEnumerable<string> Keys()
    {
        var foldGroups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < SpellingCount; i++)
        {
            var spelling = Spelling(i);
            var lower = BasicClassifier.Normalize(spelling);
            if (lower.Length > 0 && !string.Equals(lower, spelling, StringComparison.Ordinal))
            {
                if (!foldGroups.TryGetValue(lower, out var group))
                {
                    foldGroups[lower] = group = [];
                }

                group.Add(i);
            }
        }

        for (var i = 0; i < SpellingCount; i++)
        {
            var spelling = Spelling(i);
            var normalized = BasicClassifier.Normalize(spelling);
            if (normalized.Length == 0)
            {
                continue;
            }

            // A normalized spelling is handled once: at its stored lower-case spelling, and when there is none at its first
            // stored case variant
            var members = new List<int>();
            if (string.Equals(normalized, spelling, StringComparison.Ordinal))
            {
                members.Add(i);
            }
            else if (Find(normalized) >= 0 || foldGroups[normalized][0] != i)
            {
                continue;
            }

            if (foldGroups.TryGetValue(normalized, out var variants))
            {
                members.AddRange(variants);
            }

            var analyses = members.SelectMany(m => Materialize([m])).Distinct().ToList();
            foreach (var key in KeysOf(normalized, analyses))
            {
                yield return key;
            }

            foreach (var key in PossessiveKeys(normalized, analyses, foldGroups))
            {
                yield return key;
            }
        }
    }

    private IEnumerable<string> KeysOf(string spelling, List<WordDetails> analyses)
    {
        foreach (var analysis in analyses)
        {
            yield return WordKey.Format(spelling, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, null);
            foreach (var label in GetCatalog().LabelsOf(analysis.Lemma, analysis.PartOfSpeech))
            {
                yield return WordKey.Format(spelling, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, label);
            }
        }
    }

    private IEnumerable<string> PossessiveKeys(string stem, List<WordDetails> stemAnalyses, Dictionary<string, List<int>> foldGroups)
    {
        if (stem.Contains('\'', StringComparison.Ordinal))
        {
            yield break;
        }

        var possessive = stemAnalyses
            .Where(a => a.PartOfSpeech is PartOfSpeech.Noun or PartOfSpeech.Pronoun)
            .Select(a => a with { Features = WithPossessive(a.Features) })
            .Distinct()
            .ToList();
        if (possessive.Count == 0)
        {
            yield break;
        }

        var words = new List<string> { stem + "'s" };
        if (stem.Length > 1 && stem.EndsWith('s'))
        {
            words.Add(stem + "'");
        }

        foreach (var word in words.Where(w => Find(w) < 0 && !foldGroups.ContainsKey(w)))
        {
            foreach (var key in KeysOf(word, possessive))
            {
                yield return key;
            }
        }
    }
}
