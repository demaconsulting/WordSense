using WordSense.Builder.Data;
using PartOfSpeech = DemaConsulting.WordSense.PartOfSpeech;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the logistic trainer, the evaluator, the appendix and the committed data.
/// </summary>
public sealed class BuilderTests : IDisposable
{
    private static readonly string[] FunctionWords = ["the", "a", "an", "his", "albeit", "although", "alas", "ah", "two"];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     The trainer separates linearly separable data and is deterministic.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic()
    {
        var features = new List<float[]>();
        var labels = new List<bool>();
        for (var i = 0; i < 20; i++)
        {
            var offset = i * 0.01f;
            features.Add([1f + offset, 0.1f]);
            labels.Add(true);
            features.Add([-1f - offset, 0.1f]);
            labels.Add(false);
        }

        var first = LogisticTrainer.Train(features, labels, 100);
        var second = LogisticTrainer.Train(features, labels, 100);
        Assert.Equal(first.Weights, second.Weights);
        Assert.Equal(first.Bias, second.Bias);
        for (var i = 0; i < features.Count; i++)
        {
            var theta = first.Weights.Append(first.Bias).ToArray();
            Assert.Equal(labels[i], LogisticTrainer.Activation(features[i], theta) > 0);
        }
    }

    /// <summary>
    ///     The evaluation renders the share answered at each level and the accuracy of each level per set, with n/a for
    ///     empty sets, and the three measurements.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EvaluationResult_Render_FormatsSetsAndMeasurements()
    {
        var result = new EvaluationResult(
            "demo",
            [Set("holdout", 4, new(2, 1), new(1, 1), new(1, 1), 0, 2, 4, 2, 4, 3), Set("empty", 0, default, default, default, 0, 0, 0, 0, 0, 0)],
            2048,
            1.5,
            12,
            2097152);

        var text = result.Render();

        Assert.StartsWith("### demo\n\nShare of the sentences answered at each level:\n\n| Set | Rows | Sense | Group | Part of speech | None |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 4 | 50.00% | 25.00% | 25.00% | 0.00% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| empty | 0 | n/a | n/a | n/a | n/a |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 50.00% | 100.00% | 100.00% | 75.00% | 50.00% | 50.00% | 100.00% | 75.00% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| empty | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a |\n", text, StringComparison.Ordinal);
        Assert.Contains("- Weights size: 2 KiB\n", text, StringComparison.Ordinal);
        Assert.Contains("- Classification latency: 1.50 ms per sentence\n", text, StringComparison.Ordinal);
        Assert.Contains("- Word table first use: 12 ms, 2.0 MiB\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    /// <summary>
    ///     An encoder evaluation renders the confidence calibration buckets and the threshold table of its sets.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EvaluationResult_Render_EncoderModel_IncludesCalibrationAndThresholds()
    {
        (double Confidence, bool Correct)[] outcomes = [(0.95, true), (0.85, true), (0.65, false), (0.2, false)];
        var result = new EvaluationResult(
            "demo",
            [Set("holdout", 4, new(2, 2), default, new(2, 0), 0, 2, 4, 2, 4, 2, outcomes)],
            2048,
            1.5,
            12,
            2097152);

        var text = result.Render();

        Assert.Contains("#### Confidence calibration (rows; correct at the answered level)\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 1 / 0.00% | 0 / n/a | 1 / 0.00% | 0 / n/a | 1 / 100.00% | 1 / 100.00% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 0.6 | 75.00% | 66.67% |\n", text, StringComparison.Ordinal);
        Assert.Contains("| holdout | 0.9 | 25.00% | 100.00% |\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#### Comparison", text, StringComparison.Ordinal);
    }

    private static SetResult Set(string name, int rows, LevelCount sense, LevelCount group, LevelCount pos, int none, int fineTop1, int ranked, int fineTop1OfRanked, int posCorrect, int analysisCorrect, IReadOnlyList<(double Confidence, bool Correct)>? outcomes = null) =>
        new(name, rows, sense, group, pos, none, fineTop1, ranked, fineTop1OfRanked, posCorrect, analysisCorrect, outcomes);

    /// <summary>
    ///     The statistical model is evaluated on the holdout without any model files; the result has one set, the
    ///     holdout, and counts every row at one of the levels the Basic model answers.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Evaluator_RunAsync_BasicModel_CountsHoldoutRows()
    {
        var verb = SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];
        var noun = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        Write("corpus/holdout/h.tsv", $"walk\t{verb.SenseId}\tVerb\tYou should {{walk}} more.\nwalk\t{noun.SenseId}\tNoun\tThe {{walk}} was long.\n");
        using var progress = new StringWriter();

        var result = await Evaluator.RunAsync(_dir.Root, Path.Join(_dir.Root, "corpus"), ModelCatalog.Find("basic"), progress, TestContext.Current.CancellationToken);

        var set = Assert.Single(result.Sets);
        Assert.Equal("holdout", set.Name);
        Assert.Equal(2, set.Rows);
        Assert.Equal(2, set.Sense.Answered + set.Group.Answered + set.PartOfSpeech.Answered + set.None);
        Assert.Equal(0, set.Group.Answered);
        Assert.Equal(0, result.WeightsBytes);
        Assert.Contains("holdout done", progress.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The appendix explains the parts of speech, groups and forms, lists the trained lemmas
    ///     alphabetically with their parts of speech and forms, keeps every line within the limit, escapes markdown
    ///     characters, leaves out a trained lemma that has no sense in the data and is byte-identical on repeat.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Generate_ListsLemmasSensesAndForms()
    {
        // Arrange: two trained lemmas written in reverse order, one form, and a trained lemma the data does not have
        var (corpus, data) = WriteAppendixInput();

        // Act
        var text = AppendixGenerator.Generate(corpus, data);
        var again = AppendixGenerator.Generate(corpus, data);

        // Assert
        Assert.Equal(text, again);
        Assert.StartsWith("# Appendix: Supported Senses\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", text, StringComparison.Ordinal);
        Assert.All(text.Split('\n'), line => Assert.True(line.Length <= AppendixGenerator.LineLimit, line));
        Assert.Contains("### Groups\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pronunciations", text, StringComparison.Ordinal);
        Assert.Contains("### Forms\n", text, StringComparison.Ordinal);
        Assert.Contains($"| {"`Noun`",-14} | {1,6} | {4,6} |", text, StringComparison.Ordinal);
        Assert.Contains($"| {"`Verb`",-14} | {1,6} | {2,6} |", text, StringComparison.Ordinal);
        Assert.Contains($"| {"`Determiner`",-14} | {1,6} | {1,6} |", text, StringComparison.Ordinal);
        Assert.Contains("The library supports 2 lemmas with 7 senses in total", text, StringComparison.Ordinal);
        Assert.Contains("not listed: `zzyzx`.", text.ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.True(text.IndexOf("- **bank**;", StringComparison.Ordinal) < text.IndexOf("- **the**;", StringComparison.Ordinal));
        Assert.Contains(
            "- **bank**; parts of speech: noun, verb; forms: bank (noun, Number=Sing), bank (verb, VerbForm=Inf), banks (noun, Number=Plur)",
            System.Text.RegularExpressions.Regex.Replace(text, "\n {2,}", " "),
            StringComparison.Ordinal);
        Assert.Contains("- **the**; parts of speech: determiner; forms: the\n", text, StringComparison.Ordinal);
        Assert.Contains("### Lemmas Starting With B\n", text, StringComparison.Ordinal);
        Assert.Contains("### Lemmas Starting With T\n", text, StringComparison.Ordinal);
        Assert.Contains(Unwrapped(text), line => line.Contains("gloss: a financial institution that accepts deposits \\*and\\* lends money; a depository", StringComparison.Ordinal));
        Assert.Contains(text.Split('\n'), line => line.StartsWith("    ", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Each sense of a trained lemma is listed in inventory order (the order of Open English WordNet, the position of the
    ///     sense in its entry) with its id, group (none when it has no group),
    ///     OEWN sense key (a project sense has none), and gloss.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Generate_ListsSensesWithGroupKeyAndGloss()
    {
        var (corpus, data) = WriteAppendixInput();

        var text = AppendixGenerator.Generate(corpus, data);

        var senses = Unwrapped(text).Where(l => l.StartsWith("  - `", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "  - `bank#coin-bank` (noun); group: `bank#group:depository`; key: `bank%1:06:01::`; gloss: a container for coins",
                "  - `bank#banking-company` (noun); group: `bank#group:depository`; key: `bank%1:14:00::`; gloss: a financial institution that accepts deposits \\*and\\* lends money; a depository",
                "  - `bank#ridge` (noun); group: none; key: `bank%1:17:00::`; gloss: a long ridge of land",
                "  - `bank#incline` (noun); group: none; key: `bank%1:17:01::`; gloss: sloping land beside a body of water",
                "  - `bank#close-in` (verb); group: none; key: `bank%2:35:00::`; gloss: tip laterally",
                "  - `bank#cover` (verb); group: none; key: `bank%2:35:01::`; gloss: cover with ashes",
                "  - `the#determiner` (determiner); group: none; key: project sense; gloss: the definite article",
            ],
            senses);
    }

    // Joins the continuation lines of the wrapped list items
    private static List<string> Unwrapped(string text)
    {
        var items = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("    ", StringComparison.Ordinal) && items.Count > 0)
            {
                items[^1] += " " + line.Trim();
            }
            else
            {
                items.Add(line);
            }
        }

        return items;
    }

    // Writes a small data folder with glosses, a project sense and a form, and a corpus with two trained
    // lemmas written in reverse order and one that the data does not have
    private (string Corpus, string Data) WriteAppendixInput()
    {
        var data = ImageCompilerTests.WriteSmallData(_dir.Root);
        Write("data/entry/t.tsv", "entry_id\tlemma\tpos\thomograph_no\ttoken_count\thead_idx\tsource_id\ndema-the-d\tthe\td\t0\t1\t1\tdema\n");
        Write("data/sense/t.tsv", "sense_id\tsense_key\tentry_id\tsynset_id\tposition\ndema-the__d\t\tdema-the-d\tdema-1-d\t1\n");
        Write("data/sense_label/t.tsv", "sense_id\tlabel\torigin\ndema-the__d\tdeterminer\tproject\n");
        Write("data/form/t.tsv", "spelling\trank\tentry_id\tfeatures\tsource\treviewed\tsurface_lemma\tsurface_pos\torigin\n" +
            "banks\t0\toewn-bank-n\tNumber=Plur\tdema\t0\t\t\toewn_form\nthe\t0\tdema-the-d\t\tdema\t0\t\t\tdema\n");
        Write("data/gloss/g.tsv", "synset_id\tseq\ttext\n" +
            "oewn-1-n\t1\ta container for coins\n" +
            "oewn-2-n\t1\ta financial institution that accepts deposits *and* lends money\n" +
            "oewn-2-n\t2\ta depository\n" +
            "oewn-3-n\t1\ta long ridge of land\n" +
            "oewn-4-n\t1\tsloping land beside a body of water\n" +
            "oewn-5-v\t1\ttip laterally\n" +
            "oewn-6-v\t1\tcover with ashes\n" +
            "dema-1-d\t1\tthe definite article\n");
        Write("corpus/anchors/a.tsv", "the\tthe#determiner\tDeterminer\tThe {the} end.\nbank\tbank#ridge\tNoun\tA {bank} here.\nbank\tbank#cover\tVerb\tThey {bank} it.\nzzyzx\tzzyzx#noun\tNoun\tA {zzyzx} appears.\n");
        Write("corpus/curation/heteronyms.tsv", "level\tgroup\tclass\tpattern\tnote\n");
        return (Path.Join(_dir.Root, "corpus"), data);
    }

    /// <summary>
    ///     The generator rejects a missing directory, anchors without a lemma of the data and a sense without a gloss.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Generate_RejectsIncompleteInput()
    {
        // Arrange: the corpus has no anchors directory and the data folder is missing
        var corpus = Path.Join(_dir.Root, "corpus");
        var data = Path.Join(_dir.Root, "data");

        // Act and assert: missing directories
        var missing = Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus, data));
        Assert.Contains("anchors", missing.Message, StringComparison.Ordinal);
        Assert.Contains("was not found", missing.Message, StringComparison.Ordinal);

        // Act and assert: no anchors
        Write("corpus/anchors/a.tsv", string.Empty);
        data = ImageCompilerTests.WriteSmallData(_dir.Root);
        Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus, data));

        // Act and assert: no trained lemma has a sense in the data
        Write("corpus/anchors/a.tsv", "zzyzx\tzzyzx#noun\tNoun\tA {zzyzx} appears.\n");
        var none = Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus, data));
        Assert.Contains("None of the trained lemmas", none.Message, StringComparison.Ordinal);

        // Act and assert: the senses of bank have no gloss in the small data folder
        Write("corpus/anchors/a.tsv", "bank\tbank#ridge\tNoun\tA {bank} here.\n");
        var noGloss = Assert.Throws<InvalidDataException>(() => AppendixGenerator.Generate(corpus, data));
        Assert.Contains("'bank#ridge' has no gloss", noGloss.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The committed user-guide appendix equals what the generator produces from the committed corpus and data, so
    ///     it cannot go stale: regenerate it with <c>appendix</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Appendix_Committed_MatchesCorpus()
    {
        var root = FindRepositoryRoot();
        var committedPath = Path.Join(root.FullName, "docs", "user_guide", "appendix-senses.md");
        var regenerated = Path.Join(_dir.Root, "regenerated", "appendix-senses.md");

        AppendixGenerator.Write(Path.Join(root.FullName, "corpus"), Path.Join(root.FullName, "data"), regenerated);

        // Line endings are normalized, so a checkout with CRLF conversion does not matter
        var expected = File.ReadAllText(regenerated).ReplaceLineEndings("\n");
        var committed = File.ReadAllText(committedPath).ReplaceLineEndings("\n");
        Assert.True(
            expected == committed,
            "docs/user_guide/appendix-senses.md is stale. Run: dotnet run --project tools/WordSense.Builder -- appendix");
    }

    /// <summary>
    ///     The committed image was compiled from the committed data tables and corpus: its data hash equals the hash of
    ///     the current inputs, and its counts equal those of a fresh compilation, so it cannot go stale. Regenerate it
    ///     with <c>compile-image</c>.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_MatchesData()
    {
        var root = FindRepositoryRoot();
        var committed = File.ReadAllBytes(Path.Join(root.FullName, ImageCompiler.DefaultOutput.Replace('/', Path.DirectorySeparatorChar)));

        var (image, result) = ImageCompiler.Build(Path.Join(root.FullName, "data"));

        var open = DemaConsulting.WordSense.WordImage.Open(committed);
        var fresh = DemaConsulting.WordSense.WordImage.Open(image);
        Assert.True(
            open.DataHash.SequenceEqual(ImageCompiler.DataHash(Path.Join(root.FullName, "data"))),
            "src/DemaConsulting.WordSense/Resources/wordsense.img is stale; run the 'compile-image' command.");
        Assert.Equal(result.Spellings, open.SpellingCount);
        Assert.Equal(result.Entries, open.EntryCount);
        Assert.Equal(result.Senses, open.SenseCount);
        Assert.Equal(result.Groups, open.GroupCount);
        Assert.True(open.InventoryHash.SequenceEqual(fresh.InventoryHash));
        foreach (var id in fresh.SectionIds)
        {
            Assert.True(fresh.RawSection(id).AsSpan().SequenceEqual(open.RawSection(id)), $"Section {id} of the committed image differs from a fresh compilation.");
        }
    }

    /// <summary>
    ///     Every word of the committed dictionary list is a key of the embedded word table: the accuracy gate never decides
    ///     whether a word exists, so determiners, conjunction-only and interjection-only words and the words that a trained
    ///     lemma or form spells are all present, and a trained word keeps its trained senses first.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_HoldsEveryDictionaryWord()
    {
        var root = FindRepositoryRoot();
        var lexicon = LexiconSource.ReadWords(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv"));
        var table = new LexiconView();

        var missing = lexicon.Where(w => !table.ContainsKey(w.Word)).Select(w => w.Word).Take(10).ToList();
        Assert.True(missing.Count == 0, "Words of lexicon.tsv missing from the word image: " + string.Join(", ", missing));
        Assert.True(lexicon.Count > 143_000);

        // Every part of speech the dictionary lists for a word is among the senses of that word, without exception, except
        // for the analyses that the forms audit dropped in form-corrections.tsv: our own sources show
        // the analysis wrong (a plural of a verb-only word, a past spelled with one consonant), so the word list entry
        // is not made true by a wrong analysis
        var dropped = File.ReadLines(Path.Join(root.FullName, "corpus", "lexicon", "form-corrections.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'))
            .Where(p => p.Length > 4 && p[4] == "drop")
            .Select(p => p[0])
            .ToHashSet(StringComparer.Ordinal);
        var lost = lexicon
            .Where(w => !dropped.Contains(w.Word))
            .Where(w => w.Letters.Any(l => table[w.Word].All(d => (int)d.PartOfSpeech != LexiconSource.Letters.IndexOf(l, StringComparison.Ordinal))))
            .Select(w => w.Word)
            .Take(10)
            .ToList();
        Assert.True(lost.Count == 0, "Parts of speech of lexicon.tsv missing from the word image: " + string.Join(", ", lost));
        Assert.All(FunctionWords, w => Assert.True(table.ContainsKey(w), w));
    }

    /// <summary>
    ///     Every word of the committed closed list of auxiliaries has an Auxiliary analysis in the embedded word table,
    ///     and that analysis follows every other analysis of the word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_AuxiliariesGainTheirAnalysis()
    {
        var root = FindRepositoryRoot();
        var table = new LexiconView();
        var auxiliaries = LexiconSource.ReadAuxiliaries(Path.Join(root.FullName, "corpus", "lexicon", "auxiliaries.tsv"));

        var problems = auxiliaries
            .Where(w => IsWithoutLastAuxiliary(table[w]))
            .Take(10)
            .ToList();

        Assert.True(problems.Count == 0, "Auxiliaries without a last Auxiliary analysis: " + string.Join(", ", problems));
        Assert.True(auxiliaries.Count > 20);
    }

    /// <summary>
    ///     The catalog of the committed image holds every single-token Open English WordNet sense and every project sense
    ///     of the committed data whose lemma has a letter, each with the id <c>lemma#label</c>, the key of the data (none
    ///     for a project sense) and the group of the data (none for a sense that is in no group).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Words_Committed_CatalogHoldsEveryOewnAndProjectSense()
    {
        var data = Path.Join(FindRepositoryRoot().FullName, "data");
        var schema = WordSense.Builder.Data.DataSchema.Load(Path.Join(data, "schema.yaml"));
        var entries = WordSense.Builder.Data.DataTables.Read(data, schema.Table("entry")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var senses = WordSense.Builder.Data.DataTables.Read(data, schema.Table("sense")).ToDictionary(r => r[0], StringComparer.Ordinal);
        var labels = WordSense.Builder.Data.DataTables.Read(data, schema.Table("sense_label")).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var groups = WordSense.Builder.Data.DataTables.Read(data, schema.Table("group_sense")).ToDictionary(r => r[1], r => r[0], StringComparer.Ordinal);

        var missing = senses.Values
            .Where(s => entries[s[2]][4] == "1" || entries[s[2]][6] == "dema")
            .Where(s => !labels.ContainsKey(s[0]))
            .Select(s => s[0])
            .Take(10)
            .ToList();
        Assert.True(missing.Count == 0, "Senses of the data without a label: " + string.Join(", ", missing));

        // The numerals (a lemma without a letter) are left out of the image, so the catalog holds the labeled senses of the others
        var inImage = labels.Where(p => DemaConsulting.WordSense.CueModel.HasLetter(entries[senses[p.Key][2]][1])).ToList();
        var wrong = inImage
            .Select(p => (Sense: senses[p.Key], Label: p.Value))
            .Where(p =>
            {
                var lemma = entries[p.Sense[2]][1];
                return !DemaConsulting.WordSense.WordLexicon.TryGetSense(lemma + "#" + p.Label, out var found)
                    || found.SenseKey != (p.Sense[1].Length == 0 ? null : p.Sense[1])
                    || found.GroupId != groups.GetValueOrDefault(p.Sense[0]);
            })
            .Select(p => p.Sense[0])
            .Take(10)
            .ToList();
        Assert.True(wrong.Count == 0, "Senses of the data that the catalog lacks or has with other values: " + string.Join(", ", wrong));
        Assert.Equal(inImage.Count, DemaConsulting.WordSense.WordLexicon.Current.SenseCount);
    }

    /// <summary>
    ///     The Basic row of the README model table equals the measurement of the evaluator on the committed holdout set
    ///     (the sense cell and the size cells; the timing cells depend on the machine).
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation()
    {
        var root = FindRepositoryRoot();
        var committed = ModelTable.Read(Path.Join(root.FullName, "docs", "user_guide", "model-table.md"))["Basic"];
        using var progress = new StringWriter();

        var result = await Evaluator.RunAsync(root.FullName, Path.Join(root.FullName, "corpus"), ModelCatalog.Find("basic"), progress, TestContext.Current.CancellationToken);

        var measured = result.TableCells();
        foreach (var column in new[] { ModelTable.RealSenseColumn, ModelTable.DownloadColumn, ModelTable.WeightsColumn, ModelTable.NeededColumn })
        {
            Assert.Equal(measured[column], committed[column]);
        }
    }

    /// <summary>
    ///     A dictionary-style view of the word lexicon for the checks of this class.
    /// </summary>
    private sealed class LexiconView
    {
        /// <summary>
        ///     Gets the analyses of a spelling, or an empty array when the lexicon does not know it.
        /// </summary>
        /// <param name="word">The spelling.</param>
        /// <returns>The analyses.</returns>
        public System.Collections.Immutable.ImmutableArray<DemaConsulting.WordSense.WordDetails> this[string word] =>
            DemaConsulting.WordSense.WordLexicon.Lookup(word);

        /// <summary>
        ///     Checks whether the lexicon knows a spelling.
        /// </summary>
        /// <param name="word">The spelling.</param>
        /// <returns>True when the spelling is known.</returns>
        public bool ContainsKey(string word) => !this[word].IsDefaultOrEmpty;
    }

    /// <summary>
    ///     Tells whether a word fails the rule that the auxiliary analysis comes last: it has no auxiliary analysis, or
    ///     the last analysis of the lexicon is not the auxiliary one.
    /// </summary>
    /// <param name="analyses">The lexicon analyses of the word.</param>
    /// <returns>True when the word is without an auxiliary analysis in the last position.</returns>
    private static bool IsWithoutLastAuxiliary(System.Collections.Immutable.ImmutableArray<DemaConsulting.WordSense.WordDetails> analyses) =>
        analyses.IsDefaultOrEmpty
        || analyses.All(d => d.PartOfSpeech != PartOfSpeech.Auxiliary)
        || analyses[^1].PartOfSpeech != PartOfSpeech.Auxiliary;

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    private void Write(string relative, string content) => _dir.Write(relative, content);
}
