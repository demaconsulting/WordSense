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
- **WordSenseBuilder_BatchImporter_PartOfSpeechLabels_AreAssigned**, **..._WritesDerivedInventory**,
  **..._UnmappedLabel_IsInvalid**: only part-of-speech labels are accepted when an inventory is derived, and the
  derived inventory carries the part of speech of each label and no meaning (imported lemmas are not heteronyms).
- **WordSenseBuilder_SenseInventory_ValidateRules_FlagsViolations**,
  **WordSenseBuilder_SenseInventory_ValidateRules_AllowsTwoSensesOfOnePartOfSpeechWithDifferentMeanings**,
  **WordSenseBuilder_Program_ValidatePos_ReportsViolations**: each inventory rule violation (undefined part of
  speech, meanings on only some senses, a heteronym with fewer than two distinct meanings, a repeated part of
  speech and meaning, inconsistent respellings, a shared respelling) is reported, two senses of one part of speech
  with different meanings pass, and `validate-pos` exits 1 listing the violations.
- **WordSenseBuilder_SenseInventory_MeaningAndRespelling_RoundTripAndAreOptional**,
  **..._InvalidColumns_AreRejected**: the five-column inventory round-trips with empty meaning and respelling
  allowed, and an invalid part of speech, meaning or respelling is rejected.
- **WordSenseBuilder_PosAudit_RowId_IsStable**: the retained sentence-identifier helper is stable and depends on the
  lemma and the sentence.
- **WordSenseBuilder_SenseInventory_ReadDirectory_RejectsMalformedRows**, **..._WriteAndRead_RoundTrips**,
  **..._Validate_ChecksBothDirections**, **..._Validate_ConsistentCorpus_Passes**,
  **WordSenseBuilder_AccuracyEvaluator_CountsPosAndPronunciationLevels**: inventory reading and writing,
  validation in both directions, and counting of the part-of-speech and pronunciation levels (the meaning level is
  counted by `TableBuilder`, see the build report tests).
- **WordSenseBuilder_RealJudge_Resolve_FollowsTheRules**, **WordSenseBuilder_RealImport_Validate_RequiresProvenance**:
  sense judging resolution, licence ranking, and the provenance gate (missing row flagged, complete sidecar accepted).
- **WordSenseBuilder_RealImport_Validate_RejectsBadProvenanceRows**,
  **WordSenseBuilder_RealImport_Validate_RejectsLongAndGutenbergSentences**: a disallowed licence, invalid origin,
  CC BY row without contributor, an authored row without the project licence, Gutenberg row without a pg key or
  Public domain licence, a sentence over 35 words,
  and a sentence mentioning Gutenberg are each rejected; a 35-word sentence is accepted.
- **WordSenseBuilder_RealImport_Validate_CoversNewLemmaBatchesAndAcceptsAuthoredOrigin**: a `new-*.tsv` anchor file
  without provenance is flagged and a complete sidecar with an `authored` row and the project licence is accepted.
- **WordSenseBuilder_RealImport_Import_EverySentenceHasProvenanceAndValidates**: importing real sentences into a
  synthetic corpus writes a provenance row for every written sentence (real and project-authored) and the result
  passes `RealImport.Validate`.
- **WordSenseBuilder_RealImport_Import_MissingDecisions_WritesNothing**: a missing `relabel-decisions.tsv` fails the
  import before any output file is written.
- **WordSenseBuilder_RealJudge_ParseAnswers_RejectsMalformedAnswers**: a short row, unexpected or duplicate id,
  unknown sense, bad confidence, a missing note for `L` or `none`, and unanswered ids are each rejected.
- **WordSenseBuilder_RealJudge_Merge_MissingJudgeC_Throws**,
  **WordSenseBuilder_RealJudge_Merge_AdjudicatedRow_InWorkDirectoryNamedOut**: a row needing adjudication without a
  judge C answer fails the merge; with one it is adjudicated, also when the work directory name contains `.out`.
- **WordSenseBuilder_RealJudge_MalformedRows_AreDataErrors**: rows with too few columns in `rows.tsv`, the judge
  input files, `dropped-rows.tsv` and the candidates file raise an error naming the file and line.
