using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Checks of the committed corpus: every sentence of the training corpus has a provenance row whose source is one
///     of the allowed sources (real text that is CC0 1.0 or public domain in the United States, or sentences written for
///     the project and dedicated to CC0 1.0), and no treebank data or retired class-labeled corpus is in the repository's
///     corpus folder.
/// </summary>
public sealed class CorpusLicenseTests
{
    private static readonly string[] Columns = ["id", "split", "source", "source_key"];

    private static string CorpusDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return Path.Join(root.FullName, "corpus");
    }

    private static IEnumerable<string[]> ProvenanceRows(string corpus) =>
        Directory.GetFiles(Path.Join(corpus, "provenance"), "*.tsv")
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => File.ReadLines(f, new UTF8Encoding(false)).Skip(1))
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'));

    private static string IdOf(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>
    ///     Every provenance file has the columns <c>id</c>, <c>split</c>, <c>source</c> and <c>source_key</c>, and the
    ///     source of every row is one of the allowed sources (the licence gate): no sentence is recorded as coming from
    ///     Tatoeba CC BY, Wikipedia, Wiktionary or a treebank, or from any other source that is not listed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CorpusLicenses_Provenance_NamesOnlyAllowedSources()
    {
        var corpus = CorpusDirectory();
        var files = Directory.GetFiles(Path.Join(corpus, "provenance"), "*.tsv");
        var rows = ProvenanceRows(corpus).ToList();

        Assert.NotEmpty(rows);
        Assert.All(files, f => Assert.Equal(Columns, File.ReadLines(f, new UTF8Encoding(false)).First().Split('\t')));
        Assert.All(rows, c => Assert.True(c.Length == Columns.Length, $"Provenance row {c[0]} has {c.Length} columns."));
        Assert.All(rows, c => Assert.True(ProvenanceSources.IsAllowed(c[2]), $"Provenance row {c[0]} has the source '{c[2]}', which is not allowed."));
    }

    /// <summary>
    ///     The licence gate lists the real-text sources and the written sources and nothing else: a source of a licence
    ///     that the corpus excludes (Tatoeba CC BY, Wikipedia, Wiktionary, the treebank) or an unknown source is refused,
    ///     and only the written sources are not real text.
    /// </summary>
    /// <param name="source">The source of a provenance row.</param>
    /// <param name="real">Whether the source is real text.</param>
    /// <param name="written">Whether the source is written for the project.</param>
    [Theory]
    [InlineData("gutenberg", true, false)]
    [InlineData("voa", true, false)]
    [InlineData("usgs", true, false)]
    [InlineData("nps", true, false)]
    [InlineData("nws", true, false)]
    [InlineData("faa", true, false)]
    [InlineData("tatoeba-cc0", true, false)]
    [InlineData("llm-corpus", false, true)]
    [InlineData("authored", false, true)]
    [InlineData("tatoeba-by", false, false)]
    [InlineData("wikipedia", false, false)]
    [InlineData("wiktionary", false, false)]
    [InlineData("ud-ewt", false, false)]
    [InlineData("", false, false)]
    public void WordSenseBuilder_CorpusLicenses_ProvenanceSources_AllowsOnlyTheListedSources(string source, bool real, bool written)
    {
        Assert.Equal(real, ProvenanceSources.IsReal(source));
        Assert.Equal(written, ProvenanceSources.IsWritten(source));
        Assert.Equal(real || written, ProvenanceSources.IsAllowed(source));
    }

    /// <summary>
    ///     Every row of the sense, chooser and real-text files has a provenance row (found by the hash of its lemma,
    ///     form or label and its sentence), so no sentence of unknown origin is in the training corpus.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CorpusLicenses_EverySentenceRow_HasProvenance()
    {
        var corpus = CorpusDirectory();
        var known = ProvenanceRows(corpus).Select(c => c[0]).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var folder in new[] { "anchors", "holdout", "realtext", Path.Join("choosers", "anchors"), Path.Join("choosers", "holdout") })
        {
            var chooser = folder.StartsWith("choosers", StringComparison.Ordinal);
            foreach (var file in Directory.GetFiles(Path.Join(corpus, folder), "*.tsv"))
            {
                foreach (var line in File.ReadLines(file, new UTF8Encoding(false)).Where(l => l.Length > 0))
                {
                    var c = line.Split('\t');
                    var found = chooser
                        ? known.Contains(IdOf(c[0] + "|" + c[2])) || known.Contains(IdOf(c[1] + "|" + c[2]))
                        : known.Contains(IdOf(c[0] + "|" + c[3]));
                    if (!found)
                    {
                        missing.Add($"{folder}/{Path.GetFileName(file)}: {line[..Math.Min(60, line.Length)]}");
                    }
                }
            }
        }

        Assert.True(missing.Count == 0, $"{missing.Count} rows have no provenance, for example: {string.Join("; ", missing.Take(3))}");
    }

    /// <summary>
    ///     The corpus folder has no treebank file and none of the retired folders (the class-labeled legacy corpus, the
    ///     migration maps, the treebank counts and the unplaced legacy rows).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CorpusLicenses_Corpus_HasNoTreebankDataAndNoLegacyFolders()
    {
        var corpus = CorpusDirectory();

        Assert.Empty(Directory.EnumerateFiles(corpus, "*.conllu", SearchOption.AllDirectories));
        foreach (var retired in new[] { "legacy", "migration", "ud", "disputed" })
        {
            Assert.False(Directory.Exists(Path.Join(corpus, retired)), $"The retired folder corpus/{retired} exists.");
        }

        Assert.False(File.Exists(Path.Join(corpus, "lexicon", "irregular-ud.tsv")));
    }
}
