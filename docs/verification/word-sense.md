# System Verification Design

This document describes the system-level verification strategy for WordSense.

## Verification Approach

The system is verified by integration tests in `WordSenseTests.cs` that run the real int8 model and
the embedded table through the public API. Unit-level behavior is verified in the unit documents with
substitutes (a fake encoder and a local HTTP server).

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Dependencies**: the Git LFS model files under `model/`; no network access. Tests that need the
  model are skipped, not failed, when the LFS content is absent.
- **Isolation**: each test creates its own classifier; no shared state.

## External Interface Simulation

Downloads are verified against a loopback `HttpListener` (see the model downloader verification);
no test reaches a public host.

## System-Level Test Scenarios

### WordSense_Classifier_EndToEnd_ClassifiesRecord

A classifier built on the real model classifies "The record was broken last year, so they will record
the new album." and must return noun for the first and verb for the second "record".

### WordSense_EmbeddedTable_MatchesPinnedModel

The embedded table's model digest, the pinned digests and the committed `SHA256SUMS` agree with the
files in `model/`; a stale table fails.

### WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory

Creating a classifier from a directory without the model files fails.

### WordSense_Classifier_Holdout_MeetsAccuracyGate

Every third held-out sentence of every corpus file is classified; the answered accuracy must be at
least 96 % with at least 85 % coverage over more than 400 sentences.

### WordSense_Benchmark_ClassifyLatency

Classifies a two-target sentence 100 times with one and four threads and reports the median and 95th
percentile latency; the median must stay below two seconds (the typical value is a few milliseconds).

## Acceptance Criteria

All scenarios pass on every platform and target framework of the CI matrix.
