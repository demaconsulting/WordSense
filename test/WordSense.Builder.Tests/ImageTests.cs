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
        Assert.True(a.ClassHash.SequenceEqual(b.ClassHash));
        foreach (var id in a.SectionIds)
        {
            Assert.True(a.RawSection(id).AsSpan().SequenceEqual(b.RawSection(id)), $"Section {id} differs.");
        }
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

        for (var i = 0; i < image.SpellingCount; i += 1013)
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
