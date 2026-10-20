## WordSenseBuilder Verification

This document describes the verification of the `WordSenseBuilder` unit, the non-shipped build-time tool in
`tools/WordSense.Builder` that generates the word image, the model weights and the generated user guide appendix, and
measures the shipped models.

### Verification Approach

Tests in `test/WordSense.Builder.Tests` call the command entry point (`Program.RunAsync`) with explicit output writers
and the internal serialization types with synthetic data in temporary directories: `ProgramTests.cs` covers the commands
and their usage and data errors, `ImageTests.cs` covers the word image and `WeightsBlobWriterTests.cs` the weights
writers against the readers of the shipped packages, `UdCountsTests.cs` covers the treebank counts of the Basic model,
and `BasicTrainerTests.cs` covers its trainer, table writer, model data summary and hyper-parameters;
`EmbeddingCacheTests.cs`, `FitCacheTests.cs` and `ModelTrainerTests.cs` cover the embedding cache, the lemma fit cache,
the frozen mean and the thresholds with a stub encoder; `WeightsTrainerTests.cs` and `ThresholdsTests.cs` cover the fit
and the choice of the thresholds on small synthetic features; `ChooserCorpusTests.cs` and `ChooserTrainingTests.cs`
cover the chooser rows and the training of the form choosers; `TiersTests.cs`, `SenseEvalTests.cs`,
`RunningTextTests.cs` and `SuiteReaderTests.cs` cover the quota report, the two independent evaluations and the suite
reader
with small synthetic corpora and cached-file fixtures; `EvaluatorTests.cs` covers the per-level evaluation with a stub
model.
Commands that need a real encoder (`weights`, `evaluate`, `evaluate-senses` and `evaluate-running` for the encoder
models) are verified through the data they produce: the weights embedded in the model packages are loaded and matched
in the MiniLM and BertBaseCased tests, the expectations of the suite are checked by the `SuiteTests` of the three model
test projects, and the accuracy figures are recorded in the build notes, not asserted here. The downloads of SemCor and
of the treebank are verified with a small fixture file and its checksum; no network is used.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. A temporary directory per test and small synthetic corpus files;
no model files and no network access for the scenarios below.

### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

### Test Scenarios

#### compile-image

- **Compile-image command writes the image**: `compile-image --data DIR --corpus DIR --output FILE` on the committed
  data writes the word image and reports the counts; a missing data directory fails with a message
  (`Program_CompileImage_WritesImage`, `Program_CompileImage_MissingData_Fails`). The image is read back by the
  core reader.
- **Output is deterministic**: two compilations of the same data give byte-identical files, the catalog sections
  included (`ImageCompiler_Build_IsDeterministic`, `ImageCompiler_Build_CatalogIsDeterministic`), and the committed
  image
  equals a fresh compilation (`Words_Committed_MatchesData`).
- **Invalid inventory fails the build**: a sense without a label, a sense in two groups, a group of one sense or of all
  the senses of its lemma and part of speech, a group that mixes
  parts of speech, and a group id without the `#group:` marker or equal to a sense id are each rejected with an error
  that names the sense or group, and no file is written
  (`ImageCompiler_Build_SenseWithoutLabel_Fails`, `ImageCompiler_Build_SenseInTwoGroups_Fails`,
  `ImageCompiler_Build_GroupOfOneSenseOrAllSenses_Fails`, `ImageCompiler_Build_GroupMixingPartsOfSpeech_Fails`,
  `ImageCompiler_Build_GroupIdWithoutMarkerOrEqualToSenseId_Fails`).
- **Every dictionary word is kept** (`Words_Committed_HoldsEveryDictionaryWord`): the committed image holds every
  word of the committed dictionary with each of its parts of speech, without exception.
- **The catalog is complete** (`Words_Committed_CatalogHoldsEveryOewnAndProjectSense`): every single-token OEWN sense
  and every project sense of the committed data is in the catalog with its id, group (when it is in one), key (none for
  a project sense) and respelling.
