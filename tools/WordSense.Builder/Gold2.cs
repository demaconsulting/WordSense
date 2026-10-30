using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder;

/// <summary>
///     One document of the second all-words gold set: where its text comes from and under which terms.
/// </summary>
/// <param name="DocId">The document id (<c>voa-7782072</c>, <c>gutenberg-1419</c>).</param>
/// <param name="Family">The source family: <c>voa</c>, <c>usgs</c>, <c>nps</c>, <c>noaa-nws</c> or <c>gutenberg</c>.</param>
/// <param name="Title">The title.</param>
/// <param name="Url">The address the text was retrieved from.</param>
/// <param name="Author">The author, or empty.</param>
/// <param name="Year">The year (or the years of the front matter), or empty.</param>
/// <param name="LicenseBasis">The statement of why the text is public domain in the United States.</param>
/// <param name="TermsUrl">The terms page of the source.</param>
/// <param name="Retrieved">The date of the retrieval.</param>
/// <param name="RawSha256">The SHA-256 of the retrieved file.</param>
/// <param name="TextSha256">The SHA-256 of the extracted text.</param>
/// <param name="Sentences">The number of sentences that the document gave to the pool.</param>
internal sealed record Gold2Document(string DocId, string Family, string Title, string Url, string Author, string Year, string LicenseBasis, string TermsUrl, string Retrieved, string RawSha256, string TextSha256, int Sentences);

/// <summary>
///     One sampled token of the second all-words gold set, as frozen in the manifest before any labeling.
/// </summary>
/// <param name="Id">The id: <c>d-</c> or <c>t-</c> and a number.</param>
/// <param name="Split">The split: <c>dev</c> or <c>test</c>.</param>
/// <param name="DocId">The id of the document of the sentence.</param>
/// <param name="Source">The source family of the document.</param>
/// <param name="SentenceId">The id of the sentence (<c>&lt;doc id&gt;-s0012</c>).</param>
/// <param name="Target">The zero-based index of the token in <paramref name="Words"/>.</param>
/// <param name="Form">The token as written.</param>
/// <param name="Strata">The strata flags of the token (see <see cref="Gold2Strata"/>), comma-separated, or <c>C</c> for a control token.</param>
/// <param name="Words">The tokens of the sentence as a model receives them.</param>
internal sealed record Gold2Token(string Id, string Split, string DocId, string Source, string SentenceId, int Target, string Form, string Strata, IReadOnlyList<string> Words);

/// <summary>
///     One labeled token of the second all-words gold set.
/// </summary>
/// <param name="Token">The sampled token.</param>
/// <param name="Terms">The terms page of the source of the document.</param>
/// <param name="Pos">The gold part of speech: the name of a <see cref="PartOfSpeech"/> or <c>Other</c> (a token that is no word class of the lexicon).</param>
/// <param name="Lemma">The gold lemma.</param>
/// <param name="Features">The grammatical features of the gold analysis (<c>Tense=Past+VerbForm=Part</c>), or empty.</param>
/// <param name="Tense">The tense class of a verb: <c>past</c> (past tense or past participle) or <c>nonpast</c>; <c>-</c> for any other token.</param>
/// <param name="Sense">The gold sense id; <c>-</c> when the analysis has fewer than two senses; <c>none</c> when no single sense is defensible.</param>
/// <param name="Status">How the label was settled: <c>agreed</c> (both labelers) or <c>adjudicated</c>.</param>
/// <param name="Agreement">What the two labelers agreed on: <c>full</c>, <c>analysis</c> (the same analysis, another sense or one unsure), <c>pos</c> (the same part of speech) or <c>none</c>.</param>
internal sealed record Gold2Row(Gold2Token Token, string Terms, string Pos, string Lemma, string Features, string Tense, string Sense, string Status, string Agreement);

/// <summary>
///     The strata of the second all-words gold set. They are decided from the lexicon and the anchors alone, before any model
///     or labeler sees a token.
/// </summary>
internal static class Gold2Strata
{
    /// <summary>The token has a polysemous analysis and none of the lemmas of its polysemous analyses has weights.</summary>
    public const string Untrained = "U";

