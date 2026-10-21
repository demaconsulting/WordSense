# System Verification Design

This document describes the system-level verification strategy for WordSense, the set of four layered NuGet packages
`DemaConsulting.WordSense` (core, with the built-in Basic model), `DemaConsulting.WordSense.Onnx`,
`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`, together with the non-shipped builder
tool that generates their embedded data.

## Verification Approach

The system is verified in layers that follow the package layers. Each package has its own test project, and each
test project verifies the subsystem of that package through its public API and its units through the internal API
(made visible to the test project by `InternalsVisibleTo`):

- `test/DemaConsulting.WordSense.Tests` verifies the core package with the embedded word lexicon only: the lexicon,
  the catalog, the contracts and the built-in statistical model (the Basic model, against the embedded word lexicon,
  toy statistical tables and the embedded table). It needs no model, no model file and no network access.
- `test/DemaConsulting.WordSense.Onnx.Tests` verifies the shared encoder implementation with small substitutes:
  hand-built vocabularies, fake weights and a loopback `HttpListener` for downloads. Its model-level tests
  (`OnnxWordSenseModelTests`, `OnnxClassifierTests`, `OnnxEncoderTests`) use test doubles or the committed int8 model.
- `test/DemaConsulting.WordSense.MiniLM.Tests` and `test/DemaConsulting.WordSense.BertBaseCased.Tests` verify each
  model package against the real int8 model and the weights embedded in the package. The MiniLM tests also compile the
  README sample against the public API and run it against the Basic, MiniLM and BertBaseCased models.
- `test/WordSense.Builder.Tests` verifies the builder commands with synthetic data in temporary directories, and the
  word data subsystem (`Data/`) with a small OEWN slice and the committed `data/` folder.

The system-level evidence is the combination of these projects: the end-to-end classification scenarios of the model
packages, the dependency structure of the packages (verified by inspecting the project references) and the platform
runs of the CI matrix. Unit-level behavior is verified in the unit documents and subsystem-level behavior in the
subsystem documents, with substitutes where a real model is not needed.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Platforms**: Windows, Linux and macOS in the CI matrix
- **Dependencies**: the core (including Basic) and tokenizer tests have none. The real-model tests need the Git LFS
  files under
  `model/all-MiniLM-L6-v2-int8/` (`model_int8.onnx`, `vocab.txt`) and `model/bert-base-cased-int8/`
  (`model_int8.onnx`, `vocab.txt`, `config.json`, `tokenizer.json`); no network access. Tests that need a model are
  skipped, not failed, when the LFS content is absent.
- **Isolation**: each test creates its own model, classifier and store directory; no shared state.

## Acceptance Criteria

The system passes verification when every test of every test project passes on every platform and target framework of
the CI matrix, the real-model end-to-end scenarios below give the documented parts of speech and levels, and the
project references show the layering. Requirements-to-test coverage is tracked by the ReqStream trace matrix.

## Test Scenarios

### End-to-End Classification with a Real Model

The MiniLM and BertBaseCased test projects each create the model from the committed model directory, call
`EnsureModelAsync` and `ClassifyAsync`, and classify the words of "They will record the record." The first `record`
must be a verb and the second a noun (`WordSense_Classifier_EndToEnd_ClassifiesRecord`). The result of each word
carries `IsKnown`, its analyses, the selected analysis, its level, the group and sense ids that the level allows and a
confidence (`WordSense_Classifier_EndToEnd_ResultCarriesAnalysisLevelAndSense`). This scenario is run on each CI
operating system
(Windows, Linux, macOS) and each target framework (net8.0, net9.0, net10.0); the platform requirements are each linked
to it with a source filter (`windows@`, `ubuntu@`, `macos@`, `net8.0@`, `net9.0@`, `net10.0@`), so a requirement is
satisfied only by a passing result from its own platform or framework.

### Answer Level and Fallback with a Real Model

A trained lemma is classified in a clear sentence and in an unclear one. The clear one is answered at the finest level
that its thresholds allow, and the result states that level; the unclear one is answered at the group level when the
lemma has a group for its best sense and the group threshold is met, and otherwise at the part of speech level,
because a group exists only where it was curated and a level whose threshold is not met is never answered
(`WordSense_Classifier_EndToEnd_AnswersAtTheFinestConfidentLevel`). A word with several senses that the model was not
trained for is answered at the part of speech level by the statistical analysis of the Basic model of the composition,
with no sense id and no group id, and never at level none because it is known
(`WordSense_Classifier_EndToEnd_UntrainedWord_AnswersPartOfSpeech`).

### Analyses Are Ranked and Sum to One

Each model classifies a word with several analyses (for example `record`) in a sentence. The analyses of the result
are sorted by the probability of their lemma and part of speech with the largest first (and inside one lemma and part of
speech by their own probability), their probabilities sum to one within a small tolerance, the first
analysis equals the selected `Details`, and an unknown word has none
(`WordSense_Classifier_EndToEnd_AnalysesAreRankedAndSumToOne`, run for MiniLM and BertBaseCased, whose composition
answers with the Basic model in this increment).

### Senses Are Ranked and Sum to One with a Real Model

A trained lemma with several senses is classified in a context that does not decide between two of its senses. Whatever
level the result is answered at, the senses of the selected analysis are the senses of its lemma and part of speech,
ranked by their probability given the analysis with the most probable first, and their probabilities sum to one within a
small tolerance, so that a consumer can sum `analysis.Probability * sense.Probability` per category of its own
(`WordSense_Classifier_EndToEnd_SensesAreRankedAndSumToOne`, run for MiniLM and BertBaseCased once the encoder scores
fine senses; the Basic model lists senses only for an analysis with exactly one sense, as the Basic unit documents
shows).

