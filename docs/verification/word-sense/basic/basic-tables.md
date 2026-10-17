### BasicTables Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicTablesTests.cs` load small toy tables that `TestTables` builds in
the layout of the builder's writer, and corrupted variants made by editing the decompressed bytes and recompressing
them. One test loads the real embedded table. Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Toy table reads back** (`BasicTables_Load_ToyTable_ReadsContent`): the header values, the words with their masks
  and counts, the suffixes and the cue table (senses, hashes, weights) equal what was written.
- **Transitions are distributions** (`BasicTables_Load_Transitions_AreDistributions`): the interpolated transitions of
  an observed context sum to one.
- **Suffix lookup** (`BasicTables_FindSuffix_KnownAndUnknown`): the longest known suffix gives a distribution over the
  open classes; an unknown ending gives none.
- **Bad magic** (`BasicTables_Load_BadMagic_Throws`): a wrong magic number throws `InvalidDataException`.
- **Not compressed** (`BasicTables_Load_NotCompressed_Throws`): data that is not Brotli throws
  `InvalidDataException`.
- **Truncated** (`BasicTables_Load_Truncated_Throws`): a truncated blob throws `InvalidDataException`.
- **Trailing bytes** (`BasicTables_Load_TrailingBytes_Throws`): extra bytes at the end throw `InvalidDataException`.
- **Unordered hashes** (`BasicTables_Load_UnorderedHashes_Throws`): feature hashes that are not ascending throw
  `InvalidDataException`.
- **Embedded table loads** (`BasicTables_Default_LoadsEmbeddedTable`): the table embedded in the assembly loads and has
  statistics (words, suffixes and cue tables).
- **Feature hash** (`FeatureRef_Hash_PrefixAndDeterminism`): the hash of an immediate-neighbor feature equals the hash
  of the window token that spells its prefix, and the hash is stable.
