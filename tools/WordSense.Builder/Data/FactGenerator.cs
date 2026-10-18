using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Data;

/// <summary>
///     Generates the source-derived lexicon facts mechanically from the sources of the lexicon, so that the fact set is not
///     chosen by the words that failed in an evaluation.
/// </summary>
/// <remarks>
///     The rows come from four sources: every irregular past, participle, plural, comparative and superlative form of the
///     closed and hand-written irregular lists whose lemma is an entry; the inflections of a deterministic sample of the regular
///     verbs and nouns of the entry table (a row is ranked by the SHA-256 hash of the seed and the lemma, and the lowest hashes are
///     taken); every contraction of the closed list; and a deterministic sample of the entries of the Open English WordNet that are
///     nouns or adjectives spelled like an -ing form. The expected regular spellings come from the inflection rules of the form
///     generator, so these rows check that the corroboration filter and the image keep what the rules produce; the spelling rules
///     themselves are checked by the forms audit against an independent oracle. The complete set of -ing noun and adjective entries
///     is checked by <see cref="CheckIngEntries" /> and is not sampled; a spelling that is also a closed-class word (everything) is
///     left out there, because the closed-class analysis is the intended one.
/// </remarks>
internal static class FactGenerator
{
    /// <summary>The default seed of the samples.</summary>
    public const int DefaultSeed = 20260101;

    /// <summary>The default number of regular verbs, regular nouns and -ing entries per part of speech.</summary>
    public const int DefaultSample = 100;

    /// <summary>The header line of a fact file.</summary>
    public const string Header = "category\tword\tlemma\tpos\tfeatures\tmode";

    /// <summary>One fact row.</summary>
    /// <param name="Category">The category (the origin of the row).</param>
    /// <param name="Word">The spelling.</param>
    /// <param name="Lemma">The lemma, or "*" for any.</param>
    /// <param name="Pos">The part of speech, or "*" for any.</param>
    /// <param name="Features">The features, or "*" for any.</param>
    internal sealed record Fact(string Category, string Word, string Lemma, string Pos, string Features)
    {
        /// <summary>Gets the tab-separated line.</summary>
        public string Line => $"{Category}\t{Word}\t{Lemma}\t{Pos}\t{Features}\tany";
    }

    /// <summary>The result of one generation.</summary>
    /// <param name="Facts">The fact rows.</param>
    /// <param name="Summary">The counts by category.</param>
    /// <param name="NotShipped">The rule-derived regular forms that the word table does not carry, by category.</param>
    internal sealed record Generated(List<Fact> Facts, SortedDictionary<string, int> Summary, SortedDictionary<string, int> NotShipped);

    /// <summary>
    ///     Generates the facts.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="corpus">The corpus folder.</param>
    /// <param name="seed">The seed of the samples.</param>
    /// <param name="sample">The sample size per kind.</param>
    /// <returns>The facts.</returns>
    public static Generated Generate(string data, string corpus, int seed, int sample)
    {
        var sources = IrregularSources.Read(Path.Combine(corpus, "lexicon"));
        var entries = ReadEntries(data);
        var lemmas = entries.Select(e => (e.Lemma, e.Pos)).ToHashSet();
        var facts = new List<Fact>();

        AddIrregular(facts, "source-irregular-closed", sources.Closed, lemmas);
        AddIrregular(facts, "source-irregular-hand", sources.Hand, lemmas);

        var irregular = sources.Closed.Keys.Concat(sources.Hand.Keys).Concat(sources.Agid.Keys).ToHashSet();
        var listed = LexiconSource.MergeAdditions(
                LexiconSource.ReadWords(Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile)),
                Path.Combine(corpus, "lexicon", LexiconSource.AdditionsFile))
            .Select(w => w.Word).ToHashSet(StringComparer.Ordinal);
        foreach (var lemma in Sample(entries.Where(e => e.Pos == "v" && IsPlain(e.Lemma) && !irregular.Contains((e.Lemma, "v"))).Select(e => e.Lemma), seed, sample))
        {
            var forms = InflectionRules.VerbRegular(lemma, listed.Contains);
            facts.Add(new Fact("source-regular-verb", InflectionRules.VerbThirdPerson(lemma), lemma, "Verb", "Person=3+Tense=Pres"));
            facts.Add(new Fact("source-regular-verb", forms.Past[0], lemma, "Verb", "Tense=Past"));
            facts.Add(new Fact("source-regular-verb", forms.Gerund[0], lemma, "Verb", "VerbForm=Ger"));
        }

        foreach (var lemma in Sample(entries.Where(e => e.Pos == "n" && IsPlain(e.Lemma) && !e.Lemma.EndsWith('s') && !irregular.Contains((e.Lemma, "n"))).Select(e => e.Lemma), seed, sample))
        {
            var plural = InflectionRules.NounPlural(lemma)[0];
            facts.Add(new Fact("source-regular-noun", plural, lemma, "Noun", "Number=Plur"));
            facts.Add(new Fact("source-possessive", lemma + "'s", lemma, "Noun", "Number=Sing+Poss=Yes"));
            facts.Add(plural.EndsWith('s')
                ? new Fact("source-possessive", plural + "'", lemma, "Noun", "Number=Plur+Poss=Yes")
                : new Fact("source-possessive", plural + "'s", lemma, "Noun", "Number=Plur+Poss=Yes"));
        }

        foreach (var row in sources.Contractions)
        {
            facts.Add(new Fact("source-contraction", row.Spelling, row.Head, "*", "Contraction=Yes"));
        }

