### OnnxWordSenseModel Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxWordSenseModel` is the public abstract base of the encoder models. It prepares the model files, loads the
encoder and weights lazily, and creates classifiers that share them.

#### Data Model

- The private protected constructor takes the model description (`ModelSpec`), an optional model directory (converted
  to a full path) and the maximum sequence length, which shall be from 8 to 512.
- Public properties: `HiddenSize` and `ModelId`. They are public on purpose: callers and the model packages read them
  (for example to show which model and encoder size is in use) before `EnsureModelAsync` has run.
- State: the in-flight (or finished) ensure task, the cached encoder and weights table, and the disposed flag, all
  guarded by a lock, plus a cancellation source that disposal cancels and then disposes (the download holds a copy of
  its token, which stays cancelled).
- Internal test and builder hooks: the ONNX intra-op thread count, the HTTP client, the GitHub base URI, the
  environment variable reader, `OpenEncoder()` (builder training) and the description.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: starts one shared task when none exists or the previous one faulted
  or was canceled (retry after failure); concurrent callers share that task and each caller can stop waiting when its
  own token is canceled without canceling the shared work. With a model directory the task only verifies the files
  (`ModelDownloader.Verify`, no network); otherwise it calls `ModelDownloader.EnsureAsync` with the disposal token.
- `Create()`: on first call loads the embedded weights through `LemmaWeights` and opens the encoder; both are cached
  and shared by all classifiers. Returns a new `OnnxClassifier` that checks the disposed flag on every call.
- `Dispose()`: idempotent and thread-safe; the first call disposes the encoder and then cancels the disposal token, which
  aborts an in-flight download (its temporary files are removed). It does not wait for the abort. The shared ensure
  task, and so every pending `EnsureModelAsync`, ends canceled: `OperationCanceledException`, whatever exception the
  abort surfaced as.

#### Error Handling

- `Create` before a successful `EnsureModelAsync` throws `InvalidOperationException`.
- Any call after `Dispose` throws `ObjectDisposedException`, including every call of a classifier created earlier; a
  pending `EnsureModelAsync` that was running at disposal throws `OperationCanceledException` instead.
- A constructor sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
- Download and verification failures propagate from `ModelDownloader` (see that unit); a failed ensure can be
  retried.
- A missing or inconsistent weights resource throws `InvalidDataException` from `Create`.

#### Dependencies

`ModelDownloader`, `DownloadMirror` (through the downloader), `LemmaWeights` (weights table), `OnnxEncoder`,
`OnnxClassifier` and the core contracts.

#### Callers

`MiniLMWordSenseModel`, `BertBaseCasedWordSenseModel`, application code (through the interface) and the builder.
