# System Verification Design

This document describes the system-level verification strategy for WordSense.

## Verification Approach

The system is verified by integration tests in `WordSenseTests.cs` that run the real int8 model and
the embedded table through the public API. Unit-level behavior is verified in the unit documents with
substitutes (a fake encoder and a local HTTP server).

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Dependencies**: the Git LFS model files `model_int8.onnx` and `vocab.txt` under `model/`; no network access. Tests that
  need the model are skipped, not failed, when the LFS content is absent.
- **Isolation**: each test creates its own classifier; no shared state.

## External Interface Simulation

Downloads are verified against a loopback `HttpListener` (see the model downloader verification);
no test reaches a public host.

## System-Level Test Scenarios

### WordSense_Classifier_EndToEnd_ClassifiesRecord

A classifier built on the real model classifies "The record was broken last year, so they will record
the new album." and must return noun for the first and verb for the second "record".

### WordSense_Classifier_EndToEnd_WordListApi

A classifier classifies the word list "The", "record", "was", "broken", "so", "they", "will", "record", "it"
with the sense, tag, `IsTag` and detail queries. Every result array is parallel to the input; the first
"record" is noun and the second is verb (tag `noun`/`verb`, `IsTag(verb)` false/true), the non-ambiguous word
"The" yields `null`, and the detail query returns the same sense instance as the sense query.

### WordSense_EmbeddedTable_MatchesPinnedModel

The embedded table's model digest, the pinned digests and the committed `SHA256SUMS` agree with the
files in `model/`; a stale table fails.

### WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory

Creating a classifier from an empty directory (no model files) fails verification.

### WordSense_Classifier_Holdout_MeetsAccuracyGate

Every fifth held-out sentence of every corpus file is classified; the answered accuracy must be at
least 96 % with at least 85 % coverage over more than 400 sentences. Rows whose target is an inflected form are
classified through the forms table, so every such form must be listed there (see the forms completeness scenario
below); an unlisted form would count as unanswered.

### WordSense_Classifier_Holdout_MeetsNounVerbTagGate

Holdout rows whose gold sense has exactly one of the `noun` and `verb` tags (rows with both or neither are
skipped) are classified with the tag query; the answered accuracy must be at least 97 % with at least 85 %
coverage over more than 350 rows (the build report measures 99.15 % at 91.74 % over
the 5,411 rows of the whole holdout; the test evaluates every fifth row).

### WordSense_Classifier_Holdout_MeetsGroupGate

The holdout is classified (every fifth row, as in the accuracy gate) and a result counts as correct when the
predicted sense's `Group` equals the gold sense's group; the answered accuracy must be at least 96 % with at
least 85 % coverage over more than 400 rows (the senses of `bow`, `lead`, `tear`, `closer` and `polish` share
groups, so this gate can exceed the sense-level gate; the build report measures 96.37 % against 96.17 % over
the whole holdout).

### WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma

Every braced target word of every anchor, holdout and real-text row (lowercased, curly apostrophe normalized, with
the possessive fallback of the classifier) must resolve through the embedded forms table to the lemma of its row.
The only exception is the plural `rows` of the lemma `row`, because `rows` is itself a trained lemma. The test
fails with the list of missing forms, which are then added to `corpus/forms/speech.tsv` and the table rebuilt.

### WordSense_EmbeddedTable_EverySenseHasExactlyOnePartOfSpeech

Every sense of the embedded table carries exactly one of `noun`, `verb`, `adj`, `adv` and only the qualifiers
`past`, `present`, `comparative`, `superlative`, `participle` besides.

### WordSense_Benchmark_ClassifyLatency

Classifies a two-target sentence 100 times with one and four threads and reports the median and 95th
percentile latency; the median must stay below two seconds (the typical value is a few milliseconds).

### WordSense_Readme_CodeBlock_MatchesCompiledSample

For each README sample (`download`, `classify`, `tags`), extracts the code block marked `<!-- sample:name -->` from
`README.md`
and the region between the `<sample:name>` markers of `test/DemaConsulting.WordSense.Tests/ReadmeSample.cs`, and
requires them to be equal after normalizing line endings and trailing white space. Because the region is compiled
code, the README sample is guaranteed to build against the current API.

### WordSense_Readme_Output_MatchesReadme

