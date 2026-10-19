using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the import of the Moby list into the dictionary word list.
/// </summary>
public sealed class LexiconImporterTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Determiners, conjunction-only and interjection-only words stay; the letters keep the order of the codes; a
    ///     word on several lines joins its codes; words with other characters and words without a mappable code are dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconImporter_Import_KeepsEveryWordInCodeOrder()
    {
        var input = _dir.Write(
            "in/mobypos.txt",
            "the\\Dv\r\nalbeit\\C\r\nalas\\!\r\nso\\vCrA!N\r\nwill\\VN\r\nant\\N\r\nant\\t\r\nNew\\N\r\nice cream\\N\r\nzzz\\?\r\n");
        var output = Path.Join(_dir.Root, "out", "lexicon.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var counts = LexiconImporter.Import(input, output, null);

        var lines = File.ReadAllLines(output);
        Assert.Equal(["alas\tI", "albeit\tC", "ant\tNV", "so\tBCRJIN", "the\tDB", "will\tVN"], lines);
        Assert.Equal(1, counts.Dropped);
        Assert.Equal(1, counts.Determiners);
        Assert.Equal(6, counts.Words);
    }

    /// <summary>
    ///     A word that is only an adjective and ends in -ed or -ing gains the verb sense, and so does an -ing word whose
    ///     stem is a verb.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconImporter_Import_AppliesParticipleAndGerundRule()
    {
        var input = _dir.Write("in/mobypos.txt", "tired\\A\nboring\\A\nwalk\\V\nwalking\\N\nmake\\V\nmaking\\N\nrunning\\N\nrun\\V\nstat\\N\n");
        var output = Path.Join(_dir.Root, "in", "lexicon.tsv");

        var counts = LexiconImporter.Import(input, output, null);

        var lines = File.ReadAllLines(output);
        Assert.Contains("tired\tJV", lines);
        Assert.Contains("boring\tJV", lines);
        Assert.Contains("walking\tNV", lines);
        Assert.Contains("making\tNV", lines);
        Assert.Contains("running\tNV", lines);
        Assert.Contains("stat\tN", lines);
        Assert.Equal(5, counts.GainedVerb);
    }

    /// <summary>
    ///     The corrections of the directory of the output replace the set of a word, in the order given.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconImporter_Import_AppliesCorrections()
    {
        var input = _dir.Write("in/mobypos.txt", "the\\Dv\nsays\\N\n");
        _dir.Write("in/corrections.tsv", "# comment\nthe\tD\tevidence\nsays\tV\tevidence\n");
        var output = Path.Join(_dir.Root, "in", "lexicon.tsv");

        var counts = LexiconImporter.Import(input, output, null);

        Assert.Equal(["says\tV", "the\tD"], File.ReadAllLines(output));
        Assert.Equal(2, counts.Corrected);
    }

    /// <summary>
    ///     The importer checks the SHA-256 over the line-feed form, so a CRLF file passes, and rejects another file.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconImporter_Import_ChecksHash()
    {
        var input = _dir.Write("in/mobypos.txt", "ant\\N\r\nbee\\N\r\n");
        var output = Path.Join(_dir.Root, "in", "lexicon.tsv");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes("ant\\N\nbee\\N\n")));

        var counts = LexiconImporter.Import(input, output, hash);

        Assert.Equal(2, counts.Words);
        Assert.Throws<InvalidDataException>(() => LexiconImporter.Import(input, output));
        Assert.Contains("Words in the lexicon: 2", LexiconImporter.Render(counts), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A malformed correction is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LexiconImporter_Import_RejectsBadCorrection()
    {
        var input = _dir.Write("in/mobypos.txt", "ant\\N\n");
        _dir.Write("in/corrections.tsv", "ant\tX\n");

        Assert.Throws<InvalidDataException>(() => LexiconImporter.Import(input, Path.Join(_dir.Root, "in", "lexicon.tsv"), null));
    }
}
