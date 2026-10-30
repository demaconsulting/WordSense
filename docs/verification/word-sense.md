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
  skipped, not failed, when the LFS content is absent, so a local run without Git LFS stays convenient.
- **CI skip gate**: the CI acceptance run must not skip a model test. The build job first checks with
  `git lfs ls-files` that every tracked model file holds real content (not a pointer), and runs the tests with the
  environment variable `WORDSENSE_REQUIRE_MODELS=true`, which makes the shared helper `ModelFileCheck` fail a model
  test instead of skipping it (a missing, pointer, wrong-size or missing-folder condition all fail, with the reason
  and the name of the variable). The helper rules are tested in `ModelFileCheckTests` (the variable values, the failure
  with the variable and the skip without it, with temporary folders instead of the real model files).
- **Isolation**: each test creates its own model, classifier and store directory; no shared state.

## Acceptance Criteria

The system passes verification when every test of every test project passes on every platform and target framework of
the CI matrix, the real-model end-to-end scenarios below give the documented parts of speech and levels, and the
project references show the layering. Requirements-to-test coverage is tracked by the ReqStream trace matrix.

## Test Scenarios

### End-to-End Classification with a Real Model

The MiniLM and BertBaseCased test projects each create the model from the committed model directory, call the optional
`EnsureModelAsync` of the concrete model and `ClassifyAsync`, and classify the words of "They will record the record."
The first `record`
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
fine senses; the Basic model lists the only sense of an analysis with exactly one sense and, for an analysis with
several, its senses
ranked by the usage prior, as the Basic unit documents
shows).

### Untrained Lemmas Are Ranked by the Usage Prior

A word whose lemma the encoder has no weights for (`garden`) and whose lemma and part of speech have two or more senses
lists, in the selected analysis, all its senses ranked by the usage prior of the Basic model of the composition: the
list equals the ranking of the WordNet 3.0 tag counts with add-k smoothing, has at least two senses and is marked as
ranked by usage (`MiniLM_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage` and
`BertBaseCased_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`; system requirement
`WordSense-Lib-Candidates-UsagePrior`). The ranking itself, its formula, order and ties, and its validation are verified
in the core test project (`BasicClassifierUsagePriorTests.cs`, `UsagePriorTests.cs`, see the Basic classifier document).
The WordNet 3.0 counts come from the core image, so the scenario needs no further file.

### The Usage Ranking Never Answers

The same result is answered at the part-of-speech level with no sense id and no group id: the level, the confidence, the
selected analysis and the probability of every analysis are those of a classifier without the ranking (system requirement
`WordSense-Lib-Candidates-UsagePrior-NoAnswer`; the same two tests assert the level and the ids, and the core tests
`BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheAnswer`,
`BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheSelectedAnalysis` and
`BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheAnalysisProbabilities` compare it with a classifier without the
prior). A word with no sense listed, such as a word whose lemma has no sense in the catalog, lists none.

### The Key Carries the Label of the Top-Ranked Sense

The key of the result of a polysemous word ends with the label of the first ranked sense of the selected analysis, and
is a member of `WordLexicon.Keys()` (system requirement `WordSense-Lib-Candidates-UsagePrior-Key`;
`MiniLM_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`,
`BertBaseCased_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`,
`BasicClassifier_ClassifyAsync_PolysemousWord_KeyCarriesTheLabelOfTheTopRankedSense` and
`BasicClassifier_ClassifyAsync_PolysemousWord_KeyIsOneOfTheEnumeratedKeys`).

### The Usage Prior Is Measured Independently

The usage prior is measured on the all-words gold set (`corpus/allwords`, tokens of untrained polysemous lemmas of the
development and test halves of the open corpus, labeled blind by two independent labelers), never on SemCor, which the
WordNet 3.0 counts include and which is therefore circular for it. `evaluate-allwords` reports for each model and each
half the share answered at each level, the precision of the sense answers, the fine top-1 of the selected analysis and the
baselines (first sense, most frequent sense, random). The figures are in the accuracy guide
(`docs/user_guide/accuracy.md`, "The Usage Prior"): on both halves the fine top-1 of MiniLM and BertBaseCased is above the
tag-count baseline that is told the gold lemma and part of speech, and that of Basic is below it, and the log loss of the
smoothing constant is lowest at 1 on the development half. The commands and their units are verified in the builder test
project
(`WordSenseBuilder_Program_EvaluateAllWords_ReportsTheModelOnEachHalf`,
`WordSenseBuilder_AllWords_Measure_FineTop1CountsTheFirstRankedSenseOfTheSelectedAnalysis`,
`WordSenseBuilder_AllWords_Baselines_TagCountIsTheHighestCountWithInventoryOrderOnATie`; system requirement
`WordSense-Lib-Measured`); the figures themselves are regenerated by the builder and are not asserted by a test.

