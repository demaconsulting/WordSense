# WordSenseModelDownloader Unit Design

## Overview

`WordSenseModelDownloader.EnsureModelAsync` makes the model directory complete and verified and
returns a `WordSenseModelLocation`. `WordSenseModelCatalog` pins the file names and SHA-256 digests.

## Data Model

- Store: `{LocalApplicationData}/DemaConsulting/WordSense/models/all-MiniLM-L6-v2-int8-{sha8}` (or
  `StoreRoot`).
- Files: `model_int8.onnx` (22,972,370 bytes) and `vocab.txt`, each with a pinned digest and a flag
  telling whether Hugging Face may serve it. `tokenizer.json` is pinned for documentation only.
- `WordSenseModelDownloaderOptions`: `Mirror`, `GetEnvironmentVariable`, `HuggingFaceBaseUri`,
  `GitHubLfsBaseUri`, `GitHubLfsRef`, `StoreRoot`, `HttpClient`, `RequestTimeout`, `Progress`. Base
  URIs are overridable so tests use a local server.

## Algorithm

For each file: if it exists with the pinned digest, skip it (no request). Otherwise build the source
list in order (mirror from options or environment; Hugging Face if the file allows it and the base is
set; GitHub LFS) and try each: download to a unique `.part` file under a per-attempt timeout, verify
SHA-256, then atomically move into place. Request failures, timeouts, digest mismatches and I/O errors
are recorded and the next source is tried; cancellation by the caller propagates. If all sources fail,
`WordSenseModelDownloadException` is thrown with every failure. Authentication headers are attached
only to mirror requests.

## Hugging Face finding

The committed `model_int8.onnx` was produced by local dynamic quantization (22,972,370 bytes,
SHA-256 `afdb6f1a...`). The upstream repository publishes `onnx/model_qint8_avx512.onnx`
(24,113,097 bytes) among others; none matches the committed digest, so the ONNX file is never requested
from Hugging Face. `vocab.txt` carries the well-known BERT uncased vocabulary digest and is requested
from Hugging Face; the digest could not be compared against the live upstream file from the
development environment (TLS interception blocked access) and is therefore verified on download.

## Interactions

Uses `DownloadMirror`; called by `WordSenseClassifier.CreateAsync`.
