### OewnImporter Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/OewnImporterTests.cs` import the slice fixture (plain and gzip) into a
temporary folder and read the resulting TSV files; failure tests damage a copy of the fixture. The count import is
tested on the imported slice with a small hand-written counts file. The command-level tests are in
`DataEndToEndTests.cs`.

#### Test Environment

Standard xUnit v3 runner; temporary files; no network.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Rows and validity** (`Slice_CountsRows`, `Slice_IsValid`, `GzipInput_Works`): every entry, synset, sense, gloss,
  example, relation and pronunciation arrives.
- **Derivations** (`Entries_DeriveHomographsAndPos`, `SynsetsAndSenses_DeriveHeadAndKey`, `Relations_StoredOnce`,
  `TabInText_IsCleaned`).
- **Layout** (`TextTables_KeepOrder`, `NonAsciiLemmas_GoToUnderscoreFile`, `LowThreshold_SplitsAndUpdatesSchema`).
- **Safety** (`Twice_IsByteIdentical`, `HashPin_IsChecked`, `ExistingTables_RefusesWithoutOverwrite`,
  `OverwriteWithInvalidInput_KeepsExistingTables`, `FailingMoveMidReplace_RestoresEveryOriginalFile`) and rejected
  input (`MalformedXml_Throws`, `NoLexicon_Throws`,
  `UnknownRelation_Throws`, `SatelliteWithoutHead_Throws`, `MissingSynset_Throws`,
  `RelationToMissingEndpoint_Throws`): the overwrite test imports over existing tables with an invalid input and checks
  that every file is byte-identical afterwards and no staging folder remains; the endpoint test points a synset
  relation and a sense relation at something the file does not hold. The failing-move test (with a split threshold that
  changes the schema, with and without an existing id map) makes the first, a middle, the second-to-last or the last move
  of the replacement fail through the `beforeMove` seam and checks that every file is byte-identical afterwards and no
  staging or backup folder remains.
- **Forms** (`ReadForms_ReturnsCleanedFormsPerEntry`, `ReadForms_WrongHashOrMalformedXml_Throws`): the cleaned spellings
  by entry in file order, an entry without a form absent, and a wrong hash or malformed XML refused.
- **Earlier release** (`ImportPrevious_ChangedIds_MapBySenseKeyAndMajority`, `ImportPrevious_UniqueSenseKeys_Imports`,
  `ImportPrevious_DuplicateSenseKey_Throws`): a changed sense id maps by its key, a changed entry id to the entry of its
  senses, an unchanged id gets no row, an id without a successor is counted as removed, and a duplicate sense key throws.
- **Frequency counts** (`ImportCounts_JoinsOnSenseKey`, `ImportCounts_SenseWithoutEntry_HasCountZero`,
  `ImportCounts_HashPin_IsChecked`, `ImportCounts_MalformedLine_Throws`): the count of a line arrives on the sense with
  that key; a sense with no line has the count 0; a counts file whose SHA-256 differs from the pin is rejected before
  anything is written; a line that cannot be read throws.
- **Command** (`Import_PinnedHash_RejectsOtherFile`, `Import_ExistingTables_NeedsOverwrite`,
  `Import_NoInput_ReportsUsage`, `ImportWn30Counts_WritesTheTable`).