### A Usage Ranking Is Not Taken for a Score

A composition must not treat the context-free usage ranking of the Basic model as senses scored in context. Each
`AnalysisCandidate` says which it lists (`SensesRankedByUsage`), the default of a candidate is scored in context, and the
`FallbackWordSenseModel` uses a primary result only when its selected analysis lists scored senses or its level is group
or sense (system requirements `WordSense-Lib-Candidates-UsagePrior-Kind` and `WordSense-Lib-Fallback-UsageRanking`). The
core test project uses the real Basic result for an ambiguous polysemous word (`record`) as the primary result against a
fallback that disagrees with its slots: the fallback result answers the word at every primary weight, and a composite of
the Basic model with itself returns the Basic answers unchanged
(`WordSenseContracts_FallbackWordSenseModel_BasicAsPrimary_UsageRankedSenses_AreNotRefinedOrReconciled`,
`WordSenseContracts_FallbackWordSenseModel_BasicWithBasic_ReturnsTheBasicAnswerAsItIs`); the same analyses declared as
scored are used and reconciled (`WordSenseContracts_FallbackWordSenseModel_PrimaryWithContextScoredSenses_StillReconciled`),
and a reconciled result keeps the kind of every analysis
(`WordSenseContracts_FallbackWordSenseModel_Reconcile_KeepsTheRankingKindOfEveryAnalysis`). The shipped encoder
compositions are unaffected because an encoder lists no senses for an untrained lemma: the committed evaluation outcomes
of MiniLM, BertBaseCased and Basic and the running-text report do not depend on the ranking, and the
end-to-end tests assert that the trained lemma `back` lists scored senses and the untrained `garden` lists a ranking
marked by usage (`WordSense_Classifier_EndToEnd_SensesAreRankedAndSumToOne`,
`MiniLM_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`).

### Tagger Clause State and Possessive Expansion

The verb-in-clause state of the tagger and the expansion of a possessive are internal to the Basic model and are verified
at the unit level (the `PosTagger` and `BasicClassifier` documents, requirements `WordSense-Core-BasicClassifier-Possessive-*`
and `WordSense-Core-BasicClassifier-NonFinite-*`); at the system level they are covered by the published running-text
accuracy and the reviewed suite described below.

### Composition of Two Models

The core test project composes two test-double models with `FallbackWordSenseModel` and checks that a word the primary
model scored is answered by the primary result and every other word by the complete fallback result
(`WordSenseContracts_FallbackWordSenseModel_PrimaryScoredSenses_UsesPrimary`,
`WordSenseContracts_FallbackWordSenseModel_PrimaryChoseOnlyAnalysis_UsesFallback`). The MiniLM and BertBaseCased test
projects classify an ambiguous word that the encoder does not score and find the result of the Basic model
(`MiniLMWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`,
`BertBaseCasedWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`). The composition is itself an
`IDownloadableWordSenseModel` and forwards the optional ensure only to the members that have the capability (the encoder
model, not the Basic model), starting every capable member before any failure is reported and reporting the first
failure unchanged
(`WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_OnlyCapableMemberEnsured`,
`WordSenseContracts_FallbackWordSenseModel_EnsureModelAsync_BothFail_ReportsAfterBothRan`).

### Encoder Models Inherit the Tense Split of Basic

The encoder cannot tell the variants of a slot apart (the past and the infinitive of `put`, the past and the present of
`read`): it gives them the same probability, so before the tense model the lexicon order decided the tense. The composed
models (MiniLM and BertBaseCased, each with Basic as the fallback) give those tied variants the proportions that Basic
gives them from the context, and the probability of the slot, the level, the confidence, the group and the sense of the
encoder result do not change. The scenario runs on both packages with the committed model files
(`MiniLM_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext` and
`BertBaseCased_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext`, theories): `I will read that tomorrow` and
`Please put the books on the shelf` are answered with a non-past analysis, `I have read that already` and `She had put
the keys in her bag before she left` with a past one, the probabilities of the word sum to one, and the composite
equals the encoder model on its own (`Encoder.ClassifyAsync`) in the level, the confidence, the group id, the sense id
and the probability of the slot of the verb where the encoder scored senses for the verb (`read`), and the complete
result of Basic where it did not (`put`). The re-split rule, with its invariants (an exact tie only, the proportions
of the fallback, the slot total, the level, the confidence and the ids kept), is verified in the core test project
(`WordSenseContracts_FallbackWordSenseModel_TiedVariantsOfASlot_TakeTheProportionsOfTheFallback`,
`WordSenseContracts_FallbackWordSenseModel_ManyInputs_KeepSlotTotalsLevelAndConfidence`), and the split by Basic itself
in `BasicWordSenseModel_ClassifyAsync_SameSpellingVerb_TenseFollowsTheContext`.

