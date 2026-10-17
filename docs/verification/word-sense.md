# System Verification Design

This document describes the system-level verification strategy for the core WordSense package,
`DemaConsulting.WordSense`.

## Verification Approach

The core system is verified in two ways. Tests in `test/DemaConsulting.WordSense.Tests` exercise the lexicon-only
classifier, the model abstraction and the two tables through the public API with the embedded lexicon and a fake model,
so they need no model file. Tests in `test/DemaConsulting.WordSense.MiniLM.Tests` (`WordSenseTests.cs`,
`ReadmeSampleTests.cs`, `UserGuideSampleTests.cs`) run the real int8 model and the embedded weights of the MiniLM
package against the core API, because the accuracy of the shipped classifier can only be measured with a real model.
Unit-level behavior is verified in the unit documents with substitutes (a fake model).

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Dependencies**: the core tests have none. The real-model tests need the Git LFS model files `model_int8.onnx` and
  `vocab.txt` under `model/all-MiniLM-L6-v2-int8/`; no network access. Tests that need the model are skipped, not
  failed, when the LFS content is absent.
- **Isolation**: each test creates its own classifier; no shared state.

## External Interface Simulation

Models are replaced by a fake `IWordSenseModel` with deterministic vectors. Downloads belong to the MiniLM system and
are verified there against a loopback `HttpListener`; no test reaches a public host.

## System-Level Test Scenarios

### WordSenseClassifier_Create_LexiconOnly_AnswersSingleSenseWord

`WordSenseClassifier.Create()` builds a classifier from the embedded lexicon, with no model and no weights, and a
single-sense word is answered with basis `Dictionary`.

### WordSenseClassifier_Create_LexiconOnly_ContextualWordAbstainsWithCandidates

With the same classifier, a contextual multi-sense word has no details, is abstained, has basis `Dictionary` and lists
every sense as a candidate.

### WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce

A text with a context word, a dictionary word and an unknown word calls the fake model once; this scenario shows the
model abstraction delivers one vector per target to the classifier.

### WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused

The embedded weights of the MiniLM package are accepted by the real model and by the embedded lexicon, and a weights
table that names another lexicon digest is refused with `WordSenseModelMismatchException`.

### WordSenseClassifier_Constructor_Mismatch_Throws

A model and a weights table that do not belong together (another model id, model digest or dimension, or another
lexicon) are refused at construction with `WordSenseModelMismatchException`.

### WordSenseWeightsTable_EnsureMatches_OtherLexicon_Throws

A weights table whose lexicon digest differs from the lexicon table it is checked against is refused.

### WordSenseWeightsTable_RoundTrip_PreservesAllValues and WordSenseWeightsTable_Read_AnyByteFlipped_Throws

A weights table serializes and reads back without loss, and any single flipped byte is rejected.

### WordSense_Classifier_EndToEnd_ClassifiesRecord

A classifier built on the real model classifies "The record was broken last year, so they will record
the new album." and must return noun for the first and verb for the second "record".

### WordSense_Classifier_EndToEnd_WordListApi

A classifier classifies the word list "The", "record", "was", "broken", "so", "they", "will", "record", "it".
The result array is parallel to the input and holds the word as given; the first "record" is a noun and the
second a verb with lemma `record`, the non-ambiguous word "The" has no details and is not abstained, the
first "record" has a positive margin and a runner-up, and its details are one of the candidates listed for
`record`, and the analyses of both "record" words have basis `Context`, with the candidates of the first being the
list `GetCandidates("record")` returns.

### WordSense_Classifier_CommonDictionaryWords_AnsweredFromLexicon

Classifies the words `The`, `hunter`, `quickly`, `climbed`, `a`, `tall`, `mountain` and `.` with a classifier built on
the real model and the embedded lexicon. The word `hunter` is answered as a noun with basis `Dictionary`, `quickly`
and `mountain` have basis `Dictionary`, and the capitalized `The` is unknown with basis `None`.

### WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutModel

A word whose entry has one sense is answered from the dictionary with a fake model that is never called.

### WordSense_EmbeddedTable_PartsOfSpeechAndHeteronymsAreConsistent

