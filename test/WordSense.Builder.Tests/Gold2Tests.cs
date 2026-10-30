using System.Collections.Immutable;
using System.Text.Json;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the second all-words gold set (<c>corpus/allwords/v2</c>): the power calculation, the strata, the draw, the
///     files, the labeling and its merge, the measurement of a model and the paired comparison.
/// </summary>
public sealed class Gold2Tests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private static readonly string Corpus = Path.Join(DataFixture.RepositoryRoot(), "corpus");

    private static readonly string DataFolder = Path.Join(DataFixture.RepositoryRoot(), "data");

    private static readonly HashSet<string> NoLemmas = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private static Gold2Token Token(string id, string split, string doc, string family, params string[] words) =>
        new(id, split, doc, family, doc + "-s0001", 1, words[1], Gold2Strata.Control, words);

    /// <summary>
    ///     The sample size of a paired comparison follows the formula of the plan: the figures of the plan in the README.
    /// </summary>
    [Fact]
    public void Gold2Power_PairedSampleSize_PlannedMeasures_MatchesThePlan()
    {
        // Act
        var sizes = Gold2Power.Measures.Select(m => Gold2Power.PairedSampleSize(m.Discordance, m.Mdi)).ToArray();

        // Assert
        Assert.Equal([2745, 1175, 626, 438, 375], sizes);
        Assert.All(Gold2Power.Measures, m => Assert.True(m.PlannedTest >= Gold2Power.PairedSampleSize(m.Discordance, m.Mdi) * 0.8));
    }

    /// <summary>
    ///     The smallest detectable improvement is the inverse of the sample size, and the normal quantile is right.
    /// </summary>
    [Fact]
    public void Gold2Power_MinimumDetectable_IsTheInverseOfTheSampleSize()
    {
        // Act
        var delta = Gold2Power.MinimumDetectable(1175, 0.24);

        // Assert
        Assert.Equal(0.04, delta, 3);
        Assert.Equal(1.959964, Gold2Power.Quantile(0.975), 5);
        Assert.Equal(0.841621, Gold2Power.Quantile(0.8), 5);
        Assert.Equal(-1.959964, Gold2Power.Quantile(0.025), 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => Gold2Power.PairedSampleSize(0.04, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gold2Power.Quantile(1));
    }

    /// <summary>
    ///     The exact McNemar p value is 1 without discordant tokens and for an even split, and small for a lopsided one.
    /// </summary>
    [Fact]
    public void Gold2Power_ExactMcNemar_KnownSplits_GivesTheExactProbability()
    {
        // Act / Assert
        Assert.Equal(1.0, Gold2Power.ExactMcNemar(0, 0));
        Assert.Equal(1.0, Gold2Power.ExactMcNemar(5, 5));
        Assert.Equal(2.0 / 1024, Gold2Power.ExactMcNemar(10, 0), 10);
        Assert.Equal(Gold2Power.ExactMcNemar(3, 9), Gold2Power.ExactMcNemar(9, 3), 12);
        Assert.Contains("Part of speech", Gold2Power.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A form gets the strata of the lexicon and the anchors: a same-spelling verb, a noun and a verb, and a polysemous
    ///     lemma that is untrained or trained.
    /// </summary>
    [Fact]
    public void Gold2Strata_Of_Forms_FlagsFollowTheLexiconAndTheTrainedLemmas()
    {
        // Arrange
        var trained = new HashSet<string>(["bank"], StringComparer.Ordinal);
        var heteronyms = new HashSet<string>(["bow"], StringComparer.Ordinal);

        // Act
        var put = Gold2Strata.Of("put", NoLemmas, NoLemmas);
        var bank = Gold2Strata.Of("bank", trained, NoLemmas);
        var bankUntrained = Gold2Strata.Of("bank", NoLemmas, NoLemmas);
        var bow = Gold2Strata.Of("bow", NoLemmas, heteronyms);
        var saw = Gold2Strata.Of("saw", NoLemmas, NoLemmas);
        var unknown = Gold2Strata.Of("qzxvj", NoLemmas, NoLemmas);

        // Assert
        Assert.Contains(Gold2Strata.Tense, put!);
        Assert.Contains(Gold2Strata.NounVerb, bank!);
        Assert.Contains(Gold2Strata.Trained, bank!);
        Assert.DoesNotContain(Gold2Strata.Untrained, bank!);
        Assert.Contains(Gold2Strata.Untrained, bankUntrained!);
        Assert.Contains(Gold2Strata.Heteronym, bow!);
        Assert.Contains(Gold2Strata.Lemma, saw!);
        Assert.Null(unknown);
        Assert.Equal("technical", Gold2Strata.GroupOf("usgs"));
        Assert.Equal("news", Gold2Strata.GroupOf("voa"));
        Assert.Equal("literary", Gold2Strata.GroupOf("gutenberg"));
    }

    /// <summary>
    ///     Only lower-case words, sentence-initial words and <c>I</c> can be drawn: names, acronyms and non-words cannot.
    /// </summary>
    [Fact]
    public void Gold2Sampler_IsCandidateWord_NamesAcronymsAndSymbols_AreLeftOut()
    {
        // Act / Assert
        Assert.True(Gold2Sampler.IsCandidateWord("bank", 3));
        Assert.True(Gold2Sampler.IsCandidateWord("The", 0));
        Assert.True(Gold2Sampler.IsCandidateWord("I", 4));
        Assert.True(Gold2Sampler.IsCandidateWord("a", 2));
        Assert.True(Gold2Sampler.IsCandidateWord("A", 0));
        Assert.False(Gold2Sampler.IsCandidateWord("D", 5));
        Assert.False(Gold2Sampler.IsCandidateWord("Y", 13));
        Assert.False(Gold2Sampler.IsCandidateWord("Paris", 3));
        Assert.False(Gold2Sampler.IsCandidateWord("USGS", 0));
        Assert.False(Gold2Sampler.IsCandidateWord("don't", 2));
        Assert.False(Gold2Sampler.IsCandidateWord("1990", 2));
        Assert.False(Gold2Sampler.IsCandidateWord(",", 2));
    }

    private static List<Gold2Sentence> Pool()
    {
        var sentences = new List<Gold2Sentence>();
        string[] families = ["voa", "usgs", "gutenberg"];
        for (var d = 0; d < 30; d++)
        {
            var family = families[d % 3];
            for (var s = 1; s <= 6; s++)
            {
                var doc = $"{family}-{d:D3}";
                sentences.Add(new Gold2Sentence($"{doc}-s{s:D4}", doc, family, ["We", "put", "the", "bank", "on", "the", "bow", "and", "read", "it", "slowly", "."]));
            }
        }

        return sentences;
    }

    private static Gold2Plan SmallPlan() => Gold2Plan.Default with
    {
        TestQuotas = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Gold2Strata.Tense] = 12,
            [Gold2Strata.Lemma] = 0,
            [Gold2Strata.Heteronym] = 0,
            [Gold2Strata.NounVerb] = 12,
            [Gold2Strata.Untrained] = 18,
            [Gold2Strata.Trained] = 0,
            [Gold2Strata.Control] = 6,
        },
    };

    /// <summary>
    ///     The draw never puts a document in both splits, respects the caps of a sentence, a document and a form, is the same
    ///     for the same seed and fills the quotas it can.
    /// </summary>
    [Fact]
    public void Gold2Sampler_Draw_SmallPool_RespectsSplitsCapsAndQuotas()
    {
        // Arrange
        var pool = Pool();
        var plan = SmallPlan();

        // Act
        var first = Gold2Sampler.Draw(pool, plan, NoLemmas, NoLemmas, 1);
        var again = Gold2Sampler.Draw(pool, plan, NoLemmas, NoLemmas, 1);
        var other = Gold2Sampler.Draw(pool, plan, NoLemmas, NoLemmas, 2);

        // Assert
        Assert.Equal(first.Select(t => t.SentenceId + t.Target), again.Select(t => t.SentenceId + t.Target));
        Assert.NotEqual(first.Select(t => t.SentenceId + t.Target), other.Select(t => t.SentenceId + t.Target));
        Assert.DoesNotContain(first.GroupBy(t => t.DocId), g => g.Select(t => t.Split).Distinct().Count() > 1);
        Assert.All(first.GroupBy(t => t.DocId), g => Assert.True(g.Count() <= plan.DocumentCapOf(g.First().Source)));
        Assert.All(first.GroupBy(t => t.SentenceId), g =>
        {
            Assert.True(g.Count() <= plan.PerSentence);
            Assert.All(g.Select(t => t.Target).Order().Zip(g.Select(t => t.Target).Order().Skip(1)), p => Assert.True(p.Second - p.First > 1));
        });
        Assert.All(first, t => Assert.Equal(t.Form, t.Words[t.Target]));
        Assert.Equal(first.Count, first.Select(t => t.Id).Distinct().Count());
        var test = Gold2Sampler.CountStrata(first.Where(t => t.Split == Gold2.Test));
        Assert.True(test[Gold2Strata.Tense] >= 12);
        Assert.True(test[Gold2Strata.Untrained] >= 18);
        Assert.StartsWith("d-", first.First(t => t.Split == Gold2.Dev).Id, StringComparison.Ordinal);
        Assert.StartsWith("t-", first.First(t => t.Split == Gold2.Test).Id, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The split of the documents gives each family about its share to the development split and never splits a document.
    /// </summary>
    [Fact]
    public void Gold2Sampler_AssignSplits_ByFamily_GivesTheDevelopmentShareOfTheSentences()
    {
        // Arrange
        var pool = Pool();

        // Act
        var splits = Gold2Sampler.AssignSplits(pool, 0.4, 1);

        // Assert
        Assert.Equal(30, splits.Count);
        foreach (var family in new[] { "voa", "usgs", "gutenberg" })
        {
            var docs = splits.Where(p => p.Key.StartsWith(family, StringComparison.Ordinal)).ToList();
            Assert.Equal(4, docs.Count(p => p.Value == Gold2.Dev));
            Assert.Equal(6, docs.Count(p => p.Value == Gold2.Test));
        }
    }

    /// <summary>
    ///     The manifest, the documents and the labeled rows are written and read back the same.
    /// </summary>
    [Fact]
    public void Gold2_Files_RoundTrip()
    {
        // Arrange
        var token = new Gold2Token("t-0001", Gold2.Test, "voa-1", "voa", "voa-1-s0002", 2, "bank", "NV,U", ["The", "river", "bank", "."]);
        var document = new Gold2Document("voa-1", "voa", "A title", "https://example.org/1", "Staff", "2025", "basis", "https://example.org/terms", "2026-10-29", "ab", "cd", 12);
        var row = new Gold2Row(token, document.TermsUrl, "Noun", "bank", "Number=Sing", "-", "bank#slope", "adjudicated", "pos");
        var corpus = _dir.Root;
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);

        // Act
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest([token]));
        File.WriteAllText(Path.Join(folder, Gold2.SourcesName), Gold2.RenderDocuments([document]));
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [row]));
        var manifest = Gold2.ReadManifest(corpus);
        var documents = Gold2.ReadDocuments(corpus);
        var rows = Gold2.ReadRows(corpus, Gold2.Test);

        // Assert
        Assert.Equal(token.Id, Assert.Single(manifest).Id);
        Assert.Equal(document, Assert.Single(documents));
        var read = Assert.Single(rows);
        Assert.Equal((row.Pos, row.Lemma, row.Features, row.Sense, row.Status, row.Agreement, row.Terms), (read.Pos, read.Lemma, read.Features, read.Sense, read.Status, read.Agreement, read.Terms));
        Assert.Equal(token.Words, read.Token.Words);
        Assert.Single(Gold2.LeakageSets(corpus));
        Assert.Empty(Gold2.ReadRows(corpus, Gold2.Dev));
    }

    /// <summary>
    ///     A manifest line whose word is not at its index is an error.
    /// </summary>
    [Fact]
    public void Gold2_ReadManifest_WrongWordAtTheIndex_Throws()
    {
        // Arrange
        var path = _dir.Write("bad.tsv", "t-0001\ttest\tvoa-1\tvoa\tvoa-1-s0001\t1\tbank\tC\tthe river bank .\n");

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => Gold2.ReadManifestFile(path));
    }

    /// <summary>
    ///     The document check finds a document that another part of the repository uses, a token whose document is not listed
    ///     and a document in both splits.
    /// </summary>
    [Fact]
    public void Gold2_ValidateDocuments_UsedMissingAndSharedDocuments_AreProblems()
    {
        // Arrange
        var corpus = _dir.Root;
        _dir.Write("open/sources.tsv", "source_id\tfamily\ttitle\turl\nvoa-1\tvoa\tt\thttps://example.org/used\n");
        _dir.Write("provenance/x.tsv", "id\tsplit\tsource\tsource_key\nabc\tanchors\tfaa\tfaa-9#s12\n");
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        var docs = new[]
        {
            new Gold2Document("voa-1", "voa", "t", "https://example.org/1", string.Empty, string.Empty, "b", "u", "d", "a", "b", 1),
            new Gold2Document("voa-2", "voa", "t", "https://example.org/used", string.Empty, string.Empty, "b", "u", "d", "a", "b", 1),
            new Gold2Document("voa-3", "voa", "t", "https://example.org/3", string.Empty, string.Empty, "b", "u", "d", "a", "b", 1),
        };
        File.WriteAllText(Path.Join(folder, Gold2.SourcesName), Gold2.RenderDocuments(docs));
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest(
        [
            Token("d-1", "dev", "voa-3", "voa", "A", "bank", "x"),
            Token("t-1", "test", "voa-3", "voa", "A", "bank", "x"),
            Token("t-2", "test", "voa-9", "voa", "A", "bank", "x"),
        ]));

        // Act
        var problems = Gold2.ValidateDocuments(corpus);

        // Assert
        Assert.Contains(problems, p => p.Contains("'voa-1'", StringComparison.Ordinal) && p.Contains("already used", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("https://example.org/used", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'voa-9'", StringComparison.Ordinal) && p.Contains("does not list", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'voa-3'", StringComparison.Ordinal) && p.Contains("both splits", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The committed manifest, when it exists, is document-disjoint from the rest of the repository and between its splits.
    /// </summary>
    [Fact]
    public void Gold2_CommittedSet_DocumentsAreDisjointFromTheRepository()
    {
        // Act
        var problems = Gold2.ValidateDocuments(Corpus);

        // Assert
        Assert.Empty(problems);
    }

    /// <summary>
    ///     The committed labeled rows are valid: a part of speech of the lexicon, a lemma, a sense of the lemma and part of
    ///     speech when one is given, a status, and a tense only for a verb.
    /// </summary>
    [Fact]
    public void Gold2_CommittedRows_AreConsistentWithTheLexiconAndTheManifest()
    {
        // Arrange
        var manifest = Gold2.ReadManifest(Corpus).ToDictionary(t => t.Id, StringComparer.Ordinal);

        // Act
        var rows = new[] { Gold2.Dev, Gold2.Test }.SelectMany(s => Gold2.ReadRows(Corpus, s)).ToList();

        // Assert
        Assert.Equal(manifest.Count, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal(manifest[row.Token.Id].Strata, row.Token.Strata);
            Assert.True(row.Pos == "Other" || Enum.TryParse<PartOfSpeech>(row.Pos, false, out _), row.Token.Id);
            Assert.NotEmpty(row.Lemma);
            Assert.Contains(row.Status, new[] { "agreed", "adjudicated" });
            Assert.Contains(row.Tense, new[] { "past", "nonpast", "-" });
            Assert.True(row.Tense == "-" || row.Pos == "Verb", row.Token.Id);
            if (row.Sense is not ("-" or "none"))
            {
                Assert.Contains(row.Sense, WordLexicon.SensesOf(row.Lemma, Enum.Parse<PartOfSpeech>(row.Pos)).Select(s => s.SenseId));
            }
        }
    }

    /// <summary>
    ///     The analyses of a form come in a fixed order without any frequency, and an answer resolves to its analysis, with a
    ///     sense of that analysis; an invalid answer is named.
    /// </summary>
    [Fact]
    public void Gold2Labeling_Resolve_Answers_ResolveToTheAnalysisAndItsSense()
    {
        // Arrange
        var token = Token("t-1", "test", "voa-1", "voa", "We", "read", "it");
        var analyses = Gold2Labeling.AnalysesOf("read");
        var past = analyses.FindIndex(a => a.PartOfSpeech == PartOfSpeech.Verb && a.Features.Contains("Tense=Past", StringComparison.Ordinal)) + 1;
        var sense = WordLexicon.SensesOf("read", PartOfSpeech.Verb)[0].SenseId;

        // Act
        var good = Gold2Labeling.Resolve(token, new Gold2Answer("a" + past, sense, true), false, out var label);
        var unsure = Gold2Labeling.Resolve(token, new Gold2Answer("a" + past, "unsure", false), true, out var unsureAsAdjudicator);
        var none = Gold2Labeling.Resolve(token, new Gold2Answer("a" + past, "none", true), true, out var noneLabel);
        var noneByLabeler = Gold2Labeling.Resolve(token, new Gold2Answer("a" + past, "none", true), false, out _);
        var otherAnalysis = Gold2Labeling.Resolve(token, new Gold2Answer("other:Noun:reading", "-", true), false, out var other);
        var badIndex = Gold2Labeling.Resolve(token, new Gold2Answer("a99", "-", true), false, out _);
        var badSense = Gold2Labeling.Resolve(token, new Gold2Answer("a" + past, "read#nonexistent", true), false, out _);
        var badOther = Gold2Labeling.Resolve(token, new Gold2Answer("other:Planet:x", "-", true), false, out _);

        // Assert
        Assert.True(analyses.Count >= 2);
        Assert.Equal(analyses.OrderBy(a => a.Lemma, StringComparer.Ordinal).ThenBy(a => a.PartOfSpeech).ThenBy(a => a.Features, StringComparer.Ordinal), analyses);
        Assert.Null(good);
        Assert.Equal(("Verb", "read", sense), (label!.Pos, label.Lemma, label.Sense));
        Assert.True(label.Scored);
        Assert.Null(unsure);
        Assert.Equal("none", unsureAsAdjudicator!.Sense);
        Assert.Null(none);
        Assert.Equal("none", noneLabel!.Sense);
        Assert.NotNull(noneByLabeler);
        Assert.Null(otherAnalysis);
        Assert.Equal(("Noun", "reading", "-", false), (other!.Pos, other.Lemma, other.Sense, other.Scored));
        Assert.Contains("a99", badIndex, StringComparison.Ordinal);
        Assert.Contains("read#nonexistent", badSense, StringComparison.Ordinal);
        Assert.Contains("Planet", badOther, StringComparison.Ordinal);
    }

    private static Gold2Token ReadToken(string id) => new(id, "test", "voa-1", "voa", "voa-1-s0001", 1, "read", "T", ["We", "read", "it", "."]);

    private static string AnalysisKeyOf(Gold2Token token, Func<WordDetails, bool> pick) =>
        "a" + (Gold2Labeling.AnalysesOf(token.Form).FindIndex(a => pick(a)) + 1);

    /// <summary>
    ///     Two labelers who agree on the analysis and the sense settle a token; every other token goes to the adjudicator,
    ///     whose answer, even <c>none</c>, is kept; a token without an answer is a problem; nothing is dropped.
    /// </summary>
    [Fact]
    public void Gold2Labeling_Merge_AgreementAndAdjudication_NothingIsDropped()
    {
        // Arrange
        var tokens = new[] { ReadToken("t-1"), ReadToken("t-2"), ReadToken("t-3"), ReadToken("t-4") };
        var past = AnalysisKeyOf(tokens[0], a => a.PartOfSpeech == PartOfSpeech.Verb && a.Features.Contains("Tense=Past", StringComparison.Ordinal));
        var present = AnalysisKeyOf(tokens[0], a => a.PartOfSpeech == PartOfSpeech.Verb && a.Features.Contains("Tense=Pres", StringComparison.Ordinal));
        var senses = WordLexicon.SensesOf("read", PartOfSpeech.Verb).Select(s => s.SenseId).Order(StringComparer.Ordinal).ToList();
        var first = new Dictionary<string, Gold2Answer>
        {
            ["t-1"] = new(past, senses[0], true),
            ["t-2"] = new(past, senses[0], true),
            ["t-3"] = new(past, "unsure", false),
            ["t-4"] = new(past, senses[0], true),
        };
        var second = new Dictionary<string, Gold2Answer>
        {
            ["t-1"] = new(past, senses[0], false),
            ["t-2"] = new(present, senses[0], true),
            ["t-3"] = new(past, senses[1], true),
            ["t-4"] = new(past, senses[1], true),
        };
        var judge = new Dictionary<string, Gold2Answer>
        {
            ["t-2"] = new(present, senses[0], true),
            ["t-3"] = new(past, "none", true),
        };

        // Act
        var (settled, pending, missing) = Gold2Labeling.Compare(tokens, first, second);
        var (outcomes, problems) = Gold2Labeling.Merge(tokens, first, second, judge);
        var (complete, noProblems) = Gold2Labeling.Merge(tokens, first, second, new Dictionary<string, Gold2Answer>(judge) { ["t-4"] = new(past, senses[1], true) });

        // Assert
        Assert.Equal(["t-1"], settled.Select(o => o.Token.Id));
        Assert.Equal(["t-2", "t-3", "t-4"], pending.Select(t => t.Id));
        Assert.Empty(missing);
        Assert.Equal("t-4: the adjudicator gave no answer.", Assert.Single(problems));
        Assert.Equal(3, outcomes.Count);
        Assert.Empty(noProblems);
        Assert.Equal(4, complete.Count);
        Assert.Equal("agreed", complete[0].Status);
        Assert.False(complete[0].Final.High);
        Assert.Equal("adjudicated", complete[1].Status);
        Assert.Equal("pos", complete[1].Agreement);
        Assert.Equal("analysis", complete[2].Agreement);
        Assert.Equal("none", complete[2].Final.Sense);

        var pastRow = Gold2Labeling.ToRow(complete[0], "terms");
        var presentRow = Gold2Labeling.ToRow(complete[1], "terms");
        var noneRow = Gold2Labeling.ToRow(complete[2], "terms");
        Assert.Equal(("past", senses[0], "agreed"), (pastRow.Tense, pastRow.Sense, pastRow.Status));
        Assert.Equal("nonpast", presentRow.Tense);
        Assert.Equal("none", noneRow.Sense);

        var counts = Gold2Labeling.Count("test", complete);
        Assert.Equal((4, 1, 1, 1), (counts.Tokens, counts.FullAgreed, counts.Adjudicated - 2, counts.AdjudicatedNone));
        Assert.Contains("test", Gold2Labeling.RenderAgreement([counts]), StringComparison.Ordinal);
        Assert.Contains("t-3\t", Gold2Labeling.RenderLabels(complete), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A token missing from a labeler's file is reported before the merge, and an invalid answer of a labeler goes to the
    ///     adjudicator.
    /// </summary>
    [Fact]
    public void Gold2Labeling_Compare_MissingAndInvalidAnswers_AreReportedOrAdjudicated()
    {
        // Arrange
        var tokens = new[] { ReadToken("t-1"), ReadToken("t-2") };
        var first = new Dictionary<string, Gold2Answer> { ["t-1"] = new("a99", "-", true) };
        var second = new Dictionary<string, Gold2Answer> { ["t-1"] = new("a1", "unsure", true), ["t-2"] = new("a1", "unsure", true) };

        // Act
        var (settled, pending, missing) = Gold2Labeling.Compare(tokens, first, second);

        // Assert
        Assert.Empty(settled);
        Assert.Equal("t-1", Assert.Single(pending).Id);
        Assert.Equal("t-2", Assert.Single(missing).Id);
    }

    /// <summary>
    ///     The labeling batches list the analyses of the form and the glosses of their senses, show the answers of the labelers
    ///     to the adjudicator, and the answers are read back by the table of the batches.
    /// </summary>
    [Fact]
    public void Gold2Labeling_WriteBatchesAndReadAnswers_RoundTrip()
    {
        // Arrange
        var tokens = new[] { ReadToken("t-1"), ReadToken("t-2") };
        var directory = _dir.Root;

        // Act
        var batches = Gold2Labeling.WriteBatches(DataFolder, tokens, directory, 100, "label");
        var json = JsonDocument.Parse(File.ReadAllText(Path.Join(directory, "batches", "g2-t01.json")));
        File.WriteAllText(Path.Join(directory, "g2-t01.sol.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta1\tunsure\thigh\n2\ta2\t-\tlow\nbad line\n");
        var (answers, problems) = Gold2Labeling.ReadAnswers(directory, "sol", false);
        var pairs = new Dictionary<string, (Gold2Answer, Gold2Answer)> { ["t-1"] = (answers["t-1"], answers["t-2"]) };
        var adjudication = Gold2Labeling.WriteBatches(DataFolder, [tokens[0]], directory, 100, "adjudicate", pairs, "-retry");
        var judged = JsonDocument.Parse(File.ReadAllText(Path.Join(directory, "batches", "g2a-retry-t01.json")));

        // Assert
        Assert.Equal(1, batches);
        var group = json.RootElement.GetProperty("groups")[0];
        Assert.Equal("read", group.GetProperty("form").GetString());
        Assert.Equal(2, group.GetProperty("items").GetArrayLength());
        Assert.Contains(group.GetProperty("analyses").EnumerateArray(), a => a.TryGetProperty("senses", out var senses) && senses.GetArrayLength() >= 2 && senses[0].GetProperty("gloss").GetString()!.Length > 0);
        Assert.Contains("{read}", group.GetProperty("items")[0].GetProperty("sentence").GetString(), StringComparison.Ordinal);
        Assert.Equal(new Gold2Answer("a1", "unsure", true), answers["t-1"]);
        Assert.Equal(new Gold2Answer("a2", "-", false), answers["t-2"]);
        Assert.Single(problems);
        Assert.Equal(1, adjudication);
        Assert.Equal("unsure", judged.RootElement.GetProperty("groups")[0].GetProperty("items")[0].GetProperty("labeler1").GetProperty("sense").GetString());
        Assert.True(File.Exists(Path.Join(directory, "adjudication-retry.tsv")));
    }

    private static ClassifyResult Answer(string word, WordDetails details, string? firstSense)
    {
        ImmutableArray<SenseCandidate> senses = firstSense is null ? [] : [new SenseCandidate(firstSense, 1.0)];
        return new ClassifyResult(word, true, [new AnalysisCandidate(details, 1, senses)], details, SenseLevel.PartOfSpeech, 0.8, null, null);
    }

    private static Gold2Row Row(string id, string form, string pos, string lemma, string features, string tense, string sense, string strata = "C", string doc = "voa-1") =>
        new(new Gold2Token(id, "test", doc, "voa", doc + "-" + id, 1, form, strata, ["We", form, "it", "."]), "terms", pos, lemma, features, tense, sense, "agreed", "full");

    /// <summary>
    ///     A model is scored on the part of speech, the lemma, the tense of a same-spelling verb and the sense of the selected
    ///     analysis; a token that is not answered is wrong, and a token with no defensible sense is not scored on the sense.
    /// </summary>
    [Fact]
    public async Task Gold2Evaluator_MeasureAsync_PosLemmaTenseAndSense_AreScoredPerToken()
    {
        // Arrange
        var bankSenses = WordLexicon.SensesOf("bank", PartOfSpeech.Noun);
        var nounBank = new WordDetails("bank", PartOfSpeech.Noun, "Number=Sing");
        var pastRead = Gold2Labeling.AnalysesOf("read").First(a => a.PartOfSpeech == PartOfSpeech.Verb && a.Features.Contains("Tense=Past", StringComparison.Ordinal));
        var rows = new List<Gold2Row>
        {
            Row("t-1", "bank", "Noun", "bank", "Number=Sing", "-", bankSenses[0].SenseId),
            Row("t-2", "bank", "Noun", "bank", "Number=Sing", "-", bankSenses[1].SenseId, doc: "voa-2"),
            Row("t-3", "read", "Verb", "read", pastRead.Features, "past", "-", "T"),
            Row("t-4", "bank", "Noun", "bank", "Number=Sing", "-", "none"),
            Row("t-5", "qzxvj", "Other", "qzxvj", string.Empty, "-", "-"),
        };
        using var model = new FakeModel(words => words[1] switch
        {
            "bank" => [ClassifyResult.Unknown(words[0]), Answer(words[1], nounBank, bankSenses[0].SenseId), ClassifyResult.Unknown(words[2]), ClassifyResult.Unknown(words[3])],
            "read" => [ClassifyResult.Unknown(words[0]), Answer(words[1], new WordDetails("read", PartOfSpeech.Verb, "Tense=Pres"), null), ClassifyResult.Unknown(words[2]), ClassifyResult.Unknown(words[3])],
            _ => [ClassifyResult.Unknown(words[0]), ClassifyResult.Unknown(words[1]), ClassifyResult.Unknown(words[2]), ClassifyResult.Unknown(words[3])],
        });

        // Act
        var scored = await Gold2Evaluator.MeasureAsync(model, rows, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 1, 1], [scored[0].Pos, scored[0].Lemma, scored[0].Sense]);
        Assert.Equal(0, scored[1].Sense);
        Assert.Equal((1, 1, 0, -1), (scored[2].Pos, scored[2].Lemma, scored[2].Tense, scored[2].Sense));
        Assert.Equal(-1, scored[3].Sense);
        Assert.Equal((-1, 0), (scored[4].Pos, scored[4].Lemma));
        Assert.Equal(-1, scored[2].SenseOnAnalysis);
        Assert.Equal(1, scored[0].SenseOnAnalysis);
        var trained = new HashSet<string>(["bank"], StringComparer.Ordinal);
        Assert.Equal((2, 1), Gold2Evaluator.Tally(rows, scored, trained, Gold2Evaluator.All, 3));
        Assert.Equal((1, 0), Gold2Evaluator.Tally(rows, scored, trained, "tense", 2));
        Assert.Equal((0, 0), Gold2Evaluator.Tally(rows, scored, trained, "untrained lemma", 3));
        Assert.Contains("trained lemma", Gold2Evaluator.Render("test", rows, [("fake", scored)], trained), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The baselines are told the gold lemma and part of speech: the first sense and the most frequent sense are right on
    ///     the tokens whose gold sense they are.
    /// </summary>
    [Fact]
    public void Gold2Evaluator_Baseline_FirstAndMostFrequentSense_AreScoredOnTheSenseOnly()
    {
        // Arrange
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var rows = new List<Gold2Row> { Row("t-1", "bank", "Noun", "bank", "Number=Sing", "-", usage[0].SenseId), Row("t-2", "bank", "Noun", "bank", "Number=Sing", "-", "-") };

        // Act
        var first = Gold2Evaluator.Baseline(rows, false);
        var frequent = Gold2Evaluator.Baseline(rows, true);

        // Assert
        Assert.Equal(1, first[0].Sense);
        Assert.Equal(-1, first[1].Sense);
        Assert.Equal(-1, frequent[0].Pos);
        Assert.Equal(-1, frequent[1].Sense);
    }

    private static List<Gold2Scored> Outcomes(int[] pos, int[] sense) => [.. pos.Select((p, i) => new Gold2Scored("t-" + (i + 1), p, p, -1, sense[i], -1))];

    /// <summary>
    ///     A paired comparison counts the tokens that only one model gets right, gives the exact p value and an interval that
    ///     contains the difference, and leaves out the tokens a measure does not apply to.
    /// </summary>
    [Fact]
    public void Gold2Evaluator_Compare_TwoModels_CountsDiscordantTokensAndGivesAnInterval()
    {
        // Arrange
        var rows = Enumerable.Range(1, 60).Select(i => Row("t-" + i, "bank", "Noun", "bank", "Number=Sing", "-", "-", "NV", "voa-" + (i % 6))).ToList();
        var first = Outcomes([.. Enumerable.Repeat(1, 40), .. Enumerable.Repeat(0, 20)], [.. Enumerable.Repeat(-1, 60)]);
        var second = Outcomes([.. Enumerable.Repeat(1, 50), .. Enumerable.Repeat(0, 10)], [.. Enumerable.Repeat(-1, 60)]);
        var trained = new HashSet<string>(StringComparer.Ordinal);

        // Act
        var comparisons = Gold2Evaluator.Compare(rows, first, second, trained);
        var all = comparisons.Single(c => c.Stratum == Gold2Evaluator.All && c.Measure == "part of speech");

        // Assert
        Assert.Equal((60, 40, 50, 10, 0), (all.Tokens, all.CorrectFirst, all.CorrectSecond, all.Wins, all.Losses));
        Assert.Equal(Gold2Power.ExactMcNemar(10, 0), all.PValue, 12);
        Assert.True(all.Low <= 10.0 / 60 && all.High >= 10.0 / 60);
        Assert.Contains(comparisons, c => c.Stratum == "noun/verb" && c.Measure == "part of speech");
        Assert.DoesNotContain(comparisons, c => c.Measure == "sense");
        Assert.Contains("+16.67", Gold2Evaluator.RenderComparison("test", "A", "B", comparisons), StringComparison.Ordinal);
        Assert.Equal(comparisons, Gold2Evaluator.Compare(rows, first, second, trained));
    }

    /// <summary>
    ///     The outcomes of a model are written and read back the same, and a malformed file is an error.
    /// </summary>
    [Fact]
    public void Gold2Evaluator_Outcomes_RoundTrip()
    {
        // Arrange
        var scored = new List<Gold2Scored> { new("t-1", 1, 0, -1, 1, 0), new("t-2", -1, 1, 1, -1, -1) };
        var path = _dir.Write("outcomes.tsv", Gold2Evaluator.RenderOutcomes(scored));
        var bad = _dir.Write("bad.tsv", "id\tpos\tlemma\ttense\tsense\tsense_on_analysis\nt-1\t2\t0\t0\t0\t0\n");

        // Act
        var read = Gold2Evaluator.ReadOutcomes(path);

        // Assert
        Assert.Equal(scored, read.Values.ToList());
        Assert.Throws<InvalidDataException>(() => Gold2Evaluator.ReadOutcomes(bad));
    }

    /// <summary>
    ///     The sentences of the second gold set are not a view of another part: a sentence that an anchor or a part of the open
    ///     corpus has too is a leak, and the command exits with 1 on a document that the repository already uses.
    /// </summary>
    [Fact]
    public async Task Gold2_Leakage_SharedSentenceAndSharedDocument_AreLeaks()
    {
        // Arrange: a v2 test sentence that the open corpus test split also has, in a corpus whose open sources list the same document
        var shared = new[] { "The", "river", "bank", "was", "very", "steep", "near", "the", "old", "bridge", "." };
        var views = new[]
        {
            new LeakageSet("open corpus test", LeakageRole.Evaluation, [("o-1", string.Join(' ', shared))]),
            new LeakageSet(Gold2.LeakageName(Gold2.Test), LeakageRole.Evaluation, [("t-1", string.Join(' ', shared))]),
        };
        _dir.Write("corpus/open/sources.tsv", "source_id\tfamily\ttitle\turl\nvoa-1\tvoa\tt\thttps://example.org/used\n");
        _dir.Write("corpus/allwords/v2/sources.tsv", Gold2.RenderDocuments([new Gold2Document("voa-1", "voa", "t", "https://example.org/1", string.Empty, string.Empty, "b", "u", "d", "a", "b", 1)]));
        _dir.Write("corpus/allwords/v2/manifest.tsv", Gold2.RenderManifest([Token("t-1", "test", "voa-1", "voa", "A", "bank", "x")]));
        using var output = new StringWriter();

        // Act
        var report = LeakageCheck.Check(views);
        var code = await Program.RunAsync(["check-leakage", "--repository", _dir.Root, "--cache", _dir.Root], output, new StringWriter(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(report.Leaks, o => o is { First: "open corpus test", Second: "all-words v2 test" });
        Assert.Equal(1, code);
        Assert.Contains("Document check", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The committed rows match the manifest one to one: the check that evaluation and merge rely on passes on the real set.
    /// </summary>
    [Fact]
    public void Gold2_CommittedRows_MatchTheManifestOneToOne()
    {
        // Act
        var manifest = Gold2.ReadManifest(Corpus);
        var problems = new[] { Gold2.Dev, Gold2.Test }.SelectMany(s => Gold2.ValidateRows(manifest, Gold2.ReadRows(Corpus, s), s)).ToList();
        var dev = Gold2.ReadVerifiedRows(Corpus, Gold2.Dev);
        var test = Gold2.ReadVerifiedRows(Corpus, Gold2.Test);

        // Assert
        Assert.Empty(problems);
        Assert.Equal(manifest.Count, dev.Count + test.Count);
    }

    private static Gold2Row RowOf(Gold2Token token) => new(token, "terms", "Verb", "read", "Tense=Past", "past", "-", "agreed", "full");

    private static Gold2Token Sampled(string id, string split = "test", string doc = "voa-1") =>
        new(id, split, doc, "voa", doc + "-s0001", 1, "read", "T", ["We", "read", "it", "."]);

    /// <summary>
    ///     Rows that match the manifest one to one are accepted, whatever their order.
    /// </summary>
    [Fact]
    public void Gold2_ValidateRows_RowsOfTheManifest_HaveNoProblem()
    {
        // Arrange
        var manifest = new[] { Sampled("t-1"), Sampled("t-2"), Sampled("d-1", "dev") };

        // Act
        var problems = Gold2.ValidateRows(manifest, [RowOf(manifest[1]), RowOf(manifest[0])], Gold2.Test);

        // Assert
        Assert.Empty(problems);
    }

    /// <summary>
    ///     A missing row, a duplicate row, a row that the manifest does not have for the split and a manifest id that appears
    ///     twice are each named.
    /// </summary>
    [Fact]
    public void Gold2_ValidateRows_MissingDuplicateUnknownAndRepeatedIds_AreProblems()
    {
        // Arrange
        var manifest = new[] { Sampled("t-1"), Sampled("t-2"), Sampled("t-2"), Sampled("d-1", "dev") };
        var rows = new[] { RowOf(manifest[0]), RowOf(manifest[0]), RowOf(Sampled("t-9")), RowOf(Sampled("d-1")) };

        // Act
        var problems = Gold2.ValidateRows(manifest, rows, Gold2.Test);

        // Assert
        Assert.Contains(problems, p => p.Contains("'t-1'", StringComparison.Ordinal) && p.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'t-9'", StringComparison.Ordinal) && p.Contains("not a token", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'d-1'", StringComparison.Ordinal) && p.Contains("not a token", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'t-2'", StringComparison.Ordinal) && p.Contains("has no row", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'t-2'", StringComparison.Ordinal) && p.Contains("appears 2 times", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A row whose document, sentence, target, form or words differ from its token in the manifest is a problem.
    /// </summary>
    [Fact]
    public void Gold2_ValidateRows_RowThatDiffersFromTheManifest_NamesWhatDiffers()
    {
        // Arrange
        var token = Sampled("t-1");
        var changed = new Gold2Token("t-1", "test", "voa-2", "voa", "voa-2-s0009", 2, "it", "T", ["We", "read", "it", "."]);
        var reworded = new Gold2Token("t-1", "test", "voa-1", "voa", "voa-1-s0001", 1, "read", "T", ["They", "read", "it", "."]);

        // Act
        var first = Gold2.ValidateRows([token], [RowOf(changed)], Gold2.Test);
        var second = Gold2.ValidateRows([token], [RowOf(reworded)], Gold2.Test);

        // Assert
        var message = Assert.Single(first);
        Assert.All(new[] { "document", "sentence", "target", "form" }, name => Assert.Contains("the " + name, message, StringComparison.Ordinal));
        Assert.Contains("the words", Assert.Single(second), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Evaluation reads the rows only after they match the manifest: a missing, duplicate or different row is an error that
    ///     says what is wrong; a split without rows and without tokens is empty.
    /// </summary>
    [Fact]
    public void Gold2_ReadVerifiedRows_RowsThatDoNotMatchTheManifest_Throw()
    {
        // Arrange
        var corpus = _dir.Root;
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        var manifest = new[] { Sampled("t-1"), Sampled("t-2") };
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest(manifest));
        Assert.Empty(Gold2.ReadVerifiedRows(corpus, Gold2.Dev));

        // Act / Assert: a missing row
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [RowOf(manifest[0])]));
        var missing = Assert.Throws<InvalidDataException>(() => Gold2.ReadVerifiedRows(corpus, Gold2.Test));
        Assert.Contains("has no row", missing.Message, StringComparison.Ordinal);

        // A duplicate row
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [RowOf(manifest[0]), RowOf(manifest[0]), RowOf(manifest[1])]));
        Assert.Contains("more than once", Assert.Throws<InvalidDataException>(() => Gold2.ReadVerifiedRows(corpus, Gold2.Test)).Message, StringComparison.Ordinal);

        // A different row
        var other = new Gold2Token("t-2", "test", "voa-1", "voa", "voa-1-s0001", 1, "read", "T", ["We", "read", "this", "."]);
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [RowOf(manifest[0]), RowOf(other)]));
        Assert.Contains("differs from manifest.tsv in the words", Assert.Throws<InvalidDataException>(() => Gold2.ReadVerifiedRows(corpus, Gold2.Test)).Message, StringComparison.Ordinal);

        // Every row present: accepted
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [RowOf(manifest[1]), RowOf(manifest[0])]));
        Assert.Equal(2, Gold2.ReadVerifiedRows(corpus, Gold2.Test).Count);
    }

    /// <summary>
    ///     The keys of one Project Gutenberg book in the open corpus, the provenance and an address become one key.
    /// </summary>
    [Fact]
    public void Gold2_CanonicalDocumentKey_GutenbergSpellings_AreOneKey()
    {
        // Act / Assert
        Assert.Equal("gutenberg-1419", Gold2.CanonicalDocumentKey("pg1419"));
        Assert.Equal("gutenberg-1419", Gold2.CanonicalDocumentKey("gutenberg-1419"));
        Assert.Equal("gutenberg-1419", Gold2.CanonicalDocumentKey("PG01419"));
        Assert.Equal("gutenberg-1419", Gold2.CanonicalDocumentKey("https://www.gutenberg.org/cache/epub/1419/pg1419.txt"));
        Assert.Equal("gutenberg-1419", Gold2.CanonicalDocumentKey("https://www.gutenberg.org/ebooks/1419"));
        Assert.Equal("voa-7782072", Gold2.CanonicalDocumentKey("voa-7782072"));
        Assert.Equal("faa-9", Gold2.CanonicalDocumentKey("faa-9"));
        Assert.NotEqual(Gold2.CanonicalDocumentKey("pg14"), Gold2.CanonicalDocumentKey("pg1419"));
    }

    /// <summary>
    ///     A gold v2 Gutenberg book that the provenance of the anchors writes as <c>pg&lt;number&gt;</c>, that the open corpus lists
    ///     only by address, or that the first all-words set names in a sentence id, is a used document.
    /// </summary>
    [Fact]
    public void Gold2_ValidateDocuments_GutenbergBookUnderAnotherKeyFormat_IsAProblem()
    {
        // Arrange
        var corpus = _dir.Root;
        _dir.Write("provenance/x.tsv", "id\tsplit\tsource\tsource_key\nabc\tanchors\tgutenberg\tpg1419#s17295\n");
        _dir.Write("open/sources/urls.tsv", "doc_id\turl\nother-1\thttps://www.gutenberg.org/cache/epub/77/pg77.txt\n");
        _dir.Write("allwords/dev.tsv", "# columns\nd-1\tgutenberg-88-s0012\t3\tword\n");
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        string[] ids = ["gutenberg-1419", "gutenberg-77", "gutenberg-88", "gutenberg-99"];
        File.WriteAllText(Path.Join(folder, Gold2.SourcesName), Gold2.RenderDocuments(ids.Select(id =>
            new Gold2Document(id, "gutenberg", "t", $"https://www.gutenberg.org/cache/epub/{id[10..]}/pg{id[10..]}.txt", string.Empty, string.Empty, "b", "u", "d", "a", "b", 1))));
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest([]));

        // Act
        var problems = Gold2.ValidateDocuments(corpus);

        // Assert
        Assert.Contains(problems, p => p.Contains("'gutenberg-1419'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'gutenberg-77'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'gutenberg-88'", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("'gutenberg-99'", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A lemma is trained when it has weights: sentences for two or more senses. A lemma with a single anchored sense is not,
    ///     and the stratum of its forms is the untrained one.
    /// </summary>
    [Fact]
    public void Gold2_TrainedLemmas_SingleAnchoredSense_IsUntrained()
    {
        // Arrange: the committed anchors have two sentences for one sense of 'always'
        var anchors = Path.Join(Corpus, "anchors");

        // Act
        var weighted = WordSense.Builder.Corpus.ReadWeightedLemmas(anchors);
        var any = WordSense.Builder.Corpus.ReadLemmas(anchors);

        // Assert
        Assert.Contains("always", any);
        Assert.DoesNotContain("always", weighted);
        Assert.Equal(Gold2Strata.Untrained, Gold2Strata.SenseFlag("always", weighted));
        Assert.Equal(Gold2Strata.Trained, Gold2Strata.SenseFlag("always", new HashSet<string>(["always"], StringComparer.Ordinal)));
        Assert.True(WeightsTrainer.HasWeights(2));
        Assert.False(WeightsTrainer.HasWeights(1));
    }

    /// <summary>
    ///     The lemmas that have weights in the shipped models are the lemmas that the rule of the trainer finds in the anchors.
    /// </summary>
    [Fact]
    public void Gold2_TrainedLemmas_ShippedWeights_AreTheLemmasOfTheTrainerRule()
    {
        // Act
        var (lemmas, basis) = Gold2.TrainedLemmas(DataFixture.RepositoryRoot(), Corpus);

        // Assert
        Assert.Contains("shipped models", basis, StringComparison.Ordinal);
        Assert.Equal(WordSense.Builder.Corpus.ReadWeightedLemmas(Path.Join(Corpus, "anchors")).Order(StringComparer.Ordinal), lemmas.Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     Without a weights file the lemmas come from the anchors by the same rule.
    /// </summary>
    [Fact]
    public void Gold2_TrainedLemmas_NoWeightsFile_UsesTheAnchorRule()
    {
        // Arrange
        _dir.Write("corpus/anchors/a.tsv", "walk\twalk#a\tNoun\tx\nwalk\twalk#b\tNoun\ty\nrun\trun#a\tVerb\tz\n");

        // Act
        var (lemmas, basis) = Gold2.TrainedLemmas(_dir.Root, Path.Join(_dir.Root, "corpus"));

        // Assert
        Assert.Equal(["walk"], lemmas);
        Assert.Contains("no weights file", basis, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A further round of batches for the tokens that a labeler left unanswered sits beside the first round under a tag of its
    ///     own: the table of the first round keeps its mapping, and the answers of both rounds are read together.
    /// </summary>
    [Fact]
    public async Task Program_Gold2Batches_Missing_KeepsTheBatchIndexOfTheFirstRound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var corpus = Path.Join(_dir.Root, "corpus");
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest([ReadToken("t-1"), ReadToken("t-2"), ReadToken("t-3")]), ct);
        var work = Path.Join(_dir.Root, "work");
        Directory.CreateDirectory(work);
        string[] common = ["--dir", work, "--corpus", corpus, "--data", DataFolder];
        await RunBatchesAsync(["gold2-batches", .. common]);
        var table = await File.ReadAllTextAsync(Path.Join(work, "sample.tsv"), ct);
        await File.WriteAllTextAsync(Path.Join(work, "g2-t01.sol.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta1\t-\thigh\n", ct);

        // Act
        var output = await RunBatchesAsync(["gold2-batches", .. common, "--missing", "sol"]);
        await File.WriteAllTextAsync(Path.Join(work, "g2-r1-t01.sol.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta2\t-\tlow\n2\ta1\t-\thigh\n", ct);
        var (answers, problems) = Gold2Labeling.ReadAnswers(work, "sol", false);

        // Assert
        Assert.Equal(table, await File.ReadAllTextAsync(Path.Join(work, "sample.tsv"), ct));
        Assert.Contains("Retry round '-r1' for the 2 tokens", output, StringComparison.Ordinal);
        Assert.Contains("Wrote 1 labeling batches for 2 tokens", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(work, "batches", "g2-r1-t01.json")));
        Assert.Equal(["t-2", "t-3"], (await File.ReadAllLinesAsync(Path.Join(work, "sample-r1.tsv"), ct)).Skip(1).Select(l => l.Split('\t')[2]));
        Assert.Empty(problems);
        Assert.Equal(["t-1", "t-2", "t-3"], answers.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new Gold2Answer("a1", "-", true), answers["t-1"]);
        Assert.Equal(new Gold2Answer("a2", "-", false), answers["t-2"]);
    }

    private static async Task<string> RunBatchesAsync(string[] args)
    {
        using var output = new StringWriter();
        var code = await Program.RunAsync(args, output, new StringWriter(), TestContext.Current.CancellationToken);
        Assert.Equal(0, code);
        return output.ToString();
    }

    /// <summary>
    ///     A merge whose manifest names a document that <c>sources.tsv</c> does not list fails with a message and writes
    ///     nothing, so no split is left half written; with the document listed it writes both splits and the labels.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task Program_Gold2Merge_DocumentNotListed_WritesNothing()
    {
        // Arrange: one token that both labelers answer the same way, and a sources file without its document
        var ct = TestContext.Current.CancellationToken;
        var corpus = Path.Join(_dir.Root, "corpus");
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest([ReadToken("t-1")]), ct);
        await File.WriteAllTextAsync(Path.Join(folder, Gold2.SourcesName), Gold2.RenderDocuments([]), ct);
        var work = Path.Join(_dir.Root, "work");
        Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-1")], work, 100, "label");
        var answer = "n\tanalysis\tsense\tconfidence\n1\tother:Noun:reading\t-\thigh\n";
        await File.WriteAllTextAsync(Path.Join(work, "g2-t01.a.tsv"), answer, ct);
        await File.WriteAllTextAsync(Path.Join(work, "g2-t01.b.tsv"), answer, ct);
        string[] args = ["gold2-merge", "--dir", work, "--first", "a", "--second", "b", "--adjudicator", "j", "--corpus", corpus];
        using var failed = new StringWriter();
        using var succeeded = new StringWriter();

        // Act
        var failure = await Program.RunAsync(args, failed, new StringWriter(), ct);
        var written = new[] { "dev.tsv", "test.tsv", Gold2.LabelsName }.Where(n => File.Exists(Path.Join(folder, n))).ToList();
        await File.WriteAllTextAsync(Path.Join(folder, Gold2.SourcesName), Gold2.RenderDocuments([new Gold2Document("voa-1", "voa", "t", "https://example.org/1", string.Empty, string.Empty, "b", "https://example.org/terms", "d", "a", "b", 1)]), ct);
        var success = await Program.RunAsync(args, succeeded, new StringWriter(), ct);

        // Assert
        Assert.Equal(1, failure);
        Assert.Contains("'voa-1' is not in sources.tsv", failed.ToString(), StringComparison.Ordinal);
        Assert.Empty(written);
        Assert.Equal(0, success);
        Assert.True(File.Exists(Path.Join(folder, "dev.tsv")) && File.Exists(Path.Join(folder, "test.tsv")) && File.Exists(Path.Join(folder, Gold2.LabelsName)));
        Assert.Single(Gold2.ReadVerifiedRows(corpus, Gold2.Test));
    }

    /// <summary>
    ///     A table that maps the batches to other tokens is never rewritten: the answers already given would be read as the
    ///     answers of other tokens.
    /// </summary>
    [Fact]
    public void Gold2Labeling_WriteBatches_TableWithAnotherMapping_IsNotOverwritten()
    {
        // Arrange
        var directory = _dir.Root;
        Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-1"), ReadToken("t-2")], directory, 100, "label");

        // Act
        var same = Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-1"), ReadToken("t-2")], directory, 100, "label");
        var error = Assert.Throws<InvalidDataException>(() => Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-2")], directory, 100, "label"));

        // Assert
        Assert.Equal(1, same);
        Assert.Contains("--tag", error.Message, StringComparison.Ordinal);
        Assert.Equal("-r1", Gold2Labeling.RetryTag(directory, "label", string.Empty));
        Assert.Equal("-x", Gold2Labeling.RetryTag(directory, "label", "-x"));
    }

    /// <summary>
    ///     The report sets the strata of the draw, which the manifest froze, against the strata computed from the trained
    ///     lemmas now, and gives the improvement that the scored tokens of each planned measure detect.
    /// </summary>
    [Fact]
    public void Gold2Evaluator_RenderStrataAndPower_FrozenFlagsAgainstTheTrainedLemmas_AreCounted()
    {
        // Arrange: 'always' was drawn as trained but has no weights; 'bank' was drawn as untrained but has
        var trained = new HashSet<string>(["bank"], StringComparer.Ordinal);
        Gold2Row[] rows =
        [
            Row("t-1", "always", "Adverb", "always", string.Empty, "-", "-", strata: "TR"),
            Row("t-2", "bank", "Noun", "bank", "Number=Sing", "-", "-", strata: "NV,U"),
            Row("t-3", "bank", "Noun", "bank", "Number=Sing", "-", "-", strata: "NV,TR"),
        ];
        Gold2Scored[] scored = [.. rows.Select(r => new Gold2Scored(r.Token.Id, 1, 1, -1, -1, -1))];

        // Act
        var strata = Gold2Evaluator.RenderStrata(Gold2.Test, rows, trained);
        var power = Gold2Evaluator.RenderPower(Gold2.Test, rows, scored, trained);

        // Assert
        Assert.Contains("| 2 | 1 | 1 | 1 | 1 | 2 | 1 |", strata, StringComparison.Ordinal);
        Assert.Contains("| Part of speech (all tokens) | 1.0 points | 3.5% | 3 | ", power, StringComparison.Ordinal);
        Assert.Contains("| Tense (same-spelling verbs) | 5.0 points | 14.0% | 0 | n/a |", power, StringComparison.Ordinal);
    }

    private static Gold2Row ValidRow(string id = "t-1") =>
        new(Sampled(id), "terms", "Noun", "bank", "Number=Sing", "-", WordLexicon.SensesOf("bank", PartOfSpeech.Noun)[0].SenseId, "agreed", "full");

    /// <summary>
    ///     A row of the lexicon, with a sense of its lemma, is valid; so are a verb with the tense of its features, a token of
    ///     the part of speech <c>Other</c> and an adjudicated row with no defensible sense.
    /// </summary>
    [Fact]
    public void Gold2_ValidateRowFields_ValidRows_HaveNoProblem()
    {
        // Arrange
        var verb = new Gold2Row(Sampled("t-2"), "terms", "Verb", "read", "Tense=Past+VerbForm=Part", "past", "-", "agreed", "full");
        var other = new Gold2Row(Sampled("t-3"), "terms", "Other", "y", string.Empty, "-", "-", "agreed", "pos");
        var none = ValidRow("t-4") with { Sense = "none", Status = "adjudicated", Agreement = "analysis" };

        // Act
        var problems = new[] { ValidRow(), verb, other, none }.SelectMany(r => Gold2.ValidateRowFields(r, "test.tsv")).ToList();

        // Assert
        Assert.Empty(problems);
    }

    /// <summary>
    ///     A part of speech that is no name (a number, another case, an unknown word), an empty lemma, a sense that the lemma
    ///     does not have, on a lemma of one sense or on <c>Other</c>, a <c>none</c> that no adjudicator gave, a tense that is
    ///     not allowed, on a non-verb or against the features, and a status or an agreement outside the allowed values are each a
    ///     problem that names the field.
    /// </summary>
    [Fact]
    public void Gold2_ValidateRowFields_InvalidFields_AreProblemsThatNameTheField()
    {
        // Arrange
        var verb = new Gold2Row(Sampled("t-2"), "terms", "Verb", "read", "Tense=Past+VerbForm=Part", "past", "-", "agreed", "full");
        (Gold2Row Row, string Field)[] cases =
        [
            (ValidRow() with { Pos = "1" }, "part of speech"),
            (ValidRow() with { Pos = "noun" }, "part of speech"),
            (ValidRow() with { Pos = "Planet" }, "part of speech"),
            (ValidRow() with { Lemma = " " }, "lemma"),
            (ValidRow() with { Sense = "bank#no-such-sense" }, "sense"),
            (ValidRow() with { Pos = "Other" }, "sense"),
            (verb with { Lemma = "zzyzx", Sense = "zzyzx#x" }, "sense"),
            (ValidRow() with { Sense = "none" }, "sense"),
            (ValidRow() with { Tense = "future" }, "tense"),
            (ValidRow() with { Tense = "past" }, "tense"),
            (verb with { Tense = "nonpast" }, "tense"),
            (verb with { Features = string.Empty, Tense = "past" }, "tense"),
            (ValidRow() with { Status = "settled" }, "status"),
            (ValidRow() with { Agreement = "most" }, "agreement"),
        ];

        // Act / Assert
        foreach (var (row, field) in cases)
        {
            var problems = Gold2.ValidateRowFields(row, "test.tsv");
            Assert.Contains(problems, p => p.Contains($"invalid {field} '", StringComparison.Ordinal) && p.Contains("'t-", StringComparison.Ordinal));
        }

        Assert.Empty(Gold2.ValidateRowFields(verb, "test.tsv"));
    }

    /// <summary>
    ///     Evaluation reads no row with an invalid labeled field: the reader of verified rows names the field.
    /// </summary>
    [Fact]
    public void Gold2_ReadVerifiedRows_InvalidLabeledField_Throws()
    {
        // Arrange
        var corpus = _dir.Root;
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        var token = Sampled("t-1");
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), Gold2.RenderManifest([token]));
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), Gold2.RenderRows(Gold2.Test, [ValidRow() with { Pos = "7" }]));

        // Act
        var error = Assert.Throws<InvalidDataException>(() => Gold2.ReadVerifiedRows(corpus, Gold2.Test));

        // Assert
        Assert.Contains("invalid part of speech '7'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A target index that is negative (or no number) is an <see cref="InvalidDataException"/> in the manifest and in the
    ///     rows, never an index out of range.
    /// </summary>
    [Fact]
    public void Gold2_Readers_NegativeTargetIndex_Throws()
    {
        // Arrange
        var corpus = _dir.Root;
        var folder = Gold2.Directory(corpus);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Join(folder, Gold2.ManifestName), "t-0001\ttest\tvoa-1\tvoa\tvoa-1-s0001\t-1\tbank\tC\tthe river bank .\n");
        File.WriteAllText(Gold2.RowsPath(corpus, Gold2.Test), "t-0001\tvoa-1\tvoa\tterms\tvoa-1-s0001\t-1\tbank\tC\tNoun\tbank\tNumber=Sing\t-\t-\tagreed\tfull\tthe river bank .\n");

        // Act
        var manifest = Assert.Throws<InvalidDataException>(() => Gold2.ReadManifest(corpus));
        var rows = Assert.Throws<InvalidDataException>(() => Gold2.ReadRows(corpus, Gold2.Test));

        // Assert
        Assert.Contains("target index '-1'", manifest.Message, StringComparison.Ordinal);
        Assert.Contains("target index '-1'", rows.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A part of speech is read by its name only: a number, another case and an unknown word are no part of speech.
    /// </summary>
    [Fact]
    public void Gold2_TryParsePartOfSpeech_OnlyNames_AreParts()
    {
        // Act / Assert
        Assert.True(Gold2.TryParsePartOfSpeech("Noun", out var pos));
        Assert.Equal(PartOfSpeech.Noun, pos);
        Assert.False(Gold2.TryParsePartOfSpeech("1", out _));
        Assert.False(Gold2.TryParsePartOfSpeech("noun", out _));
        Assert.False(Gold2.TryParsePartOfSpeech("Other", out _));
        Assert.False(Gold2.TryParsePartOfSpeech(null, out _));
        Assert.NotNull(Gold2Labeling.Resolve(ReadToken("t-1"), new Gold2Answer("other:1:reading", "-", true), false, out _));
        Assert.Null(Gold2Labeling.Resolve(ReadToken("t-1"), new Gold2Answer("other:Noun:reading", "-", true), false, out _));
    }

    /// <summary>
    ///     A token that is answered twice, in one file or in two rounds of a labeler, is an error that names the token and the
    ///     file of its first answer; the first answer is never silently replaced.
    /// </summary>
    [Fact]
    public void Gold2Labeling_ReadAnswers_DuplicateAnswer_Throws()
    {
        // Arrange: a table with two batches that both name t-1, as a retry round of a token that was answered would
        var directory = _dir.Root;
        Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-1"), ReadToken("t-2")], directory, 100, "label");
        Gold2Labeling.WriteBatches(DataFolder, [ReadToken("t-1")], directory, 100, "label", null, "-r1");
        File.WriteAllText(Path.Join(directory, "g2-t01.sol.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta1\t-\thigh\n2\ta1\t-\thigh\n");
        File.WriteAllText(Path.Join(directory, "g2-r1-t01.sol.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta2\t-\tlow\n");
        File.WriteAllText(Path.Join(directory, "g2-t01.dup.tsv"), "n\tanalysis\tsense\tconfidence\n1\ta1\t-\thigh\n1\ta2\t-\tlow\n");

        // Act
        var rounds = Assert.Throws<InvalidDataException>(() => Gold2Labeling.ReadAnswers(directory, "sol", false));
        var sameFile = Assert.Throws<InvalidDataException>(() => Gold2Labeling.ReadAnswers(directory, "dup", false));

        // Assert
        Assert.Contains("'t-1'", rounds.Message, StringComparison.Ordinal);
        Assert.Contains("answered more than once", rounds.Message, StringComparison.Ordinal);
        Assert.Contains("g2-t01.sol.tsv", rounds.Message, StringComparison.Ordinal);
        Assert.Contains("g2-t01.dup.tsv", sameFile.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The bootstrap resamples only the documents that contribute tokens: documents without a token add no empty draw, so
    ///     the interval of documents that all differ by 0.2 is exactly 0.2, and no resample has a zero denominator.
    /// </summary>
    [Fact]
    public void Gold2Evaluator_BootstrapInterval_DocumentsWithoutTokens_AreNotResampled()
    {
        // Arrange: two contributing documents of 10 tokens each with a difference of 2, among 100 documents with none
        var sums = new (int N, int Diff)[102];
        sums[10] = (10, 2);
        sums[50] = (10, 2);

        // Act
        var (low, high) = Gold2Evaluator.BootstrapInterval(sums, 7);
        var empty = Gold2Evaluator.BootstrapInterval(new (int N, int Diff)[5], 7);

        // Assert
        Assert.Equal(0.2, low, 9);
        Assert.Equal(0.2, high, 9);
        Assert.Equal((0.0, 0.0), empty);
    }

    /// <summary>
    ///     The builder prints the power plan.
    /// </summary>
    [Fact]
    public async Task Program_Gold2Power_PrintsThePlan()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        var code = await Program.RunAsync(["gold2-power"], output, new StringWriter(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Contains("Tense (same-spelling verbs)", output.ToString(), StringComparison.Ordinal);
    }
}