### Model Failures Are Typed

A failure of the model files or of the download settings is reported by every model package with typed exceptions that
derive from the abstract `WordSenseException` and keep the original exception as inner exception (system requirement
`WordSense-Lib-Failures`). A model directory that does not exist is `ModelUnavailableException` through the public
MiniLM model (`MiniLMWordSenseModel_EnsureModelAsync_MissingDirectory_ThrowsModelUnavailable`); a file of a supplied
directory with the wrong content is `ModelIntegrityException`
(`OnnxWordSenseModel_EnsureModelAsync_AlteredFile_ThrowsModelIntegrity`); an invalid option is
`DownloadConfigurationException`, which is not an `ArgumentException`
(`OnnxWordSenseModel_EnsureModelAsync_InvalidOptions_ThrowsDownloadConfiguration`). The core package reports a damaged
or missing embedded resource as `ModelIntegrityException`, and the hierarchy itself is verified in
`WordSenseExceptions_Hierarchy_DerivesFromWordSenseException` (see the core and Onnx subsystem documents).

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
only, which declares classification and disposal. The preparation step is the optional capability
`IDownloadableWordSenseModel`: the encoder models have it and the Basic model does not
(`BasicWordSenseModel_IsNotDownloadable`, `OnnxWordSenseModel_IsDownloadable`, `MiniLMWordSenseModel_IsDownloadable`,
`BertBaseCasedWordSenseModel_IsDownloadable`), and the contract itself has no ensure member
(`WordSenseContracts_IWordSenseModel_HasNoEnsureMember`). For a word whose lemma and part of speech have a single sense
every package gives the same answer, the sense at
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
endings and trailing white space. The Quick Start sample does not call `EnsureModelAsync`, because `ClassifyAsync`
prepares the model on first need. Two more compile-only samples show the optional preparation step: the sample marked
`download` calls `await model.EnsureModelAsync();` (and the overload with `DownloadOptions`) on the concrete MiniLM
model, and the sample marked `capability` asks an `IWordSenseModel` for `IDownloadableWordSenseModel` with a type check
(`WordSense_Readme_DownloadBlock_MatchesCompiledSample`, `WordSense_Readme_CapabilityBlock_MatchesCompiledSample`). The
set of markers (`readme`, `download`, `capability`) must equal the set of checked samples
(`WordSense_Readme_SampleMarkers_AreAllChecked`). The Quick Start sample runs against
each model (a stand-in model class wraps Basic, or the committed MiniLM or BertBaseCased model) and gives the output
documented for it. The word, part of speech, level and sense id of every output line must match the README exactly; the
confidence is compared numerically, within 0.01 for Basic (deterministic managed code) and within 0.10 for the encoder
models, whose int8 inference differs slightly between CPUs and operating systems. The README is the single source of
truth that the tests parse. The two commented alternative lines are checked as text and their model classes are created.
Tests that need model files are skipped when the files are absent (failed instead when `WORDSENSE_REQUIRE_MODELS` is
set, as in the CI run). The user guide samples are reviewed with the guide
and are not compiled. The XML documentation examples of every package are
extracted from the shipped XML files and compiled against the public API (the `XmlDocExampleTests` of each test
project).

### Published Evaluation and Suite

The README model table is compared with the generated table file `docs/user_guide/model-table.md`, so a README table
that differs from the file fails the build (`ModelTable_Readme_MatchesGeneratedFile`), and the file has the header, the
separator and one row for each of the three models (`ModelTable_GeneratedFile_HasRowForEachModel`). The Basic row of the
file is also checked against a new measurement of the evaluator on the committed holdout set, for the sense, download,
weights and needed cells only, because the timing cells depend on the machine
(`WordSenseBuilder_ModelTable_BasicRow_MatchesEvaluation`); the encoder rows are written by the builder from measured
values and are not re-measured by a test. The reviewed
suite sentences carry the expected sense, group or part of speech and the expectation of each model (pass or known
miss); the Basic, MiniLM and BertBaseCased test projects run every row against their model and fail when an expected
pass no longer passes (`BasicSuite_ExpectedPasses_StillPass`, `MiniLMSuite_ExpectedPasses_StillPass`,
`BertBaseCasedSuite_ExpectedPasses_StillPass`); the encoder tests are skipped when the model files are absent. The
independent evaluations (SemCor and running-text part of speech) are verified in the builder documents: their figures
come from downloaded texts that are never committed.