- **Auxiliaries** (`Words_Committed_AuxiliariesGainTheirAnalysis`,
  `LexiconSource_ReadAuxiliaries_ReadsWordsAndRejectsBadLines`,
  `LexiconSource_Letters_CoverTheTenParts`): each word of `corpus/lexicon/auxiliaries.tsv` gains an auxiliary
  analysis; the file reader rejects malformed lines and repeats, and the letters cover all ten parts of speech.
- **Spellings and lookup** (`Image_Spellings_AreSortedByUtf8Bytes`, `Image_Find_ReturnsOwnIndex`): the spelling index is
  sorted by the UTF-8 bytes and finds each spelling at its own index.
- **Damaged images are rejected** (`Image_Corruption_IsRejected`, `Image_TruncatedAtEverySectionBoundary_IsRejected`,
  `Image_WrongHash_IsRejectedByContentHashOnly`, `Image_CountMismatch_IsRejected`,
  `Image_SenseWithUnknownGroup_IsRejected`, `Image_GroupOfOneSenseOrAllSenses_IsRejected`,
  `Image_GroupIdWithoutMarker_IsRejected`): the reader rejects a
  tampered file, a file cut off at any section boundary (the 112-byte header of version 3, the directory of 20-byte
  entries and each section) and a section count that does not match; a wrong hash is detected by the content hash
  alone; a sense that names an unknown group, a group id without the `#group:` marker, a group with fewer than two
  members, a group of all the senses of its
  lemma and part of speech, a group that mixes lemmas or parts of speech and group members that disagree with the
  sense-to-group index are rejected.
- **Sense inventory rules** (`SenseInventory_ValidateRules_FlagsViolations`,
  `SenseInventory_Validate_CommittedCorpus_IsConsistent`): the inventory rules flag each violation, and the committed
  corpus agrees with the committed inventory.

#### coverage

- **Gutenberg boundaries**: only the text between the `*** START` and `*** END` lines is read (`Body_CutsAtMarkers`).
- **Classification**: known, apostrophe, name-like (capitalized at least four times as often as lower case) and other
  tokens are counted apart (`AddText_ClassifiesTokens`).
- **Directory and rendering**: every `.txt` file of a directory is read and the report lists the figures and the unknown
  words; a missing or empty directory is rejected (`ReadAndRender_ListsFigures`, `Read_BadDirectory_Throws`).
- **Trained share** (`ReadAndRender_ListsTheTrainedShare`): the report states the share of the known words whose lemma
  has trained fine senses.
- **Command**: `coverage --text DIR` prints the report and `--output` writes it; the text option is required
  (`Program_Coverage_ReportsAndWritesFile`, `Program_Coverage_WithoutText_Fails`).
- **Regression on an authored sample** (`AuthoredSample_IsFullyKnown`): a small authored paragraph of inflected
  English (no licensed text) is fully known by the committed lexicon. The figure of the real-text sample is
  in the build notes, not asserted here.

#### import-moby

- **Every word kept in source order**: single lower-case words keep all their parts of speech in the order of the
  source, determiner, conjunction-only and interjection-only words included (`LexiconImporterTests`,
  `Import_KeepsEveryWordInCodeOrder`).
- **Rules and corrections**: the participle and gerund rule and the committed corrections are applied; a malformed
  correction is rejected (`Import_AppliesParticipleAndGerundRule`, `Import_AppliesCorrections`,
  `Import_RejectsBadCorrection`).
- **Pinned input**: input whose SHA-256 differs from the pin is rejected (`Import_ChecksHash`), and the command without
  `--input` reports a usage error (`Program_ImportMoby_RequiresPinnedInput`).
- **Reader**: the dictionary reader keeps the order of the letters, accepts the determiner letter and rejects bad
  files (`LexiconSource_ReadWords_KeepsSenseOrderAndKnowsDeterminer`).

#### Model table

- **Basic row matches the evaluation** (`WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation`): the accuracy
  numbers of the Basic row of `docs/build_notes/model-table.md` equal a fresh evaluation of the Basic model.

