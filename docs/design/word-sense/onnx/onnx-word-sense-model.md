### OnnxWordSenseModel Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxWordSenseModel` is the public abstract base of the encoder models. It implements `IDownloadableWordSenseModel` (the
classification contract plus the optional explicit ensure) and the internal marker `IPartOfSpeechEvidenceModel`. It
prepares the model files, loads the encoder, the weights and the part-of-speech head lazily, and classifies words with
them (lazily ensuring the files when a word needs the encoder).
It is usable on its own and depends only on the core package: it owns no other model, so a word whose senses it does
not score is answered from the lexicon (with the head's probabilities of its parts of speech when the model uses its
head), and the choice of the senses of such a word is left to the model it is composed with (see
`FallbackWordSenseModel`).

The model description (`ModelSpec`) says whether a head is named (`HeadResourceName`) and whether the model answers
with it (`HeadInUse`). A named head is loaded and integrity-checked whether or not it is in use. A model whose head is in
use needs the encoder, and so the model files, for every classification that contains a known word of two or more
distinct parts of speech or a word with scored senses, and there is no way to turn that off. A classification
without such a word is answered from the lexicon alone: it loads no encoder and needs no model file. A model that
carries a head it does not use, or has none, needs the encoder only for a word with scored senses (a trained lemma with
several senses) and answers as it did without a head. `ProvidesPartOfSpeechEvidence` is true exactly when a head is
named and in use; `FallbackWordSenseModel` reads it. The description also says whether the model answers with the tense
head of its head file (`TenseHeadInUse`, which needs a head in use). A model that does needs the encoder, and so the
model files, also for a classification that contains a same-spelling verb (a verb with a past and a non-past analysis:
`read`, `put`, `found`), and gives the probability that such a verb is past beside its results
(`ClassifyWithTenseAsync`, internal). `ProvidesTenseEvidence` is true exactly when a head is named, in use, and
`TenseHeadInUse` is set.

#### Data Model

- The private protected constructor takes the model description (`ModelSpec`), an optional model directory (converted
  to a full path) and the maximum sequence length, which shall be from 8 to 512.
- Public properties: `HiddenSize` and `ModelId`. They are public on purpose: callers and the model packages read them
  (for example to show which model and encoder size is in use) before an explicit ensure has run.
- State: the in-flight (or finished) ensure task, the shared classifier, the encoder, the weights table and the
  part-of-speech head (opened and
  loaded on first need), the disposed flag and an in-flight tracker that counts running classifications, all
  guarded by a lock, plus a cancellation source that disposal cancels and then disposes (the download holds a copy of
  its token, which stays cancelled).
- Internal test and builder hooks: the ONNX intra-op thread count, the HTTP client, the GitHub base URI, the
  environment variable reader, `OpenEncoder()` (builder training), `GetHead()` and the description.

#### Key Methods

- `EnsureModelAsync(options, cancellationToken)` (the `IDownloadableWordSenseModel` member, optional because
  `ClassifyAsync` ensures lazily): starts one shared task (run on the thread pool with no token of its own)
  when none exists or the previous one faulted or was canceled (retry after failure); concurrent callers share that
  task, and the next call while it runs joins it (its `options` are ignored). A caller's token only cancels that
  caller's wait (`WaitAsync`): the shared work keeps running even when no waiter is left, and only disposal cancels it.
  An already canceled token returns a canceled task rather than throwing. With a model directory the task only
  verifies the files
  (`ModelDownloader.Verify`, no network, and the options and the environment are ignored); otherwise it calls
  `ModelDownloader.EnsureAsync` with the disposal token. The shared task wraps the raw exceptions of the downloader
  at this public boundary (see _Error Handling_).
- `ClassifyAsync(words, cancellationToken)`: checks the disposed flag, validates the words, throws
  `OperationCanceledException` (through the returned task) for an already canceled token, even for an empty list or a
  list that needs no model. It then loads the embedded weights table (no model file and no ONNX; cached) together with
  the embedded part-of-speech head (the same, whenever the description names one) and, when no
  word needs the encoder (`OnnxClassifier.NeedsEncoder`, with the head's presence and whether the model judges tense as
  its arguments: no word is pending,
  that is has several senses together with usable weights or a form chooser, and, for a model whose head is in use,
  no known word has two or more distinct parts of speech in the lexicon, and, for a model that judges tense, no word is
  a same-spelling verb), answers from the lexicon alone without any
  model file: unknown words are `ClassifyResult.Unknown`, a word
  with one analysis and one sense is answered at the sense level, and every other known word with its analyses equally
  likely at the part of speech level and no scored senses (no first-sense guess). Otherwise it
  awaits `EnsureModelAsync(null, cancellationToken)` (the default and environment download settings; concurrent
  callers share one ensure and a failed ensure is retried by the next call), registers with the in-flight tracker, opens
  the encoder on first need (cached) and runs the shared `OnnxClassifier` over the encoder and the weights table, built
  with the head and the view encoder (`OnnxEncoder.EncodeAllAsync` with `PossessiveView.IsLexiconPossessive`) when the
  model uses its head and without them otherwise (with the tense head when the model judges tense); the
  registration is released in a `finally`. `ClassifyAsync` is the results of `ClassifyWithTenseAsync` (internal, also
  the member of `IPartOfSpeechEvidenceModel`), which gives the results with the probability of past of each
  same-spelling verb (null for every word when no encoder was needed or the model does not judge tense). It is safe for
  concurrent use.
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
- `GetHead()` (internal): the embedded part-of-speech head, loaded once under the lock and kept; null when the model
  description names no head resource (`ModelSpec.HeadResourceName`). The load is `PosHead.LoadEmbedded` with the model
  id, the hidden size and the pinned SHA-256 of the ONNX file from the description, so it needs no model file. The
  weights table load (`GetTable`) and the opening of the classifier call it, so the first classification, or the open of
  the classifier, loads and checks the head. A failure is not remembered: the next call fails the same way. The
  classifier uses the head only when `ModelSpec.HeadInUse` is true; the model that declares it not in use validates the
  head and never answers with it. A model with `TenseHeadInUse` also requires the head file to have a tense head:
  without one the load fails with an `InvalidDataException` ("The head file has no tense head."), which is reported
  like any other head failure.