- **WordSenseBuilder_RealImport_Import_MalformedDecisionsRow_Throws**: a `relabel-decisions.tsv` row without exactly 9
  columns fails the import with an error naming the file and line, and nothing is written.
- **WordSenseBuilder_RealGap_Import_WritesSeparateAnchorsAndHoldoutWithProvenance**: a gap batch writes the planned
  number of anchors and holdout rows to `real-gap-1.tsv` files with a provenance row (origin `real`) for each, the
  sets are disjoint, the corpus validates and the existing anchor file is unchanged.
- **WordSenseBuilder_RealGap_Import_CountsExistingRealHoldoutAndReportsShortfall**: an existing real holdout row counts
  towards the holdout target and a pool smaller than the plan is reported as `SHORT`.
- **WordSenseBuilder_RealGap_ReadPlan_RejectsBadNamesAndRows**: a name other than `real-gap-N`, a non-numeric count and
  a sense missing from the inventory are each rejected.
- **WordSenseBuilder_RealAuthored_Import_WritesProvenanceDeclaresSenseOnceAndIsDeterministic**: an authored batch
  writes anchors, holdout and provenance (origin `authored`, contributors `authored-anchor` and `authored-holdout`),
  declares a new sense once, validates, and repeats byte for byte; a second batch does not declare the sense again.
- **WordSenseBuilder_RealAuthored_Import_RejectsBadRows_AndWritesNothing**: overlap between anchors and holdout, an
  unknown sense, a bad brace count, a target that is no form, an over-long sentence and a repeat of an existing sentence
  are reported together and no file is written.
- **WordSenseBuilder_RealAuthored_Run_IsWiredAndExistingRowsIgnoreAuthoredBatches**: the `real-authored-import` command
  runs through the tool and `RealPlan.ReadExisting` ignores authored batches.
- **WordSenseBuilder_NounVerbImport_Inflect_FollowsTheRules**: regular plural, past and gerund forms, including e-drop,
  `y` to `ies`, doubling, `c` to `ck`, and both spellings of a longer word.
- **WordSenseBuilder_NounVerbImport_Import_WritesSplitsProvenanceFormsAndReport**: a batch trains only the lemma that reaches
  the minimums, writes disjoint-group anchors and holdout with normalized licences, forms and senses, validates, repeats
  byte for byte, and lists excluded, already trained, insufficient and dominant-use lemmas.
- **WordSenseBuilder_NounVerbImport_Import_DropsBadRowsAndLeavesOutCollisionForms**: rows without provenance, with a
  disallowed licence, a repeated or already existing sentence or a non-noun/verb sense are dropped and counted, and collision
  forms stay out of the forms file.
- **WordSenseBuilder_NounVerbImport_Allocate_SeparatesGroupsAndCountsSharing**: the holdout takes small groups, and sharing
  occurs only to reach the minimum.
- **WordSenseBuilder_NounVerbImport_GroupOf_UsesContributorDocumentWorkAndPage**: the group key per source.
- **WordSenseBuilder_NounVerbImport_Run_IsWiredAcceptsUserOriginAndExistingRowsIgnoreBatches**: the command runs through
  the tool, a user-supplied row validates, bad names and settings fail, and `RealPlan.ReadExisting` ignores the batches.
- **WordSenseBuilder_NounVerbGate_Evaluate_AppliesTheThresholds**: lemmas below the best-sense, per-sense or coverage
  threshold fail with the reason and numbers; others pass.
- **WordSenseBuilder_NounVerbGate_Drop_RemovesTheLemmaAndTheImportKeepsItOut**: dropping removes the lemma from the
  corpus files, records it, and a later import skips it.
- **WordSenseBuilder_NounVerbGate_Drop_FiltersTheFirstLineOfFilesWithoutHeader**: anchor and holdout files have no
  header line, so dropping a lemma whose name looks like a header (`lemma`) still removes the first data line of
  such a file while keeping the other lemmas.
