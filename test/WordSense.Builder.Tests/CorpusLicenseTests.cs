using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Checks of the committed corpus: every sentence of the training corpus has a recorded origin and a license that
///     is CC0 1.0, public domain in the United States or project-authored (dedicated to CC0 1.0), and no treebank data
///     or retired class-labeled corpus is in the repository's corpus folder.
/// </summary>
public sealed class CorpusLicenseTests
{
    private static readonly string[] AllowedPrefixes = ["CC0 1.0", "Public domain in the United States"];

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
    ///     Every provenance row records a license of CC0 1.0, public domain in the United States, or a project-authored
    ///     sentence dedicated to CC0 1.0, and no sentence is recorded as Tatoeba CC BY.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CorpusLicenses_Provenance_RecordsOnlyCc0PublicDomainAndProjectAuthoredSentences()
    {
        var corpus = CorpusDirectory();
        var rows = ProvenanceRows(corpus).ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, c => Assert.True(c.Length >= 10, $"Provenance row {c[0]} has {c.Length} columns."));
        Assert.All(rows, c => Assert.True(
            Array.Exists(AllowedPrefixes, p => c[5].StartsWith(p, StringComparison.Ordinal)) || c[5] == "CC0 1.0 (project-authored)",
            $"Provenance row {c[0]} has the license '{c[5]}'."));
        Assert.DoesNotContain(rows, c => c[4] is "tatoeba-by" or "wikipedia" or "wiktionary" or "ud-ewt");
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
