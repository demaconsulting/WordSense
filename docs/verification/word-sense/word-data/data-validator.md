### DataValidator Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/DataValidatorTests.cs` import the slice, damage one thing of the copy (a
cell, a row, a file) and assert that the expected rule tag is reported, and that the undamaged copy is valid.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Valid data** (`ImportedSlice_IsValid`, `CompletePartition_IsValid`).
- **Schema and file set** (`MissingDirectory_ReportsSchema`, `BrokenSchema_ReportsSchema`, `UnexpectedFiles_ViolateFileSet`,
  `MissingSourceFile_ViolatesFileSet`, `EmptyPartitionFile_Violates`, `SplitPlanMismatch_ViolatesFileSet`).
- **Format and cells** (`FormatDefects_AreReported`, `CellDefects_AreReported`, `EmptyRequiredCell_Violates`,
  `ClassEntryFeatures_AreChecked`).
- **Keys and order** (`UnsortedRows_ViolateSort`, `DuplicateHomograph_ViolatesUnique`, `DanglingReference_ViolatesForeignKey`,
  `RowInWrongFile_ViolatesPlacement`, `RelationDefects_AreReported`).
- **Cross-table rules** (`HeadDefects_ViolateSynset`, `TokenDefects_ViolateEntry`, `SenseDefects_ViolateSense`,
  `EntryWithoutSense_Violates`, `ExampleOfOtherSynset_Violates`, `SynsetWithoutGloss_Violates`,
  `UncoveredSense_ViolatesPartition`, `SenseInTwoClasses_ViolatesUnique`).
- **Reporting** (`ManyViolations_AreCappedPerRule`).
