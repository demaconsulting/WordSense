namespace DemaConsulting.WordSense;

/// <summary>
///     The constants of the binary word image (<c>wordsense.img</c>) shared by the reader in this assembly and the writer
///     of the build tool.
/// </summary>
/// <remarks>
///     <para>
///     Layout, all integers little-endian: an 80-byte header (magic <c>WSI1</c>, format version, section count, a reserved
///     word, the 32-byte SHA-256 of the source data at offset 16 and the 32-byte SHA-256 of every byte from offset 80 at
///     offset 48), then a directory of 20-byte entries (section id, raw length, stored length, offset, flags) and the
///     sections. A section is Brotli-compressed; the low byte of its flags is the stride of the byte-plane shuffle
///     that was applied before compression (1 for none).
///     </para>
///     <para>
///     The spellings are sorted by their UTF-8 bytes (not by UTF-16 order), so the reader searches with byte
///     comparison. An analysis row is two 32-bit words: the entry index, then the feature index in the low 16 bits and
///     the scope list index plus one in the high 16 bits (zero for no scope).
///     </para>
/// </remarks>
internal static class ImageFormat
{
    /// <summary>The magic number at the start of an image.</summary>
    public static ReadOnlySpan<byte> Magic => "WSI1"u8;

    /// <summary>The format version this code reads and writes.</summary>
    public const uint Version = 2;

    /// <summary>The size of the fixed header.</summary>
    public const int HeaderSize = 80;

    /// <summary>The size of a directory entry.</summary>
    public const int DirectoryEntrySize = 20;

    /// <summary>The offset of the SHA-256 of the source data in the header.</summary>
    public const int DataHashOffset = 16;

    /// <summary>The offset of the SHA-256 of the content in the header.</summary>
    public const int ContentHashOffset = 48;

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

    /// <summary>The analysis rows, 8 bytes each.</summary>
    public const int SectionAnalysisRows = 5;

    /// <summary>The start of every scope list in the scope items (<c>uint[]</c>, one more than the lists).</summary>
    public const int SectionScopeStart = 6;

    /// <summary>The class indexes of the scope lists (<c>uint[]</c>).</summary>
    public const int SectionScopeItems = 7;

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

    /// <summary>The byte length of each class id.</summary>
    public const int SectionClassIdLength = 16;

    /// <summary>The UTF-8 bytes of the class ids, in UTF-8 byte order.</summary>
    public const int SectionClassIdBytes = 17;

    /// <summary>The part of speech of each class (<c>byte[]</c>).</summary>
    public const int SectionClassPos = 18;

    /// <summary>The index of the spelling count in the meta section.</summary>
    public const int MetaSpellingCount = 0;

    /// <summary>The index of the entry count in the meta section.</summary>
    public const int MetaEntryCount = 1;

    /// <summary>The index of the analysis row count in the meta section.</summary>
    public const int MetaRowCount = 2;

    /// <summary>The index of the scope list count in the meta section.</summary>
    public const int MetaScopeCount = 3;

    /// <summary>The index of the class count in the meta section.</summary>
    public const int MetaClassCount = 4;

    /// <summary>The index of the feature count in the meta section.</summary>
    public const int MetaFeatureCount = 5;

    /// <summary>The index of the fold count in the meta section.</summary>
    public const int MetaFoldCount = 6;

    /// <summary>The number of values in the meta section.</summary>
    public const int MetaLength = 7;

    /// <summary>The flag bit of a section that is stored as is.</summary>
    public const int FlagStored = 0x100;
}