Every sense of every context lemma of the embedded lexicon has one of the four context parts of speech (noun, verb,
adjective, adverb); a lemma is a heteronym exactly when its senses have at least two distinct meanings, and a
candidate has a pronunciation exactly when its lemma is a heteronym.

### WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma

Every braced target word of every anchor, holdout and real-text row (lowercased, curly apostrophe normalized, with
the possessive fallback of the classifier) must resolve through the embedded forms table to the lemma of its row.
The only exception is the plural `rows` of the lemma `row`, because `rows` is itself a context lemma. The test
fails with the list of missing forms, which are then added to `corpus/forms/speech.tsv` and the tables rebuilt.

### WordSenseTable_LoadEmbedded_RoundTrips

The embedded lexicon loads and serializes back to the same decoded content.

### WordSense_Classifier_Holdout_MeetsAccuracyGate

Every fifth held-out sentence of every corpus file is classified; the answered accuracy must be at
least 96 % with at least 85 % coverage over more than 400 sentences. Rows whose target is an inflected form are
classified through the forms table, so every such form must be listed there; an unlisted form would count as
unanswered.

### WordSense_Classifier_Holdout_MeetsPartOfSpeechGate

Holdout rows (every fifth row, as in the accuracy gate) are classified and a result counts as correct when the
predicted part of speech equals that of the gold sense; the answered accuracy must be at least 97 % with at least
85 % coverage over more than 400 rows. Only the asserted thresholds are quoted here; the measured values are in
the build notes (`docs/build_notes/`). The split into two packages does not change these results.

### WordSense_Classifier_Holdout_MeetsPronunciationGate

Holdout rows of heteronym lemmas (those whose gold sense has a meaning) are classified and a result counts as
correct when the predicted pronunciation meaning equals the gold meaning (the senses of `bow`, `lead`, `tear`,
`closer` and `polish` can share a meaning, so this gate can exceed the sense-level gate); the answered accuracy
must be at least 96 % with at least 85 % coverage over more than 200 rows.

### WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp and WordSenseClassifier_ClassifyAsync_LowMargin_Abstains

With a fake model, a context word reports its margin and runner-up, and a word below the scaled threshold abstains
with its margin still reported.

### WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone

A word that does not resolve has basis `None`, no candidates, no abstention and zero margins.

### WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic

The trainer separates a small data set and gives identical weights on a repeat run (builder verification).

### WordSense_Readme_CodeBlock_MatchesCompiledSample

For the README sample (`readme`), extracts the code block marked `<!-- sample:readme -->` from `README.md`
and the region between the `<sample:readme>` markers of the compiled README sample, and requires them to be equal
after normalizing line endings and trailing white space. Because the region is compiled code, the README sample is
guaranteed to build against the current API.

### WordSense_Readme_Output_MatchesReadme

Runs the compiled README sample against the committed model (skipped when the model files are missing or are Git
LFS pointers) and requires the lines it writes to equal the output block marked `<!-- output:readme -->` in
`README.md`. A change to the model, tables or abstention shows up as a failing test and the documented output is
updated deliberately.

### WordSense_Readme_SampleMarkers_AreAllChecked

Requires the set of sample markers in `README.md` to equal the set of checked samples (only `readme`).

### WordSense_UserGuide_CodeBlock_MatchesCompiledSample

For each user guide sample, extracts the code block marked `<!-- sample:name -->` from
`docs/user_guide/introduction.md` and the region between the `<sample:name>` markers of the compiled user guide
samples, and requires them to be equal after normalizing line endings and trailing white space. The test is a theory
driven by the same list of sample names as the marker check.

### WordSense_UserGuide_SampleMarkers_AreAllChecked

Requires the set of sample markers in the user guide to equal the set of checked samples, so a new code block cannot
be added without a compiled copy.

### WordSense_UserGuide_Samples_RunAgainstModel

Runs the samples that need a classifier against the committed model (skipped when the model files are missing or are
Git LFS pointers) and requires that they complete without an error and give the documented results for the sample
words.

### WordSense_XmlDocExamples_Compile

Extracts every `<code>` example from the shipped XML documentation file (`DemaConsulting.WordSense.xml`) and
compiles it against the real public API, so a documented example cannot drift from the API. The scenario is
also linked by the quality requirements.

### Platform Support

