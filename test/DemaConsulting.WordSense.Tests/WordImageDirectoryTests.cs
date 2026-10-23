using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the validation of the section directory of the word image: every test rebuilds the real embedded image
///     with one region of its directory or of one section altered (and a correct content hash, so the damage is not
///     hidden behind the hash check) and expects the reader to reject it.
/// </summary>
public class WordImageDirectoryTests
{
    private const string ResourceName = "DemaConsulting.WordSense.wordsense.img";

    /// <summary>
    ///     The real image, taken apart and put together again without a change, is accepted; the rejections below come
    ///     from the damage alone.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_DirectoryRebuiltWithoutDamage_IsAccepted()
    {
        // Arrange
        var rebuilt = Rebuild(_ => { });

        // Act
        var image = WordImage.Open(rebuilt);
        image.ValidateCatalog();

        // Assert: the rebuilt image is the real one, byte for byte
        Assert.Equal(WordLexicon.Current.SpellingCount, image.SpellingCount);
        Assert.Equal(WordLexicon.Current.SenseCount, image.SenseCount);
        Assert.Equal(RealImage(), rebuilt);
    }

    /// <summary>
    ///     Bytes after the end of the Brotli stream of a compressed section are rejected when the section is read.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_TrailingBytesAfterCompressedStream_IsRejected()
    {
        // Arrange: every compressed section that has room for one more byte without looking stored as is. Some sections
        // are read when the image is opened and the others when they are first used, so the whole sequence is the act.
        var compressed = Parse(RealImage()).Where(s => !s.IsStored && s.Stored.Length + 1 < s.Raw).Select(s => s.Id).ToArray();
        Assert.NotEmpty(compressed);

        foreach (var id in compressed)
        {
            // Act: append one byte to that section
            var damaged = Rebuild(sections =>
            {
                var section = sections.Single(s => s.Id == id);
                section.Stored = [.. section.Stored, 0x00];
            });
            var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged).RawSection((int)id));

            // Assert
            Assert.Contains("trailing bytes", error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A section that starts inside the header or inside the directory is rejected when the image is opened.
    /// </summary>
    /// <param name="offset">The offset the first section is moved to.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(ImageFormat.HeaderSize - 1)]
    [InlineData(ImageFormat.HeaderSize)]
    [InlineData(ImageFormat.HeaderSize + ImageFormat.DirectoryEntrySize)]
    public void WordLexicon_Image_SectionStartingInHeaderOrDirectory_IsRejected(int offset)
    {
        // Arrange
        var damaged = Rebuild(_ => { }, sections => sections[0].Offset = (uint)offset);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged));

        // Assert
        Assert.Contains("outside its section area", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two sections whose byte ranges overlap, by one byte or entirely, are rejected when the image is opened.
    /// </summary>
    /// <param name="shift">The distance by which the second section starts after the first one.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WordLexicon_Image_OverlappingSections_IsRejected(int shift)
    {
        // Arrange: the second section starts inside the first
        var damaged = Rebuild(_ => { }, sections => sections[1].Offset = sections[0].Offset + (uint)shift);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged));

        // Assert
        Assert.Contains("overlapping", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Flags other than "stored as is" alone or a byte-plane stride from 1 to 255 are rejected.
    /// </summary>
    /// <param name="flags">The flags written to the first section.</param>
    [Theory]
    [InlineData(0x0u)]
    [InlineData(0x200u)]
    [InlineData(0x101u)]
    [InlineData(0x1FFu)]
    [InlineData(0x10001u)]
    [InlineData(0x80000000u)]
    public void WordLexicon_Image_UnsupportedFlags_IsRejected(uint flags)
    {
        // Arrange
        var damaged = Rebuild(sections => sections[0].Flags = flags);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged));