- **WordSenseBuilder_Program_NounVerbGate_UsageErrors_AreReported**: `nounverb-gate` without `--model`, with a
  threshold that is not a number or outside 0 to 1 (including NaN), or with an `--apply` other than `true` or `false`
  returns exit code 1 and a message naming the option, before any file is read.
- **WordSenseBuilder_RealJudge_Export_GapPlanMenusAreFrozenInTheWorkDirectory**: `real-judge-export --gap-plan` writes
  the plan's menus to `menus.tsv` in the work directory and the later steps read them back; an unknown lemma fails.
- **WordSenseBuilder_TableBuilder_Build_ReportHasPosAndPronunciationSections**: a small build report of a lemma that
  is not a heteronym contains the best-sense, part-of-speech and pronunciation sections, with no pronunciation
  sentences evaluated.
- **WordSenseBuilder_TableBuilder_Build_HeteronymReportsMeaningLevel**: a heteronym lemma adds the `Meaning level:`
  line, a meaning-correct count equal to the sense-correct count (distinct meanings per sense), and pronunciation
  counts for its holdout sentences.
- **WordSenseBuilder_Corpus_ReadSentences_SplitsCompoundTarget**: a target inside a compound word is split at the
  braces.
- **WordSenseBuilder_Program_Run_ReportsErrors**: usage and data errors return exit code 1.
- **WordSenseBuilder_Program_Run_NonNumericOption_ReportsErrorNamingOption**: a non-numeric `--size`,
  `--max` or `--per-sense` returns exit code 1 with a message naming the option.
- **WordSenseBuilder_Program_Run_OptionNameAsValue_ReportsUsageError**: an option whose value is another option
  (`--output --enforce`) returns exit code 1 with a message naming the option that lacks a value.
- **WordSenseBuilder_LearningCurve_StableHash_IsFixed**, **..._Draw_CapsPerSenseAndIsRepeatable**: the subset seed
  hash has a fixed value, and a draw takes at most the available anchors per sense and repeats exactly.
- **WordSenseBuilder_LearningCurve_RunAndWrite_CapsAtAvailableAnchorsAndIsByteIdentical**,
  **..._Run_SeparatesRealAndHoldoutSets**, **..._Run_RejectsNoSubsets**: a stub encoder and a synthetic corpus give
  capped counts, separate holdout and real rows, and byte-identical files on a repeat run.
- **WordSenseBuilder_LearningCurve_EstimatePlateau_ExtrapolatesAndFlagsPlateau**: slope, gain at twice the anchors and
  the plateau flag.
- **WordSenseBuilder_ErrorDiagnosis_Nearest_IsHighestDotProduct**, **..._Classify_FollowsThresholds**,
  **..._Run_ClassifiesEachErrorAndIsByteIdentical**: nearest-anchor similarity, the threshold rules, one fixture row
  per class (coverage-gap, overlap, ambiguous) and repeatable output.
- **WordSenseBuilder_WeakLemmas_Recommend_MapsDominantClass**, **..._Rank_OrdersWeakLemmasByScore**,
  **..._RunAndWrite_RanksFromFilesAndRendersStableReport**: the recommendation per dominant class, ranking and a
  stable report from the written files.
- **WordSenseBuilder_AnalysisData_Load_ReadsCorpusFilesAndProvenance**: corpus, provenance origin and real-eval
  rows are loaded into the three sets.
- **WordSenseBuilder_Appendix_Generate_ListsLemmasSensesAndForms**: a synthetic corpus gives an appendix with the
  parts-of-speech overview and counts, lemmas in alphabetical order, senses with part of speech, meaning and
  respelling and a bold example (shortest anchor of
  at least 30 characters), forms with restrictions in parentheses, escaped markdown characters, wrapped lines within
  120 characters, LF line endings and byte-identical output on repeat.
- **WordSenseBuilder_Appendix_Generate_RejectsIncompleteInput**: a missing `forms` or `anchors` directory, a sense
  without an anchor sentence, a form label that is not a sense and an empty inventory are each rejected with an
  `InvalidDataException` that names the problem.
- **WordSenseBuilder_Appendix_Run_WritesFileAndRequiresOptions**: the `generate-appendix` command writes the file
  and reports the lemma count; a missing `--output` exits with 1 and names the option.
  `WordSenseBuilder_Program_Run_ReportsErrors` also covers the command without options and with a missing corpus.
