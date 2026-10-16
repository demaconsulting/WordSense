using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder;

/// <summary>
///     The settings of the noun/verb import: the minimum and maximum number of rows per sense.
/// </summary>
/// <param name="MinAnchors">The anchors a sense needs; a lemma with a sense below the two minimums together is not trained.</param>
/// <param name="MinHoldout">The holdout rows a sense needs.</param>
/// <param name="MaxAnchors">The anchors taken per sense at most.</param>
/// <param name="MaxHoldout">The holdout rows taken per sense at most.</param>
internal sealed record NounVerbOptions(
    int MinAnchors = RealPlan.MinAnchors,
    int MinHoldout = RealPlan.MinHoldout,
    int MaxAnchors = 40,
    int MaxHoldout = 12);

/// <summary>
///     One accepted input row: the corpus row, its provenance (licence already normalized) and its source group.
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Row">The corpus row.</param>
/// <param name="Provenance">The provenance row; the split is filled in when the row is allocated.</param>
/// <param name="Group">The source group: contributor, document, work or page.</param>
internal sealed record NounVerbRow(string Id, CorpusRow Row, ProvenanceRow Provenance, string Group);

/// <summary>
///     Imports ready-made, judged noun/verb sentences of many lemmas (Phase 5): a corpus file with one braced target
///     per sentence and a provenance file per sentence, filtered by an exclusion list and by per-sense minimums, split
///     into anchors and holdout by source group, with generated inflected forms.
/// </summary>
/// <remarks>
///     A batch is named <c>nounverb-N</c> and writes <c>anchors/NAME.tsv</c>, <c>holdout/NAME.tsv</c>,
///     <c>provenance/NAME.tsv</c> and <c>senses/NAME.tsv</c>; the forms of the lemmas it owns replace their rows in
///     <c>forms/speech.tsv</c> (the only forms file the build reads), and <c>audit/nounverb-untrained.tsv</c> lists every
///     input lemma that was not trained, with the reason. The real-sentence import ignores <c>nounverb-*.tsv</c> files when it
///     counts existing rows. All checks run before anything is written.
/// </remarks>
internal static partial class NounVerbImport
{
    /// <summary>
    ///     The prefix of a batch name.
    /// </summary>
    public const string Prefix = "nounverb-";

    /// <summary>
    ///     The origin of sentences that the maintainer supplied.
    /// </summary>
    public const string UserOrigin = "user-supplied";

    /// <summary>
    ///     The licence recorded for sentences that the maintainer supplied.
    /// </summary>
    public const string UserLicence = "MIT (project-authored)";

    /// <summary>
    ///     The file name of the report of lemmas that were not trained, in the <c>audit</c> folder.
    /// </summary>
    public const string UntrainedFile = "nounverb-untrained.tsv";

    private const string SenseNoun = "noun";

    private const string SenseVerb = "verb";

    private const int DominantUseRows = 3;

    private static readonly Dictionary<string, string> LicenceNames = new(StringComparer.Ordinal)
    {
        ["Public domain (US)"] = "Public domain",
        ["CC0"] = "CC0 1.0",
        ["user-supplied"] = UserLicence
    };

    /// <summary>
    ///     Checks whether a file name belongs to a noun/verb batch.
    /// </summary>
    /// <param name="fileName">The file name, with extension.</param>
    /// <returns>True for <c>nounverb-*.tsv</c>.</returns>
    public static bool IsNounVerbFile(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.Ordinal) && fileName.EndsWith(".tsv", StringComparison.Ordinal);

