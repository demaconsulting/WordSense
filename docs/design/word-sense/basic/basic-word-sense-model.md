### BasicWordSenseModel Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicWordSenseModel` is the public entry point of the Basic package: an `IWordSenseModel` that needs nothing to
prepare and creates the statistical classifier.

#### Data Model

The class is sealed and holds one volatile disposed flag. The statistics live in the process-wide
`BasicTables.Default`, which the classifier loads on first use.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: throws `ObjectDisposedException` after disposal; otherwise ignores the
  mirror and returns a completed task, or a canceled task when the token is already canceled.
- `Create()`: throws `ObjectDisposedException` after disposal; otherwise returns a new `BasicClassifier` that uses the
  embedded statistics and the core lexicon and checks the disposed flag of this model on every call. It may be called
  at any time, with or without `EnsureModelAsync`. The classifier is safe for concurrent use.
- `Dispose()`: sets the flag; idempotent and thread-safe; releases nothing.

#### Error Handling

No exceptions other than the cancellation of `EnsureModelAsync` and `ObjectDisposedException` after `Dispose`. A
classifier created earlier throws `ObjectDisposedException` on every call once the model is disposed. A corrupt
embedded table is reported by the first classification, not by the model.

#### Dependencies

`BasicClassifier` and the core contracts.

#### Callers

Application code and the builder (`evaluate basic`).
