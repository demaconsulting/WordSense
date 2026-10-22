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
- **Suffix lookup** (`BasicTables_FindSuffix_KnownAndUnknown`): the longest known suffix gives a distribution over the
  open classes; an unknown ending gives none.
- **Bad magic** (`BasicTables_Load_BadMagic_Throws`): a wrong magic number throws `InvalidDataException`.
- **Not compressed** (`BasicTables_Load_NotCompressed_Throws`): data that is not Brotli throws
  `InvalidDataException`.
- **Truncated** (`BasicTables_Load_Truncated_Throws`, `BasicTables_Load_TruncatedAtEveryLength_Throws`): a truncated
  blob, cut at any byte and so at every section boundary, throws `InvalidDataException`.
- **Wrong version or tag count** (`BasicTables_Load_WrongVersionOrTagCount_Throws`): another format version in the
  magic number, or a tag count that differs from the tag set, throws `InvalidDataException`.
- **Trailing bytes** (`BasicTables_Load_TrailingBytes_Throws`): extra bytes at the end throw `InvalidDataException`.
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
- **Shipped table resolves** (`BasicTables_Default_ShippedTable_ResolvesEveryCueClass`): every cue analysis of the
  shipped table resolves to an analysis of the embedded word image.
- **Feature hash** (`FeatureRef_Hash_PrefixAndDeterminism`): the hash of an immediate-neighbor feature equals the hash
  of the window token that spells its prefix, and the hash is stable.
