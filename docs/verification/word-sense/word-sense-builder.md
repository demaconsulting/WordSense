## WordSenseBuilder Verification

This document describes the verification of the `WordSenseBuilder` unit, the non-shipped build-time tool in
`tools/WordSense.Builder` that generates the word image, the model weights and the generated user guide appendix, and
measures the shipped models.

### Verification Approach

Tests in `test/WordSense.Builder.Tests` call the command entry point (`Program.RunAsync`) with explicit output writers
and the internal serialization types with synthetic data in temporary directories: `ProgramTests.cs` covers the
commands and their usage and data errors, `ImageTests.cs` covers the word image and `WeightsBlobWriterTests.cs` the
weights writers against the readers of the shipped packages, `TaggerCountsTests.cs` and `OpenCorpusTests.cs` cover the
tag counts of the Basic model and the reader of the open corpus, `TaggerEvaluatorTests.cs` its accuracy measurement,
`CorpusLicenseTests.cs` the sources recorded for the committed corpus, and `BasicTrainerTests.cs` covers its trainer,
table writer, model data summary and hyper-parameters; `CueRunningReliabilityTests.cs` covers the running-text
reliability of the cue tables on a toy world with the runtime classifier; `EmbeddingCacheTests.cs`, `FitCacheTests.cs` and
`ModelTrainerTests.cs` cover the embedding cache, the lemma fit cache, the frozen mean and the thresholds with a stub
encoder; `WeightsTrainerTests.cs` and `ThresholdsTests.cs` cover the fit and the choice of the thresholds on small
synthetic features; `SensePriorTests.cs` covers the usage prior (the offsets on a hand-computed fixture, their effect on
the stored biases, the thresholds and the fit-cache key, and the options); `RealSplitTests.cs` and
`RealCalibratorTests.cs` cover the split of the real held-out sentences by
document and the real-text calibration of the thresholds on one-dimensional synthetic lemmas; `ChooserCorpusTests.cs`
and `ChooserTrainingTests.cs` cover the chooser rows and the training of the form choosers; `TiersTests.cs`,
`SenseEvaluatorTests.cs`, `RunningTextEvaluatorTests.cs`, `TenseEvaluatorTests.cs`, `ChallengeSetFloorTests.cs`,
`SuiteFileTests.cs`, `ShapeProbeTests.cs`, `ShapeRowsTests.cs`, `LeakageCheckTests.cs`, `Gold2Tests.cs` and
`ModelTableTests.cs` cover the
quota report, the two independent evaluations, the suite reader and its grading, the shape probe reader and report, the
shape rows of the corpus, the leakage check, the second all-words gold set (power, draw, labeling, measurement and
paired comparison) and the model table file with small synthetic corpora, a stub model and
cached-file fixtures; `EvaluatorTests.cs` covers the per-level evaluation with a stub model. `LbfgsTests.cs`,
`MultinomialLogisticTrainerTests.cs`, `PosHeadBlobWriterTests.cs`, `PosVectorCacheTests.cs` and `PosHeadTrainerTests.cs`
cover the part-of-speech head: the minimizer on known functions, the fit on synthetic vectors against an independent
dense implementation of its objective, the writer against the reader of the Onnx package, the vector cache, and the
trainer end to end on a synthetic corpus with an encoder stub that gives every word a vector, without a model file.
`TrainingOverlapTests.cs` covers the overlap check of the training sentences, `OutcomeComparerTests.cs` the outcome file
and the paired comparison, `SuiteDiffTests.cs` the recording and the diff of the suite answers, and
`RunningTextOutcomesTests.cs` the outcomes, the documents and the latency sample of the running-text evaluation.
`PosHeadProvenanceTests.cs` reads the heads embedded in the model packages and the committed training file. Commands
that need a real
encoder (`weights`, `evaluate`, `evaluate-senses`, `evaluate-running`, `evaluate-suite` and `evaluate-shapes` for the
encoder models, and `train-pos-head`) are
verified through the data they produce: the weights embedded in the model packages are loaded and matched in the
MiniLM and BertBaseCased tests, the heads embedded in them are loaded and bound to their encoders by `EmbeddedHeadTests`
of the same two test projects, the expectations of the suite are checked by the suite tests of the three model test
projects (`BasicSuiteTests`, `MiniLMSuiteTests`, `BertBaseCasedSuiteTests`) and the floors of the challenge set by
`ChallengeSetFloorTests` (both skip when the model files are not present, and fail when `WORDSENSE_REQUIRE_MODELS` is
set), and the accuracy figures are recorded in
the model comparison table and the accuracy chapter of the user guide, not asserted here. The download of SemCor (an
evaluation source only) is verified with a stub HTTP
handler, a fixture archive and its checksum; no network is used. The treebank is prepared by a script of
`tools/open-corpus`, which verifies its checksum itself.

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
- **Sense order is the Open English WordNet order**
  (`ImageCompiler_Build_CatalogIsDeterministic`, `ImageCompiler_Build_SenseOrder_IsOewnPositionThenHomographThenProjectSenses`):
  on a fixture whose WordNet 3.0 counts rank the senses the other way round, the senses of a lemma and part of speech
  are in the order of their position in the entry; the sense of the entry with the higher homograph number follows the
  sense of the same position in the first entry; a project sense (no key) is last whatever its position; and an image
  compiled from a data folder with an empty frequency table has the same order.
- **Invalid inventory fails the build**: a sense without a label, a sense in two groups, a group of one sense (or a
  declared group without any member) or of all
  the senses of its lemma and part of speech, a group that mixes
  parts of speech, and a group id without the `#group:` marker or equal to a sense id are each rejected with an error
  that names the sense or group, and no file is written
  (`ImageCompiler_Build_SenseWithoutLabel_Fails`, `ImageCompiler_Build_SenseInTwoGroups_Fails`,
  `ImageCompiler_Build_GroupOfOneSenseOrAllSenses_Fails`, `ImageCompiler_Build_GroupWithoutMembers_Fails`,
  `ImageCompiler_Build_GroupMixingPartsOfSpeech_Fails`,
  `ImageCompiler_Build_GroupIdWithoutMarkerOrEqualToSenseId_Fails`).
- **Spellings without a letter are left out** (`ImageCompiler_Build_SpellingWithoutALetter_IsLeftOut`,
  `ImageCompiler_Build_UnlabeledSpellingWithoutALetter_IsLeftOut`): a numeral such
  as `3` or `24/7` in the form table gets no spelling, entry, analysis or sense in the compiled image, a spelling with
  a letter and digits such as `3rd` stays, and the committed data tables keep the rows. The numerals are left out
  before the labels are checked, so a numeral sense without a label does not fail the build (an unlabeled sense of a
  lemma with a letter still does). A `group_sense` row for such a numeral sense is ignored, so a group that lists it
  next to retained senses compiles with the retained members only
  (`ImageCompiler_Build_GroupWithSpellingWithoutALetterMember_LeavesTheMemberOut`). An analysis of a spelling with a
  letter whose lemma has none (`3rd` as a form of the entry `3`, or of the surface lemma `3`) is left out too, while the
  other analyses of that spelling stay (`ImageCompiler_Build_AnalysisOfLemmaWithoutALetter_IsLeftOut`).
- **Every dictionary word is kept** (`Words_Committed_HoldsEveryDictionaryWord`): the committed image holds every
  word of the committed dictionary with each of its parts of speech, without exception.
- **The catalog is complete** (`Words_Committed_CatalogHoldsEveryOewnAndProjectSense`): every single-token OEWN sense
  and every project sense of the committed data whose lemma has a letter is in the catalog with its id, group (when it
  is in one), key (none for a project sense).
- **Auxiliaries** (`Words_Committed_AuxiliariesGainTheirAnalysis`,
  `LexiconSource_ReadAuxiliaries_ReadsWordsAndRejectsBadLines`,
  `LexiconSource_Letters_CoverTheTenParts`): each word of `corpus/lexicon/auxiliaries.tsv` gains an auxiliary
  analysis; the file reader rejects malformed lines and repeats, and the letters cover all ten parts of speech.
- **Spellings and lookup** (`Image_Spellings_AreSortedByUtf8Bytes`, `Image_Find_ReturnsOwnIndex`): the spelling index is
  sorted by the UTF-8 bytes and finds each spelling at its own index.
- **Image format version 5** (`Image_Version5_HasSenseFrequencyAndNoRespellingSections`,
  `Image_PreviousFormatVersion_IsRejected`,
  `Image_Corruption_IsRejected`, `DataSchema_Load_CommittedSchema_DeclaresTables`): the committed image has the version
  5 in its header, no section with the id 24, 25 or 26, a meta section of eight values and a sense frequency section
  (id 38) of one 16-bit value for each sense; the reader rejects the
  previous version 4 and any other version; the committed schema does not declare `sense_respelling`.
- **Sense frequency compiled** (`ImageCompiler_Build_SenseFrequency_IsTheTagCountOfEachSenseKey`,
  `ImageCompiler_Build_SenseFrequency_IsNotPartOfTheInventoryHash`,
  `ImageCompiler_Build_SenseFrequency_CountAboveSixteenBits_Fails`): on the small data folder each sense has the count of
  its key in `sense_frequency`, 0 for a project sense and for a key that the table does not list; the same data with
  and without the table gives the same inventory hash but another data hash and other counts; a count of 70,000 fails the
  compilation with a message that names the count and the key.