    /// <summary>The token has a polysemous analysis whose lemma has weights.</summary>
    public const string Trained = "TR";

    /// <summary>The form is a same-spelling verb: it has past and non-past verb analyses (<see cref="TenseEvaluator.GroupsOf"/>).</summary>
    public const string Tense = "T";

    /// <summary>The form has a noun analysis and a verb analysis.</summary>
    public const string NounVerb = "NV";

    /// <summary>The spelling is a heteronym of the word table (<see cref="Heteronyms"/>).</summary>
    public const string Heteronym = "H";

    /// <summary>The form is an inflected form of two or more open-class lemmas (<c>saw</c>, <c>left</c>, <c>leaves</c>).</summary>
    public const string Lemma = "L";

    /// <summary>A control token: none of the other strata.</summary>
    public const string Control = "C";

    /// <summary>The strata in the order in which the draw fills their quotas (the rarest first).</summary>
    public static readonly IReadOnlyList<string> DrawOrder = [Tense, Lemma, Heteronym, NounVerb, Untrained, Trained, Control];

    /// <summary>
    ///     Finds the strata of a written form.
    /// </summary>
    /// <param name="form">The token as written.</param>
    /// <param name="trained">The trained lemmas (the lemmas that have weights: sentences for two or more senses, see <see cref="WeightsTrainer.HasWeights"/>).</param>
    /// <param name="heteronyms">The normalized heteronym spellings.</param>
    /// <returns>The flags in <see cref="DrawOrder"/> order; <c>C</c> alone when the form has none; null when the lexicon does not know the form.</returns>
    public static IReadOnlyList<string>? Of(string form, IReadOnlySet<string> trained, IReadOnlySet<string> heteronyms)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(trained);
        ArgumentNullException.ThrowIfNull(heteronyms);
        var key = BasicClassifier.Normalize(form);
        if (key.Length == 0 || !WordLexicon.TryLookup(key, out var analyses) || analyses.IsDefaultOrEmpty)
        {
            return null;
        }

        var open = analyses.Where(a => a.PartOfSpeech is PartOfSpeech.Noun or PartOfSpeech.Verb or PartOfSpeech.Adjective or PartOfSpeech.Adverb).Select(a => a.Lemma.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count();
        var flags = new List<string>();
        if (TenseEvaluator.GroupsOf(form).Count > 0)
        {
            flags.Add(Tense);
        }

        if (open >= 2)
        {
            flags.Add(Lemma);
        }

        if (heteronyms.Contains(key))
        {
            flags.Add(Heteronym);
        }

        if (analyses.Any(a => a.PartOfSpeech == PartOfSpeech.Noun) && analyses.Any(a => a.PartOfSpeech == PartOfSpeech.Verb))
        {
            flags.Add(NounVerb);
        }

        if (SenseFlag(form, trained) is { } sense)
        {
            flags.Add(sense);
        }

        if (flags.Count == 0)
        {
            flags.Add(Control);
        }

        return [.. DrawOrder.Where(flags.Contains)];
    }

    /// <summary>
    ///     Finds the sense stratum of a written form: whether its polysemous lemmas are trained.
    /// </summary>
    /// <param name="form">The token as written.</param>
    /// <param name="trained">The trained lemmas (the lemmas that have weights).</param>
    /// <returns><see cref="Trained"/> when a polysemous analysis of the form has a trained lemma, <see cref="Untrained"/> when it has polysemous analyses and none is trained, null when it has no polysemous analysis or the lexicon does not know the form.</returns>
    public static string? SenseFlag(string form, IReadOnlySet<string> trained)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(trained);
        var key = BasicClassifier.Normalize(form);
        if (key.Length == 0 || !WordLexicon.TryLookup(key, out var analyses) || analyses.IsDefaultOrEmpty)
        {
            return null;
        }

        var polysemous = analyses.Where(a => WordLexicon.SensesOf(a.Lemma, a.PartOfSpeech).Length >= 2).Select(a => a.Lemma).Distinct(StringComparer.Ordinal).ToList();
        if (polysemous.Count == 0)
        {
            return null;
        }

