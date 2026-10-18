// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
        return File.ReadAllBytes(Path.Combine(root, ImageCompiler.DefaultOutput.Replace('/', Path.DirectorySeparatorChar)));
    });

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WordSense.slnx")))
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
        var data = Path.Combine(root, "data");
        var corpus = Path.Combine(root, "corpus");

        var a = WordImage.Open(ImageCompiler.Build(data, corpus).Image);
        var b = WordImage.Open(ImageCompiler.Build(data, corpus).Image);

        Assert.True(a.DataHash.SequenceEqual(b.DataHash));
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
        for (var i = 0; i < image.SpellingCount; i += 7)
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
}
