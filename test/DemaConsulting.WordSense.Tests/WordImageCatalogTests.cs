using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the validation of the sense catalog when the word image is read: a catalog that is damaged in a way that
///     keeps the structure intact but breaks an invariant of the groups or of the key index is rejected.
/// </summary>
public class WordImageCatalogTests
{
    private const string ResourceName = "DemaConsulting.WordSense.wordsense.img";

    /// <summary>
    ///     A group that lists the same sense twice is rejected when the catalog is read, even though the number of listed
    ///     members still equals the number of senses that name a group.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_GroupListingASenseTwice_IsRejected()
    {
        // Arrange: the second member of the first group becomes a copy of its first member
        var damaged = Damage(sections =>
        {
            var starts = Uints(sections[ImageFormat.SectionGroupMemberStart]);
            var members = Uints(sections[ImageFormat.SectionGroupMembers]);
            members[starts[0] + 1] = members[starts[0]];
            sections[ImageFormat.SectionGroupMembers] = Bytes(members);
        });

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).ValidateCatalog());

        // Assert
        Assert.Contains("lists a sense twice", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A key index that is not in strict key order (two entries swapped) is rejected when the catalog is read, not
    ///     only when a tool validates it.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_KeyIndexOutOfOrder_IsRejected()
    {
        // Arrange
        var damaged = Damage(sections =>
        {
            var index = Uints(sections[ImageFormat.SectionKeyIndex]);
            (index[0], index[1]) = (index[1], index[0]);
            sections[ImageFormat.SectionKeyIndex] = Bytes(index);
        });

        // Act
        var image = WordImage.Open(damaged);
        var error = Assert.Throws<InvalidDataException>(() => image.TryGetGroup("bank#group:none", out _));

        // Assert
        Assert.Contains("key order", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A key index that lists the same sense twice (so a key twice) is rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_KeyIndexWithDuplicate_IsRejected()
    {
        // Arrange
        var damaged = Damage(sections =>
        {
            var index = Uints(sections[ImageFormat.SectionKeyIndex]);
            index[1] = index[0];
            sections[ImageFormat.SectionKeyIndex] = Bytes(index);
        });

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).ValidateCatalog());

        // Assert
        Assert.Contains("key order", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The undamaged image passes the same rebuild, so the failures above come from the damage alone.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_RebuiltWithoutDamage_IsAccepted()
    {
        // Arrange
        var rebuilt = Damage(_ => { });

        // Act
        var image = WordImage.Open(rebuilt);
        image.ValidateCatalog();

        // Assert
        Assert.Equal(WordLexicon.Current.SenseCount, image.SenseCount);
        Assert.Equal(WordLexicon.Current.GroupCount, image.GroupCount);
    }

    private static uint[] Uints(byte[] raw) => MemoryMarshal.Cast<byte, uint>(raw).ToArray();

    private static byte[] Bytes(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    // Opens the embedded image, lets the test change its raw sections and assembles an image from them with every
    // section stored as is and a correct content hash
    private static byte[] Damage(Action<SortedDictionary<int, byte[]>> change)
    {
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded word image is missing.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        var image = WordImage.Open(data);
        var sections = new SortedDictionary<int, byte[]>(image.SectionIds.ToDictionary(id => id, image.RawSection));
        change(sections);

        var directorySize = sections.Count * ImageFormat.DirectoryEntrySize;
        var result = new byte[ImageFormat.HeaderSize + directorySize + sections.Values.Sum(s => s.Length)];
        var offset = ImageFormat.HeaderSize + directorySize;
        var entryAt = ImageFormat.HeaderSize;
        foreach (var (id, raw) in sections)
        {
            var entry = result.AsSpan(entryAt);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)id);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)raw.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)raw.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], ImageFormat.FlagStored);
            raw.CopyTo(result, offset);
            offset += raw.Length;
            entryAt += ImageFormat.DirectoryEntrySize;
        }

        ImageFormat.Magic.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), ImageFormat.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)sections.Count);
        image.DataHash.CopyTo(result.AsSpan(ImageFormat.DataHashOffset));
        image.InventoryHash.CopyTo(result.AsSpan(ImageFormat.InventoryHashOffset));
        SHA256.HashData(result.AsSpan(ImageFormat.HeaderSize)).CopyTo(result.AsSpan(ImageFormat.ContentHashOffset));
        return result;
    }
}