### Keys Name the Meaning of Every Known Answered Word

A known word that a model answered with a selected analysis has exactly one key of the format
`{spelling}|{lemma}/{Part}[/{Features}][#{label}]`, and it is the same for every model because it depends on the result
alone: at the sense level the label is the answered sense and a group id never appears
(`ClassifyResult_Key_SenseLevel_HasTheLabelOfTheAnsweredSense`, `ClassifyResult_Key_GroupId_IsNeverUsed`); a group or
part-of-speech result takes the label of the most probable ranked sense of its analysis
(`ClassifyResult_Key_RankedSenses_FallBackToTheTopRankedSense`). The three models give a key to exactly the known words
of the reviewed suite and of about 570 held-out sentences, and every such key is in `WordLexicon.Keys()`
(`BasicKeys_SuiteAndCorpusSentences_AreAllEnumerated`, `MiniLMKeys_SuiteAndCorpusSentences_AreAllEnumerated`,
`BertBaseCasedKeys_SuiteAndCorpusSentences_AreAllEnumerated`; the encoder tests need the model files). The system
requirements are `WordSense-Lib-Keys-Answered` and `WordSense-Lib-Keys-Unanswered`.

### Unknown and Unanswered Words Have No Key

An unknown word, a default result and a known word without a selected analysis have a null key
(`ClassifyResult_Key_UnknownOrUnanswered_IsNull`); in the model-level check a word has a key exactly when it is known.

### Every Key Can Be Enumerated and Parsed

`WordLexicon.Keys()` lists more than a million keys, each once and in a fixed order, and every key parses with
`WordKey.TryParse` and formats back to itself; text that is not a key in canonical form (for example an upper-case
spelling) is refused (`WordLexicon_Keys_Enumeration_ListsNoKeyTwice`,
`WordLexicon_Keys_EveryKey_ParsesBackToItselfWithANormalizedSpelling`,
`WordLexicon_Keys_LookedUpWord_HasAKeyForEachAnalysisAndSense`, `WordLexicon_Keys_Enumeration_IsDeferredAndRepeatable`,
`WordKey_TryParse_ValidKey_RoundTrips`, `WordKey_TryParse_NotAKey_IsRefused`). That every key of every model is in the
list holds by construction (the key of a result and the list are built from the same analyses and catalog senses); the
model tests above verify it on samples.

### Keys and Heteronyms Are Exported

The builder commands `keys --output FILE` and `heteronyms --output FILE` write the sorted key file and the heteronym
table, and every key of the table is a key of the lexicon
(`WordSenseBuilder_Program_Keys_WritesTheKeyFile`, `WordSenseBuilder_Program_Heteronyms_WritesTheTable`,
`WordSenseBuilder_Heteronyms_Build_EveryKeyIsAKeyOfTheLexicon`,
`WordSenseBuilder_Heteronyms_Build_ListsTheKnownHeteronymsOfEachLevel`). The committed appendix of the user guide
explains the keys and lists the heteronyms, and is compared with a regenerated one. The system requirements are
`WordSense-Lib-Keys-Export`, `WordSense-Lib-Heteronyms` and `WordSense-Lib-Heteronyms-Export`; the build workflow
publishes both files as the `word-keys` artifact.

### Data

The committed data is validated: every sense has one unique label, the groups partition the senses of their lemmas,
every keyed sense has a frequency and the project senses have no key
(`WordSenseBuilder_DataEndToEnd_CommittedData_HasSenseLabelsAndGroups`).

### Builder Commands

The builder verification document describes the commands `compile-image`, `import-moby`, `weights`, `evaluate`,
`evaluate-senses`, `evaluate-running`, `open-counts`, `evaluate-tagger`, `coverage`, `train-basic`, `tune-basic`,
`label-senses`, `import-wn30-counts`, `tiers`, `appendix`, `keys` and `heteronyms`. Together they regenerate the data
that is committed in the
packages, and the committed appendix of the user guide is compared with a regenerated one.
