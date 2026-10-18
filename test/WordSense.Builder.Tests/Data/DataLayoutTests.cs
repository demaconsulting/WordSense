using System.Text;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="DataLayout"/>: file keys, UTF-8 ordering and the table writer.
/// </summary>
public sealed class DataLayoutTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     Without a split plan a lemma goes to the file of its first letter, case folded.
    /// </summary>
    [Theory]
    [InlineData("bass", "b")]
    [InlineData("Bass", "b")]
    [InlineData("a", "a")]
    public void WordSenseBuilder_DataLayout_FileFor_NoSplit_UsesFirstLetter(string lemma, string expected)
    {
        // Arrange - no split letters
        // Act
        var key = DataLayout.FileFor(lemma, []);

        // Assert
        Assert.Equal(expected, key);
    }

    /// <summary>
    ///     A split letter uses the second letter, with <c>_</c> when there is none or it is not an ASCII letter.
    /// </summary>
    [Theory]
    [InlineData("bass", "ba")]
    [InlineData("BIG", "bi")]
    [InlineData("b", "b_")]
    [InlineData("b'x", "b_")]
    [InlineData("bé", "b_")]
    [InlineData("small", "s")]
    public void WordSenseBuilder_DataLayout_FileFor_SplitLetter_UsesTwoLetters(string lemma, string expected)
    {
        // Arrange - the letter b is split
        char[] split = ['b'];

        // Act
        var key = DataLayout.FileFor(lemma, split);

        // Assert
        Assert.Equal(expected, key);
    }

    /// <summary>
    ///     Lemmas that do not start with an ASCII letter, even a split one, go to the key <c>_</c>.
    /// </summary>
    [Theory]
    [InlineData("Ångström")]
    [InlineData("\u0065\u0301t\u00E9")]
    [InlineData("\uD835\uDC9C matrix")]
    [InlineData("'tis")]
    [InlineData("3d")]
    [InlineData("")]
    public void WordSenseBuilder_DataLayout_FileFor_NonAsciiStart_UsesUnderscore(string lemma)
    {
        // Arrange - every letter is split
        char[] split = [.. "abcdefghijklmnopqrstuvwxyz"];

        // Act
        var key = DataLayout.FileFor(lemma, split);

        // Assert
        Assert.Equal("_", key);
    }

    /// <summary>
    ///     A decomposed lemma is normalized to NFC before the key is chosen.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataLayout_FileFor_DecomposedLetter_UsesNfc()
    {
        // Arrange - "a" is ASCII, so the composed and decomposed forms of "ä" differ in the key only after NFC
        var decomposed = "a\u0308bc";

        // Act
        var key = DataLayout.FileFor(decomposed, ['a']);

        // Assert - NFC turns "a" + U+0308 into the non-ASCII U+00E4
        Assert.Equal("_", key);
    }

    /// <summary>
    ///     A file key is valid only when the split plan can produce it.
    /// </summary>
    [Theory]
    [InlineData("a", true)]
    [InlineData("_", true)]
    [InlineData("b", false)]
    [InlineData("ba", true)]
    [InlineData("b_", true)]
    [InlineData("ab", false)]
    [InlineData("bA", false)]
    [InlineData("bab", false)]
    [InlineData("", false)]
    public void WordSenseBuilder_DataLayout_IsValidKey_SplitPlan_Decides(string key, bool expected)
    {
        // Arrange - b is split
        char[] split = ['b'];

        // Act
        var valid = DataLayout.IsValidKey(key, split);

        // Assert
        Assert.Equal(expected, valid);
    }

    /// <summary>
    ///     Astral characters sort after the high BMP characters, as their UTF-8 bytes do.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataLayout_CompareUtf8_AstralAfterHighBmp()
    {
        // Arrange
        const string bmp = "\uFFFD";
        const string astral = "\uD835\uDC9C";

        // Act
        var utf8 = DataLayout.CompareUtf8(bmp, astral);
        var utf16 = string.CompareOrdinal(bmp, astral);

        // Assert - UTF-16 order is the opposite, which is why the comparison exists
        Assert.True(utf8 < 0);
        Assert.True(utf16 > 0);
        Assert.True(DataLayout.CompareUtf8("a", "ab") < 0);
        Assert.Equal(0, DataLayout.CompareUtf8("same", "same"));
    }

    /// <summary>
    ///     Cells with a tab or a line break are rejected.
    /// </summary>
    [Theory]
    [InlineData("a\tb", true)]
    [InlineData("a\nb", true)]
    [InlineData("a\rb", true)]
    [InlineData("plain text", false)]
    public void WordSenseBuilder_DataLayout_CheckCell_Separators_AreRejected(string cell, bool rejected)
    {
        // Arrange - the cell
        // Act
        var problem = DataLayout.CheckCell(cell);

        // Assert
        Assert.Equal(rejected, problem is not null);
    }

    /// <summary>
    ///     The writer produces LF line ends and no byte-order mark.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataLayout_WriteTable_WritesLfWithoutBom()
    {
        // Arrange
        var path = Path.Combine(_dir.Root, "out", "t.tsv");

        // Act
        DataLayout.WriteTable(path, ["a", "b"], [["1", "é"], ["2", ""]]);

        // Assert
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("a\tb\n1\té\n2\t\n", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    ///     The writer refuses a cell that would break the format.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_DataLayout_WriteTable_BadCell_Throws()
    {
        // Arrange
        var path = Path.Combine(_dir.Root, "bad.tsv");

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => DataLayout.WriteTable(path, ["a"], [["x\ty"]]));
    }
}