- **WordSenseBuilder_Appendix_Committed_MatchesCorpus**: regenerates the appendix from the committed corpus into a
  temporary file and requires it to equal `docs/user_guide/appendix-senses.md` (line endings normalized), so the
  committed appendix cannot go stale. `lint.ps1` performs the same comparison.

- **WordSenseBuilder_Lexicon_ImportMoby_IsDeterministic**: importing a small synthetic Moby-format file twice
  gives byte-identical `lexicon.tsv`; the part-of-speech code policy (noun, verb, adjective, adverb, pronoun,
  preposition), the dropped codes, the removal of words that cannot be classified safely, the committed corrections
  and the provenance file are checked.
- **WordSenseBuilder_Lexicon_ImportMoby_GerundOfVerb_GainsVerbSense**: an `-ing` word of at least five letters
  without a verb code gains the verb sense when its stem (as is, plus `e`, or without a doubled consonant) has one
  (`walking`, `making`, `running`); `morning` (no verb stem) and the four-letter `king` keep their class.
- **WordSenseBuilder_Lexicon_Build_EmbedsLexicon**: a table built from a small `lexicon.tsv` contains its words with
  the stated parts of speech and the enabled morphology rules.
- **WordSenseBuilder_Lexicon_Build_LexiconOption_IsResolvedAndMissingFilesAreErrors**: `--lexicon FILE` yields the
  words of the file and the enabled rules of the `morphology.tsv` next to it; `--lexicon none` yields no words and no
  rules; `build` without `--lexicon` and without the default lexicon, with a missing explicit file, and with a lexicon
  that has no `morphology.tsv` each exit 1 with an error (naming `--lexicon none`, the file and `morphology.tsv`) and
  write no table.
- **WordSenseBuilder_Lexicon_CommittedTable_IsUpToDate**: a table built from the committed `corpus/lexicon` files
  holds the same decoded dictionary content as the committed table (the compressed bytes are not compared, because
  they may differ between runtimes).
- **WordSenseBuilder_Lexicon_Eval_EnforcesPrecisionGate**: with synthetic gold data, `lexicon-eval --enforce` exits
  0 when every class reaches 95 % precision and exits 1 and names the class that falls short; without `--enforce`
  it exits 0.
- **WordSenseBuilder_Lexicon_Eval_Enforce_FailsWithoutEvidenceAndNamesTheRule**: `--enforce` exits 1 for a gold
  directory without sentences, names an enabled rule (`Plural`) whose answers are wrong, and names an enabled class
  and an enabled rule that have no scored token; without `--enforce` an empty gold directory still exits 0.
- **WordSenseBuilder_Lexicon_Eval_ThinEvidence_IsOnlyANote**: a class that passes over fewer than 100 tokens is
  marked `(thin)` in the report and does not fail `--enforce`.

## Requirements Coverage

`WordSense-Builder-Corpus`, `WordSense-Builder-Trainer`, `WordSense-Builder-Importer`,
`WordSense-Builder-Inventory`, `WordSense-Builder-InventoryRules`, `WordSense-Builder-Accuracy`,
`WordSense-Builder-Provenance`, `WordSense-Builder-AuthoredImport`,
`WordSense-Builder-NounVerbImport`, `WordSense-Builder-NounVerbGate`, `WordSense-Builder-LearningCurve`,
`WordSense-Builder-ErrorDiagnosis`, `WordSense-Builder-WeakLemmas`, `WordSense-Builder-Appendix`,
`WordSense-Builder-LexiconImport`, `WordSense-Builder-LexiconBuild` and `WordSense-Builder-LexiconEval`.

The `real-eval-import` and `real-eval` commands (`RealEval`) have no requirement of their own: they need a model and
blind-judged candidate files, so a unit test is not practical. Their option parsing is covered by
`WordSenseBuilder_Program_Run_NonNumericOption_ReportsErrorNamingOption`, and the real-eval rows are read by
`WordSenseBuilder_AnalysisData_Load_ReadsCorpusFilesAndProvenance`.
