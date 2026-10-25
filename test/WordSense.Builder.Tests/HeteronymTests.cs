using DemaConsulting.WordSense;
using WordSense.Builder.Data;

// cspell:ignore percoid ˈægərə ˈliːdɪŋ ˈlidɪŋ ˈpɹɛzənt pɹɪˈzɛnt liːd

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the heteronym table (<c>Heteronyms</c>) and the key export: the committed data and curated table give
///     the heteronyms of the three levels, every key of them is a key of the lexicon, and the invalid curated tables are
///     reported.
/// </summary>
public sealed class HeteronymTests : IDisposable
{
    private static readonly Lazy<(string Root, WordImage Image)> Committed = new(() =>
    {
        var root = FindRepositoryRoot();
        var (bytes, _) = ImageCompiler.Build(Path.Join(root, "data"));
        return (root, WordImage.Open(bytes));
    });

    private static readonly Lazy<IReadOnlyList<Heteronyms.Heteronym>> CommittedHeteronyms = new(() =>
        Heteronyms.Build(Path.Join(Committed.Value.Root, "data"), CuratedPath(Committed.Value.Root), Committed.Value.Image));

    /// <summary>
    ///     The lemmas whose entries the pronunciations of the data split but that the lexicographer review rejected as
    ///     heteronyms (the sounds are the same in standard English, differ only in one variety, or vary by accent), so the
    ///     curated table does not list them (see <c>corpus/curation/README.md</c>).
    /// </summary>
    private static readonly string[] RejectedLemmas = ["birr", "denier", "emu", "fleer", "laver", "punt", "recap", "router", "slough", "whoop"];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     The pronunciation comparison ignores marks, variety and the common dialect differences but not the stress.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_NormalizePronunciation_IgnoresDialectButNotStress()
    {
        // Arrange / Act / Assert: syllable marks, length marks and rhoticity do not count
        Assert.Equal(Heteronyms.NormalizePronunciation("ˈæ.ɡə.ɹə"), Heteronyms.NormalizePronunciation("ˈægərə"));
        Assert.Equal(Heteronyms.NormalizePronunciation("bəʊ"), Heteronyms.NormalizePronunciation("boʊ"));
        Assert.Equal(Heteronyms.NormalizePronunciation("ˈliːdɪŋ"), Heteronyms.NormalizePronunciation("ˈlidɪŋ"));

        // A stress difference and a vowel difference count
        Assert.NotEqual(Heteronyms.NormalizePronunciation("ˈpɹɛzənt"), Heteronyms.NormalizePronunciation("pɹɪˈzɛnt"));
        Assert.NotEqual(Heteronyms.NormalizePronunciation("liːd"), Heteronyms.NormalizePronunciation("lɛd"));
    }

    /// <summary>
    ///     The three levels list the spellings of the owner's examples with their meaning classes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_ListsTheKnownHeteronymsOfEachLevel()
    {
        // Arrange / Act
        var heteronyms = CommittedHeteronyms.Value;

        // Assert: entry level (derived and curated), tense level and sense level
        foreach (var spelling in new[] { "abuse", "minute", "sow", "polish", "close", "august", "wound", "dove", "use", "object", "lives" })
        {
            Assert.Contains(heteronyms, h => h.Level == Heteronyms.Entry && h.Spelling == spelling);
        }

        foreach (var spelling in new[] { "read", "reread", "misread", "proofread" })
        {
            Assert.Contains(heteronyms, h => h.Level == Heteronyms.Tense && h.Spelling == spelling);
        }

        foreach (var spelling in new[] { "bass", "bow", "lead", "tear", "wind", "row", "basses", "tears" })
        {
            Assert.Contains(heteronyms, h => h.Level == Heteronyms.Sense && h.Spelling == spelling);
        }

        Assert.Contains(heteronyms, h => h.Source == "pronunciation");
        Assert.Contains(heteronyms, h => h.Source == "curated");
    }