Runs the two compiled samples that print output (`classify` and `tags`; `download` prints a directory and is not
run) against the committed model (skipped when the model files are missing or are Git LFS pointers) and requires
the lines they write to equal the output blocks marked `<!-- output:name -->` in `README.md`.
Unknown or abstained words print `-`, so a change to the model, table or abstention shows up as a failing test and
the documented output is updated deliberately.

### WordSense_Readme_SampleMarkers_AreAllChecked

Requires the set of sample markers in `README.md` to equal the set of checked samples (`download`, `classify`,
`tags`).

### WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel

Calls `WordSenseClassifier.CreateAsync` with invalid options and a model directory that does not exist, and requires
the options error (not a model error) to be reported, so invalid options never cause a download or a file read.

### WordSense_UserGuide_CodeBlock_MatchesCompiledSample

For each user guide sample (`quick-start`, `text`, `detail`, `tags`, `senses`, `tuning`, `download`, `mirror`,
`errors`), extracts the code block marked `<!-- sample:name -->` from `docs/user_guide/introduction.md` and the region
between the `<sample:name>` markers of `test/DemaConsulting.WordSense.Tests/UserGuideSamples.cs`, and requires them to
be equal after normalizing line endings and trailing white space. The regions are compiled code, so every user guide
sample builds against the current API.

### WordSense_UserGuide_SampleMarkers_AreAllChecked

Requires the set of sample markers in the user guide to equal the set of checked samples, so a new code block cannot
be added without a compiled copy.

### WordSense_UserGuide_Samples_RunAgainstModel

Runs the samples that need only a classifier (`text`, `detail`, `tags`, `senses`) and the `mirror` sample against the
committed model (skipped when the model files are missing or are Git LFS pointers) and requires that they complete
without an error. The samples that download the model are compiled but not run.

### Platform Support

`WordSense_Classifier_EndToEnd_ClassifiesRecord` is run on each CI operating system (Windows, Linux, macOS) and
under each target framework (net8.0, net9.0, net10.0). The six `WordSense-Platform-*` requirements are each
linked to this test with a source filter (`windows@`, `ubuntu@`, `macos@`, `net8.0@`, `net9.0@`, `net10.0@`), so
a requirement is satisfied only by a passing result from its own platform or framework.

## Requirements Coverage

| Requirement                 | Verified by                                                                         |
|-----------------------------|-------------------------------------------------------------------------------------|
| WordSense-Lib-Classify      | EndToEnd_ClassifiesRecord, EndToEnd_WordListApi                                     |
| WordSense-Lib-Tags          | Holdout_MeetsNounVerbTagGate, MeetsGroupGate, EverySenseHasExactlyOnePartOfSpeech   |
| WordSense-Lib-Accuracy      | Holdout_MeetsAccuracyGate, EveryCorpusTargetResolvesToItsLemma                      |
| WordSense-Lib-Abstain       | ClassifyAsync_LowMargin_Abstains (classifier verification)                          |
| WordSense-Lib-Embedded      | EmbeddedTable_MatchesPinnedModel                                                    |
| WordSense-Lib-Encoder       | EndToEnd_ClassifiesRecord                                                           |
| WordSense-Lib-ModelDownload | EnsureModel_* (downloader verification)                                             |
| WordSense-Lib-Mirror        | EnsureModel_Mirror* (downloader and mirror verification)                            |
| WordSense-Lib-Integrity     | EnsureModel_HashMismatch_*, FromDirectory_ChecksEveryFile (downloader verification) |
| WordSense-Lib-Performance   | Benchmark_ClassifyLatency                                                           |
| WordSense-Lib-Reproducible  | LogisticTrainer_SeparatesAndIsDeterministic (builder verification)                  |
| WordSense-Lib-Documentation | Readme_CodeBlock_MatchesCompiledSample, Readme_Output_MatchesReadme, UserGuide_*    |
| WordSense-Platform-* (six)  | Source-filtered runs of EndToEnd_ClassifiesRecord (Platform Support)                |

The abstention, download, mirror and integrity rows summarize scenarios that are detailed in the unit-level
verification documents of the classifier, downloader and mirror. `WordSense-Lib-Documentation` is also verified by
the appendix scenarios of the builder verification document (`WordSense-Builder-Appendix`).

## Acceptance Criteria

All scenarios pass on every platform and target framework of the CI matrix.
