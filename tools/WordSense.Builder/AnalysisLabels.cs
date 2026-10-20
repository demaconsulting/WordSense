using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     An analysis that a corpus row stands for: a lemma, a part of speech and the features that must be present.
/// </summary>
/// <param name="Lemma">The lemma of the analysis.</param>
/// <param name="Pos">The part of speech of the analysis.</param>
/// <param name="Features">The required grammatical features (<c>Key=Value</c> items joined by <c>+</c>); empty when any features do.</param>
internal readonly record struct AnalysisKey(string Lemma, PartOfSpeech Pos, string Features)
{
    /// <summary>
    ///     Determines whether an analysis of the lexicon is the one this key stands for: the same lemma and part of speech
    ///     and every required feature item present.
    /// </summary>
    /// <param name="details">The analysis of the lexicon.</param>
    /// <returns>True when the analysis matches.</returns>
    public bool Matches(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!string.Equals(Lemma, details.Lemma, StringComparison.Ordinal) || Pos != details.PartOfSpeech)
        {
            return false;
        }

        if (Features.Length == 0)
        {
            return true;
        }

        var have = details.Features.Split('+');
        return Features.Split('+').All(item => have.Contains(item, StringComparer.Ordinal));
    }
}

/// <summary>
///     Rolls the labels of the retired class corpus (<c>corpus/legacy/anchors</c>, <c>holdout</c>, <c>realtext</c>) up to
///     analyses: the lemma, the part of speech and, for the classes that stand for one tense, the features.
/// </summary>
/// <remarks>
///     <para>
///     The corpus still carries the labels of the retired pronunciation classes (<c>lemma</c> and <c>label</c>, the class
///     id being <c>lemma#label</c>). The part of speech of a label is in <c>corpus/legacy/senses</c>; the classes that stand for
///     the entries of another lemma or for one tense of a verb (<c>read#past</c>, <c>bases#bases-base</c>) are in the
///     temporary file <c>corpus/migration/legacy-class-map.tsv</c>. A row of a class that stands for the entries of
///     several lemmas names all of them. This class is deleted together with the migration file when the corpus is
///     relabeled to fine senses.
///     </para>
/// </remarks>
internal sealed class AnalysisLabels
{
    /// <summary>
    ///     The location of the temporary migration file below the corpus directory.
    /// </summary>
    public static readonly string MigrationFile = Path.Join("migration", "legacy-class-map.tsv");

    private readonly Dictionary<(string Lemma, string Label), List<AnalysisKey>> _map;

    private AnalysisLabels(Dictionary<(string Lemma, string Label), List<AnalysisKey>> map) => _map = map;

    /// <summary>
    ///     Builds the labels from the sense inventory and the migration file.
    /// </summary>
    /// <param name="inventory">The sense inventory of the legacy corpus (<c>corpus/legacy/senses</c>).</param>
    /// <param name="corpusDirectory">The corpus directory that holds <c>migration/legacy-class-map.tsv</c>.</param>
    /// <returns>The labels.</returns>
    /// <exception cref="InvalidDataException">Thrown when the migration file is missing or malformed.</exception>
    public static AnalysisLabels Load(LegacySenseInventory inventory, string corpusDirectory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(corpusDirectory);
        var map = new Dictionary<(string, string), List<AnalysisKey>>();
        foreach (var row in inventory.Rows)
        {
            map[(row.Lemma, row.Label)] = [new AnalysisKey(row.Lemma, row.Pos, string.Empty)];
        }

        var path = Path.Join(corpusDirectory, MigrationFile);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The migration file {path} is missing.");
        }

        var overridden = new HashSet<(string, string)>();
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            var c = lines[i].Split('\t');
            if (c.Length != 13)
            {
                throw new InvalidDataException($"{MigrationFile}:{(i + 1).ToString(CultureInfo.InvariantCulture)}: expected 13 columns.");
            }

            // Only the classes that stand for entries are of interest: a class of senses keeps its inventory part of speech
            if (c[1] is not ("lemma_split" or "feature_only"))
            {
                continue;
            }

            var id = c[0];
            var hash = id.IndexOf('#', StringComparison.Ordinal);
            var key = (id[..hash], id[(hash + 1)..]);
            if (!map.ContainsKey(key))
            {
                continue;
            }

            if (overridden.Add(key))
            {
                map[key] = [];
            }

            var analysis = Resolve(new AnalysisKey(c[10], Data.DataTables.PosOf(c[11]), c[12]));
            if (!map[key].Contains(analysis))
            {
                map[key].Add(analysis);
            }
        }

        foreach (var key in map.Keys.ToList())
        {
            map[key] = map[key].Select(Resolve).ToList();
        }

        return new AnalysisLabels(map);
    }

    // A corpus lemma is lower-case, the entry of the lexicon may spell it with a capital (august the adjective, August the
    // month): an analysis that the image does not have is looked for under the spelling of the entry
    private static AnalysisKey Resolve(AnalysisKey key)
    {
        var image = WordLexicon.Current;
        if (image.AnalysisExists(key.Lemma, key.Pos, key.Features))
        {
            return key;
        }

        foreach (var spelling in new[] { key.Lemma, string.Concat(char.ToUpperInvariant(key.Lemma[0]).ToString(), key.Lemma.AsSpan(1)) })
        {
            if (WordLexicon.TryLookup(spelling, out var found)
                && found.FirstOrDefault(d => d.PartOfSpeech == key.Pos && string.Equals(d.Lemma, key.Lemma, StringComparison.OrdinalIgnoreCase)) is { } match
                && image.AnalysisExists(match.Lemma, key.Pos, key.Features))
            {
                return key with { Lemma = match.Lemma };
            }
        }

        return key;
    }

    /// <summary>
    ///     Gets the analyses a corpus row stands for.
    /// </summary>
    /// <param name="lemma">The lemma of the row.</param>
    /// <param name="label">The label of the row.</param>
    /// <returns>One analysis for a class of senses; the entries of every lemma for a class that stands for entries; empty for an unknown label.</returns>
    public IReadOnlyList<AnalysisKey> Of(string lemma, string label) =>
        _map.TryGetValue((lemma, label), out var list) ? list : [];

    /// <summary>
    ///     Determines whether an answer is right for a corpus row.
    /// </summary>
    /// <param name="lemma">The lemma of the row.</param>
    /// <param name="label">The label of the row.</param>
    /// <param name="details">The analysis the model answered, or null.</param>
    /// <returns>True when the answer is one of the analyses the row stands for.</returns>
    public bool IsRight(string lemma, string label, WordDetails? details) =>
        details is not null && Of(lemma, label).Any(key => key.Matches(details));
}
