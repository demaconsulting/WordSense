// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using DemaConsulting.WordSense;
using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests of the flat word image: determinism, ordering, folding and corruption detection.
/// </summary>
public sealed class ImageTests
{
    private static readonly Lazy<byte[]> Committed = new(() =>
    {
        var root = FindRoot();
        return File.ReadAllBytes(Path.Join(root, ImageCompiler.DefaultOutput.Replace('/', Path.DirectorySeparatorChar)));
    });

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "WordSense.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    ///     Two compilations of the same data give the same decompressed sections and data hash.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ImageCompiler_Build_IsDeterministic()
    {
        var root = FindRoot();
        var data = Path.Join(root, "data");

        var a = WordImage.Open(ImageCompiler.Build(data).Image);
        var b = WordImage.Open(ImageCompiler.Build(data).Image);

        Assert.True(a.DataHash.SequenceEqual(b.DataHash));
        Assert.True(a.InventoryHash.SequenceEqual(b.InventoryHash));
        Assert.True(a.InventoryHash.SequenceEqual(ImageFormat.ComputeInventoryHash(a.RawSection)), "The header holds the hash of the inventory sections.");
        foreach (var id in a.SectionIds)
        {
            Assert.True(a.RawSection(id).AsSpan().SequenceEqual(b.RawSection(id)), $"Section {id} differs.");
        }
    }

    /// <summary>
    ///     The sense ids of the committed catalog are in UTF-8 byte order and its structure and key index are consistent.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_Catalog_IsOrderedAndConsistent()
    {
        var image = WordImage.Open(Committed.Value);

        image.ValidateCatalog();

        Assert.True(image.SenseCount > 129_000);
        Assert.True(image.GroupCount > 0);
    }

    /// <summary>
    ///     A sense that names a group that does not exist is rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_SenseWithUnknownGroup_IsRejected()
    {
        var image = Damage(sections =>
        {
            var groups = Uints(sections[ImageFormat.SectionSenseGroup].Raw);
            groups[0] = 99;
            sections[ImageFormat.SectionSenseGroup] = (Bytes(groups), 4);
        });

        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(image).ValidateCatalog());

