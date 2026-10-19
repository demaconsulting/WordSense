namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="CoverageReport"/>.
/// </summary>
public sealed class CoverageReportTests : IDisposable
{
    private static readonly string[] BodyLines = ["the body", "more body"];
    private static readonly string[] NoMarkerLines = ["no", "markers"];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Only the text between the start and end marker lines is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CoverageReport_Body_CutsAtMarkers()
    {
        string[] lines = ["License words", "*** START OF THE PROJECT ***", "the body", "more body", "*** END OF THE PROJECT ***", "License again"];

        Assert.Equal(BodyLines, CoverageReport.Body(lines).ToArray());
        Assert.Equal(NoMarkerLines, CoverageReport.Body(["no", "markers"]).ToArray());
    }

    /// <summary>
    ///     Known tokens, apostrophe tokens, names and other unknown words are counted apart.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CoverageReport_AddText_ClassifiesTokens()
    {
        var known = new HashSet<string> { "the", "dog", "ran", "saw", "and", "again" };
        var report = new CoverageReport();

        report.AddText(
            [
                "The dog ran. The dog don't run.",
                "Zorp saw Zorp and Zorp again Zorp, the wgxy.",
                "Wgxy wgxy wgxy",
            ],
            known.Contains);

        Assert.Equal(19, report.Tokens);
        Assert.Equal(9, report.KnownTokens);
        Assert.Equal(1, report.ApostropheTokens);
        Assert.Equal(new[] { ("zorp", 4) }, report.Names.ToArray());
        Assert.Equal("wgxy", report.Other[0].Word);
        Assert.Equal(4, report.Other.Single(o => o.Word == "wgxy").Count);
        Assert.Equal(100.0 * 9 / 19, report.Coverage, 6);
        Assert.Equal(100.0 * 9 / 18, report.CoverageWithoutApostrophes, 6);
    }

    /// <summary>
    ///     A directory is read file by file, with the Gutenberg license text excluded, and rendered.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CoverageReport_ReadAndRender_ListsFigures()
    {
        _dir.Write("text/a.txt", "Header unknownlicense\n*** START ***\nthe dog\n*** END ***\nfooter unknownlicense\n");
        _dir.Write("text/b.txt", "the cat\n");

        var report = CoverageReport.Read(Path.Combine(_dir.Root, "text"), w => w is "the" or "dog");
        var markdown = report.Render(10);

        Assert.Equal(2, report.Files);
        Assert.Equal(4, report.Tokens);
        Assert.Equal(3, report.KnownTokens);
        Assert.Contains("| Known tokens | 3 (75.00 percent) |", markdown, StringComparison.Ordinal);
        Assert.Contains("| cat | 1 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("unknownlicense", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing directory and a directory without text files are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CoverageReport_Read_BadDirectory_Throws()
    {
        Assert.Throws<InvalidDataException>(() => CoverageReport.Read(Path.Combine(_dir.Root, "missing"), _ => true));
        Directory.CreateDirectory(Path.Combine(_dir.Root, "empty"));
        Assert.Throws<InvalidDataException>(() => CoverageReport.Read(Path.Combine(_dir.Root, "empty"), _ => true));
    }

    /// <summary>
    ///     Regression: an authored sample of ordinary inflected English is fully known by the committed word table.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CoverageReport_AuthoredSample_IsFullyKnown()
    {
        const string sample =
            "The children looked at the trees and seemed happier. She knows that he went home, having stopped " +
            "running. Quickly the swimmers carried boxes, wishes and churches; the monarchs were smiling sadly, " +
            "and the kindness of the stronger neighbors was greatest. Neighbouring statesmen thought and spoke.";
        var report = new CoverageReport();

        report.AddText([sample], DemaConsulting.WordSense.WordLexicon.Contains);

        Assert.True(report.Other.Count == 0 && report.Names.Count == 0, "Unknown: " + string.Join(", ", report.Other.Concat(report.Names).Select(o => o.Word)));
        Assert.Equal(report.Tokens, report.KnownTokens);
    }
}