- **Damaged images are rejected** (`Image_Corruption_IsRejected`, `Image_PreviousFormatVersion_IsRejected`,
  `Image_TruncatedAtEverySectionBoundary_IsRejected`,
  `Image_WrongHash_IsRejectedByContentHashOnly`, `Image_CountMismatch_IsRejected`,
  `Image_SenseWithUnknownGroup_IsRejected`, `Image_GroupOfOneSenseOrAllSenses_IsRejected`,
  `Image_GroupIdWithoutMarker_IsRejected`): the reader rejects a
  tampered file, an image of the previous format version 4, a file cut off at any section boundary (the 112-byte header
  of version 5, the directory of 20-byte
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
  in `docs/user_guide/accuracy.md`, not asserted here.

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

- **Basic row matches the evaluation** (`WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation`): the sense and size
  cells of the Basic row of `docs/user_guide/model-table.md` equal a fresh evaluation of the Basic model.
- **A command changes only its cells** (`WordSenseBuilder_ModelTable_Update_ChangesOnlyTheCellsOfItsCommand`): the other
  cells of the row and the rows of the other models stay; a cell never measured reads `not measured`; the models are in
  the order of the table; an unknown column is rejected (`WordSenseBuilder_ModelTable_Update_UnknownColumn_IsRejected`).

#### weights

- **Model name is required and checked**: `weights` without a model id, and with an unknown id, exit with code 1 and a
  message that lists the known models (`minilm`, `bert-base-cased`); the id may be given as the second argument or with
  `--model` (`Program_Weights_UnknownModel_ListsTheModels`, `Program_ModelCommandWithoutModel_ReportsUsage`).
- **Ensure helper prepares only downloadable models**
  (`WordSenseBuilder_ModelCatalog_EnsureAsync_EnsuresOnlyDownloadableModels`): `ModelCatalog.EnsureAsync` calls
  `EnsureModelAsync` once on a model that implements `IDownloadableWordSenseModel` and does nothing for the Basic model,
  which has no such capability; a null model throws `ArgumentNullException`.
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
  gives identical answers on the held-out sentences and a gate difference of 0.00 points (see the design of the builder).
- **Form choosers** (`ChooserCorpusTests.cs`, `ChooserTrainingTests.cs`; real forms of the word image, hand-made
  features, no encoder): a chooser row is read with its form, label and braced target, a label may be a lemma or
  `lemma/features`, and a row that is not three columns, has an upper-case form, a target that is not the form, an
  unknown form or a label of no analysis is described (`ChooserCorpus_ReadSentences_InvalidRows_AreDescribed`); a
  sentence that occurs twice or in both sets is an error (`ChooserCorpus_Validate_DuplicateSentence_IsAnError`); the
  label of an analysis is the exact `lemma/features` label, else the lemma
  (`ChooserCorpus_LabelIndexOf_PrefersTheExactLabel`). A form of two lemmas gets a chooser with labels in ordinal
  order that tells them apart and is stored with the first lemma of the form that has weights
  (`WeightsTrainer_AttachChoosers_FormOfTwoLemmas_StoresTheChooserWithTheFirstTrainedLemma`,
  `..._OnlyTheSecondLemmaTrained_...`, the blob read back by the real reader); the tense variants of one lemma get one
  label each (`..._TenseVariants_GetOneLabelEach`); a form whose rows name one label, whose analysis has no label or
  that has no trained lemma gets no chooser and a reason (`..._UnusableForms_AreSkippedWithAReason`); the result is
  the same bits with one thread and with many (`..._IsDeterministic_AndNeedsOneFeaturePerRow`).
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
- **Closed-class columns are left out** (`Thresholds_Choose_ClosedClassColumns_AreLeftOutOfTheMargins`): on a lemma with
  two open senses and a closed-class sense that cannot be told from one of them, the threshold with the candidates of
  every anchor limited to the open senses is finite and not above the threshold without them, candidates that list every
  column give the threshold of no candidates at all, and a candidate list of the wrong length is refused.
- **Row-specific candidates** (`Thresholds_Choose_RowSpecificCandidates_AreTheColumnsOfTheFormOfTheAnchor`): two senses
  that no form ranks together (a noun sense and a verb sense of one lemma) do not make each other look wrong when each
  anchor lists the candidates of its own form; an anchor with one candidate, and an anchor whose true sense is not among
  its candidates, are not tested.
- **Forms that get a chooser** (`WeightsTrainer_ChooserForms_AreTheFormsWhoseRowsTrainAChooser`): a form whose rows name
  two labels is listed, a form whose rows name one label is not, and no rows list no form (the cross-validation leaves
  out the anchors of a form of several lemmas that is not listed).

#### Usage prior

- **Offset formula** (`SensePrior_Offsets_FollowTheDocumentedFormula`): on a fixture of three senses with known anchor
  counts and tag counts, each offset equals `-ln q + alpha ln pi` computed by hand, and adding the offsets to the scores
  gives the probabilities `p/q * pi^alpha` normalized.
- **Sense without a count** (`SensePrior_Offsets_SenseWithoutCount_UsesTheSmoothingAlone`): a project sense (no sense
  key) and a sense that the table does not list get the smoothing constant alone, the same as a sense with count 0.
- **Alpha zero** (`SensePrior_Offsets_AlphaZero_RemovesOnlyTheTrainingShare`): without the exponent only the training
  share is divided out.
- **Deterministic** (`SensePrior_Offsets_AreDeterministic`): the same input gives the same offsets bit for bit.
- **Counts file** (`SensePrior_ReadCounts_ReadsTheFrequencyTable`): the tag counts are read from the partitioned
  `sense_frequency` table by sense key.
- **Wired into the training** (`WeightsTrainer_TrainDense_UsagePrior_ShiftsTheBiasesByTheOffsets`,
  `..._IsDeterministicAndKeepsTheSize`): the weights of a lemma trained with the prior equal those trained without it
  and the biases differ by exactly the offsets; two runs give the same bits with one thread and with many, and the
  number of biases and weights is unchanged.
- **Thresholds on corrected scores** (`Thresholds_Choose_Offsets_AreAppliedToTheOutOfFoldScores`): offsets that
  favor one sense change the out-of-fold margins and therefore the thresholds of a lemma, and zero offsets change
  nothing.
- **Fit cache** (`FitCache_KeyOf_DependsOnThePriorOffsets`): another offset gives another key.
- **Options** (`Program_TrainSettingsOf_ParsesThePriorOptions`): `--prior-alpha` and `--prior-smoothing` are read and
  checked (a negative value is an error), `--no-prior` switches the correction off, and the defaults are 0.5 and 0.5.
- **Real runs** (not automated): the measured effect of the correction is in the user guide chapter "Accuracy and Model
  Choice".

#### Real-text calibration

- **Split by document** (`RealSplit_Classify_SplitsRealRowsByDocument`): with a provenance file, the rows of one
  document (the same source and the same key before `#`) are all calibration or all test, both sides get rows, and a
  Tatoeba sentence is its own document.
- **Only real rows are split** (`RealSplit_Classify_WrittenAndUnknownRows_AreNeverRealRows`): a row whose source is
  written for the project, and a row without provenance, is in neither split.
- **Anchor documents** (`RealSplit_Classify_ReportsTestRowsInDocumentsWithoutAnchors`,
  `RealSplit_Classify_AnchorOfAnotherLemma_DoesNotCountAsAnchorDocument`): the test rows whose document supplies no
  anchor of the lemma of the row are listed apart; an anchor of another lemma from the same document does not count.
- **Strict provenance** (`RealSplit_ReadProvenance_MalformedFile_IsRefusedWithPathAndLine`,
  `RealSplit_ReadProvenance_IdInTwoFiles_IsRefused`): a provenance file with a header other than
  `id split source source_key`, a short or long row, an empty field, a split other than `anchors`, `holdout` or
  `realtext`, a source that `ProvenanceSources` does not allow or an id that repeats (in one file or two) is refused
  with the path and the line, and is never skipped.
- **Pooled shift** (`RealCalibrator_Apply_RaisesThresholdsUntilThePooledPrecisionReachesTheTarget`): on calibration rows
  where the cross-validation thresholds are too low, the thresholds are raised by one shift and the pooled precision of
  the answers reaches the target; thresholds that already meet it are not moved.
- **Strata** (`RealCalibrator_Apply_Strata_AreCalibratedByTheirOwnRows`, `Corpus_StagesOf_NamesTheStageOfTheHighestWaveFile`):
  with a base stratum of poor precision (shift 3) and a stage 1 stratum that is always right (shift 0), each stratum gets
  its own shift, the report lists the strata in the order of their names, the base thresholds are the same with or without
  the other stratum, and one pooled stratum gives both lemmas the same threshold; the stage of a lemma is the stage of the
  highest wave file that holds one of its anchors (`quota-w4c` is wave 4, `quota-w49` the base stage, `quota-w50` and
  `quota-w51` stage 1, `quota-w100` stage 2, `batch-1` no wave).
- **Margin as inference computes it** (`RealCalibrator_Apply_ClosedClassSenses_AreNotCandidatesOfTheMargin`,
  `RealCalibrator_Apply_FewerThanTwoCandidates_IsNotObserved`): for a lemma that has a closed-class sense, the margin
  of a calibration row ignores that sense, so a row whose best open-class sense wins over a closed-class one is
  observed with
  the open-class margin; a row whose word is not a form of the lemma, or that has fewer than two candidates, is not
  observed.
- **Form plan** (`RealCalibrator_Apply_FormOfSeveralLemmas_IsObservedOnlyWithAChooser`): `left` is a form of the lemmas
  `left` and `leave`; a calibration row of `leave` with this form is not observed without a chooser for the form (the
  encoder does not run, the lexicon answers) and is observed with one.
- **Rows that no threshold of the lemma decides**
  (`RealCalibrator_Apply_ChooserNotConfidentOrOtherLemmaSelected_RowIsLeftOut`): for the form `left` with a chooser of
  threshold 1, a chooser margin of 0.4 is not confident (the row is not observed and `ChooserUnsure` counts it), a
  chooser that prefers the lemma `left` selects another lemma (not observed, `SelectedElsewhere`), and a margin of 2 for
  `leave` is observed with no row counted as left out.
- **Closed-class slot of Basic** (`RealCalibrator_Apply_ClosedClassSlotWins_RowIsLeftOut`,
  `RealCalibrator_Apply_OpenClassSlotWins_RowIsObserved_ClosedTrueSenseIsWrong`,
  `RealCalibrator_Apply_BasicResultsOfAnotherLength_Throws`): for `little` with an adjective sense that scores 3, a
  second adjective sense and a determiner column, a Basic result that says determiner 0.9 wins the closed-class split, so
  the row is not observed and the report counts it in `ClosedWins`; with determiner 0.1 the adjective slot wins and the
  row is observed (right for an adjective true sense; wrong, and counted in `ClosedGoldRows`, for the determiner true
  sense); without the Basic results the row is observed as well; a list of Basic results of another length than the
  rows is refused.
- **Half-precision gate leaves out forms with fewer than two candidates**
  (`WeightsBlobWriter_PrecisionGate_LeavesOutFormsWithFewerThanTwoCandidates`): for the lemma `own` with a verb and an
  adjective column, the row of `owns` (only a verb, one candidate) is not counted and the row of `own` (two candidates)
  is.
- **Half-precision gate ranks the open-class columns** (`WeightsBlobWriter_PrecisionGate_RanksOnlyTheOpenClassColumns`,
  `WeightsBlobWriter_ReducedPrecision_StaysWithinTheAccuracyGate`,
  `WeightsBlobWriter_PrecisionGate_MeasuresDescribesAndEnforces`): a determiner column that outscores the true adjective
  sense does not make the row wrong, and a row whose true sense is the determiner is wrong for both sets of weights; the
  reduced weights stay within 0.2 percentage point of the full ones.
- **Basic cue tables learn the base-stage sense anchors and all chooser rows**
  (`Program_TrainBasic_AnchorsOfALaterStage_DoNotChangeTheTable`,
  `Program_TrainBasic_ChooserRowsOfALaterStage_ChangeTheTable`): the table trained after a `quota-w50` sense anchor file
  is added is byte-identical to the table trained before, and the table trained after a `quota-w50` chooser file is
  added differs from it.
- **Own threshold** (`RealCalibrator_Apply_LemmaWithEnoughRows_KeepsTheHigherOfItsOwnAndTheAdjustedThreshold`): a lemma
  with at least 20 calibration rows keeps the higher of its own threshold and the adjusted one; a lemma with fewer rows
  gets the pooled shift.
- **No rows** (`RealCalibrator_Apply_NoCalibrationRows_KeepsTheThresholds`): without calibration rows every
  threshold is unchanged.
- **Never and determinism** (`RealCalibrator_Apply_NeverStaysNever_AndIsDeterministic`): a level that was never
  answered stays never answered, and two runs give the same thresholds.
- **Group level** (`RealCalibrator_Apply_GroupThreshold_MovesOnlyWithEnoughAnswers`): the group threshold moves only
  when at least 30 calibration answers can verify it.
- **Robust shift** (`RealCalibrator_ChooseShift_ConfidentErrorsInTheTail_DoNotMoveTheShift`): with a steady error
  rate of 5 percent and the target 0.97, six more confident errors at the top of the distances do not change the
  shift (the 120 most confident of 400 answers are kept and the target is reported as not reached), while the rule
  without the share "reaches" the target on 39 answers in the far tail.
- **Unreachable target** (`RealCalibrator_ChooseShift_TargetNotReachable_KeepsTheMinimumShareAndReportsIt`,
  `RealCalibrator_Apply_TargetNotReachable_ReportsItAndKeepsTheMinimumShare`): the shift is the largest one that
  keeps the minimum share of the answers, the result says that the target was not reached (the training summary line
  contains "fine pooled shift 5.000 (target not reached)" and says that the answers before and after cover all
  calibration rows), and fewer answers than the minimum give shift 0. When every usable answer has one positive
  distance and the target is not reached, the fallback shift is that distance, not zero
  (`RealCalibrator_ChooseShift_OneDistanceAndTargetNotReachable_FallsBackToThatDistance`: 30 wrong answers at the
  distance 1 give `(1, false)`).
- **Reachable target** (`RealCalibrator_ChooseShift_TargetClearlyReachable_ReachesItOnEnoughAnswers`): when the
  precision clearly rises above the target, the lowest qualifying shift is chosen (4.1 in the fixture; the tail from
  4.0 has 96.9 percent and fails) and the kept answers reach it.
- **Floor** (`RealCalibrator_ChooseShift_FewPooledAnswers_TheMinimumCountDominatesTheShare`): with 60 usable answers
  30 percent is 18, so the floor of 30 decides (shift 3.0); with a floor of 10 the share decides (shift 4.2).
- **Fewer than 30 usable answers** (`RealCalibrator_ChooseShift_FewerThanThirtyUsableAnswers_GiveShiftZeroNotReached`):
  29 usable answers (a negative or non-finite distance is no answer) give shift 0 and "not reached"; 30 give the shift.
- **Ties** (`RealCalibrator_ChooseShift_TiesAtTheBoundary_AreKeptTogether`): answers of equal distance are kept or
  dropped together, so a boundary inside a tie keeps more than the minimum (15 answers from 7.0, but 20 from 6.0 when
  the minimum is 16).
- **Defaults** (`RealCalibrator_Defaults_ShareThirtyPercentTargetNinetySevenPercent`): the share is 0.3, the floor 30,
  the target of a weights run 0.97 and the cross-validation target 0.95.
- **Group answers above 100** (`RealCalibrator_Apply_GroupAnswersAboveOneHundred_UseTheShareAndReportTargetNotReached`):
  with 120 group answers at 90 percent precision the shift keeps 36 of them, the group level is verified, not
  reached, and the summary line says "verified, target not reached on 120 answers".
- **Infinite group distances** (`RealCalibrator_Apply_InfiniteGroupDistances_AreNotUsableAndDoNotVerify`): a lemma with
  no other group has an infinite group margin; such answers are not usable, so 40 of them do not verify the group level,
  the threshold is kept and the line says "unverified, kept on 0 answers".
- **Wired into the training** (`ModelTrainer_TrainDataAsync_CalibrationRows_MoveTheThresholds`): with calibration rows
  the trained thresholds are at least the cross-validation ones, and without them they are equal.

#### tiers

- **Quota follows the tag count** (`WordSenseBuilder_Tiers_Quota_FollowsTheTagCount`): five or more gives 20 anchor
  and 5 held-out sentences, one to four (and a project sense) gives 10 and 3, none gives 5 and 2, or 3 and 1 for a
  lemma with 15 or more senses.
- **Large lemmas** (`WordSenseBuilder_Tiers_Build_LargeLemma_AsksFewerSentencesForUntaggedSenses`): an untagged sense of
  "cut" (70 senses) has the quota 3 and 1, one of "record" (12 senses) 5 and 2.
- **Report** (`WordSenseBuilder_Tiers_Report_ListsShortAndEmptySensesWithTheMissingSentences`): every sense of a trained
  lemma has its tier and status; a sense with fewer sentences than its quota is `short`, one without any is `empty` and
  both are listed with the missing sentences; a sense that meets its quota is `met`.
- **Command** (`WordSenseBuilder_Program_Tiers_WritesReport`): `tiers` prints the summary and writes the report to the
  file given with `--output`.

#### evaluate

- **Model name is required and checked**: `evaluate` accepts `minilm`, `bert-base-cased` and `basic` and rejects any
  other id with exit code 1 (`Program_Evaluate_UnknownModel_ReportsError`).
- **Report is rendered and written**: the result is written to the standard output, and to the file given with
  `--output` (`EvaluationResult_Render_FormatsSetsAndMeasurements`).
- **Shares and accuracy per level** (`Evaluator_RunAsync_ReportsShareAndAccuracyPerLevel`): with a stub model that
  answers rows at each level, the report gives the share answered at the sense, group and part-of-speech level, the
  accuracy at each level and the part-of-speech accuracy.
- **Latency is the classification time** (`Evaluator_ScoreAsync_ClassificationTime_ExcludesWritingTheOutcomes`): with a
  fast model and an outcomes writer that takes 40 ms a line, the classification time of six rows is less than half of
  the elapsed time, so the latency does not include the outcomes file, the grading or the preparation.
- **Real test split first** (`Evaluator_RunAsync_ProvenanceNamesTheOrigin_ReportsTheRealTestSplitFirst`): with a
  provenance file that marks held-out rows as real and written, the report has the real test split as its first
  (headline) set, then the test rows of documents without anchors, the calibration split, the written rows and the whole
  holdout (their rows and correct answers are counted separately) and states that the figures are not the accuracy on
  representative text.
- **Basic evaluation needs no model**: `evaluate basic` runs on a synthetic corpus without model files, counts the
  holdout rows and prints the accuracy and coverage measurements together with the calibration buckets, the confidence
  thresholds, the table size, the latency and the fixed comparison table
  (`Program_Evaluate_BasicModel_PrintsMeasurements`, `Evaluator_RunAsync_BasicModel_CountsHoldoutRows`).

#### evaluate-senses

- **Reader** (`WordSenseBuilder_SenseEval_Parse_ReadsSingleWordAnnotationsAndCountsTheRest`): the single-word
  annotations become targets with the keys `lemma%lexsn`, punctuation stays as words, and multiword expressions and
  senses dropped by the mapping are counted and left out.
- **Key mapping** (`WordSenseBuilder_SenseEval_MapKeys_WordNet30KeyToOewnSense`): a WordNet 3.0 sense key maps to the
  sense of the catalog that has it; a word with several keys is left out, a key without a sense is counted as
  unmapped, a word of an untrained lemma is not a trained-lemma word (it is an other row when the lemma has two or more
  senses), and a sentence that is in the corpus is left out. The words with several keys are counted directly, whatever
  the sampling or the exclusion of sentences (a sentence left out does not turn one into a single-key word), and the
  sentences that the sampling does not keep are counted apart.
- **Figures** (`WordSenseBuilder_SenseEval_Run_ReportsShareAndPrecisionPerLevelAndBaseline`): on a small annotated
  fixture and a stub model, the report gives for the words of trained lemmas the share answered and the precision at
  each level, the fine top-1, the part-of-speech accuracy, the accuracy of the first-sense baseline and the
  mapping coverage (`WordSenseBuilder_SenseEval_Render_FormatsTheFunnel`).
- **All-words selection** (`WordSenseBuilder_SenseEval_Select_AllWords_KeepsUntrainedPolysemousAndCountsTheRest`): the
  mapped words of an untrained lemma with two or more senses are kept as other rows, the words of an untrained lemma with
  one sense are only counted in the funnel, the words of a trained lemma (one with one sense included) are trained-lemma
  rows, and the trained-lemma rows are the same as without the all-words set.
- **All-words figures** (`WordSenseBuilder_SenseEval_Run_UntrainedLemma_CountsNoSenseAnswerAndFineTop1FromRankedSenses`):
  a word of an untrained lemma answered at the part-of-speech level is no sense answer, its fine top-1 comes from the
  ranked senses, and the trained-lemma figures do not change.
- **Tag-count baseline** (`WordSenseBuilder_SenseEval_Run_TagCountBaseline_UsesTheHighestCountAndInventoryOrderOnTies`):
  the baseline chooses the sense with the highest tag count, the first in inventory order on a tie or without counts.
- **All-words report** (`WordSenseBuilder_SenseEval_Render_AllWords_ShowsTrainedShareAndCircularityCaveat`): the report
  has the trained, untrained and all rows per model, the share of the words that are of a trained lemma, says that
  the tag-count baseline is
  optimistic because the counts include SemCor, and says that the fine top-1 of the models on the untrained words is
  circular on SemCor too (their usage prior is made of the same counts) and that the independent measurement is
  `evaluate-allwords`.
- **Pinned checksum** (`WordSenseBuilder_SenseEval_Download_WrongHash_IsRejected`,
  `WordSenseBuilder_SenseEval_ReadArchive_VerifiesTheHashAndReadsTheTagFiles`): a downloaded or cached archive with
  another SHA-256 is rejected and leaves no file; the tag files of a gzip-compressed tar are read.
- **Command** (`WordSenseBuilder_Program_EvaluateSenses_WithoutCache_ReportsUsage`): without the archive in the cache
  and without `--download` the command reports how to get it and exits with code 1.

#### evaluate-allwords, allwords-sample and allwords-merge

The tests are in `test/WordSense.Builder.Tests/AllWordsTests.cs` and use the committed corpus, the committed catalog
and, for the measurement, a stub model.

- **Committed gold set** (`WordSenseBuilder_AllWords_CommittedGold_TokensAreOfUntrainedPolysemousLemmasWithTheirSense`,
  `..._CommittedGold_HasAtMostThreeTokensPerLemmaAndOnePerSentence`, `..._CommittedGold_TokensComeFromTheirOwnSplit`,
  `..._CommittedGold_HalvesShareNoDocument`): both halves have at least 300 tokens; every token names a sense of its lemma
  and part of speech and a word that is at its index, its lemma had no anchor when the set was drawn (the anchors of the
  stage 1 waves, `quota-w5*`, are not counted) and its part of speech has two or more
  senses; no lemma has more than three tokens and no sentence is used twice; the development tokens are sentences of the
  development split and the test tokens of the test split of the representative open corpus; and the documents of the
  two halves are disjoint.
- **Reader** (`WordSenseBuilder_AllWords_Gold_RoundTrips`, `..._Gold_WrongNumberOfColumns_IsRejected`,
  `..._Gold_WordNotAtItsIndex_IsRejected`, `..._Gold_IndexOutsideTheSentence_IsRejected` (theory: `4`, past the end, and
  `-1`, negative), `..._Gold_AgreementNotHighOrLow_IsRejected`, `..._Gold_SenseOfAnotherLemma_IsRejected`): a file that
  was rendered is read back unchanged, and a missing file reads as empty; a line with another number of columns, a word
  that is not at its index, an index past the end of the sentence or below 0, an agreement other than high and low, or a
  sense that is not a sense of the lemma is rejected with an `InvalidDataException` that names the file and the line (a
  negative index is a data error and not an index error).
- **Draw** (`WordSenseBuilder_AllWords_Draw_IsReproducibleFromTheSeed`, `..._Draw_TokensAreEligible`,
  `..._Draw_RespectsTheLimitsOfLemmaAndSentence`): the same seed gives the same tokens and another seed other ones; each
  token is a noun, verb, adjective or adverb whose lemma is not trained, has two or more senses and whose form the
  lexicon lists with that lemma and part of speech; the limits per lemma and per sentence hold, and the tokens are
  numbered in the order of their sentences.
- **Batches** (`WordSenseBuilder_AllWords_Batches_ListEverySenseInTheOrderOfTheIds`, `..._Batches_HaveNoTagCount`,
  `..._Batches_MarkTheTargetWordAndSeparateTheSplits`): the batch of a group has every sense of the lemma and part of
  speech in the order of the ids, each with only `id`, `pos` and `gloss` (no tag count), and its sentences have the
  target in braces; one batch is written for each split (the last test has no requirement). A lemma with more tokens
  than the batch size is divided into batches of at most that size, each with the senses of the lemma
  (`..._Batches_OversizedGroup_IsDividedToTheBatchSize`: four tokens in batches of three give batches of 3 and 1).
- **Empty split** (`WordSenseBuilder_Program_AllWordsMerge_SplitWithNoKeptToken_GetsAnEmptyFile`): when both labelers
  are unsure of every token of a split, `allwords-merge` overwrites an earlier gold file of the split with an empty
  file, and the reader of the set finds no token.
- **Merge** (`WordSenseBuilder_AllWords_Merge_KeepsTheTokensBothLabelersAgreeOn`, `..._Merge_Disagreement_DropsTheToken`,
  `..._Merge_UnsureAnswer_DropsTheToken`, `..._Merge_MissingAnswerLine_DropsTheToken`,
  `..._Merge_ReportsTheAgreement`, `..._Merge_WrongSense_Fails`, `..._Merge_MissingAnswerFile_Fails`): the merge keeps a
  token that both labelers answered with the same sense (one of them with a low confidence, which makes the agreement of
  the token low), drops a disagreement, an unsure answer of either labeler and a token without an answer line, reports
  the agreement (4 tokens, 2 answered by both, 1 the same, 1 unsure for each labeler, 50.00%) and fails with a sense that
  the token cannot have or with a missing answer file.
- **Measurement** (`WordSenseBuilder_AllWords_Measure_CountsTheTokensAnsweredAtEachLevel`,
  `..._Measure_ReportsThePrecisionOfTheSenseAnswers`, `..._Measure_FineTop1CountsTheFirstRankedSenseOfTheSelectedAnalysis`,
  `..._Measure_SelectedAnalysisWithoutRanking_IsAMiss`, `..._Measure_WrongSelectedAnalysis_IsAMiss`): a stub model gives
  the counts at the part-of-speech and sense levels and of an unknown word; of two sense answers, one right, the precision
  is 1 of 2; the gold sense ranked first in the selected analysis is a hit and ranked second a miss; a selected analysis
  that ranks no sense is a miss with the right analysis counted; and a selected verb analysis that ranks real senses of
  the verb `bank` is a miss and a wrong analysis, although the unselected noun analysis lists the gold sense first.
- **Baselines** (`WordSenseBuilder_AllWords_Baselines_FirstSenseIsTheFirstInInventoryOrder`,
  `..._Baselines_TagCountIsTheHighestCountWithInventoryOrderOnATie`, `..._Baselines_RandomIsOneOverTheNumberOfSenses`):
  of tokens whose gold sense is the first and the last sense of `bank` the first-sense baseline is right for the first;
  of tokens whose gold sense is the sense with the highest count and another one the tag-count baseline is right for the
  first; and the random baseline of three tokens is `3 / n`.
- **Sweep** (`WordSenseBuilder_AllWords_Sweep_ReportsTheMeanLogLossOfTheGoldSense`): the log loss of a token whose gold
  sense is the most frequent one is `-ln` of its probability under the prior, and it is lower for a smaller constant; the
  log loss of no token is 0.
- **Commands** (`WordSenseBuilder_Program_EvaluateAllWords_ReportsTheModelOnEachHalf`,
  `..._ReportsTheBaselinesOnEachHalf`, `..._ReportsTheSweepOnTheDevelopmentHalfOnly`, `..._WithoutGoldSet_Fails`,
  `WordSenseBuilder_Program_AllWordsSampleAndMerge_WriteBatchesAndGoldFiles`): with the committed set `evaluate-allwords`
  writes the report with a row of the model and the three baselines for each half and the sweep once (for the development
  half); without a gold set it fails with a message; `allwords-sample` writes batches and a table of 24 tokens (12 for
  each split) and `allwords-merge` writes the gold files and reports an agreement of 100% when both labelers choose the
  same senses (the last test has no requirement).

#### gold2-power, gold2-sample, gold2-batches, gold2-merge, evaluate-gold2 and compare-gold2

The tests are in `test/WordSense.Builder.Tests/Gold2Tests.cs`. They use the committed catalog, temporary directories,
a small synthetic pool and a stub model; four tests read the committed set `corpus/allwords/v2` and the committed anchors
and weights.

- **Power plan** (`Gold2Power_PairedSampleSize_PlannedMeasures_MatchesThePlan`,
  `Gold2Power_MinimumDetectable_IsTheInverseOfTheSampleSize`, `Program_Gold2Power_PrintsThePlan`): the sample size of
  each planned measure follows the formula of the design (2,745, 1,175, 626, 438 and 375 tokens for the five measures)
  and the planned test tokens are at least 80% of it; the minimum detectable improvement of 1,175 tokens at a
  discordance of 24% is 4.0 points; the normal quantiles are right to five decimals and an improvement that the
  discordance cannot hold, or a probability of 1, is an argument error; `gold2-power` exits with 0 and prints the
  table.
- **Exact McNemar** (`Gold2Power_ExactMcNemar_KnownSplits_GivesTheExactProbability`): the p value is 1 for no discordant
  token and for an even split, `2 / 1024` for 10 against 0, and symmetric in the two counts.
- **Strata** (`Gold2Strata_Of_Forms_FlagsFollowTheLexiconAndTheTrainedLemmas`,
  `Gold2_TrainedLemmas_SingleAnchoredSense_IsUntrained`): `put` bears the same-spelling verb
  flag, `bank` the noun-and-verb flag and the trained or untrained flag by its lemma, a heteronym spelling the
  heteronym flag, `saw` the lemma-ambiguous flag, and a form that the lexicon does not know none (null); the families
  of the sources are grouped into technical, news and literary text. In the committed anchors `always` has two sentences
  for one sense: it is in the lemmas of the anchors but not in the lemmas that get weights
  (`Corpus.ReadWeightedLemmas`), its sense flag is the untrained one, and `WeightsTrainer.HasWeights` is false for one
  anchored sense and true for two.
- **Trained lemmas** (`Gold2_TrainedLemmas_ShippedWeights_AreTheLemmasOfTheTrainerRule`,
  `Gold2_TrainedLemmas_NoWeightsFile_UsesTheAnchorRule`): the lemmas that have weights in both shipped encoder models are
  exactly the lemmas that the rule of the trainer finds in the committed anchors; without a weights file the same
  rule is applied to the anchors of a small directory (a lemma of two senses is trained, a lemma of one is not).
- **Candidates** (`Gold2Sampler_IsCandidateWord_NamesAcronymsAndSymbols_AreLeftOut`): lower-case words, a capitalized
  word at the start of a sentence, `I` and `a` can be drawn; a capitalized word inside a sentence (a single capital
  letter such as `D` included), an acronym, a word
  with an apostrophe, a numeral and a symbol cannot.
- **Draw** (`Gold2Sampler_Draw_SmallPool_RespectsSplitsCapsAndQuotas`): on a pool of 30 documents the same seed gives
  the same tokens and another seed other ones; no document has tokens in both splits; no document exceeds its cap; no
  sentence has more than the planned tokens and no two are adjacent; every token is the word at its index; the ids
  are unique and start with `d-` and `t-`; the quotas of the same-spelling verb and untrained strata are filled.
- **Split** (`Gold2Sampler_AssignSplits_ByFamily_GivesTheDevelopmentShareOfTheSentences`): of the 10 documents of each
  family, 4 go to the development split and 6 to the test split, and each document goes to one split.
- **Files** (`Gold2_Files_RoundTrip`, `Gold2_ReadManifest_WrongWordAtTheIndex_Throws`): a manifest, a documents file
  and a rows file are written and read back the same (the sentence words included), the leakage parts of the set are
  read (one part for the split that has tokens), and a manifest line whose word is not at its index is an
  `InvalidDataException`.
- **Document check** (`Gold2_ValidateDocuments_UsedMissingAndSharedDocuments_AreProblems`,
  `Gold2_CanonicalDocumentKey_GutenbergSpellings_AreOneKey`,
  `Gold2_ValidateDocuments_GutenbergBookUnderAnotherKeyFormat_IsAProblem`): a document whose id is in
  the open corpus sources, a document whose address is there, a manifest token whose document is not listed and a
  document with tokens in both splits are each a problem that names the document. `pg1419`, `PG01419`,
  `gutenberg-1419` and the addresses of the book become `gutenberg-1419`, and other keys are unchanged. A book that the
  provenance writes as `pg1419#s17295`, that the open corpus lists only by its address, or that the first all-words set
  names in a sentence id (`gutenberg-88-s0012`) is a used document, and a book that no part uses is not.
- **Committed set** (`Gold2_CommittedSet_DocumentsAreDisjointFromTheRepository`,
  `Gold2_CommittedRows_AreConsistentWithTheLexiconAndTheManifest`, `Gold2_CommittedRows_MatchTheManifestOneToOne`): the
  document check of the committed set finds no problem (every Gutenberg book under its canonical key); the committed
  rows are one for each token of the manifest with its strata, a part of speech of the lexicon
  or `Other`, a lemma, a status of agreed or adjudicated, a tense for a verb only and a sense of the lemma and part of
  speech when one is given; they match the manifest one to one on id, document, source, sentence, target, form, strata
  and words.
- **Rows against the manifest** (`Gold2_ValidateRows_RowsOfTheManifest_HaveNoProblem`,
  `Gold2_ValidateRows_MissingDuplicateUnknownAndRepeatedIds_AreProblems`,
  `Gold2_ValidateRows_RowThatDiffersFromTheManifest_NamesWhatDiffers`,
  `Gold2_ReadVerifiedRows_RowsThatDoNotMatchTheManifest_Throw`): rows of the manifest in any order have no problem; a
  token without a row, a row that appears twice, a row that the manifest does not have for the split (an unknown id, or
  the id of the other split) and a manifest id that appears twice are each named; a row with another document, sentence,
  target, form or words says what differs; the reader of verified rows throws an `InvalidDataException` for a missing,
  a duplicate and a different row, returns an empty list for a split without rows and tokens, and accepts the complete
  set. The labeled fields are checked apart from the identity
  (`Gold2_ValidateRowFields_ValidRows_HaveNoProblem`, `Gold2_ValidateRowFields_InvalidFields_AreProblemsThatNameTheField`,
  `Gold2_ReadVerifiedRows_InvalidLabeledField_Throws`): a noun with a sense of `bank`, a verb with the tense of its
  features, a token of `Other` and an adjudicated `none` are valid; a part of speech that is a number, in another case or
  unknown, an empty lemma, a sense that is no sense of the lemma, on `Other` or on a lemma of one sense, a `none` of an
  agreed row, a tense that is not allowed, on a noun or against the features, and a status or an agreement outside the
  lists are each a problem that names the field, and the reader of verified rows throws for a part of speech `7`.
- **Target index** (`Gold2_Readers_NegativeTargetIndex_Throws`): a manifest line and a row with the index `-1` are an
  `InvalidDataException` that names the index, not an index out of range.
- **Part of speech names** (`Gold2_TryParsePartOfSpeech_OnlyNames_AreParts`): `Noun` is a part of speech; `1`, `noun`,
  `Other` and null are not, and the answer `other:1:reading` is invalid where `other:Noun:reading` is valid.
- **Answers** (`Gold2Labeling_Resolve_Answers_ResolveToTheAnalysisAndItsSense`): the analyses of `read` come in the
  order of lemma, part of speech and features; an answer resolves to its analysis and a sense of it; an unsure answer
  of the adjudicator and a `none` of the adjudicator resolve to `none`, and a `none` of a labeler is a problem;
  `other:Noun:reading` resolves to a noun without a sense; an analysis that does not exist, a sense that the lemma
  does not have and a part of speech that does not exist are problems that name the answer.
- **Merge** (`Gold2Labeling_Merge_AgreementAndAdjudication_NothingIsDropped`): of four tokens, the one both labelers
  answered the same is settled (agreed, whatever the confidence of one of them) and the three others (another
  analysis, an unsure answer, another sense) go to the adjudicator; a token without an adjudicator answer is the
  problem `t-4: the adjudicator gave no answer.`; with all answers no token is dropped, the status and the agreement
  (`pos`, `analysis`) of each token are right, a `none` is kept, the rows carry the tense class (`past`, `nonpast`) of
  a verb, and the agreement counts, the table and the labels file are rendered.
- **Merge command** (`Program_Gold2Merge_DocumentNotListed_WritesNothing`): with a manifest document that
  `sources.tsv` does not list, `gold2-merge` exits with 1, prints that the document is not in `sources.tsv` and writes
  neither split nor the labels; with the document listed it exits with 0, writes `dev.tsv`, `test.tsv` and `labels.tsv`
  and the written test rows match the manifest.
- **Missing and invalid answers** (`Gold2Labeling_Compare_MissingAndInvalidAnswers_AreReportedOrAdjudicated`): a token
  that the second labeler did not answer is reported as missing and a token whose answer of the first labeler is
  invalid goes to the adjudicator; nothing is settled.
- **Batches** (`Gold2Labeling_WriteBatchesAndReadAnswers_RoundTrip`): a batch of the form `read` has two items with the
  token in braces and the analyses with their senses and glosses; the answer table is read back (`a1` with `unsure`
  and a high confidence, `a2` with a low one) and a line that is no answer is reported; the adjudication batch shows
  the first labeler's answer, and its table is written.
- **Duplicate answers** (`Gold2Labeling_ReadAnswers_DuplicateAnswer_Throws`): a token that two rounds of a labeler both
  answer, and a token that one file answers twice, are an `InvalidDataException` that says the token is answered more
  than once and names the file of its first answer.
- **Retry round** (`Program_Gold2Batches_Missing_KeepsTheBatchIndexOfTheFirstRound`,
  `Gold2Labeling_WriteBatches_TableWithAnotherMapping_IsNotOverwritten`): of three tokens of which the first has an
  answer, `gold2-batches --missing` writes the other two as `sample-r1.tsv` and `g2-r1-t01.json`, leaves `sample.tsv`
  byte for byte as it was, and the answers of both rounds are read together (the first token keeps its answer). Writing
  the same batches again is accepted; writing a table with another mapping is an `InvalidDataException` that names
  `--tag`; the retry tag is the one asked for when it is free, else the first free `-r<n>`.
- **Measurement** (`Gold2Evaluator_MeasureAsync_PosLemmaTenseAndSense_AreScoredPerToken`): a stub model gives, for five
  tokens: a right part of speech, lemma and sense; a right analysis with the wrong sense; a right lemma and part of
  speech with the wrong tense and no scored sense; a token whose gold sense is `none`, which is not scored on the sense;
  and a token that the model does not answer whose gold class is `Other`, which is not scored on the part of speech and
  is wrong on the lemma. The sense on the right analysis
  is scored apart; the tallies of all tokens, of the tense stratum and of the untrained stratum are right; and the
  report has a row of the trained lemmas.
- **Strata and power report** (`Gold2Evaluator_RenderStrataAndPower_FrozenFlagsAgainstTheTrainedLemmas_AreCounted`):
  of three rows, one drawn as trained whose lemma has no weights and one drawn as untrained whose lemma has, the report
  counts 2 drawn as `TR`, 1 still `TR`, 1 moved to `U`, 1 drawn as `U`, 1 now `TR`, 2 tokens of a trained gold lemma
  and 1 of an untrained one; the power table gives 3 tokens for the part of speech and no tokens (n/a) for the tense.
- **Baselines** (`Gold2Evaluator_Baseline_FirstAndMostFrequentSense_AreScoredOnTheSenseOnly`): the first-sense baseline
  is right on a token whose gold sense is the top sense of `bank`, neither baseline is scored on the part of speech,
  and a token without a gold sense is not scored.
- **Paired comparison** (`Gold2Evaluator_Compare_TwoModels_CountsDiscordantTokensAndGivesAnInterval`): of 60 tokens
  that one model gets 40 right and the other 50, the comparison counts 10 tokens that only the second gets right and
  none for the first, gives the exact p value of that split, an interval that contains the difference of 16.67 points,
  one row for each stratum of at least 20 tokens, no row for the sense (no token applies), and the same result again.
  The bootstrap resamples only the documents that contribute tokens
  (`Gold2Evaluator_BootstrapInterval_DocumentsWithoutTokens_AreNotResampled`): two documents of 10 tokens that each
  differ by 2, among 100 documents without a token, give the interval 0.2 to 0.2, and no document at all gives (0, 0).
- **Outcomes** (`Gold2Evaluator_Outcomes_RoundTrip`): the outcomes of two tokens are written and read back the same, and
  a file with a value that is not a flag is an `InvalidDataException`.

The commands that need the committed labeled set or a real model (`gold2-sample` on a real pool, `gold2-batches`,
`gold2-merge`, `evaluate-gold2` and `compare-gold2`) are verified through their units above and through the committed
files; the figures that they print are not asserted by a test. The leakage parts and the document check of the set are
verified in the section _corpus_ below.

#### evaluate-running (tense)

- **Gold class** (`WordSenseBuilder_TenseEvaluator_IsGoldPast_UsesTheTreebankTagThenTheFeatures`, theory): `VBD` and
  `VBN` are
  past, `VB`, `VBP` and `VBZ` are not, and without a treebank tag `Tense=Past` decides.
- **Groups** (`WordSenseBuilder_TenseEvaluator_GroupsOf_ClassifiesTheSurfaceForm`, theory): `put`, `Put` and `come` are
  same-spelling verbs of one lemma, `read` and `reread` are also of the read family, `spread` is not of the read family,
  `found` (the past of `find` and the infinitive of `found`) is decided between two lemmas, and `walk`, `walked`, an unknown
  word and punctuation are not scored.
- **Confusion** (`WordSenseBuilder_TenseEvaluator_RunAsync_ScoresTheConfusionOfPastAgainstNonPast`): with a model that
  reads every `put` as past and does not answer `cut`, a past `put` is right, an imperative `put` is a non-past token read
  as past, an imperative and a past `cut` are not answered (the past one keeps its gold class, so the gold classes tie at
  two and two and the majority baseline is two), a gold noun `put` and a `walk` are not scored; the count, the correct
  tokens, the majority baseline and the gold past tokens follow.
- **Pooling and skipped sets** (`WordSenseBuilder_TenseEvaluator_RunAsync_PoolsTheTreebankFilesAndSkipsGoldWithoutFeatures`):
  the three treebank files give a pooled row per group that adds their counts, and a set whose gold has no features is
  skipped.
- **Sets** (`WordSenseBuilder_TenseEvaluator_Load_ReadsTheSetsAndNotesWhatIsMissing`): the open corpus test and development
  splits and the treebank files next to the gold file are read; a file with three columns or a file that does not exist
  is
  left out with a note.
- **Training file for pooling**
  (`WordSenseBuilder_TenseEvaluator_Load_IncludesTheAdjacentTreebankTrainingFileForPooling`): with `ud_test.tsv`,
  `ud_dev.tsv` and `ud_train.tsv` next to each other, `Load` returns the training file as the set `UD-EWT train` (two
  sentences), without a note, and a run over the three treebank sets gives a pooled row of four tokens (one from the test
  file, one from the development file and two from the training file).
- **Report** (`WordSenseBuilder_TenseEvaluator_Render_FormatsRowsAndNotes`): one row per model, set and group with the count,
  the accuracy, the majority baseline and the confusion.
- **Command** (`WordSenseBuilder_Program_EvaluateRunning_Basic_ReportsTheTenseAndHonorsTheReportOption`): the command
  prints the part-of-speech report and the tense report of Basic, only the tense report with `--report tense` (without
  reading the part-of-speech inputs: a split that does not exist does not stop it), and refuses another value.

#### evaluate-running

- **Test file only** (`WordSenseBuilder_RunningText_DefaultGold_IsTheTestFileAndNotTheDevelopmentFile`): the default
  gold file is the converted test file, never the development file.
- **Sets** (`WordSenseBuilder_RunningText_Load_ReadsTheSetsAndNotesAMissingGoldFile`): the representative test split,
  the gold file and the challenge set are read; a missing gold file leaves its set out with a note that says how to
  make it. A split other than `test` is named after the split and never fills the test column of the model table
  (`WordSenseBuilder_RunningText_Load_DevelopmentSplit_IsNamedAndNotWrittenToTheTestColumn`).
- **Figures for any model** (`WordSenseBuilder_RunningText_Run_ReportsAllAndAmbiguousTokensForAModel`): with a stub
  model, the accuracy on all scored tokens and on the ambiguous tokens, the tokens that are not answered and the
  excluded tokens are counted and rendered, and the table cells carry both accuracies.
- **Command** (`WordSenseBuilder_Program_EvaluateRunning_Basic_PrintsTableAndWritesTheCells`): on the committed corpus
  the Basic model is measured, the left-out treebank set is noted, and the cells of the model table are written.
- **Challenge-set floor** (`WordSenseBuilder_ChallengeSet_Basic_StaysAboveItsFloor`,
  `WordSenseBuilder_ChallengeSet_MiniLM_StaysAboveItsFloor`,
  `WordSenseBuilder_ChallengeSet_BertBaseCased_StaysAboveItsFloor`): the accuracy of each model on the challenge set
  is not below the floor of `corpus/suite/ste-floors.tsv` (the encoder tests skip when the model files are not
  present: the ensure step of the model reports `ModelUnavailableException` or `ModelIntegrityException`, which the
  test turns into the skip, or into a failure when `WORDSENSE_REQUIRE_MODELS` is set).
- The treebank convention map and the checksum of its files belong to
  `tools/open-corpus/scripts/prepare_tagger_eval.py`, which is verified by running it (it exits with an error when a
  file has another SHA-256).

#### train-pos-head

The trainer is tested with a synthetic corpus whose sentences are the combinations of small word lists, laid out like
`corpus/open/representative`, and an encoder stub that returns a fixed vector for each word; no model file is needed.

- **Loadable, deterministic head** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_ProducesALoadableDeterministicHead`):
  runs with one thread and with six write the same bytes; the file reads with the reader of the Onnx package, carries the
  model id, the classes, the variant `word+-1` and the SHA-256 of the training file, predicts the part of speech of
  the words of the corpus from the word and its neighbors, and the report names the selected `C` and the half-precision
  check.
- **Cache** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_SecondRunUsesTheCache`): a second run with the same cache
  directory encodes nothing and writes the same bytes.