        // Assert
        Assert.Contains("unsupported flags", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A raw length that disagrees with the stored length or with the storage mode is rejected when the image is
    ///     opened, without decompressing anything.
    /// </summary>
    /// <param name="damage">The kind of inconsistency that is written to a compressed section with a stride above one.</param>
    [Theory]
    [InlineData("raw-equals-stored")]
    [InlineData("raw-zero-with-stored-bytes")]
    [InlineData("raw-not-a-multiple-of-stride")]
    [InlineData("stored-flag-with-other-raw")]
    [InlineData("compressed-without-bytes")]
    public void WordLexicon_Image_InconsistentLengths_IsRejected(string damage)
    {
        // Arrange: a compressed section whose stride is above one (a section of 32-bit values)
        var target = Parse(RealImage()).First(s => !s.IsStored && s.Flags > 1 && s.Raw > 8).Id;
        var damaged = Rebuild(sections =>
        {
            var section = sections.Single(s => s.Id == target);
            switch (damage)
            {
                case "raw-equals-stored":
                    section.Raw = (uint)section.Stored.Length;
                    break;
                case "raw-zero-with-stored-bytes":
                    section.Raw = 0;
                    break;
                case "raw-not-a-multiple-of-stride":
                    section.Raw++;
                    break;
                case "stored-flag-with-other-raw":
                    section.Flags = ImageFormat.FlagStored;
                    break;
                default:
                    section.Stored = [];
                    break;
            }
        });

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged));

        // Assert
        Assert.Contains("lengths disagree", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A section id that the directory lists twice is rejected.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_DuplicateSectionId_IsRejected()
    {
        // Arrange
        var damaged = Rebuild(sections => sections[1].Id = sections[0].Id);

        // Act
        var error = Assert.Throws<InvalidDataException>(() => WordImage.Open(damaged));

        // Assert
        Assert.Contains("duplicate section", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A damaged directory of the embedded image is reported at the public boundary as
    ///     <see cref="ModelIntegrityException"/> that carries the original <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void WordLexicon_OpenImage_OverlappingSections_ThrowsModelIntegrity()
    {
        // Arrange
        var damaged = Rebuild(_ => { }, sections => sections[1].Offset = sections[0].Offset);
        using var stream = new MemoryStream(damaged);

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => WordLexicon.OpenImage(stream));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains("overlapping", failure.Message, StringComparison.Ordinal);
    }

    private static byte[] RealImage()
    {
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded word image is missing.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    // Reads the directory of an image into editable entries that carry their stored bytes
    private static List<Section> Parse(byte[] data)
    {
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var sections = new List<Section>();
        for (var i = 0; i < count; i++)
        {
            var entry = data.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            var stored = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            sections.Add(new Section
            {
                Id = BinaryPrimitives.ReadUInt32LittleEndian(entry),
                Raw = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]),
                Offset = offset,
                Flags = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]),
                Stored = data.AsSpan((int)offset, (int)stored).ToArray(),
            });
        }

        return sections;
    }

    // Takes the real image apart, lets the test change the entries (their lengths, flags and bytes), lays the sections
    // out one after the other again, lets the test move them, and assembles an image with a correct content hash
    private static byte[] Rebuild(Action<List<Section>> change, Action<List<Section>>? move = null)
    {
        var real = RealImage();
        var sections = Parse(real);
        change(sections);

        var offset = (uint)(ImageFormat.HeaderSize + (sections.Count * ImageFormat.DirectoryEntrySize));
        foreach (var section in sections)
        {
            section.Offset = offset;
            offset += (uint)section.Stored.Length;
        }

        move?.Invoke(sections);

        var size = Math.Max((int)offset, ImageFormat.HeaderSize + (sections.Count * ImageFormat.DirectoryEntrySize));
        size = Math.Max(size, sections.Max(s => (int)s.Offset + s.Stored.Length));
        var result = new byte[size];
        foreach (var section in sections)
        {
            section.Stored.CopyTo(result, (int)section.Offset);
        }

        real.AsSpan(0, ImageFormat.HeaderSize).CopyTo(result);
        for (var i = 0; i < sections.Count; i++)
        {
            var entry = result.AsSpan(ImageFormat.HeaderSize + (i * ImageFormat.DirectoryEntrySize));
            BinaryPrimitives.WriteUInt32LittleEndian(entry, sections[i].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], sections[i].Raw);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)sections[i].Stored.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], sections[i].Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], sections[i].Flags);
        }

        SHA256.HashData(result.AsSpan(ImageFormat.HeaderSize)).CopyTo(result.AsSpan(ImageFormat.ContentHashOffset));
        return result;
    }

    // One directory entry with the bytes it stores
    private sealed class Section
    {
        public uint Id { get; set; }

        public uint Raw { get; set; }

        public uint Offset { get; set; }

        public uint Flags { get; set; }

        public byte[] Stored { get; set; } = [];

        public bool IsStored => Flags == ImageFormat.FlagStored;
    }
}