#### weights

- **Model name is required and checked**: `weights` without a model id, and with an unknown id, exit with code 1 and a
  message that lists the known models (`minilm`, `bert-base-cased`); the id may be given as the second argument or with
  `--model` (`Program_Weights_UnknownModel_ListsTheModels`, `Program_ModelCommandWithoutModel_ReportsUsage`).
- **Weights file round trip**: the weights writer produces a blob that `WeightsBlob` reads back with the same model id,
  dimension, mean vector, the sense ids of each lemma, the weights and both thresholds
  (`WeightsBlobWriter_Write_RoundTripsThroughTheReader`).
- **Training is deterministic**: the logistic trainer separates a small data set and gives identical weights on a repeat
  run (`LogisticTrainer_SeparatesAndIsDeterministic`).
- **Kernel solver is equivalent and deterministic** (`KernelFitTests.cs`): the coordinates of the kernel basis keep the
  inner products of the samples, a duplicate adds no coordinate and the expansion gives the same activations
  (`KernelBasis_Create_KeepsInnerProductsAndDropsDuplicates`, `..._Subset_UsesTheSubsetOnly`); the kernel solver finds
  the weights, biases and cross-validated thresholds of the primal reference solver, also with duplicate samples
  (`WeightsTrainer_Fit_KernelSolver_FindsThePrimalOptimum`, `Thresholds_Choose_KernelSolver_FindsThePrimalThresholds`),
  repeats bit for bit and gives the same bits with one thread and with many
  (`..._KernelSolver_IsDeterministic`, `LemmaFitter_Fit_IsIndependentOfTheThreadCount`) and is the primal solver when
  there are at least as many
  samples as features (`..._MoreSamplesThanDimensions_IsThePrimalSolver`). On the committed corpus the kernel fit
  gives identical answers on the held-out sentences and a gate difference of 0.00 points (see the fit-speed note).
- **Form choosers** (`ChooserCorpusTests.cs`, `ChooserTrainingTests.cs`; real forms of the word image, hand-made
  features, no encoder): a chooser row is read with its form, label and braced target, a label may be a lemma or
  `lemma/features`, and a row that is not three columns, has an upper-case form, a target that is not the form, an
  unknown form or a label of no analysis is described (`ChooserCorpus_ReadSentences_InvalidRows_AreDescribed`); a
  sentence that occurs twice or in both sets is an error (`ChooserCorpus_Validate_DuplicateSentence_IsAnError`); the
  label of an analysis is the exact `lemma/features` label, else the lemma
  (`ChooserCorpus_LabelIndexOf_PrefersTheExactLabel`). A form of two lemmas gets a chooser with labels in ordinal order
  that tells them apart and is stored with the first lemma of the form that has weights
  (`WeightsTrainer_AttachChoosers_FormOfTwoLemmas_StoresTheChooserWithTheFirstTrainedLemma`, `..._OnlyTheSecondLemmaTrained_...`,
  the blob read back by the real reader); the tense variants of one lemma get one label each
  (`..._TenseVariants_GetOneLabelEach`); a form whose rows name one label, whose analysis has no label or that has no
  trained lemma gets no chooser and a reason (`..._UnusableForms_AreSkippedWithAReason`); the result is the same bits
  with one thread and with many (`..._IsDeterministic_AndNeedsOneFeaturePerRow`).
- **Lemmas without enough anchored senses**: a lemma with a single anchored sense gets no weights
  (`WeightsTrainer_LemmaWithOneAnchoredSense_GetsNoWeights`), and a sense with a single anchor stays in the training of
  a lemma that has two anchored senses (`WeightsTrainer_SenseWithOneAnchor_StaysInTraining`).
- **Weights match the lexicon**: weights trained for the committed corpus are loaded by each model package with zero
  dropped lemmas (verified in the MiniLM and BertBaseCased tests).
