using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the shape rows of the committed corpus (<c>quota-w*-shape.tsv</c>): noun shape rows exist only for a lemma
///     whose noun senses hold a minimum share of the noun and verb tag counts. The rows are written by the quota tool
///     (<c>tools/quota-fill/shapes.py</c>); the tests verify the corpus it leaves, not the script.
/// </summary>
public sealed class ShapeRowsTests
{
    /// <summary>The minimum noun share of the noun and verb tag counts of a lemma.</summary>
    private const double NounShareMin = 0.02;

    /// <summary>
    ///     No committed noun shape row is of a lemma whose noun senses hold less than 2 percent of the WordNet tag counts of
    ///     its noun and verb senses, so a rare noun use is never written in a shape.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeRows_NounRowOfRareNounLemma_IsAbsent()
    {
        // Arrange
        var corpus = Path.Join(DataFixture.RepositoryRoot(), "corpus");
        var rare = RareNounLemmas(corpus);

        // Act: the noun shape rows (the provenance tag joined to the anchors by the hash of lemma|sentence)
        var shapeOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Join(corpus, "provenance"), "quota-w*-shape.tsv"))
        {
            foreach (var cells in File.ReadLines(file).Skip(1).Select(l => l.Split('\t')).Where(c => c.Length >= 4))
            {
                shapeOf[cells[0]] = cells[3][(cells[3].IndexOf("-shape:", StringComparison.Ordinal) + "-shape:".Length)..];
            }
        }

        var shapeRows = 0;
        var offending = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Join(corpus, "anchors"), "quota-w*-shape.tsv"))
        {
            foreach (var cells in File.ReadLines(file).Select(l => l.Split('\t')).Where(c => c.Length == 4))
            {
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells[0] + "|" + cells[3]))).ToLowerInvariant()[..12];
                if (!shapeOf.TryGetValue(id, out var shape))
                {
                    continue;
                }

                shapeRows++;
                if (shape.StartsWith("n-", StringComparison.Ordinal) && rare.Contains(cells[0]))
                {
                    offending.Add(cells[0] + " " + shape);
                }
            }
        }

        // Assert
        Assert.True(shapeRows > 10000, $"{shapeRows} shape rows");
        Assert.Empty(offending);
    }

    /// <summary>
    ///     The shape coverage report has no unit of a noun shape for a lemma whose noun senses hold less than 2 percent of the
    ///     WordNet tag counts of its noun and verb senses.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeRows_NounUnitOfRareNounLemma_IsAbsent()
    {
        // Arrange
        var corpus = Path.Join(DataFixture.RepositoryRoot(), "corpus");
        var rare = RareNounLemmas(corpus);

        // Act
        var units = File.ReadLines(Path.Join(corpus, "coverage", "shape-coverage.tsv")).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c.Length >= 3)
            .ToList();
        var offending = units.Where(c => c[2].StartsWith("n-", StringComparison.Ordinal) && rare.Contains(c[0])).Select(c => c[0] + " " + c[2]).ToList();

        // Assert
        Assert.True(units.Count > 1000, $"{units.Count} units");
        Assert.Empty(offending);
    }

    /// <summary>
    ///     The lemmas of the curation whose noun senses hold less than the minimum share of the noun and verb tag counts.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The lemmas.</returns>
    private static HashSet<string> RareNounLemmas(string corpus)
    {
        var tags = new Dictionary<string, (long Noun, long Verb)>(StringComparer.Ordinal);
        var lines = File.ReadLines(Path.Join(corpus, "curation", "senses.tsv")).ToList();
        var header = lines[0].Split('\t');
        var posColumn = Array.IndexOf(header, "part_of_speech");
        var tagColumn = Array.IndexOf(header, "tag_count");
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var cells = line.Split('\t');
            var lemma = cells[0].Split('#')[0];
            var count = cells[tagColumn].Length == 0 ? 0 : long.Parse(cells[tagColumn], CultureInfo.InvariantCulture);
            var (noun, verb) = tags.GetValueOrDefault(lemma);
            tags[lemma] = cells[posColumn] switch
            {
                "Noun" => (noun + count, verb),
                "Verb" => (noun, verb + count),
                _ => (noun, verb),
            };
        }

        return [.. tags.Where(t => t.Value.Noun + t.Value.Verb > 0 && (double)t.Value.Noun / (t.Value.Noun + t.Value.Verb) < NounShareMin).Select(t => t.Key)];
    }
}
