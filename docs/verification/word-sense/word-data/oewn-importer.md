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
- **Safety** (`Twice_IsByteIdentical`, `HashPin_IsChecked`, `ExistingTables_RefusesWithoutOverwrite`) and rejected
  input (`MalformedXml_Throws`, `NoLexicon_Throws`, `UnknownRelation_Throws`, `SatelliteWithoutHead_Throws`,
  `MissingSynset_Throws`, `ImportPrevious_DuplicateSenseKey_Throws`).
- **Frequency counts** (`ImportCounts_JoinsOnSenseKey`, `ImportCounts_SenseWithoutEntry_HasCountZero`,
  `ImportCounts_HashPin_IsChecked`, `ImportCounts_MalformedLine_Throws`): the count of a line arrives on the sense with
  that key; a sense with no line has the count 0; a counts file whose SHA-256 differs from the pin is rejected before
  anything is written; a line that cannot be read throws.
- **Command** (`Import_PinnedHash_RejectsOtherFile`, `Import_ExistingTables_NeedsOverwrite`,
  `Import_NoInput_ReportsUsage`, `ImportWn30Counts_WritesTheTable`).