- **Embedding cache** (`EmbeddingCacheTests.cs`, `ModelTrainerTests.cs`; a stub encoder, no model files): vectors read
  back bit-identical (`EmbeddingCache_SaveAndOpen_RoundTripsBitIdentical`); the key depends on every word and the
  target index (`KeyOf_DependsOnWordsAndTarget`, `ComputeDigest_DependsOnEveryInput`); a second run over unchanged
  anchors encodes nothing and gives byte-identical weights
  (`ModelTrainer_TrainDataAsync_SecondRun_EncodesNothingAndReproducesBytes`); added sentences are the only ones encoded
  (`..._AddedSentences_AreTheOnlyOnesEncoded`); a changed model digest drops the cache
  (`EmbeddingCache_Open_OtherModelDigest_IsInvalidated`, `..._ChangedModelDigest_EncodesEverythingAgain`); a corrupt
  entry, a cut-off file and a damaged header are ignored and encoded again without an exception, and the weights equal
  those of a run without a cache (`Open_CorruptEntry_*`, `Open_TruncatedFile_*`, `Open_DamagedHeader_*`,
  `Open_MissingFile_IsEmpty`, `ModelTrainer_TrainDataAsync_CorruptEntry_IsEncodedAgain`); an entry of another
  dimension is
  refused (`Add_OtherDimension_Throws`); the caching encoder asks the cache first and always encodes without a cache or
  without tokens (`CachingEncoder_EncodeAsync_*`); a failed save leaves no temporary file
  (`Save_Failure_LeavesNoTemporaryFile`); the progress output names the hits and the encoded sentences.
- **Frozen mean** (`ModelTrainerTests.cs`): with the stored mean, adding anchors to one lemma leaves the weights, biases
  and thresholds of the other lemma equal and the mean unchanged
  (`..._FrozenMean_EditingOneLemmaLeavesOthersUnchanged`);
  without it the other lemma changes (`..._RecomputedMean_CouplesTheLemmas`); the stored mean is read from the weights
  of the same model, and a missing, other-model or unreadable blob gives none (`ReadFrozenMean_*`); a stored mean of
  another dimension is not used (`..._FrozenMeanOfOtherDimension_IsComputedInstead`).
- **Fit cache** (`FitCacheTests.cs`, `ModelTrainerTests.cs`): the key depends on the settings (the threshold settings
  and the solver included, `FitCache_KeyOf_DependsOnTheThresholdSettings`), lemma, senses, labels and every feature
  number but not on
  the thread count (`FitCache_KeyOf_*`); fits read back bit-identical and a save
  writes only the fits used (`SaveAndOpen_*`, `Save_WritesOnlyUsedFits`); a damaged entry drops itself and the rest, and
  a cut-off file, damaged header or garbage never throws (`Open_CorruptEntry_*`, `Open_DamagedFile_*`); unchanged
  anchors
  reuse every lemma fit with byte-identical weights, with the frozen mean an edited lemma is the only one fitted and the
  weights equal those of a run without a cache, and a recomputed mean fits every lemma again
  (`ModelTrainer_TrainDataAsync_UnchangedAnchors_*`, `..._FrozenMeanAndEditedLemma_*`,
  `..._RecomputedMean_FitsEveryLemmaAgain`).
- **Options** (`ProgramTests.cs`): `--no-cache` and `--recompute-mean` need no value; `--cache` with `--no-cache` is an
  error; the cache folder defaults to `<repository>/.cache/embeddings` (`Program_TrainSettingsOf_ResolvesCacheAndMean`,
  `Program_Weights_SwitchOptions_AreAcceptedWithoutValue`, `Program_Weights_CacheAndNoCache_AreRejected`).
- **Real runs** (not automated, they need the model files): `weights minilm` and `weights bert-base-cased` run twice
  must leave the committed `weights.bin` byte-identical; the second run reports no encoded sentence.
- **Evaluate is uncached**: `evaluate` classifies through `ClassifyAsync` of the shipped model and does not use the
  embedding cache, by design.

#### Thresholds

- **Precision target on out-of-fold answers** (`Thresholds_Choose_MeetPrecisionTargetOnOutOfFoldAnswers`): on a
  synthetic lemma with separable and overlapping senses, the answers at or above each chosen threshold reach the
  precision target on the out-of-fold anchors.
