// cspell:ignore officialnesses undisinfecteding
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder.Data;

/// <summary>
///     The counts of one run of the form generator.
/// </summary>
/// <param name="Entries">The entries written.</param>
/// <param name="Spellings">The distinct spellings written.</param>
/// <param name="Analyses">The analyses written.</param>
/// <param name="SurfaceOnly">The analyses without an entry.</param>
/// <param name="Frozen">The analyses carried unchanged from the reviewed rows.</param>
/// <param name="ScopeRows">The scope rows written.</param>
/// <param name="Provenance">The analyses by provenance.</param>
/// <param name="Notes">Further named counts of the run.</param>
internal sealed record FormGeneratorResult(
    int Entries,
    int Spellings,
    int Analyses,
    int SurfaceOnly,
    int Frozen,
    int ScopeRows,
    SortedDictionary<string, int> Provenance,
    SortedDictionary<string, int> Notes)
{
    /// <summary>
    ///     Renders the counts as text.
    /// </summary>
    /// <returns>The text.</returns>
    public string Render()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Entries: {Entries}; spellings: {Spellings}; analyses: {Analyses} ({SurfaceOnly} surface-only, {Frozen} frozen); scope rows: {ScopeRows}.\n");
        text.Append("Provenance: ").AppendJoin(", ", Provenance.Select(p => $"{p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}")).Append('\n');
        text.Append("Notes: ").AppendJoin(", ", Notes.Select(p => $"{p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}")).Append('\n');
        return text.ToString();
    }
}

/// <summary>
///     Generates the form table lemma-first: every entry yields its own inflected forms from the irregular lists, the
///     Open English WordNet written forms, the forms of the inflection database and the regular rules; every dictionary word
///     that no entry yields becomes a surface-only analysis.
/// </summary>
internal static partial class FormGenerator
{
    private const string Pos = "nvarupcidx";
    private const string ClosedPos = "upcidx";
    private const string LexiconLetters = "NRVJBPCIDA";
    private const string LexiconPos = "nuvarpcidx";

    // The order of the provenances when two sources give the same analysis: the first is kept
    private static readonly string[] ProvenanceOrder =
        ["hand", "irregular_list", "contraction", "oewn_form", "carried", "rule", "agid", "rule_variant", "moby_reverse", "llm"];

    private sealed record Old(string Spelling, int Rank, string EntryId, string Features, string Source, string Reviewed, string Provenance);

    // Auto: the item is a plain regular inflection (or a primary listed form) of a real lemma, which the rule and the lemma attest together
    private sealed record Item(string Spelling, string? EntryId, string? Lemma, string? PosLetter, string Features, string Provenance, string Reviewed, bool Twin = false, bool Auto = false);

