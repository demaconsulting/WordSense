### DataValidator Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/DataValidatorTests.cs` import the slice, damage one thing of the copy (a
cell, a row, a file) and assert that the expected rule tag is reported, and that the undamaged copy is valid. The
sense and group rules use a small hand-written inventory (labels, groups and a baseline copy)
added to the imported slice.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Valid data** (`ImportedSlice_IsValid`, `CuratedGroupOfSomeSenses_IsValid`): the imported slice, and labeled data
  with no group or with a curated group of some of the senses of a lemma and part of speech.
- **Schema and file set** (`MissingDirectory_ReportsSchema`, `BrokenSchema_ReportsSchema`,
  `UnexpectedFiles_ViolateFileSet`,
  `MissingSourceFile_ViolatesFileSet`, `EmptyPartitionFile_Violates`, `SplitPlanMismatch_ViolatesFileSet`).
- **Unreadable input is a violation** (`UnreadableSchema_ReportsSchemaViolation`,
  `UnreadableTableFile_ReportsViolationAndContinues`,
  `DirectoryThatCannotBeListed_ReportsViolationAndContinues`, `PartitionFolderDeniedAndAbsentFromDisk_ReportsViolation`,
  `PartitionFolderMissing_IsEmptyTable`, `FileInPlaceOfPartitionFolder_ReportsViolation`,
  `DataFolderMissingOrDenied_ReportsViolation`): a schema that is a folder (which the platform reports as
  an unauthorized access) gives the one `schema` violation that names the path; a table file that the reader seam
  fails to read (with an I/O failure or a denied access, injected so that the test does not depend on the file locking
  or the permissions of the operating system) gives an `unreadable` violation that names the file while the later
  tables are still read; a data folder, and the
  folder of a partitioned table, whose listing fails (through the internal lister seam of `Validate`, with a listing
  that fails while it is being read) give a `file-set` violation that names the directory while the other tables are
  still read. The listing, not an existence probe, tells a missing folder from a denied one: a folder that is absent
  from the disk but denied by the lister is a violation, a really missing folder of a partitioned table is an empty
  table, a missing data folder is a violation, and a file in place of a partition folder is a violation on every
  platform. None of these throws.
- **Format and cells** (`FormatDefects_AreReported`, `CellDefects_AreReported`, `EmptyRequiredCell_Violates`,
  `SenseTableFeatures_AreChecked`): the last one checks the features of an analysis of the form table against the
  feature vocabulary, including a feature name that is listed twice.
- **Keys and order** (`UnsortedRows_ViolateSort`, `DuplicateHomograph_ViolatesUnique`,
  `DanglingReference_ViolatesForeignKey`,
  `RowInWrongFile_ViolatesPlacement`, `RelationDefects_AreReported`).
- **Cross-table rules** (`HeadDefects_ViolateSynset`, `TokenDefects_ViolateEntry`, `SenseDefects_ViolateSense`,
  `SenseKeyOfOtherPartOfSpeech_ViolatesSense`, `EntryWithoutSense_Violates`, `ExampleOfOtherSynset_Violates`,
  `SynsetWithoutGloss_Violates`): the part of speech of an entry and the ss_type of a sense key must fit the part of
  speech of the synset of the sense (an adjective satellite counts as an adjective).
- **Sense labels** (`SenseWithoutLabel_Violates`, `DuplicateLabelInLemma_ViolatesUnique`, `DuplicateIds_AreReportedNotThrown`,
  `SenseIdEqualToGroupId_Violates`, `ChangedPublishedLabel_ViolatesStability`, `ChangedGroupId_IsNotAStabilityViolation`,
  `ReusedRetiredId_Violates`, `ProjectSenseWithKey_Violates`): a sense without a label, a label twice in a lemma, a
  sense label with the marker that makes its id equal to a group id, a label that differs from the baseline without a map
  row, a group that is renamed or dropped against the baseline (accepted, because group ids are not frozen, while the
  same change of a sense label is reported), an id that the map retires and a current sense uses, and a project sense
  that has a sense key are each reported (or accepted) with the expected rule tag.
- **Groups** (`GroupOfOneSense_Violates`, `GroupOfAllSensesOfPartOfSpeech_Violates`, `SenseInTwoGroups_ViolatesUnique`,
  `GroupAcrossPartsOfSpeech_Violates`, `GroupAcrossLemmas_Violates`, `GroupWithTwoRespellings_Violates`,
  `DefaultGroupOrigin_Violates`, `GroupIdWithoutMarker_Violates`, `GroupIdOfAnotherLemma_Violates`,
  `GroupLabelNotASlug_Violates`, `GroupKind_MustBeInTheVocabulary`): a group of one sense, a group with every sense of its
  lemma and part of speech, a sense in two groups, a group with two parts of speech, a group with a sense of another
  lemma, a group with two respellings, a group of the retired origin `default`, a group id without the marker `group:`,
  a group id with another lemma, a group label that is not a slug, and a group without a kind or with a kind outside the
  `group_kind` vocabulary are each reported; both kinds are valid. A lemma needs no group, so data without any group is
  valid (`CuratedGroupOfSomeSenses_IsValid` first checks the labeled slice without a group). The slice has only two
  nouns of bass, so the tests add a third one to make a group of some, but not all, senses possible.
- **Reporting** (`ManyViolations_AreCappedPerRule`): at most 20 listed per rule, while the total counts all.
- **More inventory cases** (`MalformedLabel_Violates`, `PublishedIdTakenByAnotherSense_ViolatesStability`,
  `MissingBaseline_ReportsStability`, `ProjectSenseWithoutGloss_Violates`): a label that is not lower-case ASCII, a
  published id that another sense now has, a baseline folder that does not exist and a project sense without a gloss.