- **Selection of C** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_TieGoesToTheSmallestC`): every `C` of the grid is fitted
  and reported, and when several give the same development accuracy the smallest is selected and recorded in the file.
- **Gold classes** (`WordSenseBuilder_PosHeadTrainer_ReadSentences_MapsTheGoldClasses`): the gold class of a token is
  its part of speech, the infinitive marker is a preposition, and punctuation and other tokens have none.
- **Scoring** (`WordSenseBuilder_PosHeadTrainer_Score_AppliesTheLexiconRestriction`,
  `WordSenseBuilder_PosHeadTrainer_LexiconMask_ListsThePartsOfSpeech`): a posterior is restricted to the parts of speech
  of the lexicon, ambiguous words are counted, and tokens that are not scored are skipped; the mask of a word lists its
  parts of speech and is zero for an unknown word and for punctuation.
- **Table of tokens** (`WordSenseBuilder_PosTokenTable_NeighborsAndRows`): the neighbors of a token are found inside its
  sentence only, and the training rows are the tokens with a vector and a class.
- **Fit** (`WordSenseBuilder_MultinomialLogisticTrainer_Fit_Optimum_HasZeroGradientOfTheDenseObjective`,
  `WordSenseBuilder_MultinomialLogisticTrainer_Fit_LearnsTheNeighborRule`,
  `WordSenseBuilder_MultinomialLogisticTrainer_Fit_InvalidInput_Throws`): at the optimum the gradient of an independent,
  dense implementation of the objective vanishes (so the chunked objective and the folding of the standardization are
  right); a rule that depends on the previous word is learned, and a larger `C` fits the training data more tightly; an
  empty table, a class outside the parts of speech and a `C` that is not positive are rejected.
- **Determinism of the fit** (`WordSenseBuilder_MultinomialLogisticTrainer_Fit_IsDeterministicAcrossThreadCounts`): the
  weights and biases are equal, bit for bit, on every run and for every number of threads.
- **Minimizer** (`WordSenseBuilder_Lbfgs_Minimize_Quadratic_FindsTheMinimum`,
  `WordSenseBuilder_Lbfgs_Minimize_Rosenbrock_FindsTheMinimum`,
  `WordSenseBuilder_Lbfgs_Minimize_IsDeterministicAndKeepsTheStart`,
  `WordSenseBuilder_Lbfgs_Minimize_IterationCapAndStartAtMinimum`, `WordSenseBuilder_Lbfgs_Minimize_InvalidArguments_Throw`):
  a convex quadratic and the Rosenbrock valley (which needs the line search) reach their known minimum; the same
  objective and start give the same point bit for bit and the start is not changed; the iteration cap stops the run and
  says it did not converge, and a start at the minimum converges at once; null arguments and limits that are not positive
  are rejected.
- **Head file** (`WordSenseBuilder_PosHeadBlobWriter_Write_RoundTripsThroughTheReader`,
  `WordSenseBuilder_PosHeadBlobWriter_Write_IsDeterministic`,
  `WordSenseBuilder_PosHeadBlobWriter_Round_IsIdempotentAndRejectsOverflow`,
  `WordSenseBuilder_PosHeadBlobWriter_Write_InvalidHead_Throws`,
  `WordSenseBuilder_PosHeadBlobWriter_Write_LoadsAsAPosHead`): a written head reads back with every field and half
  precision weights; the same head gives the same bytes and another head other bytes; rounding is idempotent and refuses
  weights that half precision cannot hold; an inconsistent head, a digest of the wrong size and a non-finite bias are
  refused; the written head loads into the posterior arithmetic of the library.
- **Vector cache** (`WordSenseBuilder_PosVectorCache_WriteAndRead_RoundTripsForTheSameKey`,
  `WordSenseBuilder_PosVectorCache_TryRead_DamagedFile_ReturnsNull`,
  `WordSenseBuilder_PosVectorCache_Hashes_DependOnTheirInputs`): a written file reads back with its vectors and flags
  for the same digest and words only; a damaged file, a truncated file and a file with another magic are not used; the
  digest depends on every part of the encoder identity and the word hash on the sentence boundaries.
- **Command** (`WordSenseBuilder_Program_TrainPosHead_RejectsBadModelsAndGrids`): the command needs a model, rejects an
  unknown model and a model without an encoder (`basic`), and rejects a `--c-grid` that is not a comma-separated list of
  positive numbers, each with exit code 1 and a one-line message.
- **Gate and read-back**: the report of a run states the half-precision comparison with the 0.02-point gate, and the
  trainer reads the file back and compares its predictions. The failing path of the gate needs weights that half
  precision changes by more than the tolerance and is verified by inspection, not by a test.

#### train-pos-head leakage assertion

- **Held-out sentence** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_TrainingSentenceInAHeldOutPart_Aborts`): a training
  sentence that is also a treebank sentence, a read-set sentence or a development sentence stops the run before anything
  is encoded.