    private sealed class Context
    {
        public Dictionary<string, string[]> Entries { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Sensed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<Item>> Items { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FrozenSpellings { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the spellings that lost a frozen analysis because no rule or list derives it.</summary>
        public HashSet<string> DroppedFrozen { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the entries of the frozen analyses of each frozen spelling.</summary>
        public Dictionary<string, HashSet<string>> FrozenEntries { get; } = new(StringComparer.Ordinal);

        // The features, one by one, of the frozen analyses of a spelling by entry
        public HashSet<(string Spelling, string EntryId, string Feature)> FrozenFeatures { get; } = [];

        public Dictionary<string, HashSet<string>> FrozenPos { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, List<Item>> FrozenExtras { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<(string Lemma, string Pos, string Features, string Action)>> Corrections { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Stats { get; } = new(StringComparer.Ordinal);

        // The dictionary words and the spellings that the inflection database attests, for the corroboration of the generated forms
        public Dictionary<string, string[]> Moby { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, HashSet<string>> Attested { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Gets the (form, lemma) pairs that the labelled anchor sentences use.</summary>
        public HashSet<(string, string)> Labelled { get; } = [];

        // A form of the hand or closed irregular lists that the reviewed analyses of the same entry lack (beat and cast as a past beside the
        // reviewed infinitive): the list is a source of the form, and the reviewed rows are kept as they are beside it
        private bool MissesListedForm(Item item, string entryId) =>
            item.Provenance is "hand" or "irregular_list" && IsInflected(item)
            && item.Features.Split('+').Where(x => x is "Tense=Past" or "VerbForm=Part").ToList() is { Count: > 0 } wanted
            && !wanted.Any(x => FrozenFeatures.Contains((item.Spelling, entryId, x)));

        public void Count(string name) => Stats[name] = Stats.GetValueOrDefault(name) + 1;

        public void Add(Item item)
        {
            if (FrozenSpellings.Contains(item.Spelling))
            {
                // A spelling that lost a frozen analysis as not derivable (moped as a form of mop) takes the generated analyses of
                // other entries after the frozen ones, when they are corroborated (moped as the past of mope)
                // The entry that carries an -ing spelling as its own lemma (the noun tracing beside the gerund of trace) and the forms of
                // the irregular lists (the plural lives of life, the past lay of lie) are kept the same way: a reviewed analysis of another
                // entry must not hide them. Other reviewed spellings stay exactly as they were reviewed (bowed is only the bend verb).
                if (item.EntryId is { } entryId && (!(FrozenEntries.TryGetValue(item.Spelling, out var have) && have.Contains(entryId)) || MissesListedForm(item, entryId))
                    && ((DroppedFrozen.Contains(item.Spelling) && IsInflected(item) && Corroborated(this, item))
                        || (!IsInflected(item) && item.Spelling.EndsWith("ing", StringComparison.Ordinal) && string.Equals(Entries[entryId][1], item.Spelling, StringComparison.OrdinalIgnoreCase))
                        || item.Provenance is "hand" or "irregular_list"))
                {
                    if (!FrozenExtras.TryGetValue(item.Spelling, out var extras))
                    {
                        FrozenExtras[item.Spelling] = extras = [];
                    }

                    if (!extras.Any(x => x.EntryId == item.EntryId && x.Features == item.Features))
                    {
                        extras.Add(item);
                    }
                }

                return;
            }

            if (!Items.TryGetValue(item.Spelling, out var list))
            {
                Items[item.Spelling] = list = [];
            }

            list.Add(item);
        }
    }

    /// <summary>
    ///     Regenerates the form tables of a data folder and the entries they need.
    /// </summary>
    /// <param name="data">The data directory; the entry, form and form_sense_scope tables are replaced.</param>
    /// <param name="corpus">The corpus directory with the dictionary and the source lists.</param>
    /// <param name="oewn">The Open English WordNet file with the written forms, or null for none.</param>
    /// <param name="overwrite">True to regenerate tables that already carry the provenance column.</param>
    /// <param name="agid">The inflection database file <c>infl.txt</c> that corroborates generated forms, or null for none.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the tables are inconsistent or already regenerated and <paramref name="overwrite"/> is false.</exception>
    public static FormGeneratorResult Run(string data, string corpus, string? oewn, bool overwrite, string? agid = null)
    {
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        var formTable = schema.Table("form");
        var context = new Context();
        context.Attested = agid is null ? [] : AgidImporter.ReadAttested(agid);
        var oldForms = ReadOld(data, schema, overwrite);
        var scopes = DataTables.Read(data, schema.Table("form_sense_scope")).ToList();
        foreach (var row in DataTables.Read(data, schema.Table("entry")))
        {
            context.Entries[row[0]] = row;
        }

        foreach (var row in DataTables.Read(data, schema.Table("sense")))
        {
            context.Sensed.Add(row[2]);
        }

        var classEntries = DataTables.Read(data, schema.Table("class_entry")).Select(r => r[1]).ToHashSet(StringComparer.Ordinal);
        var scoped = scopes.Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        var sources = IrregularSources.Read(Path.Combine(corpus, "lexicon"));
        var correctionsPath = Path.Combine(corpus, "lexicon", "form-corrections.tsv");
        if (File.Exists(correctionsPath))
        {
            foreach (var cells in File.ReadAllLines(correctionsPath, Encoding.UTF8).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.TrimEnd('\r').Split('\t')))
            {
                if (cells.Length < 6)
                {
                    throw new InvalidDataException($"{correctionsPath}: a correction needs 6 columns: {string.Join('\t', cells)}");
                }

                if (!context.Corrections.TryGetValue(cells[0], out var list))
                {
                    context.Corrections[cells[0]] = list = [];
                }

                list.Add((cells[1], cells[2], cells[3], cells[4]));
            }
        }
        var moby = LexiconSource.MergeAdditions(
                LexiconSource.ReadWords(Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile)),
                Path.Combine(corpus, "lexicon", LexiconSource.AdditionsFile))
            .ToDictionary(w => w.Word, w => w.Letters.Select(l => LexiconPos[LexiconLetters.IndexOf(l, StringComparison.Ordinal)].ToString()).Distinct().ToArray(), StringComparer.Ordinal);
        context.Moby = moby;
        var written = oewn is null ? [] : OewnImporter.ReadForms(oewn);
        var formsDirectory = Path.Combine(corpus, "forms");
        if (Directory.Exists(formsDirectory))
        {
            foreach (var (form, lemma, _) in Directory.GetFiles(formsDirectory, "*.tsv").SelectMany(Corpus.ReadForms))
            {
                context.Labelled.Add((form.ToLowerInvariant(), lemma.ToLowerInvariant()));
            }
        }

        // Frozen rows: the reviewed and scoped analyses stay exactly as they are, except an inflected analysis that no rule or
        // list derives for its entry (the old plural of an -ing noun, checking as the plural of check): it is dropped and the
        // spelling is generated like any other
        var reviewed = oldForms.Where(f => f.Reviewed == "1" && f.Provenance is "" or "carried" || scoped.Contains(f.Spelling)).ToList();
        var kept = new List<(Old Row, int Original, int Order)>();
        foreach (var f in reviewed)
        {
            if (Derivable(context, sources, written, f))
            {
                kept.Add((MergePastParticiple(context, sources, reviewed, f), f.Rank, f.Rank));
            }
            else if (GerundNoun(context, f) is { } gerund)
            {
                // The anchor sentences use the -ing form as a noun of the verb's lemma (the walking, a failing); it is a gerund
                // noun, not a plural, and it follows the verb analysis
                kept.Add((f with { Features = gerund }, f.Rank, f.Rank + 1000));
                context.DroppedFrozen.Add(f.Spelling);
            }
            else
            {
                context.DroppedFrozen.Add(f.Spelling);
            }
        }

        context.Stats["frozen_inflections_dropped_not_derivable"] = reviewed.Count - kept.Count;

        // The remaining analyses of a spelling are renumbered and the sense scopes follow them
        var newRank = new Dictionary<(string, int), int>();
        var frozen = new List<Old>();
        foreach (var group in kept.GroupBy(k => k.Row.Spelling, StringComparer.Ordinal))
        {
            var next = 0;
            foreach (var k in group.OrderBy(k => k.Order))
            {
                newRank[(k.Row.Spelling, k.Original)] = next;
                frozen.Add(k.Row with { Rank = next++ });
            }
        }

        scopes = scopes.Where(s => !scoped.Contains(s[0]) || newRank.ContainsKey((s[0], int.Parse(s[1], CultureInfo.InvariantCulture))))
            .Select(s => scoped.Contains(s[0]) ? new[] { s[0], newRank[(s[0], int.Parse(s[1], CultureInfo.InvariantCulture))].ToString(CultureInfo.InvariantCulture), s[2] } : s).ToList();
        foreach (var f in frozen)
        {
            context.FrozenSpellings.Add(f.Spelling);
            if (f.EntryId.Length > 0)
            {
                if (!context.FrozenEntries.TryGetValue(f.Spelling, out var entrySet))
                {
                    context.FrozenEntries[f.Spelling] = entrySet = [];
                }

                entrySet.Add(f.EntryId);
                foreach (var feature in f.Features.Split('+'))
                {
                    context.FrozenFeatures.Add((f.Spelling, f.EntryId, feature));
                }
            }
            if (f.EntryId.Length > 0 && context.Entries.TryGetValue(f.EntryId, out var frozenEntry))
            {
                if (!context.FrozenPos.TryGetValue(f.Spelling, out var posSet))
                {
                    context.FrozenPos[f.Spelling] = posSet = [];
                }

                posSet.Add(frozenEntry[2]);
            }
        }

        var frozenSpellings = context.FrozenSpellings;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in context.Entries.Values.Where(e => context.Sensed.Contains(e[0]) || ClosedPos.Contains(e[2], StringComparison.Ordinal) || classEntries.Contains(e[0])))
        {
            keep.Add(e[0]);
        }

        foreach (var f in frozen.Where(f => f.EntryId.Length > 0))
        {
            keep.Add(f.EntryId);
        }

        // Entries that yield forms
        var real = context.Entries.Values.Where(e => context.Sensed.Contains(e[0]) && "nvar".Contains(e[2], StringComparison.Ordinal))
            .OrderBy(e => e[0], StringComparer.Ordinal).ToList();
        var byLemma = new Dictionary<(string, string), List<string[]>>();
        foreach (var e in context.Entries.Values.Where(e => keep.Contains(e[0]) && e[4] == "1" && "nvar".Contains(e[2], StringComparison.Ordinal)).OrderBy(e => e[0], StringComparer.Ordinal))
        {
            var key = (e[1].ToLowerInvariant(), e[2]);
            if (!byLemma.TryGetValue(key, out var list))
            {
                byLemma[key] = list = [];
            }

            list.Add(e);
        }

        foreach (var e in real)
        {
            Generate(context, e, sources, written, moby);
        }

        AddAuxiliaries(context, sources, keep);
        AddContractions(context, sources, keep);

        // Hand overrides of the reverse rules
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (spelling, list) in sources.Overrides.OrderBy(o => o.Key, StringComparer.Ordinal))
        {
            overridden.Add(spelling);
            foreach (var o in list)
            {
                if (byLemma.TryGetValue((o.Lemma, o.Pos), out var hits))
                {
                    context.Add(new Item(spelling, hits[0][0], null, null, o.Features, "hand", "1"));
                }
                else
                {
                    context.Add(new Item(spelling, null, o.Lemma, o.Pos, o.Features, "hand", "0"));
                }
            }
        }

        // The old analyses of closed-class entries stay unless the spelling already has an analysis of that part of speech
        var oldRank = new Dictionary<string, List<(string Lemma, string Pos, int Rank)>>(StringComparer.Ordinal);
        foreach (var f in oldForms)
        {
            if (!context.Entries.TryGetValue(f.EntryId, out var e))
            {
                continue;
            }

            if (!oldRank.TryGetValue(f.Spelling, out var ranks))
            {
                oldRank[f.Spelling] = ranks = [];
            }

            ranks.Add((e[1], e[2], f.Rank));
        }

        foreach (var f in oldForms)
        {
            if (frozenSpellings.Contains(f.Spelling) || !context.Entries.TryGetValue(f.EntryId, out var e) || !ClosedPos.Contains(e[2], StringComparison.Ordinal))
            {
                continue;
            }

            if (context.Items.TryGetValue(f.Spelling, out var have) && have.Any(h => PosOf(context, h) == e[2]))
            {
                continue;
            }

            context.Add(new Item(f.Spelling, f.EntryId, null, null, f.Features, "carried", f.Reviewed));
        }

        // A generated form ships only when a second, independent source corroborates it; the rest stays unknown
        Prune(context);
        ClassifyMoby(context, moby, byLemma, overridden, ReadAdded(Path.Combine(corpus, "lexicon", LexiconSource.AdditionsFile)), sources);
        Prune(context);
        var result = Write(data, schema, formTable, context, frozen, scopes, oldRank, keep);
        return result;
    }

    private static readonly string[] InflectedFeatures = ["Number=Plur", "Person=3", "Tense=Past", "VerbForm=Part", "VerbForm=Ger", "Degree=Cmp", "Degree=Sup"];

    private static bool IsInflected(Item item) =>
        item.Features.Split('+').Any(f => InflectedFeatures.Contains(f, StringComparer.Ordinal));

    /// <summary>
    ///     Tells whether a generated analysis is corroborated: a curated source gave it, or it is a plain regular inflection of a
    ///     real lemma (the rule and the lemma attest it together), or the dictionary lists the spelling, or the inflection
    ///     database attests it as a form of the lemma. A rule or reverse-rule form of the dictionary corroborates itself only
    ///     through such a second source when it is not a plain regular inflection.
    /// </summary>
    private static bool Corroborated(Context context, Item item)
    {
        if (item.Provenance is "hand" or "irregular_list" or "contraction" or "oewn_form" or "carried" or "llm" || !IsInflected(item))
        {
            return true;
        }

        if (item.Auto)
        {
            return true;
        }

        var low = item.Spelling.ToLowerInvariant();

        // A comparative or superlative of the inflection database corroborates itself only through the regular rule, which makes
        // it automatic already: a listed word that happens to start with the lemma (modest, twiner) is no form of it
        if (item.Provenance == "agid" && item.Features.Contains("Degree=", StringComparison.Ordinal))
        {
            return false;
        }
        if (item.Provenance is not ("agid" or "moby_reverse") && context.Moby.ContainsKey(low))
        {
            return true;
        }

        if (item.Provenance != "agid" && context.Attested.TryGetValue(low, out var lemmas) && AttestsPos(lemmas, LemmaOf(context, item).ToLowerInvariant(), PosOf(context, item)))
        {
            return true;
        }

        // The form of the inflection database is corroborated by the dictionary listing the spelling itself
        return item.Provenance == "agid" && context.Moby.ContainsKey(low);
    }

    // The inflection database files the adverbs that inflect (soon, sooner) with the adjectives
    // A form attests the lemma for a part of speech only under that part of speech of the database ("a" also marks the unlikely entries "a?")
    private static bool AttestsPos(HashSet<string> attested, string lemma, string pos)
    {
        var agid = pos == "r" ? "a" : pos;
        return attested.Contains(lemma + "/" + agid) || attested.Contains(lemma + "/" + agid + "?");
    }

    private static void Prune(Context context)
    {
        foreach (var spelling in context.Items.Keys.ToList())
        {
            var list = context.Items[spelling];
            var dropped = list.RemoveAll(i => !Corroborated(context, i));
            if (dropped > 0)
            {
                context.Stats["dropped_uncorroborated"] = context.Stats.GetValueOrDefault("dropped_uncorroborated") + dropped;
            }

            if (list.Count == 0)
            {
                context.Items.Remove(spelling);
            }
        }
    }

    private static string PosOf(Context context, Item item) => item.EntryId is { } id ? context.Entries[id][2] : item.PosLetter!;

    private static string LemmaOf(Context context, Item item) => item.EntryId is { } id ? context.Entries[id][1] : item.Lemma!;

    // The listed irregular analyses lead, the auxiliary analysis trails, the rest is in between
    private static int ListRank(Context context, Item item)
    {
        if (PosOf(context, item) == "x")
        {
            return 2;
        }

        // A listed irregular form that is spelled like its own lemma (wet, cut, put, read) does not lead: the base analyses of the
        // spelling come first
        if (item.Provenance is "hand" or "irregular_list" && !string.Equals(item.Spelling, LemmaOf(context, item), StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 1;
    }

    // The features of the gerund noun that an old plural analysis of an -ing form stands for, or null when it is another row
    private static string? GerundNoun(Context context, Old f)
    {
        if (f.Features != "Number=Plur" || f.EntryId.Length == 0 || !f.Spelling.EndsWith("ing", StringComparison.Ordinal)
            || !context.Entries.TryGetValue(f.EntryId, out var e) || e[2] != "n" || e[4] != "1" || !WordShape().IsMatch(e[1]))
        {
            return null;
        }

        var verb = InflectionRules.VerbRegular(e[1], w => context.Moby.ContainsKey(w));
        return verb.Gerund.Concat(verb.GerundAlternative).Contains(f.Spelling, StringComparer.Ordinal) && context.Moby.TryGetValue(e[1], out var letters) && letters.Contains("v") ? "Number=Sing+VerbForm=Ger" : null;
    }

    // The features that the hand and closed irregular lists give a spelling as a form of a verb, or null when they do not list it
    // as a past or a participle; the old table left some reviewed analyses of an irregular form without any features (bent, broke)
    private static string? ListedPastFeatures(IrregularSources sources, string lemma, string spelling)
    {
        var past = false;
        var part = false;
        foreach (var list in new[] { sources.Hand, sources.Closed })
        {
            if (list.TryGetValue((lemma, "v"), out var kinds))
            {
                past |= kinds.Past.Any(x => x.Form == spelling);
                part |= kinds.Participle.Any(x => x.Form == spelling);
            }
        }

        return (past, part) switch
        {
            (true, true) => "Tense=Past+VerbForm=Part",
            (true, false) => "Tense=Past",
            (false, true) => "VerbForm=Part",
            _ => null,
        };
    }

    // A reviewed -ed past of a regular verb is both the past tense and the participle, like the generated rows: the old
    // table named only the tense, so the row takes the merged features unless the reviewed rows already carry a separate participle
    private static Old MergePastParticiple(Context context, IrregularSources sources, List<Old> reviewed, Old f)
    {
        if (f.Features.Length == 0 && context.Entries.TryGetValue(f.EntryId, out var owner) && owner[2] == "v")
        {
            return f with { Features = ListedPastFeatures(sources, owner[1].ToLowerInvariant(), f.Spelling.ToLowerInvariant()) ?? f.Features };
        }

        if (f.Features == "Tense=Past" && context.Entries.TryGetValue(f.EntryId, out var listed) && listed[2] == "v"
            && ListedPastFeatures(sources, listed[1].ToLowerInvariant(), f.Spelling.ToLowerInvariant()) == "Tense=Past+VerbForm=Part"
            && !reviewed.Any(r => r.EntryId == f.EntryId && r.Spelling == f.Spelling && r.Features.Contains("VerbForm=Part", StringComparison.Ordinal)))
        {
            return f with { Features = "Tense=Past+VerbForm=Part" };
        }

        if (f.Features != "Tense=Past" || !context.Entries.TryGetValue(f.EntryId, out var e) || e[2] != "v" || !WordShape().IsMatch(e[1].ToLowerInvariant()))
        {
            return f;
        }

        var verb = InflectionRules.VerbRegular(e[1].ToLowerInvariant(), w => context.Moby.ContainsKey(w));
        var spelling = f.Spelling.ToLowerInvariant();
        if (!verb.Past.Concat(verb.PastAlternative).Contains(spelling, StringComparer.Ordinal)
            || reviewed.Any(r => r.EntryId == f.EntryId && r.Spelling == f.Spelling && r.Features.Contains("VerbForm=Part", StringComparison.Ordinal)))
        {
            return f;
        }

        return f with { Features = "Tense=Past+VerbForm=Part" };
    }

    // Tells whether an old inflected analysis is one that a rule, a written form or a list derives for its entry
    private static bool Derivable(Context context, IrregularSources sources, Dictionary<string, List<string>> written, Old f)
    {
        var features = f.Features.Split('+');
        var featureless = f.Features.Length == 0 && f.EntryId.Length > 0 && context.Entries.TryGetValue(f.EntryId, out var owner) && !string.Equals(owner[1], f.Spelling, StringComparison.OrdinalIgnoreCase)
            && owner[4] == "1" && !(written.TryGetValue(owner[0], out var ownerForms) && ownerForms.Contains(f.Spelling, StringComparer.OrdinalIgnoreCase))
            && !context.Labelled.Contains((f.Spelling.ToLowerInvariant(), owner[1].ToLowerInvariant()));
        if (featureless)
        {
            return false;
        }

        if (f.EntryId.Length == 0 || !features.Any(x => InflectedFeatures.Contains(x, StringComparer.Ordinal)) || features.Any(x => x.StartsWith("Capitalization=", StringComparison.Ordinal))
            || !context.Entries.TryGetValue(f.EntryId, out var e) || e[4] != "1" || !WordShape().IsMatch(e[1].ToLowerInvariant()))
        {
            return true;
        }

        var low = e[1].ToLowerInvariant();
        var pos = e[2];
        var spelling = f.Spelling.ToLowerInvariant();
        if (written.TryGetValue(e[0], out var forms) && forms.Any(x => string.Equals(x, f.Spelling, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (context.Attested.TryGetValue(spelling, out var lemmas) && lemmas.Contains(low + "/" + pos))
        {
            return true;
        }

        var lookup = (low, pos);
        string prefix = string.Empty;
        if (pos == "v" && !sources.Closed.ContainsKey(lookup) && Prefixed(low, sources, PrefixAttested(context, low)) is { } p)
        {
            prefix = p.Prefix;
            lookup = (p.Base, pos);
        }

        foreach (var list in new[] { sources.Hand, sources.Closed })
        {
            if (list.TryGetValue(lookup, out var kinds) && kinds.All().Any(x => string.Equals(prefix + x, spelling, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        var head = low;
        var regularVerb = pos == "v" ? InflectionRules.VerbRegular(head, w => context.Moby.ContainsKey(w)) : null;
        // Only the primary spelling of a rule stands alone; an alternative spelling or a degree form needs the dictionary
        IEnumerable<string> expected = pos switch
        {
            "n" => SanePlural(head) ? [InflectionRules.NounPlural(head)[0]] : [],
            "v" => [InflectionRules.VerbThirdPerson(head), regularVerb!.Past[0], regularVerb.Gerund[0]],
            _ => [],
        };
        return expected.Contains(spelling, StringComparer.Ordinal) || (context.Moby.ContainsKey(spelling) && Alternatives(head, pos).Contains(spelling, StringComparer.Ordinal));
    }

    // Every spelling the rules offer for a word: the primary forms, the alternative spellings and the degree forms
    private static IEnumerable<string> Alternatives(string head, string pos)
    {
        switch (pos)
        {
            case "n":
                return InflectionRules.NounPlural(head);
            case "v":
                var verb = InflectionRules.VerbRegular(head, _ => true);
                return [.. verb.Past, .. verb.PastAlternative, .. verb.Gerund, .. verb.GerundAlternative];
            case "a" or "r":
                return InflectionRules.AdjectiveDegrees(head) is { } d ? [d.Comparative, d.Superlative] : [];
            default:
                return [];
        }
    }

    private static List<Old> ReadOld(string data, DataSchema schema, bool overwrite)
    {
        var table = schema.Table("form");
        var rows = DataTables.Read(data, table).ToList();
        var hasProvenance = rows.Count > 0 && rows[0].Length >= 9;
        if (hasProvenance && !overwrite)
        {
            throw new InvalidDataException("The form table already carries the provenance column; the committed data is authoritative. Use --overwrite true to regenerate it.");
        }

        return rows.Select(r => new Old(r[0], int.Parse(r[1], CultureInfo.InvariantCulture), r[2], r[3], r[4], r[5], r.Length >= 9 ? r[8] : string.Empty)).ToList();
    }

    private static string Cap(string lemma)
    {
        if (lemma == lemma.ToLowerInvariant())
        {
            return string.Empty;
        }

        var tokens = lemma.Split(' ');
        if (tokens.Where(t => t.Length > 0 && char.IsLetter(t[0])).All(t => char.IsUpper(t[0]) && t[1..] == t[1..].ToLowerInvariant()))
        {
            return "Capitalization=Title";
        }

        return lemma == lemma.ToUpperInvariant() ? "Capitalization=Upper" : "Capitalization=Exact";
    }

    private static string Join(string cap, string features) => string.Join('+', new[] { cap, features }.Where(x => x.Length > 0).SelectMany(x => x.Split('+')).OrderBy(x => x, StringComparer.Ordinal));

    [GeneratedRegex("^[a-z][a-z'-]*$")]
    private static partial Regex WordShape();

    private static (string[] Tokens, int Head) Tokens(string[] entry)
    {
        var tokens = entry[1].Split(' ');
        var head = entry[4] != "1" && int.TryParse(entry[5], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ? h - 1 : 0;
        return (tokens, Math.Min(Math.Max(head, 0), tokens.Length - 1));
    }

    private static string Recase(string token, string form)
    {
        if (token.Length > 1 && token == token.ToUpperInvariant() && token.Any(char.IsLetter))
        {
            return form.ToUpperInvariant();
        }

        return token.Length > 0 && char.IsUpper(token[0]) ? char.ToUpperInvariant(form[0]) + form[1..] : form;
    }

    private static string[] Inflect(string[] entry, IEnumerable<string> forms)
    {
        var (tokens, head) = Tokens(entry);
        return forms.Select(f => string.Join(' ', tokens.Take(head).Append(Recase(tokens[head], f)).Concat(tokens.Skip(head + 1)))).Distinct(StringComparer.Ordinal).ToArray();
    }

    // Tells whether a prefixed spelling is listed for the verb: the inflection database decides when it covers the verb,
    // the dictionary otherwise (belay and delit are listed words, but not forms of belie and delight)
    private static Func<string, bool> PrefixAttested(Context context, string lemma)
    {
        var covered = context.Attested.TryGetValue(lemma + "s", out var thirds) && thirds.Contains(lemma + "/v");
        return w => covered ? context.Attested.TryGetValue(w, out var lemmas) && lemmas.Contains(lemma) : context.Moby.ContainsKey(w);
    }

    private static (string Prefix, string Base)? Prefixed(string low, IrregularSources sources, Func<string, bool> attested)
    {
        foreach (var prefix in InflectionRules.VerbPrefixes)
        {
            if (low.StartsWith(prefix, StringComparison.Ordinal) && low.Length > prefix.Length + 1)
            {
                var rest = low[prefix.Length..];
                // The prefixed verb inherits the irregular forms only when a dictionary or the inflection database lists one of
                // them for it (undid, redone); behave, delay and demean merely start like be, de and have-words
                if (!InflectionRules.NotPrefixable.Contains(rest) && (rest.Length >= 3 || rest is "go" or "do") && sources.Closed.TryGetValue((rest, "v"), out var closed)
                    && closed.All().Any(f => attested(prefix + f)))
                {
                    return (prefix, rest);
                }
            }
        }

        return null;
    }

    private static bool ParticipleShape(string form) =>
        form.EndsWith("en", StringComparison.Ordinal) || form.EndsWith("wn", StringComparison.Ordinal) || form.EndsWith("rn", StringComparison.Ordinal);

    private static List<(string Form, string Level)> Listed(IrregularKinds? kinds, string kind) => kinds?.Of(kind) is { Count: > 0 } list ? list : [];

    // The kinds of an inflected spelling of an entry by its shape, for the untyped written forms and the variants
    private static string[] ShapeKinds(string pos, string baseToken, string formToken)
    {
        switch (pos)
        {
            case "n":
                return ["Plur"];
            case "v":
                if (formToken.EndsWith("ing", StringComparison.Ordinal))
                {
                    return ["Ger"];
                }

                if (formToken == InflectionRules.VerbThirdPerson(baseToken) || formToken.EndsWith('s'))
                {
                    // Every spelling in s is a third person (chevies, gasses, programmes, quizzes), never a past
                    return ["Third"];
                }

                // A spelling of the base itself (co-ordinate for coordinate, abye for aby) is a variant of the infinitive
                if (formToken.Replace("-", string.Empty, StringComparison.Ordinal) == baseToken.Replace("-", string.Empty, StringComparison.Ordinal) || formToken == baseToken + "e")
                {
                    return [];
                }

                // A form ending in en, wn or rn (proven, shown, sworn) is a participle only: the regular past stays valid
                return ParticipleShape(formToken) ? ["Part"] : ["Past", "Part"];
            default:
                if (formToken.EndsWith("est", StringComparison.Ordinal))
                {
                    return ["Sup"];
                }

                return formToken.EndsWith("er", StringComparison.Ordinal) ? ["Cmp"] : [];
        }
    }

    private static void Generate(Context context, string[] entry, IrregularSources sources, Dictionary<string, List<string>> written, Dictionary<string, string[]> moby)
    {
        var lemma = entry[1];
        var pos = entry[2];
        var low = lemma.ToLowerInvariant();
        var cap = Cap(lemma);
        var single = entry[4] == "1";
        var id = entry[0];
        var baseProvenance = entry[6] == "oewn" ? "oewn_form" : "hand";
        var baseReviewed = baseProvenance == "hand" ? "1" : "0";
        var baseFeatures = pos switch { "n" => "Number=Sing", "v" => "VerbForm=Inf", "a" or "r" => "Degree=Pos", _ => string.Empty };
        context.Add(new Item(lemma, id, null, null, Join(cap, baseFeatures), baseProvenance, baseReviewed));

        var (tokens, head) = Tokens(entry);
        var headLow = tokens[head].ToLowerInvariant();
        var inflectable = WordShape().IsMatch(headLow);
        var listsApply = single && inflectable && (cap.Length == 0 || pos is "n" or "v");
        string? prefix = null;
        var attestedFn = PrefixAttested(context, low);
        (string Lemma, string Pos) lookup = (low, pos);
        if (listsApply && pos == "v" && !sources.Closed.ContainsKey(lookup) && Prefixed(low, sources, attestedFn) is { } p)
        {
            prefix = p.Prefix;
            lookup = (p.Base, pos);
        }

        sources.Hand.TryGetValue(lookup, out var hand);
        sources.Closed.TryGetValue(lookup, out var closed);
        sources.Agid.TryGetValue((low, pos), out var agid);

        // Written forms of the Open English WordNet, classified by their shape
        var oewn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (listsApply)
        {
            foreach (var form in new[] { hand, closed }.Where(k => k is not null).SelectMany(k => k!.All()))
            {
                known.Add(prefix is null ? form : prefix + form);
            }
        }

        if (written.TryGetValue(id, out var forms))
        {
            foreach (var form in forms)
            {
                var formTokens = form.Split(' ');
                if (formTokens.Length != tokens.Length || string.Equals(form, lemma, StringComparison.Ordinal) || known.Contains(form))
                {
                    context.Count("oewn_form_skipped");
                    continue;
                }

                var diff = Enumerable.Range(0, tokens.Length).Where(i => formTokens[i] != tokens[i]).ToArray();
                if (diff.Length != 1)
                {
                    context.Count("oewn_form_skipped");
                    continue;
                }

                var kinds = ShapeKinds(pos, tokens[diff[0]].ToLowerInvariant(), formTokens[diff[0]].ToLowerInvariant());
                if (kinds.Length == 0 || string.Equals(formTokens[diff[0]], tokens[diff[0]], StringComparison.OrdinalIgnoreCase))
                {
                    context.Count("oewn_form_skipped");
                    continue;
                }

                foreach (var kind in kinds)
                {
                    if (!oewn.TryGetValue(kind, out var list))
                    {
                        oewn[kind] = list = [];
                    }

                    list.Add(form);
                }
            }
        }

        // Hand variants join the written forms with the highest precedence
        var handVariants = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (listsApply)
        {
            foreach (var (form, _) in Listed(hand, "Variant"))
            {
                var variant = prefix is null ? form : prefix + form;
                if (prefix is not null && !attestedFn(variant))
                {
                    continue;
                }
                foreach (var kind in ShapeKinds(pos, low, variant))
                {
                    if (!handVariants.TryGetValue(kind, out var list))
                    {
                        handVariants[kind] = list = [];
                    }

                    list.Add(variant);
                }
            }
        }

        bool Moby(string word) => moby.ContainsKey(word.ToLowerInvariant());

        // A plain regular spelling is attested by the rule and the lemma together; the listed irregular word, the degree
        // forms and the unusual endings (Greek -sis, plural lemmas, -us, -is) need a second source
        bool Plain(string kind) => kind != "Cmp" && kind != "Sup" && !(kind == "Plur" && !SanePlural(headLow));
        IEnumerable<(string Spelling, string Provenance, bool Auto)> Kind(string kind, string rule, Func<IEnumerable<string>> regular, Func<string, bool> regularIsPrimary)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var top = new List<(string, string)>();
            var irregular = false;
            string Fix(string form) => prefix is null ? form : prefix + form;
            if (listsApply)
            {
                foreach (var (form, _) in Listed(hand, rule).Where(f => prefix is null || attestedFn(Fix(f.Form))))
                {
                    top.Add((Inflect(entry, [Fix(form)])[0], "hand"));
                }

                // The hand and closed lists name the irregular forms of the word; the regular spelling is then not valid
                // unless one of the lists names it. The hand variants and the closed list complete each other (spilt and spilled).
                var closedForms = Listed(closed, rule).Where(f => prefix is null || attestedFn(Fix(f.Form))).Select(f => (Inflect(entry, [Fix(f.Form)])[0], "irregular_list")).ToList();
                irregular = top.Count > 0 || closedForms.Count > 0;
                if (handVariants.TryGetValue(kind, out var hv))
                {
                    top.AddRange(hv.Select(f => (f, "hand")));
                }

                top.AddRange(closedForms);
            }

            if (top.Count == 0 && oewn.TryGetValue(kind, out var ow))
            {
                top.AddRange(ow.Select(f => (f, "oewn_form")));
            }

            var fromAgid = false;
            if (top.Count == 0 && listsApply)
            {
                var agidForms = Listed(agid, rule).Select(f => (Inflect(entry, [f.Form])[0], f.Level == "0" ? "agid" : "rule_variant")).ToList();
                fromAgid = agidForms.Count > 0;
                top.AddRange(agidForms);
            }

            foreach (var (spelling, provenance) in top)
            {
                if (seen.Add(spelling))
                {
                    // A primary form of the inflection database (abaci, aardwolves) is corroborated by the lemma it belongs to
                    // when it has the regular shape of a plural (monarchs); the unusual ones need a second source
                    var plainShape = kind == "Plur" ? Plain(kind) && new[] { headLow + "s", headLow + "es", headLow[..^1] + "ies" }.Any(c => string.Equals(Inflect(entry, [c])[0], spelling, StringComparison.Ordinal)) : Plain(kind) && inflectable && regular().Contains(spelling);
                    yield return (spelling, provenance, provenance == "agid" && fromAgid && plainShape);
                }
            }

            var regulars = inflectable ? regular().ToArray() : [];
            foreach (var spelling in regulars)
            {
                if (!seen.Add(spelling))
                {
                    continue;
                }

                if (top.Count == 0 && regularIsPrimary(spelling))
                {
                    yield return (spelling, "rule", Plain(kind));
                }
                else if (!irregular)
                {
                    // The written forms of the lexicon or the variants of the inflection database add to the regular spelling
                    // (brethren and brothers, tost and tossed, squirrel and squirrels): it stays a variant. Only a word with listed
                    // irregular forms (child, man, go) drops the regular spelling, unless the dictionary lists it.
                    yield return (spelling, "rule_variant", false);
                }
                else if (Moby(spelling) && context.Attested.TryGetValue(spelling.ToLowerInvariant(), out var attestedLemmas) && attestedLemmas.Contains(low))
                {
                    // The word has listed irregular forms and the inflection database names the regular spelling too (hung and
                    // hanged); a dictionary word alone does not make bed a form of be
                    yield return (spelling, "rule_variant", false);
                }
            }
        }

        var emitted = new List<(string Spelling, string Kind, string Provenance, bool Auto)>();
        switch (pos)
        {
            case "n":
                foreach (var (s, pv, auto) in Kind("Plur", "Plural", () => Inflect(entry, InflectionRules.NounPlural(headLow)), s => s == Inflect(entry, [InflectionRules.NounPlural(headLow)[0]])[0]))
                {
                    emitted.Add((s, "Plur", pv, auto));
                }

                break;
            case "v":
                {
                    var regular = InflectionRules.VerbRegular(headLow, w => moby.ContainsKey(w));
                    var skipRegular = low == "be";

                    // The head of a phrasal verb that is an irregular verb (go ahead, do justice, be quiet) has no regular past:
                    // goed ahead, doed justice and bed quiet are not forms of the phrase, and the lists of the head are not applied
                    // to phrases
                    var irregularHead = !single && sources.Closed.TryGetValue((headLow, "v"), out var headClosed) && (headClosed.Past.Count > 0 || headClosed.Participle.Count > 0);
                    foreach (var (s, pv, auto) in Kind("Third", "Third", () => skipRegular ? [] : Inflect(entry, [InflectionRules.VerbThirdPerson(headLow)]), _ => true))
                    {
                        emitted.Add((s, "Third", pv, auto));
                    }

                    foreach (var (s, pv, auto) in Kind("Ger", "Gerund", () => skipRegular ? [] : Inflect(entry, regular.Gerund.Concat(regular.GerundAlternative)), s => s == Inflect(entry, [regular.Gerund[0]])[0]))
                    {
                        emitted.Add((s, "Ger", pv, auto));
                    }

                    foreach (var (kind, rule) in new[] { ("Past", "Past"), ("Part", "Participle") })
                    {
                        foreach (var (s, pv, auto) in Kind(kind, rule, () => irregularHead ? [] : Inflect(entry, regular.Past.Concat(regular.PastAlternative)), s => s == Inflect(entry, [regular.Past[0]])[0]))
                        {
                            emitted.Add((s, kind, pv, auto && !EndsInIrregularVerb(headLow, sources)));
                        }
                    }

                    break;
                }

            default:
                {
                    var degrees = single && cap.Length == 0 && inflectable && (pos == "a" || (InflectionRules.GradableAdverbs.Contains(low) && !InflectionRules.IrregularAdverbs.Contains(low)))
                        ? InflectionRules.AdjectiveDegrees(low)
                        : null;
                    foreach (var (kind, rule, form) in new[] { ("Cmp", "Comparative", degrees?.Comparative), ("Sup", "Superlative", degrees?.Superlative) })
                    {
                        foreach (var (s, pv, auto) in Kind(kind, rule, () => form is null ? [] : [form], _ => true))
                        {
                            emitted.Add((s, kind, pv, auto));
                        }
                    }

                    break;
                }
        }

        // A modal has no third person of its own (will, can): the written lists name the base spelling again
        emitted.RemoveAll(e => e.Kind == "Third" && string.Equals(e.Spelling, lemma, StringComparison.Ordinal));
        EmitRows(context, id, cap, emitted);
    }

    private static void EmitRows(Context context, string entryId, string cap, List<(string Spelling, string Kind, string Provenance, bool Auto)> emitted)
    {
        foreach (var group in emitted.GroupBy(e => e.Spelling, StringComparer.Ordinal))
        {
            var kinds = group.Select(g => g.Kind).ToHashSet(StringComparer.Ordinal);
            var rows = new List<(string Features, string[] Kinds)>();
            if (kinds.Contains("Past") && kinds.Contains("Part"))
            {
                rows.Add(("Tense=Past+VerbForm=Part", ["Past", "Part"]));
            }
            else if (kinds.Contains("Past"))
            {
                rows.Add(("Tense=Past", ["Past"]));
            }
            else if (kinds.Contains("Part"))
            {
                rows.Add(("VerbForm=Part", ["Part"]));
            }

            foreach (var (kind, features) in new[] { ("Ger", "VerbForm=Ger"), ("Third", "Person=3+Tense=Pres"), ("Plur", "Number=Plur"), ("Cmp", "Degree=Cmp"), ("Sup", "Degree=Sup") })
            {
                if (kinds.Contains(kind))
                {
                    rows.Add((features, [kind]));
                }
            }

            foreach (var (features, rowKinds) in rows)
            {
                var provenance = group.Where(g => rowKinds.Contains(g.Kind)).Select(g => g.Provenance).OrderBy(p => Array.IndexOf(ProvenanceOrder, p)).First();
                var auto = group.Where(g => rowKinds.Contains(g.Kind)).All(g => g.Auto);
                context.Add(new Item(group.Key, entryId, null, null, Join(cap, features), provenance, "0", false, auto));
            }
        }
    }

    private static string EntryFor(Context context, string lemma, string pos, HashSet<string> keep)
    {
        var existing = context.Entries.Values.Where(e => e[1] == lemma && e[2] == pos).OrderBy(e => e[0], StringComparer.Ordinal).FirstOrDefault();
        if (existing is not null)
        {
            keep.Add(existing[0]);
            return existing[0];
        }

        var id = $"dema-{lemma}-{pos}";
        context.Entries[id] = [id, lemma, pos, "0", "1", "1", "dema"];
        keep.Add(id);
        context.Count("entries_minted");
        return id;
    }

    private static void AddAuxiliaries(Context context, IrregularSources sources, HashSet<string> keep)
    {
        foreach (var (lemma, forms) in sources.Auxiliaries.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            var id = EntryFor(context, lemma, "x", keep);
            context.Add(new Item(lemma, id, null, null, string.Empty, "irregular_list", "0"));
            foreach (var (spelling, features) in forms)
            {
                context.Add(new Item(spelling, id, null, null, features, "irregular_list", "0"));
            }
        }

        foreach (var lemma in sources.Modals.OrderBy(m => m, StringComparer.Ordinal))
        {
            context.Add(new Item(lemma, EntryFor(context, lemma, "x", keep), null, null, string.Empty, "irregular_list", "0"));
        }
    }

    private static void AddContractions(Context context, IrregularSources sources, HashSet<string> keep)
    {
        foreach (var c in sources.Contractions)
        {
            string? chosen = null;
            foreach (var letter in c.Letters)
            {
                var candidates = context.Entries.Values
                    .Where(e => keep.Contains(e[0]) && e[2] == letter.ToString() && string.Equals(e[1], c.Head, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e[0], StringComparer.Ordinal).ToList();
                if (candidates.Count > 0)
                {
                    chosen = candidates.FirstOrDefault(e => context.Sensed.Contains(e[0]) || ClosedPos.Contains(e[2], StringComparison.Ordinal))?[0] ?? candidates[0][0];
                    break;
                }
            }

            if (chosen is not null)
            {
                context.Add(new Item(c.Spelling, chosen, null, null, "Contraction=Yes", "contraction", "0"));

                // A clitic contraction is two words: the host (above) and the auxiliary the clitic stands for
                var auxiliary = c.Spelling.ToLowerInvariant() switch
                {
                    var s when s.EndsWith("'ll", StringComparison.Ordinal) => "will",
                    var s when s.EndsWith("'re", StringComparison.Ordinal) || s.EndsWith("'m", StringComparison.Ordinal) || s.EndsWith("'s", StringComparison.Ordinal) => "be",
                    var s when s.EndsWith("'ve", StringComparison.Ordinal) => "have",
                    var s when s.EndsWith("'d", StringComparison.Ordinal) => "would",
                    _ => null,
                };
                if (auxiliary is not null && c.Letters != "x" && !string.Equals(c.Spelling, "let's", StringComparison.OrdinalIgnoreCase))
                {
                    context.Add(new Item(c.Spelling, EntryFor(context, auxiliary, "x", keep), null, null, "Contraction=Yes", "contraction", "0"));
                }
            }
            else
            {
                context.Count("contraction_head_missing");
                context.Add(new Item(c.Spelling, null, c.Head, c.Letters[..1], "Contraction=Yes", "hand", "0"));
            }
        }
    }

    // The words and parts of speech the hand-written additions give: their inflections are generated like those of any dictionary word
    private static HashSet<(string Word, string Pos)> ReadAdded(string path)
    {
        var added = new HashSet<(string, string)>();
        if (!File.Exists(path))
        {
            return added;
        }

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var columns = line.Split('\t');
            if (columns.Length >= 2)
            {
                foreach (var letter in columns[1])
                {
                    if (LexiconLetters.IndexOf(letter, StringComparison.Ordinal) is var at and >= 0)
                    {
                        added.Add((columns[0], LexiconPos[at].ToString()));
                    }
                }
            }
        }

        return added;
    }

    // The dictionary labels derived words (a noun in -ness, a verb in -ed or -ing) as bases of their own; the unlisted
    // inflections of such words are noise (officialnesses, undisinfecteding), those of plain bases are real words
    private static bool PlainBase(string word, string pos) =>
        word.Length >= 4 && (pos == "n"
            ? !word.EndsWith("ness", StringComparison.Ordinal) && !word.EndsWith("like", StringComparison.Ordinal) && !word.EndsWith("ics", StringComparison.Ordinal)
            : !word.EndsWith("ed", StringComparison.Ordinal) && !word.EndsWith("ing", StringComparison.Ordinal) && !word.EndsWith("ly", StringComparison.Ordinal));

    // A verb that ends in an irregular verb (misthrow, outblow, betake) has irregular past forms of its own, not -ed
    private static bool EndsInIrregularVerb(string word, IrregularSources sources) =>
        Enumerable.Range(0, Math.Max(word.Length - 2, 0)).Any(i => sources.Closed.ContainsKey((word[i..], "v")));

    // A noun whose plural by rule is plausible: no Greek -sis, and no lemma that is already a plural in s
    private static bool SanePlural(string word) =>
        !word.EndsWith("sis", StringComparison.Ordinal) && (!word.EndsWith('s') || word.EndsWith("ss", StringComparison.Ordinal));

    // Tells whether the word is the plain regular inflection of the lemma: the rule gives exactly this spelling, the lemma is a
    // real word and the inflected verb is not one of an irregular verb's compounds
    private static bool ForwardRegular(string lemma, string pos, string features, string word, Dictionary<string, string[]> moby, IrregularSources sources, bool realLemma)
    {
        if (!realLemma && !(moby.TryGetValue(lemma, out var letters) && letters[0] == pos && PlainBase(lemma, pos)))
        {
            return false;
        }

        switch (features)
        {
            case "Number=Plur":
                return pos == "n" && SanePlural(lemma) && InflectionRules.NounPlural(lemma)[0] == word;
            case "Person=3+Tense=Pres":
                return pos == "v" && InflectionRules.VerbThirdPerson(lemma) == word;
            case "Tense=Past+VerbForm=Part":
                return pos == "v" && !EndsInIrregularVerb(lemma, sources) && InflectionRules.VerbRegular(lemma, w => moby.ContainsKey(w)).Past[0] == word;
            case "VerbForm=Ger":
                return pos == "v" && InflectionRules.VerbRegular(lemma, w => moby.ContainsKey(w)).Gerund[0] == word;
            default:
                return false;
        }
    }

    private static void ClassifyMoby(Context context, Dictionary<string, string[]> moby, Dictionary<(string, string), List<string[]>> byLemma, HashSet<string> overridden, HashSet<(string Word, string Pos)> added, IrregularSources sources)
    {
        var covered = new HashSet<(string, string)>();
        foreach (var (spelling, items) in context.Items)
        {
            foreach (var item in items)
            {
                // A capitalized row (Abo, Cf) is a name or an abbreviation and does not cover the lowercase word;
                // neither does a tiny-lemma rule analysis (as of a), which the ordering drops
                if (item.Features.Contains("Capitalization=", StringComparison.Ordinal)
                    || (item.Provenance is "rule" or "rule_variant" or "moby_reverse" && LemmaOf(context, item).Length <= 2 && !string.Equals(LemmaOf(context, item), spelling, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                covered.Add((spelling.ToLowerInvariant(), PosOf(context, item)));
            }
        }

        // Spellings of the frozen rows count as covered for every part of speech they carry
        var surfaceBases = new List<(string Word, string Pos)>();
        foreach (var (word, letters) in moby.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (overridden.Contains(word))
            {
                continue;
            }


            foreach (var pos in letters)
            {
                // A reviewed spelling keeps its rows; a dictionary part of speech they lack is appended as a plain surface base
                if (context.FrozenSpellings.Contains(word))
                {
                    if (!(context.FrozenPos.TryGetValue(word, out var have) && have.Contains(pos)))
                    {
                        var frozenFeatures = pos switch { "n" => "Number=Sing", "v" => "VerbForm=Inf", "a" or "r" => "Degree=Pos", _ => string.Empty };
                        if (!context.FrozenExtras.TryGetValue(word, out var extras))
                        {
                            context.FrozenExtras[word] = extras = [];
                        }

                        extras.Add(byLemma.TryGetValue((word, pos), out var heads) && heads.Count > 0
                            ? new Item(word, heads[0][0], null, null, frozenFeatures, "moby_reverse", "0")
                            : new Item(word, null, word, pos, frozenFeatures, "moby_reverse", "0"));
                        context.Count("frozen_missing_pos_surface");
                    }

                    continue;
                }

                if (covered.Contains((word, pos)))
                {
                    context.Count("moby_covered");
                    continue;
                }

                if (!ClosedPos.Contains(pos, StringComparison.Ordinal))
                {
                    var entryHits = new HashSet<(string, string)>();
                    var pending = new List<Item>();
                    var surfaceBase = ((string, string)?)null;
                    foreach (var (candidate, features) in InflectionRules.ReverseCandidates(word, pos))
                    {
                        // A tiny lemma of a word that is also closed-class (as of a) is dropped by the ordering; it is no analysis
                        if (candidate.Length <= 2 && candidate != word && letters.Any(l => ClosedPos.Contains(l, StringComparison.Ordinal)))
                        {
                            continue;
                        }
                        if (byLemma.TryGetValue((candidate, pos), out var hits))
                        {
                            foreach (var hit in hits.Where(h => entryHits.Add((h[0], features))))
                            {
                                pending.Add(new Item(word, hit[0], null, null, features, "moby_reverse", "0", false, ForwardRegular(candidate, pos, features, word, moby, sources, true)));
                            }
                        }
                        else if (surfaceBase is null && candidate != word && moby.TryGetValue(candidate, out var baseLetters) && baseLetters.Contains(pos))
                        {
                            surfaceBase = (candidate, features);
                        }
                    }

                    // A reverse-rule analysis ships only when it is corroborated; a listed word without one stays a plain dictionary word
                    var corroborated = pending.Where(p => Corroborated(context, p)).ToList();
                    if (corroborated.Count > 0)
                    {
                        foreach (var item in corroborated)
                        {
                            context.Add(item);
                        }

                        context.Count("moby_reverse_entry");
                        continue;
                    }

                    if (pending.Count > 0)
                    {
                        context.Count("moby_reverse_entry_uncorroborated");
                    }

                    if (surfaceBase is { } sb)
                    {
                        var item = new Item(word, null, sb.Item1, pos, sb.Item2, "moby_reverse", "0", false, ForwardRegular(sb.Item1, pos, sb.Item2, word, moby, sources, false));
                        if (Corroborated(context, item))
                        {
                            context.Count("moby_reverse_surface");
                            context.Add(item);
                            continue;
                        }

                        context.Count("moby_reverse_surface_uncorroborated");
                    }

                    surfaceBases.Add((word, pos));
                }

                context.Count(ClosedPos.Contains(pos, StringComparison.Ordinal) ? "moby_closed_surface" : "moby_base_surface");
                var baseFeatures = pos switch { "n" => "Number=Sing", "v" => "VerbForm=Inf", "a" or "r" => "Degree=Pos", _ => string.Empty };
                context.Add(new Item(word, null, word, pos, baseFeatures, "moby_reverse", "0"));
            }
        }

        // Regular inflections of the surface bases when the base is plainly a verb or noun by its first dictionary part of speech
        foreach (var (word, pos) in surfaceBases)
        {
            if (pos is not ("n" or "v") || (moby[word][0] != pos && !added.Contains((word, pos))) || !WordShape().IsMatch(word))
            {
                continue;
            }

            var generated = new List<(string Form, string Features)>();
            if (pos == "n")
            {
                // An added compound of man (whaleman, irishman) has the plural in men
                generated.Add((added.Contains((word, pos)) && word.Length > 5 && word.EndsWith("man", StringComparison.Ordinal) ? word[..^3] + "men" : InflectionRules.NounPlural(word)[0], "Number=Plur"));
            }
            else
            {
                var regular = InflectionRules.VerbRegular(word, w => moby.ContainsKey(w));
                generated.Add((InflectionRules.VerbThirdPerson(word), "Person=3+Tense=Pres"));
                generated.Add((regular.Past[0], "Tense=Past+VerbForm=Part"));
                generated.Add((regular.Gerund[0], "VerbForm=Ger"));
            }

            foreach (var (form, features) in generated)
            {
                if (context.FrozenSpellings.Contains(form))
                {
                    continue;
                }

                if (features.StartsWith("Tense=Past", StringComparison.Ordinal) && EndsInIrregularVerb(word, sources) && !added.Contains((word, pos)))
                {
                    context.Count("surface_inflection_irregular_past");
                    continue;
                }

                // A surface-only analysis is a plain regular inflection of a plain dictionary base (or an addition); the other
                // spellings need the dictionary or the inflection database to attest them
                var plain = (added.Contains((word, pos)) || PlainBase(word, pos)) && (pos != "n" || SanePlural(word));
                if (!moby.ContainsKey(form) && !plain && !(context.Attested.TryGetValue(form, out var attested) && attested.Contains(word)))
                {
                    context.Count("surface_inflection_unlisted");
                    continue;
                }

                context.Add(new Item(form, null, word, pos, features, "rule", "0", false, plain));
            }
        }
    }

    private static FormGeneratorResult Write(
        string data,
        DataSchema schema,
        TableDef formTable,
        Context context,
        List<Old> frozen,
        List<string[]> scopes,
        Dictionary<string, List<(string Lemma, string Pos, int Rank)>> oldRank,
        HashSet<string> keep)
    {
        var rows = new List<string[]>();
        var provenance = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var surfaceOnly = 0;
        var spellings = new HashSet<string>(StringComparer.Ordinal);
        var frozenBySpelling = frozen.GroupBy(f => f.Spelling, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Rank).ToList(), StringComparer.Ordinal);
        var memo = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        List<Item> Ordered(string spelling)
        {
            if (memo.TryGetValue(spelling, out var cached))
            {
                return cached;
            }

            var items = context.Items[spelling];

            // A capitalized spelling also carries the analyses of its lower-case spelling (a word at the start of a
            // sentence, or in capitals), first: the exact spelling of the lexicon would otherwise hide them
            var lower = spelling.ToLowerInvariant();
            var twins = new List<Item>();
            if (!string.Equals(lower, spelling, StringComparison.Ordinal))
            {
                if (context.Items.ContainsKey(lower))
                {
                    twins.AddRange(Ordered(lower).Select(t => t with { Spelling = spelling, Twin = true }));
                }
                else if (frozenBySpelling.TryGetValue(lower, out var frozenTwins))
                {
                    twins.AddRange(frozenTwins.Select(f => new Item(spelling, f.EntryId, null, null, f.Features, "carried", "0", true)));
                }
            }

            // One analysis per entry or lemma and features; the strongest provenance wins
            var closedHere = items.Any(i => ClosedPos.Contains(PosOf(context, i), StringComparison.Ordinal) || i.Provenance is "hand" or "irregular_list");
            var closedClass = items.Any(i => "upcid".Contains(PosOf(context, i), StringComparison.Ordinal));
            var unique = items
                .Where(i => !(closedHere && i.Provenance is "rule" or "rule_variant" or "moby_reverse" && LemmaOf(context, i).Length <= 2 && !string.Equals(LemmaOf(context, i), spelling, StringComparison.OrdinalIgnoreCase)))
                .Where(i => !(closedClass && i.Provenance is "rule" or "rule_variant" or "moby_reverse" && LemmaOf(context, i).Length <= 2 && !string.Equals(LemmaOf(context, i), spelling, StringComparison.OrdinalIgnoreCase)))
                .Where(i => !(spelling.Length == 1 && closedClass && !ClosedPos.Contains(PosOf(context, i), StringComparison.Ordinal) && !string.Equals(LemmaOf(context, i), spelling, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(i => (i.EntryId ?? string.Empty, i.Lemma ?? string.Empty, i.PosLetter ?? string.Empty, i.Features))
                .Select(g => g.OrderBy(i => Array.IndexOf(ProvenanceOrder, i.Provenance)).First()).ToList();
            oldRank.TryGetValue(spelling, out var ranks);

            // A word that the old table held with closed-class analyses only (the, a, alas) keeps no open-class analysis
            if (ranks is { Count: > 0 } && ranks.All(r => ClosedPos.Contains(r.Pos, StringComparison.Ordinal)) && unique.Any(i => ClosedPos.Contains(PosOf(context, i), StringComparison.Ordinal)))
            {
                unique = unique.Where(i => ClosedPos.Contains(PosOf(context, i), StringComparison.Ordinal) || i.Provenance is "hand" or "irregular_list").ToList();
            }

            int RankOf(Item i)
            {
                var lemma = i.EntryId is { } id ? context.Entries[id][1] : i.Lemma!;
                var pos = PosOf(context, i);
                var matches = ranks?.Where(r => r.Lemma == lemma && r.Pos == pos).Select(r => r.Rank).ToList();
                return matches is { Count: > 0 } ? matches.Min() : int.MaxValue;
            }

            // The listed irregular analyses (hand and closed lists) lead, then the analyses that the old table had, then the rest
            var own = unique
                .OrderBy(i => ListRank(context, i))
                .ThenBy(RankOf)
                .ThenBy(i => i.EntryId is null ? 1 : 0)
                .ThenBy(i => Pos.IndexOf(PosOf(context, i), StringComparison.Ordinal))
                .ThenBy(i => i.EntryId is { } id ? context.Entries[id][1] : i.Lemma!, StringComparer.Ordinal)
                .ThenBy(i => i.Features.Contains("VerbForm=Inf", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(i => i.EntryId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(i => i.Features, StringComparer.Ordinal)
                .ToList();
            var seen = new HashSet<(string, string, string, string)>();
            var ordered = new List<Item>();
            foreach (var i in twins.Concat(own).Where(i => seen.Add((i.EntryId ?? string.Empty, i.Lemma ?? string.Empty, i.PosLetter ?? string.Empty, i.Features))))
            {
                ordered.Add(i);
            }

            // The corrections that the audit found: an analysis that our own sources show to be wrong is dropped or retyped
            if (context.Corrections.TryGetValue(spelling, out var corrections))
            {
                ordered = ordered.Where(i => !corrections.Any(c => Matches(c, i) && c.Action == "drop"))
                    .Select(i => corrections.FirstOrDefault(c => c.Action.StartsWith("set:", StringComparison.Ordinal) && Matches(c, i)) is { Action: not null } set ? i with { Features = set.Action[4..] } : i)
                    .ToList();
            }

            bool Matches((string Lemma, string Pos, string Features, string Action) c, Item i) =>
                i.Features == c.Features && i.EntryId is null ? string.Equals(c.Lemma, i.Lemma, StringComparison.Ordinal) && c.Pos == i.PosLetter
                : i.Features == c.Features && i.EntryId is { } eid && context.Entries.TryGetValue(eid, out var row) && string.Equals(c.Lemma, row[1], StringComparison.Ordinal) && c.Pos == row[2];

            memo[spelling] = ordered;
            return ordered;
        }

        foreach (var spelling in context.Items.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var ordered = Ordered(spelling);
            for (var rank = 0; rank < ordered.Count; rank++)
            {
                var i = ordered[rank];
                if (i.EntryId is { } id)
                {
                    keep.Add(id);
                }
                else
                {
                    surfaceOnly++;
                }

                rows.Add([spelling, rank.ToString(CultureInfo.InvariantCulture), i.EntryId ?? string.Empty, i.Features, "dema", i.Reviewed, i.Lemma ?? string.Empty, i.PosLetter ?? string.Empty, i.Provenance]);
                provenance[i.Provenance] = provenance.GetValueOrDefault(i.Provenance) + 1;
                spellings.Add(spelling);
            }
        }

        foreach (var f in frozen)
        {
            rows.Add([f.Spelling, f.Rank.ToString(CultureInfo.InvariantCulture), f.EntryId, f.Features, f.Source, f.Reviewed, string.Empty, string.Empty, "carried"]);
            provenance["carried"] = provenance.GetValueOrDefault("carried") + 1;
            spellings.Add(f.Spelling);
        }

        foreach (var (spelling, extras) in context.FrozenExtras)
        {
            var next = frozen.Count(f => f.Spelling == spelling);
            foreach (var i in extras)
            {
                if (i.EntryId is { } extraId)
                {
                    keep.Add(extraId);
                    rows.Add([spelling, (next++).ToString(CultureInfo.InvariantCulture), extraId, i.Features, "dema", "0", string.Empty, string.Empty, i.Provenance]);
                    provenance[i.Provenance] = provenance.GetValueOrDefault(i.Provenance) + 1;
                    continue;
                }

                rows.Add([spelling, (next++).ToString(CultureInfo.InvariantCulture), string.Empty, i.Features, "dema", "0", i.Lemma!, i.PosLetter!, i.Provenance]);
                provenance[i.Provenance] = provenance.GetValueOrDefault(i.Provenance) + 1;
                surfaceOnly++;
            }
        }

        var frozenSet = context.FrozenSpellings;
        var scopeRows = scopes.Where(s => frozenSet.Contains(s[0])).ToList();
        var resolver = new PartitionResolver(schema.FileSplit);
        var entries = keep.Select(id => context.Entries[id]).ToList();
        foreach (var e in entries)
        {
            resolver.AddEntry(e[0], e[1]);
        }

        var before = context.Entries.Count;
        DataTables.Write(data, schema.Table("entry"), entries, resolver);
        DataTables.Write(data, formTable, rows, resolver);
        DataTables.Write(data, schema.Table("form_sense_scope"), scopeRows, resolver);
        context.Stats["entries_removed"] = before - entries.Count;
        return new FormGeneratorResult(entries.Count, spellings.Count, rows.Count, surfaceOnly, frozen.Count, scopeRows.Count, provenance, new SortedDictionary<string, int>(context.Stats, StringComparer.Ordinal));
    }
}
