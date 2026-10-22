using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DemaConsulting.WordSense;

/// <summary>
///     The constants of the binary word image (<c>wordsense.img</c>) shared by the reader in this assembly and the writer
///     of the build tool.
/// </summary>
/// <remarks>
///     <para>
///     Layout, all integers little-endian: a 112-byte header (magic <c>WSI1</c>, format version, section count, a reserved
///     word, the 32-byte SHA-256 of the source data at offset 16, the 32-byte SHA-256 of every byte from offset 112 at
///     offset 48 and the 32-byte inventory hash at offset 80), then a directory of 20-byte entries (section id, raw
///     length, stored length, offset, flags) and the sections. A section is Brotli-compressed; the low byte of its flags
///     is the stride of the byte-plane shuffle that was applied before compression (1 for none).
///     </para>
///     <para>
///     The spellings are sorted by their UTF-8 bytes (not by UTF-16 order), so the reader searches with byte
///     comparison. An analysis row is two 32-bit words: the entry index, then the feature index. There is one row for
///     every analysis of a spelling and none for a sense: the senses are in the catalog sections.
///     </para>
///     <para>
///     The catalog sections hold the sense inventory. Senses are numbered by the UTF-8 byte order of their ids. A lemma
///     block is a lemma and part of speech with the indexes of its senses in inventory order. A group has at least two
///     senses and never all the senses of its block; a sense is in at most one group. The inventory hash covers the
///     sections that name analyses, senses and groups (see <see cref="InventorySections"/>): models trained against
///     an image carry it and refuse an image with another one.
///     </para>
/// </remarks>
internal static class ImageFormat
{
    /// <summary>The format version this code reads and writes.</summary>
    public const uint Version = 3;

    /// <summary>The size of the fixed header.</summary>
    public const int HeaderSize = 112;

    /// <summary>The size of a directory entry.</summary>
    public const int DirectoryEntrySize = 20;

    /// <summary>The offset of the SHA-256 of the source data in the header.</summary>
    public const int DataHashOffset = 16;

    /// <summary>The offset of the SHA-256 of the content in the header.</summary>
    public const int ContentHashOffset = 48;

    /// <summary>The offset of the inventory hash in the header.</summary>
    public const int InventoryHashOffset = 80;

    /// <summary>The size of a hash.</summary>
    public const int HashSize = 32;

    /// <summary>The section that holds the counts (<c>uint[]</c>, see the <c>Meta*</c> constants).</summary>
    public const int SectionMeta = 1;

    /// <summary>The byte length of each spelling (<c>byte[]</c>).</summary>
    public const int SectionSpellingLength = 2;

    /// <summary>The UTF-8 bytes of all spellings, in UTF-8 byte order.</summary>
    public const int SectionSpellingBytes = 3;

    /// <summary>The number of analyses of each spelling (<c>byte[]</c>).</summary>
    public const int SectionAnalysisCount = 4;

    /// <summary>The analysis rows, 8 bytes each: the entry index and the feature index.</summary>
    public const int SectionAnalysisRows = 5;

    /// <summary>The byte length of each distinct feature string.</summary>
    public const int SectionFeatureLength = 8;

    /// <summary>The UTF-8 bytes of the distinct feature strings (index 0 is the empty string).</summary>
    public const int SectionFeatureBytes = 9;

    /// <summary>The byte length of each entry lemma.</summary>
    public const int SectionEntryLemmaLength = 10;

    /// <summary>The UTF-8 bytes of the entry lemmas; entries are sorted by lemma bytes.</summary>
    public const int SectionEntryLemmaBytes = 11;

    /// <summary>The part of speech of each entry (<c>byte[]</c>, a <see cref="PartOfSpeech"/> value).</summary>
    public const int SectionEntryPos = 12;

    /// <summary>The start of the forms of each entry in the form list (<c>uint[]</c>, one more than the entries).</summary>
    public const int SectionEntryFormStart = 13;

    /// <summary>The spelling indexes of the forms of the entries (<c>uint[]</c>, ascending within an entry).</summary>
    public const int SectionEntryForms = 14;

    /// <summary>The spelling indexes that have an upper-case letter, sorted by their lower-case text (<c>uint[]</c>).</summary>
    public const int SectionFold = 15;

    /// <summary>The byte length of each sense id, the senses in UTF-8 byte order of their ids (<c>byte[]</c>).</summary>
    public const int SectionSenseIdLength = 19;

    /// <summary>The UTF-8 bytes of the sense ids.</summary>
    public const int SectionSenseIdBytes = 20;

    /// <summary>The byte length of the key suffix of each sense, 0 for a project sense (<c>byte[]</c>).</summary>
    public const int SectionSenseKeyLength = 21;

    /// <summary>The UTF-8 bytes of the key suffixes: the part of the sense key after <c>lemma%</c>.</summary>
    public const int SectionSenseKeyBytes = 22;

    /// <summary>The group of each sense as the group index plus one, 0 for no group (<c>uint[]</c>).</summary>
    public const int SectionSenseGroup = 23;

    /// <summary>The respelling of each sense as the respelling index plus one, 0 for none (<c>uint[]</c>). Retained in the image format and validated, but not exposed by the API.</summary>
    public const int SectionSenseRespelling = 24;