- **Run of words** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_SharedRunOfWordsWithTheTreebank_Aborts`): a run of
  eight words that a training sentence shares with a treebank sentence also stops the run.
- **Other training file** (`WordSenseBuilder_PosHeadTrainer_TrainAsync_OtherTrainingFile_Refused`): a `--train` file that
  is not the representative training split is refused.
- **Explicit paths** (`WordSenseBuilder_LeakageCheck_HeldOutSets_ExplicitPathThatDoesNotExist_Throws`,
  `WordSenseBuilder_Program_CheckLeakage_ExplicitPathThatDoesNotExist_Fails`,
  `WordSenseBuilder_Program_CheckLeakage_ExplicitGoldFileThatDoesNotExist_Fails`): without `--gold-dir` and `--read-set`
  the held-out parts are those of the repository; a `--gold-dir` that does not exist throws `DirectoryNotFoundException`,
  one without a gold file throws `InvalidDataException`, a `--read-set` that does not exist throws `FileNotFoundException`
  (each message names the option), and `check-leakage` exits with 1 and the same message, also for a `--gold` file that
  does not exist.
- **Overlap rule** (`WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_FindsTheSameSentence`,
  `WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_RunOfWordsCountsForExternalPartsOnly`): a training sentence that a
  held-out part has too, after normalization, is an overlap and others are not; a run of eight words in common counts for
  an external part (the treebank, the read set) and not for a part of the repository.
- **Held-out parts** (`WordSenseBuilder_LeakageCheck_FromGoldDirectoryAndReadSet_ReadTheFiles`,
  `WordSenseBuilder_LeakageCheck_FromReadSet_ShortLine_Throws`,
  `WordSenseBuilder_LeakageCheck_HeldOutSets_ListsEvaluationAndExternalPartsOnly`): the treebank files that exist in a
  directory are read as external parts and the read set from its sentence column; a read set with a short line is
  refused; the held-out parts of a corpus are its evaluation parts, the treebank files and the read set, never its
  training parts.

#### Head provenance

- **Trained on the committed file** (`WordSenseBuilder_PosHeadProvenance_HeadsAreTrainedOnTheCommittedTrainingFile`): the
  training-file digest recorded in the head embedded in each encoder package equals the SHA-256 of
  `corpus/open/representative/data/train.tsv`.

#### evaluate-running outcomes, compare-outcomes and latency

- **Outcomes** (`WordSenseBuilder_RunningText_Run_RecordsTheOutcomeOfEveryScoredToken`): a scored token is recorded with
  its set, sentence, document, gold and answer; punctuation and unanswered tokens are not.
- **Documents** (`WordSenseBuilder_RunningText_Documents_AreTheSourceIdsOfTheOpenSets`): the documents of the open sets
  are the source ids of their sentences, parallel to the sentences of the set.
- **Outcome file** (`WordSenseBuilder_OutcomeFile_RenderAndRead_RoundTrip`): an outcome file reads back what was written,
  and a malformed file is refused.
- **Exact test** (`WordSenseBuilder_OutcomeComparer_McNemar_IsTheExactTwoSidedBinomialTest`): the p-value is one without
  discordant tokens, the doubled binomial tail otherwise, and tiny for many more repairs than regressions.
- **Gain and interval** (`WordSenseBuilder_OutcomeComparer_Compare_CountsRepairsAndRegressionsWithAnInterval`): the gain,
  the repairs and the regressions are counted over the paired tokens; the interval contains the gain and is the same for
  the same seed.
- **Degenerate gain and the ambiguous subset** (`WordSenseBuilder_OutcomeComparer_Compare_DegenerateGainAndAmbiguousSubset`):
  a version that is right on every token the base gets wrong has an interval that is exactly the gain, and the ambiguous
  subset counts only ambiguous tokens.
- **Clusters and unpaired tokens** (`WordSenseBuilder_OutcomeComparer_Compare_ClustersByDocumentAndCountsUnpairedTokens`):
  clustering by document merges the sentences of a document, and tokens that only one file has are left out and counted.
- **Invalid input** (`WordSenseBuilder_OutcomeComparer_Compare_InvalidInput_Throws`): an unknown cluster unit, no common
  token and a number of replicates below one are rejected.
- **Report** (`WordSenseBuilder_OutcomeComparer_Render_ListsTheConfusionChanges`): the changes of the wrong cells are
  listed, a cell that grows by more than 0.3 points is named, and the report states the interval method.
- **Latency sample** (`WordSenseBuilder_RunningText_MeasureLatency_UsesASeededSample`): the sample is the same for a seed
  and differs for another one, it is bounded by the number of sentences, and an empty sample gives zeros.
- **Latency label and footer** (`WordSenseBuilder_RunningText_RenderLatency_NamesTheSplitThatWasRead`,
  `WordSenseBuilder_RunningText_Render_FooterNamesTheSplitThatWasRead`): the latency table and the footer of the report
  name the split that was read, `dev` for `--split dev` and `test` by default.
- **Commands** (`WordSenseBuilder_Program_EvaluateRunningAndCompareOutcomes_Basic_WriteAndCompare`,
  `WordSenseBuilder_Program_CompareOutcomes_RejectsBadInput`): `evaluate-running` for Basic writes the outcomes and the
  latency; the comparison of a file with itself shows no gain and a degenerate interval; a file with two models needs the
  model to be named; `compare-outcomes` rejects a missing `--new`, an unknown `--cluster` and a `--reps` below one with
  exit code 1.
- **Report without outcomes** (`WordSenseBuilder_Program_EvaluateRunning_ReportWithoutOutcomes_Succeeds`):
  `evaluate-running --report running` without `--outcomes` exits 0 with no error, and `--report tense` with `--outcomes`
  writes no outcome file.
- **Development split** (`WordSenseBuilder_Program_EvaluateRunning_DevelopmentSplit_KeepsTheTestColumnAndRecordsDocuments`):
  `evaluate-running --split dev` names its open set `open corpus dev (representative)`, leaves the running-text column
  of the model table unmeasured, records the outcomes under that name with the source documents of the sentences, and
  times the sample of the development split.

#### suite

- **Rows and expectations** (`WordSenseBuilder_Suite_Read_ParsesRowsAndExpectations`): a well-formed suite gives the
  sentences with their expected analysis and acceptable senses and the expectation (fine, answer or miss) of each model.
- **Missing expectation** (`WordSenseBuilder_Suite_Read_RowWithoutModelExpectation_IsRejected`): a row with no
  expectation for a model, a value that is no expectation, a header without a model and an unknown row are rejected.
- **Unknown sense** (`WordSenseBuilder_Suite_Read_UnknownSenseOrSenseOfAnotherLemma_IsRejected`): a sense that is not in
  the catalog, or is a sense of another lemma or part of speech than the analysis, is rejected.
- **Malformed rows** (`WordSenseBuilder_Suite_Read_MalformedRow_IsRejected`,
  `WordSenseBuilder_Suite_Read_RepeatedIdOrWrongHeader_IsRejected`): a wrong number of columns, an analysis that is not
  `lemma/Part`, features without a value, a target that is not at its index or not single, a repeated id and a wrong
  header are rejected with the line.
- **Grading** (`WordSenseBuilder_Suite_Grade_JudgesTheAnswerAtItsOwnLevel`,
  `WordSenseBuilder_Suite_Grade_AnalysisOnlyRow_ChecksLemmaPartOfSpeechAndFeatures`,
  `WordSenseBuilder_Suite_GradeAsync_ClassifiesTheSentenceOfEveryRow`): a sense, group or part-of-speech answer is
  judged at its own level; the grade is fine only when the most probable ranked sense is acceptable; a row without
  senses asks for the analysis only.
- **Regressions** (`WordSenseBuilder_Suite_Regressions_ListRowsBelowTheirExpectation_AndExpectationsRoundTrip`): the
  rows below their expectation are listed, and the expectation file written from grades reads back.
- **Committed suite** (`WordSenseBuilder_Suite_Committed_ReadsWithAnExpectationForEveryRow`): the 364 rows read and
  every model has an expectation for every row.
- The expected passes of the committed suite are asserted by `BasicSuite_ExpectedPasses_StillPass`,
  `MiniLMSuite_ExpectedPasses_StillPass` and `BertBaseCasedSuite_ExpectedPasses_StillPass` in the test projects of the
  models (see their verification documents); the encoder tests skip when the model files are not present.
- **Answers of the suite** (`WordSenseBuilder_SuiteDiff_AnswerOf_DescribesTheAnswer`): the answer of a row has its grade,
  level, analysis and sense or group, and reads as one line.
- **Outcome file** (`WordSenseBuilder_SuiteDiff_RenderAndReadOutcomes_RoundTrip`): the answers written to an outcome
  file read back, and a malformed file is rejected.
- **Diff against a baseline** (`WordSenseBuilder_SuiteDiff_Diff_ListsEveryChangeInBothDirections`): every row whose
  grade or answer changed is listed, in both directions, with its verdict (better, WORSE or same grade with another
  answer), the gold of the row and the expectation of the model; rows that did not change are not listed.
- **Baseline without a row** (`WordSenseBuilder_SuiteDiff_Diff_MissingBaselineRow_Throws`): a baseline that lacks a row
  of a model is rejected, and the gold of a row that has senses lists them.

#### shape probe

- **Rows** (`WordSenseBuilder_ShapeProbe_ReadRows_ParsesRows`): a well-formed file gives the rows with their shape,
  lemma, gold part of speech, the words without braces and the index of the target.
- **Wrong header** (`WordSenseBuilder_ShapeProbe_ReadRows_WrongHeader_IsRejected`): another header is rejected.
- **Wrong column count** (`WordSenseBuilder_ShapeProbe_ReadRows_WrongColumnCount_IsRejected`): a row without six columns
  is rejected with the line.
- **Unknown shape** (`WordSenseBuilder_ShapeProbe_ReadRows_UnknownShape_IsRejected`): a shape that is not one of the
  seven is rejected.
- **Gold of the shape** (`WordSenseBuilder_ShapeProbe_ReadRows_GoldNotOfTheShape_IsRejected`): a Verb gold for a noun
  shape and a Noun gold for a verb shape are rejected.
- **Target** (`WordSenseBuilder_ShapeProbe_ReadRows_TargetMissingDoubledOrMisplaced_IsRejected`): a target index that is
  not that of the braced word, a sentence without a braced word, with two, or with a double space is rejected.
- **Empty id** (`WordSenseBuilder_ShapeProbe_ReadRows_EmptyId_IsRejected`) and **empty lemma**
  (`WordSenseBuilder_ShapeProbe_ReadRows_EmptyLemma_IsRejected`): a row with an empty id and a row with an empty lemma
  are each rejected.
- **Repeated id** (`WordSenseBuilder_ShapeProbe_ReadRows_RepeatedId_IsRejected`): an id that occurs twice is rejected.
- **Missing file** (`WordSenseBuilder_ShapeProbe_Read_MissingFile_IsEmpty`): a corpus without `shapes/probe.tsv` has no
  rows; with the file it has its rows.
- **Committed size** (`WordSenseBuilder_ShapeProbe_CommittedFile_HasEnoughRowsInEveryShape`):
  `corpus/shapes/probe.tsv` reads, has between 400 and 600 rows, at least 60 for each shape and at least 300 lemmas.
- **Committed targets** (`WordSenseBuilder_ShapeProbe_CommittedFile_TargetsAreFormsOfTheirLemma`): the target of every
  row is a form of its lemma with the gold part of speech in the lexicon.
- **Right answers** (`WordSenseBuilder_ShapeProbe_Answer_IsCorrect`): an answer is right when its part of speech is the
  gold; a word that is not answered is wrong.
- **Harmful answers** (`WordSenseBuilder_ShapeProbe_Answer_IsHarmful`): an answer is harmful only for a gold noun
  answered as a verb with a confidence of at least 0.45 (0.44 is not).
- **Classification** (`WordSenseBuilder_ShapeProbe_AnswerAsync_ReadsTheTargetWord`): with a stub model, the model gets the
  words of the sentence and the answer is the part of speech and the confidence of the target word.
- **Accuracy report** (`WordSenseBuilder_ShapeProbe_Render_ReportsAccuracyPerShape`): with two stub answer sets, the
  accuracy per shape, per gold and overall is rendered; a shape without rows shows `n/a`.
- **Harmful report** (`WordSenseBuilder_ShapeProbe_Render_ReportsTheHarmfulRatePerNounShape`): the harmful count and
  share per noun shape are rendered for each model.
- **Outcomes** (`WordSenseBuilder_ShapeProbe_RenderOutcomes_WritesOneLinePerModelAndRow`): one line for every model and
  row with the answer, the confidence and the two flags; a word that is not answered is `none`.
- **Command** (`WordSenseBuilder_Program_EvaluateShapes_ReportsEveryShapeForTheModel`): on the committed corpus the Basic
  model is measured and every shape and `all noun shapes` are reported.
- **Command outcomes** (`WordSenseBuilder_Program_EvaluateShapes_WritesTheOutcomesFile`): with the outcomes option the
  outcomes file is written.
- **Command without the probe set** (`WordSenseBuilder_Program_EvaluateShapes_WithoutProbeFile_Fails`): a corpus without
  the probe set exits with code 1 and a message that names the file.

#### shape rows

The shape rows are written by a quota tool (`tools/quota-fill`) that is not a reviewed unit; the tests verify the corpus
it leaves.

- **Noun rows** (`WordSenseBuilder_ShapeRows_NounRowOfRareNounLemma_IsAbsent`): on the committed corpus, more than 10,000
  shape rows are found by their provenance tag, and none of the noun shape rows is of a lemma whose noun senses hold less
  than 2 percent of the noun and verb tag counts of `corpus/curation/senses.tsv`.
- **Noun units** (`WordSenseBuilder_ShapeRows_NounUnitOfRareNounLemma_IsAbsent`): `corpus/coverage/shape-coverage.tsv`
  has no noun unit of such a lemma.

#### open-counts

- **Corpus reader** (`WordSenseBuilder_OpenCorpus_ReadFile_ReadsSentencesAndTokens`,
  `WordSenseBuilder_OpenCorpus_ReadSplit_FindsTheFileOfAPart`,
  `WordSenseBuilder_OpenCorpus_ReadFile_MissingFileAndMalformedLine_Throw`): the header comments and the targeted
  comments are skipped, the sentence comments and the seven-column token lines are read, a split is found by part and
  name, and a missing file and a malformed token line are errors.
- **Joined contractions** (`WordSenseBuilder_OpenCorpus_ToTagged_JoinsContractionsButNotPossessives`,
  `WordSenseBuilder_OpenCorpus_ToTagged_KeepsClitics_WhenTheJoinedFormIsNotInTheText`): `do` and `n't`, `ca` and `n't`,
  and `it` and `'s` are one token with the tag of the first part, and a possessive `'s`, a joined form that is not in
  the text and a clitic after punctuation stay as they are.
- **Committed corpus** (`WordSenseBuilder_OpenCorpus_Committed_ReadsEverySplitAndKeepsTheStePartInTheChallengeSet`):
  every split of the committed corpus reads, the splits of the representative part have no document in common, the 32
  simplified-technical-English sentences are in the test split of the targeted part, and the targeted part shares no
  sentence with the representative part.
- **Tag mapping** (`WordSenseBuilder_TaggerCounts_MapTag_MapsUniversalTags`): the universal tags map onto the tags of
  the tagger, with the particle `to` told apart.
- **Word keys** (`WordSenseBuilder_TaggerCounts_KeyOf_AcceptsLettersAndApostrophes`): only tokens made of letters and
  apostrophes get a word key.
- **Counts** (`WordSenseBuilder_TaggerCounts_Count_CountsNgramsAndWords`): the counts include the sentence boundaries
  and the word rows.
- **Round trip** (`WordSenseBuilder_TaggerCounts_WriteAndRead_RoundTripsDeterministically`): the count files read back
  as written and are byte-identical on a repeat run.
- **Malformed files** (`WordSenseBuilder_TaggerCounts_Read_MalformedFiles_Throw`): a count file with a wrong header, a
  malformed word line or a malformed n-gram line throws `InvalidDataException`.
- **Clause counts** (`WordSenseBuilder_TaggerCounts_Count_CountsClauseNgrams`): for a sentence with a verb in its first
  clause only, the bigrams and trigrams after the verb are counted in the clause state with a verb up to the punctuation
  and not after it, the start of the sentence is never counted, and no clause count is above its total.
- **Non-finite start** (`WordSenseBuilder_TaggerCounts_Count_NonFiniteStartDoesNotSetTheClauseState`): a
  sentence-initial verb that the lexicon lists only as a gerund does not count the following bigram in the clause state
  with a verb, while an infinitive at the start does.
- **Malformed clause file** (`WordSenseBuilder_TaggerCounts_Read_MalformedClauseFile_Throws`): a clause count file
  with a wrong header or a unigram line throws `InvalidDataException`.
- **Clause bigram above its total** (`WordSenseBuilder_TaggerCounts_Read_ClauseBigramAboveTotal_Throws`): a clause
  bigram count larger than the total count of the same bigram throws `InvalidDataException`.
- **Clause trigram above its total** (`WordSenseBuilder_TaggerCounts_Read_ClauseTrigramAboveTotal_Throws`): a clause
  trigram count larger than the total count of the same trigram throws `InvalidDataException`.
- **Missing clause file** (`WordSenseBuilder_TaggerCounts_Read_MissingClauseFile_Throws`): reading counts whose clause
  file is missing throws `FileNotFoundException`.
- **Clause file written and read back** (`WordSenseBuilder_TaggerCounts_WriteAndRead_RoundTripsDeterministically`): the
  clause counts read back as written, and the clause file is byte-identical on a repeat run, with LF line ends.
- **Command** (`WordSenseBuilder_Program_OpenCounts_WritesCountFiles`): the command counts the training split of the
  representative part of a tiny corpus into the counts folder.

#### train-basic and tune-basic

- **Closed-class words** (`WordSenseBuilder_BasicTrainer_ClosedMaskOf_AppliesThresholds`): a word becomes closed-class
  only with enough occurrences and a dominant closed-class share.
- **Selection** (`WordSenseBuilder_BasicTrainer_Train_SelectsWordsAndSuffixes`): the table holds the closed-class words
  and the ambiguous words, and the suffixes of words the lexicon lacks.
- **Cue table** (`WordSenseBuilder_BasicTrainer_Train_CueTablePrefersSeenAnalysis`): the cue table of a word prefers the
  analysis its context words occurred with.
- **No prior from the anchors** (`WordSenseBuilder_BasicTrainer_Train_AnalysisPriorIgnoresAnchorCounts`): every column
  of a table has the same prior, however many anchors it has.
- **Labels from senses** (`WordSenseBuilder_BasicTrainer_Train_ColumnsAreLemmaAndPartOfSpeech`,
  `WordSenseBuilder_BasicTrainer_KeyOf_RollsTheRowUpToLemmaAndPartOfSpeech`): the analysis label of an anchor is the
  lemma and part of speech of its fine sense; anchors of two senses of one analysis train the same column, and a lemma
  with one part of speech gets no table.
- **Choosers** (`WordSenseBuilder_BasicTrainer_Train_ChooserRowsMakeFormTablesWithTenseColumns`,
  `WordSenseBuilder_BasicTrainer_KeyOf_ChooserLabel_StandsForOneAnalysisOrNone`): chooser rows make a table keyed by the
  form with a column per tense, the sense sentences of the same key go to the column of their slot or are left out and
  counted when the tenses split it, and a label that stands for no single analysis is left out.
- **Writer** (`WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack`,
  `WordSenseBuilder_BasicTablesWriter_Write_ShortHash_Throws`): equal inputs give identical table bytes that
  `BasicTables` reads back, a changed hyper-parameter changes the header, and a short inventory hash is rejected.
- **Summary** (`WordSenseBuilder_BasicModelData_Summary_NamesSizes`): the summary names the sizes of the trained data.
- **Tense classes** (`WordSenseBuilder_BasicTrainer_TenseClassOf_SeparatesPastFromTheFormsThatShareItsSpelling`): a verb
  with `Tense=Past` is a past example; the infinitive, the imperative and the present of every person but the third
  singular are non-past examples; the third singular, the gerund, the present participle and a token without features are
  left out.
- **Tense table** (`WordSenseBuilder_BasicTrainer_TrainTense_LearnsTheContextOfPastAndNonPast`,
  `WordSenseBuilder_BasicTrainer_TrainTense_NeedsBothClassesAndIsDeterministic`,
  `WordSenseBuilder_BasicTrainer_Train_WithVerbs_WritesTheTenseTableAndWeight`,
  `WordSenseBuilder_BasicTablesWriter_Write_WithTenseTable_IsDeterministicAndCurrentFormat`): from sentences in which the
  context `had X` is past and `to X` non-past for six different verbs, the weight of the left neighbor `had` is positive
  and that of `to` negative, the prior is the log odds of the seven past and six non-past verbs, the third-person
  context is not learned, sentences without gold and non-verbs are skipped; there is no table without both classes or
  when no feature reaches the minimum; equal inputs give equal tables and equal bytes with the magic `WSB7`; a model
  trained with verbs writes the table and the weight, which `BasicTables` reads back, and a model without verbs has none.
- **Noun/verb table** (`WordSenseBuilder_NounVerbStacker_Fit_FeaturesOfTheClassesGetTheirSign`,
  `..._Fit_LearnsOnlyWhatTheOffsetMisses`, `..._Fit_PenaltyShrinksTheWeights`, `..._Fit_RareFeaturesAndOneClassGiveNothing`,
  `..._Fit_IsDeterministic`, `..._NullArguments_Throw`, `..._Collect_OneSampleForEveryNounOrVerbWithBothAnalyses`,
  `..._Apply_FitsATableThatSeparatesTheNeighborWords`, `..._Apply_WithoutFolds_ReturnsTheModelUnchanged`,
  `..._Apply_IsDeterministic`, `..._Fit_CollidingFeaturesCountOnceEach`,
  `..._Collect_OffsetsOfAFoldDoNotDependOnItsOwnGoldTags`,
  `..._Collect_IgnoresTheReliabilitiesMeasuredOnTheWholeSplit`): on toy samples, a feature seen with verbs gets a positive
  weight and one seen with nouns a negative weight; a feature that the offset already explains gets no weight; a larger
  penalty gives smaller weights; features below the minimum and samples of one class give no table; equal samples give
  equal tables; null arguments throw; the cross-fitted collection yields one sample for every gold noun or verb whose
  word has both analyses; on a toy corpus the fitted table separates the neighbor words that the tagger alone cannot;
  without folds the model comes back unchanged and equal inputs give equal tables. Two different features with the
  same hash (the left word `to` and the second right word `instead`) in words that are four-fifths verbs give a table
  whose score for a word with both is the log odds of the verbs (1.39 nats, not twice that), as the runtime computes it.
  The offsets of the words of one fold are the same when the gold tags of that fold are turned around, while those of
  the other folds change, and they are the same when the model carries other running-text reliabilities, so a fold is
  scored by a model that has seen no gold tag of the fold.
- **Noun/verb table in the file** (`WordSenseBuilder_BasicTablesWriter_Write_CarriesTheNounVerbTableAndWeight`,
  `WordSenseBuilder_BasicTablesWriter_Write_WithoutNounVerbTable_WritesNoNounVerbSection`,
  `WordSenseBuilder_BasicHyperparameters_NounVerb_IsTuned`): the weights, the hashes and the weight of the noun/verb
  model reach the table that `BasicTables` reads back, a model without a table writes no section, and the defaults are
  among the grid values.
- **Clause counts in the table** (`WordSenseBuilder_BasicTablesWriter_Write_CarriesTheClauseCounts`): the clause counts
  of the tag counts reach the table that `BasicTables` reads back, equal to those of `TaggerCounts` and the data, and a
  determiner after a verb is counted in the clause state with a verb and not at the start of a sentence.
- **Clause smoothing in the table** (`WordSenseBuilder_BasicTablesWriter_Write_CarriesTheClauseSmoothing`): the
  smoothing of the hyper-parameters (7.5 in the test) reaches the header of the table that `BasicTables` reads back.
- **Format revision** (`WordSenseBuilder_BasicTablesWriter_Write_StartsWithTheMagicWsb7`): the table starts with the
  magic `WSB7`.
- **Word prior in the table** (`WordSenseBuilder_BasicTablesWriter_Write_CarriesTheWordPrior`,
  `WordSenseBuilder_BasicHyperparameters_WordPrior_IsOnAndAccepted`): the share, the smoothing, the sense weight and the
  exponent of the hyper-parameters reach the header that `BasicTables` reads back, and the shipped hyper-parameters have
  a share in (0, 1] with a positive smoothing or sense weight that the runtime accepts.
- **Prior grid baseline** (`WordSenseBuilder_BasicHyperparameters_PriorGrid_HasTheBaseline`): the settings without the
  prior have a share of 0 and are in the prior grid, and no row of the grid is empty.
- **Every ambiguous word is tabled** (`WordSenseBuilder_BasicTrainer_Train_NoLimit_TablesEveryAmbiguousWord`,
  `WordSenseBuilder_BasicHyperparameters_TopAmbiguousWords_IsUnlimitedByDefault`): without a limit the ambiguous word of
  the counts is tabled with the closed-class word, and the shipped hyper-parameters set no limit.
- **Limited tabling** (`WordSenseBuilder_BasicTrainer_Train_LimitZero_TablesNoAmbiguousWord`,
  `WordSenseBuilder_BasicTrainer_Train_TopAmbiguousWordsBetween_KeepsTheMostFrequentThenBySpelling`): with a limit of 0
  only the closed-class words are tabled; with four ambiguous words of 5, 3, 3 and 1 occurrences, a limit of 2 keeps
  the most frequent word and, of the two words with 3 occurrences, the one that comes first in spelling, a limit of 3
  keeps both of them, and a second run with the limit 2 gives the same words.
- **Cue reliability** (`WordSenseBuilder_BasicTrainer_Train_HeldOutAccuracy_IsMeasuredOnSentencesOutsideTheTable`,
  `WordSenseBuilder_BasicTrainer_Train_AccurateTable_IsFullyReliable`,
  `WordSenseBuilder_BasicTrainer_Train_TableAtChance_HasNoReliability`,
  `WordSenseBuilder_BasicTrainer_Train_DampingOff_LeavesTheTableFullyReliable`,
  `WordSenseBuilder_BasicTrainer_ReliabilityOf_ScalesTheSkillOverChance`,
  `WordSenseBuilder_BasicTablesWriter_Write_CarriesTheCueReliability`): the trainer runs on toy anchors of `record`. A
  table whose features separate its analyses has a held-out balanced accuracy of at least 0.95 and a reliability of 255.
  A table whose sentences share no feature has a held-out accuracy of 0.5, which it could not have on its own training
  sentences, and a reliability of 0. With a target of 0 both stay at 255. The reliability is proportional to the skill over
  chance between the two (theory over accuracy, analyses and target, including no target and no measurement), and it
  reaches the table that `BasicTables` reads back.
- **Running-text reliability** (`WordSenseBuilder_CueRunningReliability_FactorOf_ShrinksTheNetBenefitTowardOne`,
  `WordSenseBuilder_CueRunningReliability_Combine_UsesTheProductOrTheMinimum`,
  `WordSenseBuilder_CueRunningReliability_Apply_MeasuredTable_CombinesTheFactorWithTheAnchorReliability`,
  `WordSenseBuilder_CueRunningReliability_Apply_UnmeasuredTable_KeepsTheAnchorReliability`,
  `WordSenseBuilder_CueRunningReliability_MeasureAsync_TruthfulCues_HelpAndDoNotHurt`,
  `WordSenseBuilder_CueRunningReliability_MeasureAsync_MisleadingCues_HurtAndAreDamped`,
  `WordSenseBuilder_CueRunningReliability_MeasureAsync_IsDeterministic`,
  `WordSenseBuilder_CueRunningReliability_ApplyAsync_WithoutFolds_ReturnsTheModel`,
  `WordSenseBuilder_BasicTrainer_WithCounts_ReplacesTheStatisticsOnly`): the factor is 1 when a table helps at least as
  often as it hurts and the pseudo-count ratio otherwise (theory, including no evidence and no pseudo count); the
  combination is the product or the smaller of the two and is limited to 0 to 255; a table with statistics gets the
  combined reliability and the counts, one without keeps its anchor reliability; on a toy world where the tagger cannot
  decide a word, a table whose context says the truth helps and never hurts and keeps 255, and a table whose context
  says the opposite hurts and is damped; the same input gives the same statistics; sentences that all fall into one fold
  are not measured (`..._MeasureAsync_AllSentencesInOneFold_MeasuresNothing`); without folds the model comes back
  as it is and nothing is measured; a model with other counts keeps its cue tables, takes their statistics and drops
  the tense and the noun/verb table.
- **Clause counts are deterministic** (`WordSenseBuilder_BasicTablesWriter_Write_WithClauseCounts_IsDeterministic`):
  data whose counts have clause counts gives the same bytes on every write.
- **Corpus features** (`WordSenseBuilder_OpenCorpus_ToTagged_CarriesTheLemmaAndFeaturesOfEveryToken`): the features and
  lemma of every token are read and a joined contraction has those of its first part.
- **Hyper-parameters** (`WordSenseBuilder_BasicHyperparameters_Default_IsConsistent`): the defaults carry the values
  the runtime reads and the tuning grid includes them.
- **Clause smoothing is tuned** (`WordSenseBuilder_BasicHyperparameters_ClauseSmoothing_IsTuned`): the grid of the
  smoothing of the clause state has the default among its values and no negative value.
- **Tuning** (`WordSenseBuilder_BasicTuner_SplitAnchors_IsDeterministicAndDisjoint`,
  `WordSenseBuilder_BasicTuner_ScoreTagger_CountsAmbiguousTokens`,
  `WordSenseBuilder_BasicTuner_RunAsync_MissingCounts_Throws`,
  `WordSenseBuilder_BasicTuner_RunAsync_UnknownSection_Throws`): the anchor split is deterministic and its parts are
  disjoint, the tagger score counts the tokens that have several parts of speech with the gold tag among them, missing
  tag counts are an error and so is a section that tune-basic does not know.
- **Statistical model has no weights command** (`WordSenseBuilder_Program_Weights_BasicModel_PointsToTrainBasic`):
  `weights basic` exits with code 1 and the message names `train-basic`.
- **Command** (`WordSenseBuilder_Program_TrainBasicAndEvaluateTagger_TinyCorpus_WriteAndScoreTable`,
  `WordSenseBuilder_Program_TrainBasic_WithoutCounts_ReportsError`): a tiny corpus gives a table that `BasicTables`
  loads and that `evaluate-tagger` scores; without the count files the command exits with code 1 and names
  `open-counts`.
- **Tuning reads no evaluation data**
  (`WordSenseBuilder_Program_TuneBasic_ScoresOnlyTheDevelopmentInputs`): in a temporary corpus with valid training and
  development splits of the open corpus (a same-spelling verb `put` among them), a development treebank file, and test
  splits (representative and targeted) and treebank test and training files that fail when they are read (a malformed
  line), `tune-basic --gold` succeeds, so none of them was read, and its tense table has rows for the weight 1.00 on
  `open corpus dev` and `UD-EWT dev` and none for `UD-EWT test`, `UD-EWT train` or `open corpus test`; the grid table
  names the smoothing of the clause state and has the treebank development column, and the output has the table of the
  word prior and the table of the cue reliability.
- **Sections** (`WordSenseBuilder_Program_TuneBasic_SectionsPrior_RunsOnlyThePriorSection`): `tune-basic --sections prior`
  on the same kind of temporary corpus succeeds and its output has the table of the word prior and none of the tables
  of the grid, the cue reliability or the tense weight.
- **Noun/verb section** (`WordSenseBuilder_Program_TuneBasic_SectionsNounVerb_RunsOnlyTheNounVerbSection`):
  `tune-basic --sections nounverb` on the same kind of temporary corpus succeeds and its output has the table of the
  noun/verb weight and none of the other tables.
- **Training reads only the training split**
  (`WordSenseBuilder_Program_TrainBasic_ReadsOnlyTheTrainSplit`): in a temporary corpus whose training split is valid and
  whose development and test splits (representative and targeted) fail when they are read, `open-counts` and
  `train-basic` succeed, so neither read them, and the table that `train-basic` wrote has a tense table trained from the
  verbs of the training split (the output names it).

#### evaluate-tagger

- **Scoring** (`WordSenseBuilder_TaggerEvaluator_EvaluateAsync_ScoresAnsweredWordTokens`): a token is scored when the
  classifier answers it and its gold tag is a word class; punctuation and other gold tags are excluded, tokens that are
  not answered are counted apart, the ambiguous tokens are counted on their own, and the tokens whose gold tag is noun or
  verb and whose word has both a noun and a verb analysis (the noun-verb tokens) are counted with their right ones.
- **Closed and open words** (`WordSenseBuilder_TaggerEvaluator_EvaluateAsync_CountsClosedAndOpenWords`): the tokens whose
  word has a pronoun, preposition, conjunction, determiner or auxiliary analysis and also a noun, verb, adjective or
  adverb one are counted with their right ones.
- **Starting verbs** (`WordSenseBuilder_TaggerEvaluator_EvaluateAsync_CountsStartingVerbs`): the tokens that are the
  first word of their sentence, have the gold tag verb and are followed by a determiner are counted with their right
  ones.
- **Gold file** (`WordSenseBuilder_TaggerEvaluator_ReadGold_GroupsTokensBySentence`,
  `WordSenseBuilder_TaggerEvaluator_ReadGold_SixColumns_CarriesLemmaTagAndFeatures`,
  `WordSenseBuilder_TaggerEvaluator_ReadGold_MalformedLine_Throws`): the tokens are grouped by sentence id, a line
  that is not three columns (or six: with the lemma, the treebank tag and the features, `_` for none) with a tag letter
  is
  rejected, a six-column file gives the tokens their lemma, tag and features, a three-column file gives none, and a file
  that mixes the two is rejected.
- **Table** (`WordSenseBuilder_TaggerEvaluator_Render_FormatsRows`): one row per set with the accuracy, and n/a for an
  empty set.
- **Command** (`WordSenseBuilder_Program_TrainBasicAndEvaluateTagger_TinyCorpus_WriteAndScoreTable`): the command prints
  the headline row for the test split of the representative part.

#### corpus licenses

- **Allowed sources** (`WordSenseBuilder_CorpusLicenses_Provenance_NamesOnlyAllowedSources`): every provenance file of
  the committed corpus has the columns `id`, `split`, `source` and `source_key`, and the source of every row is one of
  the allowed sources (so none is a Tatoeba CC BY, Wikipedia, Wiktionary, treebank or other unlisted source).
- **The list of sources** (`WordSenseBuilder_CorpusLicenses_ProvenanceSources_AllowsOnlyTheListedSources`): the seven
  real-text sources and the two written sources are allowed, and an excluded or unknown source is refused; only the
  written sources are not real text.
- **Every sentence has an origin** (`WordSenseBuilder_CorpusLicenses_EverySentenceRow_HasProvenance`): the lemma, form
  or label and sentence of every row of the sense, chooser and real-text files hash to a provenance id.
- **No treebank and no legacy corpus** (`WordSenseBuilder_CorpusLicenses_Corpus_HasNoTreebankDataAndNoLegacyFolders`):
  the corpus folder holds no CoNLL-U file and none of the retired folders.

#### appendix

- **Appendix lists lemmas senses and forms** (`Appendix_Generate_ListsLemmasSensesAndForms`): a synthetic corpus and
  inventory give an appendix with the parts-of-speech overview, the section on keys and heteronyms, the lemmas in
  alphabetical order, their senses and every analysis of their forms with the features (the forms spelled like the
  lemma included, with the part of speech first when there are several); markdown characters are escaped, lines are
  wrapped, line endings are LF and the output repeats byte for byte.
- **Senses carry group key and gloss** (`Appendix_Generate_ListsSensesWithGroupKeyAndGloss`): each sense is listed
  with its id, group, OEWN sense key (none for a project sense) and gloss.
- **Incomplete input is rejected** (`Appendix_Generate_RejectsIncompleteInput`, `Program_Appendix_MissingCorpus_Fails`):
  a missing anchors directory, a trained sense without an anchor sentence and an empty inventory are each rejected with
  an error that names the problem, and the command exits with code 1.
- **Command** (`Program_Appendix_WritesFile`): `appendix` writes the file given with `--output`.
- **Committed appendix is current** (`Appendix_Committed_MatchesCorpus`): the appendix regenerated from the committed
  corpus and data equals `docs/user_guide/appendix-senses.md` (line endings normalized), so the committed appendix
  cannot go stale; `lint.ps1` performs the same comparison.

#### keys and heteronyms

- **Key export** (`KeyExport_Render_SortsTheKeysAfterTheHeader`, `KeyExport_Write_ListsEveryKeyOfTheLexicon`,
  `Program_Keys_WritesTheKeyFile`): the file starts with the comment line that names the format, the inventory version
  (`WordLexicon.Version`) and the number of keys; the other lines are the keys in ordinal order (upper case before lower
  case), as many as `WordLexicon.Keys()` lists (more than a million) with no line twice; the command needs `--output`
  and exits with code 1 without it.
- **Pronunciation comparison** (`Heteronyms_NormalizePronunciation_IgnoresDialectButNotStress`): syllable marks, length
  marks and rhoticity do not make two pronunciations differ, a stress difference or a vowel difference does.
- **Heteronyms of each level** (`Heteronyms_Build_ListsTheKnownHeteronymsOfEachLevel`,
  `Heteronyms_Build_ClassesCarryTheKeys`): the committed data and table list `abuse`, `minute`, `sow`, `polish`,
  `close`, `august`, `wound`, `dove`, `use`, `object` and `lives` at the entry level (some derived from the
  pronunciations, some curated), `read`, `reread`, `misread` and `proofread` at the tense level and `bass`, `bow`,
  `lead`, `tear`, `wind`, `row`, `basses` and `tears` at the sense level; the past class of `read` is
  `read|read/Verb/Tense=Past+VerbForm=Part`, the fish class of `bass` has `bass|bass/Noun/Number=Sing#sea-bass` and the
  music class `bass|bass/Noun/Number=Sing#basso`.