`WordSense_Classifier_EndToEnd_ClassifiesRecord` is run on each CI operating system (Windows, Linux, macOS) and
under each target framework (net8.0, net9.0, net10.0). The six `WordSense-Platform-*` requirements are each
linked to this test with a source filter (`windows@`, `ubuntu@`, `macos@`, `net8.0@`, `net9.0@`, `net10.0@`):
`WordSense-Platform-Windows`, `WordSense-Platform-Linux`, `WordSense-Platform-MacOS`, `WordSense-Platform-Net8`,
`WordSense-Platform-Net9` and `WordSense-Platform-Net10`. A requirement is satisfied only by a passing result from
its own platform or framework.

## Requirements Coverage

- `WordSense-Lib-Classify`: **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**,
  **WordSense_Classifier_EndToEnd_ClassifiesRecord** and **WordSense_Classifier_EndToEnd_WordListApi**.
- `WordSense-Lib-Dictionary`: **WordSense_Classifier_CommonDictionaryWords_AnsweredFromLexicon** and
  **WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutModel**.
- `WordSense-Lib-LexiconOnly`: **WordSenseClassifier_Create_LexiconOnly_AnswersSingleSenseWord** and
  **WordSenseClassifier_Create_LexiconOnly_ContextualWordAbstainsWithCandidates**.
- `WordSense-Lib-Basis`: **WordSense_Classifier_CommonDictionaryWords_AnsweredFromLexicon** and
  **WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone**.
- `WordSense-Lib-PartOfSpeech`: **WordSense_Classifier_Holdout_MeetsPartOfSpeechGate** and
  **WordSense_EmbeddedTable_PartsOfSpeechAndHeteronymsAreConsistent**.
- `WordSense-Lib-Pronunciation`: **WordSense_Classifier_Holdout_MeetsPronunciationGate** and
  **WordSense_EmbeddedTable_PartsOfSpeechAndHeteronymsAreConsistent**.
- `WordSense-Lib-Analysis`: **WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp** and
  **WordSenseClassifier_ClassifyAsync_LowMargin_Abstains**.
- `WordSense-Lib-Accuracy`: **WordSense_Classifier_Holdout_MeetsAccuracyGate** and
  **WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma**.
- `WordSense-Lib-Abstain`: **WordSenseClassifier_ClassifyAsync_LowMargin_Abstains**.
- `WordSense-Lib-Embedded`: **WordSenseTable_LoadEmbedded_RoundTrips**.
- `WordSense-Lib-ModelAbstraction`: **WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce** and
  **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**.
- `WordSense-Lib-ModelMismatch`: **WordSenseClassifier_Constructor_Mismatch_Throws** and
  **WordSenseWeightsTable_EnsureMatches_OtherLexicon_Throws**.
- `WordSense-Lib-PerModelWeights`: **WordSenseWeightsTable_RoundTrip_PreservesAllValues** and
  **WordSenseWeightsTable_Read_AnyByteFlipped_Throws**.
- `WordSense-Lib-Reproducible`: **WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic**.
- `WordSense-Lib-Documentation`: the README and user guide scenarios
  (**WordSense_Readme_CodeBlock_MatchesCompiledSample**, **WordSense_Readme_Output_MatchesReadme**,
  **WordSense_Readme_SampleMarkers_AreAllChecked**, **WordSense_UserGuide_CodeBlock_MatchesCompiledSample**,
  **WordSense_UserGuide_SampleMarkers_AreAllChecked** and **WordSense_UserGuide_Samples_RunAgainstModel**).
- `WordSense-Platform-*` (Windows, Linux, MacOS, Net8, Net9, Net10): source-filtered runs of
  **WordSense_Classifier_EndToEnd_ClassifiesRecord** (see Platform Support).

The requirements that moved to the MiniLM system (the encoder, the model download, the mirror, the integrity of model
files and the performance) are covered by the MiniLM system verification document. The unit-level requirements that
the system requirements list as children are verified in the unit documents; `WordSense-Lib-Documentation` is also
verified by the appendix scenarios of the builder verification document (`WordSense-Builder-Appendix`), and
`WordSense-Lib-Dictionary` by the table, classifier and builder lexicon scenarios of the unit verification documents.

## Acceptance Criteria

All scenarios pass on every platform and target framework of the CI matrix.