        Assert.Contains("group that does not exist", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two senses with the same id are rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_DuplicateSenseId_IsRejected()
    {
        var image = Damage(sections =>
        {
            var lengths = sections[ImageFormat.SectionSenseIdLength].Raw;
            var text = Encoding.UTF8.GetString(sections[ImageFormat.SectionSenseIdBytes].Raw);
            var ids = new List<string>();
            var position = 0;
            foreach (var length in lengths)
            {
                ids.Add(text[position..(position + length)]);
                position += length;
            }

            ids[1] = ids[0];
            sections[ImageFormat.SectionSenseIdLength] = (ids.Select(i => (byte)Encoding.UTF8.GetByteCount(i)).ToArray(), 1);
            sections[ImageFormat.SectionSenseIdBytes] = (Encoding.UTF8.GetBytes(string.Concat(ids)), 1);
        });

        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(image).ValidateCatalog());

        Assert.Contains("occurs twice", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group of one sense, a group of all the senses of its lemma and part of speech, and a group whose members are
    ///     of two parts of speech are rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_GroupOfOneSenseOrAllSenses_IsRejected()
    {
        // The sorted senses of the small data folder: banking-company, close-in (verb), coin-bank, cover (verb), incline, ridge
        var one = Damage(sections => SetGroups(sections, [1, 0, 0, 0, 0, 0]));
        var all = Damage(sections => SetGroups(sections, [1, 0, 1, 0, 1, 1]));
        var mixed = Damage(sections => SetGroups(sections, [1, 1, 1, 0, 0, 0]));

        Assert.Contains("fewer than two", Assert.Throws<InvalidDataException>(() => WordImage.Open(one).ValidateCatalog()).Message, StringComparison.Ordinal);
        Assert.Contains("all the senses", Assert.Throws<InvalidDataException>(() => WordImage.Open(all).ValidateCatalog()).Message, StringComparison.Ordinal);
        Assert.Contains("do not agree", Assert.Throws<InvalidDataException>(() => WordImage.Open(mixed).ValidateCatalog()).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A group id without the group marker is rejected when the catalog is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_GroupIdWithoutMarker_IsRejected()
    {
        var image = Damage(sections =>
        {
            var text = Encoding.UTF8.GetString(sections[ImageFormat.SectionGroupIdBytes].Raw).Replace("#group:", "#grope:", StringComparison.Ordinal);
            sections[ImageFormat.SectionGroupIdBytes] = (Encoding.UTF8.GetBytes(text), 1);
        });

        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(image).ValidateCatalog());

        Assert.Contains("group marker", error.Message, StringComparison.Ordinal);
    }

    private static uint[] Uints(byte[] raw) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(raw).ToArray();

    private static byte[] Bytes(IEnumerable<uint> values)
    {
        var list = values.ToList();
        var bytes = new byte[list.Count * 4];
        for (var i = 0; i < list.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), list[i]);
        }

        return bytes;
    }

    // Rebuilds the group sections from the group of each sense (0 for none), listing the members in inventory order
    private static void SetGroups(SortedDictionary<int, (byte[] Raw, int Stride)> sections, uint[] senseGroup)
    {
        var blockSenses = Uints(sections[ImageFormat.SectionBlockSenses].Raw);
        var starts = new List<uint> { 0 };
        var members = new List<uint>();
        for (var group = 1u; group <= senseGroup.Max(); group++)
        {
            members.AddRange(blockSenses.Where(s => senseGroup[s] == group));
            starts.Add((uint)members.Count);
        }

        sections[ImageFormat.SectionSenseGroup] = (Bytes(senseGroup), 4);
        sections[ImageFormat.SectionGroupMemberStart] = (Bytes(starts), 4);
        sections[ImageFormat.SectionGroupMembers] = (Bytes(members), 4);
    }

    // Compiles the small data folder, lets the test change the raw sections and assembles the image again
    private static byte[] Damage(Action<SortedDictionary<int, (byte[] Raw, int Stride)>> change)
    {
        using var dir = new TestDirectory();
        var (bytes, _) = ImageCompiler.Build(ImageCompilerTests.WriteSmallData(dir.Root));
        var image = WordImage.Open(bytes);
        var sections = new SortedDictionary<int, (byte[] Raw, int Stride)>(image.SectionIds.ToDictionary(id => id, id => (image.RawSection(id), 1)));
        change(sections);
        return ImageCompiler.Assemble(sections, image.DataHash.ToArray(), image.InventoryHash.ToArray());
    }

    /// <summary>
    ///     The spelling index is sorted by UTF-8 bytes, which is what the binary search relies on.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_Spellings_AreSortedByUtf8Bytes()
    {
        var image = WordImage.Open(Committed.Value);

        var previous = Array.Empty<byte>();
        for (var i = 0; i < image.SpellingCount; i++)
        {
            var current = Encoding.UTF8.GetBytes(image.Spelling(i));
            if (i > 0)
            {
                Assert.True(previous.AsSpan().SequenceCompareTo(current) < 0, $"Spelling {i} is out of order.");
            }

            previous = current;
        }
    }

    /// <summary>
    ///     Every spelling is found by the binary search at its own index.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_Find_ReturnsOwnIndex()
    {
        var image = WordImage.Open(Committed.Value);

        for (var i = 0; i < image.SpellingCount; i++)
        {
            Assert.Equal(i, image.Find(image.Spelling(i)));
        }

        Assert.True(image.Find("zzqxjzzqxj") < 0);
    }

    /// <summary>
    ///     A wrong magic number, an unsupported version and a flipped byte are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_Corruption_IsRejected()
    {
        var bytes = Committed.Value;

        var magic = (byte[])bytes.Clone();
        magic[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => WordImage.Open(magic));

        var version = (byte[])bytes.Clone();
        version[4] ^= 0x7F;
        Assert.Throws<InvalidDataException>(() => WordImage.Open(version));

        var flipped = (byte[])bytes.Clone();
        flipped[flipped.Length / 2] ^= 0x55;
        Assert.Throws<InvalidDataException>(() => WordImage.Open(flipped));

        Assert.Throws<InvalidDataException>(() => WordImage.Open(bytes.AsSpan(0, 40).ToArray()));
    }

    /// <summary>
    ///     The committed image is of format version 4 and has no respelling sections (the ids 24 to 26), and its meta
    ///     section has eight values.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_Version4_HasNoRespellingSections()
    {
        // Arrange
        var bytes = Committed.Value;

        // Act
        var image = WordImage.Open(bytes);

        // Assert
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.DoesNotContain(image.SectionIds, id => id is >= 24 and <= 26);
        Assert.Equal(8, image.RawSection(ImageFormat.SectionMeta).Length / 4);
    }

    /// <summary>
    ///     An image of the previous format version (3, which still had the respelling sections) is rejected with a message
    ///     that names the version, with and without the content hash check.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_PreviousFormatVersion_IsRejected()
    {
        // Arrange: the committed image with the version of the previous format
        Assert.Equal(4u, ImageFormat.Version);
        var previous = (byte[])Committed.Value.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(previous.AsSpan(4), ImageFormat.Version - 1);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(previous));
        var noHashCheck = Assert.Throws<InvalidDataException>(() => WordImage.Open(previous, verifyHash: false));

        // Assert
        Assert.Contains("unsupported version", error.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported version", noHashCheck.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An image cut at the end of the header, at the end of the directory and at the start and end (and one byte
    ///     inside) of every section is rejected, with and without the content hash check.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_TruncatedAtEverySectionBoundary_IsRejected()
    {
        // Arrange: collect the boundaries from the directory
        var bytes = Committed.Value;
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        var cuts = new SortedSet<int> { 0, 4, ImageFormat.HeaderSize - 1, ImageFormat.HeaderSize, ImageFormat.HeaderSize + (count * ImageFormat.DirectoryEntrySize) };
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            var stored = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            cuts.Add(offset);
            cuts.Add(offset + 1);
            cuts.Add(offset + stored - 1);
        }

        // Act / Assert: every cut shorter than the image is rejected
        foreach (var cut in cuts.Where(c => c < bytes.Length))
        {
            var cutBytes = bytes.AsSpan(0, cut).ToArray();
            Assert.Throws<InvalidDataException>(() => WordImage.Open(cutBytes));
            Assert.Throws<InvalidDataException>(() => WordImage.Open(cutBytes, verifyHash: false));
        }
    }

    /// <summary>
    ///     A wrong content hash is rejected unless the check is switched off, and the source data hash is informational.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_WrongHash_IsRejectedByContentHashOnly()
    {
        // Arrange
        var content = (byte[])Committed.Value.Clone();
        content[ImageFormat.ContentHashOffset] ^= 0xFF;
        var source = (byte[])Committed.Value.Clone();
        source[ImageFormat.DataHashOffset] ^= 0xFF;

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => WordImage.Open(content));
        Assert.True(WordImage.Open(content, verifyHash: false).SpellingCount > 0);
        Assert.Equal(source[ImageFormat.DataHashOffset], WordImage.Open(source).DataHash[0]);
    }

    /// <summary>
    ///     A directory entry whose raw length disagrees with the section (a count mismatch), and a section count that
    ///     is too large for the file, are rejected; with the hash check off the meta section is rejected when the image
    ///     is opened, and every other section when it is read.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Image_CountMismatch_IsRejected()
    {
        // Arrange
        var bytes = Committed.Value;
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        var tooMany = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(tooMany.AsSpan(8), 65);
        Assert.Throws<InvalidDataException>(() => WordImage.Open(tooMany, verifyHash: false));

        // Act / Assert: lengthen the raw length of each section in turn
        for (var i = 0; i < count; i++)
        {
            var tampered = (byte[])bytes.Clone();
            var entry = tampered.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            var id = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], raw + 4);

            Assert.Throws<InvalidDataException>(() =>
            {
                WordImage.Open(tampered, verifyHash: false).RawSection(id);
            });
        }
    }
}