    /// <summary>The byte length of each distinct respelling. Retained in the image format and validated, but not exposed by the API.</summary>
    public const int SectionRespellingLength = 25;

    /// <summary>The UTF-8 bytes of the distinct respellings. Retained in the image format and validated, but not exposed by the API.</summary>
    public const int SectionRespellingBytes = 26;

    /// <summary>The byte length of each group id; groups are in UTF-8 byte order of their ids.</summary>
    public const int SectionGroupIdLength = 27;

    /// <summary>The UTF-8 bytes of the group ids.</summary>
    public const int SectionGroupIdBytes = 28;

    /// <summary>The part of speech of each group (<c>byte[]</c>).</summary>
    public const int SectionGroupPos = 29;

    /// <summary>The start of the members of each group in the member list (<c>uint[]</c>, one more than the groups).</summary>
    public const int SectionGroupMemberStart = 30;

    /// <summary>The sense indexes of the members of the groups, in inventory order (<c>uint[]</c>).</summary>
    public const int SectionGroupMembers = 31;

    /// <summary>The byte length of the lemma of each lemma block.</summary>
    public const int SectionBlockLemmaLength = 32;

    /// <summary>The UTF-8 bytes of the block lemmas; blocks are sorted by lemma bytes, then part of speech.</summary>
    public const int SectionBlockLemmaBytes = 33;

    /// <summary>The part of speech of each lemma block (<c>byte[]</c>).</summary>
    public const int SectionBlockPos = 34;

    /// <summary>The start of the senses of each block in the sense list (<c>uint[]</c>, one more than the blocks).</summary>
    public const int SectionBlockSenseStart = 35;

    /// <summary>The sense indexes of the blocks in inventory order: the order of Open English WordNet (the position of the sense in its entry and the homograph number of the entry), project senses last (<c>uint[]</c>).</summary>
    public const int SectionBlockSenses = 36;

    /// <summary>The indexes of the senses that have a key, sorted by their full key bytes (<c>uint[]</c>).</summary>
    public const int SectionKeyIndex = 37;

    /// <summary>The index of the spelling count in the meta section.</summary>
    public const int MetaSpellingCount = 0;

    /// <summary>The index of the entry count in the meta section.</summary>
    public const int MetaEntryCount = 1;

    /// <summary>The index of the analysis row count in the meta section.</summary>
    public const int MetaRowCount = 2;

    /// <summary>The index of the feature count in the meta section.</summary>
    public const int MetaFeatureCount = 3;

    /// <summary>The index of the fold count in the meta section.</summary>
    public const int MetaFoldCount = 4;

    /// <summary>The index of the sense count in the meta section.</summary>
    public const int MetaSenseCount = 5;

    /// <summary>The index of the group count in the meta section.</summary>
    public const int MetaGroupCount = 6;

    /// <summary>The index of the lemma block count in the meta section.</summary>
    public const int MetaBlockCount = 7;

    /// <summary>The index of the respelling count in the meta section.</summary>
    public const int MetaRespellingCount = 8;

    /// <summary>The number of values in the meta section.</summary>
    public const int MetaLength = 9;

    /// <summary>The flag bit of a section that is stored as is.</summary>
    public const int FlagStored = 0x100;

    /// <summary>
    ///     The sections the inventory hash covers: those that name analyses (spellings, rows, features, entries), senses
    ///     (ids and the order of the senses of a lemma) and groups (ids, parts of speech, members and the group of each
    ///     sense). Keys and respellings are not part of the identity of the inventory.
    /// </summary>
    public static readonly int[] InventorySections =
    [
        SectionSpellingLength,
        SectionSpellingBytes,
        SectionAnalysisCount,
        SectionAnalysisRows,
        SectionFeatureLength,
        SectionFeatureBytes,
        SectionEntryLemmaLength,
        SectionEntryLemmaBytes,
        SectionEntryPos,
        SectionSenseIdLength,
        SectionSenseIdBytes,
        SectionSenseGroup,
        SectionGroupIdLength,
        SectionGroupIdBytes,
        SectionGroupPos,
        SectionGroupMemberStart,
        SectionGroupMembers,
        SectionBlockLemmaLength,
        SectionBlockLemmaBytes,
        SectionBlockPos,
        SectionBlockSenseStart,
        SectionBlockSenses,
    ];

    /// <summary>Gets the magic number at the start of an image.</summary>
    public static ReadOnlySpan<byte> Magic => "WSI1"u8;

    /// <summary>
    ///     Computes the inventory hash: the SHA-256 over the sections of <see cref="InventorySections"/> in ascending
    ///     section order, each as its id and its raw length (32-bit little-endian) followed by its raw bytes.
    /// </summary>
    /// <param name="rawSection">Gets the raw (decompressed) bytes of a section; an absent section is empty.</param>
    /// <returns>The 32-byte hash.</returns>
    public static byte[] ComputeInventoryHash(Func<int, byte[]> rawSection)
    {
        ArgumentNullException.ThrowIfNull(rawSection);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> head = stackalloc byte[8];
        foreach (var id in InventorySections.Order())
        {
            var bytes = rawSection(id);
            BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)id);
            BinaryPrimitives.WriteUInt32LittleEndian(head[4..], (uint)bytes.Length);
            sha.AppendData(head);
            sha.AppendData(bytes);
        }

        return sha.GetHashAndReset();
    }
}
