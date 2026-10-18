### OewnImporter Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/OewnImporterTests.cs` import the slice fixture (plain and gzip) into a
temporary folder and read the resulting TSV files; failure tests damage a copy of the fixture. The command-level tests
are in `DataEndToEndTests.cs`.

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
  `MissingSynset_Throws`).
- **Command** (`Import_PinnedHash_RejectsOtherFile`, `Import_ExistingTables_NeedsOverwrite`, `Import_NoInput_ReportsUsage`).
