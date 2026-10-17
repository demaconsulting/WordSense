### BasicWordSenseModel Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicWordSenseModel` is the public entry point of the Basic package: an `IWordSenseModel` that needs nothing to
prepare and creates the rule-based classifier.

#### Data Model

The class is sealed and holds no state.

#### Key Methods

- `EnsureModelAsync(mirror, cancellationToken)`: ignores the mirror; returns a completed task, or a canceled task when
  the token is already canceled.
- `Create()`: returns a new `BasicClassifier`. It may be called at any time, with or without `EnsureModelAsync`.

#### Error Handling

No exceptions other than the cancellation of `EnsureModelAsync`. The model is not `IDisposable` because it owns no
resources.

#### Dependencies

`BasicClassifier` and the core contracts.

#### Callers

Application code and the builder (`evaluate basic`).
