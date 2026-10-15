# System Verification Design

This document describes the system-level verification strategy for WordSense.

## Verification Approach

The system is verified by integration tests in `WordSenseTests.cs` that run the real int8 model and
the embedded table through the public API. Unit-level behavior is verified in the unit documents with
substitutes (a fake encoder and a local HTTP server).

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Dependencies**: the Git LFS model files (including `tokenizer.json`) under `model/`; no network access. Tests that
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

Creating a classifier from a directory without the model files fails.

### WordSense_Classifier_Holdout_MeetsAccuracyGate

Every fifth held-out sentence of every corpus file is classified; the answered accuracy must be at
least 96 % with at least 85 % coverage over more than 400 sentences. Rows whose target is an inflected form are
classified through the forms table, so every such form must be listed there (see the forms completeness scenario
below); an unlisted form would count as unanswered.

### WordSense_Classifier_Holdout_MeetsNounVerbTagGate

Holdout rows whose gold sense has exactly one of the `noun` and `verb` tags (rows with both or neither are
skipped) are classified with the tag query; the answered
accuracy must be at least 97 % with at least 85 % coverage (the build report measures 99.15 % at 91.90 % over
the 5,383 rows of the whole holdout; the test evaluates every fifth row).

### WordSense_Classifier_Holdout_MeetsGroupGate

The holdout is classified and a result counts as correct when the predicted sense's `Group` equals the gold
sense's group (the senses of `bow`, `lead`, `tear`, `closer` and `polish` share groups, so this gate can exceed the
sense-level gate; the build report measures 96.47 % against 96.36 % over the whole holdout).

### WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma

Every braced target word of every anchor, holdout and real-text row (lowercased, curly apostrophe normalized, with
the possessive fallback of the classifier) must resolve through the embedded forms table to the lemma of its row.
The only exception is the plural `rows` of the lemma `row`, because `rows` is itself a trained lemma. The test
fails with the list of missing forms, which are then added to `corpus/forms/speech.tsv` and the table rebuilt.

### WordSense_EmbeddedTable_EverySenseHasExactlyOnePartOfSpeech

Every sense of the embedded table carries exactly one of `noun`, `verb`, `adj`, `adv` and only the qualifiers
`past`, `present`, `comparative`, `superlative` besides.

### WordSense_Benchmark_ClassifyLatency

Classifies a two-target sentence 100 times with one and four threads and reports the median and 95th
percentile latency; the median must stay below two seconds (the typical value is a few milliseconds).

### Platform Support

`WordSense_Classifier_EndToEnd_ClassifiesRecord` is run on each CI operating system (Windows, Linux, macOS) and
under each target framework (net8.0, net9.0, net10.0). The six `WordSense-Platform-*` requirements are each
linked to this test with a source filter (`windows@`, `ubuntu@`, `macos@`, `net8.0@`, `net9.0@`, `net10.0@`), so
a requirement is satisfied only by a passing result from its own platform or framework.

## Requirements Coverage

| Requirement                | Verified by                                                          |
|----------------------------|----------------------------------------------------------------------|
| WordSense-Lib-Classify     | EndToEnd_ClassifiesRecord, EndToEnd_WordListApi                      |
| WordSense-Lib-Tags         | Holdout_MeetsNounVerbTagGate, Holdout_MeetsGroupGate                 |
| WordSense-Lib-Accuracy     | Holdout_MeetsAccuracyGate, EveryCorpusTargetResolvesToItsLemma       |
| WordSense-Lib-Embedded     | EmbeddedTable_MatchesPinnedModel                                     |
| WordSense-Lib-Encoder      | EndToEnd_ClassifiesRecord                                            |
| WordSense-Lib-Performance  | Benchmark_ClassifyLatency                                            |
| WordSense-Lib-Reproducible | LogisticTrainer_SeparatesAndIsDeterministic (builder verification)   |
| WordSense-Platform-* (six) | Source-filtered runs of EndToEnd_ClassifiesRecord (Platform Support) |

The remaining library requirements (abstention, download, mirror, integrity) are verified by the unit-level
scenarios of the classifier, downloader and mirror documents.

## Acceptance Criteria

All scenarios pass on every platform and target framework of the CI matrix.