#### Error Handling

- Any call of `EnsureModelAsync` or `ClassifyAsync` after `Dispose` throws `ObjectDisposedException` (thrown from the
  call by `EnsureModelAsync`, reported through the returned task by the `async` `ClassifyAsync`; the disposed check
  comes before the cancellation check); a pending
  ensure that was running at disposal throws `OperationCanceledException` instead. Disposal never deletes model files.
  Disposing while an inference runs is safe: the running calls complete and the session is released after the last
  one. Inference itself is not serialized: the shared session serves concurrent calls in parallel.
- A constructor sequence length outside 8 to 512 throws `ArgumentOutOfRangeException` (a null spec throws
  `ArgumentNullException`).
- Download and verification failures propagate from `ModelDownloader` (see that unit) and are reported by the shared
  ensure task, not synchronously, as typed exceptions of the core with the original exception as the inner exception
  (the downloader, `DownloadMirror` and `WeightedLemmaTable` keep their raw exceptions as internal vocabulary; the
  wrapping is done here, at the public boundary). A failed ensure can be retried. The mapping is:
  - `FileNotFoundException` or `DirectoryNotFoundException` (the model directory is missing or lacks a file) and
    `HttpRequestException` (every download source failed, including a source that served bytes with a wrong SHA-256,
    because the next source is tried) become `ModelUnavailableException`;
  - `InvalidDataException` from `ModelDownloader.Verify` (a pinned SHA-256 mismatch of a file of a supplied model
    directory) becomes `ModelIntegrityException`;
  - `ArgumentException`, including `ArgumentOutOfRangeException` (a relative, non-http(s), query, fragment or user-info
    base URI, a token together with credentials, credentials over plain http to a non-loopback host, a timeout that is
    zero, negative or above about 49 days, an invalid store root path, invalid `WORDSENSE_MIRROR_*` environment
    settings) become `DownloadConfigurationException`; it is not thrown when a model directory is supplied;
  - `IOException` and `UnauthorizedAccessException` of an unwritable store are not wrapped;
    `OperationCanceledException` (a canceled caller, or disposal during a download) is unchanged.
- `ClassifyAsync` wraps at the points where it loads its own resources (`LoadTable` for `GetTable` and `GetClassifier`,
  the head load, and the encoder opening): an `InvalidDataException` of `WeightedLemmaTable` (embedded weights missing,
  corrupt or not
  for this word image or dimension) and of the classifier (weights and image inconsistent) becomes a
  `ModelIntegrityException`, and so does an `InvalidDataException` of `PosHead` (the embedded head missing, corrupt, or
  trained for another model, hidden size, encoder file, feature layout or class order) with a message that names the
  model and the original as inner exception; a structural failure of the encoder constructor on files that passed
  their SHA-256 (an
  `OnnxRuntimeException`, `InvalidDataException`, `FormatException`, `InvalidOperationException` or
  `IndexOutOfRangeException` of the ONNX file or its metadata, or an `ArgumentException` such as a vocabulary without a
  required `[UNK]`, `[CLS]` or `[SEP]` token) becomes a `ModelIntegrityException` with the original as inner exception;
  so does an opened encoder whose hidden size differs from `ModelSpec.HiddenSize` (the size the embedded weights are
  checked against): `OpenEncoderCore` disposes it and throws an `InvalidDataException` naming the model and both sizes,
  which is wrapped like the other structural failures, so the encoder is never cached and a mismatch is never answered
  silently from the lexicon (the internal `OpenEncoder()` of the builder applies the same check and lets the
  `InvalidDataException` propagate);
  an `ArgumentNullException`, an `OperationCanceledException`, an `ObjectDisposedException` and a `WordSenseException`
  are never mapped (nor re-wrapped); a
  `FileNotFoundException` or `DirectoryNotFoundException` when opening a verified model file that is gone becomes a
  `ModelUnavailableException`. Weights requested before the files were ensured throw
  `InvalidOperationException("The model files are not available.")` (an internal guard that the public call sequence
  cannot reach).

#### Dependencies

`ModelDownloader`, `DownloadMirror` (through the downloader), `LemmaWeights` (weights table), `PosHead` (the embedded
head), `OnnxEncoder`,
`OnnxClassifier` and the core contracts (`IDownloadableWordSenseModel`, `IPartOfSpeechEvidenceModel`,
`DownloadOptions` and
the typed exceptions).
No other model package.

#### Callers

`MiniLMEncoderModel` and `BertBaseCasedEncoderModel` (the internal encoder models, which the public models compose with
the Basic model), application code (through the interface) and the builder.