    /// <summary>
    ///     The classes of the read family are the past tense and the others, and those of bass the fish and the music.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_ClassesCarryTheKeys()
    {
        // Arrange / Act
        var read = CommittedHeteronyms.Value.Single(h => h.Level == Heteronyms.Tense && h.Spelling == "read");
        var bass = CommittedHeteronyms.Value.Single(h => h.Level == Heteronyms.Sense && h.Spelling == "bass");
        var wound = CommittedHeteronyms.Value.Single(h => h.Level == Heteronyms.Entry && h.Spelling == "wound");

        // Assert
        Assert.Equal(["non-past", "past"], read.Classes.Select(c => c.Name));
        Assert.Equal(["read|read/Verb/Tense=Past+VerbForm=Part"], read.Classes.Single(c => c.Name == "past").Keys.Select(k => k.Text));
        Assert.Contains("bass|bass/Noun/Number=Sing#percoid", bass.Classes.Single(c => c.Name == "fish").Keys.Select(k => k.Text));
        Assert.Contains("bass|bass/Noun/Number=Sing#basso", bass.Classes.Single(c => c.Name == "music").Keys.Select(k => k.Text));
        Assert.Contains("wound|wind/Verb/Tense=Past+VerbForm=Part", wound.Classes.Single(c => c.Name == "past-of-wind").Keys.Select(k => k.Text));
    }

    /// <summary>
    ///     Every heteronym has two classes with keys, and every key is a key of the lexicon (so a model can return it), is
    ///     spelled like its heteronym and is listed once.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_EveryKeyIsAKeyOfTheLexicon()
    {
        // Arrange
        var keys = new HashSet<string>(WordLexicon.Keys(), StringComparer.Ordinal);
        var problems = new List<string>();

        // Act
        foreach (var heteronym in CommittedHeteronyms.Value)
        {
            if (heteronym.Classes.Count < 2 || heteronym.Classes.Any(c => c.Keys.Count == 0))
            {
                problems.Add($"{heteronym.Spelling}: fewer than two classes with keys");
            }

            var all = heteronym.Classes.SelectMany(c => c.Keys.Select(k => k.Text)).ToList();
            problems.AddRange(all.Where(k => !keys.Contains(k)).Select(k => $"{k}: not a key of the lexicon"));
            problems.AddRange(all.Where(k => !k.StartsWith(heteronym.Spelling + "|", StringComparison.Ordinal)).Select(k => $"{k}: not spelled {heteronym.Spelling}"));
            problems.AddRange(all.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => $"{g.Key}: listed twice"));
        }

