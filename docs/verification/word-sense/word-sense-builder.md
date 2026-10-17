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
- **Inflections are materialized for every dictionary word**: the written lexicon contains the plural, third person,
  past, -ing, comparative, superlative, adverb and derived forms of every dictionary word and trained lemma as
  lower-case keys with the senses of their lemma, with the spelling rules of English (`GeneratesForms`,
  `AppliesSpellingRules`), so the shipped reader needs no morphology.
- **Irregular forms replace the regular form** (`UsesIrregularForms`): `went`, `took` and `children` are keys and
  `goed`, `taked` and `childs` are not; an existing word is never replaced (`RespectsKnownWordsAndDisabledRules`).
- **Precedence of sources**: a lemma's own senses win over forms, dictionary words and generated forms, and the
  dictionary's other parts of speech of the same word follow them (`MergesDictionaryPartsOfSpeechAfterTrainedSenses`).
- **Every dictionary word is kept** (`Words_Committed_HoldsEveryDictionaryWord`): the committed lexicon holds every
  word of the committed dictionary with each of its parts of speech.
- **Output is deterministic**: two runs on the same corpus give byte-identical files.
- **Writer and reader agree**: every key and sense written by the writer is read back unchanged, and the reader rejects
  a tampered file (see the `WordLexicon` unit document).
- **Invalid corpus**: an inconsistent sense inventory exits with code 1 and an error message and writes no file.

#### coverage

- **Gutenberg boundaries**: only the text between the `*** START` and `*** END` lines is read (`Body_CutsAtMarkers`).
- **Classification**: known, apostrophe, name-like (capitalized at least four times as often as lower case) and other
  tokens are counted apart (`AddText_ClassifiesTokens`).
- **Directory and rendering**: every `.txt` file of a directory is read and the report lists the figures and the unknown
  words; a missing or empty directory is rejected (`ReadAndRender_ListsFigures`, `Read_BadDirectory_Throws`).
- **Command**: `coverage --text DIR` prints the report and `--output` writes it; the text option is required
  (`Program_Coverage_*`).
- **Regression on an authored sample** (`AuthoredSample_IsFullyKnown`): a small authored paragraph of inflected
  English (no licensed text) is fully known by the committed lexicon. The figure of the real-text sample is
  in the build notes, not asserted here.

#### import-moby

- **Every word kept in source order**: single lower-case words keep all their parts of speech in the order of the
  source, determiner, conjunction-only and interjection-only words included (`LexiconImporterTests`).
- **Rules and corrections**: the participle and gerund rule and the committed corrections are applied; a malformed
  correction is rejected.
- **Pinned input**: input whose SHA-256 differs from the pin is rejected, and the command without `--input` reports a
  usage error (`WordSenseBuilder_Program_ImportMoby_RequiresPinnedInput`).
- **Auxiliaries**: each word of `corpus/lexicon/auxiliaries.tsv` gains an `Auxiliary` sense after all its other senses
  (`Build_AppendsAuxiliarySenseLast`); a word missing from the table is an error (`Build_UnknownAuxiliary_Throws`); the
  file reader rejects malformed lines and repeats (`ReadAuxiliaries_ReadsWordsAndRejectsBadLines`), the letters cover
  all ten parts of speech (`Letters_CoverTheTenParts`), and a sense inventory that trains `auxiliary` is rejected.
- **Reader**: the dictionary reader keeps the order of the letters, accepts the determiner letter and rejects bad
  files.

#### Model table

- **Basic row matches the evaluation** (`WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation`): the accuracy
  numbers of the Basic row of `docs/build_notes/model-table.md` equal a fresh evaluation of the Basic model.

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

- **No arguments or unknown command**: exit code 1 and the usage text listing `words`, `import-moby`,
  `weights <modelId>`,
  `evaluate <modelId>`, `ud-counts`, `train-basic`, `tune-basic` and `appendix`.
- **Removed command** (`WordSenseBuilder_Program_RemovedCommand_IsUnknown`): the old command names (`build`, `train`,
  `generate-appendix`, `validate-pos`) are unknown commands and exit with code 1.
- **Option errors** (`WordSenseBuilder_Program_MalformedOptions_ReportUsage`): an option without a value, an option whose
  value is another option, and a value without a name exit with code 1 and the message "Expected '--name value'".
- **Defaults**: without `--repository` and `--corpus` the paths default to the repository layout below the current
  directory. This is by inspection of the option parser; no dedicated test exercises it, because every test passes the
  paths explicitly.
