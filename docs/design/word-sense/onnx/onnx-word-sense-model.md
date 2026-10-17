### OnnxWordSenseModel Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxWordSenseModel` is the public abstract base of the encoder models. It prepares the model files, loads the
encoder and weights lazily, and classifies words with them (lazily ensuring the files when a word needs the encoder).

#### Data Model

- The private protected constructor takes the model description (`ModelSpec`), an optional model directory (converted
  to a full path) and the maximum sequence length, which shall be from 8 to 512.
- Public properties: `HiddenSize` and `ModelId`. They are public on purpose: callers and the model packages read them
  (for example to show which model and encoder size is in use) before `EnsureModelAsync` has run.
- State: the in-flight (or finished) ensure task, the cached encoder and weights table, the disposed flag and an
  in-flight tracker that counts running classifications, all guarded by a lock, plus a cancellation source that
  disposal cancels and then disposes (the download holds a copy of
  its token, which stays cancelled).
- Internal test and builder hooks: the ONNX intra-op thread count, the HTTP client, the GitHub base URI, the
  environment variable reader, `OpenEncoder()` (builder training) and the description.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: starts one shared task (run on the thread pool with no token of its own)
  when none exists or the previous one faulted or was canceled (retry after failure); concurrent callers share that
  task, and the next call while it runs joins it (its `mirror` is ignored). A caller's token only cancels that caller's
  wait (`WaitAsync`): the shared work keeps running even when no waiter is left, and only disposal cancels it. An
  already canceled token returns a canceled task rather than throwing. With a model directory the task only
  verifies the files
  (`ModelDownloader.Verify`, no network); otherwise it calls `ModelDownloader.EnsureAsync` with the disposal token.
- `ClassifyAsync(words, cancellationToken)`: checks the disposed flag, validates the words and, when some word has
  several lexicon senses, loads the embedded weights table (no model file and no ONNX; cached and shared with the
  later classifier). When no word has several senses together with intact weights for its lemma
  (`OnnxClassifier.NeedsModel` is false), it answers from the lexicon alone without touching model
  files or ONNX; a multi-sense word without weights gets its first sense with confidence `1/n`. Otherwise it awaits
  `EnsureModelAsync(null, cancellationToken)` (the default and
  environment mirror settings; concurrent callers share one ensure and a failed ensure is retried by the next call).
  It then registers with the in-flight tracker, and, under the lock, opens the encoder on first need (cached) and
  runs the `OnnxClassifier`. It is safe for
  concurrent use.
- `Dispose()`: idempotent and thread-safe; the first call marks the model disposed (new calls throw), cancels the
  disposal token, which aborts an in-flight download (its temporary files are removed), and closes the in-flight
  tracker (`InFlightTracker`, internal): when no classification is running the encoder is released at once,
  otherwise the last classification to finish releases it. It never waits, neither for the abort nor for running
  classifications. The shared ensure task, and so every pending `EnsureModelAsync` or lazy ensure, ends canceled:
  `OperationCanceledException`, whatever exception the abort surfaced as. Cancellation applies to the download
  path only; with an explicit model directory the verification is synchronous and is not canceled.
- `AcquireClassifier()` / `ReleaseClassifier()` (internal): `ClassifyAsync` registers itself with the in-flight tracker
  (throwing `ObjectDisposedException` when the model is already disposed) before it takes the classifier and
  unregisters in a `finally`, so the session cannot be released under a running inference.

#### Error Handling

- Any call of `EnsureModelAsync` or `ClassifyAsync` after `Dispose` throws `ObjectDisposedException` (thrown from the
  call by `EnsureModelAsync`, reported through the returned task by the `async` `ClassifyAsync`; the disposed check
  comes before the cancellation check); a pending
  ensure that was running at disposal throws `OperationCanceledException` instead. Disposal never deletes model files.
  Disposing while an inference runs is safe: the running calls complete and the session is released after the last
  one. Inference itself is not serialized: the shared session serves concurrent calls in parallel.
- A constructor sequence length outside 8 to 512 throws `ArgumentOutOfRangeException` (a null spec throws
  `ArgumentNullException`).
- Download and verification failures propagate from `ModelDownloader` (see that unit); a failed ensure can be
  retried. They surface through the returned task, not synchronously.
- A missing or inconsistent weights resource throws `InvalidDataException` from `ClassifyAsync`; weights requested
  before the files were ensured throw `InvalidOperationException("The model files are not available.")` (an internal
  guard that the public call sequence cannot reach).

#### Dependencies

`ModelDownloader`, `DownloadMirror` (through the downloader), `LemmaWeights` (weights table), `OnnxEncoder`,
`OnnxClassifier` and the core contracts.

#### Callers

`MiniLMWordSenseModel`, `BertBaseCasedWordSenseModel`, application code (through the interface) and the builder.