- **Curated corrections of the lexicographer review** (`Heteronyms_Build_ReflectsTheLexicographerReview`): the rejected
  groups (`birr`, `recap`, `bourgeois`, `denier`, `emu`, `fleer`, `laver`, `punt`, `router`, `slough`, `whoop`) are
  absent; `aged`, `learned` and `blessed` are split by the senses that separate their sounds (the adjective senses
  `elderly`, `of-age`, `conditioned` and `blest`, not the whole adjective); the senses `printing-strip` of `lead`,
  `loom` of `tower`, `revitalize` of `recreate`, the noun senses of `close` and the verb senses of `incense` are in the
  classes the review gave them; and the noun `read`, the auxiliary `do` and the participle `leading` are added.
- **Notes of a group** (`Heteronyms_Build_NotesBelongToTheirGroup`): the heteronyms of a group whose rows carry a note
  (the stress-shift pair `transport`) have it, a group without a note has none, and the TSV repeats the note in its last
  cell.
- **Keys of the table are keys of the lexicon** (`Heteronyms_Build_EveryKeyIsAKeyOfTheLexicon`): every heteronym has
  two classes with keys, every key is a member of `WordLexicon.Keys()`, is spelled like its heteronym and is listed once.
- **The sense table follows the pronunciations** (`Heteronyms_SenseSplitLemmas_AreCurated`): every lemma that has a
  lemma and part of speech whose entries fall into two sound classes (and a form in the word table) is a group of the
  sense table, except the ten lemmas that the lexicographer review rejected, which must not be in it.
