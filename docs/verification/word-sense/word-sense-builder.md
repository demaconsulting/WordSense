# WordSenseBuilder Verification

## Verification Approach

Tests in `test/WordSense.Builder.Tests/BuilderTests.cs` use temporary directories and synthetic data;
the full corpus build is exercised by the accuracy gate of the system tests against the committed
table.

## Test Scenarios

- **WordSenseBuilder_Corpus_ReadSentences_ParsesRow**, **..._RejectsMalformedRow**,
  **WordSenseBuilder_Corpus_DirectoryAndDisjointness**, **WordSenseBuilder_Corpus_ReadForms_ParsesLabels**:
  corpus rules.
- **WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic**: separable data is separated and two
  runs agree exactly.
- **WordSenseBuilder_BatchImporter_ImportsValidatesAndIsIdempotent**: imported, invalid and missing
  batches; a second import changes nothing.
- **WordSenseBuilder_BatchImporter_MissingLemmaIsWarning**, **..._DropsFewBadRows**,
  **..._RejectsBatchWithManyBadRows**, **..._ExcludesThinLemmas**: lemma-level absence is a warning, few
  bad rows are dropped, many reject the batch, and thin lemmas are excluded.
- **WordSenseBuilder_BatchImporter_PartOfSpeechLabels_AreTagged**, **..._WritesDerivedInventory**,
  **..._UnmappedLabel_IsInvalid**,
  **WordSenseBuilder_PosRules_Validate_FlagsMixedAndNonPartOfSpeechTags**,
  **WordSenseBuilder_Program_ValidatePos_ReportsViolations**,
  **WordSenseBuilder_SenseInventory_Group_RoundTripsAndIsOptional**, **..._InvalidGroup_IsRejected**,
  **WordSenseBuilder_PosAudit_Resolve_FollowsTheRules**, **..._RowId_IsStable**,
  **WordSenseBuilder_Relabeler_Apply_RelabelsDropsAndExcludes**: part-of-speech tags only, group column,
  audit resolution and relabeling.
  **WordSenseBuilder_SenseInventory_ReadDirectory_RejectsMalformedRows**, **..._WriteAndRead_RoundTrips**,
  **..._Validate_ChecksBothDirections**, **..._Validate_ConsistentCorpus_Passes**,
  **WordSenseBuilder_TagEvaluator_CountsRowsPerRule**: inventory derivation, validation in both directions and
  tag-level counting.
- **WordSenseBuilder_RealJudge_Resolve_FollowsTheRules**, **WordSenseBuilder_RealImport_Validate_RequiresProvenance**:
  sense judging resolution, licence ranking, and the provenance gate (missing row flagged, complete sidecar accepted).
- **WordSenseBuilder_RealImport_Validate_RejectsBadProvenanceRows**,
  **WordSenseBuilder_RealImport_Validate_RejectsLongAndGutenbergSentences**: a disallowed licence, invalid origin,
  CC BY row without contributor, Gutenberg row without a pg key or Public domain licence, a sentence over 35 words,
  and a sentence mentioning Gutenberg are each rejected; a 35-word sentence is accepted.
- **WordSenseBuilder_TableBuilder_Build_ReportHasGroupAndTagSections**: a small build report contains the group-level
  and tag-level accuracy sections.
- **WordSenseBuilder_Corpus_ReadSentences_SplitsCompoundTarget**: a target inside a compound word is split at the
  braces.
- **WordSenseBuilder_Program_Run_ReportsErrors**: usage and data errors return exit code 1.
- **WordSenseBuilder_Program_Run_NonNumericOption_ReportsErrorNamingOption**: a non-numeric `--size`,
  `--max` or `--per-sense` returns exit code 1 with a message naming the option.

## Requirements Coverage

`WordSense-Builder-Corpus`, `WordSense-Builder-Trainer`, `WordSense-Builder-Importer`,
`WordSense-Builder-Inventory` and `WordSense-Builder-Provenance`.
