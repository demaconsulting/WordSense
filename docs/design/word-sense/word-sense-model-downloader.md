# WordSenseModelDownloader Unit Design

## Overview

`WordSenseModelDownloader.EnsureModelAsync` makes the model directory complete and verified and
returns a `WordSenseModelLocation`. `WordSenseModelCatalog` pins the file names and SHA-256 digests.

## Data Model

- Store: `{LocalApplicationData}/DemaConsulting/WordSense/models/all-MiniLM-L6-v2-int8-{sha8}` (or
  `StoreRoot`).
- Files: `model_int8.onnx` (22,972,370 bytes) and `vocab.txt`, each with a pinned digest and a flag
  telling whether Hugging Face may serve it. The catalog pins exactly these two files.
  Every file under `model/` is a Git LFS object; the GitHub source uses the media endpoint and the digests are of
  the real content, never of the LFS pointer.
- `WordSenseModelDownloaderOptions`: `Mirror`, `GetEnvironmentVariable`, `HuggingFaceBaseUri`,
  `GitHubLfsBaseUri`, `GitHubLfsRef`, `StoreRoot`, `HttpClient`, `RequestTimeout`, `Progress`. Base
  URIs are overridable so tests use a local server.

## Algorithm

For each file: if it exists with the pinned digest, skip it (no request). Otherwise build the source
list in order (mirror from options or environment; Hugging Face if the file allows it and the base is
set; GitHub LFS) and try each: download to a unique `.part` file under a per-attempt timeout, verify
SHA-256, then atomically move into place. Request failures, timeouts, digest mismatches and I/O errors
are recorded and the next source is tried; cancellation by the caller propagates. If all sources fail,
`WordSenseModelDownloadException` is thrown with every failure. `RequestTimeout` is validated first
(positive and no longer than about 49 days, the largest value a cancellation timer accepts, or infinite; else
`ArgumentOutOfRangeException`). A `StoreRoot` that is not a valid path (for
example an empty string or one with invalid characters) raises `ArgumentException` from path resolution. An
exception thrown by the progress receiver is not a download failure and propagates to the caller (after the
partial file is deleted), unless it is of a type that is treated as a source failure (`HttpRequestException`,
`IOException`, `UnauthorizedAccessException` or a timeout `OperationCanceledException`), in which case it is
recorded and the next source is tried. Redirects are followed manually (at most 10 hops, http(s) only)
by the private client, which has automatic redirects disabled; authentication headers are attached per
hop and only when the hop has the mirror's scheme, host and port, so no credential reaches another
origin. A caller-supplied client keeps its own redirect policy.

## Public Types

- `WordSenseModelLocation(Directory, ModelPath, VocabularyPath)`: a sealed record naming a verified
  model set. `FromDirectory(directory)` checks every pinned file in the directory and returns the
  location with full paths. It throws `ArgumentNullException` for a null directory,
  `FileNotFoundException` when a file is missing and `InvalidDataException` when a file's SHA-256 differs
  from the pinned digest. `DefaultStoreRoot()` returns
  `{LocalApplicationData}/DemaConsulting/WordSense/models`, falling back to `AppContext.BaseDirectory`
  as the base when the platform reports no local application data folder.
- `WordSenseModelDownloadProgress(FileName, Source, BytesTransferred, TotalBytes)`: a record reported
  through `IProgress` while a file downloads; `TotalBytes` is null when the source does not report a
  length.
- `WordSenseModelDownloadException`: raised when every source fails for a file; `Failures` lists one
  entry per failed attempt, in the order tried (source name and cause).

## Hugging Face finding

The committed `model_int8.onnx` was produced by local dynamic quantization (22,972,370 bytes,
SHA-256 `afdb6f1a...`). The upstream repository publishes `onnx/model_qint8_avx512.onnx`
(24,113,097 bytes) among others; none matches the committed digest, so the ONNX file is never requested
from Hugging Face. `vocab.txt` carries the well-known BERT uncased vocabulary digest and is requested
from Hugging Face; the digest could not be compared against the live upstream file from the
development environment (TLS interception blocked access) and is therefore verified on download.

## Interactions

Uses `DownloadMirror`; called by `WordSenseClassifier.CreateAsync`.