- **TSV** (`Heteronyms_RenderTsv_WritesOneRowForEachKey`, `Program_Heteronyms_WritesTheTable`): a header and one row of
  six cells (the last is the note, empty for most) for each key, LF line ends; the command needs `--output` and writes
  the table of the committed data.
- **Invalid curated table** (`Heteronyms_Build_InvalidCuratedTable_IsReported`): a missing file, an empty file (no
  header), an unknown level, a
  row without four or five cells, a pattern that is not one, a label that is not a sense, a group with one class, a
  group with two different notes, two classes that claim one analysis and a group that matches no spelling are each
  reported, all in one error.

#### corpus

- **Row format** (`Corpus_ReadSentences_ParsesRow`, `Corpus_ReadSentences_RejectsMalformedRow`): a row of lemma, sense
  id, part of speech and sentence is parsed; a row with another number of fields is rejected.
- **Part of speech agrees with the sense** (`Corpus_ReadSentences_PartOfSpeechDisagreeingWithSense_IsRejected`): a row
  whose part of speech differs from the one of its sense is rejected with an error that names the row.
- **Compound targets** (`Corpus_ReadSentences_SplitsCompoundTarget`): a compound target is split into its words.
- **Disjointness** (`WordSenseBuilder_Corpus_DirectoryAndDisjointness`,
  `WordSenseBuilder_Corpus_Leakage_TrainingSentenceInAnEvaluationPart_IsALeak`,
  `WordSenseBuilder_Corpus_Leakage_OnlyRealLeaksFail`,
  `WordSenseBuilder_Corpus_Leakage_SharedRunOfWords_IsInformational`): a sentence that a training part shares with an
  evaluation part (whatever its case, spacing and punctuation) is a leak that the report names; two training parts, a
  chooser part and its sense part, and an external set are not leaks; two evaluation parts and a corpus sentence of
  five words or more that is a dictionary example are; a shared run of eight words is informational.