        return polysemous.Any(trained.Contains) ? Trained : Untrained;
    }

    /// <summary>
    ///     Gets the source group of a family, the unit of the genre weights and of the genre results.
    /// </summary>
    /// <param name="family">The source family.</param>
    /// <returns><c>technical</c> (USGS, NPS, NOAA), <c>news</c> (VOA) or <c>literary</c> (Gutenberg).</returns>
    public static string GroupOf(string family) => family switch
    {
        "voa" => "news",
        "gutenberg" => "literary",
        _ => "technical",
    };
}

/// <summary>
///     The files of the second all-words gold set (<c>corpus/allwords/v2</c>): the frozen manifest of the sampled tokens,
///     the labeled rows of the two splits, the raw labels and the documents.
/// </summary>
internal static partial class Gold2
{
    /// <summary>The folder below the corpus directory.</summary>
    public static readonly string Folder = Path.Join("allwords", "v2");

    /// <summary>The name of the development split.</summary>
    public const string Dev = "dev";

    /// <summary>The name of the test split.</summary>
    public const string Test = "test";

    /// <summary>The name of the manifest file.</summary>
    public const string ManifestName = "manifest.tsv";

    /// <summary>The name of the documents file.</summary>
    public const string SourcesName = "sources.tsv";

    /// <summary>The name of the raw labels file.</summary>
    public const string LabelsName = "labels.tsv";

    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly string[] SourceColumns = ["doc_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved", "raw_sha256", "text_sha256", "sentences"];

    /// <summary>
    ///     Gets the directory of the set.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The directory.</returns>
    public static string Directory(string corpus) => Path.Join(corpus, Folder);

    /// <summary>
    ///     Gets the path of the labeled rows of a split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The path.</returns>
    public static string RowsPath(string corpus, string split) => Path.Join(Directory(corpus), split + ".tsv");

    /// <summary>
    ///     Reads the manifest.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The sampled tokens in file order, or an empty list when the manifest does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<Gold2Token> ReadManifest(string corpus) => ReadManifestFile(Path.Join(Directory(corpus), ManifestName));

    /// <summary>
    ///     Reads a manifest file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The sampled tokens in file order, or an empty list when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<Gold2Token> ReadManifestFile(string path)
    {
        var tokens = new List<Gold2Token>();
        if (!File.Exists(path))
        {
            return tokens;
        }

        var number = 0;
        foreach (var raw in File.ReadLines(path, Utf8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != 9 || !int.TryParse(c[5], NumberStyles.None, CultureInfo.InvariantCulture, out var target))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{number.ToString(CultureInfo.InvariantCulture)}: expected 9 tab-separated columns (id, split, doc_id, source, sent_id, target, form, strata, sentence).");
            }

            var words = c[8].Split(' ');
            if (target >= words.Length || !string.Equals(words[target], c[6], StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{number.ToString(CultureInfo.InvariantCulture)}: the word at index {c[5]} is not '{c[6]}'.");
            }

            tokens.Add(new Gold2Token(c[0], c[1], c[2], c[3], c[4], target, c[6], c[7], words));
        }

        return tokens;
    }