- **Deterministic** (`Thresholds_Choose_IsDeterministicForSeed`): the same anchors and seed give the same thresholds
  and another seed may differ.
- **Only anchors are read** (`Thresholds_Choose_NeverReadsHoldoutOrEvaluationSentences`): the choice takes the anchors
  only, and a corpus whose holdout, real-text and suite sentences are changed gives the same thresholds.
- **Unreachable target** (`Thresholds_Choose_TargetNeverReached_NeverAnswersThatLevel`): a lemma whose out-of-fold
  precision never reaches the target gets no threshold for that level, so that level is never answered.
- **Both thresholds are written** (`ModelTrainer_TrainDataAsync_WritesBothThresholdsPerLemma`): the weights blob
  holds `t_fine` of every trained lemma and `t_group` of every trained lemma that has groups.

#### tiers

- **Quota follows the tag count** (`Tiers_Quota_FollowsTheTagCount`): five or more gives 20 anchor and 5 held-out
  sentences, one to four gives 10 and 3, none gives 5 and 2.
- **Report** (`Tiers_Report_ListsShortAndExcludedSenses`): a sense with fewer sentences than its quota and a sense that
  is excluded are each listed with the reason; a sense that meets its quota is not.
- **Command** (`Program_Tiers_WritesReport`): `tiers` writes the report to the file given with `--output`.

#### evaluate

- **Model name is required and checked**: `evaluate` accepts `minilm`, `bert-base-cased` and `basic` and rejects any
  other id with exit code 1 (`Program_Evaluate_UnknownModel_ReportsError`).
- **Report is rendered and written**: the result is written to the standard output, and to the file given with
  `--output` (`EvaluationResult_Render_FormatsSetsAndMeasurements`).
- **Shares and accuracy per level** (`Evaluator_RunAsync_ReportsShareAndAccuracyPerLevel`): with a stub model that
  answers rows at each level, the report gives the share answered at the sense, group and part-of-speech level, the
  accuracy at each level and the part-of-speech accuracy.
- **Basic evaluation needs no model**: `evaluate basic` runs on a synthetic corpus without model files, counts the
  holdout rows and prints the accuracy and coverage measurements together with the calibration buckets, the confidence
  thresholds, the table size, the latency and the fixed comparison table
  (`Program_Evaluate_BasicModel_PrintsMeasurements`, `Evaluator_RunAsync_BasicModel_CountsHoldoutRows`).

#### evaluate-senses

- **Key mapping** (`SenseEval_MapKeys_WordNet30KeyToOewnSense`): a WordNet 3.0 sense key maps to the OEWN sense through
  `sense_key` and `id_map`; a key without an OEWN sense is skipped and counted.
- **Figures** (`SenseEval_Run_ReportsShareAndPrecisionPerLevelAndBaseline`): on a small annotated fixture and a stub
  model, the report gives for the tokens of trained lemmas the share answered and the precision at each level, the
  part-of-speech accuracy and the accuracy of the most-frequent-sense baseline.
- **Pinned checksum** (`SenseEval_Download_WrongHash_IsRejected`): a cached or downloaded file with another SHA-256 is
  rejected.
- **Command** (`Program_EvaluateSenses_WithoutCache_ReportsUsage`): without the model or the cache folder the command
  reports the usage and exits with code 1.

#### evaluate-running

- **Convention map** (`RunningText_ConventionMap_MapsTagsAndReportsExclusions`): a possessive pronoun maps to a
  determiner, and `NUM`, `PART`, `SYM` and `X` tokens are excluded and counted in the report.
- **Test file only** (`RunningText_Run_UsesTheTestFileAndNotTheDevelopmentFile`): the accuracy is measured on the test
  file; a development file with other content changes nothing.
- **Command** (`Program_EvaluateRunning_WithoutCache_ReportsUsage`): without the model or the cache folder the command
  reports the usage and exits with code 1.

#### suite