- **All-words gold halves** (`WordSenseBuilder_Corpus_Leakage_AllWordsGold_IsAViewOfItsOpenSplit`,
  `..._AllWordsGoldSentenceInATrainingPart_IsALeak`, `..._AllWordsGoldSentenceOfTheOtherSplit_IsALeak`): a half of the
  all-words gold set and the split of the open corpus it is drawn from share their sentences and that is no leak; a gold
  sentence that a training part has too is a leak, and so is a gold sentence of the development half that the test split
  of the open corpus has.
- **Second all-words gold set** (`Gold2_Files_RoundTrip`, `WordSenseBuilder_Corpus_Leakage_OnlyRealLeaksFail`,
  `Gold2_ValidateDocuments_UsedMissingAndSharedDocuments_AreProblems`,
  `Gold2_CommittedSet_DocumentsAreDisjointFromTheRepository`): the sentences of the development and the test split of
  the second set are two parts of the check; they are views of no other part, so a sentence that either shares with
  any other evaluation part is a leak by the rule that two evaluation parts do not share a sentence; the document
  check reports a document that another part uses or has rejected (by id and by address), a token whose document is
  not listed and a document in both splits, and finds nothing in the committed set.
- **Committed corpus** (`WordSenseBuilder_Corpus_Disjointness_CoversSuiteEvaluationAndOewnExamples`): the anchors,
  chooser, held-out and real-text sentences, the suite, the shape probe set, the open corpus development and test
  splits, the two halves of
  the all-words gold set, the two splits of the second all-words gold set, the challenge set
  and the Open English WordNet examples of the repository share no sentence in a way that leaks.
