# System Verification Design

This document describes the system-level verification strategy for the MiniLM model package,
`DemaConsulting.WordSense.MiniLM`.

## Verification Approach

The system is verified through the public factory `MiniLMWordSense.CreateClassifierAsync`, the embedded weights and
the model downloader. Tests in `test/DemaConsulting.WordSense.MiniLM.Tests` run the real int8 model (Git LFS) against
the embedded lexicon and weights; the download scenarios use a loopback `HttpListener`. Unit-level behavior is
verified in the unit documents.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: the model files `model_int8.onnx` and `vocab.txt` under `model/all-MiniLM-L6-v2-int8/` (Git LFS);
  tests that need them are skipped when the LFS content is absent
- **Isolation**: each test creates its own classifier and store directory

## External Interface Simulation

The Hugging Face, GitHub LFS and mirror hosts are replaced by a loopback `HttpListener` serving small files with
pinned digests substituted. No test reaches a public host.

## System-Level Test Scenarios

### WordSense_Classifier_EndToEnd_ClassifiesRecord

A classifier created by the factory from the committed model directory classifies "The record was broken last year,
so they will record the new album." with noun for the first and verb for the second "record".

### WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel

Invalid options are reported before any model directory is touched.

### WordSense_EmbeddedWeights_MatchPinnedModelAndLexicon

The weights embedded for the model id and ONNX digest load and match the pinned model and the embedded lexicon.

### WordSenseModelDownloader_EnsureModel_MirrorFails_FallsThroughInOrder and WordSenseModelDownloader_EnsureModel_NoMirror_HuggingFaceThenLfs

Sources are tried in the order mirror, Hugging Face, GitHub LFS; without a mirror the order starts at Hugging Face.

### WordSenseModelDownloader_EnsureModel_MirrorFromEnvironment_IsUsedFirst and WordSenseModelDownloader_EnsureModel_ProgrammaticMirror_BeatsEnvironment

A mirror from the environment is tried first, and a mirror given in the options takes precedence over it.

### WordSenseModelDownloader_EnsureModel_HashMismatch_FallsThroughAndKeepsNothingBad

A file with a wrong digest is never kept and the next source is tried.

### WordSense_Benchmark_ClassifyLatency

Classifies a fixed text repeatedly and writes the measured latency to the test output; it fails only if the work does
not complete. The numbers are recorded in the build notes.

## Requirements Coverage

- `WordSenseMiniLM-Lib-Factory`: **WordSense_Classifier_EndToEnd_ClassifiesRecord** and
  **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel**.
- `WordSenseMiniLM-Lib-EmbeddedWeights`: **WordSense_EmbeddedWeights_MatchPinnedModelAndLexicon**.
- `WordSenseMiniLM-Lib-Encoder`: **WordSense_Classifier_EndToEnd_ClassifiesRecord**.
- `WordSenseMiniLM-Lib-ModelDownload`: **WordSenseModelDownloader_EnsureModel_MirrorFails_FallsThroughInOrder** and
  **WordSenseModelDownloader_EnsureModel_NoMirror_HuggingFaceThenLfs**.
- `WordSenseMiniLM-Lib-Mirror`: **WordSenseModelDownloader_EnsureModel_MirrorFromEnvironment_IsUsedFirst** and
  **WordSenseModelDownloader_EnsureModel_ProgrammaticMirror_BeatsEnvironment**.
- `WordSenseMiniLM-Lib-Integrity`: **WordSenseModelDownloader_EnsureModel_HashMismatch_FallsThroughAndKeepsNothingBad**.
- `WordSenseMiniLM-Lib-Performance`: **WordSense_Benchmark_ClassifyLatency**.

## Acceptance Criteria

All scenarios pass on every platform and target framework of the CI matrix.