    /// <summary>
    ///     Checks a batch name.
    /// </summary>
    /// <param name="name">The batch name, for example <c>nounverb-1</c>.</param>
    /// <exception cref="ArgumentException">Thrown when the name does not have the form <c>nounverb-N</c>.</exception>
    public static void ValidateName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length || !name[Prefix.Length..].All(char.IsAsciiDigit))
        {
            throw new ArgumentException($"The noun/verb batch name must have the form {Prefix}N but was '{name}'.");
        }
    }

    /// <summary>
    ///     Generates the regular inflected forms of a lemma.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <returns>The forms with their kind: <c>s</c> (plural or third person), <c>ed</c> or <c>ing</c>; never the lemma itself.</returns>
    /// <remarks>
    ///     Rules: <c>-s</c>/<c>-es</c> (also <c>-ies</c> after a consonant plus y, and both <c>-s</c> and <c>-es</c> after o),
    ///     <c>-ed</c>/<c>-ing</c> with e-drop, <c>ie</c> to <c>ying</c>, consonant doubling of a stressed final consonant (a
    ///     single-syllable word doubles; a longer word yields both spellings, because the stress is not known) and
    ///     <c>c</c> to <c>ck</c>. Irregular forms come from the forms file.
    /// </remarks>
    public static List<(string Form, string Kind)> Inflect(string lemma)
    {
        var forms = new List<(string, string)>();
        if (lemma.Length < 2 || !lemma.All(char.IsAsciiLetterLower))
        {
            return forms;
        }

        var stems = new List<string>();
        var endsE = lemma.EndsWith('e');
        var consonantY = lemma.EndsWith('y') && !IsVowel(lemma[^2]);
        var cvc = EndsWithConsonantVowelConsonant(lemma);
        var mono = VowelGroups(lemma) == 1;

        // Plural and third person
        if (lemma.EndsWith('s') || lemma.EndsWith('x') || lemma.EndsWith("ch", StringComparison.Ordinal) || lemma.EndsWith("sh", StringComparison.Ordinal))
        {
            forms.Add((lemma + "es", "s"));
        }
        else if (lemma.EndsWith('z'))
        {
            forms.Add((lemma + "es", "s"));
            if (cvc && mono)
            {
                forms.Add((lemma + "zes", "s"));
            }
        }
        else if (consonantY)
        {
            forms.Add((lemma[..^1] + "ies", "s"));
        }
        else if (lemma.EndsWith('o') && !IsVowel(lemma[^2]))
        {
            forms.Add((lemma + "s", "s"));
            forms.Add((lemma + "es", "s"));
        }
        else
        {
            forms.Add((lemma + "s", "s"));
        }

        // Past tense and participle, and gerund
        if (consonantY)
        {
            forms.Add((lemma[..^1] + "ied", "ed"));
            forms.Add((lemma + "ing", "ing"));
            return forms;
        }

        if (lemma.EndsWith("ie", StringComparison.Ordinal))
        {
            forms.Add((lemma + "d", "ed"));
            forms.Add((lemma[..^2] + "ying", "ing"));
            return forms;
        }

        if (endsE)
        {
            forms.Add((lemma + "d", "ed"));
            var dropsE = !lemma.EndsWith("ee", StringComparison.Ordinal) && !lemma.EndsWith("oe", StringComparison.Ordinal) && !lemma.EndsWith("ye", StringComparison.Ordinal);
            forms.Add((dropsE ? lemma[..^1] + "ing" : lemma + "ing", "ing"));
            return forms;
        }

        stems.Add(lemma);
        if (cvc && !lemma.EndsWith('c'))
        {
            stems.Add(lemma + lemma[^1]);
            if (mono)
            {
                stems.RemoveAt(0);
            }
        }

        if (lemma.EndsWith('c'))
        {
            stems.Clear();
            stems.Add(lemma + "k");
        }

        foreach (var stem in stems)
        {
            forms.Add((stem + "ed", "ed"));
            forms.Add((stem + "ing", "ing"));
        }

        return forms;
    }

    /// <summary>
    ///     Allocates the rows of one sense to anchors and holdout by source group.
    /// </summary>
    /// <param name="pool">The accepted rows of the sense; at least the sum of the minimums.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The anchors, the holdout rows and the number of anchors that share a source group with the holdout.</returns>
    /// <remarks>
    ///     The holdout comes first, from whole source groups, smallest first (a partly used group leaves its other rows
    ///     unused rather than sharing), so that large groups remain for the anchors. The anchors are taken round-robin over the
    ///     remaining groups, largest first, so that no source dominates. Only when that cannot reach the minimum are rows of
    ///     the holdout groups added to the anchors; they are counted as shared. The order inside a group is by a hash of the row
    ///     id, so the result is deterministic.
    /// </remarks>
    public static (List<NounVerbRow> Anchors, List<NounVerbRow> Holdout, int Shared) Allocate(IReadOnlyList<NounVerbRow> pool, NounVerbOptions options)
    {
        var n = pool.Count;
        var holdoutSize = Math.Min(Math.Clamp((int)Math.Round(n * 0.3), options.MinHoldout, options.MaxHoldout), n - options.MinAnchors);
        var anchorSize = Math.Min(options.MaxAnchors, n - holdoutSize);
        var groups = pool.GroupBy(r => r.Group, StringComparer.Ordinal)
            .Select(g => (Key: g.Key, Rows: g.OrderBy(r => Hash(r.Id), StringComparer.Ordinal).ToList())).ToList();

        var holdout = new List<NounVerbRow>();
        var holdoutKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.OrderBy(g => g.Rows.Count).ThenBy(g => Hash(g.Key), StringComparer.Ordinal))
        {
            if (holdout.Count >= holdoutSize)
            {
                break;
            }

            holdout.AddRange(group.Rows.Take(holdoutSize - holdout.Count));
            holdoutKeys.Add(group.Key);
        }

        var free = groups.Where(g => !holdoutKeys.Contains(g.Key)).OrderByDescending(g => g.Rows.Count).ThenBy(g => Hash(g.Key), StringComparer.Ordinal)
            .Select(g => g.Rows).ToList();
        var anchors = RoundRobin(free, anchorSize);
        var shared = 0;
        if (anchors.Count < options.MinAnchors)
        {
            var used = holdout.ToHashSet();
            var leftover = groups.Where(g => holdoutKeys.Contains(g.Key)).OrderByDescending(g => g.Rows.Count).ThenBy(g => Hash(g.Key), StringComparer.Ordinal)
                .Select(g => g.Rows.Where(r => !used.Contains(r)).ToList()).ToList();
            var fill = RoundRobin(leftover, options.MinAnchors - anchors.Count);
            anchors.AddRange(fill);
            shared = fill.Count;
        }

        return (anchors, holdout, shared);
    }

    /// <summary>
    ///     Gets the source group of a row: the unit that the anchors and the holdout should not share.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <param name="url">The URL.</param>
    /// <param name="contributor">The contributor or work.</param>
    /// <returns>The group key: a Tatoeba contributor, a UD document, a Gutenberg work, a Wiktionary page, else the source and contributor.</returns>
    public static string GroupOf(string source, string url, string contributor)
    {
        if (source.StartsWith("tatoeba", StringComparison.Ordinal))
        {
            return "tatoeba:" + contributor;
        }

        if (source.StartsWith("ud-", StringComparison.Ordinal))
        {
            var at = url.IndexOf("sent_id=", StringComparison.Ordinal);
            var sentence = at < 0 ? string.Empty : url[(at + "sent_id=".Length)..];
            var match = DocumentSuffix().Match(sentence);
            return source + ":" + (match.Success ? match.Groups[1].Value : string.Empty);
        }

        if (source == "gutenberg")
        {
            var match = EbookNumber().Match(url);
            return "pg:" + (match.Success ? match.Groups[1].Value : contributor);
        }

        if (source == "wiktionary")
        {
            return "wikt:" + url.Split('#')[0];
        }

        return source + ":" + contributor;
    }

    /// <summary>
    ///     Imports the noun/verb sentences.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="name">The batch name, for example <c>nounverb-1</c>.</param>
    /// <param name="sentenceFiles">The sentence files (<c>lemma</c>, <c>noun|verb</c>, braced sentence), without header.</param>
    /// <param name="provenanceFiles">The provenance files (header with at least <c>sentence_hash</c>, <c>lemma</c>, <c>sense</c>,
    ///     <c>source</c>, <c>licence</c>, <c>url</c>, <c>contributor_or_work</c>).</param>
    /// <param name="exclusionsFile">The exclusion list (<c>lemma</c>, <c>category</c>, optional <c>note</c>, with header).</param>
    /// <param name="formsFile">The optional explicit forms file (<c>form</c>, <c>lemma</c>, <c>labels</c>, with header) of irregular forms, or null.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The report of the import.</returns>
    /// <exception cref="InvalidDataException">Thrown when an input file is malformed or the settings are inconsistent; nothing is written then.</exception>
    public static string Import(
        string corpus,
        string name,
        IReadOnlyList<string> sentenceFiles,
        IReadOnlyList<string> provenanceFiles,
        string exclusionsFile,
        string? formsFile,
        NounVerbOptions options)
    {
        ValidateName(name);
        if (options.MinAnchors < 4 || options.MinHoldout < 1 || options.MaxAnchors < options.MinAnchors || options.MaxHoldout < options.MinHoldout)
        {
            throw new InvalidDataException("The row limits must satisfy 4 <= min anchors <= max anchors and 1 <= min holdout <= max holdout.");
        }

        var ownFile = name + ".tsv";
        var exclusions = ReadExclusions(exclusionsFile);
        var gateDropped = NounVerbGate.ReadDropped(corpus);
        var explicitForms = formsFile is null ? [] : Corpus.ReadForms(formsFile);
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"), ownFile);
        var previous = ReadPreviousLemmas(Path.Combine(corpus, "senses", ownFile));
        var speechPath = Path.Combine(corpus, "forms", "speech.tsv");
        var speechLines = File.Exists(speechPath)
            ? File.ReadAllLines(speechPath, new UTF8Encoding(false)).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList()
            : ["form\tlemma\tlabels"];
        for (var i = 1; i < speechLines.Count; i++)
        {
            if (speechLines[i].Split('\t').Length < 2)
            {
                throw new InvalidDataException($"{speechPath}:{i + 1}: expected the columns form, lemma and labels separated by tabs.");
            }
        }

        var existingForms = (File.Exists(speechPath) ? Corpus.ReadForms(speechPath) : []).Where(f => !previous.Contains(f.Lemma)).ToList();
        var taken = ReadExistingSentences(corpus, ownFile);

        var drops = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var tally = new Action<string>(reason => drops[reason] = drops.GetValueOrDefault(reason) + 1);
        var provenance = ReadInputProvenance(provenanceFiles);

        // Filter the rows that do not depend on the forms
        var pool = new List<NounVerbRow>();
        var seen = new Dictionary<string, NounVerbRow>(StringComparer.Ordinal);
        var conflicting = new HashSet<string>(StringComparer.Ordinal);
        var otherCounts = new SortedDictionary<string, (int Noun, int Verb, string Reason, string Note)>(StringComparer.Ordinal);
        var inputRows = 0;
        foreach (var file in sentenceFiles)
        {
            var (rows, errors) = Corpus.ReadSentencesLenient(file);
            inputRows += rows.Count + errors.Count;
            if (errors.Count > 0)
            {
                drops["malformed row"] = drops.GetValueOrDefault("malformed row") + errors.Count;
            }

            foreach (var row in rows)
            {
                if (row.Sense != SenseNoun && row.Sense != SenseVerb)
                {
                    tally("sense is not noun or verb");
                    continue;
                }

                if (exclusions.TryGetValue(row.Lemma, out var category))
                {
                    AddCount(otherCounts, row, "excluded", category);
                    tally("excluded lemma");
                    continue;
                }

                if (gateDropped.Contains(row.Lemma))
                {
                    tally("lemma dropped by the quality gate");
                    continue;
                }

                if (inventory.ContainsLemma(row.Lemma))
                {
                    AddCount(otherCounts, row, "already-trained", "the lemma is in the sense inventory");
                    tally("lemma already trained");
                    continue;
                }

                var id = PosAudit.RowId(row.Lemma, row.Sentence);
                if (!provenance.TryGetValue(id, out var source))
                {
                    tally("no provenance row");
                    continue;
                }

                var reason = Accept(row, id, source, out var accepted);
                if (reason is not null)
                {
                    tally(reason);
                    continue;
                }

                var key = row.Sentence.ToLowerInvariant();
                if (taken.Contains(key))
                {
                    tally("sentence already in the corpus");
                }
                else if (seen.TryGetValue(key, out var first))
                {
                    if (first.Id == id && first.Row.Sense != row.Sense)
                    {
                        conflicting.Add(id);
                    }

                    tally("duplicate sentence");
                }
                else
                {
                    seen.Add(key, accepted!);
                    pool.Add(accepted!);
                }
            }
        }

        if (conflicting.Count > 0)
        {
            pool.RemoveAll(r => conflicting.Contains(r.Id));
            drops["conflicting sense of a repeated sentence"] = conflicting.Count;
        }

        // Lemmas that can reach the minimums form the candidates for the forms check
        var minimum = options.MinAnchors + options.MinHoldout;
        var candidates = Eligible(pool, minimum);
        var (formsOf, collisions) = ResolveForms(candidates, explicitForms, existingForms, inventory);

        var report = new StringBuilder();
        var kept = new List<NounVerbRow>();
        var unresolved = new SortedDictionary<(string Lemma, string Target), int>();
        foreach (var row in pool)
        {
            if (!candidates.Contains(row.Row.Lemma))
            {
                kept.Add(row);
                continue;
            }

            var target = row.Row.Words[row.Row.TargetWordIndex].ToLowerInvariant();
            if (formsOf[row.Row.Lemma].ContainsKey(target))
            {
                kept.Add(row);
            }
            else
            {
                var isCollision = collisions.Any(c => c.Lemma == row.Row.Lemma && c.Form == target);
                tally(isCollision ? "target is a collision form" : "target is not a form of the lemma");
                unresolved[(row.Row.Lemma, target)] = unresolved.GetValueOrDefault((row.Row.Lemma, target)) + 1;
            }
        }

        var trained = Eligible(kept, minimum);
        var (anchorRows, holdoutRows, sharedBySense, surplus) = AllocateAll(kept, trained, options);

        WriteOutputs(corpus, ownFile, anchorRows, holdoutRows, trained, formsOf, speechLines, previous);
        WriteUntrained(corpus, kept, trained, otherCounts);

        RenderReport(report, name, inputRows, drops, kept, trained, anchorRows, holdoutRows, sharedBySense, surplus, formsOf, collisions, unresolved, otherCounts);
        return report.ToString();
    }

    private static HashSet<string> Eligible(IReadOnlyList<NounVerbRow> rows, int minimum)
    {
        return rows.GroupBy(r => r.Row.Lemma, StringComparer.Ordinal)
            .Where(g => g.Count(r => r.Row.Sense == SenseNoun) >= minimum && g.Count(r => r.Row.Sense == SenseVerb) >= minimum)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static void AddCount(SortedDictionary<string, (int Noun, int Verb, string Reason, string Note)> counts, CorpusRow row, string reason, string note)
    {
        var (noun, verb, _, _) = counts.GetValueOrDefault(row.Lemma);
        counts[row.Lemma] = (noun + (row.Sense == SenseNoun ? 1 : 0), verb + (row.Sense == SenseVerb ? 1 : 0), reason, note);
    }

    /// <summary>
    ///     Checks one row and builds its accepted form; returns the drop reason or null.
    /// </summary>
    private static string? Accept(CorpusRow row, string id, InputProvenance source, out NounVerbRow? accepted)
    {
        accepted = null;
        if (source.Lemma != row.Lemma || source.Sense != row.Sense)
        {
            return "provenance disagrees on lemma or sense";
        }

        var plain = row.Sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
        if (plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > RealImport.MaxWords)
        {
            return "more than 35 words";
        }

        if (plain.Contains("gutenberg", StringComparison.OrdinalIgnoreCase))
        {
            return "sentence mentions Gutenberg";
        }

        var origin = source.Source == UserOrigin ? UserOrigin : "real";
        var licence = LicenceNames.GetValueOrDefault(source.Licence, source.Licence);
        if (!RealImport.Licences.Contains(licence))
        {
            return "licence not allowed";
        }

        var contributor = source.Contributor.Length == 0 ? "-" : source.Contributor;
        var url = source.Url.Length == 0 ? "-" : source.Url;
        if (licence == "CC BY 2.0 FR" && (contributor is "-" or "unknown"))
        {
            return "CC BY row without contributor";
        }

        if (origin == UserOrigin && licence != UserLicence)
        {
            return "licence not allowed";
        }

        var sourceKey = SourceKey(source.Source, url, id);
        if (source.Source == "gutenberg" && (!sourceKey.StartsWith("pg", StringComparison.Ordinal) || licence != "Public domain"))
        {
            return "Gutenberg row without book number or public-domain licence";
        }

        var provenance = new ProvenanceRow(id, row.Lemma, row.Sense, "anchors", source.Source, licence, sourceKey, url, contributor, origin);
        accepted = new NounVerbRow(id, row, provenance, GroupOf(source.Source, url, contributor));
        return null;
    }

    private static string SourceKey(string source, string url, string id)
    {
        var last = url.Split('#')[0].TrimEnd('/').Split('/')[^1];
        if (source == "gutenberg")
        {
            var book = EbookNumber().Match(url);
            return book.Success ? $"pg{book.Groups[1].Value}#{id}" : id;
        }

        if (source == "wiktionary")
        {
            return "wikt:" + Uri.UnescapeDataString(last);
        }

        if (source == UserOrigin)
        {
            return UserOrigin;
        }

        var at = url.IndexOf("sent_id=", StringComparison.Ordinal);
        if (source.StartsWith("ud-", StringComparison.Ordinal) && at >= 0)
        {
            return url[(at + "sent_id=".Length)..];
        }

        return url == "-" || last.Length == 0 ? id : last;
    }

    /// <summary>
    ///     Resolves the forms of the candidate lemmas against each other and against the existing forms.
    /// </summary>
    /// <returns>The accepted forms per lemma (form to kind) and the collisions that were left out.</returns>
    private static (Dictionary<string, SortedDictionary<string, string>> Forms, List<(string Lemma, string Form, string Reason)> Collisions) ResolveForms(
        HashSet<string> candidates,
        IReadOnlyList<(string Form, string Lemma, string[] Labels)> explicitForms,
        IReadOnlyList<(string Form, string Lemma, string[] Labels)> existingForms,
        SenseInventory inventory)
    {
        var claims = new Dictionary<string, List<(string Lemma, string Kind)>>(StringComparer.Ordinal);
        foreach (var lemma in candidates.OrderBy(l => l, StringComparer.Ordinal))
        {
            claims.GetOrAdd(lemma).Add((lemma, "base"));
            foreach (var (form, kind) in Inflect(lemma).Where(f => !(f.Kind == "ed" && lemma is "lay" or "pay" or "say")))
            {
                claims.GetOrAdd(form).Add((lemma, kind));
            }
        }

        foreach (var (form, lemma, _) in explicitForms.Where(f => candidates.Contains(f.Lemma)))
        {
            claims.GetOrAdd(form).Add((lemma, "irregular"));
        }

        var existingByForm = existingForms.GroupBy(f => f.Form, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(f => f.Lemma).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var formsOf = candidates.ToDictionary(l => l, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var collisions = new List<(string, string, string)>();
        foreach (var (form, list) in claims.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            // A lemma's own spelling wins over an inflection of another lemma
            var isBase = candidates.Contains(form);
            foreach (var (lemma, kind) in list.Distinct())
            {
                string? reason = null;
                if (kind != "base" && isBase && form != lemma)
                {
                    reason = "is the base form of another new lemma";
                }
                else if (kind != "base" && inventory.ContainsLemma(form))
                {
                    reason = "is a trained lemma";
                }
                else if (list.Select(c => c.Lemma).Distinct().Count() > 1 && !isBase)
                {
                    reason = "is a form of more than one new lemma";
                }
                else if (existingByForm.TryGetValue(form, out var others) && others.Any(o => o != lemma))
                {
                    reason = "is already a form of " + string.Join('/', others.Where(o => o != lemma).Order(StringComparer.Ordinal));
                }

                if (reason is null)
                {
                    formsOf[lemma][form] = kind;
                }
                else
                {
                    collisions.Add((lemma, form, reason));
                }
            }
        }

        return (formsOf, collisions);
    }

    private static (List<NounVerbRow> Anchors, List<NounVerbRow> Holdout, SortedDictionary<string, int> Shared, int Surplus) AllocateAll(
        IReadOnlyList<NounVerbRow> kept, HashSet<string> trained, NounVerbOptions options)
    {
        var (anchors, holdout, surplus) = (new List<NounVerbRow>(), new List<NounVerbRow>(), 0);
        var shared = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in kept.Where(r => trained.Contains(r.Row.Lemma)).GroupBy(r => (r.Row.Lemma, r.Row.Sense)).OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.Sense, StringComparer.Ordinal))
        {
            var pool = group.ToList();
            var (a, h, s) = Allocate(pool, options);
            anchors.AddRange(a);
            holdout.AddRange(h);
            surplus += pool.Count - a.Count - h.Count;
            if (s > 0)
            {
                shared[group.Key.Lemma + "/" + group.Key.Sense] = s;
            }
        }

        return (anchors, holdout, shared, surplus);
    }

    private static void WriteOutputs(
        string corpus,
        string ownFile,
        List<NounVerbRow> anchors,
        List<NounVerbRow> holdout,
        HashSet<string> trained,
        Dictionary<string, SortedDictionary<string, string>> formsOf,
        List<string> speechLines,
        ISet<string> previous)
    {
        WriteRows(Path.Combine(corpus, "anchors", ownFile), anchors);
        WriteRows(Path.Combine(corpus, "holdout", ownFile), holdout);
        RealImport.WriteProvenance(
            Path.Combine(corpus, "provenance", ownFile),
            anchors.Select(r => r.Provenance with { Split = "anchors" }).Concat(holdout.Select(r => r.Provenance with { Split = "holdout" })));
        SenseInventory.WriteFile(
            Path.Combine(corpus, "senses", ownFile),
            trained.OrderBy(l => l, StringComparer.Ordinal).SelectMany(l => new[] { SenseNoun, SenseVerb }.Select(s => new InventoryRow(l, s, InventoryRow.PosOfTag(s)!.Value, null, null, ownFile))));

        // The forms of the owned lemmas replace their rows; the labels are the defaults of the kind plus every sense seen on the form
        var targets = anchors.Concat(holdout).GroupBy(r => (r.Row.Lemma, Form: r.Row.Words[r.Row.TargetWordIndex].ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(r => r.Row.Sense).ToHashSet(StringComparer.Ordinal));
        var lines = speechLines.Where((l, i) => i == 0 || !(trained.Contains(l.Split('\t')[1]) || previous.Contains(l.Split('\t')[1]))).ToList();
        foreach (var lemma in trained.OrderBy(l => l, StringComparer.Ordinal))
        {
            foreach (var (form, kind) in formsOf[lemma])
            {
                var labels = (kind is "base" or "s" ? new[] { SenseNoun, SenseVerb } : [SenseVerb]).ToHashSet(StringComparer.Ordinal);
                labels.UnionWith(targets.GetValueOrDefault((lemma, form), []));
                lines.Add($"{form}\t{lemma}\t{string.Join(',', labels.Order(StringComparer.Ordinal))}");
            }
        }

        Directory.CreateDirectory(Path.Combine(corpus, "forms"));
        File.WriteAllText(Path.Combine(corpus, "forms", "speech.tsv"), string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    private static void WriteUntrained(string corpus, IReadOnlyList<NounVerbRow> kept, HashSet<string> trained, SortedDictionary<string, (int Noun, int Verb, string Reason, string Note)> others)
    {
        var text = new StringBuilder("lemma\tnoun\tverb\treason\tnote\n");
        var rows = new SortedDictionary<string, (int Noun, int Verb, string Reason, string Note)>(StringComparer.Ordinal);
        foreach (var group in kept.GroupBy(r => r.Row.Lemma, StringComparer.Ordinal).Where(g => !trained.Contains(g.Key)))
        {
            var (noun, verb) = (group.Count(r => r.Row.Sense == SenseNoun), group.Count(r => r.Row.Sense == SenseVerb));
            rows[group.Key] = (noun, verb, Math.Min(noun, verb) < DominantUseRows ? "dominant-use" : "insufficient-data", string.Empty);
        }

        foreach (var (lemma, value) in others)
        {
            rows[lemma] = value;
        }

        foreach (var (lemma, (noun, verb, reason, note)) in rows)
        {
            text.Append($"{lemma}\t{noun}\t{verb}\t{reason}\t{note}\n");
        }

        Directory.CreateDirectory(Path.Combine(corpus, "audit"));
        File.WriteAllText(Path.Combine(corpus, "audit", UntrainedFile), text.ToString(), new UTF8Encoding(false));
    }

    private static void RenderReport(
        StringBuilder report,
        string name,
        int inputRows,
        SortedDictionary<string, int> drops,
        List<NounVerbRow> kept,
        HashSet<string> trained,
        List<NounVerbRow> anchors,
        List<NounVerbRow> holdout,
        SortedDictionary<string, int> shared,
        int surplus,
        Dictionary<string, SortedDictionary<string, string>> formsOf,
        List<(string Lemma, string Form, string Reason)> collisions,
        SortedDictionary<(string Lemma, string Target), int> unresolved,
        SortedDictionary<string, (int Noun, int Verb, string Reason, string Note)> others)
    {
        var lemmas = kept.Select(r => r.Row.Lemma).Distinct(StringComparer.Ordinal).Count();
        report.AppendLine($"{name}: {inputRows} input rows; {kept.Count} accepted rows of {lemmas} lemmas.");
        foreach (var (reason, count) in drops)
        {
            report.AppendLine($"  dropped {count}: {reason}");
        }

        report.AppendLine($"lemmas trained {trained.Count}; not trained {lemmas - trained.Count} (rows below the minimums of a sense) and {others.Count} excluded or already trained.");
        report.AppendLine($"rows written: anchors {anchors.Count}, holdout {holdout.Count}, surplus not used {surplus}.");
        report.AppendLine($"forms written: {trained.Sum(l => formsOf[l].Count)}; collision forms left out: {collisions.Count(c => trained.Contains(c.Lemma))}.");
        foreach (var (lemma, form, reason) in collisions.Where(c => trained.Contains(c.Lemma)).Take(50))
        {
            report.AppendLine($"  collision form '{form}' of '{lemma}' {reason}");
        }

        report.AppendLine($"holdout shares a source group with the anchors in {shared.Count} senses" + (shared.Count > 0 ? ": " + string.Join(", ", shared.Select(s => $"{s.Key} ({s.Value})").Take(50)) : string.Empty) + ".");
        var wiktionary = anchors.Concat(holdout).GroupBy(r => (r.Row.Lemma, r.Row.Sense)).Where(g => g.Count(r => r.Provenance.Source == "wiktionary") * 2 >= g.Count()).Select(g => g.Key.Lemma + "/" + g.Key.Sense).ToList();
        report.AppendLine($"senses with at least half of their rows from Wiktionary: {wiktionary.Count}" + (wiktionary.Count > 0 ? ": " + string.Join(", ", wiktionary.Take(50)) : string.Empty) + ".");
        foreach (var group in anchors.Concat(holdout).GroupBy(r => r.Provenance.Source).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            report.AppendLine($"  source {group.Key}: {group.Count()} rows");
        }

        foreach (var ((lemma, target), count) in unresolved.OrderByDescending(u => u.Value).Take(20))
        {
            report.AppendLine($"  unresolved target '{target}' of '{lemma}': {count} rows");
        }
    }

    private static void WriteRows(string path, IEnumerable<NounVerbRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Row.Lemma, StringComparer.Ordinal).ThenBy(r => r.Row.Sense, StringComparer.Ordinal).ThenBy(r => r.Row.Sentence, StringComparer.Ordinal).Select(r => r.Row))
        {
            text.Append($"{r.Lemma}\t{r.Sense}\t{r.Sentence}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static Dictionary<string, string> ReadExclusions(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length < 2 || c[0].Trim().Length == 0 || c[1].Trim().Length == 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{i + 1}: expected a lemma and a category column.");
            }

            result[c[0].Trim().ToLowerInvariant()] = c[1].Trim();
        }

        return result;
    }

    private static HashSet<string> ReadPreviousLemmas(string path)
    {
        var lemmas = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0))
            {
                lemmas.Add(line.Split('\t')[0]);
            }
        }

        return lemmas;
    }

    private static HashSet<string> ReadExistingSentences(string corpus, string ownFile)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in new[] { "anchors", "holdout", "realtext" })
        {
            var directory = Path.Combine(corpus, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(directory, "*.tsv").Where(f => Path.GetFileName(f) != ownFile).OrderBy(f => f, StringComparer.Ordinal))
            {
                keys.UnionWith(Corpus.ReadSentences(file).Select(r => r.Sentence.ToLowerInvariant()));
            }
        }

        return keys;
    }

    private static Dictionary<string, InputProvenance> ReadInputProvenance(IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, InputProvenance>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file, new UTF8Encoding(false));
            var header = (lines.Length == 0 ? string.Empty : lines[0].TrimEnd('\r')).Split('\t');
            int Column(string column) => Array.IndexOf(header, column) is var at and >= 0
                ? at
                : throw new InvalidDataException($"{Path.GetFileName(file)}:1: the header needs a '{column}' column.");
            var (hash, lemma, sense, source, licence, url, contributor) = (
                Column("sentence_hash"), Column("lemma"), Column("sense"), Column("source"), Column("licence"), Column("url"), Column("contributor_or_work"));
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                var c = line.Split('\t');
                if (c.Length < header.Length)
                {
                    throw new InvalidDataException($"{Path.GetFileName(file)}:{i + 1}: expected {header.Length} columns but found {c.Length}.");
                }

                result.TryAdd(c[hash], new InputProvenance(c[lemma], c[sense], c[source], c[licence], c[url], c[contributor]));
            }
        }

        return result;
    }

    private static List<T> RoundRobin<T>(IReadOnlyList<List<T>> groups, int take)
    {
        var result = new List<T>();
        for (var depth = 0; result.Count < take && groups.Any(g => g.Count > depth); depth++)
        {
            foreach (var group in groups.Where(g => g.Count > depth))
            {
                if (result.Count >= take)
                {
                    break;
                }

                result.Add(group[depth]);
            }
        }

        return result;
    }

    private static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
        where TValue : new()
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = new TValue();
            dictionary[key] = value;
        }

        return value;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static int VowelGroups(string word)
    {
        var groups = 0;
        var previous = false;
        for (var i = 0; i < word.Length; i++)
        {
            var vowel = (IsVowel(word[i]) && !(word[i] == 'u' && i > 0 && word[i - 1] == 'q')) || (word[i] == 'y' && i > 0);
            if (vowel && !previous)
            {
                groups++;
            }

            previous = vowel;
        }

        return groups;
    }

    private static bool EndsWithConsonantVowelConsonant(string word)
    {
        if (word.Length < 3)
        {
            return false;
        }

        var (c1, v, c2) = (word[^3], word[^2], word[^1]);
        var quVowel = v == 'u' && c1 == 'q';
        if (quVowel)
        {
            return false;
        }

        var consonant1 = !IsVowel(c1) || (c1 == 'u' && word.Length >= 4 && word[^4] == 'q');
        return consonant1 && IsVowel(v) && !IsVowel(c2) && c2 is not ('w' or 'x' or 'y');
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 6);

    [GeneratedRegex(@"^(.*)-\d+$")]
    private static partial Regex DocumentSuffix();

    [GeneratedRegex(@"/ebooks/(\d+)")]
    private static partial Regex EbookNumber();

    private sealed record InputProvenance(string Lemma, string Sense, string Source, string Licence, string Url, string Contributor);
}
