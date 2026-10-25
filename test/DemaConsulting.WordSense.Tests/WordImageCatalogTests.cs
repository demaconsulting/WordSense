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

    /// <summary>
    ///     The sense frequency section is required: an image without it is rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_SenseFrequencySectionMissing_IsRejected()
    {
        // Arrange
        var damaged = Damage(sections => sections.Remove(ImageFormat.SectionSenseFrequency));

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).ValidateCatalog());

        // Assert
        Assert.Contains("no section", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sense frequency section that does not have one value for each sense (one value short, or an odd number of
    ///     bytes) is rejected when the catalog is read.
    /// </summary>
    /// <param name="shorten">The number of bytes cut from the end of the section.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void WordLexicon_Image_SenseFrequencyWrongLength_IsRejected(int shorten)
    {
        // Arrange
        var damaged = Damage(sections => sections[ImageFormat.SectionSenseFrequency] = sections[ImageFormat.SectionSenseFrequency][..^shorten]);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).ValidateCatalog());

        // Assert
        Assert.Contains("length", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A project sense has no sense key and so no WordNet 3.0 tag count: a count on a sense without a key is rejected.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_TagCountOnASenseWithoutAKey_IsRejected()
    {
        // Arrange: the first sense without a key gets a count
        var damaged = Damage(sections =>
        {
            var keyLength = sections[ImageFormat.SectionSenseKeyLength];
            var frequency = sections[ImageFormat.SectionSenseFrequency];
            var keyless = Array.IndexOf(keyLength, (byte)0);
            Assert.True(keyless >= 0);
            BinaryPrimitives.WriteUInt16LittleEndian(frequency.AsSpan(keyless * 2), 7);
        });

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).ValidateCatalog());

        // Assert
        Assert.Contains("without a key has a tag count", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tag counts are not part of the identity of the inventory: the inventory hash of the header is that of the
    ///     inventory sections, whatever the counts are, so models trained against the image need no training when the
    ///     counts change.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_TagCounts_AreNotPartOfTheInventoryHash()
    {
        // Arrange: every count is cleared
        var damaged = Damage(sections => sections[ImageFormat.SectionSenseFrequency] = new byte[sections[ImageFormat.SectionSenseFrequency].Length]);

        // Act
        var image = WordImage.Open(damaged);
        image.ValidateCatalog();
        var hash = ImageFormat.ComputeInventoryHash(image.RawSection);

        // Assert
        Assert.DoesNotContain(ImageFormat.SectionSenseFrequency, ImageFormat.InventorySections);
        Assert.True(image.InventoryHash.SequenceEqual(hash));
        Assert.True(image.InventoryHash.SequenceEqual(WordLexicon.Current.InventoryHash));
        Assert.All(image.UsageOf("bank", PartOfSpeech.Noun), u => Assert.Equal(0, u.TagCount));
    }

    /// <summary>
    ///     The usage of the senses of a lemma and part of speech lists the senses in inventory order with their WordNet 3.0
    ///     tag counts: the same senses as <see cref="WordLexicon.SensesOf"/> and a count for a sense that WordNet tagged.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_UsageOf_ListsTheSensesInInventoryOrderWithTheirTagCounts()
    {
        // Act
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);
        var senses = WordLexicon.SensesOf("bank", PartOfSpeech.Noun);

        // Assert
        Assert.Equal(senses.Select(s => s.SenseId), usage.Select(u => u.SenseId));
        Assert.True(usage.Length >= 2);
        Assert.Contains(usage, u => u.TagCount > 0);
    }

    /// <summary>
    ///     The usage of a lemma and part of speech that is not in the catalog is empty.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_UsageOf_SlotNotInTheCatalog_IsEmpty()
    {
        // Act
        var unknownLemma = WordLexicon.Current.UsageOf("zzqxjw", PartOfSpeech.Noun);
        var unknownPartOfSpeech = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Determiner);

        // Assert
        Assert.Empty(unknownLemma);
        Assert.Empty(unknownPartOfSpeech);
    }

    /// <summary>
    ///     The image carries a tag count for every sense of the catalog: the usage of every lemma and part of speech has one
    ///     entry for each of its senses, and the entries of all slots add up to the senses of the catalog.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_SenseFrequency_HasACountForEverySense()
    {
        // Arrange
        var slots = WordLexicon.Current.AllSenses().GroupBy(s => (s.Lemma, s.PartOfSpeech)).ToList();

        // Act
        var usages = slots.ToDictionary(g => g.Key, g => WordLexicon.Current.UsageOf(g.Key.Lemma, g.Key.PartOfSpeech));

        // Assert
        Assert.All(slots, g => Assert.Equal(g.Select(s => s.SenseId).Order(StringComparer.Ordinal), usages[g.Key].Select(u => u.SenseId).Order(StringComparer.Ordinal)));
        Assert.Equal(WordLexicon.Current.SenseCount, usages.Values.Sum(u => u.Length));
    }

    /// <summary>
    ///     A project sense, which has no sense key, has the tag count 0.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_UsageOf_ProjectSense_HasTagCountZero()
    {
        // Arrange: the senses of the catalog that have no sense key
        var projectSenses = WordLexicon.Current.AllSenses().Where(s => s.SenseKey is null).ToList();

        // Act
        var counts = projectSenses.Select(s => WordLexicon.Current.UsageOf(s.Lemma, s.PartOfSpeech).First(u => u.SenseId == s.SenseId).TagCount).ToList();

        // Assert
        Assert.NotEmpty(projectSenses);
        Assert.All(counts, c => Assert.Equal(0, c));
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