    /// <summary>
    ///     Renders the manifest.
    /// </summary>
    /// <param name="tokens">The sampled tokens.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderManifest(IEnumerable<Gold2Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var text = new StringBuilder();
        text.Append("# corpus = second all-words gold set (v2): the sampled tokens, frozen before any labeling\n");
        text.Append("# license = the selection is CC0 1.0; the sentences are public domain in the United States (see sources.tsv)\n");
        text.Append("# columns = id split doc_id source sent_id target form strata sentence\n");
        foreach (var t in tokens)
        {
            text.Append(string.Join('\t', t.Id, t.Split, t.DocId, t.Source, t.SentenceId, t.Target.ToString(CultureInfo.InvariantCulture), t.Form, t.Strata, string.Join(' ', t.Words))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads the documents file.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The documents in file order, or an empty list when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<Gold2Document> ReadDocuments(string corpus) => ReadDocumentsFile(Path.Join(Directory(corpus), SourcesName));

    /// <summary>
    ///     Reads a documents file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The documents in file order, or an empty list when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<Gold2Document> ReadDocumentsFile(string path)
    {
        var documents = new List<Gold2Document>();
        if (!File.Exists(path))
        {
            return documents;
        }

        var number = 0;
        foreach (var raw in File.ReadLines(path, Utf8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (number == 1 && line.StartsWith("doc_id\t", StringComparison.Ordinal) || line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != SourceColumns.Length || !int.TryParse(c[11], NumberStyles.None, CultureInfo.InvariantCulture, out var sentences))
            {
                throw new InvalidDataException($"{SourcesName}:{number.ToString(CultureInfo.InvariantCulture)}: expected {SourceColumns.Length.ToString(CultureInfo.InvariantCulture)} tab-separated columns.");
            }

            documents.Add(new Gold2Document(c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8], c[9], c[10], sentences));
        }

        return documents;
    }

    /// <summary>
    ///     Renders the documents file.
    /// </summary>
    /// <param name="documents">The documents.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderDocuments(IEnumerable<Gold2Document> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var text = new StringBuilder(string.Join('\t', SourceColumns)).Append('\n');
        foreach (var d in documents)
        {
            text.Append(string.Join('\t', d.DocId, d.Family, d.Title, d.Url, d.Author, d.Year, d.LicenseBasis, d.TermsUrl, d.Retrieved, d.RawSha256, d.TextSha256, d.Sentences.ToString(CultureInfo.InvariantCulture))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads the labeled rows of a split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The rows in file order, or an empty list when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<Gold2Row> ReadRows(string corpus, string split)
    {
        var path = RowsPath(corpus, split);
        var rows = new List<Gold2Row>();
        if (!File.Exists(path))
        {
            return rows;
        }

        var number = 0;
        foreach (var raw in File.ReadLines(path, Utf8))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length != 16 || !int.TryParse(c[5], NumberStyles.None, CultureInfo.InvariantCulture, out var target))
            {
                throw new InvalidDataException($"{split}.tsv:{number.ToString(CultureInfo.InvariantCulture)}: expected 16 tab-separated columns.");
            }

            var words = c[15].Split(' ');
            if (target >= words.Length || !string.Equals(words[target], c[6], StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{split}.tsv:{number.ToString(CultureInfo.InvariantCulture)}: the word at index {c[5]} is not '{c[6]}'.");
            }

            rows.Add(new Gold2Row(new Gold2Token(c[0], split, c[1], c[2], c[4], target, c[6], c[7], words), c[3], c[8], c[9], c[10] == "-" ? string.Empty : c[10], c[11], c[12], c[13], c[14]));
        }

        return rows;
    }

    /// <summary>
    ///     Renders the labeled rows of a split.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderRows(string split, IEnumerable<Gold2Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        text.Append("# corpus = second all-words gold set (v2), ").Append(split).Append(" split: part of speech, lemma, tense and sense of every token\n");
        text.Append("# license = labels and selection CC0 1.0; sentences public domain in the United States (doc_id and terms_url give the source)\n");
        text.Append("# columns = id doc_id source terms_url sent_id target form strata pos lemma features tense sense status agreement sentence\n");
        foreach (var r in rows)
        {
            var t = r.Token;
            text.Append(string.Join('\t', t.Id, t.DocId, t.Source, r.Terms, t.SentenceId, t.Target.ToString(CultureInfo.InvariantCulture), t.Form, t.Strata, r.Pos, r.Lemma, r.Features.Length == 0 ? "-" : r.Features, r.Tense, r.Sense, r.Status, r.Agreement, string.Join(' ', t.Words))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads the sentences of the manifest as parts of the leakage check: one part for each split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The parts that have tokens (a sentence with two tokens counts once).</returns>
    public static List<LeakageSet> LeakageSets(string corpus)
    {
        var sets = new List<LeakageSet>();
        var tokens = ReadManifest(corpus);
        foreach (var split in new[] { Dev, Test })
        {
            var sentences = tokens.Where(t => t.Split == split)
                .GroupBy(t => t.SentenceId, StringComparer.Ordinal)
                .Select(g => (g.Key, string.Join(' ', g.First().Words)))
                .ToList();
            if (sentences.Count > 0)
            {
                sets.Add(new LeakageSet(LeakageName(split), LeakageRole.Evaluation, sentences));
            }
        }

        return sets;
    }

    /// <summary>
    ///     Gets the name of the part of the leakage check that holds the sentences of a split.
    /// </summary>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The name.</returns>
    public static string LeakageName(string split) => $"all-words v2 {split}";

    /// <summary>
    ///     Gets the one key of a document that the parts of the repository write in different ways: a Project Gutenberg book is
    ///     <c>gutenberg-&lt;number&gt;</c> in the open corpus, the all-words sets and this set, <c>pg&lt;number&gt;</c> in the
    ///     provenance of the anchors, and an address of the book is a third way.
    /// </summary>
    /// <param name="key">A document id, a source key or an address.</param>
    /// <returns><c>gutenberg-&lt;number&gt;</c> for a Gutenberg book (without leading zeros), the key itself otherwise.</returns>
    public static string CanonicalDocumentKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var trimmed = key.Trim();
        var match = GutenbergKey().Match(trimmed);
        if (!match.Success)
        {
            match = GutenbergAddress().Match(trimmed);
        }

        return match.Success ? "gutenberg-" + match.Groups[1].Value.TrimStart('0') : trimmed;
    }

    [GeneratedRegex(@"^(?:pg|gutenberg[-_:]?)(\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex GutenbergKey();

    [GeneratedRegex(@"gutenberg\.org/(?:cache/epub/|files/|ebooks/|epub/)(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex GutenbergAddress();

    [GeneratedRegex(@"-s\d+$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SentenceSuffix();

    /// <summary>
    ///     Reads the ids and addresses of every document that the rest of the repository uses or has rejected: the documents of
    ///     the open corpus (used, candidate list, rejected, downloaded), the documents of the sentences of the first all-words
    ///     set and the source keys of the provenance of the training and held-out sentences.
    /// </summary>
    /// <remarks>
    ///     Every id and address is also added under its <see cref="CanonicalDocumentKey"/>, so a Gutenberg book is found
    ///     whichever way a part of the repository writes it.
    /// </remarks>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The document ids and the addresses.</returns>
    public static (HashSet<string> Ids, HashSet<string> Urls) UsedDocuments(string corpus)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var open = Path.Join(corpus, OpenCorpus.Folder);
        AddColumns(Path.Join(open, "sources.tsv"), "source_id", "url", ids, urls);
        foreach (var name in new[] { "urls.tsv", "sources_excluded.tsv", "fetch_log.tsv" })
        {
            AddColumns(Path.Join(open, "sources", name), "doc_id", "url", ids, urls);
        }

        AddColumns(Path.Join(open, "build", "documents.tsv"), "source_id", "url", ids, urls);
        var provenance = Path.Join(corpus, "provenance");
        if (System.IO.Directory.Exists(provenance))
        {
            foreach (var file in System.IO.Directory.GetFiles(provenance, "*.tsv"))
            {
                foreach (var line in File.ReadLines(file, Utf8).Skip(1))
                {
                    var c = line.Split('\t');
                    if (c.Length >= 4)
                    {
                        var hash = c[3].IndexOf('#', StringComparison.Ordinal);
                        AddId(ids, hash < 0 ? c[3] : c[3][..hash]);
                    }
                }
            }
        }

        // The first all-words set names the document in the id of the sentence (<doc id>-s0012), in column 2
        foreach (var split in new[] { Dev, Test })
        {
            var path = Path.Join(corpus, "allwords", split + ".tsv");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path, Utf8).Where(l => l.Length > 0 && l[0] != '#'))
            {
                var c = line.Split('\t');
                if (c.Length > 1)
                {
                    AddId(ids, SentenceSuffix().Replace(c[1], string.Empty));
                }
            }
        }

        return (ids, urls);
    }

    private static void AddId(HashSet<string> ids, string id)
    {
        ids.Add(id);
        ids.Add(CanonicalDocumentKey(id));
    }

    private static void AddColumns(string path, string idColumn, string urlColumn, HashSet<string> ids, HashSet<string> urls)
    {
        if (!File.Exists(path))
        {
            return;
        }

        int idIndex = -1, urlIndex = -1;
        foreach (var raw in File.ReadLines(path, Utf8))
        {
            var c = raw.TrimEnd('\r').Split('\t');
            if (idIndex < 0)
            {
                idIndex = Array.IndexOf(c, idColumn);
                urlIndex = Array.IndexOf(c, urlColumn);
                continue;
            }

            if (idIndex >= 0 && idIndex < c.Length)
            {
                AddId(ids, c[idIndex]);
            }

            if (urlIndex >= 0 && urlIndex < c.Length)
            {
                var hash = c[urlIndex].IndexOf('#', StringComparison.Ordinal);
                var url = hash < 0 ? c[urlIndex] : c[urlIndex][..hash];
                urls.Add(url);
                if (CanonicalDocumentKey(url) is var canonical && !string.Equals(canonical, url, StringComparison.Ordinal))
                {
                    ids.Add(canonical);
                }
            }
        }
    }

    /// <summary>
    ///     Checks the documents of the set against the rest of the repository.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The problems: a document that another part of the repository uses or has rejected (a Gutenberg book under any of its keys), a manifest token whose document is not listed, a document with tokens in both splits, a duplicate id.</returns>
    public static List<string> ValidateDocuments(string corpus)
    {
        var problems = new List<string>();
        var documents = ReadDocuments(corpus);
        var tokens = ReadManifest(corpus);
        var (ids, urls) = UsedDocuments(corpus);
        foreach (var d in documents)
        {
            if (ids.Contains(d.DocId) || ids.Contains(CanonicalDocumentKey(d.DocId)))
            {
                problems.Add($"The document '{d.DocId}' is already used or was rejected by another part of the repository.");
            }

            if (urls.Contains(d.Url) || ids.Contains(CanonicalDocumentKey(d.Url)))
            {
                problems.Add($"The address '{d.Url}' of the document '{d.DocId}' is already used by another part of the repository.");
            }
        }

        foreach (var duplicate in documents.GroupBy(d => CanonicalDocumentKey(d.DocId), StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"The document '{duplicate.Key}' is listed {duplicate.Count().ToString(CultureInfo.InvariantCulture)} times.");
        }

        var known = documents.Select(d => d.DocId).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in tokens.Select(t => t.DocId).Where(d => !known.Contains(d)).Distinct(StringComparer.Ordinal))
        {
            problems.Add($"The manifest has a token of the document '{missing}', which {SourcesName} does not list.");
        }

        foreach (var docs in tokens.GroupBy(t => t.DocId, StringComparer.Ordinal).Where(g => g.Select(t => t.Split).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            problems.Add($"The document '{docs.Key}' has tokens in both splits.");
        }

        return problems;
    }

    /// <summary>
    ///     Checks the labeled rows of a split against the manifest: the rows and the sampled tokens of the split must match one
    ///     to one.
    /// </summary>
    /// <param name="manifest">The sampled tokens of the manifest (every split).</param>
    /// <param name="rows">The labeled rows of the split.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The problems: a row that the manifest does not have for the split, a row that appears twice, a manifest token without a row, a manifest id that appears twice, and a row that differs from its token in the document, the source, the sentence, the target, the form, the strata or the words.</returns>
    public static List<string> ValidateRows(IReadOnlyList<Gold2Token> manifest, IReadOnlyList<Gold2Row> rows, string split)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(rows);
        var problems = new List<string>();
        var file = split + ".tsv";
        foreach (var duplicate in manifest.GroupBy(t => t.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"{ManifestName}: the id '{duplicate.Key}' appears {duplicate.Count().ToString(CultureInfo.InvariantCulture)} times.");
        }

        var expected = manifest.Where(t => t.Split == split).GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in rows.Select(row => row.Token))
        {
            if (!seen.Add(token.Id))
            {
                problems.Add($"{file}: the row '{token.Id}' appears more than once.");
                continue;
            }

            if (!expected.TryGetValue(token.Id, out var sampled))
            {
                problems.Add($"{file}: the row '{token.Id}' is not a token of the {split} split of {ManifestName}.");
                continue;
            }

            var differs = new List<string>();
            AddDifference(differs, "document", sampled.DocId, token.DocId);
            AddDifference(differs, "source", sampled.Source, token.Source);
            AddDifference(differs, "sentence", sampled.SentenceId, token.SentenceId);
            AddDifference(differs, "target", sampled.Target.ToString(CultureInfo.InvariantCulture), token.Target.ToString(CultureInfo.InvariantCulture));
            AddDifference(differs, "form", sampled.Form, token.Form);
            AddDifference(differs, "strata", sampled.Strata, token.Strata);
            AddDifference(differs, "words", string.Join(' ', sampled.Words), string.Join(' ', token.Words));
            if (differs.Count > 0)
            {
                problems.Add($"{file}: the row '{token.Id}' differs from {ManifestName} in {string.Join(", ", differs)}.");
            }
        }

        foreach (var id in expected.Keys.Where(id => !seen.Contains(id)))
        {
            problems.Add($"{file}: the token '{id}' of {ManifestName} has no row.");
        }

        return problems;
    }

    private static void AddDifference(List<string> differences, string name, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            differences.Add($"the {name} ('{expected}' against '{actual}')");
        }
    }

    /// <summary>
    ///     Reads the labeled rows of a split and checks them against the manifest.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The rows in file order, or an empty list when the split has neither rows nor manifest tokens.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed or the rows do not match the manifest one to one (a missing, duplicate, unknown or different row).</exception>
    public static List<Gold2Row> ReadVerifiedRows(string corpus, string split)
    {
        var rows = ReadRows(corpus, split);
        var manifest = ReadManifest(corpus);
        if (rows.Count == 0 && !manifest.Any(t => t.Split == split))
        {
            return rows;
        }

        var problems = ValidateRows(manifest, rows, split);
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"The rows of the {split} split do not match {ManifestName} ({problems.Count.ToString("N0", CultureInfo.InvariantCulture)} problems; first: {string.Join(" ", problems.Take(3))}).");
        }

        return rows;
    }

    /// <summary>
    ///     Finds the lemmas that count as trained when the models are evaluated: the lemmas that have weights in every shipped
    ///     encoder model (a model answers the sense of any other lemma with the usage prior alone).
    /// </summary>
    /// <remarks>
    ///     When no weights file is present (a source tree without the packages), the lemmas that would get weights from the
    ///     anchors of the corpus are used (<see cref="Corpus.ReadWeightedLemmas"/>, the rule of <see cref="WeightsTrainer"/>).
    /// </remarks>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The lemmas and a sentence that says where they come from.</returns>
    public static (HashSet<string> Lemmas, string Basis) TrainedLemmas(string repository, string corpus)
    {
        HashSet<string>? lemmas = null;
        var names = new List<string>();
        foreach (var info in ModelCatalog.All.Where(m => m.IsEncoder))
        {
            var path = info.WeightsPath(repository);
            if (!File.Exists(path))
            {
                continue;
            }

            var set = WeightsBlob.Read(File.ReadAllBytes(path)).Lemmas.Select(l => l.Lemma).ToHashSet(StringComparer.Ordinal);
            if (lemmas is null)
            {
                lemmas = set;
            }
            else
            {
                lemmas.IntersectWith(set);
            }

            names.Add(info.Name);
        }

        if (lemmas is not null)
        {
            return (lemmas, $"{lemmas.Count.ToString("N0", CultureInfo.InvariantCulture)} lemmas that have weights in the shipped models ({string.Join(", ", names)})");
        }

        var fromAnchors = Corpus.ReadWeightedLemmas(Path.Join(corpus, "anchors"));
        return (fromAnchors, $"{fromAnchors.Count.ToString("N0", CultureInfo.InvariantCulture)} lemmas with sentences for two or more senses in corpus/anchors (no weights file found)");
    }
}
