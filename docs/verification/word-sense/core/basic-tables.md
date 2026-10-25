### BasicTables Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/BasicTablesTests.cs` load small toy tables that `TestTables`
builds in the layout of the builder's writer, and corrupted variants made by editing the decompressed bytes and
recompressing them (a changed inventory hash, an analysis the word image does not have, a repeated analysis). One test
loads the real embedded table. Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Toy table reads back** (`BasicTables_Load_ToyTable_ReadsContent`): the header values, the words with their masks
  and counts, the suffixes and the cue table (analyses, hashes, weights) equal what was written.
- **Transitions are distributions** (`BasicTables_Load_Transitions_AreDistributions`): the interpolated transitions of
  an observed context sum to one.
- **Suffix lookup** (`BasicTables_TryFindSuffix_KnownAndUnknown`): the longest known suffix gives a distribution over
  the open classes as a read-only view; an unknown ending gives `false` and the default array.
- **Bad magic** (`BasicTables_Load_BadMagic_Throws`): a wrong magic number throws `InvalidDataException`.
- **Not compressed** (`BasicTables_Load_NotCompressed_Throws`): data that is not Brotli throws
  `InvalidDataException`.
- **Truncated** (`BasicTables_Load_Truncated_Throws`, `BasicTables_Load_TruncatedAtEveryLength_Throws`): a truncated
  blob, cut at any byte and so at every section boundary, throws `InvalidDataException`.
- **Wrong version or tag count** (`BasicTables_Load_WrongVersionOrTagCount_Throws`): another format version in the
  magic number, or a tag count that differs from the tag set, throws `InvalidDataException`.
- **Retired format WSB3** (`BasicTables_Load_PreviousFormatWsb3_ThrowsUnknownFormat`): a current table whose magic is set
  back to the previous format `WSB3`, which has no tense weight and no tense table, throws `InvalidDataException`
  whose message says unknown format.
- **Tense section read back** (`BasicTables_Load_TenseTable_ReadsWeightPriorHashesAndWeights`): the weight of the tense
  model from the header (0.75) and the prior, the ascending hashes and the weights of the tense table read back.
- **No tense section** (`BasicTables_Load_WithoutTenseSection_HasNoTenseTableAndTheDefaultWeight`): a table without the
  section has no tense table (null) and the default weight 1.
- **Malformed tense section** (`BasicTables_Load_InvalidTenseSection_Throws`): a section with an unknown flag, without a
  feature, with unordered hashes or with fewer weights than hashes throws `InvalidDataException`.
- **Truncated tense section** (`BasicTables_Load_TenseTableTruncatedAtEveryLength_Throws`): a table with a tense section
  cut at any byte throws `InvalidDataException`.
- **Tense table is read-only** (`TenseCue_Constructor_DefaultOrUnequalArrays_Throws`, `BasicTables_Properties_ExposeNoArrays`):
  a `TenseCue` refuses default (uninitialized) arrays and arrays of different lengths with `ArgumentException`, and
  no public property of `TenseCue` is an array.
- **Trailing bytes** (`BasicTables_Load_TrailingBytes_Throws`): extra bytes at the end of the decompressed content throw
  `InvalidDataException`.
- **Bytes after the compressed stream** (`BasicTables_Load_BytesAfterCompressedStream_Throws`): a complete Brotli
  stream followed by one more byte throws `InvalidDataException` whose message names the trailing bytes.
- **Unordered hashes** (`BasicTables_Load_UnorderedHashes_Throws`): feature hashes that are not ascending throw
  `InvalidDataException`.
- **Other inventory** (`BasicTables_Load_OtherInventoryHash_ThrowsNamingBothHashes`): a table whose analysis inventory
  hash differs from the word image throws `InvalidDataException`, and the message names both hashes.
- **Unknown analysis** (`BasicTables_Load_UnknownAnalysis_Throws`): a cue column named by an analysis that the word
  image does not have throws `InvalidDataException`.
- **Duplicate analysis** (`BasicTables_Load_DuplicateAnalysis_Throws`): an analysis that occurs twice in one lemma table
  throws `InvalidDataException`.
- **Column of an analysis** (`LemmaCue_RowOf_Analysis_UsesTheColumnOfItsFeatures`): two analyses of one part of speech
  (for example the past participle and the present of `read`) resolve to different columns: the column whose feature
  items are all contained in the features of the analysis and that has the most items wins; an analysis without a
  column of its own uses the column with empty features (any features) of its lemma and part of speech.
- **Embedded table loads** (`BasicTables_Default_LoadsEmbeddedTable`,
  `BasicTables_LoadEmbedded_EmbeddedResource_Loads`):
  the table embedded in the assembly loads, from the default access and from its own resource stream, and has
  statistics (words, suffixes and cue tables).
- **Missing embedded table** (`BasicTables_LoadEmbedded_MissingResource_ThrowsModelIntegrity`): a missing resource
  stream throws `ModelIntegrityException` whose message says the table is missing and whose inner exception is the
  original `InvalidDataException`.
- **Corrupt or foreign embedded table** (`BasicTables_LoadEmbedded_CorruptOrForeignResource_ThrowsModelIntegrity`,
  theory): bytes that are not a table, and a well-formed table of another inventory, each throw
  `ModelIntegrityException` that carries the original `InvalidDataException`.
- **Real table with an altered region**
  (`BasicTables_LoadEmbedded_RealTableWithAlteredRegion_ThrowsModelIntegrity`, theory): the real embedded table with a
  byte appended after its compressed stream, with one byte of its inventory hash changed (decompressed, edited and
  recompressed), and cut short, throws `ModelIntegrityException` that carries the original `InvalidDataException`; the
  unaltered table loads first in the same test.
- **Statistics are read-only** (`BasicTables_Properties_ExposeNoArrays`, `BasicTables_Load_Views_AreIndependentOfTheBlob`,
  `BasicTables_Constructors_DefaultArray_Throws`): no public property of `BasicTables`, `WordEntry`, `LemmaCue` or
  `TenseCue` is an
  array (reflection), the views of a loaded table do not change when the source blob is overwritten afterwards and have
  the sizes of the tag set, and a word entry or cue table refuses a default (uninitialized) immutable array.
- **Shipped table resolves** (`BasicTables_Default_ShippedTable_ResolvesEveryCueClass`): every cue analysis of the
  shipped table resolves to an analysis of the embedded word image.
- **Feature hash** (`FeatureRef_Hash_PrefixAndDeterminism`, `FeatureRef_Hash_TenseKindsHaveOwnPrefixes`): the hash of an
  immediate-neighbor feature equals the hash of the window token that spells its prefix, the hash is stable, and the tense
  kinds have the prefixes `LW:`, `RW:` and `NA:` while the cue kinds keep `L1:` and `R1:` (so the cue tables stay valid).
