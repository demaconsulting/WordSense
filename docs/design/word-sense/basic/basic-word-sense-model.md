### BasicWordSenseModel Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicWordSenseModel` is the public entry point of the Basic package: an `IWordSenseModel` that needs nothing to
prepare and creates the statistical classifier.

#### Data Model

The class is sealed and holds no state. The statistics live in the process-wide `BasicTables.Default`, which the
classifier loads on first use.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: ignores the mirror; returns a completed task, or a canceled task when
  the token is already canceled.
- `Create()`: returns a new `BasicClassifier` that uses the embedded statistics and the core lexicon. It may be called
  at any time, with or without `EnsureModelAsync`. The classifier is safe for concurrent use.

#### Error Handling

No exceptions other than the cancellation of `EnsureModelAsync`. The model is not `IDisposable` because it owns no
resources. A corrupt embedded table is reported by the first classification, not by the model.

#### Dependencies

`BasicClassifier` and the core contracts.

#### Callers

Application code and the builder (`evaluate basic`).
