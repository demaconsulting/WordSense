### OnnxWordSenseModel Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxWordSenseModel` is the public abstract base of the encoder models. It prepares the model files, loads the
encoder and weights lazily, and classifies words with them (lazily ensuring the files when a word needs the encoder).
It is usable on its own and depends only on the core package: it owns no other model, so a word whose senses it does
not score is answered from the lexicon alone, and the choice among the analyses of such a word is left to the model it
is composed with (see `FallbackWordSenseModel`). A classification that has no word with several scored senses is
answered from the lexicon alone: it loads no encoder and needs no model file; the lazy ensure, the encoder session and
the weights are used only for a text with a trained lemma.

#### Data Model

- The private protected constructor takes the model description (`ModelSpec`), an optional model directory (converted
  to a full path) and the maximum sequence length, which shall be from 8 to 512.
- Public properties: `HiddenSize` and `ModelId`. They are public on purpose: callers and the model packages read them
  (for example to show which model and encoder size is in use) before `EnsureModelAsync` has run.
- State: the in-flight (or finished) ensure task, the shared classifier, the encoder and the weights table (opened and
  loaded on first need), the disposed flag and an in-flight tracker that counts running classifications, all
  guarded by a lock, plus a cancellation source that disposal cancels and then disposes (the download holds a copy of
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
- `ClassifyAsync(words, cancellationToken)`: checks the disposed flag, validates the words, throws
  `OperationCanceledException` (through the returned task) for an already canceled token, even for an empty list or a
  list that needs no model. It then loads the embedded weights table (no model file and no ONNX; cached) and, when no
  word is pending (`OnnxClassifier.NeedsModel`: no word has several senses together with usable weights, or a form
  chooser), answers from the lexicon alone without any model file: unknown words are `ClassifyResult.Unknown`, a word
  with one analysis and one sense is answered at the sense level, and every other known word with its analyses equally
  likely at the part of speech level and no scored senses (no first-sense guess). Only when a word is pending does it
  await `EnsureModelAsync(null, cancellationToken)` (the default and environment mirror settings; concurrent
  callers share one ensure and a failed ensure is retried by the next call), register with the in-flight tracker, open
  the encoder on first need (cached) and run the shared `OnnxClassifier` over the encoder and the weights table; the
  registration is released in a `finally`. It is safe for concurrent use.
- `Dispose()`: idempotent and thread-safe; the first call marks the model disposed (new calls throw), cancels the
  disposal token, which aborts an in-flight download (its temporary files are removed), and closes the in-flight
  tracker (`InFlightTracker`, internal): when no classification is running the resources (the encoder session, once
  the scoring opens one) are released at once, otherwise the last classification to finish releases them, so a running
  classification never meets a released resource. It never waits, neither for the abort nor for running
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
- A missing or inconsistent weights resource throws `InvalidDataException` from `ClassifyAsync` (once the weights are
  loaded by the classification); weights requested before the files were ensured throw
  `InvalidOperationException("The model files are not available.")` (an internal guard that the public call sequence
  cannot reach).

#### Dependencies

`ModelDownloader`, `DownloadMirror` (through the downloader), `LemmaWeights` (weights table), `OnnxEncoder`,
`OnnxClassifier` and the core contracts. No other model package.

#### Callers

`MiniLMEncoderModel` and `BertBaseCasedEncoderModel` (the internal encoder models, which the public models compose with
the Basic model), application code (through the interface) and the builder.