- **Rows and expectations** (`Suite_Read_ParsesRowsAndExpectations`): a well-formed suite gives the sentences with their
  expected sense, group or part of speech and the expectation (pass or known miss) of each model.
- **Missing expectation** (`Suite_Read_RowWithoutModelExpectation_IsRejected`): a row with no expectation for a model is
  rejected, and so is a malformed row.
- **Unknown sense or group** (`Suite_Read_UnknownSenseOrGroup_IsRejected`): an expectation that names a sense or group
  that is not in the inventory is rejected.
- The expected passes of the committed suite are asserted by the `SuiteTests` of the Basic, MiniLM and BertBaseCased
  test projects (see their verification documents); the encoder tests skip when the model files are not present.

#### ud-counts

- **Reader keeps multi-word tokens** (`WordSenseBuilder_UdCounts_ReadConllu_RangeTokensAndEmptyNodes`): a CoNLL-U range
  such as `1-2` is one token tagged like its first word, and empty nodes are skipped.
- **Malformed line** (`WordSenseBuilder_UdCounts_ReadConllu_ShortLine_Throws`): a line with fewer than four fields
  throws `InvalidDataException`.
- **Tag mapping** (`WordSenseBuilder_UdCounts_MapTag_MapsUniversalTags`): the universal tags map onto the tags of the
  tagger, with the particle `to` told apart.
- **Word keys** (`WordSenseBuilder_UdCounts_KeyOf_AcceptsLettersAndApostrophes`): only tokens made of letters and
  apostrophes get a word key.
- **Counts** (`WordSenseBuilder_UdCounts_Count_CountsNgramsAndWords`): the counts include the sentence boundaries and
  the word rows.
- **Round trip** (`WordSenseBuilder_UdCounts_WriteAndRead_RoundTripsDeterministically`): the count files read back as
  written and are byte-identical on a repeat run.
- **Bad header** (`WordSenseBuilder_UdCounts_Read_BadHeader_Throws`): a count file with a wrong header throws
  `InvalidDataException`.
- **Pinned checksum** (`WordSenseBuilder_UdSource_Verify_ComparesChecksum`): a file is accepted with its checksum and
  rejected with another.

#### train-basic and tune-basic

- **Closed-class words** (`WordSenseBuilder_BasicTrainer_ClosedMaskOf_AppliesThresholds`): a word becomes closed-class
  only with enough occurrences and a dominant closed-class share.
- **Selection** (`WordSenseBuilder_BasicTrainer_Train_SelectsWordsAndSuffixes`): the table holds the closed-class words
  and the ambiguous words, and the suffixes of words the lexicon lacks.
- **Cue table** (`WordSenseBuilder_BasicTrainer_Train_CueTablePrefersSeenAnalysis`): the cue table of a word prefers the
  analysis its context words occurred with.
- **Labels from senses** (`WordSenseBuilder_BasicTrainer_Train_AnalysisLabelsAreRolledUpFromSenses`): the analysis
  label of an anchor is the lemma and part of speech of its fine sense; anchors of two senses of one analysis train the
  same analysis.
- **Writer** (`WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack`): equal inputs give identical
  table bytes that `BasicTables` reads back, and a changed hyper-parameter changes the header.
- **Summary** (`WordSenseBuilder_BasicModelData_Summary_NamesSizes`): the summary names the sizes of the trained data.
- **Hyper-parameters** (`WordSenseBuilder_BasicHyperparameters_Default_IsConsistent`): the defaults carry the values
  the runtime reads and the tuning grid includes them.
- **Tuning** (`WordSenseBuilder_BasicTuner_SplitAnchors_IsDeterministicAndDisjoint`,
  `WordSenseBuilder_BasicTuner_RunAsync_MissingDevFile_Throws`): the anchor split is deterministic and its parts are
  disjoint, and a missing treebank development file is an error.
- **Statistical model has no weights command** (`WordSenseBuilder_Program_Weights_BasicModel_PointsToTrainBasic`):
  `weights basic` exits with code 1 and the message names `train-basic`.
