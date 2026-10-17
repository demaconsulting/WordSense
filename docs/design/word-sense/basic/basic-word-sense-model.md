### BasicWordSenseModel Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicWordSenseModel` is the public entry point of the Basic package: an `IWordSenseModel` that needs nothing to
prepare and classifies with the statistical classifier.

#### Data Model

The class is sealed and holds one volatile disposed flag and a lazily created internal `BasicClassifier`. The
statistics live in the process-wide `BasicTables.Default`, which the classifier loads on first use.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: throws `ObjectDisposedException` after disposal; otherwise ignores the
  mirror and returns a completed task, or a canceled task when the token is already canceled.
- `ClassifyAsync(words, cancellationToken)`: throws `ObjectDisposedException` after disposal; otherwise classifies with
  the embedded statistics and the core lexicon. It may be called at any time, with or without `EnsureModelAsync` (the
  lazy ensure is trivial), and is safe for concurrent use.
- `Dispose()`: sets the flag; idempotent and thread-safe; releases nothing, so a classification that is already
  running completes normally (the Onnx model defers its release for the same guarantee).

#### Error Handling

No exceptions other than the cancellation of `EnsureModelAsync` and `ClassifyAsync`, `ObjectDisposedException`
after `Dispose`, and the argument errors of `ClassifyAsync` (`ArgumentNullException` for a null list,
`ArgumentException` for a null element). A corrupt
embedded table is reported by the first classification, not by the model.

#### Dependencies

`BasicClassifier` and the core contracts.

#### Callers

Application code and the builder (`evaluate basic`).
