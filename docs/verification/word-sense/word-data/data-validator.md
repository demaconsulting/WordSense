### DataValidator Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/DataValidatorTests.cs` import the slice, damage one thing of the copy (a
cell, a row, a file) and assert that the expected rule tag is reported, and that the undamaged copy is valid. The
sense, group and legacy map rules use a small hand-written inventory (labels, groups, a baseline copy and a legacy map)
added to the imported slice.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Valid data** (`ImportedSlice_IsValid`, `CompleteGroups_IsValid`).
- **Schema and file set** (`MissingDirectory_ReportsSchema`, `BrokenSchema_ReportsSchema`,
  `UnexpectedFiles_ViolateFileSet`,
  `MissingSourceFile_ViolatesFileSet`, `EmptyPartitionFile_Violates`, `SplitPlanMismatch_ViolatesFileSet`).
- **Format and cells** (`FormatDefects_AreReported`, `CellDefects_AreReported`, `EmptyRequiredCell_Violates`,
  `SenseTableFeatures_AreChecked`).
- **Keys and order** (`UnsortedRows_ViolateSort`, `DuplicateHomograph_ViolatesUnique`,
  `DanglingReference_ViolatesForeignKey`,
  `RowInWrongFile_ViolatesPlacement`, `RelationDefects_AreReported`).
- **Cross-table rules** (`HeadDefects_ViolateSynset`, `TokenDefects_ViolateEntry`, `SenseDefects_ViolateSense`,
  `EntryWithoutSense_Violates`, `ExampleOfOtherSynset_Violates`, `SynsetWithoutGloss_Violates`).
- **Sense labels** (`SenseWithoutLabel_Violates`, `DuplicateLabelInLemma_ViolatesUnique`,
  `SenseIdEqualToGroupId_Violates`, `ChangedPublishedLabel_ViolatesStability`, `ReusedRetiredId_Violates`,
  `ProjectSenseWithKey_Violates`): a sense without a label, a label twice in a lemma, a sense id that is also a group
  id,
  a label that differs from the baseline without a map row, an id that the map retires and a current sense uses, and a
  project sense that has a sense key are each reported with the expected rule tag.
- **Groups** (`UncoveredSense_ViolatesPartition`, `SenseInTwoGroups_ViolatesUnique`,
  `GroupAcrossPartsOfSpeech_Violates`, `GroupWithTwoRespellings_Violates`,
  `OtherLemma_NeedsOneDefaultGroupPerPartOfSpeech`): a sense in no group, a sense in two groups, a group with two
  parts of speech, a group with two respellings, and a lemma that is not curated with no default group or two for one
  part of
  speech are each reported.
- **Legacy map** (`LegacyMapTargetMissing_Violates`, `LegacyMapDuplicateClass_Violates`,
  `LegacyMapLevelMismatch_Violates`): a target that does not exist, an old class id that appears twice and a level that
  does not fit the target are each reported.
- **Reporting** (`ManyViolations_AreCappedPerRule`).