- **Command** (`WordSenseBuilder_Program_CheckLeakage_ExitsWithOneOnALeak`): `check-leakage` prints the report and
  exits with code 1 when a part shares a sentence with an evaluation part, and 0 otherwise; it also exits with code 1
  and prints each document problem of the second set under its own heading (by inspection of the command; the
  document check itself is tested above).

#### Command line

- **No arguments or unknown command**: exit code 1 and the usage text listing `compile-image`, `import-moby`,
  `weights <modelId>`, `train-pos-head <modelId>`, `evaluate <modelId>`, `evaluate-senses`, `evaluate-allwords`,
  `allwords-sample`,
  `allwords-merge`, `gold2-power`, `gold2-sample`, `gold2-batches`, `gold2-merge`, `evaluate-gold2`, `compare-gold2`,
  `evaluate-running`, `compare-outcomes`, `evaluate-suite`, `evaluate-shapes`, `check-leakage`,
  `tiers`, `open-counts`, `evaluate-tagger`, `coverage`, `train-basic`, `tune-basic`, `label-senses`,
  `import-wn30-counts`, `appendix`, `keys` and `heteronyms`
  (`Program_NoArguments_PrintsUsage`, `Program_UnknownCommand_ReportsCommandAndUsage`,
  `Program_Usage_ListsTheSenseCommands`, `Program_Usage_ListsThePosHeadCommands`).
- **Removed command** (`Program_RemovedCommand_IsUnknown`): the old command names (`build`, `train`,
  `generate-appendix`, `validate-pos`) and the treebank commands (`ud-counts`, `ud-irregular`) are unknown commands and
  exit with code 1.
- **Option errors** (`Program_MalformedOptions_ReportUsage`): an option without a value, an option whose
  value is another option, and a value without a name exit with code 1 and the message "Expected '--name value'".
- **Number options** (`Program_MalformedNumberOption_ReportsOneLineError`): a `--top`, `--sample`, `--seed`,
  `--threshold` or `--max-error` value that is not a number (or an integer out of range) exits with code 1 and a
  one-line message that names the option and the value, without a stack trace.
- **Defaults**: without `--repository` and `--corpus` the paths default to the repository layout below the current
  directory. This is by inspection of the option parser; no dedicated test exercises it, because every test passes the
  paths explicitly.
