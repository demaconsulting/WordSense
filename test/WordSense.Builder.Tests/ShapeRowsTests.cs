using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the shape rows of the committed corpus (<c>quota-w*-shape.tsv</c>): the quota tool writes noun shape
///     rows only for a lemma whose noun senses hold a minimum share of the noun and verb tag counts.
/// </summary>
public sealed class ShapeRowsTests
{
    /// <summary>The minimum noun share (<c>NOUN_SHARE_MIN</c> of <c>tools/quota-fill/shapes.py</c>).</summary>
    private const double NounShareMin = 0.02;

    /// <summary>
    ///     No committed noun shape row, and no shape-coverage unit of a noun shape, belongs to a lemma whose noun share
    ///     of the tag counts is below the minimum, so a rare noun use is never written in a shape.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ShapeRows_NounShapeOfRareNounLemma_IsAbsent()
    {
        // Arrange: the noun share of every lemma of the curation, from the WordNet 3.0 tag counts of its senses
        var corpus = Path.Join(DataFixture.RepositoryRoot(), "corpus");
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

        bool Rare(string lemma)
        {
            var (noun, verb) = tags.GetValueOrDefault(lemma);
            return noun + verb > 0 && (double)noun / (noun + verb) < NounShareMin;
        }

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
                if (shape.StartsWith("n-", StringComparison.Ordinal) && Rare(cells[0]))
                {
                    offending.Add(cells[0] + " " + shape);
                }
            }
        }

        var units = File.ReadLines(Path.Join(corpus, "coverage", "shape-coverage.tsv")).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c.Length >= 3 && c[2].StartsWith("n-", StringComparison.Ordinal) && Rare(c[0]))
            .Select(c => c[0] + " " + c[2]).ToList();

        // Assert
        Assert.True(shapeRows > 10000, $"{shapeRows} shape rows");
        Assert.Empty(offending);
        Assert.Empty(units);
    }
}
