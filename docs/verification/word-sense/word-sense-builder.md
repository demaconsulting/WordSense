## WordSenseBuilder Verification

This document describes the verification of the `WordSenseBuilder` unit, the non-shipped build-time tool in
`tools/WordSense.Builder` that generates the word lexicon, the model weights and the generated user guide appendix.

### Verification Approach

Tests in `test/WordSense.Builder.Tests` call the command entry point (`Program.RunAsync`) with explicit output writers
and the internal serialization types with synthetic data in temporary directories: `ProgramTests.cs` covers the commands
and their usage and data errors, `WordsBlobWriterTests.cs` and `WeightsBlobWriterTests.cs` cover the lexicon and weights
writers against the readers of the shipped packages, `UdCountsTests.cs` covers the treebank counts of the Basic model,
and `BasicTrainerTests.cs` covers its trainer, table writer, model data summary and hyper-parameters.
Commands that need a real encoder (`weights` and `evaluate` for the encoder models) are verified through the data they
produce: the weights embedded in the model packages are loaded and matched in the MiniLM and BertBaseCased tests, and
the accuracy figures are recorded in the build notes, not asserted here.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. A temporary directory per test and small synthetic corpus files;
no model files and no network access for the scenarios below.

### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

### Test Scenarios

#### words

- **Words command writes the lexicon**: `words --corpus DIR --output FILE` on a small synthetic corpus writes the
  lexicon file and reports the number of words; the file is read back by `WordsBlob` into the expected keys and senses.
- **Inflections are materialized**: the written lexicon contains the inflected forms of the lemmas (plural, past and
  gerund forms) as lower-case keys with the senses of their lemma, so the shipped reader needs no morphology.
- **Precedence of sources**: a lemma's own senses win over forms, dictionary words and generated forms.
- **Output is deterministic**: two runs on the same corpus give byte-identical files.
- **Writer and reader agree**: every key and sense written by the writer is read back unchanged, and the reader rejects
  a tampered file (see the `WordLexicon` unit document).
- **Invalid corpus**: an inconsistent sense inventory exits with code 1 and an error message and writes no file.

#### weights

- **Model name is required and checked**: `weights` without a model id, and with an unknown id, exit with code 1 and a
  message that lists the known models (`minilm`, `bert-base-cased`); the id may be given as the second argument or with
  `--model`.
- **Weights file round trip**: the weights writer produces a blob that `WeightsBlob` reads back with the same model id,
  dimension, mean vector, senses and weights.
- **Training is deterministic**: the logistic trainer separates a small data set and gives identical weights on a repeat
  run.
- **Weights match the lexicon**: weights trained for the committed corpus are loaded by each model package with zero
  dropped lemmas (verified in the MiniLM and BertBaseCased tests).

#### evaluate

- **Model name is required and checked**: `evaluate` accepts `minilm`, `bert-base-cased` and `basic` and rejects any
  other id with exit code 1.
- **Report is rendered and written**: the result is written to the standard output, and to the file given with `--output`.
- **Basic evaluation needs no model**: `evaluate basic` runs on a synthetic corpus without model files, counts the
  holdout rows and prints the accuracy and coverage measurements together with the calibration buckets, the confidence
  thresholds, the table size, the latency and the fixed comparison table
  (`WordSenseBuilder_Program_Evaluate_BasicModel_PrintsMeasurements`,
  `WordSenseBuilder_Evaluator_RunAsync_BasicModel_CountsHoldoutRows`,
  `WordSenseBuilder_EvaluationResult_Render_FormatsSetsAndMeasurements`).

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
- **Cue table** (`WordSenseBuilder_BasicTrainer_Train_CueTablePrefersSeenSense`): the cue table of a lemma prefers the
  sense its context words occurred with.
- **Writer** (`WordSenseBuilder_BasicTablesWriter_Write_IsDeterministicAndReadsBack`): equal inputs give identical
  table bytes that `BasicTables` reads back, and a changed hyper-parameter changes the header.
- **Summary** (`WordSenseBuilder_BasicModelData_Summary_NamesSizes`): the summary names the sizes of the trained data.
- **Hyper-parameters** (`WordSenseBuilder_BasicHyperparameters_Default_IsConsistent`): the defaults carry the values
  the runtime reads and the tuning grid includes them.
- **Statistical model has no weights command** (`WordSenseBuilder_Program_Weights_BasicModel_PointsToTrainBasic`):
  `weights basic` exits with code 1 and the message names `train-basic`.
- **Tuning reads no evaluation data**: `tune-basic` uses only the treebank development file and the anchors, and
  writes no file; this is a property of the implementation that review verifies, because the command needs the real
  treebank.

#### appendix

- **Appendix lists lemmas senses and forms**: a synthetic corpus gives an appendix with the parts-of-speech overview, the
  lemmas in alphabetical order, the senses with part of speech, meaning and respelling, and an example sentence for
  each sense; markdown characters are escaped, lines are wrapped, line endings are LF and the output repeats byte for
  byte.
- **Incomplete input is rejected**: a missing forms or anchors directory, a sense without an anchor sentence, a form
  label that is not a sense and an empty inventory are each rejected with an error that names the problem.
- **Committed appendix is current**: the appendix regenerated from the committed corpus equals
  `docs/user_guide/appendix-senses.md` (line endings normalized), so the committed appendix cannot go stale; `lint.ps1`
  performs the same comparison.

#### Command line

- **No arguments or unknown command**: exit code 1 and the usage text listing `words`, `weights <modelId>`,
  `evaluate <modelId>`, `ud-counts`, `train-basic`, `tune-basic` and `appendix`.
- **Removed command** (`WordSenseBuilder_Program_RemovedCommand_IsUnknown`): the old command names (`build`, `train`,
  `generate-appendix`, `validate-pos`) are unknown commands and exit with code 1.
- **Option errors** (`WordSenseBuilder_Program_MalformedOptions_ReportUsage`): an option without a value, an option whose
  value is another option, and a value without a name exit with code 1 and the message "Expected '--name value'".
- **Defaults**: without `--repository` and `--corpus` the paths default to the repository layout below the current
  directory. This is by inspection of the option parser; no dedicated test exercises it, because every test passes the
  paths explicitly.