        // Assert
        Assert.True(problems.Count == 0, string.Join("; ", problems.Take(10)));
    }

    /// <summary>
    ///     Every lemma whose spellings only the senses can decide (a lemma and part of speech whose entries sound different)
    ///     is in the curated sense table, so the table cannot fall behind the pronunciations of the data.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_SenseSplitLemmas_AreCurated()
    {
        // Arrange
        var root = Committed.Value.Root;
        var curated = File.ReadLines(CuratedPath(root)).Skip(1).Select(l => l.Split('\t')).Where(c => c[0] == Heteronyms.Sense).Select(c => c[1]).ToHashSet(StringComparer.Ordinal);

        // Act
        var split = Heteronyms.SenseSplitLemmas(Path.Join(root, "data"), Committed.Value.Image);

        // Assert: a lemma whose senses sound different is a heteronym, except those the lexicographer review rejected
        Assert.NotEmpty(split);
        Assert.All(split.Except(RejectedLemmas), lemma => Assert.Contains(lemma, curated));
        Assert.All(RejectedLemmas, lemma => Assert.DoesNotContain(lemma, curated));
    }

    /// <summary>
    ///     The curated notes reach the heteronyms of their group and the TSV; the other groups have none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_NotesBelongToTheirGroup()
    {
        // Arrange / Act
        var transport = CommittedHeteronyms.Value.Where(h => h.Group == "transport").ToList();
        var bass = CommittedHeteronyms.Value.Single(h => h.Level == Heteronyms.Sense && h.Spelling == "bass");
        var tsv = Heteronyms.RenderTsv(CommittedHeteronyms.Value);

        // Assert
        Assert.NotEmpty(transport);
        Assert.All(transport, h => Assert.Equal("stress contrast; some dialects merge the classes", h.Note));
        Assert.Null(bass.Note);
        Assert.Contains("\ttransport|transport/Noun/Number=Sing\tstress contrast; some dialects merge the classes\n", tsv, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The reviewed corrections hold: the removed groups are gone, and the ambiguous adjectives and the senses the
    ///     review moved are in their classes.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_ReflectsTheLexicographerReview()
    {
        // Arrange
        var heteronyms = CommittedHeteronyms.Value;
        string[] Keys(string level, string spelling, string cls, string? group = null) =>
            [.. heteronyms.Single(h => h.Level == level && h.Spelling == spelling && h.Group == (group ?? spelling)).Classes.Single(c => c.Name == cls).Keys.Select(k => k.Text)];

        // Act / Assert: removed groups
        foreach (var removed in new[] { "birr", "recap", "bourgeois", "denier", "emu", "fleer", "laver", "punt", "router", "slough", "whoop" })
        {
            Assert.DoesNotContain(heteronyms, h => h.Group == removed || h.Spelling == removed);
        }

        // The senses that separate the sounds decide, not the whole adjective
        Assert.Contains("aged|aged/Adjective/Degree=Pos#elderly", Keys(Heteronyms.Entry, "aged", "elderly"));
        Assert.Contains("aged|aged/Adjective/Degree=Pos#of-age", Keys(Heteronyms.Entry, "aged", "past-of-age"));
        Assert.Contains("learned|learned/Adjective/Degree=Pos#conditioned", Keys(Heteronyms.Entry, "learned", "past-of-learn"));
        Assert.Contains("blessed|blessed/Adjective/Degree=Pos#blest", Keys(Heteronyms.Entry, "blessed", "past-of-bless"));

        // The moved senses
        Assert.Contains("lead|lead/Noun/Number=Sing#printing-strip", Keys(Heteronyms.Sense, "lead", "metal"));
        Assert.Contains("tower|tower/Verb/VerbForm=Inf#loom", Keys(Heteronyms.Sense, "tower", "structure"));
        Assert.Contains("recreate|recreate/Verb/VerbForm=Inf#revitalize", Keys(Heteronyms.Sense, "recreate", "play"));
        Assert.Contains("close|close/Noun/Number=Sing#finale", Keys(Heteronyms.Entry, "close", "shut"));
        Assert.Contains("incense|incense/Verb/VerbForm=Inf#exasperate", Keys(Heteronyms.Entry, "incense", "anger"));

        // The additions
        Assert.Contains("read|read/Noun/Number=Sing", Keys(Heteronyms.Tense, "read", "non-past"));
        Assert.Contains("do|do/Auxiliary", Keys(Heteronyms.Sense, "do", "party"));
        Assert.Contains("leading|lead/Verb/VerbForm=Ger", Keys(Heteronyms.Sense, "leading", "guidance"));

        // The analyses that the word table gained (the adjective graduate, the noun separate, the plural of axe)
        Assert.Contains("graduate|graduate/Adjective/Degree=Pos", Keys(Heteronyms.Entry, "graduate", "adjective-noun"));
        Assert.Contains("graduate|graduate/Noun/Number=Sing", Keys(Heteronyms.Entry, "graduate", "adjective-noun"));
        Assert.Contains("separate|separate/Noun/Number=Sing", Keys(Heteronyms.Entry, "separate", "adjective-noun"));
        Assert.Contains("separates|separate/Noun/Number=Plur", Keys(Heteronyms.Entry, "separates", "adjective-noun", "separate"));
        Assert.Contains("axes|axe/Noun/Number=Plur", Keys(Heteronyms.Entry, "axes", "plural-of-ax"));
        Assert.Contains("axes|axe/Verb/Person=3+Tense=Pres", Keys(Heteronyms.Entry, "axes", "plural-of-ax"));
    }

    /// <summary>
    ///     The TSV has a header and one row of six cells for each key of a class.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_RenderTsv_WritesOneRowForEachKey()
    {
        // Arrange
        var heteronyms = CommittedHeteronyms.Value;

        // Act
        var text = Heteronyms.RenderTsv(heteronyms);

        // Assert
        var lines = text.Split('\n');
        Assert.Equal("level\tgroup\tspelling\tclass\tkey\tnote", lines[0]);
        Assert.Equal(string.Empty, lines[^1]);
        Assert.Equal(heteronyms.Sum(h => h.Classes.Sum(c => c.Keys.Count)), lines.Length - 2);
        Assert.All(lines.Skip(1).SkipLast(1), line => Assert.Equal(6, line.Split('\t').Length));
        Assert.DoesNotContain('\r', text);
        Assert.Contains("sense\tbass\tbass\tfish\tbass|bass/Noun/Number=Sing#percoid\t\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An invalid curated table is reported with every problem: a missing file, an empty file (no header), an unknown
    ///     level, a pattern that is not
    ///     one, a label that is not a sense of the catalog, a group with one class, two classes that claim the same
    ///     analysis, a group with two notes and a group that matches no spelling.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Heteronyms_Build_InvalidCuratedTable_IsReported()
    {
        // Arrange
        var data = Path.Join(Committed.Value.Root, "data");
        var image = Committed.Value.Image;
        var missing = Path.Join(_dir.Root, "missing.tsv");
        var empty = _dir.Write("empty.tsv", string.Empty);
        var invalid = _dir.Write(
            "invalid.tsv",
            "level\tgroup\tclass\tpattern\tnote\n" +
            "word\tx\ty\tbass/Noun\n" +
            "sense\tbass\tfish\tbass\n" +
            "sense\tbass\tfish\tbass/Noun#nonesuch\n" +
            "sense\tone\tonly\tbass/Noun#percoid\n" +
            "tense\tread\tpast\tread/Verb/Tense=Past\n" +
            "tense\tread\tpresent\tread/Verb\n" +
            "tense\tnothing\ta\tzzqxjw/Noun\n" +
            "tense\tnothing\tb\tzzqxjw/Verb\n" +
            "sense\tnoted\tfish\tbass/Noun#percoid\tfirst note\n" +
            "sense\tnoted\tmusic\tbass/Noun#basso\tsecond note\n" +
            "tense\tbroken\n");

        // Act
        var notFound = Assert.Throws<InvalidDataException>(() => Heteronyms.Build(data, missing, image));
        var errors = Assert.Throws<InvalidDataException>(() => Heteronyms.Build(data, invalid, image));
        var emptyErrors = Assert.Throws<InvalidDataException>(() => Heteronyms.Build(data, empty, image));

        // Assert
        Assert.Contains("was not found", notFound.Message, StringComparison.Ordinal);
        Assert.Contains("the header must be", emptyErrors.Message, StringComparison.Ordinal);
        Assert.Contains("the level 'word' is not lemma, tense or sense", errors.Message, StringComparison.Ordinal);
        Assert.Contains("the pattern 'bass' is not lemma/Part", errors.Message, StringComparison.Ordinal);
        Assert.Contains("names senses that the catalog does not have: nonesuch", errors.Message, StringComparison.Ordinal);
        Assert.Contains("The curated group 'one' has fewer than two classes", errors.Message, StringComparison.Ordinal);
        Assert.Contains("into two classes", errors.Message, StringComparison.Ordinal);
        Assert.Contains("The curated group 'nothing' (tense) matches no spelling", errors.Message, StringComparison.Ordinal);
        Assert.Contains("a row has four non-empty cells, or five with a note", errors.Message, StringComparison.Ordinal);
        Assert.Contains("The curated group 'noted' has 2 different notes", errors.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The key export renders a header comment and the keys sorted in ordinal order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_KeyExport_Render_SortsTheKeysAfterTheHeader()
    {
        // Arrange / Act
        var text = KeyExport.Render(["b|b/Noun", "B|b/Noun", "a|a/Verb#x", "a|a/Verb"], "0123456789abcdef");

        // Assert
        Assert.Equal(
            "# WordSense keys; format: {spelling}|{lemma}/{Part}[/{Features}][#{label}]; inventory: 0123456789abcdef; count: 4\nB|b/Noun\na|a/Verb\na|a/Verb#x\nb|b/Noun\n",
            text);
    }

    /// <summary>
    ///     The key export of the embedded lexicon writes the header with its version and every key once, sorted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_KeyExport_Write_ListsEveryKeyOfTheLexicon()
    {
        // Arrange
        var target = Path.Join(_dir.Root, "out", "keys.txt");

        // Act
        var count = KeyExport.Write(target);

        // Assert
        var lines = File.ReadAllLines(target);
        Assert.Equal(count + 1, lines.Length);
        Assert.Equal($"# WordSense keys; format: {{spelling}}|{{lemma}}/{{Part}}[/{{Features}}][#{{label}}]; inventory: {WordLexicon.Version}; count: {count}", lines[0]);
        Assert.True(lines.Skip(1).Zip(lines.Skip(2), (a, b) => string.CompareOrdinal(a, b) < 0).All(sorted => sorted));
        Assert.True(count > 1_000_000);
        Assert.Equal(WordLexicon.Keys().Count(), count);
    }

    private static string CuratedPath(string root) => Path.Join(root, "corpus", "curation", "heteronyms.tsv");

    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
