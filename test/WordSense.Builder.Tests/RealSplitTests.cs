using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the split of the real-text held-out sentences by document.
/// </summary>
public sealed class RealSplitTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private string Corpus => Path.Join(_dir.Root, "corpus");

    private static string Provenance(CorpusRow row, string source, string key) =>
        string.Join('\t', RealSplit.IdOf(row), "holdout", source, key);

    private void WriteProvenance(IEnumerable<string> rows) =>
        _dir.Write("corpus/provenance/p.tsv", "id\tsplit\tsource\tsource_key\n" + string.Concat(rows.Select(r => r + "\n")));

    /// <summary>
    ///     Every sentence of a document is on one side, both sides get documents, and a Tatoeba sentence is its own
    ///     document.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealSplit_Classify_SplitsRealRowsByDocument()
    {
        // Arrange: 40 books with 3 sentences each and 40 Tatoeba sentences
        var sense = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var rows = new List<CorpusRow>();
        var provenance = new List<string>();
        var number = 0;
        for (var book = 0; book < 40; book++)
        {
            for (var s = 0; s < 3; s++)
            {
                var row = SenseFixtures.Row(sense, number++);
                rows.Add(row);
                provenance.Add(Provenance(row, "gutenberg", $"pg{book.ToString(CultureInfo.InvariantCulture)}#s{s.ToString(CultureInfo.InvariantCulture)}"));
            }
        }

        for (var t = 0; t < 40; t++)
        {
            var row = SenseFixtures.Row(sense, number++);
            rows.Add(row);
            provenance.Add(Provenance(row, "tatoeba-cc0", t.ToString(CultureInfo.InvariantCulture)));
        }

        WriteProvenance(provenance);

        // Act
        var split = RealSplit.Classify(Corpus, rows);

        // Assert
        Assert.Equal(160, split.Calibration.Count + split.Test.Count);
        Assert.NotEmpty(split.Calibration);
        Assert.NotEmpty(split.Test);
        var side = rows.Take(120).Select((r, i) => (Book: i / 3, Calibrated: split.Calibration.Contains(r))).GroupBy(p => p.Book);
        Assert.All(side, g => Assert.Single(g.Select(p => p.Calibrated).Distinct()));

        // Each Tatoeba sentence is a document of its own, so those sentences fall on both sides
        var tatoebaCalibrated = rows.Skip(120).Count(r => split.Calibration.Contains(r));
        Assert.InRange(tatoebaCalibrated, 1, 39);
    }

    /// <summary>
    ///     A written sentence and a sentence without a provenance row are in neither split.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealSplit_Classify_WrittenAndUnknownRows_AreNeverRealRows()
    {
        // Arrange
        var sense = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var real = SenseFixtures.Row(sense, 1);
        var written = SenseFixtures.Row(sense, 2);
        var unknown = SenseFixtures.Row(sense, 3);
        WriteProvenance([Provenance(real, "gutenberg", "pg1#s1"), Provenance(written, "llm-corpus", "quota-w1-written")]);

        // Act
        var split = RealSplit.Classify(Corpus, [real, written, unknown]);

        // Assert
        Assert.Equal(1, split.Calibration.Count + split.Test.Count);
        Assert.Equal([written], split.Written);
        Assert.Equal([unknown], split.Other);
    }

    /// <summary>
    ///     A provenance file is refused, with its path and line, when its header is not exact, a row is short or has an empty
    ///     field, the split or the source is not allowed, or an id repeats.
    /// </summary>
    /// <param name="content">The content of the file.</param>
    /// <param name="line">The line named by the message.</param>
    [Theory]
    [InlineData("id\tsplit\tsource\n", 1)]
    [InlineData("source_key\tid\tsplit\tsource\n", 1)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\tholdout\tgutenberg\n", 2)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\tholdout\tgutenberg\tpg1\textra\n", 2)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\tholdout\t\tpg1\n", 2)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\ttraining\tgutenberg\tpg1\n", 2)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\tholdout\twikipedia\tpg1\n", 2)]
    [InlineData("id\tsplit\tsource\tsource_key\nabc\tholdout\tgutenberg\tpg1\nabc\tanchors\tvoa\tx\n", 3)]
    public void WordSenseBuilder_RealSplit_ReadProvenance_MalformedFile_IsRefusedWithPathAndLine(string content, int line)
    {
        // Arrange
        _dir.Write("corpus/provenance/p.tsv", content);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => RealSplit.ReadProvenance(Corpus));

        // Assert
        Assert.Contains($"p.tsv:{line.ToString(CultureInfo.InvariantCulture)}:", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An id that repeats in two files is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealSplit_ReadProvenance_IdInTwoFiles_IsRefused()
    {
        // Arrange
        _dir.Write("corpus/provenance/a.tsv", "id\tsplit\tsource\tsource_key\nabc\tholdout\tgutenberg\tpg1\n");
        _dir.Write("corpus/provenance/b.tsv", "id\tsplit\tsource\tsource_key\nabc\tanchors\tllm-corpus\tbatch\n");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => RealSplit.ReadProvenance(Corpus));

        // Assert
        Assert.Contains("b.tsv:2:", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The test rows whose document supplies a real anchor are left out of the list of test rows without anchor
    ///     documents.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_RealSplit_Classify_ReportsTestRowsInDocumentsWithoutAnchors()
    {
        // Arrange: find two books on the test side, one of them also supplies an anchor
        var sense = SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];
        var books = Enumerable.Range(0, 200).Where(b => RealSplit.SideOf($"gutenberg:pg{b.ToString(CultureInfo.InvariantCulture)}") == HoldoutSide.Test).Take(2).ToArray();
        var withAnchor = SenseFixtures.Row(sense, 1);
        var holdoutWithAnchorDocument = SenseFixtures.Row(sense, 2);
        var holdoutFree = SenseFixtures.Row(sense, 3);
        WriteProvenance(
        [
            Provenance(withAnchor, "gutenberg", $"pg{books[0].ToString(CultureInfo.InvariantCulture)}#s1"),
            Provenance(holdoutWithAnchorDocument, "gutenberg", $"pg{books[0].ToString(CultureInfo.InvariantCulture)}#s2"),
            Provenance(holdoutFree, "gutenberg", $"pg{books[1].ToString(CultureInfo.InvariantCulture)}#s1"),
        ]);

        // Act
        var split = RealSplit.Classify(Corpus, [holdoutWithAnchorDocument, holdoutFree], [withAnchor]);

        // Assert
        Assert.Equal(2, split.Test.Count);
        Assert.Equal([holdoutFree], split.TestWithoutAnchorDocuments);
    }
}