- **Command** (`WordSenseBuilder_Program_TrainBasic_TinyCorpus_WritesLoadableTable`,
  `WordSenseBuilder_Program_TrainBasic_InconsistentCorpus_ReportsError`): a tiny corpus gives a table that `BasicTables`
  loads; an inconsistent corpus exits with code 1 and writes no file.
- **Tuning reads no evaluation data**: `tune-basic` uses only the treebank development file and the anchors, and
  writes no file; this is a property of the implementation that review verifies, because the command needs the real
  treebank.

#### appendix

- **Appendix lists lemmas senses and forms** (`Appendix_Generate_ListsLemmasSensesAndForms`): a synthetic corpus and
  inventory give an appendix with the parts-of-speech overview, the lemmas in alphabetical order, their senses and
  their forms; markdown characters are escaped, lines are wrapped, line endings are LF and the output repeats byte for
  byte.
- **Senses carry group key gloss and respelling** (`Appendix_Generate_ListsSensesWithGroupKeyGlossAndRespelling`): each
  sense is listed with its id, group, OEWN sense key (none for a project sense), gloss and respelling.
- **Incomplete input is rejected** (`Appendix_Generate_RejectsIncompleteInput`, `Program_Appendix_MissingCorpus_Fails`):
  a missing anchors directory, a trained sense without an anchor sentence and an empty inventory are each rejected with
  an error that names the problem, and the command exits with code 1.
- **Command** (`Program_Appendix_WritesFile`): `appendix` writes the file given with `--output`.
- **Committed appendix is current** (`Appendix_Committed_MatchesCorpus`): the appendix regenerated from the committed
  corpus and data equals `docs/user_guide/appendix-senses.md` (line endings normalized), so the committed appendix
  cannot go stale; `lint.ps1` performs the same comparison.

#### corpus

- **Row format** (`Corpus_ReadSentences_ParsesRow`, `Corpus_ReadSentences_RejectsMalformedRow`): a row of lemma, sense
  id, part of speech and sentence is parsed; a row with another number of fields is rejected.
- **Part of speech agrees with the sense** (`Corpus_ReadSentences_PartOfSpeechDisagreeingWithSense_IsRejected`): a row
  whose part of speech differs from the one of its sense is rejected with an error that names the row.
- **Compound targets** (`Corpus_ReadSentences_SplitsCompoundTarget`): a compound target is split into its words.
- **Disjointness** (`Corpus_DirectoryAndDisjointness`, `Corpus_Disjointness_CoversSuiteEvaluationAndOewnExamples`):
  anchors that share a normalized sentence or an 8-gram with a holdout, real-text, suite, evaluation or Open English
  WordNet example sentence are rejected, each with the pair that overlaps.
- **Forms** (`Corpus_ReadForms_ParsesLabels`): the form files are parsed with their labels.

#### Command line

- **No arguments or unknown command**: exit code 1 and the usage text listing `compile-image`, `import-moby`,
  `weights <modelId>`, `evaluate <modelId>`, `evaluate-senses <modelId>`, `evaluate-running <modelId>`, `ud-counts`,
  `ud-irregular`, `coverage`, `train-basic`, `tune-basic`, `label-senses`, `import-wn30-counts`, `tiers` and `appendix`
  (`Program_NoArguments_PrintsUsage`, `Program_UnknownCommand_ReportsCommandAndUsage`,
  `Program_Usage_ListsTheSenseCommands`).
- **Removed command** (`Program_RemovedCommand_IsUnknown`): the old command names (`build`, `train`,
  `generate-appendix`, `validate-pos`) are unknown commands and exit with code 1.
- **Option errors** (`Program_MalformedOptions_ReportUsage`): an option without a value, an option whose
  value is another option, and a value without a name exit with code 1 and the message "Expected '--name value'".
- **Defaults**: without `--repository` and `--corpus` the paths default to the repository layout below the current
  directory. This is by inspection of the option parser; no dedicated test exercises it, because every test passes the
  paths explicitly.