        foreach (var (pos, name) in new[] { ("n", "Noun"), ("a", "Adjective") })
        {
            foreach (var lemma in Sample(IngEntries(entries, pos), seed, sample))
            {
                facts.Add(new Fact("source-ing-entry", lemma, lemma, name, "*"));
            }
        }

        // A regular form that a rule derives is shipped only when a second source corroborates it, so a missing one is a
        // policy decision and is counted, not asserted; every other category must hold
        var notShipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        facts = facts.Where(f =>
        {
            if (f.Category is not ("source-regular-verb" or "source-regular-noun" or "source-possessive") || Holds(f, WordLexicon.Lookup(f.Word)))
            {
                return true;
            }

            notShipped[f.Category] = notShipped.GetValueOrDefault(f.Category) + 1;
            return false;
        }).ToList();
        var distinct = facts.DistinctBy(f => (f.Category, f.Word, f.Lemma, f.Pos, f.Features)).OrderBy(f => f.Category, StringComparer.Ordinal).ThenBy(f => f.Word, StringComparer.Ordinal).ThenBy(f => f.Lemma, StringComparer.Ordinal).ThenBy(f => f.Features, StringComparer.Ordinal).ToList();
        var summary = new SortedDictionary<string, int>(distinct.GroupBy(f => f.Category).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        return new Generated(distinct, summary, notShipped);
    }

    /// <summary>
    ///     Checks every fact against the shipped word table.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The failing rows with the reason.</returns>
    public static List<string> Check(IEnumerable<Fact> facts)
    {
        return facts.Where(f => !Holds(f, WordLexicon.Lookup(f.Word))).Select(f => $"{f.Category}\t{f.Word}\t{f.Lemma}\t{f.Pos}\t{f.Features}").ToList();
    }

    /// <summary>
    ///     Checks that every -ing spelled noun or adjective entry of the data has that analysis in the shipped word table.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <returns>The number of entries checked, the number that also have the verb gerund analysis, and the entries that lack the analysis.</returns>
    public static (int Checked, int WithGerund, List<string> Missing) CheckIngEntries(string data)
    {
        var entries = ReadEntries(data);
        var missing = new List<string>();
        var checkedCount = 0;
        var withGerund = 0;
        var closed = entries.Where(e => e.Pos is not ("n" or "v" or "a" or "r")).Select(e => e.Lemma).ToHashSet(StringComparer.Ordinal);
        foreach (var (pos, part) in new[] { ("n", PartOfSpeech.Noun), ("a", PartOfSpeech.Adjective) })
        {
            foreach (var lemma in IngEntries(entries, pos).Where(l => !closed.Contains(l)))
            {
                checkedCount++;
                var senses = WordLexicon.Lookup(lemma);
                if (!senses.Any(s => s.PartOfSpeech == part && string.Equals(s.Lemma, lemma, StringComparison.OrdinalIgnoreCase)))
                {
                    missing.Add($"{lemma} {part}");
                }

                if (senses.Any(s => s.PartOfSpeech == PartOfSpeech.Verb && s.Features.Contains("VerbForm=Ger", StringComparison.Ordinal)))
                {
                    withGerund++;
                }
            }
        }

        return (checkedCount, withGerund, missing);
    }

    /// <summary>
    ///     Renders a fact file.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The file text.</returns>
    public static string Render(IEnumerable<Fact> facts)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var f in facts)
        {
            text.Append(f.Line).Append('\n');
        }

        return text.ToString();
    }

    private static bool Holds(Fact f, System.Collections.Immutable.ImmutableArray<WordDetails> senses) =>
        !senses.IsDefaultOrEmpty && senses.Any(s =>
            (f.Lemma == "*" || string.Equals(s.Lemma, f.Lemma, StringComparison.OrdinalIgnoreCase))
            && (f.Pos == "*" || s.PartOfSpeech.ToString() == f.Pos)
            && (f.Features == "*" || s.Features == f.Features));

    private static IEnumerable<string> IngEntries(List<(string Lemma, string Pos)> entries, string pos) =>
        entries.Where(e => e.Pos == pos && IsPlain(e.Lemma) && e.Lemma.Length > 4 && e.Lemma.EndsWith("ing", StringComparison.Ordinal)).Select(e => e.Lemma);

    private static bool IsPlain(string lemma) => lemma.Length > 2 && lemma.All(c => c is >= 'a' and <= 'z');

    private static List<(string Lemma, string Pos)> ReadEntries(string data)
    {
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        return DataTables.Read(data, schema.Table("entry")).Select(e => (e[1], e[2])).Distinct().ToList();
    }

    private static List<string> Sample(IEnumerable<string> lemmas, int seed, int count) =>
        lemmas.Distinct(StringComparer.Ordinal)
            .OrderBy(l => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture) + ":" + l))), StringComparer.Ordinal)
            .Take(count)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

    private static void AddIrregular(List<Fact> facts, string category, Dictionary<(string Lemma, string Pos), IrregularKinds> source, HashSet<(string Lemma, string Pos)> lemmas)
    {
        foreach (var ((lemma, letter), kinds) in source)
        {
            if (lemma.Contains(' ', StringComparison.Ordinal) || !lemmas.Contains((lemma, letter.ToLowerInvariant())))
            {
                continue;
            }

            var part = DataTables.PosOf(letter.ToLowerInvariant());
            foreach (var kind in new[] { "Past", "Participle", "Plural", "Comparative", "Superlative" })
            {
                foreach (var (form, _) in kinds.Of(kind)!)
                {
                    if (!form.Contains(' ', StringComparison.Ordinal))
                    {
                        facts.Add(new Fact(category, form, lemma, part.ToString(), kind == "Plural" ? "Number=Plur" : "*"));
                    }
                }
            }
        }
    }
}