### Composition of Two Models

The core test project composes two test-double models with `FallbackWordSenseModel` and checks that a word the primary
model scored is answered by the primary result and every other word by the complete fallback result
(`WordSenseContracts_FallbackWordSenseModel_PrimaryScoredSenses_UsesPrimary`,
`WordSenseContracts_FallbackWordSenseModel_PrimaryChoseOnlyAnalysis_UsesFallback`). The MiniLM and BertBaseCased test
projects classify an ambiguous word that the encoder does not score and find the result of the Basic model
(`MiniLMWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`,
`BertBaseCasedWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`).

### Sense Catalog Is Consistent

The core test project walks the embedded catalog: every sense id is unique and has at most one OEWN sense key, a project
sense has no key, a sense found by its id equals the sense found by its key, every sense is in at most one group of
its own lemma and part of speech, every group lists its senses, every group id carries the group marker
(`lemma#group:label`) and never equals a sense id, and the version of the inventory is a stable string of lower-case
hexadecimal digits
(`WordSense_Catalog_SenseIds_AreUniqueAndLinkedToOneKey`, `WordSense_Catalog_ProjectSenses_HaveNoKey`,
`WordSense_Catalog_LookupByIdAndByKey_FindTheSameSense`, `WordSense_Catalog_EverySense_BelongsToAtMostOneGroup`,
`WordSense_Catalog_Group_ListsItsSenses`, `WordLexicon_Groups_EveryGroupId_HasGroupMarkerAndNeverEqualsASenseId`,
`WordLexicon_Version_IsStableLowerCaseHex`).

### Lexicon-Only Classification with the Basic Model

The core test project classifies a short word list through `BasicWordSenseModel`, with no model files: a word whose
lemma and part of speech have one sense is answered at the sense level with confidence 1, a word without a lexicon
entry is unknown at level none with confidence 0 and no analysis, and a word with several analyses lists every analysis
with its probability and is answered at the part of speech level with the probability of its selected analysis. Repeated
calls give identical results, and the embedded statistics table loads on first use.

### Core Package Has No ONNX Dependency

The core package must not reference ONNX Runtime or any model package. This is verified by inspecting the project
references of the core project: its only references are the framework and build-time analyzers (the Basic model is part
of it). The Onnx package references only the core package and ONNX Runtime
(`OnnxWordSense_Assembly_ReferencesOnlyCore`); the MiniLM and BertBaseCased packages reference the core and Onnx
packages (`MiniLMWordSenseModel_Assembly_ReferencesCoreAndOnnx`,
`BertBaseCasedWordSenseModel_Assembly_ReferencesCoreAndOnnx`).

### Packages Share One Contract

A model of each package (Basic in the core package, MiniLM, BertBaseCased) is used through `IWordSenseModel`
only. For a word whose lemma and part of speech have a single sense every package gives the same answer, the sense at
level sense with confidence 1 and one analysis and one sense with probability 1 (this includes the determiners such as
`the`, in any case); for an unknown word every
package gives the word, not known, no analyses, no details, level none and confidence 0.

### Embedded Data Is Consistent

The embedded word lexicon of the core package loads, and the weights embedded in each model package load, match the
lexicon and its sense catalog and drop no lemma (see the MiniLM and BertBaseCased subsystem documents). A weights blob
for another model id, dimension or sense inventory is refused when loaded.

### Documentation Samples Compile and Run

The README sample is extracted by marker from `README.md` and compared with the compiled copies in the test projects
after normalizing line
endings and trailing white space. The set of markers must equal the set of checked samples. The sample runs against
each model (a stand-in model class wraps Basic, or the committed MiniLM or BertBaseCased model) and gives the output
documented for it. The word, part of speech, level and sense id of every output line must match the README exactly; the
confidence is compared numerically, within 0.01 for Basic (deterministic managed code) and within 0.10 for the encoder
models, whose int8 inference differs slightly between CPUs and operating systems. The README is the single source of
truth that the tests parse. The two commented alternative lines are checked as text and their model classes are created.
Tests that need model files are skipped when the files are absent. The user guide samples are reviewed with the guide
and are not compiled. The XML documentation examples of every package are
extracted from the shipped XML files and compiled against the public API (the `XmlDocExampleTests` of each test
project).

### Published Evaluation and Suite

The README model table is parsed and compared with the committed evaluation figures, so a published number that no
longer matches the measurement fails the build (`WordSense_Readme_ModelTable_MatchesPublishedEvaluation`). The reviewed
suite sentences carry the expected sense, group or part of speech and the expectation of each model (pass or known
miss); the Basic, MiniLM and BertBaseCased test projects run every row against their model and fail when an expected
pass no longer passes (`BasicSuite_ExpectedPasses_StillPass`, `MiniLMSuite_ExpectedPasses_StillPass`,
`BertBaseCasedSuite_ExpectedPasses_StillPass`); the encoder tests are skipped when the model files are absent. The
independent evaluations (SemCor and running-text part of speech) are verified in the builder documents: their figures
come from downloaded texts that are never committed.

### Data

The committed data is validated: every sense has one unique label, the groups partition the senses of their lemmas,
every keyed sense has a frequency and the project senses have no key
(`WordSenseBuilder_DataEndToEnd_CommittedData_HasSenseLabelsAndGroups`).

### Builder Commands

The builder verification document describes the commands `compile-image`, `import-moby`, `weights`, `evaluate`,
`evaluate-senses`, `evaluate-running`, `open-counts`, `evaluate-tagger`, `coverage`, `train-basic`, `tune-basic`,
`label-senses`, `import-wn30-counts`, `tiers` and `appendix`. Together they regenerate the data that is committed in the
packages, and the committed appendix of the user guide is compared with a regenerated one.
